using System.Diagnostics;
using System.Text;
using BananaNana.Input;
using BananaNana.Linux.Clipboard;
using BananaNana.Linux.Evdev;

namespace BananaNana.Linux;

public sealed record LinuxSnippet(string Trigger, string Html, string PlainText);

// Input -> KeyMap -> TriggerMatcher -> expansion. The sequence and timings match the Python prototype:
// backspaces, save clipboard, copy snippet, wait 150 ms, Ctrl+V, wait 500 ms, restore clipboard.
public sealed class LinuxExpansionEngine : IDisposable
{
    const int PasteWaitMs = 150, RestoreWaitMs = 500;

    readonly Action<string> log;
    readonly object gate = new();
    readonly KeyMap keyMap = new();
    readonly TriggerMatcher matcher = new([]);
    readonly WaylandClipboard clipboard = new();
    readonly InputMonitor input;
    Dictionary<string, LinuxSnippet> snippets = new(StringComparer.OrdinalIgnoreCase);
    UInputKeyboard? keyboard;
    bool paused, expanding;

    public LinuxExpansionEngine(Action<string> log)
    {
        this.log = log;
        input = new InputMonitor(log, OnKeyDown, OnKeyUp);
    }

    public bool Paused
    {
        get { lock (gate) return paused; }
        set { lock (gate) { paused = value; matcher.Reset(); } }
    }

    // Creates the virtual keyboard (throws if /dev/uinput is not usable), then starts reading input.
    public void Start()
    {
        keyboard = new UInputKeyboard();
        input.Start();
    }

    public void SetSnippets(IEnumerable<LinuxSnippet> list)
    {
        lock (gate)
        {
            snippets = list.ToDictionary(s => s.Trigger, StringComparer.OrdinalIgnoreCase);
            matcher.SetTriggers(snippets.Keys);
        }
    }

    void OnKeyDown(int code, bool isRepeat)
    {
        LinuxSnippet? matched = null;
        string trigger = "";
        lock (gate)
        {
            // Modifier state is tracked even during an expansion, so it never gets stuck.
            var result = keyMap.Down(code, isRepeat);
            if (paused || expanding) return;
            switch (result.Action)
            {
                case KeyAction.Backspace: matcher.OnBackspace(); break;
                case KeyAction.Reset: matcher.Reset(); break;
                case KeyAction.Char:
                    if (matcher.OnChar(result.Char) is { } t && snippets.TryGetValue(t, out var s))
                    {
                        expanding = true;
                        trigger = t;
                        matched = s;
                    }
                    break;
            }
        }
        if (matched is null) return;
        log($"trigger '{trigger}' matched, expanding");
        Task.Run(() => Expand(matched));
    }

    void OnKeyUp(int code)
    {
        lock (gate) keyMap.Up(code);
    }

    void Expand(LinuxSnippet s)
    {
        var sw = Stopwatch.StartNew();
        void Mark(string what) => log($"  {sw.ElapsedMilliseconds,4} ms  {what}");
        var kb = keyboard!;
        try
        {
            kb.Backspace(s.Trigger.Length + 1); // the trigger plus the separator the user typed
            Mark($"{s.Trigger.Length + 1} backspaces sent");

            var saved = clipboard.Save();
            Mark($"clipboard saved ({saved?.MimeType ?? "no text, nothing to restore"})");

            var (mime, payload) = ClipboardFormat.Choose(s.Html, s.PlainText);
            clipboard.Set(Encoding.UTF8.GetBytes(payload), mime);
            Mark($"snippet copied as {mime}");

            Thread.Sleep(PasteWaitMs);
            kb.CtrlV();
            Mark("Ctrl+V sent");

            Thread.Sleep(RestoreWaitMs);
            clipboard.Restore(saved);
            Mark("clipboard restored");
        }
        catch (Exception e)
        {
            // A background task must not take the process down; report and carry on.
            log($"expansion of '{s.Trigger}' failed: {e.Message}");
        }
        finally
        {
            lock (gate)
            {
                expanding = false;
                matcher.Reset();
            }
        }
    }

    public void Dispose()
    {
        input.Dispose();
        keyboard?.Dispose();
    }
}
