using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class LanServerService : ILanServerService
{
    private readonly IAuditLogService _audit;
    private readonly IRemoteSessionService _sessions;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, FrameCopy> _frames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, WebSocket> _controlSockets = new();
    private readonly ConcurrentDictionary<Guid, long> _sequences = new();
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private string _allowedOrigin = string.Empty;

    public LanServerService(IAuditLogService audit, IRemoteSessionService sessions)
    {
        _audit = audit; _sessions = sessions; _sessions.Revoked += OnRevoked;
    }

    public event EventHandler<RemoteControlRequest>? ControlRequested;
    public bool IsRunning => _listener.IsListening;
    public Uri? Address { get; private set; }

    public Task StartAsync(LanServerOptions options, string token)
    {
        if (!options.Enabled) throw new InvalidOperationException("LAN access is disabled.");
        if (options.BindAddress is "0.0.0.0" or "*" or "+") throw new InvalidOperationException("Wildcard binding is prohibited.");
        if (!IPAddress.TryParse(options.BindAddress, out var address) || (!IPAddress.IsLoopback(address) && !IsPrivate(address))) throw new InvalidOperationException("Only localhost or a private LAN address may be used.");
        if (_sessions.Authenticate(token, DateTimeOffset.UtcNow) is null) throw new UnauthorizedAccessException("The remote session is not active.");
        Address = new Uri($"http://{options.BindAddress}:{options.Port}/");
        _allowedOrigin = (options.AllowedOrigin ?? Address.GetLeftPart(UriPartial.Authority)).TrimEnd('/');
        _listener.Prefixes.Clear(); _listener.Prefixes.Add(Address.ToString()); _listener.Start();
        _lifetime = new(); _worker = Task.Run(() => ListenAsync(_lifetime.Token));
        _audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Enable LAN session", "Success"));
        return Task.CompletedTask;
    }

    public void PublishFrame(string serialAlias, VideoFrame frame)
    {
        var bytes = new byte[frame.BufferLength]; Buffer.BlockCopy(frame.Buffer, 0, bytes, 0, bytes.Length);
        _frames[serialAlias] = new(frame.Width, frame.Height, frame.Stride, bytes);
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(token); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            _ = Task.Run(() => HandleAsync(context, token), token);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        SetSecurityHeaders(context.Response);
        if (context.Request.Url?.AbsolutePath == "/") { await ReplyAsync(context, WebClient, "text/html; charset=utf-8"); return; }
        var session = Authenticate(context.Request);
        if (session is null) { Deny(context, "Authenticate"); return; }
        if (!OriginMatches(context.Request)) { Deny(context, "Validate origin", 403); return; }
        var path = context.Request.Url?.AbsolutePath;
        if (path == "/api/sessions" && Has(session, RemotePermission.ViewScreen))
        {
            await ReplyAsync(context, JsonSerializer.Serialize(_frames.Select(item => new { alias = item.Key, width = item.Value.Width, height = item.Value.Height })), "application/json"); return;
        }
        if (path == "/ws/view" && context.Request.IsWebSocketRequest && Has(session, RemotePermission.ViewScreen))
        {
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket; await StreamAsync(socket, session.Id, token); return;
        }
        if (path == "/ws/control" && context.Request.IsWebSocketRequest && HasAnyControl(session))
        {
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket; _controlSockets[session.Id] = socket;
            await ReceiveControlAsync(socket, session.Id, context.Request.Headers["Origin"]!, token); return;
        }
        Deny(context, "Authorize endpoint", 403);
    }

    private RemoteSession? Authenticate(HttpListenerRequest request)
    {
        var token = request.QueryString["token"] ?? request.Headers["Authorization"]?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(token) ? null : _sessions.Authenticate(token, DateTimeOffset.UtcNow);
    }

    private bool OriginMatches(HttpListenerRequest request) => string.Equals(request.Headers["Origin"]?.TrimEnd('/'), _allowedOrigin, StringComparison.OrdinalIgnoreCase);

    private async Task ReceiveControlAsync(WebSocket socket, Guid sessionId, string origin, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (!result.EndOfMessage || result.MessageType != WebSocketMessageType.Text) { await ClosePolicyAsync(socket, "One JSON message is required.", token); break; }
                RemoteWireMessage? message;
                try { message = JsonSerializer.Deserialize<RemoteWireMessage>(buffer.AsSpan(0, result.Count), JsonOptions); }
                catch (JsonException) { await ClosePolicyAsync(socket, "Invalid JSON.", token); break; }
                var active = _sessions.IsActive(sessionId, DateTimeOffset.UtcNow) ? _sessions.Authenticate(message?.Token ?? string.Empty, DateTimeOffset.UtcNow) : null;
                if (active is null || active.Id != sessionId || !string.Equals(origin.TrimEnd('/'), _allowedOrigin, StringComparison.OrdinalIgnoreCase) || message is null || !IsAuthorized(active, message.Action) || !AcceptSequence(sessionId, message.Sequence))
                { _audit.Append(new(DateTimeOffset.Now, active?.Actor ?? "Remote session", message?.TargetAlias ?? "Unknown", "Remote control message", "Denied")); await ClosePolicyAsync(socket, "Authorization, origin, permission, expiration or sequence check failed.", token); break; }
                ControlRequested?.Invoke(this, new(sessionId, message.TargetAlias, message.Action, message.X, message.Y, message.KeyCode, message.Sequence));
                _audit.Append(new(DateTimeOffset.Now, active.Actor, message.TargetAlias, $"Remote {message.Action}", "Accepted"));
            }
        }
        finally { _controlSockets.TryRemove(sessionId, out _); _sequences.TryRemove(sessionId, out _); socket.Dispose(); }
    }

    private async Task StreamAsync(WebSocket socket, Guid sessionId, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested && _sessions.IsActive(sessionId, DateTimeOffset.UtcNow))
        {
            foreach (var pair in _frames)
            {
                var header = JsonSerializer.SerializeToUtf8Bytes(new { alias = pair.Key, pair.Value.Width, pair.Value.Height, pair.Value.Stride, length = pair.Value.Bytes.Length });
                await socket.SendAsync(header, WebSocketMessageType.Text, true, token); await socket.SendAsync(pair.Value.Bytes, WebSocketMessageType.Binary, true, token);
            }
            await Task.Delay(100, token);
        }
        socket.Dispose();
    }

    private void OnRevoked(object? sender, Guid id)
    {
        _sequences.TryRemove(id, out _);
        if (_controlSockets.TryRemove(id, out var socket)) socket.Abort();
    }

    private static bool IsAuthorized(RemoteSession session, string action) => RequiredPermission(action) is { } required && Has(session, required);
    private bool AcceptSequence(Guid sessionId, long sequence)
    {
        if (sequence <= 0) return false;
        while (true)
        {
            if (!_sequences.TryGetValue(sessionId, out var previous)) return _sequences.TryAdd(sessionId, sequence);
            if (sequence <= previous) return false;
            if (_sequences.TryUpdate(sessionId, sequence, previous)) return true;
        }
    }
    private static RemotePermission? RequiredPermission(string action) => action.ToLowerInvariant() switch { "tap" or "touchdown" or "touchup" => RemotePermission.ControlTouch, "key" => RemotePermission.KeyboardInput, "back" or "home" or "recents" => RemotePermission.DeviceButtons, _ => null };
    private static bool Has(RemoteSession session, RemotePermission permission) => (session.Permissions & permission) == permission;
    private static bool HasAnyControl(RemoteSession session) => (session.Permissions & (RemotePermission.ControlTouch | RemotePermission.KeyboardInput | RemotePermission.DeviceButtons)) != 0;
    private void Deny(HttpListenerContext context, string action, int status = 401) { _audit.Append(new(DateTimeOffset.Now, "Remote session", "Shared sessions", action, "Denied")); context.Response.StatusCode = status; context.Response.Close(); }
    private static void SetSecurityHeaders(HttpListenerResponse response) { response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' ws: wss:; style-src 'unsafe-inline'"; response.Headers["Cache-Control"] = "no-store"; }
    private static async Task ClosePolicyAsync(WebSocket socket, string reason, CancellationToken token) { if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, token); }
    private static async Task ReplyAsync(HttpListenerContext context, string body, string contentType) { var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close(); }
    public async Task StopAsync() { _lifetime?.Cancel(); if (_listener.IsListening) _listener.Stop(); foreach (var socket in _controlSockets.Values) socket.Abort(); _controlSockets.Clear(); _sequences.Clear(); if (_worker is not null) try { await _worker; } catch (OperationCanceledException) { } _lifetime?.Dispose(); _lifetime = null; _audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Disable LAN session", "Success")); }
    public async ValueTask DisposeAsync() { _sessions.Revoked -= OnRevoked; await StopAsync(); _listener.Close(); }
    private static bool IsPrivate(IPAddress address) { var bytes = address.GetAddressBytes(); return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31); }
    private sealed record FrameCopy(int Width, int Height, int Stride, byte[] Bytes);
    private sealed record RemoteWireMessage(string Token, string TargetAlias, string Action, double X, double Y, int KeyCode, long Sequence);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string WebClient = """<!doctype html><html><head><meta name="viewport" content="width=device-width"><title>PocketBridge LAN</title><style>body{margin:0;background:#020617;color:#f8fafc;font:15px Segoe UI,sans-serif}main{max-width:1100px;margin:auto;padding:24px}canvas{max-width:100%;background:#0c111b;border:1px solid #334155;border-radius:12px}small{color:#aab4c5}</style></head><body><main><h1>PocketBridge LAN</h1><small>Permission-scoped session. It expires and can be revoked immediately.</small><h2 id="name">Waiting for a shared session...</h2><canvas id="screen"></canvas></main><script>const token=new URLSearchParams(location.search).get('token'),origin=location.origin;const ws=new WebSocket(`${location.protocol==='https:'?'wss':'ws'}://${location.host}/ws/view?token=${encodeURIComponent(token)}`);ws.binaryType='arraybuffer';let meta;ws.onmessage=e=>{if(typeof e.data==='string'){meta=JSON.parse(e.data);return}if(!meta)return;name.textContent=meta.alias;screen.width=meta.width;screen.height=meta.height;const src=new Uint8ClampedArray(e.data),rgba=new Uint8ClampedArray(meta.width*meta.height*4);for(let y=0;y<meta.height;y++)for(let x=0;x<meta.width;x++){let s=y*meta.stride+x*4,d=(y*meta.width+x)*4;rgba[d]=src[s+2];rgba[d+1]=src[s+1];rgba[d+2]=src[s];rgba[d+3]=255}screen.getContext('2d').putImageData(new ImageData(rgba,meta.width,meta.height),0,0)};</script></body></html>""";
}
