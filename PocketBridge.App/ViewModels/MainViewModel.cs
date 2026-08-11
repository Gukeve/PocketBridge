using System.Collections.ObjectModel;
using System.Windows.Media;
using PocketBridge.App.Services;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAdbService _adb;
    private readonly IScrcpyService _scrcpy;
    private readonly IEmbeddedSessionManager _embeddedSessions;
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    private readonly IRuntimeToolsService _runtimeTools;
    private readonly ISettingsDialogService _settingsDialog;
    private readonly IConfirmationService _confirmation;
    private readonly IFeatureDialogService _featureDialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private DeviceItemViewModel? _selectedDevice;
    private bool _isBusy;
    private bool _adbAvailable;
    private string _statusMessage = LocalizationService.Current["StatusRefreshing"];
    private StatusKind _statusKind = StatusKind.Neutral;

    public MainViewModel(
        IAdbService adb,
        IScrcpyService scrcpy,
        IEmbeddedSessionManager embeddedSessions,
        IExecutableLocator locator,
        IAppSettingsService settings,
        IRuntimeToolsService runtimeTools,
        ISettingsDialogService settingsDialog,
        IConfirmationService confirmation,
        IFeatureDialogService featureDialogs)
    {
        _adb = adb;
        _scrcpy = scrcpy;
        _embeddedSessions = embeddedSessions;
        _locator = locator;
        _settings = settings;
        _runtimeTools = runtimeTools;
        _settingsDialog = settingsDialog;
        _confirmation = confirmation;
        _featureDialogs = featureDialogs;
        _uiContext = SynchronizationContext.Current;
        _scrcpy.SessionChanged += OnSessionChanged;
        _embeddedSessions.SessionsChanged += OnEmbeddedSessionsChanged;

        RefreshCommand = new AsyncRelayCommand(() => RefreshDevicesAsync(true), () => !IsBusy);
        PrepareToolsCommand = new AsyncRelayCommand(PrepareToolsAsync, () => !IsBusy);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync, () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        OpenExternalCommand = new AsyncRelayCommand(OpenExternalAsync, CanOpenExternal);
        StopCommand = new AsyncRelayCommand(StopAsync, CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, CanRestart);
        BackCommand = DeviceCommand("StatusBack", "shell", "input", "keyevent", "KEYCODE_BACK");
        HomeCommand = DeviceCommand("StatusHome", "shell", "input", "keyevent", "KEYCODE_HOME");
        RecentsCommand = DeviceCommand("StatusRecents", "shell", "input", "keyevent", "KEYCODE_APP_SWITCH");
        VolumeUpCommand = DeviceCommand("StatusVolumeUp", "shell", "input", "keyevent", "KEYCODE_VOLUME_UP");
        VolumeDownCommand = DeviceCommand("StatusVolumeDown", "shell", "input", "keyevent", "KEYCODE_VOLUME_DOWN");
        PowerCommand = DeviceCommand("StatusPower", "shell", "input", "keyevent", "KEYCODE_POWER");
        RebootCommand = new AsyncRelayCommand(RebootAsync, CanControl);
        OpenFilesCommand = new RelayCommand(OpenFiles, CanControl);
        InstallApkCommand = new AsyncRelayCommand(InstallApkAsync, CanControl);
        WifiCommand = new AsyncRelayCommand(OpenWifiAsync, CanControl);
        ScreenshotCommand = new AsyncRelayCommand(CaptureScreenshotAsync, CanControl);
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PrepareToolsCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand OpenExternalCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand HomeCommand { get; }
    public AsyncRelayCommand RecentsCommand { get; }
    public AsyncRelayCommand VolumeUpCommand { get; }
    public AsyncRelayCommand VolumeDownCommand { get; }
    public AsyncRelayCommand PowerCommand { get; }
    public AsyncRelayCommand RebootCommand { get; }
    public RelayCommand OpenFilesCommand { get; }
    public AsyncRelayCommand InstallApkCommand { get; }
    public AsyncRelayCommand WifiCommand { get; }
    public AsyncRelayCommand ScreenshotCommand { get; }

    public DeviceItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowSelectionPrompt));
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            UpdateSelectionMessage();
            NotifyCommands();
        }
    }

    public int DeviceCount => Devices.Count;
    public string DeviceCountText => LocalizationService.Current.Format("DeviceCountFormat", DeviceCount);
    public bool HasSelection => SelectedDevice is not null;
    public IEmbeddedDisplaySession? SelectedEmbeddedSession => SelectedDevice is null ? null : _embeddedSessions.Get(SelectedDevice.Serial);
    public bool ToolsAvailable => FindTool("adb.exe") is not null && FindTool("scrcpy.exe") is not null;
    public bool ShowToolsMissingState => !ToolsAvailable && !IsBusy;
    public bool ShowEmptyState => ToolsAvailable && Devices.Count == 0 && !IsBusy;
    public bool ShowSelectionPrompt => ToolsAvailable && Devices.Count > 0 && !HasSelection;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(ShowToolsMissingState)); NotifyCommands(); } } }
    public string AdbStatusText => LocalizationService.Current[_adbAvailable ? "AdbReady" : "AdbUnavailable"];
    public Brush AdbStatusBrush => Brush(_adbAvailable ? 88 : 242, _adbAvailable ? 214 : 120, _adbAvailable ? 168 : 120);
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public Brush StatusBrush => _statusKind switch
    {
        StatusKind.Success => Brush(88, 214, 168),
        StatusKind.Warning => Brush(243, 191, 99),
        StatusKind.Error => Brush(242, 120, 120),
        _ => Brush(170, 180, 197)
    };
    public string SessionSummary => LocalizationService.Current.Format("ActiveSessionsFormat", _embeddedSessions.Sessions.Count(session => session.IsRunning) + _scrcpy.GetActiveSessions().Count);

    public async Task InitializeAsync()
    {
        await RefreshDevicesAsync(false);
    }

    private async Task RefreshDevicesAsync(bool announce)
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            if (announce) SetStatus(LocalizationService.Current["StatusRefreshing"], StatusKind.Neutral);
            UpdateToolsProperties();
            if (!ToolsAvailable)
            {
                Devices.Clear();
                SelectedDevice = null;
                _adbAvailable = false;
                OnPropertyChanged(nameof(AdbStatusText));
                OnPropertyChanged(nameof(AdbStatusBrush));
                SetStatus(LocalizationService.Current["StatusRuntimeMissing"], StatusKind.Warning);
                UpdateDeviceProperties();
                return;
            }
            _adbAvailable = await _adb.IsAvailableAsync(_lifetime.Token);
            OnPropertyChanged(nameof(AdbStatusText));
            OnPropertyChanged(nameof(AdbStatusBrush));
            if (!_adbAvailable)
            {
                Devices.Clear();
                SelectedDevice = null;
                SetStatus(LocalizationService.Current["StatusAdbBroken"], StatusKind.Error);
                UpdateDeviceProperties();
                return;
            }

            var devices = await _adb.GetDevicesAsync(_lifetime.Token);
            var selectedSerial = SelectedDevice?.Serial;
            var incomingSerials = devices.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in Devices.Where(item => !incomingSerials.Contains(item.Serial)).ToArray())
            {
                Devices.Remove(removed);
            }

            foreach (var device in devices)
            {
                var existing = Devices.FirstOrDefault(item => item.Serial == device.Serial);
                if (existing is null)
                {
                    Devices.Add(new DeviceItemViewModel(device, IsAnySessionRunning(device.Serial)));
                }
                else
                {
                    existing.Update(device, IsAnySessionRunning(device.Serial));
                }
            }

            if (selectedSerial is not null && !incomingSerials.Contains(selectedSerial)) SelectedDevice = null;
            if (Devices.Count == 1 && SelectedDevice is null) SelectedDevice = Devices[0];
            UpdateDeviceProperties();
            if (announce || devices.Count == 0)
            {
                SetStatus(devices.Count == 0 ? LocalizationService.Current["StatusNoDevices"] : LocalizationService.Current.Format("StatusFoundDevices", devices.Count), devices.Count == 0 ? StatusKind.Warning : StatusKind.Success);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _adbAvailable = false;
            OnPropertyChanged(nameof(AdbStatusText));
            OnPropertyChanged(nameof(AdbStatusBrush));
            SetStatus(exception.Message, StatusKind.Error);
        }
        finally
        {
            IsBusy = false;
            _refreshGate.Release();
        }
    }

    private async Task ConnectAsync()
    {
        var selected = SelectedDevice!;
        try
        {
            SetStatus(LocalizationService.Current.Format("StatusStarting", selected.FriendlyName), StatusKind.Neutral);
            await _embeddedSessions.StartAsync(selected.Device, _lifetime.Token);
            selected.IsSessionRunning = true;
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            SetStatus(LocalizationService.Current.Format("StatusSessionStarted", selected.FriendlyName), StatusKind.Success);
            SessionStateChanged();
        }
        catch (Exception exception) { SetStatus(exception.Message, StatusKind.Error); }
    }

    private async Task StopAsync()
    {
        var selected = SelectedDevice!;
        try
        {
            await _embeddedSessions.StopAsync(selected.Serial);
            selected.IsSessionRunning = _scrcpy.IsRunning(selected.Serial);
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            SetStatus(LocalizationService.Current.Format("StatusSessionStopped", selected.FriendlyName), StatusKind.Success);
            SessionStateChanged();
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("StatusStopFailed", exception.Message), StatusKind.Error); }
    }

    private async Task RestartAsync()
    {
        var selected = SelectedDevice!;
        try
        {
            await _embeddedSessions.StopAsync(selected.Serial);
            await _embeddedSessions.StartAsync(selected.Device, _lifetime.Token);
            selected.IsSessionRunning = true;
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            SetStatus(LocalizationService.Current.Format("StatusSessionRestarted", selected.FriendlyName), StatusKind.Success);
            SessionStateChanged();
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("StatusRestartFailed", exception.Message), StatusKind.Error); }
    }

    private async Task OpenExternalAsync()
    {
        var selected = SelectedDevice!;
        try
        {
            await _scrcpy.StartAsync(selected.Device, new ScrcpyLaunchOptions
            {
                WindowTitle = $"PocketBridge — {selected.FriendlyName} — {selected.Serial}",
                StayAwake = true
            });
            selected.IsSessionRunning = true;
            SetStatus(LocalizationService.Current.Format("StatusSessionStarted", selected.FriendlyName), StatusKind.Success);
            SessionStateChanged();
        }
        catch (Exception exception) { SetStatus(exception.Message, StatusKind.Error); }
    }

    private void OpenFiles()
    {
        var selected = SelectedDevice;
        if (selected is not null) _featureDialogs.ShowFiles(selected.Device);
    }

    private async Task OpenWifiAsync()
    {
        var selected = SelectedDevice;
        if (selected is null) return;
        _featureDialogs.ShowWifi(selected.Device);
        await RefreshDevicesAsync(true);
    }

    private async Task InstallApkAsync() => await InstallApkAsync(null);

    public async Task InstallDroppedApkAsync(string apkPath) => await InstallApkAsync(apkPath);

    private async Task InstallApkAsync(string? apkPath)
    {
        var selected = SelectedDevice;
        if (selected?.Device.IsReady != true) return;
        try
        {
            SetStatus(LocalizationService.Current["Installing"], StatusKind.Neutral);
            var message = await _featureDialogs.InstallApkAsync(selected.Device, apkPath);
            if (message is not null) SetStatus(message, StatusKind.Success);
            else UpdateSelectionMessage();
        }
        catch (Exception exception)
        {
            SetStatus(LocalizationService.Current.Format("InstallFailed", exception.Message), StatusKind.Error);
        }
    }

    private async Task CaptureScreenshotAsync()
    {
        var selected = SelectedDevice;
        if (selected?.Device.IsReady != true) return;
        try
        {
            var message = await _featureDialogs.CaptureScreenshotAsync(selected.Device);
            if (message is not null) SetStatus(message, StatusKind.Success);
        }
        catch (Exception exception)
        {
            SetStatus(LocalizationService.Current.Format("ScreenshotFailed", exception.Message), StatusKind.Error);
        }
    }

    private AsyncRelayCommand DeviceCommand(string successMessageKey, params string[] arguments) =>
        new(() => ExecuteDeviceCommandAsync(LocalizationService.Current[successMessageKey], arguments), CanControl);

    private async Task ExecuteDeviceCommandAsync(string successMessage, string[] arguments)
    {
        try
        {
            var result = await _adb.ExecuteAsync(SelectedDevice!.Serial, arguments);
            SetStatus(result.IsSuccess ? successMessage : HumanizeCommandFailure(result), result.IsSuccess ? StatusKind.Success : StatusKind.Error);
        }
        catch (Exception exception) { SetStatus(exception.Message, StatusKind.Error); }
    }

    private async Task RebootAsync()
    {
        var selected = SelectedDevice!;
        if (!_confirmation.Confirm(LocalizationService.Current["RebootTitle"], LocalizationService.Current.Format("RebootConfirm", selected.FriendlyName))) return;
        await ExecuteDeviceCommandAsync(LocalizationService.Current["StatusRebootSent"], new[] { "reboot" });
    }

    private async Task OpenSettingsAsync()
    {
        if (!await _settingsDialog.ShowAsync()) return;
        SetStatus(LocalizationService.Current["StatusSettingsSaved"], StatusKind.Neutral);
        await RefreshDevicesAsync(true);
    }

    private async Task PrepareToolsAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(LocalizationService.Current["StatusRuntimeDownloading"], StatusKind.Neutral);
            var result = await _runtimeTools.DownloadLatestAsync(_lifetime.Token);
            UpdateToolsProperties();
            SetStatus(LocalizationService.Current.Format("StatusRuntimeReady", result.Version, result.FileCount), StatusKind.Success);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            SetStatus(LocalizationService.Current.Format("StatusRuntimeFailed", exception.Message), StatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }

        if (ToolsAvailable) await RefreshDevicesAsync(true);
    }

    private void UpdateSelectionMessage()
    {
        if (SelectedDevice is null) return;
        switch (SelectedDevice.Device.State)
        {
            case AndroidDeviceState.Unauthorized:
                SetStatus(LocalizationService.Current["StatusUnauthorized"], StatusKind.Warning);
                break;
            case AndroidDeviceState.Offline:
                SetStatus(LocalizationService.Current["StatusOffline"], StatusKind.Error);
                break;
            case AndroidDeviceState.Device:
                SetStatus(LocalizationService.Current[SelectedDevice.IsSessionRunning ? "StatusSessionAlready" : "StatusReady"], StatusKind.Success);
                break;
            default:
                SetStatus(LocalizationService.Current["StatusUnknownState"], StatusKind.Warning);
                break;
        }
    }

    private void OnSessionChanged(object? sender, DeviceSession session)
    {
        void Update()
        {
            var item = Devices.FirstOrDefault(device => device.Serial == session.Serial);
            if (item is not null) item.IsSessionRunning = session.IsRunning || _embeddedSessions.Get(session.Serial)?.IsRunning == true;
            if (!session.IsRunning) SetStatus(session.ExitCode is 0 or null ? LocalizationService.Current.Format("StatusSessionEnded", session.Serial) : LocalizationService.Current.Format("StatusScrcpyExit", session.Serial, session.ExitCode), session.ExitCode is 0 or null ? StatusKind.Neutral : StatusKind.Error);
            SessionStateChanged();
        }
        if (_uiContext is null) Update(); else _uiContext.Post(_ => Update(), null);
    }

    private void OnEmbeddedSessionsChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            foreach (var item in Devices) item.IsSessionRunning = IsAnySessionRunning(item.Serial);
            if (SelectedEmbeddedSession is { IsRunning: false, UnavailableReason: { Length: > 0 } reason }) SetStatus(reason, StatusKind.Error);
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            SessionStateChanged();
        }
        if (_uiContext is null) Update(); else _uiContext.Post(_ => Update(), null);
    }

    private void SessionStateChanged()
    {
        OnPropertyChanged(nameof(SessionSummary));
        NotifyCommands();
    }

    private void UpdateDeviceProperties()
    {
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSelectionPrompt));
    }

    private string? FindTool(string executableName) => _locator.Find(executableName, _settings.Load().ToolsDirectory);

    private void UpdateToolsProperties()
    {
        OnPropertyChanged(nameof(ToolsAvailable));
        OnPropertyChanged(nameof(ShowToolsMissingState));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSelectionPrompt));
    }

    private bool CanConnect() => SelectedDevice?.Device.IsReady == true && _embeddedSessions.Get(SelectedDevice.Serial)?.IsRunning != true && !IsBusy;
    private bool CanOpenExternal() => SelectedDevice?.Device.IsReady == true && !_scrcpy.IsRunning(SelectedDevice.Serial) && !IsBusy;
    private bool CanStop() => SelectedDevice is not null && _embeddedSessions.Get(SelectedDevice.Serial)?.IsRunning == true && !IsBusy;
    private bool CanRestart() => SelectedDevice?.Device.IsReady == true && _embeddedSessions.Get(SelectedDevice.Serial)?.IsRunning == true && !IsBusy;
    private bool CanControl() => SelectedDevice?.Device.IsReady == true && !IsBusy;

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); PrepareToolsCommand.NotifyCanExecuteChanged(); OpenSettingsCommand.NotifyCanExecuteChanged(); ConnectCommand.NotifyCanExecuteChanged(); OpenExternalCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); RestartCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged(); HomeCommand.NotifyCanExecuteChanged(); RecentsCommand.NotifyCanExecuteChanged(); VolumeUpCommand.NotifyCanExecuteChanged(); VolumeDownCommand.NotifyCanExecuteChanged(); PowerCommand.NotifyCanExecuteChanged(); RebootCommand.NotifyCanExecuteChanged(); OpenFilesCommand.NotifyCanExecuteChanged(); InstallApkCommand.NotifyCanExecuteChanged(); WifiCommand.NotifyCanExecuteChanged(); ScreenshotCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        _statusKind = kind;
        OnPropertyChanged(nameof(StatusBrush));
    }

    private static string HumanizeCommandFailure(AdbCommandResult result) => string.IsNullOrWhiteSpace(result.StandardError) ? LocalizationService.Current["StatusCommandFailed"] : result.StandardError.Trim();
    private static SolidColorBrush Brush(int red, int green, int blue) => new(Color.FromRgb((byte)red, (byte)green, (byte)blue));

    public void Dispose()
    {
        _scrcpy.SessionChanged -= OnSessionChanged;
        _embeddedSessions.SessionsChanged -= OnEmbeddedSessionsChanged;
        _lifetime.Cancel();
        _embeddedSessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _lifetime.Dispose();
    }

    private bool IsAnySessionRunning(string serial) => _embeddedSessions.Get(serial)?.IsRunning == true || _scrcpy.IsRunning(serial);

    private enum StatusKind { Neutral, Success, Warning, Error }
}
