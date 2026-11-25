using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BananaNana.Paste;
using Xunit;

namespace BananaNana.Tests;

public class LinkTests
{
    static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    static FlowDocument WithLink(string url)
    {
        var p = new Paragraph(new Run("See "));
        p.Inlines.Add(new Hyperlink(new Run("our offer")) { NavigateUri = new Uri(url) });
        return new FlowDocument(p);
    }

    [Fact]
    public void Links_survive_storage_and_paste_as_html_and_rtf_links() => Sta(() =>
    {
        var box = new RichTextBox { Document = WithLink("https://example.com/offer?a=1&b=2") };
        using var stream = new MemoryStream();
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Save(stream, DataFormats.XamlPackage);

        var content = RichContentRenderer.Render(RichContentRenderer.LoadXamlPackage(Convert.ToBase64String(stream.ToArray())));

        Assert.Matches("<a href=\"https://example.com/offer\\?a=1&amp;b=2\">.*our offer.*</a>", content.Html);
        Assert.Contains("HYPERLINK", content.Rtf);
        Assert.Contains("https://example.com/offer?a=1&b=2", content.Rtf);
    });

    [Fact]
    public void Unsafe_links_are_pasted_as_plain_text() => Sta(() =>
    {
        var html = RichContentRenderer.Render(WithLink("file:///C:/Windows/notepad.exe")).Html;

        Assert.DoesNotContain("<a ", html);
        Assert.Contains("our offer", html);
    });
}
