using System.Collections.Concurrent;
using System.Diagnostics;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class VirtualDisplayService(IExecutableLocator locator, IAppSettingsService settings) : IVirtualDisplayService
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(StringComparer.Ordinal);
    public Task StartAsync(AndroidDevice device, VirtualDisplayOptions options)
    {
        if (options.Width is < 640 or > 7680 || options.Height is < 480 or > 4320 || options.Dpi is < 120 or > 960) throw new ArgumentOutOfRangeException(nameof(options));
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory) ?? throw new FileNotFoundException("scrcpy runtime was not found.");
        var info = Base(executable); info.ArgumentList.Add($"--serial={device.Serial}"); info.ArgumentList.Add($"--new-display={options.Width}x{options.Height}/{options.Dpi}");
        if (!string.IsNullOrWhiteSpace(options.PackageName)) info.ArgumentList.Add($"--start-app=+{options.PackageName}");
        var process = Process.Start(info) ?? throw new InvalidOperationException("Virtual display process did not start."); if (!_processes.TryAdd(device.Serial, process)) { process.Kill(true); process.Dispose(); throw new InvalidOperationException("A virtual display session already exists for this device."); }
        process.EnableRaisingEvents = true; process.Exited += (_, _) => { if (_processes.TryRemove(device.Serial, out var removed)) removed.Dispose(); }; return Task.CompletedTask;
    }
    public Task StopAsync(string serial) { if (_processes.TryRemove(serial, out var process)) { if (!process.HasExited) process.Kill(true); process.Dispose(); } return Task.CompletedTask; }
    public void Dispose() { foreach (var serial in _processes.Keys) StopAsync(serial).GetAwaiter().GetResult(); }
    private ProcessStartInfo Base(string executable) { var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false }; if (locator.Find("adb.exe", settings.Load().ToolsDirectory) is { } adb) info.Environment["ADB"] = adb; return info; }
}

public sealed class InputBackendService(IExecutableLocator locator, IAppSettingsService settings) : IInputBackendService
{
    private Process? _otg;
    public InputBackendKind Select(InputBackendKind requested, HidOtgCapabilities capabilities) => InputBackendSelector.Select(requested, capabilities);
    public Task StartOtgAsync()
    {
        if (_otg is { HasExited: false }) return Task.CompletedTask;
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory) ?? throw new FileNotFoundException("scrcpy runtime was not found.");
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false }; info.ArgumentList.Add("--otg"); info.ArgumentList.Add("--keyboard=aoa"); info.ArgumentList.Add("--mouse=aoa");
        _otg = Process.Start(info) ?? throw new InvalidOperationException("OTG mode did not start."); return Task.CompletedTask;
    }
    public Task StopOtgAsync() { if (_otg is not null) { if (!_otg.HasExited) _otg.Kill(true); _otg.Dispose(); _otg = null; } return Task.CompletedTask; }
}
