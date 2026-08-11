namespace PocketBridge.Core.Models;

public enum PreferredDeviceConnection
{
    Automatic,
    Usb,
    TcpIp
}

public enum PreferredVideoCodec
{
    H264,
    H265,
    Av1
}

public enum ClipboardSyncMode
{
    Off,
    Manual,
    Automatic
}

public sealed record DeviceProfile
{
    public required string Serial { get; init; }
    public string? FriendlyName { get; init; }
    public PreferredDeviceConnection PreferredConnection { get; init; }
    public string? LastKnownIp { get; init; }
    public int? PreferredResolution { get; init; } = 1280;
    public int? PreferredBitrateMbps { get; init; }
    public int? PreferredFps { get; init; } = 60;
    public PreferredVideoCodec PreferredCodec { get; init; } = PreferredVideoCodec.H264;
    public bool ScreenOffOnConnect { get; init; }
    public bool StayAwake { get; init; } = true;
    public bool AlwaysOnTop { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public ClipboardSyncMode ClipboardMode { get; init; } = ClipboardSyncMode.Manual;
    public DateTimeOffset? LastConnected { get; init; }

    public string DisplayName(string fallback) => string.IsNullOrWhiteSpace(FriendlyName) ? fallback : FriendlyName.Trim();

    public ScrcpyLaunchOptions ToLaunchOptions() => new()
    {
        StayAwake = StayAwake,
        AlwaysOnTop = AlwaysOnTop,
        TurnScreenOff = ScreenOffOnConnect,
        MaxSize = PreferredResolution,
        MaxFps = PreferredFps,
        VideoBitRate = PreferredBitrateMbps is > 0 ? $"{PreferredBitrateMbps}M" : null,
        VideoCodec = PreferredCodec,
        ClipboardMode = ClipboardMode,
        AudioEnabled = AudioEnabled
    };
}
