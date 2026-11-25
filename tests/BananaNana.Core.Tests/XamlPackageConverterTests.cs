using System.IO;
using System.IO.Compression;
using System.Text;
using BananaNana.Import;
using Xunit;

namespace BananaNana.Tests;

public class XamlPackageConverterTests
{
    // 1x1 PNG.
    const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    // Builds a base64 XamlPackage: [Content_Types].xml, _rels/.rels pointing at the document, and the given parts.
    static string Package(string xaml, string documentPath = "Xaml/Document.xaml", params (string Name, byte[] Data)[] parts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + $"<Override PartName=\"/{documentPath}\" ContentType=\"application/xaml+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + $"<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/xps/2005/06/fixedrepresentation\" Target=\"/{documentPath}\"/></Relationships>");
            Add(zip, documentPath, $"<?xml version=\"1.0\" encoding=\"utf-8\"?><FlowDocument {Ns}>{xaml}</FlowDocument>");
            foreach (var (name, data) in parts)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(data);
            }
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    static ConvertedContent Read(string xaml) => XamlPackageConverter.Read(Package(xaml), ";test");

    [Fact]
    public void Bold_italic_underline_and_escaping()
    {
        var content = Read(
            "<Section><Paragraph><Bold>Zażółć</Bold><Run> &lt;gęślą&gt; &amp; </Run>"
            + "<Run FontStyle=\"Italic\" TextDecorations=\"Underline\">jaźń</Run></Paragraph></Section>");

        Assert.Equal("<p><b>Zażółć</b> &lt;gęślą&gt; &amp; <i><u>jaźń</u></i></p>", content.Html);
        Assert.Equal("Zażółć <gęślą> & jaźń", content.PlainText);
    }

    [Fact]
    public void Font_weight_and_strikethrough_attributes()
    {
        var content = Read("<Paragraph><Run FontWeight=\"SemiBold\" TextDecorations=\"Strikethrough\">old</Run> <Run FontWeight=\"400\">plain</Run></Paragraph>");

        Assert.Equal("<p><b><s>old</s></b> plain</p>", content.Html);
    }

    [Fact]
    public void Colours_hex_named_and_black_default()
    {
        var content = Read(
            "<Paragraph><Run Foreground=\"#FFC0392B\">red</Run> <Run Foreground=\"Blue\">blue</Run> "
            + "<Run Foreground=\"#FF000000\">black</Run> <Run><Run.Foreground><SolidColorBrush Color=\"#80112233\"/></Run.Foreground>hex8</Run></Paragraph>");

        Assert.Equal("<p><span style=\"color:#C0392B\">red</span> <span style=\"color:#0000FF\">blue</span> black <span style=\"color:#112233\">hex8</span></p>", content.Html);
    }

    [Fact]
    public void Font_size_is_written_only_when_it_differs_from_body_text()
    {
        var content = Read("<Paragraph><Run>Body text that is long enough to be the body size</Run> <Run FontSize=\"20\">Big</Run></Paragraph>");

        Assert.Equal("<p>Body text that is long enough to be the body size <span style=\"font-size:20px\">Big</span></p>", content.Html);
    }

    [Fact]
    public void Alignment_and_line_breaks()
    {
        var content = Read("<Paragraph TextAlignment=\"Center\">a<LineBreak/>b</Paragraph>");

        Assert.Equal("<p style=\"text-align:center\">a<br>b</p>", content.Html);
        Assert.Equal("a\nb", content.PlainText);
    }

    [Fact]
    public void Lists_ordered_unordered_and_nested()
    {
        var content = Read(
            "<List MarkerStyle=\"Decimal\"><ListItem><Paragraph>One</Paragraph></ListItem>"
            + "<ListItem><Paragraph>Two</Paragraph><List MarkerStyle=\"Disc\"><ListItem><Paragraph>Nested</Paragraph></ListItem></List></ListItem></List>"
            + "<List><ListItem><Paragraph>Bullet</Paragraph></ListItem></List>");

        Assert.Equal(
            "<ol><li>One</li><li><p>Two</p><ul><li>Nested</li></ul></li></ol><ul><li>Bullet</li></ul>",
            content.Html);
        Assert.Equal("One\nTwo\nNested\nBullet", content.PlainText);
    }

    [Fact]
    public void Tables_write_rows_and_cells_and_tab_separated_text()
    {
        var content = Read(
            "<Table><TableRowGroup><TableRow><TableCell><Paragraph>A</Paragraph></TableCell><TableCell><Paragraph>B</Paragraph></TableCell></TableRow>"
            + "<TableRow><TableCell><Paragraph>C</Paragraph></TableCell><TableCell><Paragraph>D</Paragraph></TableCell></TableRow></TableRowGroup></Table>");

        const string cell = "<td style=\"border:1px solid #999;padding:2px 6px\">";
        Assert.Equal(
            "<table style=\"border-collapse:collapse\"><tr>" + cell + "<p>A</p></td>" + cell + "<p>B</p></td></tr>"
            + "<tr>" + cell + "<p>C</p></td>" + cell + "<p>D</p></td></tr></table>",
            content.Html);
        Assert.Equal("A\tB\nC\tD", content.PlainText);
    }

    [Fact]
    public void Links_allow_only_http_https_and_mailto()
    {
        var content = Read(
            "<Paragraph><Hyperlink NavigateUri=\"https://example.com/a?x=1&amp;y=2\">site</Hyperlink> "
            + "<Hyperlink NavigateUri=\"mailto:matt@example.com\">mail</Hyperlink> "
            + "<Hyperlink NavigateUri=\"javascript:alert(1)\">bad</Hyperlink></Paragraph>");

        Assert.Equal(
            "<p><a href=\"https://example.com/a?x=1&amp;y=2\">site</a> <a href=\"mailto:matt@example.com\">mail</a> bad</p>",
            content.Html);
    }

    [Fact]
    public void Images_are_embedded_as_data_uris_with_size()
    {
        var png = Convert.FromBase64String(PngBase64);
        var content = XamlPackageConverter.Read(Package(
            "<BlockUIContainer><Image Source=\"./Image1.png\" Width=\"40\" Height=\"20\"/></BlockUIContainer>",
            "Xaml/Document.xaml",
            ("Xaml/Image1.png", png)));

        Assert.Equal($"<p><img src=\"data:image/png;base64,{PngBase64}\" width=\"40\" height=\"20\"></p>", content.Html);
        Assert.Equal("", content.PlainText);
    }

    [Fact]
    public void Image_found_at_package_root_and_bitmap_image_source_form()
    {
        var png = Convert.FromBase64String(PngBase64);
        var content = XamlPackageConverter.Read(Package(
            "<Paragraph><InlineUIContainer><Image><Image.Source><BitmapImage UriSource=\"Image2.png\"/></Image.Source></Image></InlineUIContainer></Paragraph>",
            "Xaml/Document.xaml",
            ("Image2.png", png)));

        Assert.Equal($"<p><img src=\"data:image/png;base64,{PngBase64}\"></p>", content.Html);
    }

    [Fact]
    public void Missing_image_is_left_out_without_failing()
    {
        var content = Read("<Paragraph>before <InlineUIContainer><Image Source=\"missing.png\"/></InlineUIContainer>after</Paragraph>");

        Assert.Equal("<p>before after</p>", content.Html);
    }

    [Fact]
    public void Unknown_elements_are_walked_through()
    {
        var content = Read("<Figure><Paragraph>Inside</Paragraph><Mystery><Run>Kept</Run></Mystery></Figure>");

        Assert.Equal("<p>Inside</p><p>Kept</p>", content.Html);
        Assert.Equal("Inside\nKept", content.PlainText);
    }

    [Fact]
    public void Document_part_is_found_without_relationships()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            Add(zip, "Other/Doc.xaml", $"<Section {Ns}><Paragraph>Found</Paragraph></Section>");
        var content = XamlPackageConverter.Read(Convert.ToBase64String(ms.ToArray()), ";norels");

        Assert.Equal("<p>Found</p>", content.Html);
    }

    [Fact]
    public void Corrupt_content_throws_with_the_trigger_name()
    {
        var notBase64 = Assert.Throws<InvalidDataException>(() => XamlPackageConverter.Read("%%% not base64", ";broken"));
        Assert.Contains(";broken", notBase64.Message);

        var notZip = Convert.ToBase64String(Encoding.UTF8.GetBytes("plain text, not a zip"));
        var notZipError = Assert.Throws<InvalidDataException>(() => XamlPackageConverter.Read(notZip, ";zip"));
        Assert.Contains(";zip", notZipError.Message);
    }

    [Fact]
    public void Empty_content_is_an_empty_snippet()
    {
        var content = XamlPackageConverter.Read("", ";empty");

        Assert.Equal(("", ""), (content.Html, content.PlainText));
    }
}
