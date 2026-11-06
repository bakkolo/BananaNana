using BananaNana.Input;
using Xunit;

namespace BananaNana.Tests;

public class ForegroundGuardTests
{
    [Theory]
    [InlineData("keepass.exe", "keepass.exe", true)]
    [InlineData("KeePass.EXE", "keepass", true)]
    [InlineData("mstsc.exe", "keepass.exe\nmstsc.exe", true)]
    [InlineData("mstsc.exe", "keepass.exe, mstsc", true)]
    [InlineData("word.exe", "keepass.exe\nmstsc.exe", false)]
    [InlineData("word.exe", "", false)]
    [InlineData("", "keepass.exe", false)]
    public void Exclusion_list_matches_program_names(string exe, string list, bool expected) =>
        Assert.Equal(expected, ForegroundGuard.IsExcluded(exe, list));

    [Fact]
    public void Safety_options_are_on_by_default()
    {
        var s = new AppSettings();
        Assert.True(s.ProtectPasswords && s.SkipAdminApps && s.UseExcludedApps);
    }
}
