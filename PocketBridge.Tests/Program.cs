using PocketBridge.Core.Models;
using PocketBridge.Core.Services;
using PocketBridge.Infrastructure;
using PocketBridge.Infrastructure.Embedded;
using System.Buffers.Binary;

var tests = new (string Name, Action Run)[]
{
    ("Parses multiple ADB states", ParsesMultipleStates),
    ("Detects TCP/IP serials", DetectsTcpSerial),
    ("Falls back to serial when model is absent", FallsBackToSerial),
    ("Always adds an explicit scrcpy serial", BuildsSafeScrcpyArguments),
    ("APK installation remains serial scoped", ApkInstallIsSerialScoped),
    ("File listing remains serial scoped", FileListingIsSerialScoped),
    ("Prefers managed component directories", PrefersManagedRuntime),
    ("Prefers bundled tools over other locations", PrefersBundledTools),
    ("Validates a complete runtime directory", ValidatesRuntimeDirectory),
    ("Validates the split managed runtime", ValidatesSplitRuntime),
    ("Serializes scrcpy 4.1 touch messages", SerializesTouchMessage),
    ("Keeps embedded sessions alive when switching devices", KeepsEmbeddedSessionsAlive),
    ("Persists independent device profiles", PersistsIndependentDeviceProfiles),
    ("Builds profile quality arguments", BuildsProfileQualityArguments),
    ("Pairs Wireless Debugging with explicit endpoint", PairsWirelessDebuggingEndpoint),
    ("Serializes clipboard protocol messages", SerializesClipboardMessages),
    ("Prevents clipboard feedback loops", PreventsClipboardFeedbackLoops),
    ("Processes serial-scoped file transfer queue", ProcessesFileTransferQueue),
    ("Parses Android application metadata", ParsesAndroidApplicationMetadata),
    ("Application actions remain serial scoped", ApplicationActionsAreSerialScoped),
    ("Parses device properties", ParsesDeviceProperties),
    ("Parses quoted ADB console commands", ParsesAdbConsoleCommand),
    ("Rejects Android versions unsupported by scrcpy", RejectsUnsupportedScrcpyAndroid),
    ("Formats screenshot names without corrupting literals", FormatsScreenshotFilename)
    ,("Group actions remain serial scoped and tolerate partial failure", GroupActionsAreIsolated)
    ,("Shortcut conflict replacement is scope aware", ShortcutConflictReplacementIsScopeAware)
    ,("Shortcut bindings persist in settings", ShortcutBindingsPersist)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void ParsesMultipleStates()
{
    const string output = "List of devices attached\r\nABC123 device product:oriole model:Pixel_6 device:oriole transport_id:1\r\nXYZ unauthorized usb:2-1 transport_id:2\r\nOLD offline transport_id:3\r\n";
    var devices = AdbDevicesParser.Parse(output);
    Equal(3, devices.Count);
    Equal(AndroidDeviceState.Device, devices[0].State);
    Equal("Pixel 6", devices[0].Model);
    Equal(AndroidDeviceState.Unauthorized, devices[1].State);
    Equal(AndroidDeviceState.Offline, devices[2].State);
}

static void DetectsTcpSerial()
{
    // 192.0.2.0/24 is reserved by RFC 5737 for documentation and examples.
    var device = AdbDevicesParser.Parse("List of devices attached\n192.0.2.10:5555 device model:Remote_Phone transport_id:4\n").Single();
    Equal(DeviceConnectionType.TcpIp, device.ConnectionType);
}

static void FallsBackToSerial()
{
    var device = AdbDevicesParser.Parse("List of devices attached\nSERIAL_ONLY device transport_id:5\n").Single();
    Equal("SERIAL_ONLY", device.FriendlyName);
}

static void BuildsSafeScrcpyArguments()
{
    var device = new AndroidDevice("ABC 123", "Pixel", null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Device);
    var arguments = ScrcpyArgumentBuilder.Build(device, new ScrcpyLaunchOptions { StayAwake = true });
    True(arguments.Contains("--serial=ABC 123"), "Explicit serial argument is missing.");
    True(arguments.Contains("--stay-awake"), "Stay-awake argument is missing.");
    True(arguments.All(argument => argument != "--serial"), "Serial must be carried in the explicit --serial=value argument.");
}

static void ApkInstallIsSerialScoped()
{
    var path = Path.Combine(Path.GetTempPath(), $"PocketBridge-{Guid.NewGuid():N}.apk");
    try
    {
        File.WriteAllBytes(path, Array.Empty<byte>());
        var adb = new RecordingAdbService(new AdbCommandResult(0, "Success", string.Empty));
        _ = new ApkInstallerService(adb).InstallAsync("TARGET-SERIAL", path).GetAwaiter().GetResult();
        Equal("TARGET-SERIAL", adb.LastSerial);
        True(adb.LastArguments.SequenceEqual(new[] { "install", "-r", path }), "Unexpected APK arguments.");
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

static void FileListingIsSerialScoped()
{
    var adb = new RecordingAdbService(new AdbCommandResult(0, "Download/\nphoto.png\n", string.Empty));
    var entries = new AdbFileService(adb).ListAsync("FILES-SERIAL", "/storage/emulated/0").GetAwaiter().GetResult();
    Equal("FILES-SERIAL", adb.LastSerial);
    True(adb.LastArguments.SequenceEqual(new[] { "shell", "ls", "-1p", "/storage/emulated/0" }), "Unexpected file-list arguments.");
    Equal(2, entries.Count);
}

static void PrefersBundledTools()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-tests-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        File.WriteAllBytes(Path.Combine(root, "adb.exe"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(root, "tools", "adb.exe"), Array.Empty<byte>());
        var found = new ExecutableLocator(root).Find("adb.exe");
        Equal(Path.Combine(root, "tools", "adb.exe"), found);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void PrefersManagedRuntime()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-tests-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "runtime", "platform-tools"));
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        File.WriteAllBytes(Path.Combine(root, "runtime", "platform-tools", "adb.exe"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(root, "tools", "adb.exe"), Array.Empty<byte>());
        var found = new ExecutableLocator(root).Find("adb.exe");
        Equal(Path.Combine(root, "runtime", "platform-tools", "adb.exe"), found);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void ValidatesRuntimeDirectory()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-tests-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(root);
        foreach (var file in new[] { "adb.exe", "scrcpy.exe", "scrcpy-server", "AdbWinApi.dll", "AdbWinUsbApi.dll" })
        {
            File.WriteAllBytes(Path.Combine(root, file), Array.Empty<byte>());
        }

        var status = new RuntimeToolsService(root).Inspect(root);
        True(status.IsComplete, "A complete official runtime layout was not recognized.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void ValidatesSplitRuntime()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-tests-{Guid.NewGuid():N}");
    try
    {
        var platform = Path.Combine(root, "platform-tools");
        var scrcpy = Path.Combine(root, "scrcpy");
        Directory.CreateDirectory(platform);
        Directory.CreateDirectory(scrcpy);
        foreach (var file in new[] { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll" }) File.WriteAllBytes(Path.Combine(platform, file), Array.Empty<byte>());
        foreach (var file in new[] { "scrcpy.exe", "scrcpy-server" }) File.WriteAllBytes(Path.Combine(scrcpy, file), Array.Empty<byte>());
        True(new RuntimeToolsService(root).Inspect(root).IsComplete, "The split runtime layout was not recognized.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void SerializesTouchMessage()
{
    var message = ScrcpyProtocolV41.Touch(AndroidTouchAction.Down, -2, 321, 654, 720, 1280, 1, 0);
    Equal(32, message.Length);
    Equal((byte)2, message[0]);
    Equal((byte)0, message[1]);
    Equal(unchecked((ulong)-2L), BinaryPrimitives.ReadUInt64BigEndian(message.AsSpan(2)));
    Equal(321, BinaryPrimitives.ReadInt32BigEndian(message.AsSpan(10)));
    Equal(654, BinaryPrimitives.ReadInt32BigEndian(message.AsSpan(14)));
    Equal((ushort)720, BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(18)));
    Equal((ushort)1280, BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(20)));
}

static void KeepsEmbeddedSessionsAlive()
{
    var factory = new FakeDisplaySessionFactory();
    var manager = new EmbeddedSessionManager(factory);
    var first = new AndroidDevice("A", "One", null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Device);
    var second = new AndroidDevice("B", "Two", null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Device);
    manager.StartAsync(first).GetAwaiter().GetResult();
    manager.StartAsync(second).GetAwaiter().GetResult();
    Equal(2, manager.Sessions.Count);
    True(manager.Get("A")?.IsRunning == true, "The first session stopped when the second started.");
    True(manager.Get("B")?.IsRunning == true, "The second session did not start.");
    manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static void PersistsIndependentDeviceProfiles()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-profiles-{Guid.NewGuid():N}");
    var path = Path.Combine(root, "settings.json");
    try
    {
        var settings = new JsonAppSettingsService(path);
        var profiles = new DeviceProfileService(settings);
        profiles.SaveAsync(new DeviceProfile { Serial = "SERIAL-A", FriendlyName = "Lab phone", PreferredFps = 45 }).GetAwaiter().GetResult();
        profiles.SaveAsync(new DeviceProfile { Serial = "SERIAL-B", FriendlyName = "Demo phone", PreferredBitrateMbps = 8 }).GetAwaiter().GetResult();
        var reloaded = new DeviceProfileService(new JsonAppSettingsService(path));
        Equal("Lab phone", reloaded.Get("SERIAL-A").FriendlyName);
        Equal(45, reloaded.Get("SERIAL-A").PreferredFps);
        Equal("Demo phone", reloaded.Get("SERIAL-B").FriendlyName);
        Equal(8, reloaded.Get("SERIAL-B").PreferredBitrateMbps);
        Equal(2, reloaded.GetAll().Count);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void BuildsProfileQualityArguments()
{
    var device = new AndroidDevice("QUALITY-SERIAL", "Phone", null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Device);
    var profile = new DeviceProfile
    {
        Serial = device.Serial,
        PreferredResolution = 1600,
        PreferredFps = 45,
        PreferredBitrateMbps = 12,
        AlwaysOnTop = true,
        ScreenOffOnConnect = true
    };
    var arguments = ScrcpyArgumentBuilder.Build(device, profile.ToLaunchOptions());
    True(arguments.Contains("--max-size=1600"), "Profile resolution was not applied.");
    True(arguments.Contains("--max-fps=45"), "Profile FPS was not applied.");
    True(arguments.Contains("--video-bit-rate=12M"), "Profile bitrate was not applied.");
    True(arguments.Contains("--video-codec=h264"), "Profile codec was not applied.");
    True(arguments.Contains("--always-on-top"), "Always-on-top was not applied.");
    True(arguments.Contains("--turn-screen-off"), "Screen-off was not applied.");
}

static void PairsWirelessDebuggingEndpoint()
{
    var adb = new RecordingAdbService(new AdbCommandResult(0, "Successfully paired", string.Empty));
    var result = new WifiAdbService(adb).PairAsync("192.0.2.20", 37123, "123456").GetAwaiter().GetResult();
    Equal("192.0.2.20:37123", result.Address);
    True(adb.LastArguments.SequenceEqual(new[] { "pair", "192.0.2.20:37123", "123456" }), "Pairing was not scoped to the explicit endpoint.");
}

static void SerializesClipboardMessages()
{
    var get = ScrcpyProtocolV41.GetClipboard();
    True(get.SequenceEqual(new byte[] { 8, 0 }), "Unexpected get-clipboard message.");
    var set = ScrcpyProtocolV41.SetClipboard("hello", 42, false);
    Equal((byte)9, set[0]);
    Equal((ulong)42, BinaryPrimitives.ReadUInt64BigEndian(set.AsSpan(1, 8)));
    Equal((byte)0, set[9]);
    Equal((uint)5, BinaryPrimitives.ReadUInt32BigEndian(set.AsSpan(10, 4)));
    Equal("hello", System.Text.Encoding.UTF8.GetString(set.AsSpan(14)));
}

static void PreventsClipboardFeedbackLoops()
{
    var tracker = new ClipboardSyncTracker();
    var windows = tracker.ObserveWindows("alpha");
    True(windows is { Source: ClipboardUpdateSource.Windows, Version: 1 }, "First Windows update was not forwarded.");
    True(tracker.ObserveAndroid("alpha") is null, "Android echo created a feedback loop.");
    var android = tracker.ObserveAndroid("beta");
    True(android is { Source: ClipboardUpdateSource.Android, Version: 2 }, "New Android update was not accepted.");
    True(tracker.ObserveWindows("beta") is null, "Windows echo created a feedback loop.");
}

static void ProcessesFileTransferQueue()
{
    var path = Path.Combine(Path.GetTempPath(), $"PocketBridge-transfer-{Guid.NewGuid():N}.txt");
    File.WriteAllText(path, "test");
    var executor = new RecordingTransferExecutor();
    var queue = new FileTransferQueueService(executor);
    try
    {
        var id = queue.Enqueue(new[] { new FileTransferRequest(path, "/sdcard/Download/test.txt", "QUEUE-SERIAL", FileTransferOperation.Upload, "Test phone") }).Single();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (queue.Items.Single(item => item.Id == id).State is FileTransferState.Waiting or FileTransferState.Transferring && DateTime.UtcNow < deadline) Thread.Sleep(10);
        var completed = queue.Items.Single(item => item.Id == id);
        Equal(FileTransferState.Completed, completed.State);
        Equal(1d, completed.Progress);
        Equal("QUEUE-SERIAL", executor.Serial);
        Equal("/sdcard/Download/test.txt", executor.Destination);
        Equal("Test phone", completed.DeviceAlias);
        Equal(4L, completed.Bytes);
        True(completed.Duration is not null, "Completed transfer must include duration metadata.");
        Equal("PC → Android", completed.Direction);
        Equal(1, queue.ClearCompleted());
    }
    finally
    {
        queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        File.Delete(path);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void ParsesAndroidApplicationMetadata()
{
    var packages = ApplicationService.ParsePackageList("package:com.example.alpha\nignored\npackage:android.system\n");
    True(packages.SequenceEqual(new[] { "com.example.alpha", "android.system" }), "Package list was not parsed safely.");
    var versions = ApplicationService.ParseVersions("Package [com.example.alpha] (abc):\n  versionCode=42 minSdk=23\n  versionName=1.2.3\n");
    Equal("1.2.3", versions["com.example.alpha"].VersionName);
    Equal(42L, versions["com.example.alpha"].VersionCode);
}

static void ApplicationActionsAreSerialScoped()
{
    var adb = new RecordingAdbService(new AdbCommandResult(0, "Events injected: 1", string.Empty));
    _ = new ApplicationService(adb).LaunchAsync("APP-SERIAL", "com.example.alpha").GetAwaiter().GetResult();
    Equal("APP-SERIAL", adb.LastSerial);
    True(adb.LastArguments.SequenceEqual(new[] { "shell", "monkey", "-p", "com.example.alpha", "-c", "android.intent.category.LAUNCHER", "1" }), "Unexpected application launch arguments.");
}

static void ParsesDeviceProperties()
{
    var values = DeviceInformationService.ParseProperties("[ro.product.model]: [Pixel Test]\n[ro.build.version.sdk]: [35]\n");
    Equal("Pixel Test", values["ro.product.model"]); Equal("35", values["ro.build.version.sdk"]);
}

static void ParsesAdbConsoleCommand()
{
    var arguments = AdbConsoleService.Parse("shell am start -d \"https://example.invalid/a b\"");
    True(arguments.SequenceEqual(new[] { "shell", "am", "start", "-d", "https://example.invalid/a b" }), "Quoted ADB arguments were not preserved.");
}

static void RejectsUnsupportedScrcpyAndroid()
{
    Equal(19, ScrcpyCompatibility.ParseApiLevel("19\r\n"));
    True(!ScrcpyCompatibility.IsSupported(19), "Android 4.4 must be rejected before starting scrcpy.");
    True(ScrcpyCompatibility.IsSupported(21), "Android 5.0 must remain supported.");
}

static void FormatsScreenshotFilename()
{
    var timestamp = new DateTime(2026, 8, 13, 20, 50, 11);
    var name = ScreenshotFilenameFormatter.Format("PocketBridge_<device>_yyyy-MM-dd_HH-mm-ss", "MI PLAY", timestamp);
    Equal("PocketBridge_MI PLAY_2026-08-13_20-50-11", name);

    var sanitized = ScreenshotFilenameFormatter.Format("PocketBridge_<device>_yyyy", "Phone: A", timestamp);
    True(!sanitized.Contains(':'), "Invalid Windows filename characters must be replaced.");
}

static void GroupActionsAreIsolated()
{
    var adb = new GroupRecordingAdbService("SERIAL-B");
    var targets = new[]
    {
        new GroupActionTarget("SERIAL-A", "Alpha"),
        new GroupActionTarget("SERIAL-B", "Beta"),
        new GroupActionTarget("SERIAL-C", "Gamma")
    };

    var results = new GroupActionService(adb).ExecuteAsync(GroupAction.Home, targets).GetAwaiter().GetResult();
    True(adb.Serials.SequenceEqual(new[] { "SERIAL-A", "SERIAL-B", "SERIAL-C" }), "Every command must use exactly the explicitly selected serial.");
    True(adb.Arguments.All(x => x.SequenceEqual(new[] { "shell", "input", "keyevent", "KEYCODE_HOME" })), "Unexpected group command arguments.");
    True(results[0].Success && !results[1].Success && results[2].Success, "A failed target must not stop later targets.");
}

static void ShortcutConflictReplacementIsScopeAware()
{
    var bindings = ShortcutBindingResolver.ReplaceConflicts(new[]
    {
        new ShortcutBinding(ShortcutAction.Home, ShortcutScope.Global, "F5"),
        new ShortcutBinding(ShortcutAction.Back, ShortcutScope.SelectedDevice, "F5"),
        new ShortcutBinding(ShortcutAction.RefreshDevices, ShortcutScope.Global, "f5")
    });
    Equal(2, bindings.Count);
    True(bindings.Any(x => x.Action == ShortcutAction.RefreshDevices && x.Scope == ShortcutScope.Global), "The newest same-scope binding must replace the conflicting binding.");
    True(bindings.Any(x => x.Action == ShortcutAction.Back && x.Scope == ShortcutScope.SelectedDevice), "The same gesture in another scope must remain valid.");
}

static void ShortcutBindingsPersist()
{
    var path = Path.Combine(Path.GetTempPath(), $"PocketBridge-settings-{Guid.NewGuid():N}.json");
    try
    {
        var service = new JsonAppSettingsService(path);
        var expected = new[] { new ShortcutBinding(ShortcutAction.Screenshot, ShortcutScope.EmbeddedView, "Ctrl+9") };
        service.SaveAsync(new AppSettings { ShortcutBindings = expected }).GetAwaiter().GetResult();
        var actual = service.Load().ShortcutBindings.Single();
        Equal(expected[0], actual);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecordingAdbService : IAdbService
{
    private readonly AdbCommandResult _result;
    public RecordingAdbService(AdbCommandResult result) => _result = result;
    public string? LastSerial { get; private set; }
    public IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();
    public Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AndroidDevice>>(Array.Empty<AndroidDevice>());
    public Task<AdbCommandResult> ExecuteAsync(string serial, params string[] arguments) { LastSerial = serial; LastArguments = arguments; return Task.FromResult(_result); }
    public Task<AdbCommandResult> ExecuteHostAsync(params string[] arguments) { LastArguments = arguments; return Task.FromResult(_result); }
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task StartServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task KillServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class GroupRecordingAdbService(string failedSerial) : IAdbService
{
    public List<string> Serials { get; } = new();
    public List<IReadOnlyList<string>> Arguments { get; } = new();
    public Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AndroidDevice>>(Array.Empty<AndroidDevice>());
    public Task<AdbCommandResult> ExecuteAsync(string serial, params string[] arguments)
    {
        Serials.Add(serial);
        Arguments.Add(arguments);
        return Task.FromResult(serial == failedSerial
            ? new AdbCommandResult(1, string.Empty, "simulated failure")
            : new AdbCommandResult(0, "ok", string.Empty));
    }
    public Task<AdbCommandResult> ExecuteHostAsync(params string[] arguments) => throw new NotSupportedException();
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task StartServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task KillServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class FakeDisplaySessionFactory : IDeviceDisplaySessionFactory
{
    public IDeviceDisplaySession CreateExternal(AndroidDevice device, ScrcpyLaunchOptions options) => throw new NotSupportedException();
    public IEmbeddedDisplaySession CreateEmbedded(AndroidDevice device, ScrcpyLaunchOptions options) => new FakeEmbeddedSession(device.Serial);
}

sealed class FakeEmbeddedSession(string serial) : IEmbeddedDisplaySession
{
    public event EventHandler<VideoFrameEventArgs>? FrameReady { add { } remove { } }
    public event EventHandler? StateChanged;
    public event EventHandler<DeviceClipboardEventArgs>? ClipboardChanged { add { } remove { } }
    public string Serial { get; } = serial;
    public DeviceDisplayMode Mode => DeviceDisplayMode.Embedded;
    public bool IsAvailable => true;
    public bool IsRunning { get; private set; }
    public string? UnavailableReason => null;
    public int VideoWidth => 720;
    public int VideoHeight => 1280;
    public string DeviceName => Serial;
    public Task StartAsync(CancellationToken cancellationToken = default) { IsRunning = true; StateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; StateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task SendTouchAsync(AndroidTouchAction action, long pointerId, int x, int y, float pressure = 1, uint buttons = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendKeyAsync(AndroidKeyAction action, int keyCode, int repeat = 0, int metaState = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendScrollAsync(int x, int y, float horizontal, float vertical, uint buttons = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RequestClipboardAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendClipboardAsync(string text, long sequence, bool paste = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public async ValueTask DisposeAsync() => await StopAsync();
}

sealed class RecordingTransferExecutor : IAdbTransferExecutor
{
    public string? Serial { get; private set; }
    public string? Destination { get; private set; }
    public Task<AdbCommandResult> UploadAsync(string serial, string source, string destination, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Serial = serial; Destination = destination; progress.Report(0.5); progress.Report(1);
        return Task.FromResult(new AdbCommandResult(0, "1 file pushed", string.Empty));
    }
    public Task<AdbCommandResult> InstallApkAsync(string serial, string source, IProgress<double> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
}
