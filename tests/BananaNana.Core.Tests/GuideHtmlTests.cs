using Xunit;

namespace BananaNana.Core.Tests;

// GuideHtml: the one builder behind Copy guide, the web page export and the snippet.
public class GuideHtmlTests
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void Fragment_has_the_title_then_the_steps_in_order()
    {
        var steps = new[] { new GuideHtmlStep("First", "Do one.", null, 0), new GuideHtmlStep("Second", "Do two.", null, 0) };

        var html = GuideHtml.Fragment("Guide", "Intro text", steps);

        Assert.StartsWith("<h1>Guide</h1>", html);
        Assert.Contains("<p>Intro text</p>", html);
        int one = html.IndexOf("<h2>1. First</h2>", StringComparison.Ordinal);
        int two = html.IndexOf("<h2>2. Second</h2>", StringComparison.Ordinal);
        Assert.True(one > 0 && two > one);
        Assert.True(html.IndexOf("<p>Do one.</p>", StringComparison.Ordinal) < two);
    }

    [Fact]
    public void Each_text_line_is_a_paragraph_and_blank_lines_are_dropped()
    {
        var html = GuideHtml.Fragment("", "", [new GuideHtmlStep("S", "One\r\n\r\nTwo\nThree", null, 0)]);

        Assert.Contains("<p>One</p>", html);
        Assert.Contains("<p>Two</p>", html);
        Assert.Contains("<p>Three</p>", html);
        Assert.DoesNotContain("<h1>", html);
    }

    [Fact]
    public void Pictures_are_embedded_as_png_data_with_the_width_capped_at_640()
    {
        var steps = new[] { new GuideHtmlStep("Wide", "", Png, 1200), new GuideHtmlStep("Narrow", "", Png, 300) };

        var html = GuideHtml.Fragment("", "", steps);

        string base64 = Convert.ToBase64String(Png);
        Assert.Contains($"<img src=\"data:image/png;base64,{base64}\" width=\"640\"", html);
        Assert.Contains($"<img src=\"data:image/png;base64,{base64}\" width=\"300\"", html);
    }

    [Fact]
    public void Steps_without_a_picture_have_no_img()
    {
        var html = GuideHtml.Fragment("T", "", [new GuideHtmlStep("S", "Text", null, 0)]);

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Text_is_escaped()
    {
        var html = GuideHtml.Fragment("Tom & <Jerry>", "\"quoted\"", [new GuideHtmlStep("A <b> & B", "x < y", null, 0)]);

        Assert.Contains("<h1>Tom &amp; &lt;Jerry&gt;</h1>", html);
        Assert.Contains("<h2>1. A &lt;b&gt; &amp; B</h2>", html);
        Assert.Contains("<p>x &lt; y</p>", html);
        Assert.DoesNotContain("<Jerry>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Document_is_a_full_page_with_the_title_and_the_same_steps()
    {
        var steps = new[] { new GuideHtmlStep("Only step", "Text", Png, 100) };

        var page = GuideHtml.Document("My guide", "", steps);

        Assert.StartsWith("<!doctype html>", page);
        Assert.Contains("<meta charset=\"utf-8\">", page);
        Assert.Contains("<title>My guide</title>", page);
        Assert.Contains("<style>", page);
        Assert.Contains(GuideHtml.Fragment("My guide", "", steps), page);
        Assert.EndsWith("</html>\n", page);
    }

    [Fact]
    public void PlainText_has_the_title_and_numbered_steps()
    {
        var steps = new[] { new GuideHtmlStep("Open it", "Press the button.\nThen wait.", Png, 10), new GuideHtmlStep("Close it", "", null, 0) };

        var text = GuideHtml.PlainText("Guide", "Intro", steps);

        Assert.Equal("Guide\nIntro\n\n1. Open it\nPress the button.\nThen wait.\n\n2. Close it", text);
    }

    [Fact]
    public void Empty_title_gives_no_h1()
    {
        Assert.DoesNotContain("<h1>", GuideHtml.Fragment("  ", "", []));
        Assert.Equal("", GuideHtml.PlainText("", "", []));
    }
}
