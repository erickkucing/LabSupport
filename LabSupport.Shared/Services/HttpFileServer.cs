using System;
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
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action<HeartbeatMessage>? OnHeartbeatReceived;
    public Func<byte[]?>? PresentationFrameProvider { get; set; }

    public bool IsRunning => _listener?.IsListening ?? false;
    public int Port => _port;

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
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        try
        {
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Fallback to localhost if wildcard requires admin elevation
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
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
                    response.StatusCode = 200;
                    byte[] okBytes = Encoding.UTF8.GetBytes("{\"status\":\"accepted\"}");
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

                        await Task.Delay(150, token);
                    }
                }
                catch { }
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
    }
}
