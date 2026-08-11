using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IAppSettingsService
{
    AppSettings Load();
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
