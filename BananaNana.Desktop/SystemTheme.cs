using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Styling;
namespace BananaNana.Desktop;

// Light or dark, as the desktop says. KDE and GNOME answer through the freedesktop settings portal:
// org.freedesktop.appearance color-scheme is 1 = prefer dark, 2 = prefer light, 0 = no preference.
// The value is read with busctl; changes arrive on a dbus-monitor stream. Without the portal, KDE's kdeglobals is read.
public sealed class SystemTheme : IDisposable
{
    const string Destination = "org.freedesktop.portal.Desktop";
    const string ObjectPath = "/org/freedesktop/portal/desktop";
    const string Filter = "type='signal',interface='org.freedesktop.portal.Settings',member='SettingChanged'";
    const int TimeoutMs = 2000;
    Process? monitor;

    // Raised on a background thread whenever the portal reports a setting change. Re-read Current() to get the value.
    public event Action? Changed;

    public SystemTheme()
    {
        try
        {
            var start = new ProcessStartInfo("dbus-monitor") { RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add("--session");
            start.ArgumentList.Add(Filter);
            monitor = Process.Start(start);
            if (monitor is null) return;
            monitor.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line && line.Contains("SettingChanged", StringComparison.Ordinal)) Changed?.Invoke();
            };
            monitor.BeginOutputReadLine();
        }
        catch (Win32Exception) { monitor = null; } // no dbus-monitor: the theme is read at startup only
    }

    public static ThemeVariant Current() => ReadPortalColorScheme() switch
    {
        1 => ThemeVariant.Dark,
        2 => ThemeVariant.Light,
        _ => KdeGlobalsIsDark() ? ThemeVariant.Dark : ThemeVariant.Default,
    };

    static int? ReadPortalColorScheme()
    {
        var output = Run("busctl", "--user", "call", Destination, ObjectPath, "org.freedesktop.portal.Settings", "ReadOne",
            "ss", "org.freedesktop.appearance", "color-scheme");
        // The answer is "v u 1": a variant holding a uint32.
        var last = output?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(last, out var value) ? value : null;
    }

    // The [General] ColorScheme of KDE, e.g. BreezeDark. Dark when its name says so.
    static bool KdeGlobalsIsDark()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "kdeglobals");
            if (!File.Exists(path)) return false;
            bool general = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) general = line == "[General]";
                else if (general && line.StartsWith("ColorScheme=", StringComparison.Ordinal))
                    return line.Contains("Dark", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }

    // Standard output of a short command, or null when it fails or does not finish in time.
    static string? Run(string file, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return null;
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                process.Kill();
                return null;
            }
            var output = process.StandardOutput.ReadToEnd();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception) { return null; }
    }

    public void Dispose()
    {
        try
        {
            if (monitor is { HasExited: false }) monitor.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
        monitor?.Dispose();
        monitor = null;
    }
}
