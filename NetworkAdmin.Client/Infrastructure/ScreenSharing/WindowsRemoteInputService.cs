using System.Runtime.InteropServices;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

internal sealed class WindowsRemoteInputService : IRemoteInputService
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseVirtualDesk = 0x4000, MouseAbsolute = 0x8000;
    private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
    private const uint MiddleDown = 0x0020, MiddleUp = 0x0040, MouseWheel = 0x0800;
    private const uint KeyUp = 0x0002;
    private readonly object _sync = new();
    private readonly HashSet<ushort> _pressedKeys = [];

    public bool Execute(RemoteInputEventMessage input)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return input.Kind switch
        {
            RemoteInputEventKind.MouseMove => Send([MouseAt(input.X, input.Y, 0, 0)]),
            RemoteInputEventKind.MouseClick => Click(input),
            RemoteInputEventKind.MouseWheel => Send([MouseAt(input.X, input.Y, unchecked((uint)input.WheelDelta), MouseWheel)]),
            RemoteInputEventKind.KeyDown => Key(input.VirtualKeyCode, up: false),
            RemoteInputEventKind.KeyUp => Key(input.VirtualKeyCode, up: true),
            _ => false
        };
    }

    public void ReleaseAll()
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (_sync)
        {
            if (_pressedKeys.Count == 0) return;
            var release = _pressedKeys.Select(key => Keyboard(key, KeyUp)).ToArray();
            _pressedKeys.Clear();
            _ = Send(release);
        }
    }

    private bool Click(RemoteInputEventMessage input)
    {
        var (down, up) = input.Button switch
        {
            RemoteMouseButton.Left => (LeftDown, LeftUp),
            RemoteMouseButton.Right => (RightDown, RightUp),
            RemoteMouseButton.Middle => (MiddleDown, MiddleUp),
            _ => (0u, 0u)
        };
        return down != 0 && Send([MouseAt(input.X, input.Y, 0, 0), MouseAt(input.X, input.Y, 0, down), MouseAt(input.X, input.Y, 0, up)]);
    }

    private bool Key(int key, bool up)
    {
        lock (_sync)
        {
            if (up) _pressedKeys.Remove((ushort)key);
            else _pressedKeys.Add((ushort)key);
            return Send([Keyboard((ushort)key, up ? KeyUp : 0)]);
        }
    }

    private static Input MouseAt(int x, int y, uint data, uint flags)
    {
        var absoluteX = Math.Clamp((int)Math.Round((double)x * 65535 / RemoteInputEventPolicy.MaxCoordinate), 0, 65535);
        var absoluteY = Math.Clamp((int)Math.Round((double)y * 65535 / RemoteInputEventPolicy.MaxCoordinate), 0, 65535);
        return new Input { Type = InputMouse, Union = new InputUnion { Mouse = new MouseInput
        {
            X = (uint)absoluteX, Y = (uint)absoluteY, Data = data,
            Flags = flags | MouseMove | MouseAbsolute | MouseVirtualDesk, ExtraInfo = 0
        } } };
    }

    private static Input Keyboard(ushort virtualKey, uint flags) =>
        new() { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = flags } } };

    private static bool Send(Input[] inputs) => inputs.Length > 0 && SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == (uint)inputs.Length;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public uint X, Y, Data, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nint ExtraInfo;
    }
}
