using System.ComponentModel;
using System.Diagnostics;

namespace BananaNana.Linux.Evdev;

// Follows KDE's active keyboard layout over the session bus. Both tools are optional: without them the
// layout group stays at 0 and nothing is logged.
public sealed class KdeLayoutMonitor : IDisposable
{
    const string Service = "org.kde.keyboard", ObjectPath = "/Layouts", Iface = "org.kde.KeyboardLayouts";
    static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    static readonly string[] MonitorArgs =
        ["--session", $"type='signal',interface='{Iface}',member='layoutChanged'"];

    readonly Action<uint> onGroup;
    Process? monitor;

    public KdeLayoutMonitor(Action<uint> onGroup) => this.onGroup = onGroup;

    // Current layout index, from qdbus6. Null when qdbus6 is missing or fails.
    public static uint? QueryGroup()
    {
        var output = Run("qdbus6", Service, ObjectPath, $"{Iface}.getLayout");
        return uint.TryParse(output?.Trim(), out var group) ? group : null;
    }

    // Runs dbus-monitor (line-buffered through stdbuf when present, so signals arrive promptly).
    public void Start()
    {
        monitor = Spawn("stdbuf", ["-oL", "dbus-monitor", .. MonitorArgs]);
        if (monitor is not null && monitor.WaitForExit(300)) monitor = null; // stdbuf could not run dbus-monitor
        monitor ??= Spawn("dbus-monitor", MonitorArgs);
        if (monitor is { } p) Task.Run(() => ReadSignals(p));
    }

    // dbus-monitor prints the signal header, then "   uint32 <index>" for the argument.
    void ReadSignals(Process p)
    {
        bool expectGroup = false;
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                if (line.StartsWith("signal", StringComparison.Ordinal))
                {
                    expectGroup = line.Contains("member=layoutChanged", StringComparison.Ordinal);
                    continue;
                }
                var t = line.Trim();
                if (expectGroup && t.StartsWith("uint32 ", StringComparison.Ordinal)
                    && uint.TryParse(t["uint32 ".Length..], out var group))
                {
                    expectGroup = false;
                    onGroup(group);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Stopped by Dispose.
        }
    }

    static Process? Spawn(string tool, string[] args)
    {
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            return Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    static string? Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(QueryTimeout))
            {
                p.Kill(entireProcessTree: true);
                return null;
            }
            return p.ExitCode == 0 ? stdout.Result : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var p = monitor;
        monitor = null;
        if (p is null) return;
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        p.Dispose();
    }
}
