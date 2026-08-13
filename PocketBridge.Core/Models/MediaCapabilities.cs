namespace PocketBridge.Core.Models;

public sealed record AudioCapability(bool IsSupported, int? AndroidApi, string Codec, string? Reason);
public sealed record RecordingOptions(string Container, string VideoCodec, bool IncludeAudio, int? MaxSize, int? MaxFps, int? BitrateMbps);

public static class AudioCapabilityDetector
{
    public static AudioCapability Evaluate(int? androidApi, bool hasRuntime, string codec = "opus")
    {
        if (!hasRuntime) return new(false, androidApi, codec, "scrcpy runtime is unavailable");
        if (androidApi is null) return new(false, null, codec, "Android API could not be detected");
        return androidApi >= 30 ? new(true, androidApi, codec, null) : new(false, androidApi, codec, "Android 11 (API 30) or newer is required for audio capture");
    }
}
