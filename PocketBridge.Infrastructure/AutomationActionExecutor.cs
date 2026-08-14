using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class AutomationActionExecutor(IAdbService adb, IScreenshotService screenshots, IEmbeddedSessionManager sessions, IRecordingService recordings) : IAutomationActionExecutor
{
    public async Task<AutomationExecutionResult> ExecuteAsync(AutomationRule rule, AndroidDevice device, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (rule.Action)
            {
                case AutomationAction.StartMirroring: await sessions.StartAsync(device, cancellationToken: cancellationToken); break;
                case AutomationAction.StopMirroring: await sessions.StopAsync(device.Serial, cancellationToken: cancellationToken); break;
                case AutomationAction.TakeScreenshot:
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PocketBridge"); Directory.CreateDirectory(folder);
                    await screenshots.CaptureAsync(device.Serial, Path.Combine(folder, $"{Sanitize(device.FriendlyName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png")); break;
                case AutomationAction.LaunchApp when !string.IsNullOrWhiteSpace(rule.Argument):
                    var launch = await adb.ExecuteAsync(device.Serial, "shell", "monkey", "-p", rule.Argument.Trim(), "-c", "android.intent.category.LAUNCHER", "1");
                    if (!launch.IsSuccess) return new(false, launch.StandardError); break;
                case AutomationAction.Notify: break;
                case AutomationAction.StartRecording:
                    var recordingFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "PocketBridge"); Directory.CreateDirectory(recordingFolder); await recordings.StartAsync(device, Path.Combine(recordingFolder, $"{Sanitize(device.FriendlyName)}-{DateTime.Now:yyyyMMdd-HHmmss}.mp4")); break;
                case AutomationAction.StopRecording: await recordings.StopAsync(device.Serial); break;
                case AutomationAction.SendKey when int.TryParse(rule.Argument, out var keyCode):
                    var key = await adb.ExecuteAsync(device.Serial, "shell", "input", "keyevent", keyCode.ToString()); if (!key.IsSuccess) return new(false, key.StandardError); break;
                case AutomationAction.Home: return await FixedKeyAsync(device.Serial, 3);
                case AutomationAction.Back: return await FixedKeyAsync(device.Serial, 4);
                case AutomationAction.Power: return await FixedKeyAsync(device.Serial, 26);
                case AutomationAction.VolumeUp: return await FixedKeyAsync(device.Serial, 24);
                case AutomationAction.VolumeDown: return await FixedKeyAsync(device.Serial, 25);
                default: return new(false, "Typed action is unavailable or missing a validated argument.");
            }
            return new(true, "Completed");
        }
        catch (Exception exception) { return new(false, exception.Message); }
    }
    private async Task<AutomationExecutionResult> FixedKeyAsync(string serial, int keyCode) { var result = await adb.ExecuteAsync(serial, "shell", "input", "keyevent", keyCode.ToString()); return result.IsSuccess ? new(true, "Completed") : new(false, result.StandardError); }
    private static string Sanitize(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
