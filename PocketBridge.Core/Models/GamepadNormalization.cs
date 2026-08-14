namespace PocketBridge.Core.Models;

public static class GamepadNormalization
{
    public static float Axis(short value) => value < 0 ? value / 32768f : value / 32767f;
    public static float Trigger(byte value) => value / 255f;
}
