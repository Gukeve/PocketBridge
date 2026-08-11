namespace PocketBridge.Core.Models;

public enum ClipboardUpdateSource
{
    Windows,
    Android
}

public sealed record ClipboardSyncUpdate(long Version, ClipboardUpdateSource Source, string Text);

public sealed class ClipboardSyncTracker
{
    private readonly object _gate = new();
    private long _version;
    private string? _lastSentToAndroid;
    private string? _lastAppliedToWindows;

    public ClipboardSyncUpdate? ObserveWindows(string text)
    {
        lock (_gate)
        {
            if (text == _lastAppliedToWindows || text == _lastSentToAndroid) return null;
            _lastSentToAndroid = text;
            return new ClipboardSyncUpdate(++_version, ClipboardUpdateSource.Windows, text);
        }
    }

    public ClipboardSyncUpdate? ObserveAndroid(string text)
    {
        lock (_gate)
        {
            if (text == _lastSentToAndroid || text == _lastAppliedToWindows) return null;
            _lastAppliedToWindows = text;
            return new ClipboardSyncUpdate(++_version, ClipboardUpdateSource.Android, text);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastSentToAndroid = null;
            _lastAppliedToWindows = null;
        }
    }
}
