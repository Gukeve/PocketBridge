using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class AdbService : IAdbService
{
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settingsService;

    public AdbService(IExecutableLocator locator, IAppSettingsService settingsService)
    {
        _locator = locator;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "devices", "-l" }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(HumanizeAdbError(result.StandardError));
        }

        return AdbDevicesParser.Parse(result.StandardOutput);
    }

    public Task<AdbCommandResult> ExecuteAsync(string serial, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var safeArguments = new List<string> { "-s", serial };
        safeArguments.AddRange(arguments);
        return RunAsync(safeArguments, CancellationToken.None);
    }

    public Task<AdbCommandResult> ExecuteHostAsync(params string[] arguments) =>
        RunAsync(arguments, CancellationToken.None);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunAsync(new[] { "version" }, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "start-server" }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(HumanizeAdbError(result.StandardError));
        }
    }

    public async Task KillServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "kill-server" }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(HumanizeAdbError(result.StandardError));
        }
    }

    private Task<AdbCommandResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var executable = _locator.Find("adb.exe", _settingsService.Load().ToolsDirectory)
            ?? throw new FileNotFoundException("ADB не найден. Подготовьте официальный runtime scrcpy в папке tools.");
        return ProcessRunner.RunCapturedAsync(executable, arguments, cancellationToken);
    }

    private static string HumanizeAdbError(string error) => string.IsNullOrWhiteSpace(error)
        ? "ADB не смог выполнить команду. Проверьте подключение и перезапустите ADB-сервер."
        : $"ADB сообщил об ошибке: {error.Trim()}";
}
