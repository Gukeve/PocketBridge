using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class GroupActionService(IAdbService adb) : IGroupActionService
{
    public async Task<IReadOnlyList<GroupActionResult>> ExecuteAsync(GroupAction action, IReadOnlyList<GroupActionTarget> targets, CancellationToken cancellationToken = default)
    {
        var arguments = Arguments(action);
        var results = new List<GroupActionResult>(targets.Count);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await adb.ExecuteAsync(target.Serial, arguments).ConfigureAwait(false);
                results.Add(new GroupActionResult(target.Serial, target.Alias, result.IsSuccess, result.IsSuccess ? null : Error(result)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new GroupActionResult(target.Serial, target.Alias, false, exception.Message));
            }
        }
        return results;
    }

    private static string[] Arguments(GroupAction action) => action switch
    {
        GroupAction.Home => ["shell", "input", "keyevent", "KEYCODE_HOME"],
        GroupAction.Back => ["shell", "input", "keyevent", "KEYCODE_BACK"],
        GroupAction.Recents => ["shell", "input", "keyevent", "KEYCODE_APP_SWITCH"],
        GroupAction.VolumeUp => ["shell", "input", "keyevent", "KEYCODE_VOLUME_UP"],
        GroupAction.VolumeDown => ["shell", "input", "keyevent", "KEYCODE_VOLUME_DOWN"],
        GroupAction.Power => ["shell", "input", "keyevent", "KEYCODE_POWER"],
        GroupAction.Reboot => ["reboot"],
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static string Error(AdbCommandResult result) => string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim();
}
