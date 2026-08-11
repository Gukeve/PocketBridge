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

public sealed record FileTransferRequest(string Source, string Destination, string Serial, FileTransferOperation Operation);

public sealed record FileTransferSnapshot(
    Guid Id,
    string Source,
    string Destination,
    string Serial,
    FileTransferOperation Operation,
    double Progress,
    FileTransferState State,
    string? Error);
