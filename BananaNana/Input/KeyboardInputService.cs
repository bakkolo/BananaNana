using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using static BananaNana.Input.NativeMethods;
namespace BananaNana.Input;

// Observes keyboard and mouse system-wide via Raw Input (RIDEV_INPUTSINK), per docs/RESEARCH.md:
// unlike WH_KEYBOARD_LL it cannot be silently unhooked and never delays other apps' input.
// It only observes; it never blocks keys and keeps no history. Create and use on the UI thread.
public sealed class KeyboardInputService : IDisposable
{
    public event Action<char>? CharTyped;
    public event Action? Backspace;
    // The caret may have moved or the context changed: click, focus switch, Enter, arrows, shortcuts.
    public event Action? Reset;

    const ushort VK_BACK = 0x08, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14;
    const ushort RI_MOUSE_ANY_BUTTON_DOWN = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100;
    // Keys that move the caret or end the line: Tab, Enter, Esc, PgUp..Down arrows, Insert, Delete.
    static readonly HashSet<ushort> ContextKeys = [0x09, 0x0D, 0x1B, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E];
    // Shift/Ctrl/Alt/Win (generic and left/right) on their own type nothing.
    static readonly HashSet<ushort> ModifierKeys = [VK_SHIFT, VK_CONTROL, VK_MENU, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    readonly int headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    readonly byte[] keyState = new byte[256];
    readonly char[] chars = new char[8];
    HwndSource? window;
    IntPtr lastForeground;
    bool capsLock;

    public void Start()
    {
        // A message-only window (parent HWND_MESSAGE) is enough to receive WM_INPUT.
        window = new HwndSource(new HwndSourceParameters("BananaNanaInput") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        window.AddHook(WndProc);
        capsLock = (GetKeyState(VK_CAPITAL) & 1) != 0;
        Register(RIDEV_INPUTSINK, window.Handle);
    }

    public void Dispose()
    {
        if (window == null) return;
        Register(RIDEV_REMOVE, IntPtr.Zero);
        window.Dispose();
        window = null;
    }

    static void Register(uint flags, IntPtr target)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = flags, hwndTarget = target }, // keyboard
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = flags, hwndTarget = target }, // mouse
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, (uint)headerSize);
        var data = new byte[size];
        if (GetRawInputData(lParam, RID_INPUT, data, ref size, (uint)headerSize) != size) return IntPtr.Zero;

        // RAWINPUT = header, then RAWKEYBOARD { MakeCode, Flags, Reserved, VKey, Message, ExtraInformation }
        // or RAWMOUSE { usFlags, (pad), usButtonFlags, ... }.
        uint type = BitConverter.ToUInt32(data, 0);
        if (type == RIM_TYPEMOUSE)
        {
            if ((BitConverter.ToUInt16(data, headerSize + 4) & RI_MOUSE_ANY_BUTTON_DOWN) != 0) Reset?.Invoke();
        }
        else if (type == RIM_TYPEKEYBOARD)
        {
            OnKey(vk: BitConverter.ToUInt16(data, headerSize + 6), scan: BitConverter.ToUInt16(data, headerSize),
                message: BitConverter.ToUInt32(data, headerSize + 8), extra: BitConverter.ToUInt32(data, headerSize + 12));
        }
        return IntPtr.Zero;
    }

    void OnKey(ushort vk, ushort scan, uint message, uint extra)
    {
        if (extra == InputInjector.Marker || message is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return;

        var foreground = GetForegroundWindow();
        if (foreground != lastForeground) { lastForeground = foreground; Reset?.Invoke(); }

        if (vk == VK_CAPITAL) { capsLock = !capsLock; return; }
        if (ModifierKeys.Contains(vk)) return;
        if (vk == VK_BACK) { Backspace?.Invoke(); return; }
        if (ContextKeys.Contains(vk)) { Reset?.Invoke(); return; }

        bool shift = Down(VK_SHIFT), ctrl = Down(VK_CONTROL), alt = Down(VK_MENU), win = Down(0x5B) || Down(0x5C);
        // Ctrl or Alt alone, or Win, is a shortcut. Ctrl+Alt together is AltGr, which Polish needs for ą, ę, ł...
        if (win || ctrl != alt) { Reset?.Invoke(); return; }

        Array.Clear(keyState);
        if (shift) keyState[VK_SHIFT] = 0x80;
        if (ctrl) keyState[VK_CONTROL] = keyState[0xA2] = 0x80;
        if (alt) keyState[VK_MENU] = keyState[0xA5] = 0x80;
        if (capsLock) keyState[VK_CAPITAL] = 0x01;
        // Translate with the foreground window's layout. Flag 0x4 leaves the system's dead-key state alone,
        // so the target app still composes accents; a dead key itself returns < 0 and is ignored here.
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(foreground, out _));
        int n = ToUnicodeEx(vk, scan, keyState, chars, chars.Length, 0x4, layout);
        for (int i = 0; i < n; i++)
            if (!char.IsControl(chars[i])) CharTyped?.Invoke(chars[i]);
    }

    static bool Down(int vk) => GetAsyncKeyState(vk) < 0;
}
