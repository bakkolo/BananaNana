using System.Xml.Linq;
namespace BananaNana.Desktop;

// UI texts for the Linux app. It reads the Windows app's Strings/Strings.xx.xaml files (embedded as resources)
// with a plain XML reader: each x:Key is read by local name, so both apps share one set of translations.
// English is loaded first; the chosen language overrides it, so a missing translation falls back to English.
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
        [("en", "English"), ("pl", "Polski"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français"), ("th", "ไทย"), ("zh", "简体中文")];

    static Dictionary<string, string>? english, current;

    public static string Current { get; private set; } = "en";
    public static event Action? Changed;

    public static void Apply(string code)
    {
        if (!Languages.Any(l => l.Code == code)) code = "en";
        english ??= Load("en");
        current = code == "en" ? null : Load(code);
        Current = code;
        Changed?.Invoke();
    }

    // The key itself when it is unknown, so a typo shows up on screen instead of crashing.
    public static string T(string key)
    {
        english ??= Load("en");
        if (current is not null && current.TryGetValue(key, out var text)) return text;
        return english.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    public static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Strings.{code}.xaml")
            ?? throw new FileNotFoundException($"No strings resource for '{code}'.");
        return Parse(XDocument.Load(stream));
    }

    // Namespace-agnostic: every element named String with a Key attribute (the XAML namespace prefix does not matter).
    public static Dictionary<string, string> Parse(XDocument document)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root?.Elements().Where(e => e.Name.LocalName == "String") ?? [])
        {
            var key = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value
                ?? throw new InvalidDataException("A string has no x:Key.");
            if (!strings.TryAdd(key, element.Value))
                throw new InvalidDataException($"The key '{key}' is defined twice.");
        }
        return strings;
    }
}
