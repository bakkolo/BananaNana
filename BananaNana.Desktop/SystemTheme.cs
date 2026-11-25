using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Styling;
namespace BananaNana.Desktop;

// Light or dark, as the desktop says. Order: the freedesktop settings portal (org.freedesktop.appearance color-scheme:
// 1 = prefer dark, 2 = prefer light, 0 = no preference; read with busctl), then KDE's own answer from
// kreadconfig6 (which also knows the system default), then the [General] ColorScheme line of kdeglobals.
// Changes arrive from a dbus-monitor stream of SettingChanged and from a watch on ~/.config/kdeglobals.
public sealed class SystemTheme : IDisposable
{
    const string Destination = "org.freedesktop.portal.Desktop";
    const string ObjectPath = "/org/freedesktop/portal/desktop";
    const string Filter = "type='signal',interface='org.freedesktop.portal.Settings',member='SettingChanged'";
    const int TimeoutMs = 2000;
    const int DebounceMs = 300;
    Process? monitor;
    Timer? debounce;
    FileSystemWatcher? watcher;

    // Raised on a background thread when the desktop theme may have changed. Re-read Current() to get the value.
    public event Action? Changed;

    public SystemTheme()
    {
        debounce = new Timer(_ => Changed?.Invoke());
        StartMonitor();
        StartWatcher();
    }

    public static ThemeVariant Current()
    {
        var portal = ReadPortalColorScheme();
        if (portal == 1) return ThemeVariant.Dark;
        if (portal == 2) return ThemeVariant.Light;
        var scheme = KdeColorScheme();
        if (scheme is null) return ThemeVariant.Default;
        return scheme.Contains("Dark", StringComparison.OrdinalIgnoreCase) ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    static int? ReadPortalColorScheme()
    {
        var output = Run("busctl", "--user", "call", Destination, ObjectPath, "org.freedesktop.portal.Settings", "ReadOne",
            "ss", "org.freedesktop.appearance", "color-scheme");
        // The answer is "v u 1": a variant holding a uint32.
        var last = output?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(last, out var value) ? value : null;
    }

    // The colour scheme name KDE uses ("BreezeDark", ...), or null when none can be found.
    static string? KdeColorScheme()
    {
        var fromTool = Run("kreadconfig6", "--file", "kdeglobals", "--group", "General", "--key", "ColorScheme")?.Trim();
        if (!string.IsNullOrEmpty(fromTool)) return fromTool;
        return ReadColorSchemeLine(Path.Combine(ConfigDirectory(), "kdeglobals"));
    }

    // The ColorScheme= line of the [General] group in a KDE config file.
    static string? ReadColorSchemeLine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            bool general = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) general = line == "[General]";
                else if (general && line.StartsWith("ColorScheme=", StringComparison.Ordinal))
                    return line["ColorScheme=".Length..].Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    static string ConfigDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    void StartMonitor()
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
        catch (Win32Exception) { monitor = null; } // no dbus-monitor: the portal path only reacts at startup
    }

    // The portal refuses some sessions (for example a newgrp shell), so kdeglobals is watched as well.
    void StartWatcher()
    {
        var directory = ConfigDirectory();
        if (!Directory.Exists(directory)) return;
        watcher = new FileSystemWatcher(directory, "kdeglobals")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => Touch();
        watcher.Created += (_, _) => Touch();
        watcher.Renamed += (_, _) => Touch();
        watcher.EnableRaisingEvents = true;
    }

    void Touch() => debounce?.Change(DebounceMs, Timeout.Infinite);

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
        watcher?.Dispose();
        watcher = null;
        debounce?.Dispose();
        debounce = null;
        try
        {
            if (monitor is { HasExited: false }) monitor.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
        monitor?.Dispose();
        monitor = null;
    }
}
