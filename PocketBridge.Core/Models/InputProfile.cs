namespace PocketBridge.Core.Models;

public enum InputActionKind { UiCommand, AndroidKey, TouchGesture, GamepadButton }

public sealed record InputAction(InputActionKind Kind, string Value);
public sealed record KeyBinding(string Input, InputAction Action);
public sealed record InputProfile(string Name, IReadOnlyList<KeyBinding> Bindings)
{
    public static InputProfile Default { get; } = new("Default", Array.Empty<KeyBinding>());
}
