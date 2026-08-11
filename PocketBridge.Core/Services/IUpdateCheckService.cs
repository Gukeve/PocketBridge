using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IUpdateCheckService
{
    Task<IReadOnlyList<ComponentUpdateStatus>> CheckAsync(CancellationToken cancellationToken = default);
}
