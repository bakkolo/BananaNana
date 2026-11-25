using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BananaNana.Import;
namespace BananaNana;

public sealed class HtmlSnippet { public string Trigger { get; set; } = ""; public string Html { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class HtmlSnippetLibrary { public string Name { get; set; } = "My library"; public List<HtmlSnippet> Snippets { get; set; } = new(); }

// Build-time generated JSON code, as in LibraryStore.
[JsonSerializable(typeof(HtmlSnippetLibrary))][JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class HtmlLibraryJson : JsonSerializerContext { }

// How ImportFromWindows treats snippets whose trigger is already in the Linux library.
// Auto: an empty library takes every snippet; otherwise existing triggers are skipped and reported.
public enum ImportMode { Auto }

public sealed record ImportIssue(string Trigger, string Reason);
public sealed record ImportReport(IReadOnlyList<string> Imported, IReadOnlyList<ImportIssue> Skipped, IReadOnlyList<ImportIssue> Failed);

// The Linux library: { "Name", "Snippets": [ { "Trigger", "Html", "Text" } ] }, in BananaNana/library.json form.
public sealed class LinuxLibrary
{
    public const string FileName = "library.json";

    public LinuxLibrary(string filePath) { FilePath = filePath; }

    public string FilePath { get; }
    public HtmlSnippetLibrary Data { get; private set; } = new();
    public IReadOnlyList<HtmlSnippet> Snippets => Data.Snippets;
    public bool IsEmpty => Data.Snippets.Count == 0;

    // $XDG_DATA_HOME/BananaNana/library.json, or ~/.local/share/BananaNana/library.json.
    public static string DefaultPath
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var dataHome = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(dataHome, "BananaNana", FileName);
        }
    }

    public static LinuxLibrary Open(string filePath)
    {
        var library = new LinuxLibrary(filePath);
        library.Load();
        return library;
    }

    // A missing file is an empty library. An invalid file throws InvalidDataException and is not changed.
    public void Load()
    {
        Data = File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new HtmlSnippetLibrary();
    }

    static HtmlSnippetLibrary Parse(string json)
    {
        HtmlSnippetLibrary? data;
        try { data = JsonSerializer.Deserialize(json, HtmlLibraryJson.Default.HtmlSnippetLibrary); }
        catch (JsonException e) { throw new InvalidDataException($"Not a Linux library file: {e.Message}", e); }
        if (data is null) throw new InvalidDataException("The library file is empty.");
        data.Snippets ??= new List<HtmlSnippet>();
        Validate(data.Snippets);
        return data;
    }

    // Same rules as the Windows library: no empty triggers, no duplicates (case-insensitive).
    static void Validate(IEnumerable<HtmlSnippet> snippets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in snippets)
        {
            if (string.IsNullOrWhiteSpace(snippet.Trigger)) throw new InvalidDataException("A snippet has an empty trigger.");
            if (!seen.Add(snippet.Trigger)) throw new InvalidDataException($"Two snippets use the trigger '{snippet.Trigger}'.");
        }
    }

    // Writes a temporary file next to the library and moves it into place, so a crash never leaves a half-written file.
    public void Save()
    {
        Validate(Data.Snippets);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, HtmlLibraryJson.Default.HtmlSnippetLibrary));
        File.Move(temp, FilePath, true);
    }

    // Reads a Windows BananaNana.library.json (read only) and converts each snippet. One bad snippet is reported
    // as failed and does not stop the others. Saves this library when anything was imported.
    public ImportReport ImportFromWindows(string windowsLibraryPath, ImportMode mode = ImportMode.Auto)
    {
        var source = JsonSerializer.Deserialize(File.ReadAllText(windowsLibraryPath), LibraryJson.Default.SnippetLibrary)
            ?? throw new InvalidDataException("The Windows library file is empty.");
        var existing = new HashSet<string>(Data.Snippets.Select(s => s.Trigger), StringComparer.OrdinalIgnoreCase);
        var importAll = mode == ImportMode.Auto && Data.Snippets.Count == 0;

        var imported = new List<string>();
        var skipped = new List<ImportIssue>();
        var failed = new List<ImportIssue>();
        foreach (var item in source.Snippets ?? new List<Snippet>())
        {
            var trigger = item?.Trigger ?? "";
            try
            {
                if (string.IsNullOrWhiteSpace(trigger))
                {
                    failed.Add(new ImportIssue(trigger, "empty trigger"));
                    continue;
                }
                if (Data.Snippets.Any(s => string.Equals(s.Trigger, trigger, StringComparison.OrdinalIgnoreCase)))
                {
                    var reason = !importAll && existing.Contains(trigger)
                        ? "already in the Linux library"
                        : "duplicate trigger in the Windows file";
                    skipped.Add(new ImportIssue(trigger, reason));
                    continue;
                }
                var content = XamlPackageConverter.Read(item!.Content ?? "", trigger);
                Data.Snippets.Add(new HtmlSnippet { Trigger = trigger, Html = content.Html, Text = content.PlainText });
                imported.Add(trigger);
            }
            catch (Exception e)
            {
                failed.Add(new ImportIssue(trigger, e.Message));
            }
        }

        if (imported.Count > 0) Save();
        return new ImportReport(imported, skipped, failed);
    }
}
