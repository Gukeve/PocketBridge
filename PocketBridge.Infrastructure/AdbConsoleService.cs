using System.Diagnostics;
using System.Text;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class AdbConsoleService(IExecutableLocator locator, IAppSettingsService settings) : IAdbConsoleService
{
    public async Task<int> ExecuteAsync(string serial, string command, Action<string, bool> output, CancellationToken cancellationToken)
    {
        var arguments = Parse(command);
        if (arguments.Count == 0) throw new ArgumentException("Enter an ADB command.", nameof(command));
        if (arguments[0].Equals("adb", StringComparison.OrdinalIgnoreCase)) arguments.RemoveAt(0);
        if (arguments.Any(argument => argument is "-s" or "--serial") || arguments.FirstOrDefault() is "connect" or "pair" or "devices" or "kill-server" or "start-server") throw new InvalidOperationException("This console cannot change its fixed device target.");
        var executable = locator.Find("adb.exe", settings.Load().ToolsDirectory) ?? throw new FileNotFoundException("adb.exe was not found.");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-s"); info.ArgumentList.Add(serial); foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output(e.Data, false); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output(e.Data, true); };
        if (!process.Start()) throw new InvalidOperationException("Could not start adb.exe.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); return process.ExitCode; }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
    }
    internal static List<string> Parse(string command)
    {
        var result = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var i = 0; i < command.Length; i++) { var c = command[i]; if (c == '"') { quoted = !quoted; continue; } if (char.IsWhiteSpace(c) && !quoted) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } } else current.Append(c); }
        if (quoted) throw new ArgumentException("Unclosed quote in command.", nameof(command)); if (current.Length > 0) result.Add(current.ToString()); return result;
    }
}
