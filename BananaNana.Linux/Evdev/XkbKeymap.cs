using System.Runtime.InteropServices;
using System.Text;

namespace BananaNana.Linux.Evdev;

// Layout-aware translation through libxkbcommon, the same engine the compositor uses. Every key down and up
// from every keyboard goes into one xkb state, so Shift, CapsLock, AltGr and dead keys come from xkb itself.
// Evdev codes are xkb keycodes minus 8.
public sealed class XkbKeymap : IKeyMap
{
    const string Lib = "libxkbcommon.so.0";
    const int XkbKeyUp = 0, XkbKeyDown = 1;
    const int ComponentEffective = 1 << 3;                     // XKB_STATE_*_EFFECTIVE
    const int ModsDepressed = 1 << 0, ModsLatched = 1 << 1, ModsLocked = 1 << 2;
    const int LayoutDepressed = 1 << 0, LayoutLatched = 1 << 1;
    const int KeyBufferSize = 16;

    // Keys that only change modifier state: never text, never a reset.
    static readonly HashSet<int> ModifierKeys = [29, 42, 54, 56, 58, 69, 70, 97, 100, 125, 126]; // Ctrl, Shift, Alt, CapsLock, NumLock, ScrollLock, AltGr, Meta
    static readonly HashSet<int> ContextKeys =
    [
        EvKey.Tab, EvKey.Enter, EvKey.Esc, EvKey.PageUp, EvKey.PageDown, EvKey.End, EvKey.Home,
        EvKey.Left, EvKey.Up, EvKey.Right, EvKey.Down, EvKey.Insert, EvKey.Delete,
    ];

    IntPtr context, keymap, state;

    public LayoutConfig Layout { get; }

