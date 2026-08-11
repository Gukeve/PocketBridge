using System.Windows.Media;
using PocketBridge.Core.Models;
using PocketBridge.App.Localization;

namespace PocketBridge.App.ViewModels;

public sealed class DeviceItemViewModel : ObservableObject
{
    private bool _isSessionRunning;
    public DeviceItemViewModel(AndroidDevice device, bool isSessionRunning) { Device = device; _isSessionRunning = isSessionRunning; }
    public AndroidDevice Device { get; private set; }
    public string Serial => Device.Serial;
    public string FriendlyName => Device.FriendlyName;
    public string ModelLine => string.IsNullOrWhiteSpace(Device.Model) ? LocalizationService.Current["ModelUnknown"] : Device.Model;
    public string ConnectionLabel => Device.ConnectionType == DeviceConnectionType.TcpIp ? "TCP/IP" : "USB";
    public string StateLabel => Device.State switch
    {
        AndroidDeviceState.Device => LocalizationService.Current[IsSessionRunning ? "BusySession" : "Connected"],
        AndroidDeviceState.Unauthorized => LocalizationService.Current["Unauthorized"],
        AndroidDeviceState.Offline => LocalizationService.Current["Offline"],
        _ => LocalizationService.Current["Unknown"]
    };
    public Brush StateBrush => Device.State switch
    {
        AndroidDeviceState.Device => new SolidColorBrush(Color.FromRgb(88, 214, 168)),
        AndroidDeviceState.Unauthorized => new SolidColorBrush(Color.FromRgb(243, 191, 99)),
        _ => new SolidColorBrush(Color.FromRgb(242, 120, 120))
    };
    public bool IsSessionRunning
    {
        get => _isSessionRunning;
        set { if (SetProperty(ref _isSessionRunning, value)) OnPropertyChanged(nameof(StateLabel)); }
    }

    public void Update(AndroidDevice device, bool isSessionRunning)
    {
        var deviceChanged = Device != device;
        Device = device;
        IsSessionRunning = isSessionRunning;
        if (!deviceChanged) return;
        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(Serial));
        OnPropertyChanged(nameof(FriendlyName));
        OnPropertyChanged(nameof(ModelLine));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(StateBrush));
    }
}
