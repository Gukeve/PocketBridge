using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IInputMappingService
{
    IReadOnlyList<InputProfile> Profiles { get; }
    Task SaveAsync(InputProfile profile);
    Task DeleteAsync(Guid id);
    Task ExportAsync(Guid id, string path);
    Task<InputProfile> ImportAsync(string path);
    InputAction? Resolve(Guid profileId, InputSourceKind sourceKind, string input, double magnitude = 1);
}
public sealed record GamepadState(int Controller, bool Connected, ushort Buttons, float LeftX, float LeftY, float RightX, float RightY, float LeftTrigger, float RightTrigger);
public interface IGamepadService : IDisposable { event EventHandler<GamepadState>? StateChanged; IReadOnlyList<GamepadState> Current { get; } }
public interface IP3CapabilityService { Task<HidOtgCapabilities> DetectInputAsync(string serial); Task<DisplayCapabilities> DetectDisplaysAsync(string serial); }
public interface IAuditLogService { event EventHandler? Changed; IReadOnlyList<AuditRecord> Items { get; } void Append(AuditRecord record); void Clear(); }
public interface IRemoteSessionService { event EventHandler<Guid>? Revoked; (RemoteSession Session, string Token, string PairingCode) Create(RemotePermission permissions, TimeSpan lifetime); RemoteSession? Authenticate(string token, DateTimeOffset now); bool IsActive(Guid id, DateTimeOffset now); void Revoke(Guid id); }
public interface IAutomationActionExecutor { Task<AutomationExecutionResult> ExecuteAsync(AutomationRule rule, AndroidDevice device, CancellationToken cancellationToken = default); }
public interface IAutomationService { IReadOnlyList<AutomationRule> Rules { get; } Task SaveAsync(AutomationRule rule); Task DeleteAsync(Guid id); bool CanExecute(AutomationRule rule, bool destructiveOptIn); Task<IReadOnlyList<AutomationExecutionResult>> DispatchAsync(AutomationEvent automationEvent, bool destructiveOptIn = false, CancellationToken cancellationToken = default); }
public sealed record LanServerOptions(bool Enabled, string BindAddress = "127.0.0.1", int Port = 27183, string? AllowedOrigin = null);
public sealed record RemoteControlRequest(Guid SessionId, string TargetAlias, string Action, double X, double Y, int KeyCode, long Sequence);
public interface ILanServerService : IAsyncDisposable { event EventHandler<RemoteControlRequest>? ControlRequested; bool IsRunning { get; } Uri? Address { get; } Task StartAsync(LanServerOptions options, string token); Task StopAsync(); void PublishFrame(string serialAlias, VideoFrame frame); }
public sealed record VirtualDisplayOptions(int Width, int Height, int Dpi, string? PackageName = null);
public interface IVirtualDisplayService : IDisposable { Task StartAsync(AndroidDevice device, VirtualDisplayOptions options); Task StopAsync(string serial); }
public interface IInputBackendService { InputBackendKind Select(InputBackendKind requested, HidOtgCapabilities capabilities); Task StartOtgAsync(); Task StopOtgAsync(); }