    // Throws DllNotFoundException when libxkbcommon.so.0 is missing, and InvalidOperationException when the
    // layout cannot be compiled.
    public XkbKeymap(LayoutConfig layout)
    {
        Layout = layout;
        try
        {
            context = xkb_context_new(0);
            if (context == IntPtr.Zero) throw new InvalidOperationException("xkb_context_new failed.");

            var names = new RuleNames
            {
                Rules = ToNative("evdev"),
                Model = ToNative(layout.Model),
                Layout = ToNative(layout.LayoutString),
                Variant = ToNative(layout.VariantString),
                Options = ToNative(layout.Options),
            };
            try
            {
                keymap = xkb_keymap_new_from_names(context, ref names, 0);
            }
            finally
            {
                foreach (var p in new[] { names.Rules, names.Model, names.Layout, names.Variant, names.Options }) Free(p);
            }
            if (keymap == IntPtr.Zero) throw new InvalidOperationException($"xkb cannot compile the layout '{layout.Describe(0)}'.");

            state = xkb_state_new(keymap);
            if (state == IntPtr.Zero) throw new InvalidOperationException("xkb_state_new failed.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public KeyResult Down(int code, bool isRepeat = false)
    {
        uint xkbCode = (uint)code + 8;
        // Text and shortcut state are read before the key changes the state, as xkb expects.
        string text = Utf8(xkbCode);
        bool shortcut = !IsActive("Mod5") && (IsActive("Control") || IsActive("Mod1") || IsActive("Mod4"));
        // A repeat means the key is still down, so xkb must not see a second press (CapsLock would toggle).
        if (!isRepeat) xkb_state_update_key(state, xkbCode, XkbKeyDown);
        return Classify(code, text, shortcut);
    }

    public void Up(int code) => xkb_state_update_key(state, (uint)code + 8, XkbKeyUp);

    public void SetGroup(uint group)
    {
        // Keep the held and latched modifiers; only the locked group changes.
        uint depressedMods = xkb_state_serialize_mods(state, ModsDepressed);
        uint latchedMods = xkb_state_serialize_mods(state, ModsLatched);
        uint lockedMods = xkb_state_serialize_mods(state, ModsLocked);
        uint depressedLayout = xkb_state_serialize_layout(state, LayoutDepressed);
        uint latchedLayout = xkb_state_serialize_layout(state, LayoutLatched);
        xkb_state_update_mask(state, depressedMods, latchedMods, lockedMods, depressedLayout, latchedLayout, group);
    }

    public int CodeFor(char c)
    {
        string lower = char.ToLowerInvariant(c).ToString(), upper = char.ToUpperInvariant(c).ToString();
        int upperCode = -1;
        for (int code = 1; code <= 247; code++)
        {
            string text = Utf8((uint)code + 8);
            if (text == lower) return code;
            if (upperCode < 0 && text == upper) upperCode = code;
        }
        return upperCode;
    }

    // Shortcut rule as on Windows: Ctrl, Alt or Meta held with a key. AltGr (Mod5 / Level3) is text, not a shortcut.
    static KeyResult Classify(int code, string text, bool shortcut)
    {
        if (ModifierKeys.Contains(code)) return new KeyResult(KeyAction.Ignore);
        if (code is >= EvKey.BtnFirst and <= EvKey.BtnLast) return new KeyResult(KeyAction.Reset);
        if (code == EvKey.Backspace) return new KeyResult(KeyAction.Backspace);
        if (ContextKeys.Contains(code)) return new KeyResult(KeyAction.Reset);
        if (shortcut) return new KeyResult(KeyAction.Reset);
        if (code == EvKey.Space) return new KeyResult(KeyAction.Char, ' ');
        // Empty text means a dead key or a key with no character; a multi-char text (surrogate pair) is not supported.
        if (text.Length == 1 && !char.IsControl(text[0])) return new KeyResult(KeyAction.Char, text[0]);
        return new KeyResult(KeyAction.Ignore);
    }

    string Utf8(uint xkbCode)
    {
        var buf = new byte[KeyBufferSize];
        int n = xkb_state_key_get_utf8(state, xkbCode, buf, (nuint)buf.Length);
        return n > 0 ? Encoding.UTF8.GetString(buf, 0, Math.Min(n, buf.Length - 1)) : "";
    }

    bool IsActive(string modName) => xkb_state_mod_name_is_active(state, modName, ComponentEffective) > 0;

    static IntPtr ToNative(string? s) => s is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(s);

    static void Free(IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
    }

    public void Dispose()
    {
        if (state != IntPtr.Zero) { xkb_state_unref(state); state = IntPtr.Zero; }
        if (keymap != IntPtr.Zero) { xkb_keymap_unref(keymap); keymap = IntPtr.Zero; }
        if (context != IntPtr.Zero) { xkb_context_unref(context); context = IntPtr.Zero; }
    }

    // struct xkb_rule_names: five const char* fields.
    [StructLayout(LayoutKind.Sequential)]
    struct RuleNames
    {
        public IntPtr Rules, Model, Layout, Variant, Options;
    }

    [DllImport(Lib)] static extern IntPtr xkb_context_new(int flags);
    [DllImport(Lib)] static extern void xkb_context_unref(IntPtr context);
    [DllImport(Lib)] static extern IntPtr xkb_keymap_new_from_names(IntPtr context, ref RuleNames names, int flags);
    [DllImport(Lib)] static extern void xkb_keymap_unref(IntPtr keymap);
    [DllImport(Lib)] static extern IntPtr xkb_state_new(IntPtr keymap);
    [DllImport(Lib)] static extern void xkb_state_unref(IntPtr state);
    [DllImport(Lib)] static extern int xkb_state_update_key(IntPtr state, uint key, int direction);
    [DllImport(Lib)] static extern int xkb_state_key_get_utf8(IntPtr state, uint key, byte[] buffer, nuint size);
    [DllImport(Lib)] static extern uint xkb_state_serialize_mods(IntPtr state, int components);
    [DllImport(Lib)] static extern uint xkb_state_serialize_layout(IntPtr state, int components);
    [DllImport(Lib)] static extern int xkb_state_update_mask(IntPtr state, uint depressedMods, uint latchedMods, uint lockedMods,
        uint depressedLayout, uint latchedLayout, uint lockedLayout);
    [DllImport(Lib)] static extern int xkb_state_mod_name_is_active(IntPtr state,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int type);
}
