namespace BananaNana.Linux.Evdev;

// evdev key codes (linux/input-event-codes.h) used by the key map.
public static class EvKey
{
    public const int Esc = 1, Backspace = 14, Tab = 15, Enter = 28, LCtrl = 29, LShift = 42, RShift = 54,
        LAlt = 56, Space = 57, CapsLock = 58, RCtrl = 97, RAlt = 100, Home = 102, Up = 103, PageUp = 104,
        Left = 105, Right = 106, End = 107, Down = 108, PageDown = 109, Insert = 110, Delete = 111,
        LMeta = 125, RMeta = 126, KeyA = 30, KeyV = 47;
    public const int BtnFirst = 0x110, BtnLast = 0x11F; // BTN_LEFT .. mouse and other buttons
}

public enum KeyAction { Ignore, Char, Backspace, Reset }

public readonly record struct KeyResult(KeyAction Action, char Char = '\0');

// Turns evdev codes into the same actions the Windows KeyboardInputService produces, for a US layout.
// Backspace removes a character, the context keys and mouse buttons reset the word, Ctrl or Alt alone and
// Meta are shortcuts (reset), and modifiers and CapsLock type nothing.
public sealed class KeyMap
{
    static readonly HashSet<int> ContextKeys =
    [
        EvKey.Tab, EvKey.Enter, EvKey.Esc, EvKey.PageUp, EvKey.PageDown, EvKey.End, EvKey.Home,
        EvKey.Left, EvKey.Up, EvKey.Right, EvKey.Down, EvKey.Insert, EvKey.Delete,
    ];
    static readonly Dictionary<int, (char Plain, char Shift)> Printable = Build();

    bool lShift, rShift, lCtrl, rCtrl, lAlt, rAlt, meta;

    public bool CapsLock { get; private set; }

    // isRepeat is true for auto-repeat; CapsLock only toggles on a real press.
    public KeyResult Down(int code, bool isRepeat = false)
    {
        if (code == EvKey.CapsLock)
        {
            if (!isRepeat) CapsLock = !CapsLock;
            return Ignore;
        }
        if (SetModifier(code, true)) return Ignore;
        if (code is >= EvKey.BtnFirst and <= EvKey.BtnLast) return Reset;
        if (code == EvKey.Backspace) return new KeyResult(KeyAction.Backspace);
        if (ContextKeys.Contains(code)) return Reset;

        bool ctrl = lCtrl || rCtrl, alt = lAlt || rAlt;
        // Same rule as Windows: Ctrl or Alt alone, or Meta, is a shortcut. Note that on Linux AltGr is
        // RAlt alone, so AltGr resets the word here (Windows sends Ctrl+Alt for AltGr instead).
        if (meta || ctrl != alt) return Reset;
        if (ctrl) return Ignore;

        if (!Printable.TryGetValue(code, out var ch)) return Ignore;
        bool shift = lShift || rShift;
        // CapsLock only changes letters; digits and symbols follow Shift alone.
        bool useShifted = char.IsLetter(ch.Plain) ? shift ^ CapsLock : shift;
        return new KeyResult(KeyAction.Char, useShifted ? ch.Shift : ch.Plain);
    }

    public void Up(int code) => SetModifier(code, false);

    static KeyResult Ignore => new(KeyAction.Ignore);
    static KeyResult Reset => new(KeyAction.Reset);

    // Returns true when code is a modifier; its state is tracked.
    bool SetModifier(int code, bool down)
    {
        switch (code)
        {
            case EvKey.LShift: lShift = down; return true;
            case EvKey.RShift: rShift = down; return true;
            case EvKey.LCtrl: lCtrl = down; return true;
            case EvKey.RCtrl: rCtrl = down; return true;
            case EvKey.LAlt: lAlt = down; return true;
            case EvKey.RAlt: rAlt = down; return true;
            case EvKey.LMeta:
            case EvKey.RMeta: meta = down; return true;
            default: return false;
        }
    }

    static Dictionary<int, (char, char)> Build()
    {
        var d = new Dictionary<int, (char, char)>();
        Row(d, 2, "1234567890-=", "!@#$%^&*()_+");
        Row(d, 16, "qwertyuiop[]", "QWERTYUIOP{}");
        Row(d, 30, "asdfghjkl;'`", "ASDFGHJKL:\"~");
        Row(d, 43, "\\", "|");
        Row(d, 44, "zxcvbnm,./", "ZXCVBNM<>?");
        d[EvKey.Space] = (' ', ' ');
        return d;
    }

    static void Row(Dictionary<int, (char, char)> d, int firstCode, string plain, string shifted)
    {
        for (int i = 0; i < plain.Length; i++) d[firstCode + i] = (plain[i], shifted[i]);
    }
}
