using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class RemoteSessionService : IRemoteSessionService
{
    private readonly Dictionary<Guid, RemoteSession> _sessions = new();
    public event EventHandler<Guid>? Revoked;
    public (RemoteSession Session, string Token, string PairingCode) Create(RemotePermission permissions, TimeSpan lifetime)
    {
        var bytes = RandomNumberGenerator.GetBytes(32); var token = Convert.ToBase64String(bytes); var pairing = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var session = new RemoteSession(Guid.NewGuid(), Hash(token), DateTimeOffset.UtcNow + lifetime, permissions, $"Remote session {pairing[^2..]}"); lock (_sessions) _sessions[session.Id] = session; return (session, token, pairing);
    }
    public RemoteSession? Authenticate(string token, DateTimeOffset now) { var hash = Hash(token); lock (_sessions) return _sessions.Values.FirstOrDefault(item => !item.IsExpired(now) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(item.TokenHash), Convert.FromHexString(hash))); }
    public bool IsActive(Guid id, DateTimeOffset now) { lock (_sessions) return _sessions.TryGetValue(id, out var session) && !session.IsExpired(now); }
    public void Revoke(Guid id) { lock (_sessions) _sessions.Remove(id); Revoked?.Invoke(this, id); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly LinkedList<AuditRecord> _items = new(); private readonly int _maximum; private readonly string _path;
    public AuditLogService(int maximum = 500, string? path = null)
    {
        _maximum = Math.Max(1, maximum);
        var dataRoot = Environment.GetEnvironmentVariable("POCKETBRIDGE_DATA_ROOT");
        _path = path ?? Path.Combine(string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketBridge")
            : Path.GetFullPath(dataRoot), "audit-log.json");
        Load();
    }
    public event EventHandler? Changed;
    public IReadOnlyList<AuditRecord> Items { get { lock (_items) return _items.ToArray(); } }
    public void Append(AuditRecord record) { record = Sanitize(record); lock (_items) { _items.AddFirst(record); while (_items.Count > _maximum) _items.RemoveLast(); Persist(); } Changed?.Invoke(this, EventArgs.Empty); }
    public void Clear() { lock (_items) { _items.Clear(); Persist(); } Changed?.Invoke(this, EventArgs.Empty); }
    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var item in JsonSerializer.Deserialize<AuditRecord[]>(File.ReadAllText(_path)) ?? []) { _items.AddLast(item); if (_items.Count == _maximum) break; }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Move(_path, _path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", true); } catch (IOException) { }
        }
    }
    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(_items)); File.Move(temporary, _path, true);
    }
    private static AuditRecord Sanitize(AuditRecord value)
    {
        static string Text(string text, int maximum) => new(text.Where(character => !char.IsControl(character)).Take(maximum).ToArray());
        var alias = Text(value.DeviceAlias, 64); if (alias.Contains(':') || alias.Length > 24 && alias.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')) alias = alias.Length <= 4 ? "Device" : $"Device ••••{alias[^4..]}";
        return value with { Actor = Text(value.Actor, 64), DeviceAlias = alias, Action = Text(value.Action, 128), Result = Text(value.Result, 160), Category = Text(value.Category, 40) };
    }
}

public sealed class AutomationService(IAppSettingsService settings, IAuditLogService audit, IAutomationActionExecutor executor) : IAutomationService
{
    public IReadOnlyList<AutomationRule> Rules => settings.Load().AutomationRules;
    public async Task SaveAsync(AutomationRule rule) { var current = settings.Load(); await settings.SaveAsync(current with { AutomationRules = current.AutomationRules.Where(item => item.Id != rule.Id).Append(rule).ToArray() }); audit.Append(new(DateTimeOffset.Now, "Local user", rule.TargetAlias ?? "Generic", "Save automation rule", "Success")); }
    public async Task DeleteAsync(Guid id) { var current = settings.Load(); await settings.SaveAsync(current with { AutomationRules = current.AutomationRules.Where(item => item.Id != id).ToArray() }); audit.Append(new(DateTimeOffset.Now, "Local user", "Generic", "Delete automation rule", "Success")); }
    public bool CanExecute(AutomationRule rule, bool destructiveOptIn) => AutomationPermissionPolicy.IsAllowed(rule, destructiveOptIn);
    public async Task<IReadOnlyList<AutomationExecutionResult>> DispatchAsync(AutomationEvent automationEvent, bool destructiveOptIn = false, CancellationToken cancellationToken = default)
    {
        var results = new List<AutomationExecutionResult>();
        foreach (var rule in Rules.Where(item => item.Enabled && item.Trigger == automationEvent.Trigger && (item.TargetAlias is null || string.Equals(item.TargetAlias, automationEvent.Device.FriendlyName, StringComparison.Ordinal))))
        {
            AutomationExecutionResult result;
            if (!CanExecute(rule, destructiveOptIn)) result = new(false, "Denied by permission/risk policy");
            else if (rule.Trigger == AutomationTrigger.BatteryBelowThreshold && (!int.TryParse(rule.Argument, out var threshold) || automationEvent.BatteryPercent is null || automationEvent.BatteryPercent >= threshold)) continue;
            else result = await executor.ExecuteAsync(rule, automationEvent.Device, cancellationToken);
            results.Add(result); audit.Append(new(DateTimeOffset.Now, "Automation", automationEvent.Device.FriendlyName, $"{rule.Trigger}: {rule.Action}", result.Success ? "Success" : $"Denied/failed: {result.Result}"));
            await SaveAsync(rule with { LastRun = DateTimeOffset.Now, LastResult = result.Result });
        }
        return results;
    }
}
