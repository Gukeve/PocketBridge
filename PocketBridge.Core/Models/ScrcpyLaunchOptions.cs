namespace PocketBridge.Core.Models;

public sealed record ScrcpyLaunchOptions
{
    public string? WindowTitle { get; init; }
    public bool StayAwake { get; init; } = true;
    public bool AlwaysOnTop { get; init; }
    public bool TurnScreenOff { get; init; }
    public int? MaxSize { get; init; }
    public int? MaxFps { get; init; }
    public string? VideoBitRate { get; init; }
    public PreferredVideoCodec VideoCodec { get; init; } = PreferredVideoCodec.H264;
    public ClipboardSyncMode ClipboardMode { get; init; } = ClipboardSyncMode.Manual;
    public bool AudioEnabled { get; init; } = true;
    public bool FullScreen { get; init; }
}
