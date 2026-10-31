using System.IO;
using Microsoft.Win32;
using Xunit;

namespace BananaNana.Tests;

// Uses a scratch key under HKCU\Software, never the real Run key.
public class AutostartTests : IDisposable
{
    readonly string key = @"Software\BananaNanaTests\Run-" + Guid.NewGuid().ToString("N");
    readonly string originalKey = Autostart.KeyPath, originalExe = Autostart.ExePath;

    public AutostartTests() => Autostart.KeyPath = key;

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        Autostart.KeyPath = originalKey;
        Autostart.ExePath = originalExe;
    }

    static string? Value(string key) => Registry.CurrentUser.OpenSubKey(key)?.GetValue("BananaNana") as string;

    [Fact]
    public void Enable_writes_a_quoted_tray_command_and_disable_removes_it()
    {
        Autostart.ExePath = @"C:\Program Files\Banana Nana\BananaNana.exe";
        Assert.False(Autostart.Enabled);

        Autostart.Enabled = true;
        Assert.True(Autostart.Enabled);
        Assert.Equal("\"C:\\Program Files\\Banana Nana\\BananaNana.exe\" --tray", Value(key));

        Autostart.Enabled = false;
        Assert.False(Autostart.Enabled);
        Assert.Null(Value(key));
        Autostart.Enabled = false;   // disabling twice is fine
    }

    [Fact]
    public void Repair_repoints_only_when_the_registered_exe_is_gone()
    {
        var existing = Path.GetTempFileName();
        try
        {
            Autostart.ExePath = existing;
            Autostart.Enabled = true;
            Autostart.ExePath = @"C:\Elsewhere\BananaNana.exe";
            Autostart.RepairIfMoved();
            Assert.Contains(existing, Value(key));            // still exists: left alone

            File.Delete(existing);
            Autostart.RepairIfMoved();
            Assert.Equal("\"C:\\Elsewhere\\BananaNana.exe\" --tray", Value(key));
        }
        finally { File.Delete(existing); }
    }
}
