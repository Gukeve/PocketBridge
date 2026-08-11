using Microsoft.Win32;
using System.Windows;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App.Services;

public interface IFeatureDialogService
{
    Task<string?> InstallApkAsync(AndroidDevice device, string? apkPath = null);
    void ShowWifi(AndroidDevice device);
    void ShowFiles(AndroidDevice device);
    Task<string?> CaptureScreenshotAsync(AndroidDevice device);
}

public sealed class FeatureDialogService : IFeatureDialogService
{
    private readonly IApkInstallerService _apkInstaller;
    private readonly IWifiAdbService _wifi;
    private readonly IAdbFileService _files;
    private readonly IScreenshotService _screenshots;
    private readonly IConfirmationService _confirmation;

    public FeatureDialogService(IApkInstallerService apkInstaller, IWifiAdbService wifi, IAdbFileService files, IScreenshotService screenshots, IConfirmationService confirmation)
    {
        _apkInstaller = apkInstaller; _wifi = wifi; _files = files; _screenshots = screenshots; _confirmation = confirmation;
    }

    public async Task<string?> InstallApkAsync(AndroidDevice device, string? apkPath = null)
    {
        if (apkPath is null)
        {
            var picker = new OpenFileDialog { Title = LocalizationService.Current["ChooseApk"], Filter = "Android packages (*.apk)|*.apk", Multiselect = false };
            if (picker.ShowDialog() != true) return null;
            apkPath = picker.FileName;
        }
        if (!_confirmation.Confirm(LocalizationService.Current["InstallApk"], LocalizationService.Current.Format("ConfirmInstall", device.FriendlyName))) return null;
        var result = await _apkInstaller.InstallAsync(device.Serial, apkPath);
        if (!result.IsSuccess || !result.StandardOutput.Contains("Success", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
        return LocalizationService.Current.Format("ApkInstalledOn", device.FriendlyName);
    }

    public void ShowWifi(AndroidDevice device) => new WifiWindow(device, _wifi) { Owner = Application.Current.MainWindow }.ShowDialog();
    public void ShowFiles(AndroidDevice device) => new FileManagerWindow(device, _files, _confirmation) { Owner = Application.Current.MainWindow }.Show();

    public async Task<string?> CaptureScreenshotAsync(AndroidDevice device)
    {
        var picker = new SaveFileDialog { Title = LocalizationService.Current["ChooseScreenshot"], Filter = "PNG image (*.png)|*.png", FileName = $"{device.FriendlyName}_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
        if (picker.ShowDialog() != true) return null;
        await _screenshots.CaptureAsync(device.Serial, picker.FileName);
        return LocalizationService.Current.Format("ScreenshotSaved", picker.FileName);
    }
}
