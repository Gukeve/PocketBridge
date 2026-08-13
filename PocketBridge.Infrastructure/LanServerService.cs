using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class LanServerService(IAuditLogService audit) : ILanServerService
{
    private readonly HttpListener _listener = new(); private readonly ConcurrentDictionary<string, FrameCopy> _frames = new(StringComparer.Ordinal); private CancellationTokenSource? _lifetime; private Task? _worker; private string _token = string.Empty;
    public bool IsRunning => _listener.IsListening;
    public Uri? Address { get; private set; }
    public Task StartAsync(LanServerOptions options, string token)
    {
        if (!options.Enabled) throw new InvalidOperationException("LAN access is disabled.");
        if (options.BindAddress is "0.0.0.0" or "*" or "+") throw new InvalidOperationException("Wildcard binding is prohibited. Select localhost or a private LAN address explicitly.");
        if (!IPAddress.TryParse(options.BindAddress, out var address) || (!IPAddress.IsLoopback(address) && !IsPrivate(address))) throw new InvalidOperationException("Only localhost or a private LAN address may be used.");
        _token = token; Address = new Uri($"http://{options.BindAddress}:{options.Port}/"); _listener.Prefixes.Clear(); _listener.Prefixes.Add(Address.ToString()); _listener.Start(); _lifetime = new(); _worker = Task.Run(() => ListenAsync(_lifetime.Token)); audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Enable LAN view", "Success")); return Task.CompletedTask;
    }
    public void PublishFrame(string serialAlias, VideoFrame frame) { var bytes = new byte[frame.BufferLength]; Buffer.BlockCopy(frame.Buffer, 0, bytes, 0, bytes.Length); _frames[serialAlias] = new(frame.Width, frame.Height, frame.Stride, bytes); }
    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context; try { context = await _listener.GetContextAsync().WaitAsync(token); } catch (OperationCanceledException) { break; } catch (HttpListenerException) { break; }
            _ = Task.Run(() => HandleAsync(context, token), token);
        }
    }
    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' ws: wss:; style-src 'unsafe-inline'"; context.Response.Headers["Cache-Control"] = "no-store";
        if (context.Request.Url?.AbsolutePath == "/") { await ReplyAsync(context, WebClient, "text/html; charset=utf-8"); return; }
        if (!Authenticate(context.Request)) { audit.Append(new(DateTimeOffset.Now, "Remote session", "Shared sessions", "Authenticate", "Denied")); context.Response.StatusCode = 401; context.Response.Close(); return; }
        if (context.Request.Url?.AbsolutePath == "/api/sessions") { await ReplyAsync(context, JsonSerializer.Serialize(_frames.Select(item => new { alias = item.Key, width = item.Value.Width, height = item.Value.Height })), "application/json"); return; }
        if (context.Request.Url?.AbsolutePath == "/ws/view" && context.Request.IsWebSocketRequest) { var socket = (await context.AcceptWebSocketAsync(null)).WebSocket; await StreamAsync(socket, token); return; }
        context.Response.StatusCode = 404; context.Response.Close();
    }
    private bool Authenticate(HttpListenerRequest request) => string.Equals(request.QueryString["token"] ?? request.Headers["Authorization"]?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), _token, StringComparison.Ordinal);
    private async Task StreamAsync(WebSocket socket, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            foreach (var pair in _frames)
            {
                var header = JsonSerializer.SerializeToUtf8Bytes(new { alias = pair.Key, pair.Value.Width, pair.Value.Height, pair.Value.Stride, length = pair.Value.Bytes.Length });
                await socket.SendAsync(header, WebSocketMessageType.Text, true, token); await socket.SendAsync(pair.Value.Bytes, WebSocketMessageType.Binary, true, token);
            }
            await Task.Delay(100, token);
        }
    }
    private static async Task ReplyAsync(HttpListenerContext context, string body, string contentType) { var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close(); }
    public async Task StopAsync() { _lifetime?.Cancel(); _listener.Stop(); if (_worker is not null) try { await _worker; } catch (OperationCanceledException) { } _lifetime?.Dispose(); _lifetime = null; audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Disable LAN view", "Success")); }
    public async ValueTask DisposeAsync() { await StopAsync(); _listener.Close(); }
    private static bool IsPrivate(IPAddress address) { var bytes = address.GetAddressBytes(); return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31); }
    private sealed record FrameCopy(int Width, int Height, int Stride, byte[] Bytes);
    private const string WebClient = """<!doctype html><html><head><meta name="viewport" content="width=device-width"><title>PocketBridge LAN</title><style>body{margin:0;background:#020617;color:#f8fafc;font:15px Segoe UI,sans-serif}main{max-width:1100px;margin:auto;padding:24px}canvas{max-width:100%;background:#0c111b;border:1px solid #334155;border-radius:12px}small{color:#aab4c5}</style></head><body><main><h1>PocketBridge LAN</h1><small>View-only session. Token expires and may be revoked.</small><h2 id="name">Waiting for a shared session…</h2><canvas id="screen"></canvas></main><script>const token=new URLSearchParams(location.search).get('token');const ws=new WebSocket(`ws://${location.host}/ws/view?token=${encodeURIComponent(token)}`);ws.binaryType='arraybuffer';let meta;ws.onmessage=e=>{if(typeof e.data==='string'){meta=JSON.parse(e.data);return}if(!meta)return;name.textContent=meta.alias;screen.width=meta.width;screen.height=meta.height;const src=new Uint8ClampedArray(e.data),rgba=new Uint8ClampedArray(meta.width*meta.height*4);for(let y=0;y<meta.height;y++)for(let x=0;x<meta.width;x++){let s=y*meta.stride+x*4,d=(y*meta.width+x)*4;rgba[d]=src[s+2];rgba[d+1]=src[s+1];rgba[d+2]=src[s];rgba[d+3]=255}screen.getContext('2d').putImageData(new ImageData(rgba,meta.width,meta.height),0,0)};</script></body></html>""";
}
