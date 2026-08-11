using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IRuntimeToolsService
{
    string DefaultToolsDirectory { get; }
    RuntimeToolsStatus Inspect(string? directory = null);
    Task<RuntimeToolsInstallResult> DownloadLatestAsync(CancellationToken cancellationToken = default);
}
