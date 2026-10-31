using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BananaNana.Paste;
using Xunit;

namespace BananaNana.Tests;

public class RichContentRendererTests
{
    // WPF elements must be created and used on an STA thread; xUnit runs tests on MTA threads.
    static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    static BitmapSource Pixel()
    {
        var bmp = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 2 * 4], 4 * 4);
        bmp.Freeze();
        return bmp;
    }

    static FlowDocument Sample()
    {
        var line = new Paragraph();
        line.Inlines.Add(new Bold(new Run("Zażółć")));
        line.Inlines.Add(new Run(" <gęślą> & "));
        line.Inlines.Add(new Run("jaźń") { FontStyle = FontStyles.Italic, TextDecorations = TextDecorations.Underline });
        var list = new List { MarkerStyle = TextMarkerStyle.Disc };
        list.ListItems.Add(new ListItem(new Paragraph(new Run("Pierwszy"))));
        list.ListItems.Add(new ListItem(new Paragraph(new Run("Drugi"))));
        var doc = new FlowDocument(line);
        doc.Blocks.Add(list);
        doc.Blocks.Add(new Paragraph(new InlineUIContainer(new Image { Source = Pixel(), Width = 40, Height = 20 })));
        return doc;
    }

    [Fact]
    public void Renders_formatting_lists_and_images_to_html() => Sta(() =>
    {
        var html = RichContentRenderer.Render(Sample()).Html;

        Assert.StartsWith("<p><b>Zażółć</b> &lt;gęślą&gt; &amp; <i><u>jaźń</u></i></p>", html);
        Assert.Contains("<ul><li>Pierwszy</li><li>Drugi</li></ul>", html);
        Assert.Matches("<p><img src=\"data:image/png;base64,[A-Za-z0-9+/=]+\" width=\"40\" height=\"20\"></p>$", html);
    });

    [Fact]
    public void Rtf_is_ascii_with_image_and_text_keeps_polish() => Sta(() =>
    {
        var content = RichContentRenderer.Render(Sample());

        Assert.Contains(@"\pict", content.Rtf);
        Assert.All(content.Rtf, c => Assert.True(c < 128));
        Assert.Contains("Zażółć <gęślą> & jaźń", content.PlainText);
    });

    [Fact]
    public void Images_survive_xamlpackage_round_trip() => Sta(() =>
    {
        var box = new RichTextBox { Document = Sample() };
        using var stream = new MemoryStream();
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Save(stream, DataFormats.XamlPackage);

        var reloaded = RichContentRenderer.LoadXamlPackage(Convert.ToBase64String(stream.ToArray()));
        var html = RichContentRenderer.Render(reloaded).Html;

        Assert.Contains("<img src=\"data:image/png;base64,", html);
        Assert.Contains("<b>Zażółć</b>", html);
    });

    [Fact]
    public void Center_and_right_alignment_survive_storage_and_render() => Sta(() =>
    {
        var box = new RichTextBox();
        box.Document.Blocks.Clear();
        box.Document.Blocks.Add(new Paragraph(new Run("left")));
        box.Document.Blocks.Add(new Paragraph(new Run("middle")) { TextAlignment = TextAlignment.Center });
        box.Document.Blocks.Add(new Paragraph(new Run("end")) { TextAlignment = TextAlignment.Right });
        using var stream = new MemoryStream();
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Save(stream, DataFormats.XamlPackage);

        var content = RichContentRenderer.Render(RichContentRenderer.LoadXamlPackage(Convert.ToBase64String(stream.ToArray())));

        Assert.StartsWith("<p>left</p><p style=\"text-align:center\">middle</p><p style=\"text-align:right\">end</p>", content.Html);
        Assert.Contains(@"\qc", content.Rtf);
        Assert.Contains(@"\qr", content.Rtf);
    });

    [Fact]
    public void Oversized_images_are_sent_at_displayed_size() => Sta(() =>
    {
        // A 1000x500 "screenshot" shown at 100x50: HTML and RTF must carry a 100x50 picture, not the original.
        var big = BitmapSource.Create(1000, 500, 96, 96, PixelFormats.Bgra32, null, new byte[1000 * 500 * 4], 1000 * 4);
        var doc = new FlowDocument(new Paragraph(new InlineUIContainer(new Image { Source = big, Width = 100, Height = 50 })));

        var content = RichContentRenderer.Render(doc);

        var base64 = System.Text.RegularExpressions.Regex.Match(content.Html, "base64,([^\"]+)").Groups[1].Value;
        var sent = BitmapFrame.Create(new MemoryStream(Convert.FromBase64String(base64)));
        Assert.Equal((100, 50), (sent.PixelWidth, sent.PixelHeight));
        Assert.Contains("width=\"100\" height=\"50\"", content.Html);
        // WPF writes {\pict\picwgoal..\pichgoal..\pngblip <hex>}: decode the embedded PNG itself.
        var hex = System.Text.RegularExpressions.Regex.Match(content.Rtf, @"\\pngblip\s*([0-9a-fA-F\s]+)\}").Groups[1].Value;
        var rtfPicture = BitmapFrame.Create(new MemoryStream(Convert.FromHexString(System.Text.RegularExpressions.Regex.Replace(hex, @"\s", ""))));
        Assert.Equal((100, 50), (rtfPicture.PixelWidth, rtfPicture.PixelHeight));
    });

    [Fact]
    public void Image_size_respects_max_width() => Sta(() =>
    {
        var doc = new FlowDocument(new Paragraph(new InlineUIContainer(new Image { Source = Pixel(), MaxWidth = 2 })));

        Assert.Contains("width=\"2\" height=\"1\"", RichContentRenderer.Render(doc).Html);
    });
}
