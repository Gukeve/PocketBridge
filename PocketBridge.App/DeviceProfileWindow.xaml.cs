using System.Windows;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;

namespace PocketBridge.App;

public partial class DeviceProfileWindow : Window
{
    private readonly DeviceProfile _profile;

    public DeviceProfileWindow(AndroidDevice device, DeviceProfile profile)
    {
        _profile = profile;
        InitializeComponent();
        SerialText.Text = device.Serial;
        FriendlyNameBox.Text = profile.FriendlyName ?? string.Empty;
        ConnectionBox.ItemsSource = new[]
        {
            new ConnectionOption(PreferredDeviceConnection.Automatic, LocalizationService.Current["Automatic"]),
            new ConnectionOption(PreferredDeviceConnection.Usb, "USB"),
            new ConnectionOption(PreferredDeviceConnection.TcpIp, "TCP/IP")
        };
        ConnectionBox.SelectedValuePath = nameof(ConnectionOption.Value);
        ConnectionBox.DisplayMemberPath = nameof(ConnectionOption.Label);
        ConnectionBox.SelectedValue = profile.PreferredConnection;
        ResolutionBox.ItemsSource = new object[] { LocalizationService.Current["Native"], 1920, 1600, 1280, 1024 };
        ResolutionBox.Text = profile.PreferredResolution?.ToString() ?? LocalizationService.Current["Native"];
        FpsBox.ItemsSource = new[] { 60, 45, 30, 24 };
        FpsBox.Text = profile.PreferredFps?.ToString() ?? "60";
        BitrateBox.ItemsSource = new object[] { LocalizationService.Current["Automatic"], 4, 8, 12, 16 };
        BitrateBox.Text = profile.PreferredBitrateMbps?.ToString() ?? LocalizationService.Current["Automatic"];
        CodecBox.ItemsSource = new[] { "H.264" };
        CodecBox.SelectedIndex = 0;
        StayAwakeBox.IsChecked = profile.StayAwake;
        ScreenOffBox.IsChecked = profile.ScreenOffOnConnect;
        AlwaysOnTopBox.IsChecked = profile.AlwaysOnTop;
        AudioBox.IsChecked = profile.AudioEnabled;
        ClipboardModeBox.ItemsSource = new[]
        {
            new ClipboardOption(ClipboardSyncMode.Off, LocalizationService.Current["ClipboardOff"]),
            new ClipboardOption(ClipboardSyncMode.Manual, LocalizationService.Current["ClipboardManual"]),
            new ClipboardOption(ClipboardSyncMode.Automatic, LocalizationService.Current["ClipboardAutomatic"])
        };
        ClipboardModeBox.SelectedValuePath = nameof(ClipboardOption.Value);
        ClipboardModeBox.DisplayMemberPath = nameof(ClipboardOption.Label);
        ClipboardModeBox.SelectedValue = profile.ClipboardMode;
    }

    public DeviceProfile? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryOptionalInt(ResolutionBox.Text, "Native", 256, 8192, out var resolution) ||
            !TryOptionalInt(FpsBox.Text, null, 1, 240, out var fps) ||
            !TryOptionalInt(BitrateBox.Text, "Automatic", 1, 200, out var bitrate))
        {
            MessageBox.Show(LocalizationService.Current["InvalidProfileValues"], LocalizationService.Current["DeviceProfile"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = _profile with
        {
            FriendlyName = string.IsNullOrWhiteSpace(FriendlyNameBox.Text) ? null : FriendlyNameBox.Text.Trim(),
            PreferredConnection = ConnectionBox.SelectedValue is PreferredDeviceConnection connection ? connection : PreferredDeviceConnection.Automatic,
            PreferredResolution = resolution,
            PreferredFps = fps,
            PreferredBitrateMbps = bitrate,
            PreferredCodec = PreferredVideoCodec.H264,
            StayAwake = StayAwakeBox.IsChecked == true,
            ScreenOffOnConnect = ScreenOffBox.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopBox.IsChecked == true,
            AudioEnabled = AudioBox.IsChecked == true,
            ClipboardMode = ClipboardModeBox.SelectedValue is ClipboardSyncMode clipboardMode ? clipboardMode : ClipboardSyncMode.Manual
        };
        DialogResult = true;
    }

    private static bool TryOptionalInt(string text, string? emptyLabel, int minimum, int maximum, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text) || (emptyLabel is not null && (text.Equals(emptyLabel, StringComparison.OrdinalIgnoreCase) || text.Equals(LocalizationService.Current[emptyLabel], StringComparison.OrdinalIgnoreCase)))) return true;
        if (!int.TryParse(text.Trim(), out var parsed) || parsed < minimum || parsed > maximum) return false;
        value = parsed;
        return true;
    }

    private sealed record ConnectionOption(PreferredDeviceConnection Value, string Label);
    private sealed record ClipboardOption(ClipboardSyncMode Value, string Label);
}
