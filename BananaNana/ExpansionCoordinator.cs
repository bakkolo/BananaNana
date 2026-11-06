using System.Runtime.InteropServices;
using System.Windows;
using BananaNana.Input;
using BananaNana.Paste;
namespace BananaNana;

// Glues the key listener, matcher and paste engine to the snippet library.
// Safety checks (password field, admin app, excluded app) run in ExpandAsync via ForegroundGuard.
public sealed class ExpansionCoordinator : IDisposable
{
    readonly KeyboardInputService input = new();
    readonly TriggerMatcher matcher = new([]);
    readonly ClipboardPasteService paster = new();
    Dictionary<string, Snippet> snippets = new(StringComparer.OrdinalIgnoreCase);
    bool paused;

    public AppSettings Settings { get; set; } = new();

    public event Action<string>? StatusChanged;

    public bool Paused
    {
        get => paused;
        set { paused = value; matcher.Reset(); }
    }

    public void Start()
    {
        input.CharTyped += OnChar;
        input.Backspace += matcher.OnBackspace;
        input.Reset += matcher.Reset;
        input.Start();
    }

    // LibraryStore already rejects duplicate triggers (case-insensitive), so ToDictionary cannot collide.
    public void SetSnippets(IEnumerable<Snippet> list)
    {
        snippets = list.ToDictionary(s => s.Trigger, StringComparer.OrdinalIgnoreCase);
        matcher.SetTriggers(snippets.Keys);
    }

    void OnChar(char c)
    {
        if (paused) return;
        if (matcher.OnChar(c) is { } trigger && snippets.TryGetValue(trigger, out var snippet)) _ = ExpandAsync(trigger, snippet);
    }

    async Task ExpandAsync(string trigger, Snippet snippet)
    {
        // Never expand inside Banana Nana itself (e.g. while typing a trigger in the editor).
        if (Application.Current.Windows.OfType<Window>().Any(w => w.IsActive)) return;
        switch (ForegroundGuard.Check(Settings))
        {
            case Skip.Admin: StatusChanged?.Invoke(Loc.F("Status.SkippedAdmin", trigger)); return;
            case Skip.Password or Skip.Excluded: return; // silent on purpose
        }
        try
        {
            var content = string.IsNullOrEmpty(snippet.Content)
                ? new RenderedContent("", "", "")
                : RichContentRenderer.Render(RichContentRenderer.LoadXamlPackage(snippet.Content));
            await paster.PasteAsync(content, backspaces: trigger.Length + 1);
            StatusChanged?.Invoke(Loc.F("Status.Expanded", trigger, DateTime.Now.ToString("HH:mm:ss")));
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or FormatException)
        {
            StatusChanged?.Invoke(Loc.F("Status.Failed", trigger, ex.Message));
        }
    }

    public void Dispose() => input.Dispose();
}
