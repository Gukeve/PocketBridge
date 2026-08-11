using System.Windows;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class WifiWindow : Window
{
    private readonly AndroidDevice _device;
    private readonly IWifiAdbService _wifi;
    private readonly IDeviceProfileService _profiles;

    public WifiWindow(AndroidDevice device, IWifiAdbService wifi, IDeviceProfileService profiles)
    {
        _device = device;
        _wifi = wifi;
        _profiles = profiles;
        InitializeComponent();
        var profile = profiles.Get(device.Serial);
        DeviceText.Text = $"{profile.DisplayName(device.FriendlyName)} — {device.Serial}";
        IpBox.Text = profile.LastKnownIp ?? string.Empty;
    }

    private async void Enable_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var result = await _wifi.EnableTcpIpAsync(_device.Serial, ParsePort());
        IpBox.Text = result.Address.Split(':')[0];
        await RememberEndpointAsync(IpBox.Text);
        return result.Output;
    });

    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var result = await _wifi.ConnectAsync(IpBox.Text.Trim(), ParsePort());
        await RememberEndpointAsync(IpBox.Text.Trim());
        return result.Output;
    });

    private async void Pair_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        (await _wifi.PairAsync(PairIpBox.Text.Trim(), ParsePairPort(), PairCodeBox.Text.Trim())).Output);

    private int ParsePort() => int.TryParse(PortBox.Text, out var port) ? port : throw new InvalidOperationException(LocalizationService.Current["Port"]);
    private int ParsePairPort() => int.TryParse(PairPortBox.Text, out var port) ? port : throw new InvalidOperationException(LocalizationService.Current["PairingPort"]);

    private Task RememberEndpointAsync(string ip)
    {
        var profile = _profiles.Get(_device.Serial);
        return _profiles.SaveAsync(profile with { LastKnownIp = ip, PreferredConnection = PreferredDeviceConnection.TcpIp, LastConnected = DateTimeOffset.Now });
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        EnableButton.IsEnabled = ConnectButton.IsEnabled = PairButton.IsEnabled = false;
        StatusText.Text = LocalizationService.Current["StatusRefreshing"];
        try
        {
            StatusText.Text = await action();
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
        finally
        {
            EnableButton.IsEnabled = ConnectButton.IsEnabled = PairButton.IsEnabled = true;
        }
    }
}
