namespace PocketBridge.Core.Models;

public enum FileTransferOperation
{
    Upload,
    InstallApk
}

public enum FileTransferState
{
    Waiting,
    Transferring,
    Completed,
    Failed,
    Cancelled
}

public sealed record FileTransferRequest(string Source, string Destination, string Serial, FileTransferOperation Operation, string? DeviceAlias = null);

public sealed record FileTransferSnapshot(
    Guid Id,
    string Source,
    string Destination,
    string Serial,
    FileTransferOperation Operation,
    double Progress,
    FileTransferState State,
    string? Error,
    DateTimeOffset Timestamp,
    string DeviceAlias,
    long Bytes,
    TimeSpan? Duration,
    string Direction);
