using Microsoft.Win32;
using System.IO;
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
    Task<int> QueueDroppedFilesAsync(AndroidDevice device, IReadOnlyList<string> files);
    void ShowTransfers();
    void ShowApplications(AndroidDevice device);
}

public sealed class FeatureDialogService : IFeatureDialogService
{
    private readonly IApkInstallerService _apkInstaller;
    private readonly IWifiAdbService _wifi;
    private readonly IAdbFileService _files;
    private readonly IScreenshotService _screenshots;
    private readonly IConfirmationService _confirmation;
    private readonly IDeviceProfileService _profiles;
    private readonly IFileTransferQueueService _transfers;
    private readonly IApplicationService _applications;
    private TransferQueueWindow? _transferWindow;

    public FeatureDialogService(IApkInstallerService apkInstaller, IWifiAdbService wifi, IAdbFileService files, IScreenshotService screenshots, IConfirmationService confirmation, IDeviceProfileService profiles, IFileTransferQueueService transfers, IApplicationService applications)
    {
        _apkInstaller = apkInstaller; _wifi = wifi; _files = files; _screenshots = screenshots; _confirmation = confirmation; _profiles = profiles; _transfers = transfers; _applications = applications;
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

    public void ShowWifi(AndroidDevice device) => new WifiWindow(device, _wifi, _profiles) { Owner = Application.Current.MainWindow }.ShowDialog();
    public void ShowFiles(AndroidDevice device) => new FileManagerWindow(device, _files, _confirmation) { Owner = Application.Current.MainWindow }.Show();

    public async Task<string?> CaptureScreenshotAsync(AndroidDevice device)
    {
        var picker = new SaveFileDialog { Title = LocalizationService.Current["ChooseScreenshot"], Filter = "PNG image (*.png)|*.png", FileName = $"{device.FriendlyName}_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
        if (picker.ShowDialog() != true) return null;
        await _screenshots.CaptureAsync(device.Serial, picker.FileName);
        return LocalizationService.Current.Format("ScreenshotSaved", picker.FileName);
    }

    public Task<int> QueueDroppedFilesAsync(AndroidDevice device, IReadOnlyList<string> files)
    {
        var existing = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (existing.Length == 0) return Task.FromResult(0);
        var apk = existing.Where(path => Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase)).ToArray();
        var regular = existing.Except(apk, StringComparer.OrdinalIgnoreCase).ToArray();
        var requests = new List<FileTransferRequest>();
        if (apk.Length > 0 && _confirmation.Confirm(LocalizationService.Current["InstallApk"], LocalizationService.Current.Format("ConfirmInstallCount", apk.Length, device.FriendlyName)))
            requests.AddRange(apk.Select(path => new FileTransferRequest(path, "package", device.Serial, FileTransferOperation.InstallApk)));
        if (regular.Length > 0)
        {
            var prompt = new TextPromptWindow(LocalizationService.Current.Format("TransferFilesPrompt", regular.Length, device.FriendlyName), "/sdcard/Download/") { Owner = Application.Current.MainWindow };
            if (prompt.ShowDialog() == true)
            {
                var destination = NormalizeDestination(prompt.Value);
                requests.AddRange(regular.Select(path => new FileTransferRequest(path, $"{destination}{Path.GetFileName(path)}", device.Serial, FileTransferOperation.Upload)));
            }
        }
        var count = _transfers.Enqueue(requests).Count;
        if (count > 0) ShowTransfers();
        return Task.FromResult(count);
    }

    public void ShowTransfers()
    {
        if (_transferWindow is { IsLoaded: true }) { _transferWindow.Activate(); return; }
        _transferWindow = new TransferQueueWindow(_transfers) { Owner = Application.Current.MainWindow };
        _transferWindow.Closed += (_, _) => _transferWindow = null;
        _transferWindow.Show();
    }

    public void ShowApplications(AndroidDevice device) => new ApplicationManagerWindow(device, _applications, _apkInstaller, _confirmation) { Owner = Application.Current.MainWindow }.Show();

    private static string NormalizeDestination(string value)
    {
        var normalized = "/" + string.Join('/', value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!normalized.StartsWith("/sdcard/", StringComparison.Ordinal) && !normalized.StartsWith("/storage/emulated/0/", StringComparison.Ordinal)) throw new InvalidOperationException(LocalizationService.Current["InvalidTransferDestination"]);
        if (normalized.Split('/').Any(segment => segment == "..")) throw new InvalidOperationException(LocalizationService.Current["InvalidTransferDestination"]);
        return normalized.TrimEnd('/') + "/";
    }
}
