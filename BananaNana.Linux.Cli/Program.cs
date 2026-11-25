using System.Text.Json;
using BananaNana;
using BananaNana.Linux;
using BananaNana.Linux.Evdev;

// bn-linux-cli [--layout <xkb layout>[:variant]] [snippets.json]
//   (no file)                      run the snippets in the Linux library and reload them when the file changes
//   <snippets.json>                run a test file of { "trigger", "html", "text" } snippets
//   import <windows-library.json>  convert a Windows BananaNana.library.json into the Linux library, then exit
//   list                           print the triggers in the Linux library, then exit
// Prints keyboards, hotplug changes, the layout in use and the timings. Ctrl+C quits and destroys the virtual keyboard.
const string Usage = "usage: bn-linux-cli [--layout <xkb layout>[:variant]] [snippets.json]\n"
    + "       bn-linux-cli import <windows-library.json>\n"
    + "       bn-linux-cli list";

LayoutConfig? layout = null;
var positional = new List<string>();
try
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--layout" && i + 1 < args.Length) layout = LayoutConfig.Parse(args[++i]);
        else if (args[i].StartsWith("--layout=", StringComparison.Ordinal)) layout = LayoutConfig.Parse(args[i]["--layout=".Length..]);
        else positional.Add(args[i]);
    }
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

if (positional.Count == 0) return await RunLibrary(layout);
if (positional is ["import", var windowsPath]) return RunImport(windowsPath);
if (positional is ["list"]) return RunList();
if (positional.Count == 1 && positional[0] is not ("import" or "list")) return await RunTestFile(positional[0], layout);
Console.Error.WriteLine(Usage);
return 2;

async Task<int> RunLibrary(LayoutConfig? layoutOverride)
{
    LinuxLibrary library;
    try { library = LinuxLibrary.Open(LinuxLibrary.DefaultPath); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Cannot read the Linux library {LinuxLibrary.DefaultPath}: {e.Message}");
        return 1;
    }
    if (library.IsEmpty)
        Log($"No snippets in {library.FilePath}. Import your Windows library with: bn-linux-cli import <BananaNana.library.json>");

    using var engine = new LinuxExpansionEngine(Log, layoutOverride);
    try
    {
        engine.SetSnippets(LibraryWatcher.ToSnippets(library));
        Log($"Triggers: {string.Join(", ", library.Snippets.Select(s => s.Trigger))}");
        engine.Start();
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"Cannot start: {e.Message}");
        return 1;
    }
    using var watcher = new LibraryWatcher(library, engine, Log);
    Log($"Library: {library.FilePath}");
    return await WaitForCtrlC();
}

async Task<int> RunTestFile(string snippetPath, LayoutConfig? layoutOverride)
{
    var items = JsonSerializer.Deserialize<List<SnippetJson>>(
        File.ReadAllText(snippetPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    var snippets = new List<LinuxSnippet>();
    foreach (var item in items)
    {
        if (string.IsNullOrWhiteSpace(item.Trigger) || item.Trigger.Any(char.IsWhiteSpace))
        {
            Console.Error.WriteLine($"Invalid trigger '{item.Trigger}': triggers must be non-empty and contain no spaces.");
            return 2;
        }
        string text = item.Text ?? item.Html ?? "";
        snippets.Add(new LinuxSnippet(item.Trigger, item.Html ?? text, text));
    }

    using var engine = new LinuxExpansionEngine(Log, layoutOverride);
    try
    {
        engine.SetSnippets(snippets);
        Log($"Triggers: {string.Join(", ", snippets.Select(s => s.Trigger))}");
        engine.Start();
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"Cannot start: {e.Message}");
        return 1;
    }
    return await WaitForCtrlC();
}

int RunImport(string windowsPath)
{
    try
    {
        var library = LinuxLibrary.Open(LinuxLibrary.DefaultPath);
        var report = library.ImportFromWindows(windowsPath);
        Console.WriteLine($"Linux library: {library.FilePath}");
        Console.WriteLine($"Imported: {report.Imported.Count}");
        foreach (var trigger in report.Imported) Console.WriteLine($"  {trigger}");
        Console.WriteLine($"Skipped: {report.Skipped.Count}");
        foreach (var issue in report.Skipped) Console.WriteLine($"  {issue.Trigger}: {issue.Reason}");
        Console.WriteLine($"Failed: {report.Failed.Count}");
        foreach (var issue in report.Failed) Console.WriteLine($"  {issue.Trigger}: {issue.Reason}");
        return report.Failed.Count > 0 ? 1 : 0;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Import failed: {e.Message}");
        return 1;
    }
}

int RunList()
{
    try
    {
        var library = LinuxLibrary.Open(LinuxLibrary.DefaultPath);
        if (library.IsEmpty)
        {
            Console.WriteLine($"No snippets in {library.FilePath}. Import with: bn-linux-cli import <BananaNana.library.json>");
            return 0;
        }
        foreach (var snippet in library.Snippets)
        {
            var preview = snippet.Text.Replace('\r', ' ').Replace('\n', ' ');
            preview = preview.Length > 60 ? preview[..60] : preview;
            Console.WriteLine($"{snippet.Trigger,-16} {preview}");
        }
        return 0;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Cannot read the Linux library: {e.Message}");
        return 1;
    }
}

async Task<int> WaitForCtrlC()
{
    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    Log("Listening. Press Ctrl+C to quit.");
    await stop.Task;
    Log("Stopping, destroying the virtual keyboard.");
    return 0;
}

static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");

record SnippetJson(string? Trigger, string? Html, string? Text);

// Reloads the Linux library when its file changes (debounced, so a save that writes twice reloads once).
// A file that does not parse is reported and the previous snippets stay active.
sealed class LibraryWatcher : IDisposable
{
    const int DebounceMs = 300;
    readonly LinuxLibrary library;
    readonly LinuxExpansionEngine engine;
    readonly Action<string> log;
    readonly Timer timer;
    readonly FileSystemWatcher watcher;

    public static List<LinuxSnippet> ToSnippets(LinuxLibrary library) =>
        library.Snippets.Select(s => new LinuxSnippet(s.Trigger, s.Html, s.Text)).ToList();

    public LibraryWatcher(LinuxLibrary library, LinuxExpansionEngine engine, Action<string> log)
    {
        this.library = library;
        this.engine = engine;
        this.log = log;
        var directory = Path.GetDirectoryName(Path.GetFullPath(library.FilePath))!;
        Directory.CreateDirectory(directory);
        timer = new Timer(_ => Reload());
        watcher = new FileSystemWatcher(directory, LinuxLibrary.FileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;
    }

    void Schedule() => timer.Change(DebounceMs, Timeout.Infinite);

    void Reload()
    {
        try
        {
            library.Load();
            engine.SetSnippets(ToSnippets(library));
            log($"Library reloaded: {library.Snippets.Count} snippet(s).");
        }
        catch (Exception e)
        {
            log($"Library not reloaded, keeping the previous snippets: {e.Message}");
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        timer.Dispose();
    }
}
