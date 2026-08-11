namespace PocketBridge.Core.Models;

public sealed record AdbCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
