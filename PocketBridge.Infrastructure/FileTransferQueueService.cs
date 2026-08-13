using System.Collections.Concurrent;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class FileTransferQueueService : IFileTransferQueueService
{
    private readonly IAdbTransferExecutor _executor;
    private readonly ConcurrentDictionary<Guid, Entry> _items = new();
    private readonly ConcurrentQueue<Guid> _waiting = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;

    public FileTransferQueueService(IAdbTransferExecutor executor)
    {
        _executor = executor;
        _worker = Task.Run(ProcessAsync);
    }

    public event EventHandler? Changed;
    public IReadOnlyList<FileTransferSnapshot> Items => _items.Values.Select(entry => entry.Snapshot()).OrderBy(entry => entry.State is FileTransferState.Waiting or FileTransferState.Transferring ? 0 : 1).ThenBy(entry => entry.Source, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public IReadOnlyList<Guid> Enqueue(IEnumerable<FileTransferRequest> requests)
    {
        var ids = new List<Guid>();
        foreach (var request in requests)
        {
            Validate(request);
            var entry = new Entry(Guid.NewGuid(), request);
            if (!_items.TryAdd(entry.Id, entry)) continue;
            ids.Add(entry.Id);
            _waiting.Enqueue(entry.Id);
            _signal.Release();
        }
        if (ids.Count > 0) Changed?.Invoke(this, EventArgs.Empty);
        return ids;
    }

    public void Cancel(Guid id)
    {
        if (!_items.TryGetValue(id, out var entry)) return;
        lock (entry.Gate)
        {
            if (entry.State == FileTransferState.Waiting) entry.State = FileTransferState.Cancelled;
            else if (entry.State == FileTransferState.Transferring) entry.Cancellation?.Cancel();
            else return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Retry(Guid id)
    {
        if (!_items.TryGetValue(id, out var entry)) return false;
        lock (entry.Gate)
        {
            if (entry.State is not (FileTransferState.Failed or FileTransferState.Cancelled)) return false;
            entry.State = FileTransferState.Waiting;
            entry.Progress = 0;
            entry.Error = null;
        }
        _waiting.Enqueue(id);
        _signal.Release();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public int ClearCompleted()
    {
        var removed = 0;
        foreach (var pair in _items.Where(pair => pair.Value.State == FileTransferState.Completed).ToArray())
            if (_items.TryRemove(pair.Key, out _)) removed++;
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public int ClearHistory()
    {
        var removed = 0;
        foreach (var pair in _items.Where(pair => pair.Value.State is FileTransferState.Completed or FileTransferState.Failed or FileTransferState.Cancelled).ToArray())
            if (_items.TryRemove(pair.Key, out _)) removed++;
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    private async Task ProcessAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            if (!_waiting.TryDequeue(out var id) || !_items.TryGetValue(id, out var entry)) continue;
            lock (entry.Gate)
            {
                if (entry.State != FileTransferState.Waiting) continue;
                entry.State = FileTransferState.Transferring;
                entry.Started = DateTimeOffset.Now;
                entry.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                var progress = new Progress<double>(value => { lock (entry.Gate) { if (entry.State == FileTransferState.Transferring) entry.Progress = value; } Changed?.Invoke(this, EventArgs.Empty); });
                var result = entry.Request.Operation == FileTransferOperation.InstallApk
                    ? await _executor.InstallApkAsync(entry.Request.Serial, entry.Request.Source, progress, entry.Cancellation.Token).ConfigureAwait(false)
                    : await _executor.UploadAsync(entry.Request.Serial, entry.Request.Source, entry.Request.Destination, progress, entry.Cancellation.Token).ConfigureAwait(false);
                lock (entry.Gate)
                {
                    entry.Progress = result.IsSuccess ? 1 : entry.Progress;
                    entry.State = result.IsSuccess ? FileTransferState.Completed : FileTransferState.Failed;
                    entry.Error = result.IsSuccess ? null : (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
                    entry.Completed = DateTimeOffset.Now;
                }
            }
            catch (OperationCanceledException)
            {
                lock (entry.Gate) { entry.State = FileTransferState.Cancelled; entry.Error = null; entry.Completed = DateTimeOffset.Now; }
            }
            catch (Exception exception)
            {
                lock (entry.Gate) { entry.State = FileTransferState.Failed; entry.Error = exception.Message; entry.Completed = DateTimeOffset.Now; }
            }
            finally
            {
                lock (entry.Gate) { entry.Cancellation?.Dispose(); entry.Cancellation = null; }
                Changed?.Invoke(this, EventArgs.Empty);
                TrimHistory();
            }
        }
    }

    private void TrimHistory()
    {
        foreach (var entry in _items.Values.Where(item => item.State is FileTransferState.Completed or FileTransferState.Failed or FileTransferState.Cancelled).OrderByDescending(item => item.Completed).Skip(500).ToArray())
            _items.TryRemove(entry.Id, out _);
    }

    private static void Validate(FileTransferRequest request)
    {
        if (!File.Exists(request.Source)) throw new FileNotFoundException("Transfer source was not found.", request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Serial);
        if (request.Operation == FileTransferOperation.InstallApk && !Path.GetExtension(request.Source).Equals(".apk", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Install jobs require an APK file.", nameof(request));
        if (request.Operation == FileTransferOperation.Upload && (!request.Destination.StartsWith("/sdcard/", StringComparison.Ordinal) && !request.Destination.StartsWith("/storage/emulated/0/", StringComparison.Ordinal))) throw new ArgumentException("Destination must be inside Android shared storage.", nameof(request));
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (var entry in _items.Values) entry.Cancellation?.Cancel();
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _signal.Dispose();
        _lifetime.Dispose();
    }

    private sealed class Entry(Guid id, FileTransferRequest request)
    {
        public object Gate { get; } = new();
        public Guid Id { get; } = id;
        public FileTransferRequest Request { get; } = request;
        public double Progress { get; set; }
        public FileTransferState State { get; set; } = FileTransferState.Waiting;
        public string? Error { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public DateTimeOffset Created { get; } = DateTimeOffset.Now;
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? Completed { get; set; }
        public FileTransferSnapshot Snapshot()
        {
            lock (Gate)
            {
                var bytes = File.Exists(Request.Source) ? new FileInfo(Request.Source).Length : 0;
                return new(Id, Request.Source, Request.Destination, Request.Serial, Request.Operation, Progress, State, Error, Created, Request.DeviceAlias ?? Redact(Request.Serial), bytes, Started is null ? null : (Completed ?? DateTimeOffset.Now) - Started, "PC → Android");
            }
        }
        private static string Redact(string serial) => serial.Length <= 4 ? "••••" : $"••••{serial[^4..]}";
    }
}
