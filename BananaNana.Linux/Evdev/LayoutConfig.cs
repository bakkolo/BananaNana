using System.Text.RegularExpressions;

namespace BananaNana.Linux.Evdev;

// A keyboard layout as xkb takes it: one entry per group, in the order KDE lists them.
public sealed record LayoutConfig
{
    public string[] Layouts { get; }
    public string[] Variants { get; } // same length as Layouts; "" = default variant
    public string? Model { get; }
    public string? Options { get; }

    LayoutConfig(string[] layouts, string[] variants, string? model, string? options)
    {
        Layouts = layouts;
        Variants = variants;
        Model = model;
        Options = options;
    }

    public static LayoutConfig Us { get; } = Create(["us"], [], null, null);

    public string LayoutString => string.Join(',', Layouts);
    public string? VariantString => Variants.Any(v => v.Length > 0) ? string.Join(',', Variants) : null;

    // Identity for "did the layout change?" checks.
    public string Key => $"{LayoutString}|{VariantString}|{Model}|{Options}";

    // "gb", or "pl(dvorak)" for a variant, for logs.
    public string Describe(uint group)
    {
        int i = group < Layouts.Length ? (int)group : 0;
        return Variants[i].Length > 0 ? $"{Layouts[i]}({Variants[i]})" : Layouts[i];
    }

    public override string ToString() => string.Join(", ", Layouts.Select((_, i) => Describe((uint)i)));

    // Lists are kept positionally, so variants line up with layouts; missing variants become "".
    static LayoutConfig Create(IList<string> layouts, IList<string> variants, string? model, string? options)
    {
        var v = new string[layouts.Count];
        for (int i = 0; i < v.Length; i++) v[i] = i < variants.Count ? variants[i] : "";
        return new LayoutConfig(layouts.ToArray(), v, NullIfEmpty(model), NullIfEmpty(options));
    }

    // "gb", "pl:dvorak" (from --layout).
    public static LayoutConfig Parse(string spec)
    {
        var parts = spec.Split(':', 2);
        if (parts[0].Length == 0) throw new ArgumentException("Layout must look like 'gb' or 'pl:dvorak'.");
        var variants = parts.Length > 1 ? new[] { parts[1].Trim() } : Array.Empty<string>();
        return Create(new[] { parts[0].Trim() }, variants, null, null);
    }

    // ~/.config/kxkbrc: only used when [Layout] Use=true. Null otherwise.
    public static LayoutConfig? ParseKxkbrc(string text)
    {
        var values = IniSection(text, "Layout");
        if (!values.TryGetValue("Use", out var use) || !string.Equals(use, "true", StringComparison.OrdinalIgnoreCase))
            return null;
        var layouts = SplitList(values.GetValueOrDefault("LayoutList"));
        if (layouts.Count == 0 || layouts.Any(l => l.Length == 0)) return null;
        return Create(layouts, SplitList(values.GetValueOrDefault("VariantList")),
            values.GetValueOrDefault("Model"), values.GetValueOrDefault("Options"));
    }

    static readonly Regex XorgOption = new(@"^\s*Option\s+""(Xkb\w+)""\s+""([^""]*)""", RegexOptions.Multiline);

    // /etc/X11/xorg.conf.d/*.conf: Option "XkbLayout" "gb" and friends.
    public static LayoutConfig? ParseXorgConf(string text)
    {
        var options = new Dictionary<string, string>();
        foreach (Match m in XorgOption.Matches(text)) options[m.Groups[1].Value] = m.Groups[2].Value;
        if (!options.TryGetValue("XkbLayout", out var layout) || layout.Trim().Length == 0) return null;
        var layouts = SplitList(layout);
        if (layouts.Any(l => l.Length == 0)) return null;
        return Create(layouts, SplitList(options.GetValueOrDefault("XkbVariant")),
            options.GetValueOrDefault("XkbModel"), options.GetValueOrDefault("XkbOptions"));
    }

    // XKB_DEFAULT_LAYOUT / _VARIANT / _MODEL / _OPTIONS.
    public static LayoutConfig? FromEnvironment(Func<string, string?> env)
    {
        var layout = env("XKB_DEFAULT_LAYOUT");
        if (string.IsNullOrWhiteSpace(layout)) return null;
        return Create(SplitList(layout), SplitList(env("XKB_DEFAULT_VARIANT")),
            env("XKB_DEFAULT_MODEL"), env("XKB_DEFAULT_OPTIONS"));
    }

    // First match wins: kxkbrc, xorg.conf.d, environment, then us.
    public static (LayoutConfig Config, string Source) Detect(string home, Func<string, string?> env)
    {
        if (ReadOrNull(Path.Combine(home, ".config", "kxkbrc")) is { } kxkb && ParseKxkbrc(kxkb) is { } fromKde)
            return (fromKde, "~/.config/kxkbrc");
        if (ReadOrNull("/etc/X11/xorg.conf.d/00-keyboard.conf") is { } xorg && ParseXorgConf(xorg) is { } fromXorg)
            return (fromXorg, "xorg.conf.d");
        if (FromEnvironment(env) is { } fromEnv) return (fromEnv, "XKB_DEFAULT_* environment");
        return (Us, "default");
    }

    static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Keeps empty entries: "," is two default layouts' variants.
    static List<string> SplitList(string? s) =>
        s is null ? [] : s.Split(',').Select(x => x.Trim()).ToList();

    static Dictionary<string, string> IniSection(string text, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line == $"[{section}]"; continue; }
            if (!inSection || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0) result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }
}
