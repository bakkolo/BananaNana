namespace BananaNana.Linux.Evdev;

// Turns evdev key events into text and word-reset actions. Implemented by XkbKeymap (any layout) and by
// KeyMap (built-in US table, used when libxkbcommon cannot be loaded).
public interface IKeyMap : IDisposable
{
    KeyResult Down(int code, bool isRepeat = false);

    void Up(int code);

    // Locked layout group (index into the configured layouts). The built-in table has one layout and ignores it.
    void SetGroup(uint group);

    // Evdev code that types c in the current state, or -1 when none does.
    int CodeFor(char c);
}
