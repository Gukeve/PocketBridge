namespace PocketBridge.Core.Models;

public static class ShortcutGestureFormatter
{
    public static string? Format(string key, bool control, bool shift, bool alt, bool windows)
    {
        if (string.IsNullOrWhiteSpace(key) || key is "None" or "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift" or "LeftAlt" or "RightAlt" or "LWin" or "RWin")
            return null;

        var parts = new List<string>(5);
        if (control) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (windows) parts.Add("Windows");
        parts.Add(key);
        return string.Join('+', parts);
    }
}
