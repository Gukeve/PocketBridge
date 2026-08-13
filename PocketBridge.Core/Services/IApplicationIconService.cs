using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IApplicationIconService
{
    Task<byte[]?> GetAsync(string serial, InstalledApplication application, CancellationToken cancellationToken = default);
    void Invalidate(string serial, string packageName);
    void InvalidateDevice(string serial);
}
