using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IAdbTransferExecutor
{
    Task<AdbCommandResult> UploadAsync(string serial, string source, string destination, IProgress<double> progress, CancellationToken cancellationToken);
    Task<AdbCommandResult> InstallApkAsync(string serial, string source, IProgress<double> progress, CancellationToken cancellationToken);
}

public interface IFileTransferQueueService : IAsyncDisposable
{
    event EventHandler? Changed;
    IReadOnlyList<FileTransferSnapshot> Items { get; }
    string? DiagnosticMessage { get; }
    IReadOnlyList<Guid> Enqueue(IEnumerable<FileTransferRequest> requests);
    void Cancel(Guid id);
    bool Retry(Guid id);
    int ClearCompleted();
    int ClearHistory();
}
