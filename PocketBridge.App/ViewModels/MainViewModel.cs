using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PocketBridge.App.Services;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;
using PocketBridge.Infrastructure;

namespace PocketBridge.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAdbService _adb;
    private readonly IScrcpyService _scrcpy;
    private readonly IEmbeddedSessionManager _embeddedSessions;
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    private readonly IRuntimeToolsService _runtimeTools;
    private readonly IDeviceProfileService _profiles;
    private readonly ISettingsDialogService _settingsDialog;
    private readonly IDeviceProfileDialogService _profileDialog;
    private readonly IConfirmationService _confirmation;
    private readonly IFeatureDialogService _featureDialogs;
    private readonly IRecordingService _recordings;
    private readonly IGroupActionService _groupActions;
    private readonly IAudioForwardingService _audio;
    private readonly IDeviceMediaCapabilityService _mediaCapabilityService;
    private readonly ILanServerService _lan;
    private readonly IInputMappingService _inputMappings;
    private readonly IAutomationService _automation;
    private readonly IGamepadService _gamepads;
    private HashSet<string> _knownDeviceSerials = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private readonly DispatcherTimer _clipboardTimer;
    private readonly DispatcherTimer _recordingTimer;
    private readonly Dictionary<string, ClipboardSyncTracker> _clipboardTrackers = new(StringComparer.Ordinal);
    private readonly HashSet<IEmbeddedDisplaySession> _clipboardSessions = new();
    private long _clipboardSequence;
    private DeviceItemViewModel? _selectedDevice;
    private bool _isBusy;
    private bool _isMultiView;
    private bool _adbAvailable;
    private string _statusMessage = LocalizationService.Current["StatusRefreshing"];
    private StatusKind _statusKind = StatusKind.Neutral;
    private string _audioStatus = "Audio: not checked";
    private bool _audioSupported;
    private DeviceMediaCapabilities? _mediaCapabilities;
    private bool _isMappingEditMode;

    public MainViewModel(
        IAdbService adb,
        IScrcpyService scrcpy,
        IEmbeddedSessionManager embeddedSessions,
        IExecutableLocator locator,
        IAppSettingsService settings,
        IRuntimeToolsService runtimeTools,
        IDeviceProfileService profiles,
        ISettingsDialogService settingsDialog,
        IDeviceProfileDialogService profileDialog,
        IConfirmationService confirmation,
        IFeatureDialogService featureDialogs,
        IRecordingService recordings,
        IGroupActionService groupActions,
        IAudioForwardingService audio,
        IDeviceMediaCapabilityService mediaCapabilityService,
        ILanServerService lan,
        IInputMappingService inputMappings,
        IAutomationService automation,
        IGamepadService gamepads)
    {
        _adb = adb;
        _scrcpy = scrcpy;
        _embeddedSessions = embeddedSessions;
        _locator = locator;
        _settings = settings;
        _runtimeTools = runtimeTools;
        _profiles = profiles;
        _settingsDialog = settingsDialog;
        _profileDialog = profileDialog;
        _confirmation = confirmation;
        _featureDialogs = featureDialogs;
        _recordings = recordings;
        _groupActions = groupActions;
        _audio = audio;
        _mediaCapabilityService = mediaCapabilityService;
        _lan = lan;
        _inputMappings = inputMappings;
        _automation = automation;
        _gamepads = gamepads; _gamepads.StateChanged += OnGamepadStateChanged;
        _uiContext = SynchronizationContext.Current;
        _scrcpy.SessionChanged += OnSessionChanged;
        _embeddedSessions.SessionsChanged += OnEmbeddedSessionsChanged;
        _lan.ControlRequested += OnRemoteControlRequested;

        RefreshCommand = new AsyncRelayCommand(() => RefreshDevicesAsync(true), () => !IsBusy);
        PrepareToolsCommand = new AsyncRelayCommand(PrepareToolsAsync, () => !IsBusy);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync, () => !IsBusy);
        ConfigureProfileCommand = new AsyncRelayCommand(ConfigureProfileAsync, CanControl);
        ShowSingleViewCommand = new RelayCommand(ShowSingleView);
        StartMultiViewCommand = new AsyncRelayCommand(StartMultiViewAsync, CanStartMultiView);
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
        OpenTransfersCommand = new RelayCommand(_featureDialogs.ShowTransfers);
        OpenApplicationsCommand = new RelayCommand(OpenApplications, CanControl);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanControl);
        OpenDeviceInformationCommand = new RelayCommand(OpenDeviceInformation, CanControl);
        OpenAdbConsoleCommand = new RelayCommand(OpenAdbConsole, CanControl);
        OpenInputMappingCommand = new RelayCommand(_featureDialogs.ShowInputMapping);
        OpenExperimentalCommand = new RelayCommand(() => { if (SelectedDevice is { } selected) _featureDialogs.ShowExperimental(selected.Device); }, CanControl);
        ToggleMappingEditCommand = new RelayCommand(() => IsMappingEditMode = !IsMappingEditMode, CanControl);
        SendClipboardCommand = new AsyncRelayCommand(SendClipboardToDeviceAsync, CanUseClipboard);
        CopyDeviceClipboardCommand = new AsyncRelayCommand(CopyDeviceClipboardAsync, CanUseClipboard);
        GroupHomeCommand = GroupCommand(GroupAction.Home);
        GroupBackCommand = GroupCommand(GroupAction.Back);
        GroupRecentsCommand = GroupCommand(GroupAction.Recents);
        GroupVolumeUpCommand = GroupCommand(GroupAction.VolumeUp);
        GroupVolumeDownCommand = GroupCommand(GroupAction.VolumeDown);
        GroupPowerCommand = GroupCommand(GroupAction.Power);
        GroupRebootCommand = new AsyncRelayCommand(GroupRebootAsync, CanRunGroupAction);
        GroupScreenshotCommand = new AsyncRelayCommand(() => ExecuteGroupFeatureAsync(_featureDialogs.CaptureScreenshotsAsync), CanRunGroupAction);
        GroupSendFilesCommand = new AsyncRelayCommand(() => ExecuteGroupFeatureAsync(_featureDialogs.QueueFilesForDevicesAsync), CanRunGroupAction);
        GroupInstallApkCommand = new AsyncRelayCommand(() => ExecuteGroupFeatureAsync(_featureDialogs.InstallApkForDevicesAsync), CanRunGroupAction);
        ToggleAudioCommand = new AsyncRelayCommand(ToggleAudioAsync, () => CanControl() && _audioSupported);
        _clipboardTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(750) };
        _clipboardTimer.Tick += ClipboardTimer_Tick;
        _clipboardTimer.Start();
        _recordingTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _recordingTimer.Tick += (_, _) => OnPropertyChanged(nameof(RecordingText));
        _recordingTimer.Start();
        _recordings.Changed += OnRecordingChanged;
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PrepareToolsCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand ConfigureProfileCommand { get; }
    public RelayCommand ShowSingleViewCommand { get; }
    public AsyncRelayCommand StartMultiViewCommand { get; }
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
    public RelayCommand OpenTransfersCommand { get; }
    public RelayCommand OpenApplicationsCommand { get; }
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public RelayCommand OpenDeviceInformationCommand { get; }
    public RelayCommand OpenAdbConsoleCommand { get; }
    public RelayCommand OpenInputMappingCommand { get; }
    public RelayCommand OpenExperimentalCommand { get; }
    public RelayCommand ToggleMappingEditCommand { get; }
    public AsyncRelayCommand SendClipboardCommand { get; }
    public AsyncRelayCommand CopyDeviceClipboardCommand { get; }
    public AsyncRelayCommand GroupHomeCommand { get; }
    public AsyncRelayCommand GroupBackCommand { get; }
    public AsyncRelayCommand GroupScreenshotCommand { get; }
    public AsyncRelayCommand GroupSendFilesCommand { get; }
    public AsyncRelayCommand GroupInstallApkCommand { get; }
    public AsyncRelayCommand ToggleAudioCommand { get; }
    public AsyncRelayCommand GroupRecentsCommand { get; }
    public AsyncRelayCommand GroupVolumeUpCommand { get; }
    public AsyncRelayCommand GroupVolumeDownCommand { get; }
    public AsyncRelayCommand GroupPowerCommand { get; }
    public AsyncRelayCommand GroupRebootCommand { get; }
    public int GroupTargetCount => Devices.Count(device => device.IsGroupSelected && device.Device.IsReady);
    public string GroupTargetCountText => LocalizationService.Current.Format("GroupTargetsFormat", GroupTargetCount);

    public ShortcutAction? MatchShortcut(string gesture)
    {
        var scopes = new[] { IsMultiView ? ShortcutScope.MultiView : ShortcutScope.EmbeddedView, ShortcutScope.SelectedDevice, ShortcutScope.Global };
        return _settings.Load().ShortcutBindings.FirstOrDefault(binding => scopes.Contains(binding.Scope) && string.Equals(binding.Gesture, gesture, StringComparison.OrdinalIgnoreCase))?.Action;
    }

    public void ExecuteShortcut(ShortcutAction action)
    {
        var command = action switch
        {
            ShortcutAction.Home => HomeCommand,
            ShortcutAction.Back => BackCommand,
            ShortcutAction.Recents => RecentsCommand,
            ShortcutAction.Screenshot => ScreenshotCommand,
            ShortcutAction.ToggleRecording => ToggleRecordingCommand,
            ShortcutAction.SendClipboard => SendClipboardCommand,
            ShortcutAction.RefreshDevices => RefreshCommand,
            _ => null
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
        if (action is ShortcutAction.NextDevice or ShortcutAction.PreviousDevice && Devices.Count > 0)
        {
            var current = Math.Max(0, Devices.IndexOf(SelectedDevice!));
            var delta = action == ShortcutAction.NextDevice ? 1 : -1;
            SelectedDevice = Devices[(current + delta + Devices.Count) % Devices.Count];
        }
    }

    public DeviceItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowSelectionPrompt));
            OnPropertyChanged(nameof(SelectedEmbeddedSession));
            OnPropertyChanged(nameof(SelectedClipboardMode));
            OnPropertyChanged(nameof(ShowSingleDeviceContent));
            OnPropertyChanged(nameof(ActiveInputProfile));
            UpdateSelectionMessage();
            NotifyCommands();
            _ = RefreshAudioCapabilityAsync();
        }
    }

    public ClipboardSyncMode SelectedClipboardMode => SelectedDevice is null ? ClipboardSyncMode.Off : _profiles.Get(SelectedDevice.Serial).ClipboardMode;

    public int DeviceCount => Devices.Count;
    public string DeviceCountText => LocalizationService.Current.Format("DeviceCountFormat", DeviceCount);
    public bool HasSelection => SelectedDevice is not null;
    public IEmbeddedDisplaySession? SelectedEmbeddedSession => SelectedDevice is null ? null : _embeddedSessions.Get(SelectedDevice.Serial);
    public IReadOnlyList<IEmbeddedDisplaySession> ActiveEmbeddedSessions => _embeddedSessions.Sessions.Where(session => session.IsRunning).Take(4).ToArray();
    public bool IsMultiView
    {
        get => _isMultiView;
        private set
        {
            if (!SetProperty(ref _isMultiView, value)) return;
            OnPropertyChanged(nameof(IsSingleView));
            OnPropertyChanged(nameof(ShowSingleDeviceContent));
            OnPropertyChanged(nameof(ShowSelectionPrompt));
        }
    }
    public bool IsSingleView => !IsMultiView;
    public bool ShowSingleDeviceContent => IsSingleView && HasSelection;
    public int MultiViewColumns => ActiveEmbeddedSessions.Count <= 1 ? 1 : 2;
    public int MultiViewRows => ActiveEmbeddedSessions.Count <= 2 ? 1 : 2;
    public bool ToolsAvailable => FindTool("adb.exe") is not null && FindTool("scrcpy.exe") is not null;
    public bool ShowToolsMissingState => !ToolsAvailable && !IsBusy;
    public bool ShowEmptyState => ToolsAvailable && Devices.Count == 0 && !IsBusy;
    public bool ShowSelectionPrompt => IsSingleView && ToolsAvailable && Devices.Count > 0 && !HasSelection;
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
    public bool IsRecording => SelectedDevice is not null && _recordings.Get(SelectedDevice.Serial)?.IsRunning == true;
    public string RecordingText => IsRecording
        ? $"● REC {(DateTimeOffset.Now - _recordings.Get(SelectedDevice!.Serial)!.StartedAt):hh\\:mm\\:ss}"
        : LocalizationService.Current["Record"];

    public string AudioStatus { get => _audioStatus; private set => SetProperty(ref _audioStatus, value); }
    public string AudioButtonText => SelectedDevice is { } selected && _audio.IsRunning(selected.Serial) ? LocalizationService.Current["StopAudio"] : LocalizationService.Current["StartAudio"];
    public bool IsMappingEditMode { get => _isMappingEditMode; set => SetProperty(ref _isMappingEditMode, value); }
    public InputProfile ActiveInputProfile => _inputMappings.Profiles.FirstOrDefault(profile => profile.TargetAlias == SelectedDevice?.FriendlyName) ?? _inputMappings.Profiles.FirstOrDefault() ?? InputProfile.Default;
    public async Task AddMappingPointAsync(NormalizedPoint point)
    {
        var profile = ActiveInputProfile; var number = profile.Bindings.Count(item => item.Action.Point is not null) + 1;
        var binding = new KeyBinding(InputSourceKind.KeyboardKey, $"Key{number}", new InputAction(InputActionKind.TouchPoint, "tap", point));
        await _inputMappings.SaveAsync(profile with { Bindings = profile.Bindings.Append(binding).ToArray() }); OnPropertyChanged(nameof(ActiveInputProfile));
    }

    private async Task RefreshAudioCapabilityAsync()
    {
        var selected = SelectedDevice;
        if (selected is null) { _audioSupported = false; _mediaCapabilities = null; AudioStatus = LocalizationService.Current["AudioNotChecked"]; ToggleAudioCommand.NotifyCanExecuteChanged(); return; }
        var media = await _mediaCapabilityService.DetectAsync(selected.Serial);
        var capability = media.Audio;
        if (SelectedDevice?.Serial != selected.Serial) return;
        _mediaCapabilities = media;
        _audioSupported = capability.IsSupported;
        AudioStatus = capability.IsSupported ? LocalizationService.Current.Format("AudioSupported", capability.Codec, capability.AndroidApi) : LocalizationService.Current.Format("AudioUnsupported", capability.Reason ?? "unknown");
        ToggleAudioCommand.NotifyCanExecuteChanged();
    }

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
            var previousSerials = _knownDeviceSerials;
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
                    Devices.Add(new DeviceItemViewModel(device, _profiles.Get(device.Serial), IsAnySessionRunning(device.Serial)));
                }
                else
                {
                    existing.Update(device, _profiles.Get(device.Serial), IsAnySessionRunning(device.Serial));
                }
            }

            foreach (var connected in devices.Where(item => !previousSerials.Contains(item.Serial))) _ = _automation.DispatchAsync(new(AutomationTrigger.DeviceConnected, connected), cancellationToken: _lifetime.Token);
            foreach (var disconnectedSerial in previousSerials.Where(serial => devices.All(item => item.Serial != serial)))
            {
                var prior = Devices.FirstOrDefault(item => item.Serial == disconnectedSerial)?.Device ?? new AndroidDevice(disconnectedSerial, disconnectedSerial, null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Offline);
                _ = _automation.DispatchAsync(new(AutomationTrigger.DeviceDisconnected, prior), cancellationToken: _lifetime.Token);
            }
            _knownDeviceSerials = incomingSerials;

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
            await EnsureScrcpyCompatibleAsync(selected.Serial);
            SetStatus(LocalizationService.Current.Format("StatusStarting", selected.FriendlyName), StatusKind.Neutral);
            var profile = _profiles.Get(selected.Serial);
            await _embeddedSessions.StartAsync(selected.Device, profile.ToLaunchOptions(), _lifetime.Token);
            await _automation.DispatchAsync(new(AutomationTrigger.SessionStarted, selected.Device), cancellationToken: _lifetime.Token);
            await _profiles.SaveAsync(profile with { LastConnected = DateTimeOffset.Now });
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
            await EnsureScrcpyCompatibleAsync(selected.Serial);
            await _embeddedSessions.StopAsync(selected.Serial);
            var profile = _profiles.Get(selected.Serial);
            await _embeddedSessions.StartAsync(selected.Device, profile.ToLaunchOptions(), _lifetime.Token);
            await _profiles.SaveAsync(profile with { LastConnected = DateTimeOffset.Now });
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
            await EnsureScrcpyCompatibleAsync(selected.Serial);
            var profile = _profiles.Get(selected.Serial);
            await _scrcpy.StartAsync(selected.Device, profile.ToLaunchOptions() with
            {
                WindowTitle = $"PocketBridge — {selected.FriendlyName} — {selected.Serial}"
            });
            await _profiles.SaveAsync(profile with { LastConnected = DateTimeOffset.Now });
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

    private void OpenApplications()
    {
        var selected = SelectedDevice;
        if (selected is not null) _featureDialogs.ShowApplications(selected.Device);
    }
    private void OpenDeviceInformation() { if (SelectedDevice is { } selected) _featureDialogs.ShowDeviceInformation(selected.Device); }
    private void OpenAdbConsole() { if (SelectedDevice is { } selected) _featureDialogs.ShowAdbConsole(selected.Device); }

    private async Task OpenWifiAsync()
    {
        var selected = SelectedDevice;
        if (selected is null) return;
        _featureDialogs.ShowWifi(selected.Device);
        await RefreshDevicesAsync(true);
    }

    private async Task InstallApkAsync() => await InstallApkAsync(null);

    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> files, DeviceItemViewModel? target = null)
    {
        var device = target?.Device ?? SelectedDevice?.Device;
        if (device is null) { SetStatus(LocalizationService.Current["ChooseDevice"], StatusKind.Warning); return; }
        try
        {
            var count = await _featureDialogs.QueueDroppedFilesAsync(device, files);
            if (count > 0) SetStatus(LocalizationService.Current.Format("StatusTransfersQueued", count, target?.FriendlyName ?? SelectedDevice?.FriendlyName ?? device.FriendlyName), StatusKind.Success);
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("FileOperationFailed", exception.Message), StatusKind.Error); }
    }

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

    private async Task ToggleRecordingAsync()
    {
        var selected = SelectedDevice;
        if (selected is null) return;
        try
        {
            if (_recordings.Get(selected.Serial)?.IsRunning == true)
            {
                var stopped = await _recordings.StopAsync(selected.Serial);
                if (stopped is not null) _featureDialogs.ShowMediaResult(stopped.FilePath, false);
            }
            else
            {
                await EnsureScrcpyCompatibleAsync(selected.Serial);
                var settings = _settings.Load();
                var format = settings.RecordingFormat.Equals("mkv", StringComparison.OrdinalIgnoreCase) ? "mkv" : "mp4";
                var folder = string.IsNullOrWhiteSpace(settings.RecordingFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : settings.RecordingFolder;
                var safeDevice = SanitizeFilename(selected.FriendlyName);
                var path = Path.Combine(folder!, $"PocketBridge_{safeDevice}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{format}");
                var options = new RecordingOptions(format, settings.RecordingVideoCodec, settings.RecordingIncludeAudio, settings.RecordingMaxSize, settings.RecordingMaxFps, settings.RecordingBitrateMbps);
                var capabilities = _mediaCapabilities ?? await _mediaCapabilityService.DetectAsync(selected.Serial);
                if (!capabilities.Supports(options, out var reason)) throw new NotSupportedException(reason);
                await _recordings.StartAsync(selected.Device, path, options);
            }
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("RecordingFailed", exception.Message), StatusKind.Error); }
    }

    private void OnRecordingChanged(object? sender, RecordingSession session)
    {
        void Update() { OnPropertyChanged(nameof(IsRecording)); OnPropertyChanged(nameof(RecordingText)); ToggleRecordingCommand.NotifyCanExecuteChanged(); }
        if (_uiContext is null) Update(); else _uiContext.Post(_ => Update(), null);
    }

    private async Task ToggleAudioAsync()
    {
        if (SelectedDevice is not { } selected) return;
        try
        {
            if (_audio.IsRunning(selected.Serial)) await _audio.StopAsync(selected.Serial); else await _audio.StartAsync(selected.Device);
            var capability = await _audio.DetectAsync(selected.Serial);
            AudioStatus = capability.IsSupported ? $"Audio: {capability.Codec}, API {capability.AndroidApi}" : $"Audio unavailable: {capability.Reason}";
        }
        catch (Exception exception) { AudioStatus = $"Audio unavailable: {exception.Message}"; }
        OnPropertyChanged(nameof(AudioButtonText));
    }

    private static string SanitizeFilename(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private async Task EnsureScrcpyCompatibleAsync(string serial)
    {
        var result = await _adb.ExecuteAsync(serial, "shell", "getprop", "ro.build.version.sdk");
        var apiLevel = result.IsSuccess ? ScrcpyCompatibility.ParseApiLevel(result.StandardOutput) : null;
        if (apiLevel is not null && !ScrcpyCompatibility.IsSupported(apiLevel.Value))
            throw new NotSupportedException(LocalizationService.Current.Format("ScrcpyAndroidUnsupported", apiLevel, ScrcpyCompatibility.MinimumApiLevel));
    }

    private async Task SendClipboardToDeviceAsync()
    {
        var session = SelectedEmbeddedSession;
        if (session is null) return;
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            Tracker(session.Serial).ObserveWindows(text);
            await session.SendClipboardAsync(text, Interlocked.Increment(ref _clipboardSequence), cancellationToken: _lifetime.Token);
            SetStatus(LocalizationService.Current["StatusClipboardSent"], StatusKind.Success);
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("StatusClipboardFailed", exception.Message), StatusKind.Error); }
    }

    private async Task CopyDeviceClipboardAsync()
    {
        var session = SelectedEmbeddedSession;
        if (session is null) return;
        try
        {
            await session.RequestClipboardAsync(_lifetime.Token);
            SetStatus(LocalizationService.Current["StatusClipboardRequested"], StatusKind.Neutral);
        }
        catch (Exception exception) { SetStatus(LocalizationService.Current.Format("StatusClipboardFailed", exception.Message), StatusKind.Error); }
    }

    private async void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        var selected = SelectedDevice;
        var session = SelectedEmbeddedSession;
        if (selected is null || session is not { IsRunning: true } || _profiles.Get(selected.Serial).ClipboardMode != ClipboardSyncMode.Automatic) return;
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            var update = Tracker(selected.Serial).ObserveWindows(text);
            if (update is not null) await session.SendClipboardAsync(text, update.Version, cancellationToken: _lifetime.Token);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            SetStatus(LocalizationService.Current.Format("StatusClipboardFailed", exception.Message), StatusKind.Warning);
        }
    }

    private void AttachClipboardHandlers()
    {
        foreach (var session in _embeddedSessions.Sessions)
        {
            if (!_clipboardSessions.Add(session)) continue;
            session.ClipboardChanged += OnClipboardChanged;
            session.FrameReady += OnLanFrameReady;
        }
    }

    private void OnLanFrameReady(object? sender, VideoFrameEventArgs e)
    {
        if (!_lan.IsRunning || sender is not IEmbeddedDisplaySession session) return;
        var alias = Devices.FirstOrDefault(item => item.Serial == session.Serial)?.FriendlyName ?? session.DeviceName;
        _lan.PublishFrame(alias, e.Frame);
    }

    private void OnRemoteControlRequested(object? sender, RemoteControlRequest request)
    {
        var device = Devices.FirstOrDefault(item => string.Equals(item.FriendlyName, request.TargetAlias, StringComparison.Ordinal));
        var session = device is null ? null : _embeddedSessions.Get(device.Serial);
        if (session is not { IsRunning: true }) return;
        _ = ExecuteRemoteControlAsync(session, request);
    }

    private async Task ExecuteRemoteControlAsync(IEmbeddedDisplaySession session, RemoteControlRequest request)
    {
        try
        {
            var x = Math.Clamp((int)Math.Round(request.X * session.VideoWidth), 0, Math.Max(0, session.VideoWidth - 1));
            var y = Math.Clamp((int)Math.Round(request.Y * session.VideoHeight), 0, Math.Max(0, session.VideoHeight - 1));
            switch (request.Action.ToLowerInvariant())
            {
                case "tap": await session.SendTouchAsync(AndroidTouchAction.Down, 0, x, y, cancellationToken: _lifetime.Token); await session.SendTouchAsync(AndroidTouchAction.Up, 0, x, y, 0, cancellationToken: _lifetime.Token); break;
                case "touchdown": await session.SendTouchAsync(AndroidTouchAction.Down, 0, x, y, cancellationToken: _lifetime.Token); break;
                case "touchup": await session.SendTouchAsync(AndroidTouchAction.Up, 0, x, y, 0, cancellationToken: _lifetime.Token); break;
                case "key": await session.SendKeyAsync(AndroidKeyAction.Down, request.KeyCode, cancellationToken: _lifetime.Token); await session.SendKeyAsync(AndroidKeyAction.Up, request.KeyCode, cancellationToken: _lifetime.Token); break;
                case "back": await SendRemoteKeyAsync(session, 4); break;
                case "home": await SendRemoteKeyAsync(session, 3); break;
                case "recents": await SendRemoteKeyAsync(session, 187); break;
            }
        }
        catch (Exception exception) { SetStatus(exception.Message, StatusKind.Warning); }
    }

    private async Task SendRemoteKeyAsync(IEmbeddedDisplaySession session, int keyCode)
    {
        await session.SendKeyAsync(AndroidKeyAction.Down, keyCode, cancellationToken: _lifetime.Token);
        await session.SendKeyAsync(AndroidKeyAction.Up, keyCode, cancellationToken: _lifetime.Token);
    }

    private void OnGamepadStateChanged(object? sender, GamepadState state)
    {
        var session = SelectedEmbeddedSession; if (session is not { IsRunning: true }) return;
        foreach (var binding in ActiveInputProfile.Bindings.Where(item => item.SourceKind is InputSourceKind.GamepadButton or InputSourceKind.GamepadAxis))
        {
            double magnitude = binding.Input.ToUpperInvariant() switch { "LEFTX" => state.LeftX, "LEFTY" => state.LeftY, "RIGHTX" => state.RightX, "RIGHTY" => state.RightY, "LT" => state.LeftTrigger, "RT" => state.RightTrigger, _ => 0 };
            if (binding.SourceKind == InputSourceKind.GamepadButton && int.TryParse(binding.Input, out var mask)) magnitude = (state.Buttons & mask) != 0 ? 1 : 0;
            if (Math.Abs(magnitude) < binding.DeadZone) continue;
            _ = ExecuteGamepadActionAsync(session, binding.Action, magnitude);
        }
    }

    private static async Task ExecuteGamepadActionAsync(IEmbeddedDisplaySession session, InputAction action, double magnitude)
    {
        if (action.Kind == InputActionKind.AndroidKey && int.TryParse(action.Value, out var key)) { await session.SendKeyAsync(AndroidKeyAction.Down, key); await session.SendKeyAsync(AndroidKeyAction.Up, key); }
        else if (action.Kind is InputActionKind.TouchPoint or InputActionKind.VirtualJoystick && action.Point is { } point) { var pixel = point.ToPixels(session.VideoWidth, session.VideoHeight); await session.SendTouchAsync(AndroidTouchAction.Down, -11, pixel.X, pixel.Y, (float)Math.Abs(magnitude)); await session.SendTouchAsync(AndroidTouchAction.Up, -11, pixel.X, pixel.Y, 0); }
    }

    private void OnClipboardChanged(object? sender, DeviceClipboardEventArgs e)
    {
        if (e.Sequence is not null || SelectedDevice?.Serial != e.Serial || _profiles.Get(e.Serial).ClipboardMode == ClipboardSyncMode.Off) return;
        void Apply()
        {
            var update = Tracker(e.Serial).ObserveAndroid(e.Text);
            if (update is null) return;
            try
            {
                Clipboard.SetText(e.Text);
                SetStatus(LocalizationService.Current["StatusClipboardReceived"], StatusKind.Success);
            }
            catch (Exception exception) { SetStatus(LocalizationService.Current.Format("StatusClipboardFailed", exception.Message), StatusKind.Warning); }
        }
        if (_uiContext is null) Apply(); else _uiContext.Post(_ => Apply(), null);
    }

    private ClipboardSyncTracker Tracker(string serial)
    {
        if (!_clipboardTrackers.TryGetValue(serial, out var tracker)) _clipboardTrackers.Add(serial, tracker = new ClipboardSyncTracker());
        return tracker;
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
        if (!await _settingsDialog.ShowAsync(_mediaCapabilities)) return;
        SetStatus(LocalizationService.Current["StatusSettingsSaved"], StatusKind.Neutral);
        await RefreshDevicesAsync(true);
    }

    private async Task ConfigureProfileAsync()
    {
        var selected = SelectedDevice;
        if (selected is null || !await _profileDialog.ShowAsync(selected.Device)) return;
        selected.Update(selected.Device, _profiles.Get(selected.Serial), selected.IsSessionRunning);
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(SelectedClipboardMode));
        SetStatus(LocalizationService.Current["StatusProfileSaved"], StatusKind.Success);
    }

    private void ShowSingleView() => IsMultiView = false;

    public void GroupSelectionChanged()
    {
        OnPropertyChanged(nameof(GroupTargetCount));
        OnPropertyChanged(nameof(GroupTargetCountText));
        NotifyCommands();
    }

    private AsyncRelayCommand GroupCommand(GroupAction action) => new(() => ExecuteGroupActionAsync(action), CanRunGroupAction);

    private bool CanRunGroupAction() => IsMultiView && GroupTargetCount > 0 && !IsBusy;

    private async Task GroupRebootAsync()
    {
        if (!_confirmation.Confirm(LocalizationService.Current["GroupRebootTitle"], LocalizationService.Current.Format("GroupRebootConfirm", GroupTargetCount))) return;
        await ExecuteGroupActionAsync(GroupAction.Reboot);
    }

    private async Task ExecuteGroupActionAsync(GroupAction action)
    {
        var targets = Devices.Where(device => device.IsGroupSelected && device.Device.IsReady)
            .Select(device => new GroupActionTarget(device.Serial, device.FriendlyName)).ToArray();
        if (targets.Length == 0) return;
        IsBusy = true;
        try
        {
            var results = await _groupActions.ExecuteAsync(action, targets, _lifetime.Token);
            ShowGroupResults(results);
        }
        finally { IsBusy = false; }
    }

    private async Task ExecuteGroupFeatureAsync(Func<IReadOnlyList<AndroidDevice>, Task<IReadOnlyList<GroupActionResult>>> operation)
    {
        var devices = Devices.Where(device => device.IsGroupSelected && device.Device.IsReady).Select(device => device.Device).ToArray();
        if (devices.Length == 0) return;
        IsBusy = true;
        try
        {
            var results = await operation(devices);
            if (results.Count > 0) ShowGroupResults(results);
        }
        finally { IsBusy = false; }
    }

    private void ShowGroupResults(IReadOnlyList<GroupActionResult> results)
    {
        var lines = results.Select(result => result.Success
            ? LocalizationService.Current.Format("GroupSuccess", result.Alias)
            : LocalizationService.Current.Format("GroupFailed", result.Alias, result.Error ?? LocalizationService.Current["StatusCommandFailed"]));
        SetStatus(string.Join(Environment.NewLine, lines), results.All(result => result.Success) ? StatusKind.Success : StatusKind.Warning);
    }

    private async Task StartMultiViewAsync()
    {
        IsBusy = true;
        var failures = new List<string>();
        try
        {
            var activeSerials = ActiveEmbeddedSessions.Select(session => session.Serial).ToHashSet(StringComparer.Ordinal);
            foreach (var device in Devices.Where(item => item.Device.IsReady && !activeSerials.Contains(item.Serial)).Take(Math.Max(0, 4 - activeSerials.Count)))
            {
                try
                {
                    var profile = _profiles.Get(device.Serial);
                    await _embeddedSessions.StartAsync(device.Device, profile.ToLaunchOptions(), _lifetime.Token);
                    await _profiles.SaveAsync(profile with { LastConnected = DateTimeOffset.Now });
                    device.IsSessionRunning = true;
                    activeSerials.Add(device.Serial);
                }
                catch (Exception exception)
                {
                    failures.Add($"{device.FriendlyName}: {exception.Message}");
                }
            }
            IsMultiView = true;
            NotifyMultiViewProperties();
            SetStatus(failures.Count == 0
                ? LocalizationService.Current.Format("StatusMultiViewStarted", ActiveEmbeddedSessions.Count)
                : string.Join(Environment.NewLine, failures), failures.Count == 0 ? StatusKind.Success : StatusKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void FocusSession(string serial)
    {
        SelectedDevice = Devices.FirstOrDefault(device => device.Serial.Equals(serial, StringComparison.Ordinal));
        IsMultiView = false;
    }

    private void NotifyMultiViewProperties()
    {
        OnPropertyChanged(nameof(ActiveEmbeddedSessions));
        OnPropertyChanged(nameof(MultiViewColumns));
        OnPropertyChanged(nameof(MultiViewRows));
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
            AttachClipboardHandlers();
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
        NotifyMultiViewProperties();
        NotifyCommands();
    }

    private void UpdateDeviceProperties()
    {
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSelectionPrompt));
        OnPropertyChanged(nameof(GroupTargetCount));
        OnPropertyChanged(nameof(GroupTargetCountText));
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
    private bool CanUseClipboard() => SelectedEmbeddedSession is { IsRunning: true } && SelectedClipboardMode != ClipboardSyncMode.Off && !IsBusy;
    private bool CanStartMultiView() => Devices.Any(device => device.Device.IsReady) && !IsBusy;

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); PrepareToolsCommand.NotifyCanExecuteChanged(); OpenSettingsCommand.NotifyCanExecuteChanged(); ConfigureProfileCommand.NotifyCanExecuteChanged(); StartMultiViewCommand.NotifyCanExecuteChanged(); ConnectCommand.NotifyCanExecuteChanged(); OpenExternalCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); RestartCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged(); HomeCommand.NotifyCanExecuteChanged(); RecentsCommand.NotifyCanExecuteChanged(); VolumeUpCommand.NotifyCanExecuteChanged(); VolumeDownCommand.NotifyCanExecuteChanged(); PowerCommand.NotifyCanExecuteChanged(); RebootCommand.NotifyCanExecuteChanged(); OpenFilesCommand.NotifyCanExecuteChanged(); InstallApkCommand.NotifyCanExecuteChanged(); WifiCommand.NotifyCanExecuteChanged(); ScreenshotCommand.NotifyCanExecuteChanged();
        SendClipboardCommand.NotifyCanExecuteChanged(); CopyDeviceClipboardCommand.NotifyCanExecuteChanged(); OpenApplicationsCommand.NotifyCanExecuteChanged(); ToggleRecordingCommand.NotifyCanExecuteChanged(); ToggleAudioCommand.NotifyCanExecuteChanged(); OpenApplicationsCommand.NotifyCanExecuteChanged(); OpenDeviceInformationCommand.NotifyCanExecuteChanged(); OpenAdbConsoleCommand.NotifyCanExecuteChanged(); OpenExperimentalCommand.NotifyCanExecuteChanged(); ToggleMappingEditCommand.NotifyCanExecuteChanged();
        GroupHomeCommand.NotifyCanExecuteChanged(); GroupBackCommand.NotifyCanExecuteChanged(); GroupRecentsCommand.NotifyCanExecuteChanged(); GroupVolumeUpCommand.NotifyCanExecuteChanged(); GroupVolumeDownCommand.NotifyCanExecuteChanged(); GroupPowerCommand.NotifyCanExecuteChanged(); GroupRebootCommand.NotifyCanExecuteChanged(); GroupScreenshotCommand.NotifyCanExecuteChanged(); GroupSendFilesCommand.NotifyCanExecuteChanged(); GroupInstallApkCommand.NotifyCanExecuteChanged();
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
        _lan.ControlRequested -= OnRemoteControlRequested;
        _gamepads.StateChanged -= OnGamepadStateChanged;
        _clipboardTimer.Stop();
        _recordingTimer.Stop();
        _recordings.Changed -= OnRecordingChanged;
        _clipboardTimer.Tick -= ClipboardTimer_Tick;
        foreach (var session in _clipboardSessions) session.ClipboardChanged -= OnClipboardChanged;
        _lifetime.Cancel();
        _embeddedSessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _lifetime.Dispose();
    }

    private bool IsAnySessionRunning(string serial) => _embeddedSessions.Get(serial)?.IsRunning == true || _scrcpy.IsRunning(serial);

    private enum StatusKind { Neutral, Success, Warning, Error }
}
