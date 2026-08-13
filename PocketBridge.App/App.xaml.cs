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
    private IFileTransferQueueService? _transfers;
    private IRecordingService? _recordings;
    private IAudioForwardingService? _audio;
    private IGamepadService? _gamepads; private IVirtualDisplayService? _virtualDisplays; private ILanServerService? _lan;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IExecutableLocator locator = new ExecutableLocator();
        IAppSettingsService settings = new JsonAppSettingsService();
        LocalizationService.Initialize(settings.Load().Language);
        IRuntimeToolsService runtimeTools = new RuntimeToolsService();
        IAdbService adb = new AdbService(locator, settings);
        IDeviceProfileService profiles = new DeviceProfileService(settings);
        IFileTransferQueueService transfers = new FileTransferQueueService(new AdbTransferExecutor(locator, settings));
        _transfers = transfers;
        IScrcpyService scrcpy = new ScrcpyService(locator, settings);
        IRecordingService recordings = new RecordingService(locator, settings);
        IAudioForwardingService audio = new AudioForwardingService(adb, locator, settings);
        IApplicationIconService applicationIcons = new ApplicationIconService(adb, new AppIconCache());
        IDeviceMediaCapabilityService mediaCapabilities = new DeviceMediaCapabilityService(audio, locator, settings);
        IAuditLogService audit = new AuditLogService(); IInputMappingService inputMappings = new InputMappingService(settings); IGamepadService gamepads = new XInputGamepadService(); IP3CapabilityService p3Capabilities = new P3CapabilityService(adb, locator, settings); IInputBackendService inputBackend = new InputBackendService(locator, settings); IVirtualDisplayService virtualDisplays = new VirtualDisplayService(locator, settings); ILanServerService lan = new LanServerService(audit); IRemoteSessionService remoteSessions = new RemoteSessionService(); IAutomationService automation = new AutomationService(settings, audit);
        _gamepads = gamepads; _virtualDisplays = virtualDisplays; _lan = lan;
        _recordings = recordings;
        IDeviceDisplaySessionFactory displaySessionFactory = new DeviceDisplaySessionFactory(scrcpy, adb, locator, settings);
        IEmbeddedSessionManager embeddedSessions = new EmbeddedSessionManager(displaySessionFactory);
        IUpdateCheckService updates = new UpdateCheckService(locator, settings, runtimeTools, Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        var confirmation = new MessageBoxConfirmationService();
        var featureDialogs = new FeatureDialogService(
            new ApkInstallerService(adb),
            new WifiAdbService(adb),
            new AdbFileService(adb),
            new ScreenshotService(adb),
            confirmation,
            profiles,
            transfers,
            new ApplicationService(adb), applicationIcons,
            settings,
            new DeviceInformationService(adb),
            new AdbConsoleService(locator, settings), inputMappings, p3Capabilities, gamepads, inputBackend, virtualDisplays, lan, remoteSessions, automation);
        var viewModel = new MainViewModel(adb, scrcpy, embeddedSessions, locator, settings, runtimeTools, profiles, new SettingsDialogService(settings, runtimeTools, updates), new DeviceProfileDialogService(profiles), confirmation, featureDialogs, recordings, new GroupActionService(adb), audio, mediaCapabilities, lan);
        _audio = audio;
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _transfers?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _recordings?.Dispose();
        _audio?.Dispose();
        _gamepads?.Dispose(); _virtualDisplays?.Dispose(); _lan?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
