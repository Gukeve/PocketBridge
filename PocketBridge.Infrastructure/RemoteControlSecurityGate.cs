using System.Collections.Concurrent;
using PocketBridge.Core.Models;

namespace PocketBridge.Infrastructure;

public sealed class RemoteControlSecurityGate(string allowedOrigin, int maximumRequestsPerSecond = 30)
{
    private readonly ConcurrentDictionary<Guid, State> _states = new();
    public RemoteControlDecision Validate(RemoteSession? session, Guid expectedSessionId, string? origin, string action, long sequence, string? nonce, long timestampUnixMs, DateTimeOffset now)
    {
        if (session is null || session.Id != expectedSessionId || session.IsExpired(now)) return new(false, "inactive or expired session");
        if (!string.Equals(origin?.TrimEnd('/'), allowedOrigin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return new(false, "invalid origin");
        if (RequiredPermission(action) is not { } required || (session.Permissions & required) != required) return new(false, "permission denied");
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length is < 16 or > 128) return new(false, "invalid nonce");
        DateTimeOffset timestamp; try { timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMs); } catch (ArgumentOutOfRangeException) { return new(false, "invalid timestamp"); }
        if ((timestamp - now).Duration() > TimeSpan.FromSeconds(30)) return new(false, "stale or future timestamp");
        var state = _states.GetOrAdd(expectedSessionId, _ => new State());
        lock (state)
        {
            if (sequence <= state.LastSequence || !state.Nonces.Add(nonce)) return new(false, "replay rejected");
            while (state.Nonces.Count > 128) state.Nonces.Remove(state.Nonces.First());
            var second = now.ToUnixTimeSeconds(); if (state.RateSecond != second) { state.RateSecond = second; state.RateCount = 0; }
            if (++state.RateCount > maximumRequestsPerSecond) return new(false, "rate limit exceeded");
            state.LastSequence = sequence;
        }
        return new(true, "accepted");
    }
    public void Revoke(Guid sessionId) => _states.TryRemove(sessionId, out _);
    public static RemotePermission? RequiredPermission(string action) => action.ToLowerInvariant() switch { "tap" or "touchdown" or "touchup" => RemotePermission.ControlTouch, "key" => RemotePermission.KeyboardInput, "back" or "home" or "recents" => RemotePermission.DeviceButtons, _ => null };
    private sealed class State { public long LastSequence; public readonly HashSet<string> Nonces = new(StringComparer.Ordinal); public long RateSecond; public int RateCount; }
}
public sealed record RemoteControlDecision(bool Allowed, string Reason);
