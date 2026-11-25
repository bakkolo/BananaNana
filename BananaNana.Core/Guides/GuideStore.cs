using System.Diagnostics;
using System.Text.Json;
namespace BananaNana;

// Guides kept one per file in a folder: <guid>.bnguide. Saves go to a .tmp file first and then replace the
// old one, so a crash never leaves half a guide behind.
public sealed class GuideStore
{
    public const string Extension = ".bnguide";
    public const string FolderName = "BananaNana Guides";

    public GuideStore(string folder) => Folder = folder;

    public string Folder { get; }

    // $(xdg-user-dir DOCUMENTS)/BananaNana Guides on Linux (~/Documents when that tool is missing), MyDocuments on Windows.
    public static string DefaultFolder()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FolderName);
        var documents = XdgDocumentsDirectory();
        if (string.IsNullOrWhiteSpace(documents))
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        return Path.Combine(documents, FolderName);
    }

    public string PathFor(Guid id) => Path.Combine(Folder, id.ToString("D") + Extension);

    // Every readable guide, newest first. Files whose name is not <guid>.bnguide, or that cannot be read, are skipped.
    public IReadOnlyList<GuideSummary> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<GuideSummary>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(name, "D", out var id) || id.ToString("D") != name) continue;
            try
            {
                var doc = Read(file, id);
                result.Add(new GuideSummary(id, doc.Title, doc.Modified));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // An unreadable file stays on disk and does not hide the other guides.
            }
        }
        return result.OrderByDescending(g => g.Modified).ToList();
    }

    public GuideDocument Create(string title)
    {
        var now = DateTimeOffset.Now;
        var doc = new GuideDocument { Id = Guid.NewGuid(), Title = title, Created = now, Modified = now };
        Save(doc);
        return doc;
    }

    public GuideDocument Load(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) throw new FileNotFoundException("The guide does not exist.", path);
        return Read(path, id);
    }

    // Sets Modified to now. The file is written as <guid>.bnguide.tmp (flushed to disk) and then moved over the old one.
    public void Save(GuideDocument doc)
    {
        Validate(doc);
        doc.Modified = DateTimeOffset.Now;
        Directory.CreateDirectory(Folder);
        var path = PathFor(doc.Id);
        var temp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, GuideJson.Default.GuideDocument);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }

    // Deletes the file. Returns false when there was nothing to delete.
    public bool Delete(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    static GuideDocument Read(string path, Guid id)
    {
        var doc = JsonSerializer.Deserialize(File.ReadAllBytes(path), GuideJson.Default.GuideDocument)
            ?? throw new InvalidDataException("The guide file is empty.");
        if (doc.Version != GuideDocument.CurrentVersion)
            throw new InvalidDataException($"Unsupported guide version {doc.Version}.");
        doc.Id = id;
        Validate(doc);
        return doc;
    }

    // Ids must be real, non-empty and unique GUIDs. The JSON reader already rejects text that is not a GUID.
    static void Validate(GuideDocument doc)
    {
        if (doc.Id == Guid.Empty) throw new InvalidDataException("The guide id is empty.");
        var seen = new HashSet<Guid>();
        foreach (var step in doc.Steps)
        {
            if (step.Id == Guid.Empty) throw new InvalidDataException("A step id is empty.");
            if (!seen.Add(step.Id)) throw new InvalidDataException("A step id is used twice.");
        }
    }

    static string? XdgDocumentsDirectory()
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-user-dir") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("DOCUMENTS");
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(2000) && process.ExitCode == 0 && output.Length > 0 && Directory.Exists(output) ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // xdg-user-dir is not installed
        }
    }
}
