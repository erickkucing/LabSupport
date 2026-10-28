using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public class HttpFileServer : IDisposable
{
    private readonly int _port;
    private readonly string _configuredAgentBinaryPath;
    private readonly ConcurrentDictionary<string, FileTransferSession> _fileSessions = new(StringComparer.OrdinalIgnoreCase);
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action<HeartbeatMessage>? OnHeartbeatReceived;
    public event Action<UnregisterAgentMessage>? OnAgentUnregisterReceived;
    public event Action<string, int>? OnFileChunkTransferred;
    public Func<byte[]?>? PresentationFrameProvider { get; set; }
    public Func<HeartbeatMessage, string?>? HeartbeatDirectiveEvaluator { get; set; }
    public int StreamIntervalMs { get; set; } = 100;

    public void RegisterFileSession(FileTransferSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.Token)) return;
        _fileSessions[session.Token] = session;
    }

    public void UnregisterFileSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (_fileSessions.TryRemove(token, out var session))
        {
            session.Dispose();
        }
    }

    public FileTransferSession? GetFileSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        _fileSessions.TryGetValue(token, out var session);
        return session;
    }

    public bool IsRunning => _listener?.IsListening ?? false;
    public int Port => _port;
    public bool IsBoundToWildcard { get; private set; }
    public string BoundPrefixes { get; private set; } = string.Empty;

    public HttpFileServer(int port = 8080, string? agentBinaryPath = null)
    {
        _port = port;
        _configuredAgentBinaryPath = agentBinaryPath ?? Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe");
    }

    public string? ResolveAgentBinaryPath()
    {
        var candidates = new List<string?>
        {
            _configuredAgentBinaryPath,
            Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "agent", "LabSupport.Agent.exe")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                string fileName = Path.GetFileName(fullPath);

                bool isExplicitlyConfigured = !string.IsNullOrEmpty(_configuredAgentBinaryPath) &&
                    string.Equals(fullPath, Path.GetFullPath(_configuredAgentBinaryPath), StringComparison.OrdinalIgnoreCase);

                if (isExplicitlyConfigured)
                {
                    if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.Equals("LabSupport.Agent", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!fileName.Equals("LabSupport.Agent.exe", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.Equals("LabSupport.Agent", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch { }
        }

        return null;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        bool started = false;
        try
        {
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
            IsBoundToWildcard = true;
            BoundPrefixes = $"http://*:{_port}/";
            started = true;
        }
        catch (HttpListenerException)
        {
            IsBoundToWildcard = false;
            try { _listener.Close(); } catch { }
            _listener = new HttpListener();
        }

        if (!started)
        {
            var lanIps = NetworkHelper.GetLocalIPv4Addresses();
            var addedPrefixes = new List<string>();

            foreach (var ip in lanIps)
            {
                if (ip != "127.0.0.1")
                {
                    try
                    {
                        string prefix = $"http://{ip}:{_port}/";
                        _listener.Prefixes.Add(prefix);
                        addedPrefixes.Add(prefix);
                    }
                    catch { }
                }
            }

            try
            {
                string loopbackPrefix = $"http://127.0.0.1:{_port}/";
                _listener.Prefixes.Add(loopbackPrefix);
                addedPrefixes.Add(loopbackPrefix);
            }
            catch { }

            try
            {
                _listener.Start();
                BoundPrefixes = string.Join(", ", addedPrefixes);
                started = true;
            }
            catch
            {
                try
                {
                    _listener.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                    BoundPrefixes = $"http://127.0.0.1:{_port}/";
                }
                catch { }
            }
        }

        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => ProcessContextAsync(context, token), token);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Continue listening
            }
        }
    }

    private async Task ProcessContextAsync(HttpListenerContext context, CancellationToken token)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string rawUrl = request.RawUrl ?? string.Empty;
            string decodedUrl = WebUtility.UrlDecode(rawUrl);

            // Iterative URL decode to catch nested or double-encoding (e.g. %252e%252e or %255c)
            string fullyDecoded = decodedUrl;
            for (int i = 0; i < 5; i++)
            {
                string next = WebUtility.UrlDecode(fullyDecoded);
                if (next == fullyDecoded) break;
                fullyDecoded = next;
            }

            // Defensive: Detect path traversal tokens in raw, single-decoded, and fully-decoded URI
            if (rawUrl.Contains("..") || decodedUrl.Contains("..") || fullyDecoded.Contains("..") ||
                rawUrl.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
                decodedUrl.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
                fullyDecoded.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
                decodedUrl.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
                fullyDecoded.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.Contains('\\') || decodedUrl.Contains('\\') || fullyDecoded.Contains('\\'))
            {
                response.StatusCode = 400;
                byte[] badReq = Encoding.UTF8.GetBytes("Bad Request: Directory traversal is prohibited.");
                response.ContentType = "text/plain";
                response.ContentLength64 = badReq.Length;
                await response.OutputStream.WriteAsync(badReq, 0, badReq.Length, token);
                return;
            }

            string path = request.Url?.AbsolutePath.ToLowerInvariant() ?? "/";
            string method = request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && path == "/health")
            {
                byte[] buffer = Encoding.UTF8.GetBytes("OK");
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, token);
            }
            else if (method == "GET" && path == "/agent/download")
            {
                if (request.QueryString.Count > 0 &&
                    (!string.IsNullOrEmpty(request.QueryString["file"]) ||
                     !string.IsNullOrEmpty(request.QueryString["path"]) ||
                     !string.IsNullOrEmpty(request.QueryString["download"])))
                {
                    response.StatusCode = 400;
                    byte[] forbiddenMsg = Encoding.UTF8.GetBytes("Bad Request: Custom file query parameter is forbidden.");
                    response.ContentType = "text/plain";
                    response.ContentLength64 = forbiddenMsg.Length;
                    await response.OutputStream.WriteAsync(forbiddenMsg, 0, forbiddenMsg.Length, token);
                    return;
                }

                string? resolvedPath = ResolveAgentBinaryPath();
                if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(resolvedPath, token);
                    response.StatusCode = 200;
                    response.ContentType = "application/octet-stream";
                    response.AddHeader("Content-Disposition", "attachment; filename=\"LabSupport.Agent.exe\"");
                    response.ContentLength64 = fileBytes.Length;
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length, token);
                }
                else
                {
                    byte[] notFound = Encoding.UTF8.GetBytes("LabSupport.Agent.exe binary not found on Manager server.");
                    response.StatusCode = 404;
                    response.ContentType = "text/plain";
                    await response.OutputStream.WriteAsync(notFound, 0, notFound.Length, token);
                }
            }
            else if (method == "POST" && path == "/agent/heartbeat")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                string json = await reader.ReadToEndAsync(token);

                var heartbeat = MessageSerializer.Deserialize<HeartbeatMessage>(json);
                if (heartbeat != null)
                {
                    OnHeartbeatReceived?.Invoke(heartbeat);
                    string? directive = HeartbeatDirectiveEvaluator?.Invoke(heartbeat);

                    var respPayload = new HeartbeatResponsePayload
                    {
                        Status = "accepted",
                        Directive = directive
                    };

                    response.StatusCode = 200;
                    byte[] okBytes = Encoding.UTF8.GetBytes(MessageSerializer.Serialize(respPayload));
                    response.ContentType = "application/json";
                    await response.OutputStream.WriteAsync(okBytes, 0, okBytes.Length, token);
                }
                else
                {
                    response.StatusCode = 400;
                }
            }
            else if (method == "POST" && path == "/agent/unregister")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                string json = await reader.ReadToEndAsync(token);

                var unregisterMsg = MessageSerializer.Deserialize<UnregisterAgentMessage>(json);
                if (unregisterMsg != null)
                {
                    OnAgentUnregisterReceived?.Invoke(unregisterMsg);
                    response.StatusCode = 200;
                    byte[] okBytes = Encoding.UTF8.GetBytes("{\"status\":\"unregistered\"}");
                    response.ContentType = "application/json";
                    await response.OutputStream.WriteAsync(okBytes, 0, okBytes.Length, token);
                }
                else
                {
                    response.StatusCode = 400;
                }
            }
            else if (method == "GET" && path == "/presentation/view")
            {
                string html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <title>LabSupport Presentation</title>
    <style>
        body, html { margin: 0; padding: 0; width: 100%; height: 100%; background: #000; overflow: hidden; }
        img { width: 100%; height: 100%; object-fit: contain; }
    </style>
</head>
<body>
    <img src='/presentation/stream' alt='Live Presentation'/>
</body>
</html>";
                byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                response.StatusCode = 200;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = htmlBytes.Length;
                await response.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length, token);
            }
            else if (method == "GET" && path == "/presentation/stream")
            {
                response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
                response.StatusCode = 200;
                var stream = response.OutputStream;

                try
                {
                    while (!token.IsCancellationRequested && stream.CanWrite)
                    {
                        byte[]? frame = PresentationFrameProvider?.Invoke();
                        if (frame != null && frame.Length > 0)
                        {
                            string header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                            byte[] footerBytes = Encoding.ASCII.GetBytes("\r\n");

                            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token);
                            await stream.WriteAsync(frame, 0, frame.Length, token);
                            await stream.WriteAsync(footerBytes, 0, footerBytes.Length, token);
                            await stream.FlushAsync(token);
                        }

                        await Task.Delay(StreamIntervalMs, token);
                    }
                }
                catch { }
            }
            else if (method == "GET" && path.StartsWith("/files/download/"))
            {
                string tokenParam = path.Substring("/files/download/".Length).Trim('/');
                if (string.IsNullOrWhiteSpace(tokenParam) || tokenParam.Contains('/') || tokenParam.Contains('\\') || tokenParam.Contains('.'))
                {
                    response.StatusCode = 400;
                    byte[] errBytes = Encoding.UTF8.GetBytes("Bad Request: Invalid download token.");
                    response.ContentType = "text/plain";
                    response.ContentLength64 = errBytes.Length;
                    await response.OutputStream.WriteAsync(errBytes, 0, errBytes.Length, token);
                    return;
                }

                var session = GetFileSession(tokenParam);
                if (session == null || !File.Exists(session.FilePath))
                {
                    response.StatusCode = 404;
                    byte[] notFoundBytes = Encoding.UTF8.GetBytes("Download session not found or expired.");
                    response.ContentType = "text/plain";
                    response.ContentLength64 = notFoundBytes.Length;
                    await response.OutputStream.WriteAsync(notFoundBytes, 0, notFoundBytes.Length, token);
                    return;
                }

                try
                {
                    response.StatusCode = 200;
                    response.ContentType = session.ContentType;
                    response.ContentLength64 = session.FileSize;
                    response.AddHeader("Content-Disposition", $"attachment; filename=\"{WebUtility.UrlEncode(session.FileName)}\"");
                    if (!string.IsNullOrWhiteSpace(session.Sha256Hash))
                    {
                        response.AddHeader("X-Checksum-SHA256", session.Sha256Hash);
                    }

                    using var fs = new FileStream(session.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
                    byte[] buffer = new byte[64 * 1024];
                    int bytesRead;
                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        await response.OutputStream.WriteAsync(buffer, 0, bytesRead, token);
                        session.AddBytesTransferred(bytesRead);
                        OnFileChunkTransferred?.Invoke(session.Token, bytesRead);
                    }
                    await response.OutputStream.FlushAsync(token);
                }
                catch (HttpListenerException) { }
                catch (OperationCanceledException) { }
                catch (Exception) { /* Stream write failed, e.g. client disconnected midway */ }
            }
            else
            {
                response.StatusCode = 404;
            }
        }
        catch
        {
            response.StatusCode = 500;
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        foreach (var session in _fileSessions.Values)
        {
            session.Dispose();
        }
        _fileSessions.Clear();
    }
}
