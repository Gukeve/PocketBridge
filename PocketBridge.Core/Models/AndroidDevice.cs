namespace PocketBridge.Core.Models;

public enum AndroidDeviceState
{
    Device,
    Unauthorized,
    Offline,
    Unknown
}

public enum DeviceConnectionType
{
    Usb,
    TcpIp
}

public sealed record AndroidDevice(
    string Serial,
    string? Model,
    string? Product,
    string? DeviceName,
    string? TransportId,
    DeviceConnectionType ConnectionType,
    AndroidDeviceState State)
{
    public string FriendlyName => FirstMeaningful(Model, DeviceName, Product, Serial);
    public bool IsReady => State == AndroidDeviceState.Device;

    private static string FirstMeaningful(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;
}
