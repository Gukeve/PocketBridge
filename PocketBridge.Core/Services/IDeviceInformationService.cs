using PocketBridge.Core.Models;
namespace PocketBridge.Core.Services;
public interface IDeviceInformationService { Task<DeviceInformation> GetAsync(AndroidDevice device); }
