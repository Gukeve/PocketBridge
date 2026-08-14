namespace PocketBridge.Core.Models;

public sealed class DeviceSession
{
    public required string Serial { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public int DisplayId { get; init; }
    public bool IsRunning { get; set; }
    public int? ExitCode { get; set; }
}
