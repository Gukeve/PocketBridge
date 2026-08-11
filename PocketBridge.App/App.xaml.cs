using System.Windows;
using PocketBridge.App.Services;
using PocketBridge.App.ViewModels;
using PocketBridge.App.Localization;
using PocketBridge.Core.Services;
using PocketBridge.Infrastructure;
using PocketBridge.Infrastructure.Embedded;
using System.Reflection;

namespace PocketBridge.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IExecutableLocator locator = new ExecutableLocator();
        IAppSettingsService settings = new JsonAppSettingsService();
        LocalizationService.Initialize(settings.Load().Language);
        IRuntimeToolsService runtimeTools = new RuntimeToolsService();
        IAdbService adb = new AdbService(locator, settings);
        IScrcpyService scrcpy = new ScrcpyService(locator, settings);
        IDeviceDisplaySessionFactory displaySessionFactory = new DeviceDisplaySessionFactory(scrcpy, adb, locator, settings);
        IEmbeddedSessionManager embeddedSessions = new EmbeddedSessionManager(displaySessionFactory);
        IUpdateCheckService updates = new UpdateCheckService(locator, settings, runtimeTools, Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        var confirmation = new MessageBoxConfirmationService();
        var featureDialogs = new FeatureDialogService(
            new ApkInstallerService(adb),
            new WifiAdbService(adb),
            new AdbFileService(adb),
            new ScreenshotService(adb),
            confirmation);
        var viewModel = new MainViewModel(adb, scrcpy, embeddedSessions, locator, settings, runtimeTools, new SettingsDialogService(settings, runtimeTools, updates), confirmation, featureDialogs);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync();
    }
}
