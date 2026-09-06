using System.Collections.Concurrent;
using System.Diagnostics;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class ScrcpyService : IScrcpyService
{
    private readonly IExecutableLocator _locator;
    private readonly IAppSettingsService _settingsService;
    private readonly ConcurrentDictionary<string, SessionProcess> _sessions = new(StringComparer.Ordinal);

    public ScrcpyService(IExecutableLocator locator, IAppSettingsService settingsService)
    {
        _locator = locator;
        _settingsService = settingsService;
    }

    public event EventHandler<DeviceSession>? SessionChanged;

    public Task<DeviceSession> StartAsync(AndroidDevice device, ScrcpyLaunchOptions options)
    {
        if (!device.IsReady)
        {
            throw new InvalidOperationException("Нельзя запустить scrcpy: устройство не готово к подключению.");
        }

        var sessionKey = Key(device.Serial, options.DisplayId);
        if (IsRunning(device.Serial, options.DisplayId))
        {
            throw new InvalidOperationException("Для этого устройства уже запущена активная сессия.");
        }

        var executable = _locator.Find("scrcpy.exe", _settingsService.Load().ToolsDirectory)
            ?? throw new FileNotFoundException("scrcpy не найден. Подготовьте официальный runtime scrcpy в папке tools.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        var adbExecutable = _locator.Find("adb.exe", _settingsService.Load().ToolsDirectory);
        if (adbExecutable is not null)
        {
            var adbDirectory = Path.GetDirectoryName(adbExecutable)!;
            startInfo.Environment["ADB"] = adbExecutable;
            startInfo.Environment["PATH"] = $"{adbDirectory}{Path.PathSeparator}{startInfo.Environment["PATH"]}";
        }

        foreach (var argument in ScrcpyArgumentBuilder.Build(device, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Не удалось запустить scrcpy.");
        }

        var session = new DeviceSession
        {
            Serial = device.Serial,
            ProcessId = process.Id,
            StartTime = DateTimeOffset.Now,
            IsRunning = true
            ,DisplayId = options.DisplayId
        };

        var holder = new SessionProcess(session, process);
        if (!_sessions.TryAdd(sessionKey, holder))
        {
            process.Kill(true);
            process.Dispose();
            throw new InvalidOperationException("Для этого устройства уже запускается сессия.");
        }

        SessionChanged?.Invoke(this, session);
        process.Exited += (_, _) => CompleteSession(sessionKey, holder);
        if (process.HasExited)
        {
            CompleteSession(sessionKey, holder);
        }
        return Task.FromResult(session);
    }

    public async Task StopAsync(string serial, int displayId = 0)
    {
        if (!_sessions.TryGetValue(Key(serial, displayId), out var holder) || holder.Process.HasExited)
        {
            return;
        }

        holder.Process.Kill(true);
        await holder.Completion.Task.ConfigureAwait(false);
    }

    public async Task StopAllAsync()
    {
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (!_sessions.TryGetValue(key, out var holder)) continue;
            if (!holder.Process.HasExited) holder.Process.Kill(true);
        }
        await Task.WhenAll(_sessions.Values.Select(holder => holder.Completion.Task)).ConfigureAwait(false);
    }

    public bool IsRunning(string serial, int displayId = 0) =>
        _sessions.TryGetValue(Key(serial, displayId), out var holder) && holder.Session.IsRunning && !holder.Process.HasExited;

    public IReadOnlyCollection<DeviceSession> GetActiveSessions() =>
        _sessions.Values.Where(value => value.Session.IsRunning).Select(value => value.Session).ToArray();

    private void CompleteSession(string serial, SessionProcess holder)
    {
        if (!_sessions.TryRemove(serial, out var removed) || !ReferenceEquals(removed, holder))
        {
            return;
        }

        holder.Session.IsRunning = false;
        try
        {
            holder.Session.ExitCode = holder.Process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            holder.Session.ExitCode = null;
        }

        SessionChanged?.Invoke(this, holder.Session);
        holder.Completion.TrySetResult();
        holder.Process.Dispose();
    }
    private static string Key(string serial, int displayId) => $"{serial}\u001f{displayId}";

    private sealed class SessionProcess
    {
        public SessionProcess(DeviceSession session, Process process)
        {
            Session = session;
            Process = process;
        }

        public DeviceSession Session { get; }
        public Process Process { get; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
