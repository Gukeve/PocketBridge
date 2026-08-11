using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public enum DeviceDisplayMode
{
    Embedded,
    External
}

public interface IDeviceDisplaySession
{
    string Serial { get; }
    DeviceDisplayMode Mode { get; }
    bool IsAvailable { get; }
    bool IsRunning { get; }
    string? UnavailableReason { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IEmbeddedDisplaySession : IDeviceDisplaySession, IAsyncDisposable
{
    event EventHandler<VideoFrameEventArgs>? FrameReady;
    event EventHandler? StateChanged;
    event EventHandler<DeviceClipboardEventArgs>? ClipboardChanged;
    int VideoWidth { get; }
    int VideoHeight { get; }
    string DeviceName { get; }
    Task SendTouchAsync(AndroidTouchAction action, long pointerId, int x, int y, float pressure = 1f, uint buttons = 1, CancellationToken cancellationToken = default);
    Task SendKeyAsync(AndroidKeyAction action, int keyCode, int repeat = 0, int metaState = 0, CancellationToken cancellationToken = default);
    Task SendScrollAsync(int x, int y, float horizontal, float vertical, uint buttons = 0, CancellationToken cancellationToken = default);
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);
    Task RequestClipboardAsync(CancellationToken cancellationToken = default);
    Task SendClipboardAsync(string text, long sequence, bool paste = false, CancellationToken cancellationToken = default);
}

public sealed class DeviceClipboardEventArgs(string serial, string text, long? sequence = null) : EventArgs
{
    public string Serial { get; } = serial;
    public string Text { get; } = text;
    public long? Sequence { get; } = sequence;
}

public interface IEmbeddedSessionManager : IAsyncDisposable
{
    event EventHandler? SessionsChanged;
    IReadOnlyCollection<IEmbeddedDisplaySession> Sessions { get; }
    IEmbeddedDisplaySession? Get(string serial);
    Task<IEmbeddedDisplaySession> StartAsync(AndroidDevice device, ScrcpyLaunchOptions? options = null, CancellationToken cancellationToken = default);
    Task StopAsync(string serial, CancellationToken cancellationToken = default);
}

public interface IDeviceDisplaySessionFactory
{
    IDeviceDisplaySession CreateExternal(AndroidDevice device, ScrcpyLaunchOptions options);
    IEmbeddedDisplaySession CreateEmbedded(AndroidDevice device, ScrcpyLaunchOptions options);
}
