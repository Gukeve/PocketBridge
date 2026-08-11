namespace PocketBridge.Core.Models;

public sealed record RemoteFileEntry(string Name, string FullPath, bool IsDirectory)
{
    public string TypeLabel => IsDirectory ? "Folder" : "File";
}

public sealed record WifiConnectionResult(string Address, string Output);
