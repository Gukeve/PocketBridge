using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class ApkInstallerService : IApkInstallerService
{
    private readonly IAdbService _adb;
    public ApkInstallerService(IAdbService adb) => _adb = adb;
    public async Task<AdbCommandResult> InstallAsync(string serial, string apkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!File.Exists(apkPath) || !Path.GetExtension(apkPath).Equals(".apk", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("APK file was not found.", apkPath);
        return await _adb.ExecuteAsync(serial, "install", "-r", apkPath).ConfigureAwait(false);
    }
}

public sealed partial class WifiAdbService : IWifiAdbService
{
    private readonly IAdbService _adb;
    public WifiAdbService(IAdbService adb) => _adb = adb;

    public async Task<string> GetWifiAddressAsync(string serial)
    {
        var result = await _adb.ExecuteAsync(serial, "shell", "ip", "-f", "inet", "addr", "show", "wlan0").ConfigureAwait(false);
        var match = IpAddressRegex().Match(result.StandardOutput);
        if (!match.Success)
        {
            result = await _adb.ExecuteAsync(serial, "shell", "ip", "route").ConfigureAwait(false);
            match = SourceAddressRegex().Match(result.StandardOutput);
        }
        if (!match.Success) throw new InvalidOperationException("Wi-Fi IPv4 address was not found. Connect the device to Wi-Fi first.");
        return match.Groups[1].Value;
    }

    public async Task<WifiConnectionResult> EnableTcpIpAsync(string serial, int port = 5555)
    {
        ValidatePort(port);
        var ip = await GetWifiAddressAsync(serial).ConfigureAwait(false);
        var tcpip = await _adb.ExecuteAsync(serial, "tcpip", port.ToString()).ConfigureAwait(false);
        if (!tcpip.IsSuccess) throw new InvalidOperationException(tcpip.StandardError.Trim());
        await Task.Delay(1200).ConfigureAwait(false);
        return await ConnectAsync(ip, port).ConfigureAwait(false);
    }

    public async Task<WifiConnectionResult> ConnectAsync(string ipAddress, int port = 5555)
    {
        ValidatePort(port);
        if (!IPAddress.TryParse(ipAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Enter a valid IPv4 address.", nameof(ipAddress));
        var endpoint = $"{address}:{port}";
        var result = await _adb.ExecuteHostAsync("connect", endpoint).ConfigureAwait(false);
        if (!result.IsSuccess || result.StandardOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) || result.StandardOutput.Contains("cannot", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
        return new WifiConnectionResult(endpoint, result.StandardOutput.Trim());
    }

    public async Task<WifiConnectionResult> PairAsync(string ipAddress, int port, string pairingCode)
    {
        ValidatePort(port);
        if (!IPAddress.TryParse(ipAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Enter a valid IPv4 address.", nameof(ipAddress));
        if (pairingCode.Length != 6 || pairingCode.Any(character => !char.IsAsciiDigit(character))) throw new ArgumentException("Enter the six-digit pairing code.", nameof(pairingCode));
        var endpoint = $"{address}:{port}";
        var result = await _adb.ExecuteHostAsync("pair", endpoint, pairingCode).ConfigureAwait(false);
        if (!result.IsSuccess || result.StandardOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) || result.StandardOutput.Contains("cannot", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
        return new WifiConnectionResult(endpoint, result.StandardOutput.Trim());
    }

    private static void ValidatePort(int port) { if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port)); }
    [GeneratedRegex(@"\binet\s+(\d{1,3}(?:\.\d{1,3}){3})/")]
    private static partial Regex IpAddressRegex();
    [GeneratedRegex(@"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})\b")]
    private static partial Regex SourceAddressRegex();
}

public sealed class AdbFileService : IAdbFileService
{
    private const string StorageRoot = "/storage/emulated/0";
    private readonly IAdbService _adb;
    public AdbFileService(IAdbService adb) => _adb = adb;

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string serial, string remotePath)
    {
        var path = Normalize(remotePath);
        var result = await _adb.ExecuteAsync(serial, "shell", "ls", "-1p", path).ConfigureAwait(false);
        EnsureSuccess(result);
        return result.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line is not "." and not "..")
            .Select(line =>
            {
                var directory = line.EndsWith('/');
                var name = directory ? line.TrimEnd('/') : line;
                return new RemoteFileEntry(name, Combine(path, name), directory);
            })
            .OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task UploadAsync(string serial, string localPath, string remoteDirectory)
    {
        if (!File.Exists(localPath)) throw new FileNotFoundException("Local file was not found.", localPath);
        var result = await _adb.ExecuteAsync(serial, "push", localPath, $"{Normalize(remoteDirectory).TrimEnd('/')}/{Path.GetFileName(localPath)}").ConfigureAwait(false); EnsureSuccess(result);
    }
    public async Task DownloadAsync(string serial, string remotePath, string localPath)
    { var result = await _adb.ExecuteAsync(serial, "pull", Normalize(remotePath), localPath).ConfigureAwait(false); EnsureSuccess(result); }
    public async Task CreateDirectoryAsync(string serial, string parentPath, string name)
    { ValidateName(name); var result = await _adb.ExecuteAsync(serial, "shell", "mkdir", Combine(Normalize(parentPath), name)).ConfigureAwait(false); EnsureSuccess(result); }
    public async Task DeleteAsync(string serial, string remotePath)
    { var path = Normalize(remotePath); if (path == StorageRoot) throw new InvalidOperationException("The storage root cannot be deleted."); var result = await _adb.ExecuteAsync(serial, "shell", "rm", "-rf", path).ConfigureAwait(false); EnsureSuccess(result); }
    public async Task RenameAsync(string serial, string remotePath, string newName)
    { ValidateName(newName); var path = Normalize(remotePath); var parent = path[..path.LastIndexOf('/')]; var result = await _adb.ExecuteAsync(serial, "shell", "mv", path, Combine(parent, newName)).ConfigureAwait(false); EnsureSuccess(result); }

    private static string Normalize(string path)
    {
        var normalized = "/" + string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!normalized.Equals(StorageRoot, StringComparison.Ordinal) && !normalized.StartsWith(StorageRoot + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Only shared Android storage is available.");
        if (normalized.Split('/').Any(segment => segment == "..")) throw new InvalidOperationException("Parent traversal is not allowed.");
        return normalized;
    }
    private static string Combine(string parent, string name) => $"{parent.TrimEnd('/')}/{name}";
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0 || name is "." or "..") throw new ArgumentException("Invalid file name.", nameof(name)); }
    private static void EnsureSuccess(AdbCommandResult result) { if (!result.IsSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim()); }
}

public sealed class ScreenshotService : IScreenshotService
{
    private readonly IAdbService _adb;
    public ScreenshotService(IAdbService adb) => _adb = adb;
    public async Task CaptureAsync(string serial, string localPath)
    {
        var remote = $"/storage/emulated/0/Pictures/PocketBridge_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
        var capture = await _adb.ExecuteAsync(serial, "shell", "screencap", "-p", remote).ConfigureAwait(false);
        if (!capture.IsSuccess) throw new InvalidOperationException(capture.StandardError.Trim());
        try
        {
            var pull = await _adb.ExecuteAsync(serial, "pull", remote, localPath).ConfigureAwait(false);
            if (!pull.IsSuccess) throw new InvalidOperationException(pull.StandardError.Trim());
        }
        finally { await _adb.ExecuteAsync(serial, "shell", "rm", remote).ConfigureAwait(false); }
    }
}
