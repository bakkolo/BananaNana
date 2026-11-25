using BananaNana.Linux.Evdev;
using Xunit;

namespace BananaNana.Linux.Tests;

// These need libxkbcommon.so.0 and the xkeyboard-config data; they are skipped when either is missing.
public class XkbKeymapTests
{
    const int KeyQ = 16, KeyY = 21, KeyA = 30, Key2 = 3, Key3 = 4, KeyApostrophe = 40, KeyDot = 52;

    static KeyResult Ch(char c) => new(KeyAction.Char, c);

    // Probed once: a missing library or data directory skips every test here, it does not fail them.
    static readonly string? Unavailable = Probe();

    static string? Probe()
    {
        try
        {
            new XkbKeymap(LayoutConfig.Us).Dispose();
            return null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return e.Message;
        }
    }

    static XkbKeymap Open(string spec)
    {
        Skip.If(Unavailable is not null, $"libxkbcommon unavailable: {Unavailable}");
        return new XkbKeymap(LayoutConfig.Parse(spec));
    }

    static KeyResult Press(IKeyMap m, int code)
    {
        var r = m.Down(code);
        m.Up(code);
        return r;
    }

    static KeyResult WithShift(IKeyMap m, int code)
    {
        m.Down(EvKey.LShift);
        var r = Press(m, code);
        m.Up(EvKey.LShift);
        return r;
    }

    [SkippableFact]
    public void Gb_shift_2_is_double_quote()
    {
        using var m = Open("gb");
        Assert.Equal(Ch('"'), WithShift(m, Key2));
    }

    [SkippableFact]
    public void Gb_shift_3_is_pound_sign()
    {
        using var m = Open("gb");
        Assert.Equal(Ch('£'), WithShift(m, Key3));
    }

    [SkippableFact]
    public void Us_shift_2_is_at_sign()
    {
        using var m = Open("us");
        Assert.Equal(Ch('@'), WithShift(m, Key2));
    }

    [SkippableFact]
    public void Pl_altgr_a_is_a_with_ogonek_and_does_not_reset()
    {
        using var m = Open("pl");
        m.Down(EvKey.RAlt);
        var r = Press(m, KeyA);
        m.Up(EvKey.RAlt);
        Assert.Equal(Ch('ą'), r);
    }

    [SkippableFact]
    public void De_y_is_z()
    {
        using var m = Open("de");
        Assert.Equal(Ch('z'), Press(m, KeyY));
    }

    [SkippableFact]
    public void Fr_q_is_a()
    {
        using var m = Open("fr");
        Assert.Equal(Ch('a'), Press(m, KeyQ));
    }

    [SkippableFact]
    public void Ctrl_a_is_a_shortcut_that_resets()
    {
        using var m = Open("us");
        m.Down(EvKey.LCtrl);
        Assert.Equal(KeyAction.Reset, Press(m, KeyA).Action);
        m.Up(EvKey.LCtrl);
    }

    [SkippableFact]
    public void Modifier_only_keys_never_reset()
    {
        using var m = Open("pl");
        Assert.Equal(KeyAction.Ignore, m.Down(EvKey.LShift).Action);
        Assert.Equal(KeyAction.Ignore, m.Down(EvKey.RAlt).Action);
        Assert.Equal(KeyAction.Ignore, m.Down(EvKey.LMeta).Action);
        m.Up(EvKey.LMeta);
        m.Up(EvKey.RAlt);
        m.Up(EvKey.LShift);
    }

    [SkippableFact]
    public void Caps_lock_from_xkb_upper_cases_letters()
    {
        using var m = Open("us");
        m.Down(EvKey.CapsLock);
        m.Up(EvKey.CapsLock);
        Assert.Equal(Ch('A'), Press(m, KeyA));
    }

    [SkippableFact]
    public void Dead_key_is_ignored_not_a_reset()
    {
        using var m = Open("us:intl");
        Assert.Equal(KeyAction.Ignore, Press(m, KeyApostrophe).Action);
    }

    [SkippableFact]
    public void Special_keys_follow_evdev_rules()
    {
        using var m = Open("de");
        Assert.Equal(new KeyResult(KeyAction.Backspace), Press(m, EvKey.Backspace));
        Assert.Equal(KeyAction.Reset, Press(m, EvKey.Enter).Action);
        Assert.Equal(new KeyResult(KeyAction.Char, ' '), Press(m, EvKey.Space));
    }

    [SkippableFact]
    public void Set_group_switches_layout_and_keeps_working()
    {
        using var m = Open("us,de");
        Assert.Equal(Ch('y'), Press(m, KeyY));
        m.SetGroup(1);
        Assert.Equal(Ch('z'), Press(m, KeyY));
        m.SetGroup(0);
        Assert.Equal(Ch('y'), Press(m, KeyY));
    }

    [SkippableFact]
    public void Code_for_v_follows_the_layout()
    {
        using var us = Open("us");
        Assert.Equal(47, us.CodeFor('v'));          // KEY_V on QWERTY
        using var dvorak = Open("us:dvorak");
        Assert.Equal(KeyDot, dvorak.CodeFor('v'));  // Dvorak types v on the . key
    }
}
