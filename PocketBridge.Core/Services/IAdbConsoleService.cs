namespace PocketBridge.Core.Services;
public interface IAdbConsoleService { Task<int> ExecuteAsync(string serial, string command, Action<string, bool> output, CancellationToken cancellationToken); }
