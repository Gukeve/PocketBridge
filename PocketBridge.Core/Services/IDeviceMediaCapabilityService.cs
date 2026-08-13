using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IDeviceMediaCapabilityService { Task<DeviceMediaCapabilities> DetectAsync(string serial); }
