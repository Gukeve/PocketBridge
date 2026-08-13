using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IRecordingService : IDisposable
{
    event EventHandler<RecordingSession>? Changed;
    RecordingSession? Get(string serial);
    Task<RecordingSession> StartAsync(AndroidDevice device, string filePath, RecordingOptions? options = null);
    Task<RecordingSession?> StopAsync(string serial);
}
