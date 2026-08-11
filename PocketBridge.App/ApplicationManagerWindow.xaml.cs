using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PocketBridge.App.Localization;
using PocketBridge.App.Services;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class ApplicationManagerWindow : Window
{
    private readonly AndroidDevice _device;
    private readonly IApplicationService _applications;
    private readonly IApkInstallerService _installer;
    private readonly IConfirmationService _confirmation;
    private IReadOnlyList<InstalledApplication> _all = Array.Empty<InstalledApplication>();
    private readonly ObservableCollection<Row> _user = new();
    private readonly ObservableCollection<Row> _system = new();
    private bool _busy;

    public ApplicationManagerWindow(AndroidDevice device, IApplicationService applications, IApkInstallerService installer, IConfirmationService confirmation)
    {
        _device = device; _applications = applications; _installer = installer; _confirmation = confirmation;
        InitializeComponent();
        DeviceText.Text = $"{device.FriendlyName} — {device.Serial}";
        UserList.ItemsSource = _user; SystemList.ItemsSource = _system;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private Row? Selected => (TypeTabs.SelectedIndex == 0 ? UserList.SelectedItem : SystemList.SelectedItem) as Row;
    private async Task RefreshAsync() => await RunAsync(async () => { _all = await _applications.ListAsync(_device.Serial); ApplyFilter(); return LocalizationService.Current.Format("ApplicationsFound", _all.Count); });
    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = _all.Where(app => query.Length == 0 || app.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase) || app.ApplicationName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        _user.Clear(); _system.Clear();
        foreach (var app in filtered) (app.Type == AndroidApplicationType.User ? _user : _system).Add(new Row(app));
    }
    private async Task RunAsync(Func<Task<string>> action)
    {
        if (_busy) return; _busy = true; IsEnabled = false; StatusText.Text = LocalizationService.Current["Working"];
        try { StatusText.Text = await action(); StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush"); }
        catch (Exception exception) { StatusText.Text = exception.Message; StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush"); }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task RunSelectedAsync(Func<string, Task<AdbCommandResult>> action, string success)
    {
        if (Selected is not { } selected) return;
        await RunAsync(async () => { var result = await action(selected.Application.PackageName); if (!result.IsSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim()); return success; });
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (IsInitialized) ApplyFilter(); }
    private void TypeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private async void Launch_Click(object sender, RoutedEventArgs e) => await RunSelectedAsync(package => _applications.LaunchAsync(_device.Serial, package), LocalizationService.Current["ApplicationLaunched"]);
    private async void Stop_Click(object sender, RoutedEventArgs e) { if (Selected is not { } app || !_confirmation.Confirm(LocalizationService.Current["StopApplication"], LocalizationService.Current.Format("ConfirmStopApplication", app.Application.ApplicationName))) return; await RunSelectedAsync(package => _applications.StopAsync(_device.Serial, package), LocalizationService.Current["ApplicationStopped"]); }
    private async void Uninstall_Click(object sender, RoutedEventArgs e) { if (Selected is not { } app || !_confirmation.Confirm(LocalizationService.Current["UninstallApplication"], LocalizationService.Current.Format("ConfirmUninstallApplication", app.Application.ApplicationName))) return; await RunSelectedAsync(package => _applications.UninstallAsync(_device.Serial, package), LocalizationService.Current["ApplicationUninstalled"]); await RefreshAsync(); }
    private async void ClearData_Click(object sender, RoutedEventArgs e) { if (Selected is not { } app || !_confirmation.Confirm(LocalizationService.Current["ClearApplicationData"], LocalizationService.Current.Format("ConfirmClearApplicationData", app.Application.ApplicationName))) return; await RunSelectedAsync(package => _applications.ClearDataAsync(_device.Serial, package), LocalizationService.Current["ApplicationDataCleared"]); }
    private async void Details_Click(object sender, RoutedEventArgs e) => await RunSelectedAsync(package => _applications.OpenDetailsAsync(_device.Serial, package), LocalizationService.Current["ApplicationDetailsOpened"]);
    private void CopyPackage_Click(object sender, RoutedEventArgs e) { if (Selected is { } app) Clipboard.SetText(app.Application.PackageName); }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        var picker = new OpenFileDialog { Filter = "Android packages (*.apk)|*.apk", Title = LocalizationService.Current["ChooseApk"] };
        if (picker.ShowDialog(this) != true || !_confirmation.Confirm(LocalizationService.Current["UpdateApplication"], LocalizationService.Current.Format("ConfirmUpdateApplication", app.Application.ApplicationName))) return;
        await RunAsync(async () => { var result = await _installer.InstallAsync(_device.Serial, picker.FileName); if (!result.IsSuccess) throw new InvalidOperationException(result.StandardError); return LocalizationService.Current["ApplicationUpdated"]; });
        await RefreshAsync();
    }
    private sealed record Row(InstalledApplication Application)
    {
        public string ApplicationName => Application.ApplicationName;
        public string PackageName => Application.PackageName;
        public string VersionLabel => Application.VersionName ?? (Application.VersionCode is { } code ? $"v{code}" : "—");
        public string TypeLabel => LocalizationService.Current[Application.Type == AndroidApplicationType.User ? "UserApplication" : "SystemApplication"];
    }
}
