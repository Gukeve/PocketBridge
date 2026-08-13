using System.Text.Json;

namespace PocketBridge.Core.Models;

public enum InputActionKind { UiCommand, AndroidKey, TouchPoint, TouchRegion, Swipe, VirtualJoystick, FreeLook, GamepadButton }
public enum InputSourceKind { KeyboardKey, MouseButton, GamepadButton, GamepadAxis }

public sealed record NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Clamp() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1));
    public (int X, int Y) ToPixels(int width, int height, int rotation = 0)
    {
        var point = Clamp(); var transformed = (rotation % 4) switch { 1 => new NormalizedPoint(1 - point.Y, point.X), 2 => new NormalizedPoint(1 - point.X, 1 - point.Y), 3 => new NormalizedPoint(point.Y, 1 - point.X), _ => point };
        return ((int)Math.Round(transformed.X * width), (int)Math.Round(transformed.Y * height));
    }
}
public sealed record NormalizedRegion(NormalizedPoint Start, NormalizedPoint End);
public sealed record InputAction(InputActionKind Kind, string Value, NormalizedPoint? Point = null, NormalizedRegion? Region = null);
public sealed record KeyBinding(InputSourceKind SourceKind, string Input, InputAction Action, double DeadZone = 0.18);
public sealed record InputProfile(Guid Id, string Name, IReadOnlyList<KeyBinding> Bindings, string? TargetAlias = null, int SchemaVersion = 1)
{
    public static InputProfile Default { get; } = new(Guid.Parse("76c09b67-3b6d-46c3-83b4-85e918325565"), "Default", Array.Empty<KeyBinding>());
    public InputProfile Portable() => this with { TargetAlias = null };
}

public static class InputProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string Export(InputProfile profile) => JsonSerializer.Serialize(profile.Portable(), Options);
    public static InputProfile Import(string json)
    {
        var profile = JsonSerializer.Deserialize<InputProfile>(json, Options) ?? throw new InvalidDataException("Invalid input profile.");
        if (profile.SchemaVersion != 1 || profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Unsupported input profile format.");
        return profile.Portable();
    }
}
