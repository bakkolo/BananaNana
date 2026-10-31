using System.Runtime.InteropServices;
namespace BananaNana.Input;

static class NativeMethods
{
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;
    public const int WM_INPUT = 0x00FF, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    public const uint RID_INPUT = 0x10000003, RIM_TYPEMOUSE = 0, RIM_TYPEKEYBOARD = 1;
    public const uint RIDEV_REMOVE = 0x00000001, RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    // MOUSEINPUT is the largest member and sets the union size SendInput checks via cbSize.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, [Out] byte[]? pData, ref uint pcbSize, uint cbSizeHeader);

    // CharSet.Unicode is required: the default (Ansi) marshals char[] as a byte buffer half the size
    // ToUnicodeEx writes into, which corrupts the native heap.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);
}
