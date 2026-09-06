using PocketBridge.Core.Models;
using PocketBridge.Core.Services;
using PocketBridge.Infrastructure;
using PocketBridge.Infrastructure.Embedded;
using System.Buffers.Binary;
using System.Xml.Linq;

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
    ,("App icon cache is version keyed and invalidatable", AppIconCacheIsVersionKeyed)
    ,("Audio capability requires runtime and Android 11", AudioCapabilityIsDetected)
    ,("Application icon retrieval failure keeps placeholder", ApplicationIconFailureFallsBack)
    ,("Transfer history persists with a 500-entry bound", TransferHistoryPersistsBounded)
    ,("Corrupted transfer history is quarantined", CorruptedTransferHistoryRecovers)
    ,("Transfer history filters combine status and device", TransferHistoryFiltersCombine)
    ,("Codec availability parses encoders", CodecAvailabilityParsesEncoders)
    ,("Unsupported recording combinations are rejected", UnsupportedRecordingCombinationIsRejected)
    ,("Normalized touch mapping survives orientation", NormalizedTouchMappingSurvivesOrientation)
    ,("Input profiles export without device association", InputProfilesExportPortable)
    ,("Gamepad mapping honors dead zone", GamepadMappingHonorsDeadZone)
    ,("HID backend falls back when unavailable", HidBackendFallsBack)
    ,("Remote sessions expire and revoke", RemoteSessionsExpireAndRevoke)
    ,("Automation destructive actions require opt-in", AutomationDestructiveRequiresOptIn)
    ,("Audit log remains bounded", AuditLogRemainsBounded)
    ,("LAN access is disabled by default", LanAccessDisabledByDefault)
    ,("Embedded sessions are isolated by display", EmbeddedSessionsAreDisplayScoped)
    ,("Automation dispatcher executes typed matching rules", AutomationDispatcherExecutesTypedRule)
    ,("Visible video coordinates survive letterboxing", VisibleVideoCoordinatesSurviveLetterboxing)
    ,("Centered viewport mapping survives resize fullscreen and DPI", CenteredViewportMappingSurvivesLayoutChanges)
    ,("Input action geometry normalizes bounds", InputActionGeometryNormalizesBounds)
    ,("LAN control rejects origin permission and replay attacks", LanControlRejectsAdversarialMessages)
    ,("LAN control applies bounded request rate", LanControlAppliesRateLimit)
    ,("Audit log sanitizes sensitive identifiers", AuditLogSanitizesIdentifiers)
    ,("P3 localization keys have EN RU zh-CN parity", P3LocalizationKeysHaveParity)
    ,("All localization keys have EN RU zh-CN parity", AllLocalizationKeysHaveParity)
    ,("Clean data root starts with default settings", CleanDataRootStartsWithDefaults)
    ,("XInput axes and triggers normalize across controllers", XInputValuesNormalize)
    ,("Automation permissions are action specific", AutomationPermissionsAreActionSpecific)
    ,("Input profile file import export round-trips geometry", InputProfileFileRoundTripsGeometry)
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
    var historyPath = Path.Combine(Path.GetTempPath(), $"PocketBridge-transfer-history-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, "test");
    var executor = new RecordingTransferExecutor();
    var queue = new FileTransferQueueService(executor, historyPath);
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
        var persistDeadline = DateTime.UtcNow.AddSeconds(2); while (!File.Exists(historyPath) && DateTime.UtcNow < persistDeadline) Thread.Sleep(10);
        var restored = new FileTransferQueueService(executor, historyPath);
        try { var persisted = restored.Items.Single(item => item.Id == id); Equal(FileTransferState.Completed, persisted.State); Equal("Test phone", persisted.DeviceAlias); }
        finally { restored.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    finally
    {
        queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        File.Delete(path);
        if (File.Exists(historyPath)) File.Delete(historyPath);
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

static void AppIconCacheIsVersionKeyed()
{
    var cache = new AppIconCache(); var loads = 0;
    Task<byte[]?> Load(CancellationToken _) { loads++; return Task.FromResult<byte[]?>(new byte[] { (byte)loads }); }
    var first = cache.GetAsync(new AppIconCacheKey("S", "com.example.app", 1), Load).GetAwaiter().GetResult();
    var repeated = cache.GetAsync(new AppIconCacheKey("S", "com.example.app", 1), Load).GetAwaiter().GetResult();
    var updated = cache.GetAsync(new AppIconCacheKey("S", "com.example.app", 2), Load).GetAwaiter().GetResult();
    var otherDevice = cache.GetAsync(new AppIconCacheKey("OTHER", "com.example.app", 2), Load).GetAwaiter().GetResult();
    Equal(3, loads); Equal(first![0], repeated![0]); True(updated![0] != first[0], "A changed version must use a new cache entry."); True(otherDevice![0] != updated[0], "Identical packages on different serials must not share icons.");
    cache.Invalidate("S", "com.example.app");
    cache.GetAsync(new AppIconCacheKey("S", "com.example.app", 2), Load).GetAwaiter().GetResult();
    Equal(4, loads);
}

static void AudioCapabilityIsDetected()
{
    True(!AudioCapabilityDetector.Evaluate(35, false).IsSupported, "Missing runtime must disable audio.");
    True(!AudioCapabilityDetector.Evaluate(29, true).IsSupported, "Android 10 must not advertise audio capture.");
    var supported = AudioCapabilityDetector.Evaluate(30, true);
    True(supported.IsSupported && supported.Codec == "opus", "Android 11 with runtime should advertise the selected codec.");
}

static void ApplicationIconFailureFallsBack()
{
    var service = new ApplicationIconService(new FailingIconAdbService(), new AppIconCache());
    var app = new InstalledApplication("com.example.missing", "Missing", "1", 1, AndroidApplicationType.User);
    var icon = service.GetAsync("ICON-SERIAL", app).GetAwaiter().GetResult();
    True(icon is null, "Failed retrieval must preserve the UI placeholder.");
}

static void TransferHistoryPersistsBounded()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-history-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
    var path = Path.Combine(root, "history.json");
    try
    {
        var snapshots = Enumerable.Range(0, 505).Select(index => new FileTransferSnapshot(Guid.NewGuid(), $"C:\\private\\{index}.bin", $"/sdcard/{index}.bin", "SERIAL", FileTransferOperation.Upload, 1, FileTransferState.Completed, null, DateTimeOffset.UtcNow.AddMinutes(-index), "Phone", index, TimeSpan.FromSeconds(1), "PC → Android")).ToArray();
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(snapshots));
        var queue = new FileTransferQueueService(new RecordingTransferExecutor(), path);
        try { Equal(500, queue.Items.Count); }
        finally { queue.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    finally { Directory.Delete(root, true); }
}

static void CorruptedTransferHistoryRecovers()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-history-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
    var path = Path.Combine(root, "history.json"); File.WriteAllText(path, "{ broken");
    try
    {
        var queue = new FileTransferQueueService(new RecordingTransferExecutor(), path);
        try { Equal(0, queue.Items.Count); True(queue.DiagnosticMessage is not null, "Recovery must expose a diagnostic message."); True(Directory.GetFiles(root, "*.corrupt-*").Length == 1, "Damaged history must be quarantined."); }
        finally { queue.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    finally { Directory.Delete(root, true); }
}

static void TransferHistoryFiltersCombine()
{
    var item = new FileTransferSnapshot(Guid.NewGuid(), "C:\\private\\a.apk", "package", "SERIAL", FileTransferOperation.InstallApk, 0, FileTransferState.Failed, "offline", DateTimeOffset.UtcNow, "Lab phone", 42, TimeSpan.FromSeconds(2), "APK install");
    True(TransferHistoryFilter.Matches(item, "Lab", FileTransferState.Failed), "Matching device and status must pass.");
    True(!TransferHistoryFilter.Matches(item, "Other", FileTransferState.Failed), "Device filter must be combined with status.");
    True(!TransferHistoryFilter.Matches(item, "Lab", FileTransferState.Completed), "Wrong status must be rejected.");
}

static void CodecAvailabilityParsesEncoders()
{
    var codecs = VideoCodecCapabilityDetector.ParseEncoders("c2.vendor.avc.encoder\nOMX.vendor.hevc.encoder\nc2.android.av1.decoder");
    True(codecs.SequenceEqual(new[] { "h264", "h265" }), "Only actual encoder entries must be advertised.");
}

static void UnsupportedRecordingCombinationIsRejected()
{
    var capabilities = new DeviceMediaCapabilities(AudioCapabilityDetector.Evaluate(29, true), new[] { "h264" });
    True(!capabilities.Supports(new RecordingOptions("mp4", "h265", false, null, 30, 8), out _), "Unavailable H.265 must be rejected before launch.");
    True(!capabilities.Supports(new RecordingOptions("mp4", "h264", true, null, 30, 8), out _), "Audio recording must be rejected when capture is unsupported.");
    True(capabilities.Supports(new RecordingOptions("mkv", "h264", false, 1280, 30, 8), out _), "Supported video-only settings must remain valid.");
}

static void NormalizedTouchMappingSurvivesOrientation()
{
    var point = new NormalizedPoint(0.25, 0.75); Equal((250, 1500), point.ToPixels(1000, 2000)); Equal((250, 500), point.ToPixels(1000, 2000, 1));
    Equal((1000, 0), new NormalizedPoint(2, -1).ToPixels(1000, 2000));
}

static void InputProfilesExportPortable()
{
    var profile = new InputProfile(Guid.NewGuid(), "Game", new[] { new KeyBinding(InputSourceKind.KeyboardKey, "W", new InputAction(InputActionKind.TouchPoint, "tap", new NormalizedPoint(.5, .4))) }, "Private phone");
    var restored = InputProfileSerializer.Import(InputProfileSerializer.Export(profile));
    Equal("Game", restored.Name); True(restored.TargetAlias is null, "Portable export must omit device association."); Equal(.5, restored.Bindings[0].Action.Point!.X);
}

static void GamepadMappingHonorsDeadZone()
{
    var root = Path.Combine(Path.GetTempPath(), $"PocketBridge-input-{Guid.NewGuid():N}"); var settings = new JsonAppSettingsService(Path.Combine(root, "settings.json"));
    try
    {
        var profile = new InputProfile(Guid.NewGuid(), "Pad", new[] { new KeyBinding(InputSourceKind.GamepadAxis, "LeftX", new InputAction(InputActionKind.VirtualJoystick, "move"), .2) }); settings.SaveAsync(new AppSettings { InputProfiles = new[] { profile } }).GetAwaiter().GetResult();
        var mappings = new InputMappingService(settings); True(mappings.Resolve(profile.Id, InputSourceKind.GamepadAxis, "LeftX", .1) is null, "Dead-zone input must be ignored."); True(mappings.Resolve(profile.Id, InputSourceKind.GamepadAxis, "LeftX", .5) is not null, "Input above dead zone must resolve.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static void HidBackendFallsBack()
{
    var unavailable = new HidOtgCapabilities(CapabilityState.Unsupported, CapabilityState.Unsupported, CapabilityState.RequiresUsb, "unsupported"); Equal(InputBackendKind.ScrcpyControl, InputBackendSelector.Select(InputBackendKind.HidInput, unavailable)); Equal(InputBackendKind.ScrcpyControl, InputBackendSelector.Select(InputBackendKind.Automatic, unavailable));
}

static void RemoteSessionsExpireAndRevoke()
{
    var service = new RemoteSessionService(); var created = service.Create(RemotePermission.ViewScreen, TimeSpan.FromMinutes(1)); True(service.Authenticate(created.Token, DateTimeOffset.UtcNow) is not null, "Valid token was rejected."); True(service.Authenticate(created.Token, DateTimeOffset.UtcNow.AddMinutes(2)) is null, "Expired token was accepted."); service.Revoke(created.Session.Id); True(service.Authenticate(created.Token, DateTimeOffset.UtcNow) is null, "Revoked token was accepted.");
}

static void AutomationDestructiveRequiresOptIn()
{
    var rule = new AutomationRule(Guid.NewGuid(), "Reboot", true, AutomationTrigger.DeviceConnected, AutomationAction.Reboot, AutomationRisk.Destructive, null, RemotePermission.DeviceButtons); True(!AutomationPermissionPolicy.IsAllowed(rule, false), "Destructive automation ran without opt-in."); True(AutomationPermissionPolicy.IsAllowed(rule, true), "Explicitly permitted destructive automation was rejected.");
}

static void AuditLogRemainsBounded()
{
    var path = Path.Combine(Path.GetTempPath(), $"pocketbridge-audit-{Guid.NewGuid():N}.json");
    try { var log = new AuditLogService(3, path); for (var index = 0; index < 5; index++) log.Append(new AuditRecord(DateTimeOffset.UtcNow, "Local user", "Phone", $"Action {index}", "Success")); Equal(3, log.Items.Count); Equal("Action 4", log.Items[0].Action); var restored = new AuditLogService(3, path); Equal(3, restored.Items.Count); Equal("Action 4", restored.Items[0].Action); }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void LanAccessDisabledByDefault()
{
    var settings = new AppSettings(); True(!settings.LanEnabled, "LAN must be opt-in."); Equal("127.0.0.1", settings.LanBindAddress);
}

static void EmbeddedSessionsAreDisplayScoped()
{
    var manager = new EmbeddedSessionManager(new FakeDisplaySessionFactory()); var device = Device("display-phone");
    var primary = manager.StartAsync(device, new ScrcpyLaunchOptions { DisplayId = 0 }).GetAwaiter().GetResult();
    var secondary = manager.StartAsync(device, new ScrcpyLaunchOptions { DisplayId = 7 }).GetAwaiter().GetResult();
    True(!ReferenceEquals(primary, secondary), "Secondary display reused the primary session."); Equal(0, primary.DisplayId); Equal(7, secondary.DisplayId);
    manager.StopAsync(device.Serial, 7).GetAwaiter().GetResult(); True(manager.Get(device.Serial, 0) is not null, "Stopping secondary display removed primary."); manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static void AutomationDispatcherExecutesTypedRule()
{
    var settings = new InMemorySettingsService(new AppSettings { AutomationRules = [new(Guid.NewGuid(), "Notify", true, AutomationTrigger.DeviceConnected, AutomationAction.Notify, AutomationRisk.Safe, "display-phone", RemotePermission.None)] });
    var auditPath = Path.Combine(Path.GetTempPath(), $"pb-audit-{Guid.NewGuid():N}.json");
    try { var executor = new RecordingAutomationExecutor(); var service = new AutomationService(settings, new AuditLogService(10, auditPath), executor); var results = service.DispatchAsync(new(AutomationTrigger.DeviceConnected, Device("display-phone"))).GetAwaiter().GetResult(); Equal(1, results.Count); Equal(1, executor.Count); }
    finally { if (File.Exists(auditPath)) File.Delete(auditPath); }
}
static AndroidDevice Device(string name) => new(name + "-serial", name, null, null, null, DeviceConnectionType.Usb, AndroidDeviceState.Device);

static void VisibleVideoCoordinatesSurviveLetterboxing()
{
    var sideBars = InputCoordinateMapper.Fit(1600, 900, 1080, 1920); var center = sideBars.ToNormalized(800, 450); True(center is not null, "Portrait video center was outside visible rect."); Near(.5, center!.X, .001); Near(.5, center.Y, .001); True(sideBars.ToNormalized(10, 450) is null, "Letterbox side bar mapped to Android.");
    var topBars = InputCoordinateMapper.Fit(900, 1600, 1920, 1080); center = topBars.ToNormalized(450, 800); True(center is not null, "Landscape video center was outside visible rect."); Near(.5, center!.X, .001); Near(.5, center.Y, .001); True(topBars.ToNormalized(450, 10) is null, "Letterbox top bar mapped to Android.");
    var control = topBars.ToControl(new(.25, .75)); var roundTrip = topBars.ToNormalized(control.X, control.Y)!; Near(.25, roundTrip.X, .001); Near(.75, roundTrip.Y, .001);
}

static void CenteredViewportMappingSurvivesLayoutChanges()
{
    static void AssertCenteredRoundTrip(double hostWidth, double hostHeight, double videoWidth, double videoHeight, double originX = 0, double originY = 0)
    {
        var rect = InputCoordinateMapper.Fit(hostWidth, hostHeight, videoWidth, videoHeight, originX, originY);
        Near(originX + (hostWidth - rect.Width) / 2, rect.Left, .001);
        Near(originY + (hostHeight - rect.Height) / 2, rect.Top, .001);
        foreach (var point in new[] { new NormalizedPoint(0, 0), new(.25, .75), new(.5, .5), new(1, 1) })
        {
            var control = rect.ToControl(point);
            var mapped = rect.ToNormalized(control.X, control.Y) ?? throw new InvalidOperationException("Centered video point fell outside the visible rectangle.");
            Near(point.X, mapped.X, .001); Near(point.Y, mapped.Y, .001);
        }
    }

    AssertCenteredRoundTrip(1200, 700, 1080, 2400);       // portrait, side letterbox
    AssertCenteredRoundTrip(900, 1200, 2400, 1080);       // landscape, top/bottom letterbox
    AssertCenteredRoundTrip(640, 360, 1080, 2400, 8, 8); // resized host with control padding
    AssertCenteredRoundTrip(1920, 1080, 2400, 1080);      // fullscreen-sized host

    var logical = InputCoordinateMapper.Fit(800, 500, 1080, 2400);
    var dpiScaled = InputCoordinateMapper.Fit(1200, 750, 1080, 2400);
    var logicalPoint = logical.ToControl(new(.37, .62));
    var scaledPoint = dpiScaled.ToControl(new(.37, .62));
    var logicalMapped = logical.ToNormalized(logicalPoint.X, logicalPoint.Y)!;
    var scaledMapped = dpiScaled.ToNormalized(scaledPoint.X, scaledPoint.Y)!;
    Near(logicalMapped.X, scaledMapped.X, .001); Near(logicalMapped.Y, scaledMapped.Y, .001);
}

static void InputActionGeometryNormalizesBounds()
{
    var action = new InputAction(InputActionKind.TouchRegion, "region", Region: new(new(1.2, .9), new(-.2, .1)), Radius: 1, DurationMs: 10, Sensitivity: 99).Normalize();
    Equal(new NormalizedPoint(0, .1), action.Region!.Start); Equal(new NormalizedPoint(1, .9), action.Region.End); Near(.5, action.Radius, .001); Equal(50, action.DurationMs); Near(10, action.Sensitivity, .001);
}

static void LanControlRejectsAdversarialMessages()
{
    const string allowedOrigin = "http://localhost:27183";
    var now = DateTimeOffset.UtcNow; var session = new RemoteSession(Guid.NewGuid(), "hash", now.AddMinutes(1), RemotePermission.ViewScreen | RemotePermission.ControlTouch, "Peer"); var gate = new RemoteControlSecurityGate(allowedOrigin);
    True(gate.Validate(session, session.Id, allowedOrigin, "tap", 1, "nonce-0000000001", now.ToUnixTimeMilliseconds(), now).Allowed, "Valid control message was denied.");
    True(!gate.Validate(session, session.Id, allowedOrigin, "tap", 1, "nonce-0000000001", now.ToUnixTimeMilliseconds(), now).Allowed, "Duplicate sequence/nonce was accepted.");
    True(!gate.Validate(session, Guid.NewGuid(), allowedOrigin, "tap", 2, "nonce-0000000009", now.ToUnixTimeMilliseconds(), now).Allowed, "Token/session mismatch was accepted.");
    True(!gate.Validate(session, session.Id, "http://evil.invalid", "tap", 2, "nonce-0000000002", now.ToUnixTimeMilliseconds(), now).Allowed, "Unexpected Origin was accepted.");
    True(!gate.Validate(session, session.Id, null, "tap", 2, "nonce-0000000003", now.ToUnixTimeMilliseconds(), now).Allowed, "Missing Origin was accepted.");
    True(!gate.Validate(session, session.Id, allowedOrigin, "key", 2, "nonce-0000000004", now.ToUnixTimeMilliseconds(), now).Allowed, "Permission escalation was accepted.");
    True(!gate.Validate(session, session.Id, allowedOrigin, "install", 2, "nonce-0000000005", now.ToUnixTimeMilliseconds(), now).Allowed, "Unknown/disallowed command was accepted.");
    True(!gate.Validate(session, session.Id, allowedOrigin, "tap", 2, "nonce-0000000006", now.AddMinutes(-2).ToUnixTimeMilliseconds(), now).Allowed, "Stale timestamp was accepted.");
    True(!gate.Validate(session, session.Id, allowedOrigin, "tap", 2, "nonce-0000000010", now.AddMinutes(2).ToUnixTimeMilliseconds(), now).Allowed, "Future timestamp was accepted.");
    True(!gate.Validate(session with { ExpiresAt = now }, session.Id, allowedOrigin, "tap", 2, "nonce-0000000007", now.ToUnixTimeMilliseconds(), now).Allowed, "Expired session was accepted.");
    gate.Revoke(session.Id); True(gate.Validate(session, session.Id, allowedOrigin, "tap", 1, "nonce-0000000008", now.ToUnixTimeMilliseconds(), now).Allowed, "Revoked gate state was not cleared for a newly authenticated session state.");
}

static void LanControlAppliesRateLimit()
{
    var now = DateTimeOffset.UtcNow; var session = new RemoteSession(Guid.NewGuid(), "hash", now.AddMinutes(1), RemotePermission.ControlTouch, "Peer"); var gate = new RemoteControlSecurityGate("http://localhost:27183", 2);
    True(gate.Validate(session, session.Id, "http://localhost:27183", "tap", 1, "rate-nonce-000001", now.ToUnixTimeMilliseconds(), now).Allowed, "First request denied."); True(gate.Validate(session, session.Id, "http://localhost:27183", "tap", 2, "rate-nonce-000002", now.ToUnixTimeMilliseconds(), now).Allowed, "Second request denied."); True(!gate.Validate(session, session.Id, "http://localhost:27183", "tap", 3, "rate-nonce-000003", now.ToUnixTimeMilliseconds(), now).Allowed, "Rate limit did not reject excess request.");
}

static void AuditLogSanitizesIdentifiers()
{
    var path = Path.Combine(Path.GetTempPath(), $"pb-audit-redact-{Guid.NewGuid():N}.json"); try { var log = new AuditLogService(5, path); log.Append(new(DateTimeOffset.UtcNow, "peer\n", "0123456789ABCDEF0123456789ABCDEF", "touch\rpayload", "Denied", "Security", AuditSeverity.Warning)); var record = log.Items.Single(); True(!record.DeviceAlias.Contains("0123456789ABCDEF0123456789ABCDEF", StringComparison.Ordinal), "Full identifier was retained."); True(!record.Actor.Contains('\n') && !record.Action.Contains('\r'), "Control characters were retained."); }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void P3LocalizationKeysHaveParity()
{
    static Dictionary<string, string> Load(string path) => XDocument.Load(path).Descendants("data").Where(item => item.Attribute("name")?.Value.StartsWith("P3_", StringComparison.Ordinal) == true).ToDictionary(item => item.Attribute("name")!.Value, item => item.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);
    var root = Path.Combine(Directory.GetCurrentDirectory(), "PocketBridge.App", "Resources"); var en = Load(Path.Combine(root, "Strings.resx")); var ru = Load(Path.Combine(root, "Strings.ru-RU.resx")); var zh = Load(Path.Combine(root, "Strings.zh-CN.resx")); Equal(string.Join('|', en.Keys.Order()), string.Join('|', ru.Keys.Order())); Equal(string.Join('|', en.Keys.Order()), string.Join('|', zh.Keys.Order())); True(en.Values.All(value => !string.IsNullOrWhiteSpace(value)) && ru.Values.All(value => !string.IsNullOrWhiteSpace(value)) && zh.Values.All(value => !string.IsNullOrWhiteSpace(value)), "A P3 translation is empty.");
}
static void AllLocalizationKeysHaveParity()
{
    static Dictionary<string, string> Load(string path) => XDocument.Load(path).Descendants("data").ToDictionary(item => item.Attribute("name")!.Value, item => item.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);
    var root = Path.Combine(Directory.GetCurrentDirectory(), "PocketBridge.App", "Resources"); var en = Load(Path.Combine(root, "Strings.resx")); var ru = Load(Path.Combine(root, "Strings.ru-RU.resx")); var zh = Load(Path.Combine(root, "Strings.zh-CN.resx"));
    Equal(string.Join('|', en.Keys.Order()), string.Join('|', ru.Keys.Order())); Equal(string.Join('|', en.Keys.Order()), string.Join('|', zh.Keys.Order()));
    True(en.Values.All(value => !string.IsNullOrWhiteSpace(value)) && ru.Values.All(value => !string.IsNullOrWhiteSpace(value)) && zh.Values.All(value => !string.IsNullOrWhiteSpace(value)), "A localized value is empty.");
}
static void CleanDataRootStartsWithDefaults()
{
    var root = Path.Combine(Path.GetTempPath(), $"pb-clean-data-{Guid.NewGuid():N}"); var previous = Environment.GetEnvironmentVariable("POCKETBRIDGE_DATA_ROOT");
    try
    {
        Environment.SetEnvironmentVariable("POCKETBRIDGE_DATA_ROOT", root);
        var settings = new JsonAppSettingsService().Load();
        True(string.IsNullOrWhiteSpace(settings.ToolsDirectory), "Clean data root inherited a configured runtime path.");
        var audit = new AuditLogService(); True(audit.Items.Count == 0, "Clean data root inherited audit records.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("POCKETBRIDGE_DATA_ROOT", previous);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
static void XInputValuesNormalize() { Near(-1, GamepadNormalization.Axis(short.MinValue), .0001); Near(1, GamepadNormalization.Axis(short.MaxValue), .0001); Near(0, GamepadNormalization.Axis(0), .0001); Near(0, GamepadNormalization.Trigger(0), .0001); Near(1, GamepadNormalization.Trigger(byte.MaxValue), .0001); for (var controller = 1; controller <= 4; controller++) { var state = new GamepadState(controller, true, 0, 0, 0, 0, 0, 0, 0); Equal(controller, state.Controller); } }
static void AutomationPermissionsAreActionSpecific() { var home = new AutomationRule(Guid.NewGuid(), "Home", true, AutomationTrigger.DeviceConnected, AutomationAction.Home, AutomationRisk.Interactive, null, RemotePermission.ViewScreen); True(!AutomationPermissionPolicy.IsAllowed(home, false), "View permission authorized device buttons."); True(AutomationPermissionPolicy.IsAllowed(home with { GrantedPermissions = RemotePermission.DeviceButtons }, false), "Correct device-button permission was rejected."); var uninstall = home with { Action = AutomationAction.Uninstall, Risk = AutomationRisk.Destructive, GrantedPermissions = RemotePermission.ApplicationManager }; True(!AutomationPermissionPolicy.IsAllowed(uninstall, false), "Destructive action ran without opt-in."); True(AutomationPermissionPolicy.IsAllowed(uninstall, true), "Explicit destructive permission was rejected."); }
static void InputProfileFileRoundTripsGeometry() { var root = Path.Combine(Path.GetTempPath(), $"pb-profile-{Guid.NewGuid():N}"); Directory.CreateDirectory(root); try { var settings = new InMemorySettingsService(new AppSettings()); var service = new InputMappingService(settings); var profile = new InputProfile(Guid.NewGuid(), "Geometry", [new(InputSourceKind.GamepadAxis, "LeftX", new(InputActionKind.VirtualJoystick, "stick", new(.3, .7), Radius: .18, Sensitivity: 1.4), .22), new(InputSourceKind.KeyboardKey, "Space", new(InputActionKind.Swipe, "swipe", Region: new(new(.1, .2), new(.8, .9)), DurationMs: 450))], "Private alias"); service.SaveAsync(profile).GetAwaiter().GetResult(); var path = Path.Combine(root, "profile.pbinput.json"); service.ExportAsync(profile.Id, path).GetAwaiter().GetResult(); var imported = service.ImportAsync(path).GetAwaiter().GetResult(); True(imported.Id != profile.Id, "Imported profile reused source id."); True(imported.TargetAlias is null, "Import retained private target alias."); Near(.18, imported.Bindings[0].Action.Radius, .001); Near(1.4, imported.Bindings[0].Action.Sensitivity, .001); Equal(450, imported.Bindings[1].Action.DurationMs); }
    finally { Directory.Delete(root, true); } }

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Near(double expected, double actual, double tolerance) { if (Math.Abs(expected - actual) > tolerance) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }

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

sealed class FailingIconAdbService : IAdbService
{
    public Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AndroidDevice>>(Array.Empty<AndroidDevice>());
    public Task<AdbCommandResult> ExecuteAsync(string serial, params string[] arguments) => Task.FromResult(new AdbCommandResult(1, string.Empty, "package unavailable"));
    public Task<AdbCommandResult> ExecuteHostAsync(params string[] arguments) => throw new NotSupportedException();
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task StartServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task KillServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class FakeDisplaySessionFactory : IDeviceDisplaySessionFactory
{
    public IDeviceDisplaySession CreateExternal(AndroidDevice device, ScrcpyLaunchOptions options) => throw new NotSupportedException();
    public IEmbeddedDisplaySession CreateEmbedded(AndroidDevice device, ScrcpyLaunchOptions options) => new FakeEmbeddedSession(device.Serial, options.DisplayId);
}

sealed class InMemorySettingsService(AppSettings value) : IAppSettingsService
{
    private AppSettings _value = value;
    public AppSettings Load() => _value;
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { _value = settings; return Task.CompletedTask; }
}

sealed class RecordingAutomationExecutor : IAutomationActionExecutor
{
    public int Count { get; private set; }
    public Task<AutomationExecutionResult> ExecuteAsync(AutomationRule rule, AndroidDevice device, CancellationToken cancellationToken = default) { Count++; return Task.FromResult(new AutomationExecutionResult(true, "Completed")); }
}

sealed class FakeEmbeddedSession(string serial, int displayId = 0) : IEmbeddedDisplaySession
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
    public int DisplayId { get; } = displayId;
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
