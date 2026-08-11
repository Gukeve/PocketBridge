namespace PocketBridge.Core.Models;

public enum UpdateComponentKind
{
    PocketBridge,
    ScrcpyRuntime,
    AndroidPlatformTools
}

public sealed record ComponentUpdateStatus(
    UpdateComponentKind Component,
    string? InstalledVersion,
    string? AvailableVersion,
    string InformationUrl);
