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
}
