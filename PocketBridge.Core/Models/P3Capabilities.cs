namespace PocketBridge.Core.Models;

public enum CapabilityState { Supported, Unsupported, RequiresUsb, RequiresExclusiveConnection, Unknown }
public enum InputBackendKind { Automatic, ScrcpyControl, AdbInput, HidInput }

public sealed record HidOtgCapabilities(CapabilityState HidKeyboard, CapabilityState HidMouse, CapabilityState Otg, string Explanation);
public sealed record DisplayDescriptor(int DisplayId, string Name, int? Width, int? Height);
public sealed record DisplayCapabilities(bool CanListDisplays, CapabilityState VirtualDisplay, IReadOnlyList<DisplayDescriptor> Displays, string Explanation);

public static class InputBackendSelector
{
    public static InputBackendKind Select(InputBackendKind requested, HidOtgCapabilities capabilities) => requested switch
    {
        InputBackendKind.Automatic when capabilities.HidKeyboard == CapabilityState.Supported => InputBackendKind.HidInput,
        InputBackendKind.Automatic => InputBackendKind.ScrcpyControl,
        InputBackendKind.HidInput when capabilities.HidKeyboard != CapabilityState.Supported => InputBackendKind.ScrcpyControl,
        _ => requested
    };
}
