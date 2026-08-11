using System.Collections.Concurrent;
using System.Diagnostics;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class RecordingService(IExecutableLocator locator, IAppSettingsService settings) : IRecordingService
{
    private readonly ConcurrentDictionary<string, Holder> _active = new(StringComparer.Ordinal);
    public event EventHandler<RecordingSession>? Changed;
    public RecordingSession? Get(string serial) => _active.TryGetValue(serial, out var holder) ? holder.Session : null;

    public Task<RecordingSession> StartAsync(AndroidDevice device, string filePath)
    {
        if (!device.IsReady) throw new InvalidOperationException("The selected device is not ready.");
        if (_active.ContainsKey(device.Serial)) throw new InvalidOperationException("This device is already being recorded.");
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory) ?? throw new FileNotFoundException("scrcpy.exe was not found.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add($"--serial={device.Serial}");
        info.ArgumentList.Add($"--record={Path.GetFullPath(filePath)}");
        info.ArgumentList.Add("--no-playback");
        var adb = locator.Find("adb.exe", settings.Load().ToolsDirectory);
        if (adb is not null) info.Environment["ADB"] = adb;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Could not start screen recording."); }
        var session = new RecordingSession(device.Serial, Path.GetFullPath(filePath), DateTimeOffset.Now, true);
        var holder = new Holder(session, process);
        if (!_active.TryAdd(device.Serial, holder)) { process.Kill(true); process.Dispose(); throw new InvalidOperationException("This device is already being recorded."); }
        process.Exited += (_, _) => Complete(device.Serial, holder);
        Changed?.Invoke(this, session);
        return Task.FromResult(session);
    }

    public async Task<RecordingSession?> StopAsync(string serial)
    {
        if (!_active.TryGetValue(serial, out var holder)) return null;
        if (!holder.Process.HasExited) holder.Process.Kill(true);
        await holder.Completion.Task.ConfigureAwait(false);
        return holder.Session with { IsRunning = false };
    }

    private void Complete(string serial, Holder holder)
    {
        if (!_active.TryRemove(serial, out var removed) || !ReferenceEquals(removed, holder)) return;
        holder.Session = holder.Session with { IsRunning = false };
        Changed?.Invoke(this, holder.Session);
        holder.Completion.TrySetResult();
        holder.Process.Dispose();
    }

    public void Dispose()
    {
        foreach (var serial in _active.Keys) StopAsync(serial).GetAwaiter().GetResult();
    }

    private sealed class Holder(RecordingSession session, Process process)
    {
        public RecordingSession Session { get; set; } = session;
        public Process Process { get; } = process;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
