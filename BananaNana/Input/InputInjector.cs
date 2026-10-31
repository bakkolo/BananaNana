using System.Runtime.InteropServices;
using static BananaNana.Input.NativeMethods;
namespace BananaNana.Input;

// Sends synthetic keys with SendInput. Every event carries Marker so KeyboardInputService skips its own input.
public static class InputInjector
{
    // RAWKEYBOARD.ExtraInformation is 32-bit, so the marker must fit in a uint.
    public const uint Marker = 0xBA7A7A;

    const ushort VK_BACK = 0x08, VK_CONTROL = 0x11, VK_V = 0x56;
    // Left/right Shift, Ctrl, Alt and Win. A held Shift would turn Ctrl+V into "paste as plain text".
    static readonly ushort[] Modifiers = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    // Deletes the typed trigger and pastes, in one SendInput call so no user key lands in between.
    // Returns false when Windows blocked the input (e.g. the target app is elevated: UIPI).
    public static bool BackspacesThenPaste(int backspaces)
    {
        var inputs = new List<INPUT>();
        foreach (var vk in Modifiers)
            if (GetAsyncKeyState(vk) < 0) inputs.Add(Key(vk, up: true));
        for (int i = 0; i < backspaces; i++) { inputs.Add(Key(VK_BACK, false)); inputs.Add(Key(VK_BACK, true)); }
        inputs.AddRange([Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true)]);
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>()) == inputs.Count;
    }

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new() { ki = new() { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Marker } },
    };
}
