namespace PocketBridge.Core.Models;

public sealed record DeviceInformation(
    string FriendlyName, string Manufacturer, string Model, string AndroidVersion, string Sdk,
    string Serial, string ConnectionType, string IpAddress, string Resolution, string Abi,
    string BatteryPercent, string BatteryState, string Storage, string Uptime)
{
    public string ToReport(bool hidePrivate) => string.Join(Environment.NewLine, new[]
    {
        $"Friendly name: {FriendlyName}", $"Manufacturer: {Manufacturer}", $"Model: {Model}",
        $"Android: {AndroidVersion} (SDK {Sdk})", $"Serial: {(hidePrivate ? "[hidden]" : Serial)}",
        $"Connection: {ConnectionType}", $"IP: {(hidePrivate ? "[hidden]" : IpAddress)}", $"Resolution: {Resolution}",
        $"ABI: {Abi}", $"Battery: {BatteryPercent} ({BatteryState})", $"Storage: {Storage}", $"Uptime: {Uptime}"
    });
}
