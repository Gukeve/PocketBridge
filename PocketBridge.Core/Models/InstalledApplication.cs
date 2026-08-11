namespace PocketBridge.Core.Models;

public enum AndroidApplicationType
{
    User,
    System
}

public sealed record InstalledApplication(string PackageName, string ApplicationName, string? VersionName, long? VersionCode, AndroidApplicationType Type);
