using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IDeviceProfileService
{
    DeviceProfile Get(string serial);
    IReadOnlyList<DeviceProfile> GetAll();
    Task SaveAsync(DeviceProfile profile, CancellationToken cancellationToken = default);
}
