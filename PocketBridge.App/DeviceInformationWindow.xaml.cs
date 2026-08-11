using System.Windows;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;
public partial class DeviceInformationWindow : Window
{
    private readonly AndroidDevice _device; private readonly IDeviceInformationService _service; private DeviceInformation? _information;
    public DeviceInformationWindow(AndroidDevice device, IDeviceInformationService service) { _device = device; _service = service; InitializeComponent(); DeviceText.Text = $"{device.FriendlyName} — {device.Serial}"; Loaded += async (_, _) => await LoadAsync(); }
    private async Task LoadAsync() { try { _information = await _service.GetAsync(_device); InfoList.ItemsSource = Rows(_information); } catch { InfoList.ItemsSource = Rows(new DeviceInformation(_device.FriendlyName, "—", _device.Model ?? "—", "—", "—", _device.Serial, _device.ConnectionType.ToString(), "—", "—", "—", "—", "—", "—", "—")); } }
    private static object[] Rows(DeviceInformation i) => new[] { new Row(L("FriendlyName"), i.FriendlyName), new Row(L("Manufacturer"), i.Manufacturer), new Row(L("Model"), i.Model), new Row(L("AndroidVersion"), $"{i.AndroidVersion} / SDK {i.Sdk}"), new Row(L("Serial"), i.Serial), new Row(L("ConnectionType"), i.ConnectionType), new Row("IP", i.IpAddress), new Row(L("Resolution"), i.Resolution), new Row("ABI", i.Abi), new Row(L("Battery"), $"{i.BatteryPercent} / {i.BatteryState}"), new Row(L("Storage"), i.Storage), new Row(L("Uptime"), i.Uptime) };
    private static string L(string key) => LocalizationService.Current[key];
    private void Copy_Click(object sender, RoutedEventArgs e) { if (_information is not null) Clipboard.SetText(_information.ToReport(PrivacyBox.IsChecked != false)); }
    private sealed record Row(string Label, string Value);
}
