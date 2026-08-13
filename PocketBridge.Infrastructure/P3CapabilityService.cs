using System.Diagnostics;
using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class P3CapabilityService(IAdbService adb, IExecutableLocator locator, IAppSettingsService settings) : IP3CapabilityService
{
    public async Task<HidOtgCapabilities> DetectInputAsync(string serial)
    {
        var apiResult = await adb.ExecuteAsync(serial, "shell", "getprop", "ro.build.version.sdk");
        var runtime = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory) is not null;
        var api = int.TryParse(apiResult.StandardOutput.Trim(), out var value) ? value : (int?)null;
        var hid = runtime && api >= 28 ? CapabilityState.Supported : api is null ? CapabilityState.Unknown : CapabilityState.Unsupported;
        var usb = !serial.Contains(':');
        var otg = !runtime ? CapabilityState.Unknown : usb ? CapabilityState.RequiresExclusiveConnection : CapabilityState.RequiresUsb;
        return new(hid, hid, otg, hid == CapabilityState.Supported ? "UHID is available; Automatic may select it." : "Standard scrcpy control remains available; UHID requires Android 9 or newer.");
    }
    public async Task<DisplayCapabilities> DetectDisplaysAsync(string serial)
    {
        var apiResult = await adb.ExecuteAsync(serial, "shell", "getprop", "ro.build.version.sdk"); int.TryParse(apiResult.StandardOutput.Trim(), out var api);
        var output = await RunScrcpyAsync(serial, "--list-displays");
        var displays = DisplayRegex().Matches(output).Select(match => new DisplayDescriptor(int.Parse(match.Groups[1].Value), match.Groups[2].Value.Trim(), null, null)).DistinctBy(item => item.DisplayId).ToArray();
        if (displays.Length == 0) displays = new[] { new DisplayDescriptor(0, "Main display", null, null) };
        var virtualState = api >= 30 ? CapabilityState.Supported : CapabilityState.Unsupported;
        return new(!string.IsNullOrWhiteSpace(output), virtualState, displays, virtualState == CapabilityState.Supported ? "Official scrcpy new-display is available." : "Virtual displays require Android 11 or newer in PocketBridge.");
    }
    private async Task<string> RunScrcpyAsync(string serial, string argument)
    {
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory); if (executable is null) return string.Empty;
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add($"--serial={serial}"); info.ArgumentList.Add(argument); if (locator.Find("adb.exe", settings.Load().ToolsDirectory) is { } adbPath) info.Environment["ADB"] = adbPath;
        using var process = Process.Start(info); if (process is null) return string.Empty; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); return await stdout + Environment.NewLine + await stderr;
    }
    [GeneratedRegex(@"--display-id=(\d+)\s+\(([^\r\n]+)\)", RegexOptions.IgnoreCase)] private static partial Regex DisplayRegex();
}
