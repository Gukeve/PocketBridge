namespace PocketBridge.Core.Services;

public interface IExecutableLocator
{
    string? Find(string executableName, string? configuredPath = null);
}
