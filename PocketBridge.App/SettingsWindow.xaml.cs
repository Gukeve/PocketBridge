using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.ObjectModel;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class SettingsWindow : Window
{
    private readonly IRuntimeToolsService _runtimeTools;
    private readonly IUpdateCheckService _updates;
    private readonly string _initialLanguage;
    private readonly AppSettings _initialSettings;

    public SettingsWindow(AppSettings settings, IRuntimeToolsService runtimeTools, IUpdateCheckService updates)
    {
        _runtimeTools = runtimeTools;
        _updates = updates;
        _initialSettings = settings;
        _initialLanguage = LocalizationService.NormalizeLanguage(settings.Language);
        ShortcutItems = new ObservableCollection<ShortcutEditorItem>((settings.ShortcutBindings.Count == 0 ? ShortcutBinding.Defaults : settings.ShortcutBindings).Select(ShortcutEditorItem.From));
        InitializeComponent();
        DataContext = this;
        LanguageBox.ItemsSource = new[] { new LanguageOption("ru-RU", "Русский"), new LanguageOption("en-US", "English"), new LanguageOption("zh-CN", "简体中文") };
        LanguageBox.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageBox.DisplayMemberPath = nameof(LanguageOption.Name);
        LanguageBox.SelectedValue = _initialLanguage;
        ToolsDirectoryBox.Text = runtimeTools.DefaultToolsDirectory;
        RecordingFolderBox.Text = settings.RecordingFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        ScreenshotFolderBox.Text = settings.ScreenshotFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        ScreenshotFormatBox.Text = settings.ScreenshotFilenameFormat;
        RecordingFormatBox.SelectedIndex = settings.RecordingFormat.Equals("mkv", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        RecordingCodecBox.SelectedIndex = settings.RecordingVideoCodec switch { "h265" => 1, "av1" => 2, _ => 0 };
        RecordingAudioBox.IsChecked = settings.RecordingIncludeAudio;
        RecordingSizeBox.Text = settings.RecordingMaxSize?.ToString() ?? string.Empty;
        RecordingFpsBox.Text = settings.RecordingMaxFps?.ToString() ?? string.Empty;
        RecordingBitrateBox.Text = settings.RecordingBitrateMbps?.ToString() ?? string.Empty;
        VersionText.Text = LocalizationService.Current.Format("VersionFormat", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        UpdateComponentStatus();
    }

    public AppSettings? Result { get; private set; }
    public ObservableCollection<ShortcutEditorItem> ShortcutItems { get; }

    private void ToolsDirectoryBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateComponentStatus();
    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => LanguageRestartText.Visibility = Equals(LanguageBox.SelectedValue, _initialLanguage) ? Visibility.Collapsed : Visibility.Visible;

    private void UpdateComponentStatus()
    {
        if (!IsInitialized) return;
        RuntimeToolsStatus status;
        try { status = _runtimeTools.Inspect(ToolsDirectoryBox.Text); }
        catch
        {
            SetComponent(AdbStatusText, false, "adb.exe"); SetComponent(ScrcpyStatusText, false, "scrcpy.exe"); SetComponent(ServerStatusText, false, "scrcpy-server");
            ValidationText.Text = LocalizationService.Current["RuntimeIncomplete"]; return;
        }
        SetComponent(AdbStatusText, status.HasAdb, "adb.exe"); SetComponent(ScrcpyStatusText, status.HasScrcpy, "scrcpy.exe"); SetComponent(ServerStatusText, status.HasScrcpyServer, "scrcpy-server");
        ValidationText.Text = LocalizationService.Current[status.IsComplete ? "AllRuntimeFound" : "RuntimeIncomplete"];
        ValidationText.Foreground = status.IsComplete ? Brush(88, 214, 168) : Brush(243, 191, 99);
    }

    private static void SetComponent(TextBlock target, bool found, string name) { target.Text = $"{(found ? "✓" : "—")} {name}"; target.Foreground = found ? Brush(88, 214, 168) : Brush(170, 180, 197); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var format = (RecordingFormatBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mp4";
        var codec = (RecordingCodecBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "h264";
        var requested = ShortcutItems.Select(item => item.ToBinding()).Where(item => !string.IsNullOrWhiteSpace(item.Gesture)).ToArray();
        var resolved = ShortcutBindingResolver.ReplaceConflicts(requested);
        if (resolved.Count != requested.Length && MessageBox.Show(LocalizationService.Current["ShortcutConflictReplace"], LocalizationService.Current["Shortcuts"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Result = _initialSettings with { ToolsDirectory = _runtimeTools.DefaultToolsDirectory, Language = LanguageBox.SelectedValue as string ?? _initialLanguage, RecordingFolder = RecordingFolderBox.Text.Trim(), RecordingFormat = format, RecordingVideoCodec = codec, RecordingIncludeAudio = RecordingAudioBox.IsChecked == true, RecordingMaxSize = ParseOptional(RecordingSizeBox.Text), RecordingMaxFps = ParseOptional(RecordingFpsBox.Text), RecordingBitrateMbps = ParseOptional(RecordingBitrateBox.Text), ScreenshotFolder = ScreenshotFolderBox.Text.Trim(), ScreenshotFilenameFormat = ScreenshotFormatBox.Text.Trim(), ShortcutBindings = resolved }; DialogResult = true;
    }
    private void ResetShortcuts_Click(object sender, RoutedEventArgs e) { ShortcutItems.Clear(); foreach (var item in ShortcutBinding.Defaults.Select(ShortcutEditorItem.From)) ShortcutItems.Add(item); }
    private static int? ParseOptional(string value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    private void BrowseRecording_Click(object sender, RoutedEventArgs e) => BrowseFolder(RecordingFolderBox);
    private void BrowseScreenshot_Click(object sender, RoutedEventArgs e) => BrowseFolder(ScreenshotFolderBox);
    private void BrowseFolder(TextBox target) { var picker = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = target.Text }; if (picker.ShowDialog(this) == true) target.Text = picker.FolderName; }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenScrcpy_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/Genymobile/scrcpy");
    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        OpenBundledDocument("LICENSE");
    }
    private void OpenThirdParty_Click(object sender, RoutedEventArgs e) => OpenBundledDocument("THIRD_PARTY_NOTICES.md");
    private static void OpenBundledDocument(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var statuses = await _updates.CheckAsync();
            var lines = statuses.Select(status => LocalizationService.Current.Format(
                "ComponentVersionFormat",
                status.Component,
                status.InstalledVersion ?? LocalizationService.Current["NotInstalled"],
                status.AvailableVersion ?? LocalizationService.Current["NotPublished"]));
            MessageBox.Show(string.Join(Environment.NewLine, lines), LocalizationService.Current["CheckUpdates"], MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(LocalizationService.Current.Format("UpdateCheckFailed", exception.Message), LocalizationService.Current["CheckUpdates"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private static SolidColorBrush Brush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));
    private sealed record LanguageOption(string Code, string Name);
    public sealed class ShortcutEditorItem
    {
        public ShortcutAction Action { get; init; }
        public ShortcutScope Scope { get; init; }
        public string Gesture { get; set; } = string.Empty;
        public ShortcutBinding ToBinding() => new(Action, Scope, Gesture);
        public static ShortcutEditorItem From(ShortcutBinding binding) => new() { Action = binding.Action, Scope = binding.Scope, Gesture = binding.Gesture };
    }
}
