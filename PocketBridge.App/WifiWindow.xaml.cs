using System.Windows;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class WifiWindow : Window
{
    private readonly AndroidDevice _device; private readonly IWifiAdbService _wifi;
    public WifiWindow(AndroidDevice device, IWifiAdbService wifi) { _device = device; _wifi = wifi; InitializeComponent(); DeviceText.Text = $"{device.FriendlyName} — {device.Serial}"; }
    private async void Enable_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { var result = await _wifi.EnableTcpIpAsync(_device.Serial, ParsePort()); IpBox.Text = result.Address.Split(':')[0]; return result.Output; });
    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => (await _wifi.ConnectAsync(IpBox.Text.Trim(), ParsePort())).Output);
    private int ParsePort() => int.TryParse(PortBox.Text, out var port) ? port : throw new InvalidOperationException(LocalizationService.Current["Port"]);
    private async Task RunAsync(Func<Task<string>> action)
    {
        EnableButton.IsEnabled = ConnectButton.IsEnabled = false; StatusText.Text = LocalizationService.Current["StatusRefreshing"];
        try { StatusText.Text = await action(); StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush"); }
        catch (Exception ex) { StatusText.Text = ex.Message; StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush"); }
        finally { EnableButton.IsEnabled = ConnectButton.IsEnabled = true; }
    }
}
