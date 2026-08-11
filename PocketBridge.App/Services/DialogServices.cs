using System.Windows;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App.Services;

public interface ISettingsDialogService { Task<bool> ShowAsync(); }
public interface IDeviceProfileDialogService { Task<bool> ShowAsync(AndroidDevice device); }
public interface IConfirmationService { bool Confirm(string title, string message); }

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}

public sealed class SettingsDialogService : ISettingsDialogService
{
    private readonly IAppSettingsService _settingsService;
    private readonly IRuntimeToolsService _runtimeTools;
    private readonly IUpdateCheckService _updates;
    public SettingsDialogService(IAppSettingsService settingsService, IRuntimeToolsService runtimeTools, IUpdateCheckService updates)
    {
        _settingsService = settingsService;
        _runtimeTools = runtimeTools;
        _updates = updates;
    }
    public async Task<bool> ShowAsync()
    {
        var dialog = new SettingsWindow(_settingsService.Load(), _runtimeTools, _updates) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is null) return false;
        await _settingsService.SaveAsync(dialog.Result);
        return true;
    }
}

public sealed class DeviceProfileDialogService(IDeviceProfileService profiles) : IDeviceProfileDialogService
{
    public async Task<bool> ShowAsync(AndroidDevice device)
    {
        var dialog = new DeviceProfileWindow(device, profiles.Get(device.Serial)) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is null) return false;
        await profiles.SaveAsync(dialog.Result);
        return true;
    }
}
