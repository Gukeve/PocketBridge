namespace PocketBridge.Core.Models;

[Flags] public enum RemotePermission { None = 0, ViewScreen = 1, ControlTouch = 2, KeyboardInput = 4, Clipboard = 8, FilesUpload = 16, FilesDownload = 32, InstallApk = 64, DeviceButtons = 128, AdbConsole = 256, ApplicationManager = 512, Recording = 1024 }
public enum AutomationRisk { Safe, Interactive, Destructive }
public enum AutomationTrigger { DeviceConnected, DeviceDisconnected, SessionStarted, BatteryBelowThreshold, WifiAvailable }
public enum AutomationAction { StartMirroring, TakeScreenshot, StartRecording, StopRecording, LaunchApp, Notify, SendKey, TransferFile, Reboot, Uninstall, ClearData }
public sealed record AutomationRule(Guid Id, string Name, bool Enabled, AutomationTrigger Trigger, AutomationAction Action, AutomationRisk Risk, string? TargetAlias, RemotePermission GrantedPermissions, DateTimeOffset? LastRun = null, string? LastResult = null, string? Argument = null);
public sealed record AutomationEvent(AutomationTrigger Trigger, AndroidDevice Device, int? BatteryPercent = null, string? WifiSsid = null);
public sealed record AutomationExecutionResult(bool Success, string Result);
public sealed record AuditRecord(DateTimeOffset Timestamp, string Actor, string DeviceAlias, string Action, string Result);
public sealed record RemoteSession(Guid Id, string TokenHash, DateTimeOffset ExpiresAt, RemotePermission Permissions, string Actor)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

public static class AutomationPermissionPolicy
{
    public static bool IsAllowed(AutomationRule rule, bool destructiveOptIn) => rule.Risk switch { AutomationRisk.Safe => true, AutomationRisk.Interactive => rule.GrantedPermissions != RemotePermission.None, AutomationRisk.Destructive => destructiveOptIn && rule.GrantedPermissions != RemotePermission.None, _ => false };
}
