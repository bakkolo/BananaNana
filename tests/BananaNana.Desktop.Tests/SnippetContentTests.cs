using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

public class SnippetContentTests
{
    [Theory]
    [InlineData("<p>Kind regards</p>", false)]
    [InlineData("<p>Hi<br>there</p>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("<p><b>Bold</b></p>", true)]
    [InlineData("<p><span style=\"color:red\">x</span></p>", true)]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">", false)]
    public void Formatting_is_any_tag_plain_text_cannot_carry(string? html, bool expected) =>
        Assert.Equal(expected, SnippetContent.HasFormatting(html));

    [Theory]
    [InlineData("<p>x</p>", false)]
    [InlineData("<p><img src=\"a.png\"></p>", true)]
    [InlineData(null, false)]
    public void Pictures_are_detected(string? html, bool expected) =>
        Assert.Equal(expected, SnippetContent.HasImages(html));

    [Fact]
    public void Summary_is_one_line_and_short()
    {
        Assert.Equal("a b c", SnippetContent.Summary("  a   b\n c "));
        var long120 = SnippetContent.Summary(new string('x', 300));
        Assert.Equal(121, long120.Length); // 120 characters and an ellipsis
        Assert.EndsWith("…", long120);
    }
}
