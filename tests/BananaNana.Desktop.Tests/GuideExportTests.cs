using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The Guide maker's exports: PDF, Word, web page and snippet. Files are checked by their structure, not by eye.
public class GuideExportTests : IDisposable
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    readonly string folder = Path.Combine(Path.GetTempPath(), "bn-export-tests-" + Guid.NewGuid().ToString("N"));

    public GuideExportTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(0x2E, 0x6F, 0xB7));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    static ExportGuide SampleGuide() => new(
        "Tom & <Jerry> \"Quote\"",
        "Intro line one.\nIntro line two.",
        [
            new ExportStep("First & last", "Open the <menu>.\nThen pick an item.", MakePng(800, 450), 800, 450),
            new ExportStep("Small", "Short text.", MakePng(320, 200), 320, 200),
            new ExportStep("No picture", "Only text here.", null, 0, 0),
        ]);

    static ExportGuide LongGuide()
    {
        var steps = Enumerable.Range(1, 30).Select(i => new ExportStep(
            $"Step {i}",
            string.Join("\n", Enumerable.Repeat("A paragraph of plain text that is long enough to wrap onto more than one line of the page when it is set at eleven points.", 3)),
            MakePng(800, 450),
            800,
            450));
        return new ExportGuide("Long guide", "Intro", steps.ToList());
    }

    static int CountPages(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;

    // ---- PDF ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Pdf_starts_with_the_pdf_header_and_has_pages()
    {
        using var stream = new MemoryStream();

        GuidePdf.Write(SampleGuide(), stream);

        var bytes = stream.ToArray();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(CountPages(bytes) > 0);
    }

    [Fact]
    public void A_long_guide_makes_several_pages()
    {
        using var stream = new MemoryStream();

        GuidePdf.Write(LongGuide(), stream);

        Assert.True(CountPages(stream.ToArray()) >= 3);
    }

    [Fact]
    public void Polish_title_and_text_export_without_error()
    {
        var guide = new ExportGuide(
            "Zażółć gęślą jaźń: Przewodnik ąęśżźćńół",
            "Wstęp: łódź, ćma, źdźbło.",
            [new ExportStep("Krok pierwszy", "Zażółć gęślą jaźń.\nŁÓDŹ ĄĘŚŻŹĆŃÓŁ.", MakePng(400, 300), 400, 300)]);
        using var stream = new MemoryStream();

        GuidePdf.Write(guide, stream);

        Assert.True(CountPages(stream.ToArray()) >= 1);
    }

    [Fact]
    public void Empty_guide_still_makes_one_page()
    {
        using var stream = new MemoryStream();

        GuidePdf.Write(new ExportGuide("", "", []), stream);

        Assert.Equal(1, CountPages(stream.ToArray()));
    }

    // ---- Word ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Docx_has_every_part_and_each_xml_part_parses()
    {
        var zip = WriteDocx(SampleGuide());

        var names = zip.Entries.Select(e => e.FullName).ToList();
        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "word/_rels/document.xml.rels", "word/styles.xml", "word/document.xml", "word/media/image1.png", "word/media/image2.png" })
            Assert.Contains(part, names);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
            XDocument.Load(entry.Open());
    }

    [Fact]
    public void Docx_relationships_point_to_existing_parts()
    {
        var zip = WriteDocx(SampleGuide());
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();

        var relationships = XDocument.Load(zip.GetEntry("word/_rels/document.xml.rels")!.Open())
            .Descendants(Rels + "Relationship").ToList();

        Assert.Contains(relationships, r => (string?)r.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
        foreach (var image in relationships.Where(r => ((string?)r.Attribute("Type"))!.EndsWith("/image", StringComparison.Ordinal)))
            Assert.Contains("word/" + (string)image.Attribute("Target")!, names);
    }

    [Fact]
    public void Docx_pictures_use_the_size_rule_in_emu()
    {
        var zip = WriteDocx(SampleGuide());

        var extents = Document(zip).Descendants(WP + "extent")
            .Select(e => ((long)e.Attribute("cx")!, (long)e.Attribute("cy")!))
            .ToList();

        // 800 px is wider than 6 inches at 96 dpi, so it is 6 inches wide; 320 px is 320 / 96 inch.
        Assert.Equal(new (long, long)[] { (5486400, 3086100), (3048000, 1905000) }, extents);
    }

    [Fact]
    public void Docx_text_is_escaped_and_reads_back_unchanged()
    {
        var zip = WriteDocx(SampleGuide());
        var raw = ReadEntry(zip, "word/document.xml");

        var texts = Document(zip).Descendants(W + "t").Select(t => t.Value).ToList();

        Assert.Contains("Tom & <Jerry> \"Quote\"", texts);
        Assert.Contains("1. First & last", texts);
        Assert.Contains("Open the <menu>.", texts);
        Assert.Contains("&amp;", raw);
        Assert.DoesNotContain("<menu>", raw);
    }

    [Fact]
    public void Docx_uses_the_title_heading_and_normal_styles()
    {
        var doc = Document(WriteDocx(SampleGuide()));

        var styles = doc.Descendants(W + "pStyle").Select(s => (string)s.Attribute(W + "val")!).ToList();
        Assert.Equal(new[] { "Title", "Heading1", "Heading1", "Heading1" }, styles);
        Assert.Equal(2, doc.Descendants(WP + "inline").Count());
    }

    [Fact]
    public void Docx_of_an_empty_guide_is_still_a_valid_package()
    {
        var zip = WriteDocx(new ExportGuide("", "", []));

        Assert.Null(zip.GetEntry("word/media/image1.png"));
        XDocument.Load(zip.GetEntry("word/document.xml")!.Open());
    }

    static ZipArchive WriteDocx(ExportGuide guide)
    {
        var stream = new MemoryStream();
        GuideDocx.Write(guide, stream);
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    static XDocument Document(ZipArchive zip) => XDocument.Load(zip.GetEntry("word/document.xml")!.Open());

    static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ---- Web page and the shared builder -------------------------------------------------------------------------

    [Fact]
    public void Html_export_has_the_steps_in_order_with_their_pictures()
    {
        var html = GuideExport.Document(SampleGuide());

        int first = html.IndexOf("<h2>1. First &amp; last</h2>", StringComparison.Ordinal);
        int second = html.IndexOf("<h2>2. Small</h2>", StringComparison.Ordinal);
        int third = html.IndexOf("<h2>3. No picture</h2>", StringComparison.Ordinal);
        Assert.True(first > 0 && second > first && third > second);
        Assert.Equal(2, Regex.Matches(html, "data:image/png;base64,").Count);
        Assert.Contains("width=\"640\"", html);
        Assert.Contains("width=\"320\"", html);
    }

    [Fact]
    public void Prepare_flattens_the_picture_so_the_crop_is_in_the_export()
    {
        var store = new GuideStore(Path.Combine(folder, "store"));
        var guide = GuideExport.Prepare(store.Load(GuideMarkupDemo.Create(store)));

        var step = Assert.Single(guide.Steps);
        Assert.NotNull(step.Png);
        Assert.Equal((720, 406), (step.Width, step.Height));
    }

    // ---- Snippet ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Snippet_takes_the_fragment_as_html_and_the_plain_text()
    {
        var snippet = GuideExport.Snippet(SampleGuide(), "  ;demo  ");

        Assert.Equal(";demo", snippet.Trigger);
        Assert.Equal(GuideExport.Fragment(SampleGuide()), snippet.Html);
        Assert.Equal(GuideHtml.PlainText(SampleGuide().Title, SampleGuide().Intro,
            GuideExport.HtmlSteps(SampleGuide())), snippet.Text);
    }

    [Fact]
    public void Snippet_lands_in_the_library_file()
    {
        var path = Path.Combine(folder, "library.json");
        var library = LinuxLibrary.Open(path);
        library.Data.Snippets.Add(GuideExport.Snippet(SampleGuide(), ";demo"));

        library.Save();

        var reopened = LinuxLibrary.Open(path);
        var snippet = Assert.Single(reopened.Snippets);
        Assert.Equal(";demo", snippet.Trigger);
        Assert.Contains("<h1>", snippet.Html);
        Assert.Contains("1. First & last", snippet.Text);
    }

    [Theory]
    [InlineData("", TriggerError.Empty)]
    [InlineData("   ", TriggerError.Empty)]
    [InlineData(";my trigger", TriggerError.HasSpace)]
    [InlineData(";OLD", TriggerError.Duplicate)]
    public void Trigger_is_checked_like_the_editor(string trigger, TriggerError expected)
    {
        var existing = new[] { ";old" };

        Assert.Equal((TriggerError?)expected, TriggerRules.Validate(trigger, existing));
    }

    [Fact]
    public void A_free_trigger_is_accepted_and_the_snippet_may_keep_its_own_trigger()
    {
        Assert.Null(TriggerRules.Validate(";fresh", new[] { ";old" }));
        Assert.Null(TriggerRules.Validate(";OLD", new[] { ";old" }, current: ";old"));
    }

    [Fact]
    public void Suggested_trigger_is_a_semicolon_and_a_slug()
    {
        Assert.Equal(";guide-1", GuideExport.SuggestedTrigger("Guide 1!"));
        Assert.Equal(";guide", GuideExport.SuggestedTrigger("   "));
        Assert.Equal(";za-guide", GuideExport.SuggestedTrigger("Zażółć guide"));
        Assert.DoesNotContain(" ", GuideExport.SuggestedTrigger("Two  words here"));
    }

    [Fact]
    public void File_names_come_from_the_title_and_are_safe()
    {
        Assert.Equal("a b c.pdf", GuideExport.FileName("a/b:c*?", "pdf"));
        Assert.Equal("Guide.docx", GuideExport.FileName("  ", "docx"));
        Assert.Equal("Guide.html", GuideExport.FileName("...", "html"));
        Assert.True(GuideExport.FileName(new string('x', 300), "pdf").Length <= 84);
    }
}
