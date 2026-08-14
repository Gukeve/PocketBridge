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
    private readonly ConcurrentDictionary<Guid, WebSocket> _viewSockets = new();
    private RemoteControlSecurityGate? _securityGate;
    private const int MaximumClients = 8;
    private const int MaximumMessageBytes = 4096;
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
        _securityGate = new(_allowedOrigin);
        _listener.Prefixes.Clear(); _listener.Prefixes.Add(Address.ToString()); _listener.Start();
        _lifetime = new(); _worker = Task.Run(() => ListenAsync(_lifetime.Token));
        _audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Enable LAN session", "Success"));
        return Task.CompletedTask;
    }

    public void PublishFrame(string serialAlias, VideoFrame frame)
    {
        var bytes = new byte[frame.BufferLength]; Buffer.BlockCopy(frame.Buffer, 0, bytes, 0, bytes.Length);
        if (_frames.Count < 16 || _frames.ContainsKey(serialAlias)) _frames[serialAlias] = new(frame.Width, frame.Height, frame.Stride, bytes);
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
        if (context.Request.IsWebSocketRequest && !OriginMatches(context.Request)) { Deny(context, "Validate origin", 403); return; }
        _audit.Append(new(DateTimeOffset.Now, session.Actor, "Shared sessions", "Remote authentication", "Success", "Security"));
        var path = context.Request.Url?.AbsolutePath;
        if (path == "/api/sessions" && Has(session, RemotePermission.ViewScreen))
        {
            await ReplyAsync(context, JsonSerializer.Serialize(_frames.Select(item => new { alias = item.Key, width = item.Value.Width, height = item.Value.Height })), "application/json"); return;
        }
        if (path == "/api/capabilities") { await ReplyAsync(context, JsonSerializer.Serialize(new { view = Has(session, RemotePermission.ViewScreen), touch = Has(session, RemotePermission.ControlTouch), keyboard = Has(session, RemotePermission.KeyboardInput), buttons = Has(session, RemotePermission.DeviceButtons) }), "application/json"); return; }
        if (path == "/ws/view" && context.Request.IsWebSocketRequest && Has(session, RemotePermission.ViewScreen))
        {
            if (_viewSockets.Count + _controlSockets.Count >= MaximumClients && !_viewSockets.ContainsKey(session.Id)) { Deny(context, "View client limit", 429); return; }
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket; if (_viewSockets.TryGetValue(session.Id, out var old)) old.Abort(); _viewSockets[session.Id] = socket; await StreamAsync(socket, session.Id, token); return;
        }
        if (path == "/ws/control" && context.Request.IsWebSocketRequest && HasAnyControl(session))
        {
            if (_controlSockets.Count >= MaximumClients && !_controlSockets.ContainsKey(session.Id)) { Deny(context, "Control client limit", 429); return; }
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket; _controlSockets[session.Id] = socket;
            _audit.Append(new(DateTimeOffset.Now, session.Actor, "Shared sessions", "Control connection opened", "Success"));
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
        var buffer = new byte[MaximumMessageBytes];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(60));
                WebSocketReceiveResult result; try { result = await socket.ReceiveAsync(buffer, idle.Token); } catch (OperationCanceledException) when (!token.IsCancellationRequested) { await ClosePolicyAsync(socket, "Idle timeout.", token); break; }
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (!result.EndOfMessage || result.MessageType != WebSocketMessageType.Text) { await ClosePolicyAsync(socket, "One JSON message is required.", token); break; }
                RemoteWireMessage? message;
                try { message = JsonSerializer.Deserialize<RemoteWireMessage>(buffer.AsSpan(0, result.Count), JsonOptions); }
                catch (JsonException) { await ClosePolicyAsync(socket, "Invalid JSON.", token); break; }
                var now = DateTimeOffset.UtcNow; var active = _sessions.IsActive(sessionId, now) ? _sessions.Authenticate(message?.Token ?? string.Empty, now) : null;
                if (message is null || active is null) { _audit.Append(new(DateTimeOffset.Now, "Remote session", "Unknown", "Remote control rejected", "inactive session or malformed message")); await ClosePolicyAsync(socket, "inactive session or malformed message", token); break; }
                var decision = _securityGate!.Validate(active, sessionId, origin, message.Action, message.Sequence, message.Nonce, message.TimestampUnixMs, now);
                if (!decision.Allowed)
                { _audit.Append(new(DateTimeOffset.Now, active?.Actor ?? "Remote session", message?.TargetAlias ?? "Unknown", "Remote control rejected", decision.Reason, "Security", AuditSeverity.Warning)); await ClosePolicyAsync(socket, decision.Reason, token); break; }
                ControlRequested?.Invoke(this, new(sessionId, message.TargetAlias, message.Action, message.X, message.Y, message.KeyCode, message.Sequence));
                _audit.Append(new(DateTimeOffset.Now, active.Actor, message.TargetAlias, $"Remote {message.Action}", "Accepted"));
            }
        }
        finally { _controlSockets.TryRemove(sessionId, out _); _audit.Append(new(DateTimeOffset.Now, "Remote session", "Shared sessions", "Control connection closed", "Success")); socket.Dispose(); }
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
        _viewSockets.TryRemove(sessionId, out _); socket.Dispose();
    }

    private void OnRevoked(object? sender, Guid id)
    {
        _securityGate?.Revoke(id); _audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Remote token revoked", "Success", "Security", AuditSeverity.Warning));
        if (_controlSockets.TryRemove(id, out var socket)) socket.Abort();
        if (_viewSockets.TryRemove(id, out var view)) view.Abort();
    }

    private static bool Has(RemoteSession session, RemotePermission permission) => (session.Permissions & permission) == permission;
    private static bool HasAnyControl(RemoteSession session) => (session.Permissions & (RemotePermission.ControlTouch | RemotePermission.KeyboardInput | RemotePermission.DeviceButtons)) != 0;
    private void Deny(HttpListenerContext context, string action, int status = 401) { _audit.Append(new(DateTimeOffset.Now, "Remote session", "Shared sessions", action, "Denied", "Security", AuditSeverity.Warning)); context.Response.StatusCode = status; context.Response.Close(); }
    private static void SetSecurityHeaders(HttpListenerResponse response) { response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' ws: wss:; style-src 'unsafe-inline'; script-src 'unsafe-inline'"; response.Headers["Cache-Control"] = "no-store"; }
    private static async Task ClosePolicyAsync(WebSocket socket, string reason, CancellationToken token) { if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, token); }
    private static async Task ReplyAsync(HttpListenerContext context, string body, string contentType) { var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close(); }
    public async Task StopAsync() { _lifetime?.Cancel(); if (_listener.IsListening) _listener.Stop(); foreach (var socket in _controlSockets.Values.Concat(_viewSockets.Values)) socket.Abort(); _controlSockets.Clear(); _viewSockets.Clear(); if (_worker is not null) try { await _worker; } catch (OperationCanceledException) { } _lifetime?.Dispose(); _lifetime = null; _audit.Append(new(DateTimeOffset.Now, "Local user", "Shared sessions", "Disable LAN session", "Success")); }
    public async ValueTask DisposeAsync() { _sessions.Revoked -= OnRevoked; await StopAsync(); _listener.Close(); }
    private static bool IsPrivate(IPAddress address) { var bytes = address.GetAddressBytes(); return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31); }
    private sealed record FrameCopy(int Width, int Height, int Stride, byte[] Bytes);
    private sealed record RemoteWireMessage(string Token, string TargetAlias, string Action, double X, double Y, int KeyCode, long Sequence, string Nonce, long TimestampUnixMs);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string WebClient = """<!doctype html><html><head><meta name="viewport" content="width=device-width"><title>PocketBridge LAN</title><style>body{margin:0;background:#020617;color:#f8fafc;font:15px Segoe UI,sans-serif}main{max-width:1100px;margin:auto;padding:24px}canvas{max-width:100%;background:#0c111b;border:1px solid #334155;border-radius:12px}small{color:#aab4c5}.controls{display:none;gap:8px;margin:12px 0}.controls.on{display:flex}button{min-height:44px;padding:0 18px;border:1px solid #475569;border-radius:9px;background:#172033;color:#f8fafc}</style></head><body><main><h1>PocketBridge LAN</h1><small>Permission-scoped local-network session. It expires and can be revoked immediately.</small><h2 id="name">Waiting for a shared session...</h2><div id="controls" class="controls"><button data-action="back">Back</button><button data-action="home">Home</button><button data-action="recents">Recents</button></div><canvas id="screen" tabindex="0"></canvas></main><script>const token=new URLSearchParams(location.search).get('token'),scheme=location.protocol==='https:'?'wss':'ws';let meta,control,sequence=0,caps={};const view=new WebSocket(`${scheme}://${location.host}/ws/view?token=${encodeURIComponent(token)}`);view.binaryType='arraybuffer';view.onmessage=e=>{if(typeof e.data==='string'){meta=JSON.parse(e.data);return}if(!meta)return;name.textContent=meta.alias;screen.width=meta.width;screen.height=meta.height;const src=new Uint8ClampedArray(e.data),rgba=new Uint8ClampedArray(meta.width*meta.height*4);for(let y=0;y<meta.height;y++)for(let x=0;x<meta.width;x++){let s=y*meta.stride+x*4,d=(y*meta.width+x)*4;rgba[d]=src[s+2];rgba[d+1]=src[s+1];rgba[d+2]=src[s];rgba[d+3]=255}screen.getContext('2d').putImageData(new ImageData(rgba,meta.width,meta.height),0,0)};function send(action,data={}){if(!control||control.readyState!==1||!meta)return;control.send(JSON.stringify({token,targetAlias:meta.alias,action,x:0,y:0,keyCode:0,sequence:++sequence,nonce:crypto.randomUUID(),timestampUnixMs:Date.now(),...data}))}fetch(`/api/capabilities?token=${encodeURIComponent(token)}`,{cache:'no-store'}).then(r=>r.json()).then(c=>{caps=c;if(c.touch||c.keyboard||c.buttons){control=new WebSocket(`${scheme}://${location.host}/ws/control?token=${encodeURIComponent(token)}`);control.onopen=()=>controls.classList.add('on')}});screen.addEventListener('click',e=>{if(!caps.touch)return;const r=screen.getBoundingClientRect();send('tap',{x:(e.clientX-r.left)/r.width,y:(e.clientY-r.top)/r.height})});controls.addEventListener('click',e=>{const action=e.target.dataset.action;if(action&&caps.buttons)send(action)});screen.addEventListener('keydown',e=>{if(!caps.keyboard)return;const keys={Enter:66,Backspace:67,ArrowLeft:21,ArrowRight:22,ArrowUp:19,ArrowDown:20,Escape:4};if(keys[e.key]){send('key',{keyCode:keys[e.key]});e.preventDefault()}});</script></body></html>""";
}
