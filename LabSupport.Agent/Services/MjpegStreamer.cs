using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public class MjpegStreamer : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public bool IsRunning => _listener?.IsListening ?? false;

    public MjpegStreamer(ScreenCaptureService captureService, int port = 5901)
    {
        _captureService = captureService;
        _port = port;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // If prefix http://*:port fails due to ACLs, fall back to localhost / IP
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
        }

        _serverTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, token), token);
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

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
    {
        string path = context.Request.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

        if (path == "/screenshot")
        {
            byte[] jpeg = _captureService.CaptureScreenJpeg(75);
            context.Response.ContentType = "image/jpeg";
            context.Response.ContentLength64 = jpeg.Length;
            await context.Response.OutputStream.WriteAsync(jpeg, 0, jpeg.Length, token);
            context.Response.Close();
        }
        else if (path == "/stream")
        {
            context.Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            context.Response.StatusCode = 200;

            var stream = context.Response.OutputStream;

            try
            {
                while (!token.IsCancellationRequested && stream.CanWrite)
                {
                    byte[] frame = _captureService.CaptureScreenJpeg(70);

                    string header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                    byte[] footerBytes = Encoding.ASCII.GetBytes("\r\n");

                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token);
                    await stream.WriteAsync(frame, 0, frame.Length, token);
                    await stream.WriteAsync(footerBytes, 0, footerBytes.Length, token);
                    await stream.FlushAsync(token);

                    await Task.Delay(100, token); // ~10 FPS
                }
            }
            catch
            {
                // Client disconnected
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
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
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
