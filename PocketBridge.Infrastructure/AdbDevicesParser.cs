using System.Text.RegularExpressions;
using PocketBridge.Core.Models;

namespace PocketBridge.Infrastructure;

public static partial class AdbDevicesParser
{
    public static IReadOnlyList<AndroidDevice> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<AndroidDevice>();
        }

        var devices = new List<AndroidDevice>();
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || line.StartsWith('*'))
            {
                continue;
            }

            var parts = WhitespaceRegex().Split(line);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = ParseState(parts[1]);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(2))
            {
                var separator = part.IndexOf(':');
                if (separator <= 0 || separator == part.Length - 1)
                {
                    continue;
                }

                fields[part[..separator]] = DecodeValue(part[(separator + 1)..]);
            }

            devices.Add(new AndroidDevice(
                serial,
                Get(fields, "model"),
                Get(fields, "product"),
                Get(fields, "device"),
                Get(fields, "transport_id"),
                TcpSerialRegex().IsMatch(serial) ? DeviceConnectionType.TcpIp : DeviceConnectionType.Usb,
                state));
        }

        return devices;
    }

    private static AndroidDeviceState ParseState(string state) => state.ToLowerInvariant() switch
    {
        "device" => AndroidDeviceState.Device,
        "unauthorized" => AndroidDeviceState.Unauthorized,
        "offline" => AndroidDeviceState.Offline,
        _ => AndroidDeviceState.Unknown
    };

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static string DecodeValue(string value) => value.Replace('_', ' ');

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:\[[0-9a-fA-F:]+\]|[^:]+):\d+$")]
    private static partial Regex TcpSerialRegex();
}
