using System.IO.Compression;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class ApplicationIconService(IAdbService adb, IAppIconCache cache) : IApplicationIconService
{
    public Task<byte[]?> GetAsync(string serial, InstalledApplication application, CancellationToken cancellationToken = default)
        => cache.GetAsync(new AppIconCacheKey(serial, application.PackageName, application.VersionCode), token => RetrieveAsync(serial, application.PackageName, token), cancellationToken);

    public void Invalidate(string serial, string packageName) => cache.Invalidate(serial, packageName);
    public void InvalidateDevice(string serial) => cache.InvalidateDevice(serial);

    private async Task<byte[]?> RetrieveAsync(string serial, string packageName, CancellationToken cancellationToken)
    {
        var pathResult = await adb.ExecuteAsync(serial, "shell", "pm", "path", packageName).ConfigureAwait(false);
        if (!pathResult.IsSuccess) return null;
        var remote = pathResult.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => line.StartsWith("package:", StringComparison.Ordinal)).Select(line => line[8..]).FirstOrDefault(path => path.EndsWith("base.apk", StringComparison.OrdinalIgnoreCase))
            ?? pathResult.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("package:", StringComparison.Ordinal))?[8..];
        if (string.IsNullOrWhiteSpace(remote)) return null;
        var temporary = Path.Combine(Path.GetTempPath(), $"PocketBridge-icon-{Guid.NewGuid():N}.apk");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pull = await adb.ExecuteAsync(serial, "pull", remote, temporary).ConfigureAwait(false);
            if (!pull.IsSuccess || !File.Exists(temporary)) return null;
            await using var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
            var icon = SelectBestPng(archive.Entries);
            if (icon is null || icon.Length is <= 0 or > 4 * 1024 * 1024) return null;
            await using var source = icon.Open();
            using var output = new MemoryStream((int)icon.Length);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return output.ToArray();
        }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }

    internal static ZipArchiveEntry? SelectBestPng(IEnumerable<ZipArchiveEntry> entries) => entries
        .Where(entry => entry.FullName.StartsWith("res/", StringComparison.Ordinal) && entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .Select(entry => new { Entry = entry, Score = Score(entry.FullName) })
        .Where(item => item.Score > 0).OrderByDescending(item => item.Score).ThenByDescending(item => item.Entry.Length).Select(item => item.Entry).FirstOrDefault();

    private static int Score(string name)
    {
        var lower = name.ToLowerInvariant();
        var score = lower.Contains("mipmap") ? 40 : 10;
        if (lower.Contains("launcher")) score += 60;
        else if (lower.Contains("app_icon") || lower.Contains("/icon")) score += 45;
        else if (!lower.Contains("icon")) return 0;
        if (lower.Contains("xxxhdpi")) score += 30; else if (lower.Contains("xxhdpi")) score += 20; else if (lower.Contains("xhdpi")) score += 10;
        if (lower.Contains("foreground") || lower.Contains("background") || lower.EndsWith(".9.png", StringComparison.Ordinal)) score -= 25;
        return score;
    }
}
