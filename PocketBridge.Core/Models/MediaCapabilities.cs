namespace PocketBridge.Core.Models;

public sealed record AudioCapability(bool IsSupported, int? AndroidApi, string Codec, string? Reason);
public sealed record RecordingOptions(string Container, string VideoCodec, bool IncludeAudio, int? MaxSize, int? MaxFps, int? BitrateMbps);
public sealed record DeviceMediaCapabilities(AudioCapability Audio, IReadOnlyList<string> VideoCodecs)
{
    public bool Supports(RecordingOptions options, out string? reason)
    {
        if (!VideoCodecs.Contains(options.VideoCodec, StringComparer.OrdinalIgnoreCase)) { reason = $"{options.VideoCodec} encoder is unavailable on this device"; return false; }
        if (options.IncludeAudio && !Audio.IsSupported) { reason = Audio.Reason ?? "audio capture is unavailable"; return false; }
        if (options.Container is not ("mp4" or "mkv")) { reason = $"{options.Container} container is unsupported"; return false; }
        reason = null; return true;
    }
}

public static class VideoCodecCapabilityDetector
{
    public static IReadOnlyList<string> ParseEncoders(string output)
    {
        var lower = output.ToLowerInvariant(); var codecs = new List<string>();
        if (HasEncoder(lower, "avc") || HasEncoder(lower, "h264")) codecs.Add("h264");
        if (HasEncoder(lower, "hevc") || HasEncoder(lower, "h265")) codecs.Add("h265");
        if (HasEncoder(lower, "av1")) codecs.Add("av1");
        return codecs;
    }
    private static bool HasEncoder(string output, string token) => output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Any(line => line.Contains(token, StringComparison.Ordinal) && (line.Contains("encoder", StringComparison.Ordinal) || line.Contains("enc", StringComparison.Ordinal)));
}

public static class AudioCapabilityDetector
{
    public static AudioCapability Evaluate(int? androidApi, bool hasRuntime, string codec = "opus")
    {
        if (!hasRuntime) return new(false, androidApi, codec, "scrcpy runtime is unavailable");
        if (androidApi is null) return new(false, null, codec, "Android API could not be detected");
        return androidApi >= 30 ? new(true, androidApi, codec, null) : new(false, androidApi, codec, "Android 11 (API 30) or newer is required for audio capture");
    }
}
