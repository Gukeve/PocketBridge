namespace PocketBridge.Core.Models;

public sealed record AppSettings
{
    public string? ToolsDirectory { get; init; }
    public string Language { get; init; } = "ru-RU";
    public IReadOnlyList<DeviceProfile> DeviceProfiles { get; init; } = Array.Empty<DeviceProfile>();
    public string? RecordingFolder { get; init; }
    public string RecordingFormat { get; init; } = "mp4";
    public string? ScreenshotFolder { get; init; }
    public string ScreenshotFilenameFormat { get; init; } = "PocketBridge_<device>_yyyy-MM-dd_HH-mm-ss";
    public IReadOnlyList<ShortcutBinding> ShortcutBindings { get; init; } = ShortcutBinding.Defaults;
    public IReadOnlyList<InputProfile> InputProfiles { get; init; } = new[] { InputProfile.Default };
    public string RecordingVideoCodec { get; init; } = "h264";
    public bool RecordingIncludeAudio { get; init; }
    public int? RecordingMaxSize { get; init; }
    public int? RecordingMaxFps { get; init; } = 60;
    public int? RecordingBitrateMbps { get; init; } = 8;
    public IReadOnlyList<AutomationRule> AutomationRules { get; init; } = Array.Empty<AutomationRule>();
    public bool LanEnabled { get; init; }
    public string LanBindAddress { get; init; } = "127.0.0.1";
    public int LanPort { get; init; } = 27183;
    public InputBackendKind InputBackend { get; init; } = InputBackendKind.Automatic;
}
