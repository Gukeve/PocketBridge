using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class ExecutableLocator : IExecutableLocator
{
    private readonly string _applicationDirectory;

    public ExecutableLocator(string? applicationDirectory = null)
    {
        _applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
    }

    public string? Find(string executableName, string? configuredPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        foreach (var candidate in GetCandidates(executableName, configuredPath))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private IEnumerable<string> GetCandidates(string executableName, string? configuredPath)
    {
        yield return Path.Combine(_applicationDirectory, "runtime", "platform-tools", executableName);
        yield return Path.Combine(_applicationDirectory, "runtime", "scrcpy", executableName);
        yield return Path.Combine(_applicationDirectory, "tools", executableName);
        yield return Path.Combine(_applicationDirectory, executableName);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return Path.Combine(configuredPath, "platform-tools", executableName);
            yield return Path.Combine(configuredPath, "scrcpy", executableName);
            yield return Directory.Exists(configuredPath)
                ? Path.Combine(configuredPath, executableName)
                : configuredPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim('"'), executableName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidate;
        }
    }
}
