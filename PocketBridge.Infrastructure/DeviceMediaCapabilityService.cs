using PocketBridge.Core.Models;
using PocketBridge.Core.Services;
using System.Diagnostics;

namespace PocketBridge.Infrastructure;

public sealed class DeviceMediaCapabilityService(IAudioForwardingService audio, IExecutableLocator locator, IAppSettingsService settings) : IDeviceMediaCapabilityService
{
    public async Task<DeviceMediaCapabilities> DetectAsync(string serial)
    {
        var audioTask = audio.DetectAsync(serial);
        var executable = locator.Find("scrcpy.exe", settings.Load().ToolsDirectory);
        if (executable is null) return new(await audioTask.ConfigureAwait(false), Array.Empty<string>());
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add($"--serial={serial}"); info.ArgumentList.Add("--list-encoders");
        if (locator.Find("adb.exe", settings.Load().ToolsDirectory) is { } adbPath) info.Environment["ADB"] = adbPath;
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(); var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false) + Environment.NewLine + await errorTask.ConfigureAwait(false);
            return new(await audioTask.ConfigureAwait(false), process.ExitCode == 0 ? VideoCodecCapabilityDetector.ParseEncoders(output) : Array.Empty<string>());
        }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } return new(await audioTask.ConfigureAwait(false), Array.Empty<string>()); }
    }
}
