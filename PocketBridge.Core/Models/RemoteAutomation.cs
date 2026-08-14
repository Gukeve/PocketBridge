namespace PocketBridge.Core.Models;

[Flags] public enum RemotePermission { None = 0, ViewScreen = 1, ControlTouch = 2, KeyboardInput = 4, Clipboard = 8, FilesUpload = 16, FilesDownload = 32, InstallApk = 64, DeviceButtons = 128, AdbConsole = 256, ApplicationManager = 512, Recording = 1024 }
public enum AutomationRisk { Safe, Interactive, Destructive }
public enum AutomationTrigger { DeviceConnected, DeviceDisconnected, SessionStarted, BatteryBelowThreshold, WifiAvailable }
public enum AutomationAction { StartMirroring, StopMirroring, TakeScreenshot, StartRecording, StopRecording, LaunchApp, Notify, SendKey, Home, Back, Power, VolumeUp, VolumeDown, TransferFile, Reboot, Uninstall, ClearData }
public sealed record AutomationRule(Guid Id, string Name, bool Enabled, AutomationTrigger Trigger, AutomationAction Action, AutomationRisk Risk, string? TargetAlias, RemotePermission GrantedPermissions, DateTimeOffset? LastRun = null, string? LastResult = null, string? Argument = null);
public sealed record AutomationEvent(AutomationTrigger Trigger, AndroidDevice Device, int? BatteryPercent = null, string? WifiSsid = null);
public sealed record AutomationExecutionResult(bool Success, string Result);
public enum AuditSeverity { Info, Warning, Error }
public sealed record AuditRecord(DateTimeOffset Timestamp, string Actor, string DeviceAlias, string Action, string Result, string Category = "General", AuditSeverity Severity = AuditSeverity.Info);
public sealed record RemoteSession(Guid Id, string TokenHash, DateTimeOffset ExpiresAt, RemotePermission Permissions, string Actor)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

public static class AutomationPermissionPolicy
{
    public static bool IsAllowed(AutomationRule rule, bool destructiveOptIn)
    {
        if (rule.Risk == AutomationRisk.Destructive && !destructiveOptIn) return false;
        var required = RequiredPermission(rule.Action); return required == RemotePermission.None || (rule.GrantedPermissions & required) == required;
    }
    public static RemotePermission RequiredPermission(AutomationAction action) => action switch
    {
        AutomationAction.StartMirroring or AutomationAction.StopMirroring or AutomationAction.TakeScreenshot => RemotePermission.ViewScreen,
        AutomationAction.StartRecording or AutomationAction.StopRecording => RemotePermission.Recording,
        AutomationAction.LaunchApp or AutomationAction.Uninstall or AutomationAction.ClearData => RemotePermission.ApplicationManager,
        AutomationAction.SendKey => RemotePermission.KeyboardInput,
        AutomationAction.Home or AutomationAction.Back or AutomationAction.Power or AutomationAction.VolumeUp or AutomationAction.VolumeDown or AutomationAction.Reboot => RemotePermission.DeviceButtons,
        AutomationAction.TransferFile => RemotePermission.FilesUpload,
        _ => RemotePermission.None
    };
}
