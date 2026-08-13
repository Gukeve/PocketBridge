using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class XInputGamepadService : IGamepadService
{
    private readonly CancellationTokenSource _lifetime = new(); private readonly ConcurrentDictionary<int, GamepadState> _current = new(); private readonly Task _worker;
    public XInputGamepadService() => _worker = Task.Run(PollAsync);
    public event EventHandler<GamepadState>? StateChanged;
    public IReadOnlyList<GamepadState> Current => _current.Values.OrderBy(item => item.Controller).ToArray();
    private async Task PollAsync()
    {
        var packets = new uint[4];
        while (!_lifetime.IsCancellationRequested)
        {
            for (var index = 0; index < 4; index++)
            {
                var connected = XInputGetState((uint)index, out var state) == 0;
                if (!connected) { if (_current.TryRemove(index, out _)) StateChanged?.Invoke(this, new(index + 1, false, 0, 0, 0, 0, 0, 0, 0)); continue; }
                if (_current.ContainsKey(index) && packets[index] == state.PacketNumber) continue;
                packets[index] = state.PacketNumber; var pad = state.Gamepad;
                var value = new GamepadState(index + 1, true, pad.Buttons, Axis(pad.ThumbLX), Axis(pad.ThumbLY), Axis(pad.ThumbRX), Axis(pad.ThumbRY), pad.LeftTrigger / 255f, pad.RightTrigger / 255f);
                _current[index] = value; StateChanged?.Invoke(this, value);
            }
            try { await Task.Delay(16, _lifetime.Token); } catch (OperationCanceledException) { }
        }
    }
    private static float Axis(short value) => value < 0 ? value / 32768f : value / 32767f;
    public void Dispose() { _lifetime.Cancel(); try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } _lifetime.Dispose(); }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint userIndex, out XInputState state);
    [StructLayout(LayoutKind.Sequential)] private struct XInputState { public uint PacketNumber; public XInputGamepad Gamepad; }
    [StructLayout(LayoutKind.Sequential)] private struct XInputGamepad { public ushort Buttons; public byte LeftTrigger; public byte RightTrigger; public short ThumbLX; public short ThumbLY; public short ThumbRX; public short ThumbRY; }
}
