using BananaNana.Linux.Clipboard;
using Xunit;

namespace BananaNana.Linux.Tests;

public class ClipboardFormatTests
{
    [Fact]
    public void Html_equal_to_plain_text_is_sent_as_plain_text()
    {
        var (mime, payload) = ClipboardFormat.Choose("Kind regards", "Kind regards");
        Assert.Equal(ClipboardFormat.Plain, mime);
        Assert.Equal("Kind regards", payload);
    }

    [Fact]
    public void Formatted_html_is_sent_as_html()
    {
        var (mime, payload) = ClipboardFormat.Choose("<b>Bold</b>", "Bold");
        Assert.Equal(ClipboardFormat.Html, mime);
        Assert.Equal("<b>Bold</b>", payload);
    }

    [Fact]
    public void Empty_html_falls_back_to_plain_text()
    {
        var (mime, payload) = ClipboardFormat.Choose("", "Plain only");
        Assert.Equal(ClipboardFormat.Plain, mime);
        Assert.Equal("Plain only", payload);
    }
}
