using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IAppIconCache
{
    Task<byte[]?> GetAsync(AppIconCacheKey key, Func<CancellationToken, Task<byte[]?>> loader, CancellationToken cancellationToken = default);
    void Invalidate(string serial, string packageName);
    void InvalidateDevice(string serial);
    void Clear();
}
