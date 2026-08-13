namespace PocketBridge.Core.Models;

public enum ShortcutAction { Home, Back, Recents, Screenshot, ToggleRecording, ToggleFullscreen, SendClipboard, RefreshDevices, NextDevice, PreviousDevice }
public enum ShortcutScope { Global, SelectedDevice, EmbeddedView, MultiView }

public sealed record ShortcutBinding(ShortcutAction Action, ShortcutScope Scope, string Gesture)
{
    public static IReadOnlyList<ShortcutBinding> Defaults { get; } = new[]
    {
        new ShortcutBinding(ShortcutAction.Home, ShortcutScope.SelectedDevice, "Ctrl+H"),
        new ShortcutBinding(ShortcutAction.Back, ShortcutScope.SelectedDevice, "Alt+Left"),
        new ShortcutBinding(ShortcutAction.Recents, ShortcutScope.SelectedDevice, "Ctrl+R"),
        new ShortcutBinding(ShortcutAction.Screenshot, ShortcutScope.SelectedDevice, "Ctrl+Shift+S"),
        new ShortcutBinding(ShortcutAction.ToggleRecording, ShortcutScope.SelectedDevice, "Ctrl+Shift+R"),
        new ShortcutBinding(ShortcutAction.ToggleFullscreen, ShortcutScope.EmbeddedView, "F11"),
        new ShortcutBinding(ShortcutAction.SendClipboard, ShortcutScope.SelectedDevice, "Ctrl+Shift+V"),
        new ShortcutBinding(ShortcutAction.RefreshDevices, ShortcutScope.Global, "F5"),
        new ShortcutBinding(ShortcutAction.NextDevice, ShortcutScope.Global, "Ctrl+Tab"),
        new ShortcutBinding(ShortcutAction.PreviousDevice, ShortcutScope.Global, "Ctrl+Shift+Tab")
    };
}

public static class ShortcutBindingResolver
{
    public static IReadOnlyList<ShortcutBinding> ReplaceConflicts(IEnumerable<ShortcutBinding> bindings)
    {
        var result = new List<ShortcutBinding>();
        foreach (var binding in bindings.Where(item => !string.IsNullOrWhiteSpace(item.Gesture)))
        {
            result.RemoveAll(existing => existing.Scope == binding.Scope && string.Equals(existing.Gesture, binding.Gesture, StringComparison.OrdinalIgnoreCase));
            result.Add(binding with { Gesture = binding.Gesture.Trim() });
        }
        return result;
    }
}
