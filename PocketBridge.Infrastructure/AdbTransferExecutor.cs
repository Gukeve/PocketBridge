using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed partial class AdbTransferExecutor(IExecutableLocator locator, IAppSettingsService settings) : IAdbTransferExecutor
{
    public Task<AdbCommandResult> UploadAsync(string serial, string source, string destination, IProgress<double> progress, CancellationToken cancellationToken) =>
        RunAsync(serial, new[] { "push", "-p", source, destination }, progress, cancellationToken);

    public Task<AdbCommandResult> InstallApkAsync(string serial, string source, IProgress<double> progress, CancellationToken cancellationToken) =>
        RunAsync(serial, new[] { "install", "-r", source }, progress, cancellationToken);

    private async Task<AdbCommandResult> RunAsync(string serial, IReadOnlyList<string> arguments, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var adb = locator.Find("adb.exe", settings.Load().ToolsDirectory) ?? throw new FileNotFoundException("PocketBridge runtime does not contain adb.exe.");
        var info = new ProcessStartInfo { FileName = adb, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add(serial);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start adb transfer.");
        progress.Report(0);
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var outputTask = ReadAsync(process.StandardOutput, output, progress, cancellationToken);
        var errorTask = ReadAsync(process.StandardError, error, progress, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
        if (process.ExitCode == 0) progress.Report(1);
        return new AdbCommandResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder destination, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var buffer = new char[256];
        var tail = string.Empty;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            var text = new string(buffer, 0, count);
            destination.Append(text);
            tail = (tail + text)[Math.Max(0, tail.Length + text.Length - 512)..];
            foreach (Match match in ProgressRegex().Matches(tail))
                if (int.TryParse(match.Groups[1].Value, out var percent)) progress.Report(Math.Clamp(percent / 100d, 0, 1));
        }
    }

    [GeneratedRegex(@"(?:\[\s*)?(\d{1,3})%(?:\])?")]
    private static partial Regex ProgressRegex();
}
