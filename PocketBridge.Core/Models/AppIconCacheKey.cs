namespace PocketBridge.Core.Models;

public sealed record AppIconCacheKey(string Serial, string PackageName, long? VersionCode);
