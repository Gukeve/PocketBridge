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
    ("Pairs Wireless Debugging with explicit endpoint", PairsWirelessDebuggingEndpoint)
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

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
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

sealed class FakeDisplaySessionFactory : IDeviceDisplaySessionFactory
{
    public IDeviceDisplaySession CreateExternal(AndroidDevice device, ScrcpyLaunchOptions options) => throw new NotSupportedException();
    public IEmbeddedDisplaySession CreateEmbedded(AndroidDevice device, ScrcpyLaunchOptions options) => new FakeEmbeddedSession(device.Serial);
}

sealed class FakeEmbeddedSession(string serial) : IEmbeddedDisplaySession
{
    public event EventHandler<VideoFrameEventArgs>? FrameReady { add { } remove { } }
    public event EventHandler? StateChanged;
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
    public async ValueTask DisposeAsync() => await StopAsync();
}
