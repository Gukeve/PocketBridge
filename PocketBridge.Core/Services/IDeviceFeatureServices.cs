using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IApkInstallerService
{
    Task<AdbCommandResult> InstallAsync(string serial, string apkPath);
}

public interface IWifiAdbService
{
    Task<string> GetWifiAddressAsync(string serial);
    Task<WifiConnectionResult> EnableTcpIpAsync(string serial, int port = 5555);
    Task<WifiConnectionResult> ConnectAsync(string ipAddress, int port = 5555);
}

public interface IAdbFileService
{
    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string serial, string remotePath);
    Task UploadAsync(string serial, string localPath, string remoteDirectory);
    Task DownloadAsync(string serial, string remotePath, string localPath);
    Task CreateDirectoryAsync(string serial, string parentPath, string name);
    Task DeleteAsync(string serial, string remotePath);
    Task RenameAsync(string serial, string remotePath, string newName);
}

public interface IScreenshotService
{
    Task CaptureAsync(string serial, string localPath);
}
