using System.Collections.Concurrent;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class AppIconCache : IAppIconCache
{
    private readonly ConcurrentDictionary<AppIconCacheKey, Lazy<Task<byte[]?>>> _cache = new();
    public Task<byte[]?> GetAsync(AppIconCacheKey key, Func<CancellationToken, Task<byte[]?>> loader, CancellationToken cancellationToken = default)
        => _cache.GetOrAdd(key, _ => new Lazy<Task<byte[]?>>(() => loader(cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    public void Invalidate(string serial, string packageName) { foreach (var key in _cache.Keys.Where(key => key.Serial == serial && key.PackageName == packageName)) _cache.TryRemove(key, out _); }
    public void InvalidateDevice(string serial) { foreach (var key in _cache.Keys.Where(key => key.Serial == serial)) _cache.TryRemove(key, out _); }
    public void Clear() => _cache.Clear();
}
