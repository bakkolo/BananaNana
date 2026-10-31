using System.Text;
using BananaNana.Paste;
using Xunit;

namespace BananaNana.Tests;

public class CfHtmlTests
{
    static int Offset(string payload, string key)
    {
        int start = payload.IndexOf(key + ":", StringComparison.Ordinal) + key.Length + 1;
        return int.Parse(payload.Substring(start, 10));
    }

    [Fact]
    public void Offsets_point_at_utf8_bytes_of_fragment()
    {
        const string fragment = "<p><b>Zażółć</b> gęślą jaźń – €</p>";
        var bytes = CfHtml.Build(fragment);
        var payload = Encoding.UTF8.GetString(bytes);

        int startHtml = Offset(payload, "StartHTML"), endHtml = Offset(payload, "EndHTML");
        int startFragment = Offset(payload, "StartFragment"), endFragment = Offset(payload, "EndFragment");

        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, startHtml, 6));
        Assert.EndsWith("</html>", Encoding.UTF8.GetString(bytes, 0, endHtml));
        Assert.Equal(bytes.Length - 1, endHtml);
        Assert.Equal(0, bytes[^1]);
    }
}

public class RtfBuilderTests
{
    [Fact]
    public void Escapes_specials_and_unicode_as_ascii()
    {
        var rtf = new RtfBuilder().Bold("Zażółć").Text(@" {a}\b").ToString();

        Assert.Contains(@"{\b Za\u380?\u243?\u322?\u263?}", rtf);
        Assert.Contains(@" \{a\}\\b", rtf);
        Assert.All(rtf, c => Assert.True(c < 128));
        Assert.StartsWith(@"{\rtf1", rtf);
        Assert.EndsWith("}", rtf);
    }

    [Fact]
    public void Embeds_png_as_hex_with_twip_size()
    {
        var rtf = new RtfBuilder().PngImage([0x89, 0x50, 0xAB], 240, 96).ToString();

        Assert.Contains(@"{\pict\pngblip\picw240\pich96\picwgoal3600\pichgoal1440 8950ab}", rtf);
    }
}
