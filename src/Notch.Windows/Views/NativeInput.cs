using System.Runtime.InteropServices;

namespace Notch.Windows.Views;

internal static class NativeInput
{
    public static void OpenEmojiPicker()
    {
        Input[] keys = [Key(0x5B), Key(0xBE), Key(0xBE, true), Key(0x5B, true)];
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != keys.Length)
        {
            // A partial injected chord must not leave the Windows key held down.
            Input[] releases = [Key(0xBE, true), Key(0x5B, true)];
            _ = SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
            throw new InvalidOperationException("Windows could not open the emoji picker.");
        }
    }
    private static Input Key(ushort code, bool up = false) => new() { Type = 1, Keyboard = new() { VirtualKey = code, Flags = up ? 2u : 0u } };
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] input, int size);
}
