using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class DeviceProfileService(IAppSettingsService settings) : IDeviceProfileService
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public DeviceProfile Get(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return GetAll().FirstOrDefault(profile => profile.Serial.Equals(serial, StringComparison.Ordinal))
            ?? new DeviceProfile { Serial = serial };
    }

    public IReadOnlyList<DeviceProfile> GetAll() => (settings.Load().DeviceProfiles ?? Array.Empty<DeviceProfile>())
        .Where(profile => !string.IsNullOrWhiteSpace(profile.Serial))
        .GroupBy(profile => profile.Serial, StringComparer.Ordinal)
        .Select(group => group.Last())
        .OrderBy(profile => profile.Serial, StringComparer.Ordinal)
        .ToArray();

    public async Task SaveAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
    {
        Validate(profile);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = settings.Load();
            var profiles = (current.DeviceProfiles ?? Array.Empty<DeviceProfile>())
                .Where(existing => !existing.Serial.Equals(profile.Serial, StringComparison.Ordinal))
                .Append(profile with { FriendlyName = profile.FriendlyName?.Trim(), LastKnownIp = profile.LastKnownIp?.Trim() })
                .OrderBy(existing => existing.Serial, StringComparer.Ordinal)
                .ToArray();
            await settings.SaveAsync(current with { DeviceProfiles = profiles }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static void Validate(DeviceProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Serial);
        if (profile.FriendlyName?.Length > 80) throw new ArgumentException("Friendly name must not exceed 80 characters.", nameof(profile));
        if (profile.PreferredResolution is < 256 or > 8192) throw new ArgumentOutOfRangeException(nameof(profile), "Resolution must be between 256 and 8192 pixels.");
        if (profile.PreferredFps is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(profile), "FPS must be between 1 and 240.");
        if (profile.PreferredBitrateMbps is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(profile), "Bitrate must be between 1 and 200 Mbps.");
    }
}
