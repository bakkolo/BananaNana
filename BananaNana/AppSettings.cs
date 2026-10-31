using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace BananaNana;

// Per-user preferences in %APPDATA%\BananaNana (the snippet library itself lives elsewhere, see LibraryStore).
public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public bool WelcomeShown { get; set; }

    // A separately named instance (BANANANANA_INSTANCE, used for testing) gets its own settings file.
    static string FilePath
    {
        get
        {
            var instance = Environment.GetEnvironmentVariable("BANANANANA_INSTANCE");
            var name = string.IsNullOrEmpty(instance) ? "settings.json" : $"settings.{instance}.json";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BananaNana", name);
        }
    }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new() : new();
        }
        // A damaged settings file must never stop the app; fall back to defaults.
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

[JsonSerializable(typeof(AppSettings))][JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJson : JsonSerializerContext { }
