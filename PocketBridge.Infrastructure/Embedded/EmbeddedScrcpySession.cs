// The stream framing and codec-config packet handling are adapted from scrcpy 4.1.
// Copyright (C) 2018 Genymobile
// Copyright (C) 2018-2026 Romain Vimont
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Buffers;
using System.Text;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure.Embedded;

public sealed class EmbeddedScrcpySession : IEmbeddedDisplaySession
{
    private readonly AndroidDevice _device;
    private readonly ScrcpyLaunchOptions _options;
    private readonly IAdbService _adb;
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private ScrcpyTransport? _transport;
    private Task? _videoTask;
    private Task? _controlTask;
    private string? _failureReason;
    private long _frameSequence;

    public EmbeddedScrcpySession(AndroidDevice device, ScrcpyLaunchOptions options, IAdbService adb, IExecutableLocator locator, IAppSettingsService settings)
    {
        _device = device;
        _options = options;
        _adb = adb;
        _locator = locator;
        _settings = settings;
    }

    public event EventHandler<VideoFrameEventArgs>? FrameReady;
    public event EventHandler? StateChanged;
    public event EventHandler<DeviceClipboardEventArgs>? ClipboardChanged;
    public string Serial => _device.Serial;
    public DeviceDisplayMode Mode => DeviceDisplayMode.Embedded;
    public bool IsAvailable => true;
    public bool IsRunning { get; private set; }
    public string? UnavailableReason => _failureReason;
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public string DeviceName { get; private set; } = string.Empty;
    public int DisplayId => _options.DisplayId;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        _lifetime?.Dispose();
        _failureReason = null;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transport = new ScrcpyTransport(_device, _options, _adb, _locator, _settings);
        try
        {
            await _transport.StartAsync(_lifetime.Token).ConfigureAwait(false);
            DeviceName = _transport.DeviceName;
            IsRunning = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            _videoTask = Task.Run(() => ReadVideoAsync(_transport, _lifetime.Token), CancellationToken.None);
            _controlTask = Task.Run(() => ReadControlAsync(_transport, _lifetime.Token), CancellationToken.None);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReadControlAsync(ScrcpyTransport transport, CancellationToken cancellationToken)
    {
        var type = new byte[1];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ScrcpyTransport.ReadExactlyAsync(transport.ControlStream, type, cancellationToken).ConfigureAwait(false);
                switch (type[0])
                {
                    case 0:
                        var lengthBuffer = new byte[4];
                        await ScrcpyTransport.ReadExactlyAsync(transport.ControlStream, lengthBuffer, cancellationToken).ConfigureAwait(false);
                        var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer));
                        if (length > 262_139) throw new InvalidDataException($"Invalid clipboard payload length {length}.");
                        var textBuffer = new byte[length];
                        await ScrcpyTransport.ReadExactlyAsync(transport.ControlStream, textBuffer, cancellationToken).ConfigureAwait(false);
                        ClipboardChanged?.Invoke(this, new DeviceClipboardEventArgs(Serial, Encoding.UTF8.GetString(textBuffer)));
                        break;
                    case 1:
                        var sequenceBuffer = new byte[8];
                        await ScrcpyTransport.ReadExactlyAsync(transport.ControlStream, sequenceBuffer, cancellationToken).ConfigureAwait(false);
                        ClipboardChanged?.Invoke(this, new DeviceClipboardEventArgs(Serial, string.Empty, unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(sequenceBuffer))));
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported scrcpy device message type {type[0]}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (EndOfStreamException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _failureReason = $"Control receiver failed: {exception.Message}";
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ReadVideoAsync(ScrcpyTransport transport, CancellationToken cancellationToken)
    {
        try
        {
            var codec = new byte[4];
            await ScrcpyTransport.ReadExactlyAsync(transport.VideoStream, codec, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32BigEndian(codec) != ScrcpyProtocolV41.H264CodecId)
                throw new NotSupportedException("scrcpy-server did not negotiate H.264.");
            var sessionMeta = new byte[12];
            await ScrcpyTransport.ReadExactlyAsync(transport.VideoStream, sessionMeta, cancellationToken).ConfigureAwait(false);
            VideoWidth = checked((int)BinaryPrimitives.ReadUInt32BigEndian(sessionMeta.AsSpan(4)));
            VideoHeight = checked((int)BinaryPrimitives.ReadUInt32BigEndian(sessionMeta.AsSpan(8)));
            StateChanged?.Invoke(this, EventArgs.Empty);

            var nativeDirectory = Path.GetDirectoryName(_locator.Find("avcodec-62.dll", _settings.Load().ToolsDirectory))
                ?? throw new FileNotFoundException("PocketBridge runtime does not contain FFmpeg libraries.");
            using var decoder = new FfmpegH264Decoder(nativeDirectory);
            var header = new byte[12];
            byte[]? codecConfig = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                await ScrcpyTransport.ReadExactlyAsync(transport.VideoStream, header, cancellationToken).ConfigureAwait(false);
                var ptsAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header);
                if ((ptsAndFlags & ScrcpyProtocolV41.SessionFlag) != 0)
                {
                    VideoWidth = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
                    VideoHeight = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)));
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)));
                if (length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException($"Invalid scrcpy packet length {length}.");
                var packet = ArrayPool<byte>.Shared.Rent(length);
                byte[]? mergedPacket = null;
                try
                {
                    await ScrcpyTransport.ReadExactlyAsync(transport.VideoStream, packet.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                    if ((ptsAndFlags & ScrcpyProtocolV41.ConfigFlag) != 0)
                    {
                        codecConfig = new byte[length];
                        Array.Copy(packet, codecConfig, length);
                        continue;
                    }

                    ReadOnlyMemory<byte> encodedPacket = packet.AsMemory(0, length);
                    if (codecConfig is not null)
                    {
                        var mergedLength = codecConfig.Length + length;
                        mergedPacket = ArrayPool<byte>.Shared.Rent(mergedLength);
                        codecConfig.CopyTo(mergedPacket, 0);
                        Array.Copy(packet, 0, mergedPacket, codecConfig.Length, length);
                        encodedPacket = mergedPacket.AsMemory(0, mergedLength);
                        codecConfig = null;
                    }
                    var pts = unchecked((long)(ptsAndFlags & ScrcpyProtocolV41.PtsMask));
                    var frameHandler = FrameReady;
                    foreach (var frame in decoder.Decode(encodedPacket, pts, frameHandler is not null))
                    {
                        frame.Sequence = Interlocked.Increment(ref _frameSequence);
                        VideoWidth = frame.Width;
                        VideoHeight = frame.Height;
                        if (frameHandler is null) frame.Dispose();
                        else frameHandler.Invoke(this, new VideoFrameEventArgs(frame));
                    }
                }
                finally
                {
                    if (mergedPacket is not null) ArrayPool<byte>.Shared.Return(mergedPacket);
                    ArrayPool<byte>.Shared.Return(packet);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (EndOfStreamException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await Task.Delay(250).ConfigureAwait(false);
            var serverLog = transport.ServerLog;
            _failureReason = string.IsNullOrWhiteSpace(serverLog) ? exception.Message : $"{exception.Message}{Environment.NewLine}{serverLog}";
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            Interlocked.CompareExchange(ref _transport, null, transport);
            IsRunning = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task SendTouchAsync(AndroidTouchAction action, long pointerId, int x, int y, float pressure = 1, uint buttons = 1, CancellationToken cancellationToken = default) =>
        WriteControlAsync(ScrcpyProtocolV41.Touch(action, pointerId, x, y, VideoWidth, VideoHeight, pressure, buttons), cancellationToken);

    public Task SendKeyAsync(AndroidKeyAction action, int keyCode, int repeat = 0, int metaState = 0, CancellationToken cancellationToken = default) =>
        WriteControlAsync(ScrcpyProtocolV41.Key(action, keyCode, repeat, metaState), cancellationToken);

    public Task SendScrollAsync(int x, int y, float horizontal, float vertical, uint buttons = 0, CancellationToken cancellationToken = default) =>
        WriteControlAsync(ScrcpyProtocolV41.Scroll(x, y, VideoWidth, VideoHeight, horizontal, vertical, buttons), cancellationToken);

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => WriteControlAsync(ScrcpyProtocolV41.Text(text), cancellationToken);
    public Task RequestClipboardAsync(CancellationToken cancellationToken = default) => WriteControlAsync(ScrcpyProtocolV41.GetClipboard(), cancellationToken);
    public Task SendClipboardAsync(string text, long sequence, bool paste = false, CancellationToken cancellationToken = default) => WriteControlAsync(ScrcpyProtocolV41.SetClipboard(text, sequence, paste), cancellationToken);

    private async Task WriteControlAsync(byte[] message, CancellationToken cancellationToken)
    {
        if (!IsRunning || _transport is null) return;
        await _controlGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _transport.ControlStream.WriteAsync(message, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _failureReason = $"Control socket failed: {exception.Message}";
            IsRunning = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _controlGate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        lifetime?.Cancel();
        var transport = Interlocked.Exchange(ref _transport, null);
        if (transport is not null) await transport.DisposeAsync().ConfigureAwait(false);
        if (_videoTask is { } videoTask && Task.CurrentId != videoTask.Id)
        {
            try { await videoTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (TimeoutException) { }
        }
        _videoTask = null;
        if (_controlTask is { } controlTask && Task.CurrentId != controlTask.Id)
        {
            try { await controlTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (TimeoutException) { }
        }
        _controlTask = null;
        lifetime?.Dispose();
        IsRunning = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _controlGate.Dispose();
    }
}
