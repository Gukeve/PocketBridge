using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IAudioForwardingService : IDisposable
{
    Task<AudioCapability> DetectAsync(string serial);
    bool IsRunning(string serial);
    Task StartAsync(AndroidDevice device);
    Task StopAsync(string serial);
}
