using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace BananaNana.Desktop;

// A minimal Word 2007+ (.docx) package, written by hand: the content types, the package and document relationships,
// styles (Title, Heading1, Normal), the document and one PNG per picture. Every XML part is built with XLINQ, so text
// is escaped, and the namespaces are the ones Word and LibreOffice expect (w, r, wp, a, pic).
public static class GuideDocx
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    static readonly XNamespace PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const int EmuPerInch = 914400;
    const int MaxWidthEmu = 6 * EmuPerInch;   // 6 inches: the text width of an A4 page with 1-inch margins, rounded down
    const int EmuPerPixel = 9525;             // 96 pixels per inch

    // Writes the .docx to output (the caller owns and closes the stream).
    public static void Write(ExportGuide guide, Stream output)
    {
        var pictures = new List<(string RelId, string Name, byte[] Png, long Cx, long Cy)>();
        var body = BuildBody(guide, pictures);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Part(zip, "[Content_Types].xml", ContentTypesXml(), Xml);
        Part(zip, "_rels/.rels", RootRelsXml(), Xml);
        Part(zip, "word/_rels/document.xml.rels", DocumentRelsXml(pictures), Xml);
        Part(zip, "word/styles.xml", StylesXml(), Xml);
        Part(zip, "word/document.xml", DocumentXml(body), Xml);
        foreach (var picture in pictures) Part(zip, "word/media/" + picture.Name, picture.Png, b => b);
    }

    // The picture's size on the page in EMU: its pixel size at 96 dpi, or 6 inches when it is wider than that,
    // always with the picture's own aspect ratio.
    public static (long Cx, long Cy) PictureExtent(int widthPx, int heightPx)
    {
        long cx = Math.Min(MaxWidthEmu, (long)widthPx * EmuPerPixel);
        long cy = (long)Math.Round(cx * (double)heightPx / widthPx);
        return (cx, cy);
    }

    static List<XElement> BuildBody(ExportGuide guide, List<(string RelId, string Name, byte[] Png, long Cx, long Cy)> pictures)
    {
        var body = new List<XElement>();
        if (guide.Title.Trim().Length > 0) body.Add(Paragraph("Title", guide.Title.Trim()));
        foreach (var line in GuideHtml.Paragraphs(guide.Intro)) body.Add(Paragraph(null, line));
        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var step = guide.Steps[i];
            string heading = $"{i + 1}. {step.Title.Trim()}";
            body.Add(Paragraph("Heading1", heading.TrimEnd()));
            foreach (var line in GuideHtml.Paragraphs(step.Text)) body.Add(Paragraph(null, line));
            if (step.Png is { Length: > 0 } png && step.Width > 0 && step.Height > 0)
            {
                int number = pictures.Count + 1;
                var (cx, cy) = PictureExtent(step.Width, step.Height);
                string relId = "rIdImg" + number;
                string name = "image" + number + ".png";
                pictures.Add((relId, name, png, cx, cy));
                body.Add(PictureParagraph(number, relId, name, cx, cy));
            }
        }
        return body;
    }

    static XElement Paragraph(string? style, string text)
    {
        var paragraph = new XElement(W + "p");
        if (style is not null) paragraph.Add(new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style))));
        if (text.Length > 0) paragraph.Add(new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(text))));
        return paragraph;
    }

    // An inline picture in its own paragraph (wp:inline, the picture namespace inside a:graphicData).
    static XElement PictureParagraph(int number, string relId, string name, long cx, long cy) =>
        new(W + "p",
            new XElement(W + "r",
                new XElement(W + "drawing",
                    new XElement(WP + "inline",
                        new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
                        new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                        new XElement(WP + "effectExtent", new XAttribute("l", 0), new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)),
                        new XElement(WP + "docPr", new XAttribute("id", number), new XAttribute("name", "Picture " + number)),
                        new XElement(WP + "cNvGraphicFramePr",
                            new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
                        new XElement(A + "graphic",
                            new XElement(A + "graphicData",
                                new XAttribute("uri", PIC.NamespaceName),
                                new XElement(PIC + "pic",
                                    new XElement(PIC + "nvPicPr",
                                        new XElement(PIC + "cNvPr", new XAttribute("id", number), new XAttribute("name", name)),
                                        new XElement(PIC + "cNvPicPr")),
                                    new XElement(PIC + "blipFill",
                                        new XElement(A + "blip", new XAttribute(R + "embed", relId)),
                                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                    new XElement(PIC + "spPr",
                                        new XElement(A + "xfrm",
                                            new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                            new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))))));

    static XDocument DocumentXml(List<XElement> body)
    {
        var sectionProperties = new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1440), new XAttribute(W + "right", 1440), new XAttribute(W + "bottom", 1440),
                new XAttribute(W + "left", 1440), new XAttribute(W + "header", 708), new XAttribute(W + "footer", 708), new XAttribute(W + "gutter", 0)));
        var root = new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "wp", WP.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pic", PIC.NamespaceName),
            new XElement(W + "body", body, sectionProperties));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
    }

    static XDocument StylesXml()
    {
        var root = new XElement(W + "styles",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XElement(W + "docDefaults",
                new XElement(W + "rPrDefault", new XElement(W + "rPr",
                    new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"), new XAttribute(W + "cs", "Calibri")),
                    new XElement(W + "sz", new XAttribute(W + "val", 22)),
                    new XElement(W + "lang", new XAttribute(W + "val", "en-GB")))),
                new XElement(W + "pPrDefault", new XElement(W + "pPr",
                    new XElement(W + "spacing", new XAttribute(W + "after", 120), new XAttribute(W + "line", 264), new XAttribute(W + "lineRule", "auto"))))),
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "Normal"),
                new XElement(W + "name", new XAttribute(W + "val", "Normal")),
                new XElement(W + "qFormat")),
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "Title"),
                new XElement(W + "name", new XAttribute(W + "val", "Title")),
                new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
                new XElement(W + "next", new XAttribute(W + "val", "Normal")),
                new XElement(W + "qFormat"),
                new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 240))),
                new XElement(W + "rPr", new XElement(W + "b"), new XElement(W + "sz", new XAttribute(W + "val", 48)))),
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "Heading1"),
                new XElement(W + "name", new XAttribute(W + "val", "heading 1")),
                new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
                new XElement(W + "next", new XAttribute(W + "val", "Normal")),
                new XElement(W + "qFormat"),
                new XElement(W + "pPr",
                    new XElement(W + "keepNext"),
                    new XElement(W + "spacing", new XAttribute(W + "before", 240), new XAttribute(W + "after", 120)),
                    new XElement(W + "outlineLvl", new XAttribute(W + "val", 0))),
                new XElement(W + "rPr",
                    new XElement(W + "b"),
                    new XElement(W + "color", new XAttribute(W + "val", "5B2C6F")),
                    new XElement(W + "sz", new XAttribute(W + "val", 30)))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
    }

    static XDocument ContentTypesXml() => new(new XDeclaration("1.0", "UTF-8", "yes"),
        new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));

    static XDocument RootRelsXml() => new(new XDeclaration("1.0", "UTF-8", "yes"),
        new XElement(PackageRels + "Relationships",
            new XElement(PackageRels + "Relationship", new XAttribute("Id", "rId1"),
                new XAttribute("Type", RelBase + "/officeDocument"), new XAttribute("Target", "word/document.xml"))));

    static XDocument DocumentRelsXml(List<(string RelId, string Name, byte[] Png, long Cx, long Cy)> pictures)
    {
        var relationships = new List<object>
        {
            new XElement(PackageRels + "Relationship", new XAttribute("Id", "rId1"),
                new XAttribute("Type", RelBase + "/styles"), new XAttribute("Target", "styles.xml")),
        };
        relationships.AddRange(pictures.Select(p => new XElement(PackageRels + "Relationship", new XAttribute("Id", p.RelId),
            new XAttribute("Type", RelBase + "/image"), new XAttribute("Target", "media/" + p.Name))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(PackageRels + "Relationships", relationships));
    }

    // XML 1.0 cannot hold control characters; they are dropped so the part stays well formed.
    static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c is '\t' or '\n' or '\r' || (c >= ' ' && c != '￾' && c != '￿')) sb.Append(c);
        return sb.ToString();
    }

    static void Part<T>(ZipArchive zip, string name, T content, Func<T, byte[]> encode)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = encode(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    static byte[] Xml(XDocument document)
    {
        using var memory = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
        using (var writer = XmlWriter.Create(memory, settings)) document.Save(writer);
        return memory.ToArray();
    }
}
