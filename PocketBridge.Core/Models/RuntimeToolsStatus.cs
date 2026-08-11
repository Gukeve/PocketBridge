namespace PocketBridge.Core.Models;

public sealed record RuntimeToolsStatus(
    string Directory,
    bool HasAdb,
    bool HasScrcpy,
    bool HasScrcpyServer,
    bool HasAdbWinApi,
    bool HasAdbWinUsbApi)
{
    public bool IsComplete => HasAdb && HasScrcpy && HasScrcpyServer && HasAdbWinApi && HasAdbWinUsbApi;
}

public sealed record RuntimeToolsInstallResult(string Version, string Directory, int FileCount);
