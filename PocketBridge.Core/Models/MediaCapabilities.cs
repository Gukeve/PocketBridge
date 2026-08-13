namespace PocketBridge.Core.Models;

public sealed record AudioCapability(bool IsSupported, int? AndroidApi, string Codec, string? Reason);
public sealed record RecordingOptions(string Container, string VideoCodec, bool IncludeAudio, int? MaxSize, int? MaxFps, int? BitrateMbps);
