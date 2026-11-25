using BananaNana.Linux;
namespace BananaNana.Desktop;

// Owns the Linux library and the expansion engine, both in this process. Edits go through Upsert and Remove:
// they save atomically and push the new snippets to the engine. The library file is watched too, so an outside
// edit reloads live (as bn-linux-cli does). A file that does not parse keeps the last good snippets and turns editing off.
public sealed class ExpansionService : IDisposable
{
    const int DebounceMs = 300;
    readonly object gate = new();
    readonly LinuxExpansionEngine engine;
    readonly FileSystemWatcher watcher;
    readonly Timer timer;
    LinuxLibrary library;
    volatile string? keyboardProblem; // the input monitor's last permission or open error
    bool disposed;

    public ExpansionService(string libraryPath, bool startPaused)
    {
        library = new LinuxLibrary(libraryPath);
        try { library.Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { LibraryError = e.Message; }

        engine = new LinuxExpansionEngine(Log);
        engine.SetSnippets(ToSnippets());
        try
        {
            engine.Start();
            engine.Paused = startPaused;
        }
        catch (Exception e)
        {
            // /dev/uinput or /dev/input cannot be used (for example, the input group is not active yet in this session).
            EngineError = e.Message;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(libraryPath))!;
        Directory.CreateDirectory(directory);
        timer = new Timer(_ => Reload());
        watcher = new FileSystemWatcher(directory, LinuxLibrary.FileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => timer.Change(DebounceMs, Timeout.Infinite);
        watcher.Created += (_, _) => timer.Change(DebounceMs, Timeout.Infinite);
        watcher.Renamed += (_, _) => timer.Change(DebounceMs, Timeout.Infinite);
        watcher.EnableRaisingEvents = true;
    }

    // Raised on any thread after a change (snippets, library error, pause, keyboards). Subscribers re-post to the UI.
    public event Action? Changed;

    public string LibraryPath { get { lock (gate) return library.FilePath; } }
    public string LibraryName { get { lock (gate) return library.Data.Name; } }
    public string? LibraryError { get; private set; }
    public string? EngineError { get; private set; }
    public bool CanEdit => LibraryError is null;
    public int KeyboardCount => engine.KeyboardCount;

    // What stops expansion, or null when it works.
    public string? Problem => EngineError ?? (KeyboardCount == 0 ? keyboardProblem ?? "no keyboard can be read under /dev/input" : null);

    public bool Paused
    {
        get => engine.Paused;
        set
        {
            engine.Paused = value;
            Changed?.Invoke();
        }
    }

    public List<HtmlSnippet> Snapshot()
    {
        lock (gate) return library.Snippets.ToList();
    }

    public HtmlSnippet? Find(string trigger)
    {
        lock (gate) return library.Snippets.FirstOrDefault(s => s.Trigger == trigger);
    }

    // Adds a snippet, or replaces the one whose trigger is original (case-sensitive match on the stored text).
    public void Upsert(string? original, HtmlSnippet snippet) => Change(list =>
    {
        int index = original is null ? -1 : list.FindIndex(s => s.Trigger == original);
        if (index < 0) list.Add(snippet);
        else list[index] = snippet;
    });

    public void Remove(string trigger) => Change(list => list.RemoveAll(s => s.Trigger == trigger));

    // Imports a Windows library (the same rules as the CLI). The engine gets the new snippets at once.
    public ImportReport Import(string windowsLibraryPath)
    {
        ImportReport report;
        lock (gate)
        {
            if (!CanEdit) throw new InvalidOperationException(LibraryError);
            var before = library.Data.Snippets.ToList();
            try
            {
                report = library.ImportFromWindows(windowsLibraryPath);
            }
            catch
            {
                library.Data.Snippets = before; // a failed save leaves the file unchanged, so memory must match it
                throw;
            }
            engine.SetSnippets(ToSnippets());
        }
        Changed?.Invoke();
        return report;
    }

    public void Export(string path)
    {
        lock (gate)
        {
            if (!CanEdit) throw new InvalidOperationException(LibraryError);
            library.Export(path);
        }
    }

    void Change(Action<List<HtmlSnippet>> edit)
    {
        lock (gate)
        {
            if (!CanEdit) throw new InvalidOperationException(LibraryError);
            var before = library.Data.Snippets.ToList();
            edit(library.Data.Snippets);
            try
            {
                library.Save(); // validates duplicates and writes a temp file, then moves it into place
            }
            catch
            {
                library.Data.Snippets = before; // the file is unchanged, so the memory copy must match it again
                throw;
            }
            engine.SetSnippets(ToSnippets());
        }
        Changed?.Invoke();
    }

    IEnumerable<LinuxSnippet> ToSnippets() => library.Snippets.Select(s => new LinuxSnippet(s.Trigger, s.Html, s.Text));

    // Timer thread: reload the file after a change. Keeps the previous snippets when the new file is bad.
    void Reload()
    {
        lock (gate)
        {
            if (disposed) return;
            try
            {
                library.Load();
                LibraryError = null;
                engine.SetSnippets(ToSnippets());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                LibraryError = e.Message;
            }
        }
        Changed?.Invoke();
    }

    void Log(string message)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        if (message.Contains("permission denied", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Cannot ", StringComparison.Ordinal))
            keyboardProblem = message;
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        watcher.Dispose();
        timer.Dispose();
        engine.Dispose();
    }
}
