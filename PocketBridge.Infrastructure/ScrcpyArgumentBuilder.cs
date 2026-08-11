using PocketBridge.Core.Models;

namespace PocketBridge.Infrastructure;

public static class ScrcpyArgumentBuilder
{
    public static IReadOnlyList<string> Build(AndroidDevice device, ScrcpyLaunchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Serial);
        var arguments = new List<string>
        {
            $"--serial={device.Serial}",
            $"--window-title={options.WindowTitle ?? $"PocketBridge — {device.FriendlyName} — {device.Serial}"}"
        };

        if (options.StayAwake) arguments.Add("--stay-awake");
        if (options.AlwaysOnTop) arguments.Add("--always-on-top");
        if (options.TurnScreenOff) arguments.Add("--turn-screen-off");
        if (options.MaxSize is > 0) arguments.Add($"--max-size={options.MaxSize}");
        if (options.MaxFps is > 0) arguments.Add($"--max-fps={options.MaxFps}");
        if (!string.IsNullOrWhiteSpace(options.VideoBitRate)) arguments.Add($"--video-bit-rate={options.VideoBitRate}");
        arguments.Add($"--video-codec={options.VideoCodec.ToString().ToLowerInvariant()}");
        if (options.ClipboardMode != ClipboardSyncMode.Automatic) arguments.Add("--no-clipboard-autosync");
        if (!options.AudioEnabled) arguments.Add("--no-audio");
        if (options.FullScreen) arguments.Add("--fullscreen");
        return arguments;
    }
}
