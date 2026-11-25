using BananaNana.Linux.Evdev;
using Xunit;

namespace BananaNana.Linux.Tests;

public class LayoutConfigTests
{
    const string Kxkbrc = """
        [Layout]
        DisplayNames=
        LayoutList=gb,pl
        Model=pc105
        Options=compose:ralt
        ResetOldOptions=true
        SwitchMode=Global
        Use=true
        VariantList=,dvorak
        """;

    [Fact]
    public void Kxkbrc_with_use_true_gives_all_layouts_in_order()
    {
        var c = LayoutConfig.ParseKxkbrc(Kxkbrc);
        Assert.NotNull(c);
        Assert.Equal("gb,pl", c.LayoutString);
        Assert.Equal(",dvorak", c.VariantString);
        Assert.Equal("pc105", c.Model);
        Assert.Equal("compose:ralt", c.Options);
        Assert.Equal("pl(dvorak)", c.Describe(1));
        Assert.Equal("gb", c.Describe(0));
    }

    [Fact]
    public void Kxkbrc_with_use_false_is_not_used()
    {
        Assert.Null(LayoutConfig.ParseKxkbrc(Kxkbrc.Replace("Use=true", "Use=false")));
    }

    [Fact]
    public void Kxkbrc_without_empty_variant_list_has_no_variant_string()
    {
        var c = LayoutConfig.ParseKxkbrc("[Layout]\r\nLayoutList=de\r\nUse=true\r\nVariantList=\r\n");
        Assert.NotNull(c);
        Assert.Equal("de", c.LayoutString);
        Assert.Null(c.VariantString);
        Assert.Null(c.Model);
    }

    [Fact]
    public void Kxkbrc_keys_in_other_sections_are_ignored()
    {
        const string text = "[Other]\nUse=true\nLayoutList=fr\n[Layout]\nUse=true\nLayoutList=pl\n";
        Assert.Equal("pl", LayoutConfig.ParseKxkbrc(text)!.LayoutString);
    }

    [Fact]
    public void Xorg_conf_reads_xkb_options()
    {
        const string text = """
            Section "InputClass"
                Identifier "keyboard"
                MatchIsKeyboard "on"
                Option "XkbLayout" "fr,us"
                Option "XkbVariant" "azerty"
                Option "XkbModel" "pc104"
                Option "XkbOptions" "grp:alt_shift_toggle"
            EndSection
            """;
        var c = LayoutConfig.ParseXorgConf(text);
        Assert.NotNull(c);
        Assert.Equal("fr,us", c.LayoutString);
        Assert.Equal("azerty,", c.VariantString);
        Assert.Equal("pc104", c.Model);
        Assert.Equal("grp:alt_shift_toggle", c.Options);
    }

    [Fact]
    public void Xorg_conf_without_layout_is_not_used()
    {
        Assert.Null(LayoutConfig.ParseXorgConf("Section \"InputClass\"\n  Option \"XkbModel\" \"pc105\"\nEndSection\n"));
        Assert.Null(LayoutConfig.ParseXorgConf("# Option \"XkbLayout\" \"gb\"\n"));
    }

    [Fact]
    public void Environment_variables_are_read_when_set()
    {
        var env = new Dictionary<string, string?>
        {
            ["XKB_DEFAULT_LAYOUT"] = "de",
            ["XKB_DEFAULT_VARIANT"] = "nodeadkeys",
        };
        var c = LayoutConfig.FromEnvironment(k => env.GetValueOrDefault(k));
        Assert.NotNull(c);
        Assert.Equal("de", c.LayoutString);
        Assert.Equal("nodeadkeys", c.VariantString);
        Assert.Null(LayoutConfig.FromEnvironment(_ => null));
    }

    [Fact]
    public void Cli_spec_splits_layout_and_variant()
    {
        var c = LayoutConfig.Parse("pl:dvorak");
        Assert.Equal("pl", c.LayoutString);
        Assert.Equal("dvorak", c.VariantString);
        Assert.Equal("de", LayoutConfig.Parse("de").LayoutString);
        Assert.Null(LayoutConfig.Parse("de").VariantString);
        Assert.Throws<ArgumentException>(() => LayoutConfig.Parse(":dvorak"));
    }

    [Fact]
    public void Default_is_us()
    {
        Assert.Equal("us", LayoutConfig.Us.LayoutString);
    }
}
