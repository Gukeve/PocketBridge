using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class ApplicationService(IAdbService adb) : IApplicationService
{
    public async Task<IReadOnlyList<InstalledApplication>> ListAsync(string serial)
    {
        var userTask = adb.ExecuteAsync(serial, "shell", "pm", "list", "packages", "-3");
        var systemTask = adb.ExecuteAsync(serial, "shell", "pm", "list", "packages", "-s");
        var detailsTask = adb.ExecuteAsync(serial, "shell", "dumpsys", "package", "packages");
        await Task.WhenAll(userTask, systemTask, detailsTask).ConfigureAwait(false);
        EnsureSuccess(userTask.Result);
        EnsureSuccess(systemTask.Result);
        var versions = detailsTask.Result.IsSuccess ? ParseVersions(detailsTask.Result.StandardOutput) : new Dictionary<string, (string?, long?)>(StringComparer.Ordinal);
        var applications = new List<InstalledApplication>();
        Add(applications, ParsePackageList(userTask.Result.StandardOutput), AndroidApplicationType.User, versions);
        Add(applications, ParsePackageList(systemTask.Result.StandardOutput), AndroidApplicationType.System, versions);
        return applications.DistinctBy(application => application.PackageName, StringComparer.Ordinal).OrderBy(application => application.ApplicationName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task<AdbCommandResult> LaunchAsync(string serial, string packageName) { ValidatePackage(packageName); return adb.ExecuteAsync(serial, "shell", "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"); }
    public Task<AdbCommandResult> StopAsync(string serial, string packageName) { ValidatePackage(packageName); return adb.ExecuteAsync(serial, "shell", "am", "force-stop", packageName); }
    public Task<AdbCommandResult> UninstallAsync(string serial, string packageName) { ValidatePackage(packageName); return adb.ExecuteAsync(serial, "uninstall", packageName); }
    public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName) { ValidatePackage(packageName); return adb.ExecuteAsync(serial, "shell", "pm", "clear", packageName); }
    public Task<AdbCommandResult> OpenDetailsAsync(string serial, string packageName) { ValidatePackage(packageName); return adb.ExecuteAsync(serial, "shell", "am", "start", "-a", "android.settings.APPLICATION_DETAILS_SETTINGS", "-d", $"package:{packageName}"); }

    internal static IReadOnlyList<string> ParsePackageList(string output) => output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim()).Where(line => line.StartsWith("package:", StringComparison.Ordinal)).Select(line => line[8..]).Where(package => PackageRegex().IsMatch(package)).ToArray();

    internal static Dictionary<string, (string? VersionName, long? VersionCode)> ParseVersions(string output)
    {
        var result = new Dictionary<string, (string?, long?)>(StringComparer.Ordinal);
        string? current = null;
        string? versionName = null;
        long? versionCode = null;
        void Commit() { if (current is not null) result[current] = (versionName, versionCode); }
        foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
        {
            var line = raw.Trim();
            var packageMatch = PackageBlockRegex().Match(line);
            if (packageMatch.Success) { Commit(); current = packageMatch.Groups[1].Value; versionName = null; versionCode = null; continue; }
            if (current is null) continue;
            if (line.StartsWith("versionName=", StringComparison.Ordinal)) versionName = line[12..].Trim();
            else if (line.StartsWith("versionCode=", StringComparison.Ordinal))
            {
                var token = line[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (long.TryParse(token, out var parsed)) versionCode = parsed;
            }
        }
        Commit();
        return result;
    }

    private static void Add(List<InstalledApplication> target, IEnumerable<string> packages, AndroidApplicationType type, IReadOnlyDictionary<string, (string? VersionName, long? VersionCode)> versions)
    {
        foreach (var package in packages)
        {
            versions.TryGetValue(package, out var version);
            target.Add(new InstalledApplication(package, Humanize(package), version.VersionName, version.VersionCode, type));
        }
    }

    private static string Humanize(string packageName)
    {
        var segment = packageName.Split('.').LastOrDefault() ?? packageName;
        return string.Join(' ', Regex.Split(segment, "(?=[A-Z])").Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
    private static void ValidatePackage(string packageName) { if (!PackageRegex().IsMatch(packageName)) throw new ArgumentException("Invalid Android package name.", nameof(packageName)); }
    private static void EnsureSuccess(AdbCommandResult result) { if (!result.IsSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim()); }
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+$")]
    private static partial Regex PackageRegex();
    [GeneratedRegex(@"^Package \[([^\]]+)\]")]
    private static partial Regex PackageBlockRegex();
}
