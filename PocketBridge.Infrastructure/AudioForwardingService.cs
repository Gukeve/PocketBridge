using System.Collections.Concurrent;
using System.Diagnostics;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class AudioForwardingService(IAdbService adb, IExecutableLocator locator, IAppSettingsService settings) : IAudioForwardingService
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(StringComparer.Ordinal);
    public async Task<AudioCapability> DetectAsync(string serial)
    {
        if (locator.Find("scrcpy.exe", settings.Load().ToolsDirectory) is null) return AudioCapabilityDetector.Evaluate(null, false);
        var result = await adb.ExecuteAsync(serial, "shell", "getprop", "ro.build.version.sdk");
        if (!result.IsSuccess || !int.TryParse(result.StandardOutput.Trim(), out var api)) return AudioCapabilityDetector.Evaluate(null, true);
        return AudioCapabilityDetector.Evaluate(api, true);
    }
    public bool IsRunning(string serial) => _processes.TryGetValue(serial, out var process) && !process.HasExited;
    public async Task StartAsync(AndroidDevice device)
    {
        var capability = await DetectAsync(device.Serial);
        if (!capability.IsSupported) throw new NotSupportedException(capability.Reason);
        if (IsRunning(device.Serial)) return;
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory)!;
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { $"--serial={device.Serial}", "--no-video", "--no-window", "--audio", "--audio-codec=opus" }) info.ArgumentList.Add(argument);
        if (locator.Find("adb.exe", settings.Load().ToolsDirectory) is { } adbPath) info.Environment["ADB"] = adbPath;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Could not start audio forwarding."); }
        if (!_processes.TryAdd(device.Serial, process)) { process.Kill(true); process.Dispose(); return; }
        process.Exited += (_, _) => { if (_processes.TryRemove(device.Serial, out var removed)) removed.Dispose(); };
    }
    public Task StopAsync(string serial)
    {
        if (_processes.TryRemove(serial, out var process)) { if (!process.HasExited) process.Kill(true); process.Dispose(); }
        return Task.CompletedTask;
    }
    public void Dispose() { foreach (var serial in _processes.Keys) StopAsync(serial).GetAwaiter().GetResult(); }
}
