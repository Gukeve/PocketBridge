using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class RemoteSessionService : IRemoteSessionService
{
    private readonly Dictionary<Guid, RemoteSession> _sessions = new();
    public (RemoteSession Session, string Token, string PairingCode) Create(RemotePermission permissions, TimeSpan lifetime)
    {
        var bytes = RandomNumberGenerator.GetBytes(32); var token = Convert.ToBase64String(bytes); var pairing = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var session = new RemoteSession(Guid.NewGuid(), Hash(token), DateTimeOffset.UtcNow + lifetime, permissions, $"Remote session {pairing[^2..]}"); lock (_sessions) _sessions[session.Id] = session; return (session, token, pairing);
    }
    public RemoteSession? Authenticate(string token, DateTimeOffset now) { var hash = Hash(token); lock (_sessions) return _sessions.Values.FirstOrDefault(item => !item.IsExpired(now) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(item.TokenHash), Convert.FromHexString(hash))); }
    public void Revoke(Guid id) { lock (_sessions) _sessions.Remove(id); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly LinkedList<AuditRecord> _items = new(); private readonly int _maximum;
    public AuditLogService(int maximum = 500) => _maximum = maximum;
    public IReadOnlyList<AuditRecord> Items { get { lock (_items) return _items.ToArray(); } }
    public void Append(AuditRecord record) { lock (_items) { _items.AddFirst(record); while (_items.Count > _maximum) _items.RemoveLast(); } }
    public void Clear() { lock (_items) _items.Clear(); }
}

public sealed class AutomationService(IAppSettingsService settings, IAuditLogService audit) : IAutomationService
{
    public IReadOnlyList<AutomationRule> Rules => settings.Load().AutomationRules;
    public async Task SaveAsync(AutomationRule rule) { var current = settings.Load(); await settings.SaveAsync(current with { AutomationRules = current.AutomationRules.Where(item => item.Id != rule.Id).Append(rule).ToArray() }); audit.Append(new(DateTimeOffset.Now, "Local user", rule.TargetAlias ?? "Generic", "Save automation rule", "Success")); }
    public async Task DeleteAsync(Guid id) { var current = settings.Load(); await settings.SaveAsync(current with { AutomationRules = current.AutomationRules.Where(item => item.Id != id).ToArray() }); audit.Append(new(DateTimeOffset.Now, "Local user", "Generic", "Delete automation rule", "Success")); }
    public bool CanExecute(AutomationRule rule, bool destructiveOptIn) => AutomationPermissionPolicy.IsAllowed(rule, destructiveOptIn);
}
