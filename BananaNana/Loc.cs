using System.Windows;
namespace BananaNana;

// UI translations. Each language is a ResourceDictionary of strings (Strings/Strings.xx.xaml). English is
// always loaded first and the chosen language on top of it, so a missing translation falls back to English.
// XAML uses {DynamicResource Key}, so switching language updates open windows immediately; code uses T/F
// and re-renders its own texts on Changed.
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
        [("en", "English"), ("pl", "Polski"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français"), ("th", "ไทย"), ("zh", "简体中文")];

    // Culture tags for FrameworkElement.Language. Without zh-CN, WPF's font fallback draws Chinese with a
    // Japanese font, so several simplified characters get Japanese glyph shapes.
    static readonly Dictionary<string, string> Cultures = new()
        { ["en"] = "en-US", ["pl"] = "pl-PL", ["de"] = "de-DE", ["es"] = "es-ES", ["fr"] = "fr-FR", ["th"] = "th-TH", ["zh"] = "zh-CN" };

    static ResourceDictionary? english, current;
    static bool windowHookRegistered;

    public static string Current { get; private set; } = "en";
    public static event Action? Changed;

    public static void Apply(string code)
    {
        if (!Languages.Any(l => l.Code == code)) code = "en";
        var merged = Application.Current.Resources.MergedDictionaries;
        if (english == null) merged.Add(english = Load("en"));
        if (current != null) merged.Remove(current);
        current = code == "en" ? null : Load(code);
        if (current != null) merged.Add(current);
        Current = code;
        // Tag open windows now and every window created later, so the font matches the language.
        if (!windowHookRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Tag((Window)s)));
            windowHookRegistered = true;
        }
        foreach (Window window in Application.Current.Windows) Tag(window);
        Changed?.Invoke();
    }

    static void Tag(Window window) => window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Cultures[Current]);

    static ResourceDictionary Load(string code) => new() { Source = new Uri($"pack://application:,,,/Strings/Strings.{code}.xaml") };

    // Outside the app (unit tests) there are no resources; the key itself is returned.
    public static string T(string key) => Application.Current?.TryFindResource(key) as string ?? key;
    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
