using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class DeviceInformationService(IAdbService adb) : IDeviceInformationService
{
    public async Task<DeviceInformation> GetAsync(AndroidDevice device)
    {
        var propsTask = adb.ExecuteAsync(device.Serial, "shell", "getprop");
        var sizeTask = adb.ExecuteAsync(device.Serial, "shell", "wm", "size");
        var batteryTask = adb.ExecuteAsync(device.Serial, "shell", "dumpsys", "battery");
        var storageTask = adb.ExecuteAsync(device.Serial, "shell", "df", "-h", "/data");
        var uptimeTask = adb.ExecuteAsync(device.Serial, "shell", "cat", "/proc/uptime");
        var ipTask = adb.ExecuteAsync(device.Serial, "shell", "ip", "route");
        await Task.WhenAll(propsTask, sizeTask, batteryTask, storageTask, uptimeTask, ipTask).ConfigureAwait(false);
        var props = ParseProperties(propsTask.Result.StandardOutput);
        var battery = batteryTask.Result.StandardOutput;
        return new DeviceInformation(
            device.FriendlyName, Value(props, "ro.product.manufacturer"), Value(props, "ro.product.model"), Value(props, "ro.build.version.release"), Value(props, "ro.build.version.sdk"),
            device.Serial, device.ConnectionType.ToString(), ParseIp(ipTask.Result.StandardOutput), ParseResolution(sizeTask.Result.StandardOutput), Value(props, "ro.product.cpu.abi"),
            Match(battery, @"level:\s*(\d+)", "$1%"), ParseBatteryState(battery), ParseStorage(storageTask.Result.StandardOutput), ParseUptime(uptimeTask.Result.StandardOutput));
    }

    internal static Dictionary<string, string> ParseProperties(string output) => PropertyRegex().Matches(output).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
    private static string Value(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && value.Length > 0 ? value : "—";
    private static string Match(string text, string pattern, string replacement = "$1") { var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return match.Success ? Regex.Replace(match.Value, pattern, replacement, RegexOptions.IgnoreCase) : "—"; }
    private static string ParseIp(string text) => Match(text, @"src\s+((?:\d{1,3}\.){3}\d{1,3})");
    private static string ParseResolution(string text) => Match(text, @"(?:Physical|Override) size:\s*(\d+x\d+)");
    private static string ParseBatteryState(string text) => Regex.IsMatch(text, @"status:\s*2") ? "charging" : Regex.IsMatch(text, @"status:\s*5") ? "full" : Regex.IsMatch(text, @"status:\s*3|status:\s*4") ? "discharging" : "—";
    private static string ParseStorage(string text) { var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault(); if (line is null) return "—"; var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); return parts.Length >= 4 ? $"{parts[2]} used / {parts[3]} free" : "—"; }
    private static string ParseUptime(string text) { var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(); return double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds).ToString(@"d\.hh\:mm\:ss") : "—"; }
    [GeneratedRegex(@"\[([^\]]+)\]: \[([^\]]*)\]")]
    private static partial Regex PropertyRegex();
}
