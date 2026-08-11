using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class UpdateCheckService : IUpdateCheckService
{
    private const string ScrcpyApi = "https://api.github.com/repos/Genymobile/scrcpy/releases/latest";
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settings;
    private readonly IRuntimeToolsService _runtime;
    private readonly HttpClient _http;
    private readonly string _pocketBridgeVersion;

    public UpdateCheckService(IExecutableLocator locator, IAppSettingsService settings, IRuntimeToolsService runtime, string pocketBridgeVersion, HttpClient? http = null)
    {
        _locator = locator;
        _settings = settings;
        _runtime = runtime;
        _pocketBridgeVersion = pocketBridgeVersion;
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PocketBridge", "1.0"));
    }

    public async Task<IReadOnlyList<ComponentUpdateStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var scrcpyInstalled = ReadManifestVersion(Path.Combine(_runtime.DefaultToolsDirectory, "scrcpy", "pocketbridge-runtime.json"));
        var scrcpyLatest = await ReadLatestScrcpyVersionAsync(cancellationToken).ConfigureAwait(false);
        var adbInstalled = await ReadAdbVersionAsync(cancellationToken).ConfigureAwait(false);
        return new[]
        {
            new ComponentUpdateStatus(UpdateComponentKind.PocketBridge, _pocketBridgeVersion, null, "https://github.com/"),
            new ComponentUpdateStatus(UpdateComponentKind.ScrcpyRuntime, scrcpyInstalled, scrcpyLatest, "https://github.com/Genymobile/scrcpy/releases/latest"),
            new ComponentUpdateStatus(UpdateComponentKind.AndroidPlatformTools, adbInstalled, "latest stable", "https://developer.android.com/tools/releases/platform-tools")
        };
    }

    private async Task<string?> ReadLatestScrcpyVersionAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ScrcpyApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("tag_name").GetString();
    }

    private async Task<string?> ReadAdbVersionAsync(CancellationToken cancellationToken)
    {
        var executable = _locator.Find("adb.exe", _settings.Load().ToolsDirectory);
        if (executable is null) return null;
        var result = await ProcessRunner.RunCapturedAsync(executable, new[] { "version" }, cancellationToken).ConfigureAwait(false);
        return AdbVersionRegex().Match(result.StandardOutput) is { Success: true } match ? match.Groups[1].Value : null;
    }

    private static string? ReadManifestVersion(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    [GeneratedRegex(@"Version\s+([^\r\n]+)")]
    private static partial Regex AdbVersionRegex();
}
