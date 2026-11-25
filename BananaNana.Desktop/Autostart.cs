namespace BananaNana.Desktop;

// "Start Banana Nana when I log in": a per-user desktop entry in ~/.config/autostart (no root needed).
public static class Autostart
{
    public const string FileName = "banananana.desktop";
    public const string TrayArgument = "--tray";

    public static string DefaultConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } dir && Path.IsPathRooted(dir)
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    public static string FilePath(string? configHome = null) =>
        Path.Combine(configHome ?? DefaultConfigHome, "autostart", FileName);

    public static bool IsEnabled(string? configHome = null) => File.Exists(FilePath(configHome));

    public static void SetEnabled(bool enabled, string exePath, string? configHome = null)
    {
        var path = FilePath(configHome);
        if (!enabled)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Content(exePath));
        File.Move(temp, path, true);
    }

    // The desktop entry text. Exec starts this exact binary hidden in the tray.
    public static string Content(string exePath) =>
        "[Desktop Entry]\n"
        + "Type=Application\n"
        + "Name=Banana Nana\n"
        + "Comment=Text expander\n"
        + $"Exec={ExecArgument(exePath)} {TrayArgument}\n"
        + "Icon=banananana\n"
        + "Terminal=false\n"
        + "Categories=Utility;\n";

    // Exec quoting from the desktop entry spec: paths with spaces or shell characters are double-quoted, and
    // the characters that are special inside quotes (and the percent sign) are escaped.
    public static string ExecArgument(string path)
    {
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return path.AsSpan().IndexOfAny(" \t\"'\\`$%><~|&;*?#()") >= 0 ? $"\"{escaped}\"" : escaped;
    }
}
