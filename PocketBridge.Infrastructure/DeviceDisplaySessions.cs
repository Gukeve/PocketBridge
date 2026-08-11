using PocketBridge.Core.Models;
using PocketBridge.Core.Services;
using PocketBridge.Infrastructure.Embedded;

namespace PocketBridge.Infrastructure;

public sealed class DeviceDisplaySessionFactory : IDeviceDisplaySessionFactory
{
    private readonly IScrcpyService _scrcpy;
    private readonly IAdbService _adb;
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    public DeviceDisplaySessionFactory(IScrcpyService scrcpy, IAdbService adb, IExecutableLocator locator, IAppSettingsService settings)
    {
        _scrcpy = scrcpy;
        _adb = adb;
        _locator = locator;
        _settings = settings;
    }

    public IDeviceDisplaySession CreateExternal(AndroidDevice device, ScrcpyLaunchOptions options) =>
        new ExternalScrcpySession(device, options, _scrcpy);

    public IEmbeddedDisplaySession CreateEmbedded(AndroidDevice device, ScrcpyLaunchOptions options) => new EmbeddedScrcpySession(device, options, _adb, _locator, _settings);
}

public sealed class ExternalScrcpySession : IDeviceDisplaySession
{
    private readonly AndroidDevice _device;
    private readonly ScrcpyLaunchOptions _options;
    private readonly IScrcpyService _scrcpy;

    public ExternalScrcpySession(AndroidDevice device, ScrcpyLaunchOptions options, IScrcpyService scrcpy)
    {
        _device = device;
        _options = options;
        _scrcpy = scrcpy;
    }

    public string Serial => _device.Serial;
    public DeviceDisplayMode Mode => DeviceDisplayMode.External;
    public bool IsAvailable => true;
    public bool IsRunning => _scrcpy.IsRunning(Serial);
    public string? UnavailableReason => null;
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _scrcpy.StartAsync(_device, _options).ConfigureAwait(false);
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _scrcpy.StopAsync(Serial).ConfigureAwait(false);
    }
}
