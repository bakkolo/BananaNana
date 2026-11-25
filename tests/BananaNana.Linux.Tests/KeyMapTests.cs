using BananaNana.Linux.Evdev;
using Xunit;

namespace BananaNana.Linux.Tests;

public class KeyMapTests
{
    static KeyResult Char(char c) => new(KeyAction.Char, c);

    [Fact]
    public void Letters_are_lowercase_by_default_and_uppercase_with_shift()
    {
        var m = new KeyMap();
        Assert.Equal(Char('a'), m.Down(EvKey.KeyA));
        m.Up(EvKey.KeyA);
        m.Down(EvKey.LShift);
        Assert.Equal(Char('A'), m.Down(EvKey.KeyA));
    }

    [Fact]
    public void Shift_is_released_on_key_up()
    {
        var m = new KeyMap();
        m.Down(EvKey.RShift);
        m.Up(EvKey.RShift);
        Assert.Equal(Char('a'), m.Down(EvKey.KeyA));
    }

    [Fact]
    public void CapsLock_toggles_letters_but_not_digits()
    {
        var m = new KeyMap();
        m.Down(EvKey.CapsLock);
        Assert.Equal(Char('A'), m.Down(EvKey.KeyA));
        Assert.Equal(Char('1'), m.Down(2));
        m.Down(EvKey.LShift);
        Assert.Equal(Char('a'), m.Down(EvKey.KeyA)); // Shift + CapsLock cancel out for letters
        Assert.Equal(Char('!'), m.Down(2));           // digits follow Shift only
    }

    [Fact]
    public void CapsLock_auto_repeat_does_not_toggle()
    {
        var m = new KeyMap();
        m.Down(EvKey.CapsLock);
        m.Down(EvKey.CapsLock, isRepeat: true);
        Assert.True(m.CapsLock);
    }

    [Theory]
    [InlineData(2, '1', '!')]
    [InlineData(11, '0', ')')]
    [InlineData(12, '-', '_')]
    [InlineData(13, '=', '+')]
    [InlineData(26, '[', '{')]
    [InlineData(39, ';', ':')]
    [InlineData(40, '\'', '"')]
    [InlineData(41, '`', '~')]
    [InlineData(43, '\\', '|')]
    [InlineData(51, ',', '<')]
    [InlineData(53, '/', '?')]
    public void Symbols_map_to_us_layout(int code, char plain, char shifted)
    {
        Assert.Equal(Char(plain), new KeyMap().Down(code));
        var m = new KeyMap();
        m.Down(EvKey.LShift);
        Assert.Equal(Char(shifted), m.Down(code));
    }

    [Fact]
    public void Space_is_a_space_character()
    {
        Assert.Equal(Char(' '), new KeyMap().Down(EvKey.Space));
    }

    [Fact]
    public void Backspace_is_reported_as_backspace()
    {
        Assert.Equal(new KeyResult(KeyAction.Backspace), new KeyMap().Down(EvKey.Backspace));
    }

    [Theory]
    [InlineData(EvKey.Tab)]
    [InlineData(EvKey.Enter)]
    [InlineData(EvKey.Esc)]
    [InlineData(EvKey.Left)]
    [InlineData(EvKey.Up)]
    [InlineData(EvKey.Right)]
    [InlineData(EvKey.Down)]
    [InlineData(EvKey.Home)]
    [InlineData(EvKey.End)]
    [InlineData(EvKey.PageUp)]
    [InlineData(EvKey.PageDown)]
    [InlineData(EvKey.Insert)]
    [InlineData(EvKey.Delete)]
    [InlineData(0x110)] // BTN_LEFT
    [InlineData(0x111)] // BTN_RIGHT
    public void Context_keys_and_mouse_buttons_reset_the_word(int code)
    {
        Assert.Equal(KeyAction.Reset, new KeyMap().Down(code).Action);
    }

    [Fact]
    public void Ctrl_or_Alt_shortcut_resets_the_word()
    {
        var m = new KeyMap();
        m.Down(EvKey.LCtrl);
        Assert.Equal(KeyAction.Reset, m.Down(EvKey.KeyA).Action);
        m.Up(EvKey.LCtrl);

        m.Down(EvKey.LAlt);
        Assert.Equal(KeyAction.Reset, m.Down(EvKey.KeyA).Action);
    }

    [Fact]
    public void Meta_resets_the_word()
    {
        var m = new KeyMap();
        m.Down(EvKey.LMeta);
        Assert.Equal(KeyAction.Reset, m.Down(EvKey.KeyA).Action);
    }

    [Fact]
    public void Modifiers_and_unknown_keys_are_ignored()
    {
        var m = new KeyMap();
        Assert.Equal(KeyAction.Ignore, m.Down(EvKey.LShift).Action);
        Assert.Equal(KeyAction.Ignore, m.Down(59).Action); // F1
    }
}
