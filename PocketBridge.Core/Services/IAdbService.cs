using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IAdbService
{
    Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ExecuteAsync(string serial, params string[] arguments);
    Task<AdbCommandResult> ExecuteHostAsync(params string[] arguments);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task StartServerAsync(CancellationToken cancellationToken = default);
    Task KillServerAsync(CancellationToken cancellationToken = default);
}
