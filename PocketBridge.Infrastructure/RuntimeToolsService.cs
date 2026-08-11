using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class RuntimeToolsService : IRuntimeToolsService
{
    private const string SupportedScrcpyVersion = "v4.1";
    private const string ScrcpyReleaseApi = "https://api.github.com/repos/Genymobile/scrcpy/releases/tags/v4.1";
    private const string PlatformToolsVersion = "37.0.0";
    private const string PlatformToolsDownloadUrl = "https://dl.google.com/android/repository/platform-tools_r37.0.0-win.zip";
    private const string PlatformToolsSha256 = "4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918";
    private static readonly string[] PlatformToolFiles = { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "NOTICE.txt", "source.properties" };
    private static readonly string[] ScrcpyFiles = { "scrcpy.exe", "scrcpy-server" };
    private readonly HttpClient _httpClient;

    public RuntimeToolsService(string? applicationDirectory = null, HttpClient? httpClient = null)
    {
        DefaultToolsDirectory = Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, "runtime");
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PocketBridge", "1.0"));
        }
    }

    public string DefaultToolsDirectory { get; }

    public RuntimeToolsStatus Inspect(string? directory = null)
    {
        var root = string.IsNullOrWhiteSpace(directory) ? DefaultToolsDirectory : Path.GetFullPath(directory);
        var platformDirectory = Directory.Exists(Path.Combine(root, "platform-tools")) ? Path.Combine(root, "platform-tools") : root;
        var scrcpyDirectory = Directory.Exists(Path.Combine(root, "scrcpy")) ? Path.Combine(root, "scrcpy") : root;
        return new RuntimeToolsStatus(
            root,
            File.Exists(Path.Combine(platformDirectory, "adb.exe")),
            File.Exists(Path.Combine(scrcpyDirectory, "scrcpy.exe")),
            File.Exists(Path.Combine(scrcpyDirectory, "scrcpy-server")),
            File.Exists(Path.Combine(platformDirectory, "AdbWinApi.dll")),
            File.Exists(Path.Combine(platformDirectory, "AdbWinUsbApi.dll")));
    }

    public async Task<RuntimeToolsInstallResult> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"PocketBridge-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var release = await GetScrcpyReleaseAsync(cancellationToken).ConfigureAwait(false);
            var scrcpyArchive = Path.Combine(temporaryRoot, release.AssetName);
            var platformArchive = Path.Combine(temporaryRoot, "platform-tools-latest-windows.zip");
            await Task.WhenAll(
                DownloadAsync(release.DownloadUrl, scrcpyArchive, cancellationToken),
                DownloadAsync(PlatformToolsDownloadUrl, platformArchive, cancellationToken)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                await VerifySha256Async(scrcpyArchive, release.Sha256, cancellationToken).ConfigureAwait(false);
            }
            await VerifySha256Async(platformArchive, PlatformToolsSha256, cancellationToken).ConfigureAwait(false);

            var scrcpyExtract = Path.Combine(temporaryRoot, "scrcpy-extracted");
            var platformExtract = Path.Combine(temporaryRoot, "platform-extracted");
            ZipFile.ExtractToDirectory(scrcpyArchive, scrcpyExtract);
            ZipFile.ExtractToDirectory(platformArchive, platformExtract);

            var scrcpyExecutable = Directory.EnumerateFiles(scrcpyExtract, "scrcpy.exe", SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException("The official scrcpy archive does not contain scrcpy.exe.");
            var adbExecutable = Directory.EnumerateFiles(platformExtract, "adb.exe", SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException("The official Android platform-tools archive does not contain adb.exe.");
            var scrcpySource = Path.GetDirectoryName(scrcpyExecutable)!;
            var platformSource = Path.GetDirectoryName(adbExecutable)!;
            ValidateFiles(scrcpySource, ScrcpyFiles, "scrcpy");
            ValidateFiles(platformSource, PlatformToolFiles, "Android platform-tools");

            var scrcpyDestination = Path.Combine(DefaultToolsDirectory, "scrcpy");
            var platformDestination = Path.Combine(DefaultToolsDirectory, "platform-tools");
            Directory.CreateDirectory(scrcpyDestination);
            Directory.CreateDirectory(platformDestination);
            CopyDirectory(scrcpySource, scrcpyDestination, PlatformToolFiles);
            CopyFiles(platformSource, platformDestination, PlatformToolFiles);

            await WriteManifestAsync(scrcpyDestination, new
            {
                component = "scrcpy",
                source = "https://github.com/Genymobile/scrcpy/releases",
                version = release.Version,
                asset = release.AssetName,
                sha256 = await ComputeSha256Async(scrcpyArchive, cancellationToken).ConfigureAwait(false),
                preparedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            await WriteManifestAsync(platformDestination, new
            {
                component = "Android SDK Platform-Tools",
                source = PlatformToolsDownloadUrl,
                version = PlatformToolsVersion,
                sha256 = PlatformToolsSha256,
                preparedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            var count = Directory.EnumerateFiles(DefaultToolsDirectory, "*", SearchOption.AllDirectories).Count();
            return new RuntimeToolsInstallResult(release.Version, DefaultToolsDirectory, count);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private async Task<ScrcpyRelease> GetScrcpyReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(ScrcpyReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The scrcpy release has no version tag.");
        if (!version.Equals(SupportedScrcpyVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected scrcpy {SupportedScrcpyVersion}, received {version}.");
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => Win64AssetRegex().IsMatch(item.GetProperty("name").GetString() ?? string.Empty));
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The official scrcpy release has no Windows x64 archive.");
        return new ScrcpyRelease(
            version,
            asset.GetProperty("name").GetString()!,
            asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("The scrcpy release asset has no download URL."),
            asset.TryGetProperty("digest", out var digest) ? digest.GetString()?.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase) : null);
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySha256Async(string path, string expected, CancellationToken cancellationToken)
    {
        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The scrcpy archive SHA-256 does not match GitHub release metadata.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void ValidateFiles(string directory, IEnumerable<string> files, string component)
    {
        var missing = files.Where(file => !File.Exists(Path.Combine(directory, file))).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"The official {component} archive is missing: {string.Join(", ", missing)}.");
    }

    private static void CopyDirectory(string source, string destination, IEnumerable<string>? excludedFileNames = null)
    {
        var excluded = (excludedFileNames ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (excluded.Contains(Path.GetFileName(file))) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyFiles(string source, string destination, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            File.Copy(Path.Combine(source, fileName), Path.Combine(destination, fileName), true);
        }
    }

    private static Task WriteManifestAsync(string directory, object manifest, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(directory, "pocketbridge-runtime.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

    [GeneratedRegex(@"^scrcpy-win64-v[\d.]+\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex Win64AssetRegex();

    private sealed record ScrcpyRelease(string Version, string AssetName, string DownloadUrl, string? Sha256);
}
