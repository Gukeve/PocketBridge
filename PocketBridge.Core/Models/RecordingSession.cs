namespace PocketBridge.Core.Models;

public sealed record RecordingSession(string Serial, string FilePath, DateTimeOffset StartedAt, bool IsRunning);
