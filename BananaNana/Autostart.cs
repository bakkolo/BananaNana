using System.IO;
using Microsoft.Win32;
namespace BananaNana;

// "Start with Windows": a per-user Run entry (no admin rights) that starts the EXE hidden in the tray.
public static class Autostart
{
    // Tests point this at a scratch key so they never touch the real Run entry.
    internal static string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string ExePath = Environment.ProcessPath ?? "";
    const string ValueName = "BananaNana";
    public const string TrayArgument = "--tray";

    static string Command => $"\"{ExePath}\" {TrayArgument}";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    // If the registered EXE no longer exists (the user moved it), point the entry at this copy.
    // A registered EXE that still exists is left alone, so running a second copy doesn't hijack it.
    public static void RepairIfMoved()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key?.GetValue(ValueName) is not string current) return;
        var registered = current.StartsWith('"') ? current[1..current.IndexOf('"', 1)] : current.Split(' ')[0];
        if (!File.Exists(registered)) key.SetValue(ValueName, Command);
    }
}
