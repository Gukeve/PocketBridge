using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IApplicationService
{
    Task<IReadOnlyList<InstalledApplication>> ListAsync(string serial);
    Task<AdbCommandResult> LaunchAsync(string serial, string packageName);
    Task<AdbCommandResult> StopAsync(string serial, string packageName);
    Task<AdbCommandResult> UninstallAsync(string serial, string packageName);
    Task<AdbCommandResult> ClearDataAsync(string serial, string packageName);
    Task<AdbCommandResult> OpenDetailsAsync(string serial, string packageName);
}
