using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IScrcpyService
{
    event EventHandler<DeviceSession>? SessionChanged;

    Task<DeviceSession> StartAsync(AndroidDevice device, ScrcpyLaunchOptions options);
    Task StopAsync(string serial, int displayId = 0);
    bool IsRunning(string serial, int displayId = 0);
    IReadOnlyCollection<DeviceSession> GetActiveSessions();
}
