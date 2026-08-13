namespace PocketBridge.Core.Models;

public enum GroupAction
{
    Home,
    Back,
    Recents,
    VolumeUp,
    VolumeDown,
    Power,
    Reboot
}

public sealed record GroupActionTarget(string Serial, string Alias);
public sealed record GroupActionResult(string Serial, string Alias, bool Success, string? Error);
