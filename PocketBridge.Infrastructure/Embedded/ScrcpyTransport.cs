// The scrcpy server launch and socket handshake are adapted from scrcpy 4.1.
// Copyright (C) 2018 Genymobile
// Copyright (C) 2018-2026 Romain Vimont
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure.Embedded;

internal sealed class ScrcpyTransport : IAsyncDisposable
{
    private readonly AndroidDevice _device;
    private readonly ScrcpyLaunchOptions _options;
    private readonly IAdbService _adb;
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _server;
    private TcpClient? _videoClient;
    private TcpClient? _controlClient;
    private int _forwardPort;
    private int _disposed;
    private readonly ConcurrentQueue<string> _serverLog = new();

    public ScrcpyTransport(AndroidDevice device, ScrcpyLaunchOptions options, IAdbService adb, IExecutableLocator locator, IAppSettingsService settings)
    {
        _device = device;
        _options = options;
        _adb = adb;
        _locator = locator;
        _settings = settings;
    }

    public NetworkStream VideoStream => _videoClient?.GetStream() ?? throw new InvalidOperationException("Video socket is not connected.");
    public NetworkStream ControlStream => _controlClient?.GetStream() ?? throw new InvalidOperationException("Control socket is not connected.");
    public string DeviceName { get; private set; } = string.Empty;
    public string ServerLog => string.Join(Environment.NewLine, _serverLog);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _settings.Load();
        var adbPath = _locator.Find("adb.exe", config.ToolsDirectory) ?? throw new FileNotFoundException("PocketBridge runtime does not contain adb.exe.");
        var serverPath = _locator.Find("scrcpy-server", config.ToolsDirectory) ?? throw new FileNotFoundException("PocketBridge runtime does not contain scrcpy-server.");

        var push = await _adb.ExecuteAsync(_device.Serial, "push", serverPath, "/data/local/tmp/scrcpy-server.jar").ConfigureAwait(false);
        if (!push.IsSuccess) throw new InvalidOperationException($"Could not deploy scrcpy-server: {push.StandardError.Trim()}");

        _forwardPort = GetFreeTcpPort();
        var scid = Random.Shared.Next(1, int.MaxValue);
        var socketName = $"scrcpy_{scid:x8}";
        var forward = await _adb.ExecuteAsync(_device.Serial, "forward", $"tcp:{_forwardPort}", $"localabstract:{socketName}").ConfigureAwait(false);
        if (!forward.IsSuccess) throw new InvalidOperationException($"Could not create ADB tunnel: {forward.StandardError.Trim()}");

        _server = StartServer(adbPath, scid);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            _videoClient = await ConnectVideoWithHandshakeAsync(_forwardPort, linked.Token).ConfigureAwait(false);
            _controlClient = await ConnectWithRetryAsync(_forwardPort, linked.Token).ConfigureAwait(false);
            var deviceMeta = new byte[64];
            await ReadExactlyAsync(VideoStream, deviceMeta, linked.Token).ConfigureAwait(false);
            var terminator = Array.IndexOf(deviceMeta, (byte)0);
            DeviceName = Encoding.UTF8.GetString(deviceMeta, 0, terminator < 0 ? deviceMeta.Length : terminator);
            await _adb.ExecuteAsync(_device.Serial, "forward", "--remove", $"tcp:{_forwardPort}").ConfigureAwait(false);
            _forwardPort = 0;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Process StartServer(string adbPath, int scid)
    {
        var info = new ProcessStartInfo
        {
            FileName = adbPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var serverArguments = new List<string>
        {
            "-s", _device.Serial, "shell", "CLASSPATH=/data/local/tmp/scrcpy-server.jar", "app_process", "/",
            "com.genymobile.scrcpy.Server", ScrcpyProtocolV41.Version, $"scid={scid:x8}", "tunnel_forward=true",
            "audio=false", "control=true", "video=true", "video_codec=h264",
            "send_device_meta=true", "send_stream_meta=true", "send_frame_meta=true",
            $"stay_awake={_options.StayAwake.ToString().ToLowerInvariant()}",
            $"clipboard_autosync={(_options.ClipboardMode == ClipboardSyncMode.Automatic).ToString().ToLowerInvariant()}",
            "power_off_on_close=false"
        };
        if (_options.MaxSize is > 0) serverArguments.Add($"max_size={_options.MaxSize}");
        if (_options.MaxFps is > 0) serverArguments.Add($"max_fps={_options.MaxFps}");
        if (!string.IsNullOrWhiteSpace(_options.VideoBitRate))
        {
            var normalized = _options.VideoBitRate.Trim().ToUpperInvariant();
            if (normalized.EndsWith('M') && int.TryParse(normalized[..^1], out var mbps)) serverArguments.Add($"video_bit_rate={mbps * 1_000_000}");
        }
        foreach (var argument in serverArguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("Could not start scrcpy-server through ADB.");
        _ = DrainAsync(process.StandardOutput, _serverLog);
        _ = DrainAsync(process.StandardError, _serverLog);
        return process;
    }

    private static async Task DrainAsync(StreamReader reader, ConcurrentQueue<string> log)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                log.Enqueue(line);
                while (log.Count > 40) log.TryDequeue(out _);
            }
        }
        catch (ObjectDisposedException) { }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<TcpClient> ConnectWithRetryAsync(int port, CancellationToken cancellationToken)
    {
        Exception? last = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                last = exception;
                client.Dispose();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException("Timed out waiting for scrcpy-server socket.", last);
    }

    private static async Task<TcpClient> ConnectVideoWithHandshakeAsync(int port, CancellationToken cancellationToken)
    {
        Exception? last = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                var dummy = new byte[1];
                await ReadExactlyAsync(client.GetStream(), dummy, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception exception) when (exception is SocketException or IOException or EndOfStreamException)
            {
                last = exception;
                client.Dispose();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException("Timed out waiting for scrcpy-server handshake.", last);
    }

    internal static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("scrcpy-server closed the video socket.");
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _videoClient?.Dispose();
        _controlClient?.Dispose();
        if (_server is { HasExited: false })
        {
            try { _server.Kill(true); await _server.WaitForExitAsync().ConfigureAwait(false); } catch (InvalidOperationException) { }
        }
        _server?.Dispose();
        if (_forwardPort != 0)
        {
            try { await _adb.ExecuteAsync(_device.Serial, "forward", "--remove", $"tcp:{_forwardPort}").ConfigureAwait(false); } catch { }
            _forwardPort = 0;
        }
        _lifetime.Dispose();
    }
}
