using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure.Embedded;

public sealed class EmbeddedSessionManager(IDeviceDisplaySessionFactory factory) : IEmbeddedSessionManager
{
    private readonly Dictionary<string, IEmbeddedDisplaySession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event EventHandler? SessionsChanged;
    public IReadOnlyCollection<IEmbeddedDisplaySession> Sessions { get { lock (_sessions) return _sessions.Values.ToArray(); } }
    public IEmbeddedDisplaySession? Get(string serial) { lock (_sessions) return _sessions.GetValueOrDefault(serial); }

    public async Task<IEmbeddedDisplaySession> StartAsync(AndroidDevice device, ScrcpyLaunchOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = Get(device.Serial);
            if (existing is not null)
            {
                if (!existing.IsRunning) await existing.StartAsync(cancellationToken).ConfigureAwait(false);
                return existing;
            }
            var session = factory.CreateEmbedded(device, options ?? new ScrcpyLaunchOptions());
            session.StateChanged += OnStateChanged;
            lock (_sessions) _sessions.Add(device.Serial, session);
            try { await session.StartAsync(cancellationToken).ConfigureAwait(false); }
            catch
            {
                lock (_sessions) _sessions.Remove(device.Serial);
                session.StateChanged -= OnStateChanged;
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return session;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(string serial, CancellationToken cancellationToken = default)
    {
        IEmbeddedDisplaySession? session;
        lock (_sessions) { _sessions.Remove(serial, out session); }
        if (session is null) return;
        session.StateChanged -= OnStateChanged;
        await session.DisposeAsync().ConfigureAwait(false);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs e) => SessionsChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        IEmbeddedDisplaySession[] sessions;
        lock (_sessions) { sessions = _sessions.Values.ToArray(); _sessions.Clear(); }
        foreach (var session in sessions) await session.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
