using System.Diagnostics;
using System.Text;
using BananaNana.Input;
using BananaNana.Linux.Clipboard;
using BananaNana.Linux.Evdev;

namespace BananaNana.Linux;

public sealed record LinuxSnippet(string Trigger, string Html, string PlainText);

// Input -> key map -> TriggerMatcher -> expansion. The sequence and timings match the Python prototype:
// backspaces, save clipboard, copy snippet, wait 150 ms, Ctrl+V, wait 500 ms, restore clipboard.
public sealed class LinuxExpansionEngine : IDisposable
{
    const int PasteWaitMs = 150, RestoreWaitMs = 500, ConfigSettleMs = 300;
    const int FallbackKeyV = 47; // evdev KEY_V, used when the layout has no 'v'

    readonly Action<string> log;
    readonly LayoutConfig? layoutOverride;
    readonly object gate = new();
    readonly TriggerMatcher matcher = new([]);
    readonly WaylandClipboard clipboard = new();
    readonly InputMonitor input;
    string home = "/";
    Dictionary<string, LinuxSnippet> snippets = new(StringComparer.OrdinalIgnoreCase);
    UInputKeyboard? keyboard;
    IKeyMap keyMap = new KeyMap();
    LayoutConfig layout = LayoutConfig.Us;
    uint group;
    bool paused, expanding, disposed;
    KdeLayoutMonitor? kde;
    FileSystemWatcher? kxkbWatcher;

    // layoutOverride (from --layout) fixes the layout and disables detection and KDE following.
    public LinuxExpansionEngine(Action<string> log, LayoutConfig? layoutOverride = null)
    {
        this.log = log;
        this.layoutOverride = layoutOverride;
        input = new InputMonitor(log, OnKeyDown, OnKeyUp);
    }

    // Keyboards the input monitor is reading (0 before Start, or when /dev/input cannot be read).
    public int KeyboardCount => input.KeyboardCount;

    public bool Paused
    {
        get { lock (gate) return paused; }
        set { lock (gate) { paused = value; matcher.Reset(); } }
    }

    // Creates the virtual keyboard (throws if /dev/uinput is not usable), sets up the layout, then reads input.
    public void Start()
    {
        keyboard = new UInputKeyboard();
        home = Environment.GetEnvironmentVariable("HOME") ?? "/";
        if (layoutOverride is { } forced)
        {
            ApplyLayout(forced, "--layout", followKde: false);
        }
        else
        {
            var (config, source) = LayoutConfig.Detect(home, Environment.GetEnvironmentVariable);
            ApplyLayout(config, source, followKde: true);
            WatchKxkbrc();
        }
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

    // Builds the key map for config and swaps it in. followKde also reads KDE's active layout and follows it.
    void ApplyLayout(LayoutConfig config, string source, bool followKde)
    {
        IKeyMap next = BuildKeyMap(config);
        uint start = 0;
        if (followKde && config.Layouts.Length > 1)
        {
            start = KdeLayoutMonitor.QueryGroup() ?? 0;
            if (start >= config.Layouts.Length) start = 0;
            if (kde is null)
            {
                kde = new KdeLayoutMonitor(OnKdeGroup);
                kde.Start();
            }
        }
        next.SetGroup(start);

        lock (gate)
        {
            if (disposed)
            {
                next.Dispose();
                return;
            }
            var old = keyMap;
            keyMap = next;
            layout = config;
            group = start;
            matcher.Reset();
            old.Dispose();
        }
        log($"layout: {config.Describe(start)} (from {source})");
    }

    IKeyMap BuildKeyMap(LayoutConfig config)
    {
        try
        {
            return new XkbKeymap(config);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            log($"warning: {e.Message} Using the built-in US key map.");
            return new KeyMap();
        }
    }

    void OnKdeGroup(uint newGroup)
    {
        string name;
        lock (gate)
        {
            if (newGroup == group || newGroup >= layout.Layouts.Length) return;
            group = newGroup;
            keyMap.SetGroup(newGroup);
            matcher.Reset();
            name = layout.Describe(newGroup);
        }
        log($"layout changed: {name}");
    }

    void WatchKxkbrc()
    {
        string dir = Path.Combine(home, ".config");
        if (!Directory.Exists(dir)) return;
        kxkbWatcher = new FileSystemWatcher(dir, "kxkbrc")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        kxkbWatcher.Changed += (_, _) => Task.Run(ReloadLayout);
        kxkbWatcher.Created += (_, _) => Task.Run(ReloadLayout);
        kxkbWatcher.EnableRaisingEvents = true;
    }

    void ReloadLayout()
    {
        Thread.Sleep(ConfigSettleMs); // editors write in bursts
        var (config, source) = LayoutConfig.Detect(home, Environment.GetEnvironmentVariable);
        lock (gate)
        {
            if (disposed || config.Key == layout.Key) return;
        }
        ApplyLayout(config, source, followKde: true);
    }

    void OnKeyDown(int code, bool isRepeat)
    {
        LinuxSnippet? matched = null;
        string trigger = "";
        lock (gate)
        {
            // The key map sees every key, even during an expansion, so modifier and layout state stay correct.
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
            int vKey;
            lock (gate) vKey = keyMap.CodeFor('v');
            if (vKey < 0) vKey = FallbackKeyV;

            kb.Backspace(s.Trigger.Length + 1); // the trigger plus the separator the user typed
            Mark($"{s.Trigger.Length + 1} backspaces sent");

            var saved = clipboard.Save();
            Mark($"clipboard saved ({saved?.MimeType ?? "no text, nothing to restore"})");

            var (mime, payload) = ClipboardFormat.Choose(s.Html, s.PlainText);
            clipboard.Set(Encoding.UTF8.GetBytes(payload), mime);
            Mark($"snippet copied as {mime}");

            Thread.Sleep(PasteWaitMs);
            kb.CtrlV(vKey);
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
        lock (gate) disposed = true;
        kxkbWatcher?.Dispose();
        kde?.Dispose();
        input.Dispose();
        lock (gate) keyMap.Dispose();
        keyboard?.Dispose();
    }
}
