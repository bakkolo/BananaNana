using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

public class AutostartTests
{
    [Fact]
    public void The_desktop_entry_starts_the_binary_in_the_tray()
    {
        var content = Autostart.Content("/home/user/bin/banananana");
        var lines = content.Split('\n');
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Name=Banana Nana", lines);
        Assert.Contains("Exec=/home/user/bin/banananana --tray", lines);
        Assert.Contains("Icon=banananana", lines);
    }

    [Fact]
    public void A_path_with_spaces_is_quoted_in_exec()
    {
        Assert.Equal("\"/home/user/My Apps/banananana\"", Autostart.ExecArgument("/home/user/My Apps/banananana"));
        Assert.Contains("Exec=\"/home/user/My Apps/banananana\" --tray", Autostart.Content("/home/user/My Apps/banananana"));
    }

    [Fact]
    public void Percent_signs_are_doubled_and_the_path_is_quoted()
    {
        Assert.Equal("\"/opt/50%%/banananana\"", Autostart.ExecArgument("/opt/50%/banananana"));
    }

    [Fact]
    public void Enabling_writes_the_file_and_disabling_removes_it()
    {
        var home = Path.Combine(Path.GetTempPath(), "bn-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(Autostart.IsEnabled(home));

            Autostart.SetEnabled(true, "/home/user/bin/banananana", home);
            var path = Path.Combine(home, "autostart", "banananana.desktop");
            Assert.True(File.Exists(path));
            Assert.Equal(Autostart.Content("/home/user/bin/banananana"), File.ReadAllText(path));
            Assert.True(Autostart.IsEnabled(home));

            Autostart.SetEnabled(false, "/home/user/bin/banananana", home);
            Assert.False(File.Exists(path));
            Autostart.SetEnabled(false, "/home/user/bin/banananana", home); // already off: no error
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }
}
