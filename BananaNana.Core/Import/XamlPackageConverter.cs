using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BananaNana.Import;

public sealed record ConvertedContent(string Html, string PlainText);

// Reads a WPF XamlPackage (base64 of an OPC zip whose XAML part is a FlowDocument fragment) and writes the
// same HTML fragment RichContentRenderer writes on Windows, plus plain text. No WPF: elements are matched by
// local name, so namespace prefixes do not matter. Unknown elements are walked through, never rejected.
// Images that cannot be found in the package, or are not PNG/JPEG/GIF, are left out.
public static class XamlPackageConverter
{
    const double DefaultFontSize = 12; // WPF's FlowDocument default

    // Throws InvalidDataException (naming the trigger) when the content is not a readable package.
    public static ConvertedContent Read(string base64, string? trigger = null)
    {
        var label = trigger is null ? "Snippet" : $"Snippet '{trigger}'";
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length == 0) return new ConvertedContent("", "");
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var parts = Parts(zip);
            var xamlPath = FindXamlPart(parts) ?? throw new InvalidDataException("no XAML document part in the package");
            var root = ParseXml(ReadEntry(parts[xamlPath])).Root ?? throw new InvalidDataException("the XAML document part is empty");
            return new Renderer(parts, xamlPath).Render(root);
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"{label} is not a readable XamlPackage: {e.Message}", e);
        }
    }

    // ---- package ----

    static string PartName(string name) => Uri.UnescapeDataString(name.Replace('\\', '/')).TrimStart('/');

    static Dictionary<string, ZipArchiveEntry> Parts(ZipArchive zip)
    {
        var parts = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries) parts.TryAdd(PartName(entry.FullName), entry);
        return parts;
    }

    // The document part is found through the package relationships, then [Content_Types].xml, then the first .xaml part.
    static string? FindXamlPart(Dictionary<string, ZipArchiveEntry> parts)
    {
        if (TryParseEntry(parts, "_rels/.rels") is { } rels)
            foreach (var rel in rels.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                var name = Attr(rel, "Target") is { } target ? PartName(target) : null;
                if (name is not null && name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && parts.ContainsKey(name)) return name;
            }
        if (TryParseEntry(parts, "[Content_Types].xml") is { } types)
            foreach (var over in types.Descendants().Where(e => e.Name.LocalName == "Override"))
            {
                var name = Attr(over, "PartName") is { } partName ? PartName(partName) : null;
                if (name is not null && name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && parts.ContainsKey(name)) return name;
            }
        return parts.Keys.FirstOrDefault(k => k.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
    }

    static XDocument? TryParseEntry(Dictionary<string, ZipArchiveEntry> parts, string name)
    {
        if (!parts.TryGetValue(name, out var entry)) return null;
        try { return ParseXml(ReadEntry(entry)); }
        catch (XmlException) { return null; }
    }

    static XDocument ParseXml(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader);
    }

    static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    static string? Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && a.Name.LocalName == name)?.Value;

    // ---- rendering ----

    sealed record Style(bool Bold, bool Italic, bool Underline, bool Strike, string? Color, double? Size, string Align)
    {
        public static readonly Style Default = new(false, false, false, false, null, null, "left");
    }

    sealed class Renderer
    {
        static readonly HashSet<string> InlineNames = new(StringComparer.Ordinal)
        {
            "Run", "Span", "Bold", "Italic", "Underline", "Hyperlink", "LineBreak", "InlineUIContainer", "Image",
        };
        static readonly HashSet<string> OrderedMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Decimal", "LowerLatin", "UpperLatin", "LowerRoman", "UpperRoman",
        };

        readonly Dictionary<string, ZipArchiveEntry> parts;
        readonly string xamlDir;
        StringBuilder html = new();
        StringBuilder plain = new();
        // Font sizes weighted by characters, to find the body size (the one most text uses), as the Windows renderer does.
        readonly Dictionary<double, int> sizes = new();
        bool measuring;
        double bodySize = DefaultFontSize;

        public Renderer(Dictionary<string, ZipArchiveEntry> parts, string xamlPath)
        {
            this.parts = parts;
            xamlDir = xamlPath.Contains('/') ? xamlPath[..xamlPath.LastIndexOf('/')] : "";
        }

        public ConvertedContent Render(XElement root)
        {
            measuring = true;
            BlockSequence(new[] { root }, Style.Default);
            bodySize = sizes.Count == 0 ? DefaultFontSize : sizes.MaxBy(kv => kv.Value).Key;
            measuring = false;
            html = new StringBuilder();
            plain = new StringBuilder();
            BlockSequence(new[] { root }, Style.Default);
            return new ConvertedContent(html.ToString(), plain.ToString().TrimEnd('\r', '\n'));
        }

        // Consecutive inline elements are wrapped in one paragraph; block elements are rendered as they are.
        void BlockSequence(IEnumerable<XElement> children, Style style)
        {
            var group = new List<XNode>();
            foreach (var element in children)
            {
                var name = element.Name.LocalName;
                if (name.Contains('.')) continue; // property elements such as Section.Blocks
                if (InlineNames.Contains(name))
                {
                    group.Add(element);
                    continue;
                }
                FlushInlines(group, style);
                Block(element, style);
            }
            FlushInlines(group, style);
        }

        void FlushInlines(List<XNode> group, Style style)
        {
            if (group.Count == 0) return;
            html.Append("<p>");
            Inlines(group, style);
            html.Append("</p>");
            plain.Append('\n');
            group.Clear();
        }

        void Block(XElement element, Style style)
        {
            switch (element.Name.LocalName)
            {
                case "Paragraph":
                    Paragraph(element, style);
                    break;
                case "List":
                    ListBlock(element, style);
                    break;
                case "Table":
                    TableBlock(element, style);
                    break;
                case "BlockUIContainer":
                    html.Append("<p>");
                    Inlines(element.Nodes(), Apply(element, style));
                    html.Append("</p>");
                    break;
                default: // Section, FlowDocument and unknown containers
                    BlockSequence(element.Elements(), Apply(element, style));
                    break;
            }
        }

        void Paragraph(XElement element, Style style)
        {
            var ps = Apply(element, style);
            html.Append("<p");
            if (ps.Align != "left") html.Append(" style=\"text-align:").Append(ps.Align).Append('"');
            html.Append('>');
            Inlines(element.Nodes(), ps);
            html.Append("</p>");
            plain.Append('\n');
        }

        void ListBlock(XElement element, Style style)
        {
            var ls = Apply(element, style);
            var marker = (Attr(element, "MarkerStyle") ?? "Disc").Trim();
            var tag = OrderedMarkers.Contains(marker) ? "ol" : "ul";
            html.Append('<').Append(tag).Append('>');
            foreach (var item in element.Elements().Where(e => e.Name.LocalName == "ListItem"))
            {
                html.Append("<li>");
                var itemStyle = Apply(item, ls);
                var children = item.Elements().Where(c => !c.Name.LocalName.Contains('.')).ToList();
                // A single paragraph is unwrapped, otherwise Word adds paragraph spacing to every item.
                if (children.Count == 1 && children[0].Name.LocalName == "Paragraph")
                {
                    Inlines(children[0].Nodes(), Apply(children[0], itemStyle));
                    plain.Append('\n');
                }
                else BlockSequence(children, itemStyle);
                html.Append("</li>");
            }
            html.Append("</").Append(tag).Append('>');
        }

        void TableBlock(XElement element, Style style)
        {
            var ts = Apply(element, style);
            html.Append("<table style=\"border-collapse:collapse\">");
            foreach (var row in TableRows(element))
            {
                html.Append("<tr>");
                var cells = row.Elements().Where(c => c.Name.LocalName == "TableCell").ToList();
                for (var i = 0; i < cells.Count; i++)
                {
                    html.Append("<td style=\"border:1px solid #999;padding:2px 6px\">");
                    var start = plain.Length;
                    BlockSequence(cells[i].Elements(), Apply(cells[i], ts));
                    html.Append("</td>");
                    // Cell text is tab separated and each row ends with a newline; paragraph breaks inside a cell are dropped at its end.
                    while (plain.Length > start && plain[^1] == '\n') plain.Length--;
                    plain.Append(i < cells.Count - 1 ? '\t' : '\n');
                }
                html.Append("</tr>");
            }
            html.Append("</table>");
        }

        static IEnumerable<XElement> TableRows(XElement table)
        {
            foreach (var child in table.Elements())
            {
                if (child.Name.LocalName == "TableRowGroup")
                {
                    foreach (var row in child.Elements().Where(r => r.Name.LocalName == "TableRow")) yield return row;
                }
                else if (child.Name.LocalName == "TableRow") yield return child;
            }
        }

        void Inlines(IEnumerable<XNode> nodes, Style style)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    // Spaces between runs are content. Whitespace-only text with a line break is XML indentation.
                    case XText text when text.Value.Trim().Length > 0 || !text.Value.Contains('\n'):
                        Text(text.Value, style);
                        break;
                    case XElement element:
                        Inline(element, style);
                        break;
                }
            }
        }

        void Inline(XElement element, Style style)
        {
            var name = element.Name.LocalName;
            if (name.Contains('.')) return;
            switch (name)
            {
                case "Run":
                    Text(RunText(element), Apply(element, style));
                    break;
                case "LineBreak":
                    html.Append("<br>");
                    plain.Append('\n');
                    break;
                case "Hyperlink":
                {
                    var linkStyle = Apply(element, style);
                    if (SafeLink(Attr(element, "NavigateUri")) is { } href)
                    {
                        html.Append("<a href=\"").Append(Encode(href)).Append("\">");
                        Inlines(element.Nodes(), linkStyle);
                        html.Append("</a>");
                    }
                    else Inlines(element.Nodes(), linkStyle); // other schemes are pasted as their text
                    break;
                }
                case "Image":
                    Image(element);
                    break;
                default: // Span, Bold, Italic, Underline, InlineUIContainer and unknown elements
                    Inlines(element.Nodes(), Apply(element, style));
                    break;
            }
        }

        static string RunText(XElement run)
        {
            var text = string.Concat(run.Nodes().OfType<XText>().Select(t => t.Value));
            return text.Length > 0 ? text : Attr(run, "Text") ?? "";
        }

        void Text(string text, Style style)
        {
            if (text.Length == 0) return;
            var size = style.Size ?? DefaultFontSize;
            if (measuring) sizes[size] = sizes.GetValueOrDefault(size) + text.Length;

            var css = new List<string>();
            if (style.Color is { } color && color != "000000") css.Add("color:#" + color);
            if (Math.Abs(size - bodySize) > 0.1) css.Add("font-size:" + size.ToString("0.#", CultureInfo.InvariantCulture) + "px");

            if (css.Count > 0) html.Append("<span style=\"").Append(string.Join(';', css)).Append("\">");
            var tags = new List<string>(4);
            if (style.Bold) tags.Add("b");
            if (style.Italic) tags.Add("i");
            if (style.Underline) tags.Add("u");
            if (style.Strike) tags.Add("s");
            foreach (var tag in tags) html.Append('<').Append(tag).Append('>');
            html.Append(Encode(text));
            for (var i = tags.Count - 1; i >= 0; i--) html.Append("</").Append(tags[i]).Append('>');
            if (css.Count > 0) html.Append("</span>");
            plain.Append(text);
        }

        void Image(XElement element)
        {
            var bytes = ImageBytes(element);
            if (bytes is null || ImageMime(bytes) is not { } mime) return;
            html.Append("<img src=\"data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(bytes)).Append('"');
            if (Dimension(element, "Width") is { } width) html.Append(" width=\"").Append(Math.Round(width).ToString(CultureInfo.InvariantCulture)).Append('"');
            if (Dimension(element, "Height") is { } height) html.Append(" height=\"").Append(Math.Round(height).ToString(CultureInfo.InvariantCulture)).Append('"');
            html.Append('>');
        }

        // Image source: Source="..." on the element, or <Image.Source><BitmapImage UriSource="..."/></Image.Source>.
        byte[]? ImageBytes(XElement element)
        {
            var source = Attr(element, "Source") ?? element.Elements()
                .Where(c => c.Name.LocalName == "Image.Source")
                .SelectMany(c => c.DescendantsAndSelf())
                .Select(d => Attr(d, "UriSource") ?? Attr(d, "Source"))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (string.IsNullOrWhiteSpace(source)) return null;
            var entry = ResolveImage(source);
            return entry is null ? null : ReadEntry(entry);
        }

        // Tries the path relative to the XAML part, then relative to the package root, then any part with the same file name.
        ZipArchiveEntry? ResolveImage(string source)
        {
            var text = source.Trim();
            var cut = text.IndexOfAny(['?', '#']);
            if (cut >= 0) text = text[..cut];
            if (!text.Contains("://"))
            {
                var rel = Uri.UnescapeDataString(text.Replace('\\', '/'));
                foreach (var candidate in new[] { Join(xamlDir, rel), Join("", rel) })
                    if (parts.TryGetValue(candidate, out var entry)) return entry;
            }
            var fileName = text[(text.LastIndexOf('/') + 1)..];
            return parts.Values.FirstOrDefault(e => e.FullName.Replace('\\', '/').Split('/').Last()
                .Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        static string Join(string baseDir, string rel)
        {
            var segments = new List<string>();
            if (!rel.StartsWith('/')) segments.AddRange(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));
            foreach (var part in rel.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(part);
            }
            return string.Join('/', segments);
        }

        static string? ImageMime(byte[] b)
        {
            if (b.Length >= 4 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return "image/png";
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b.Length >= 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "image/gif";
            return null;
        }

        static double? Dimension(XElement element, string name)
        {
            var text = Attr(element, name);
            return text is not null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value > 0 && !double.IsInfinity(value) ? value : null;
        }

        // Applies the element's own formatting (attributes, property elements, Bold/Italic/Underline wrappers) on top of the inherited style.
        static Style Apply(XElement element, Style style)
        {
            switch (element.Name.LocalName)
            {
                case "Bold": style = style with { Bold = true }; break;
                case "Italic": style = style with { Italic = true }; break;
                case "Underline": style = style with { Underline = true }; break;
            }
            foreach (var attr in element.Attributes())
                if (!attr.IsNamespaceDeclaration) style = Property(style, attr.Name.LocalName, attr.Value);
            foreach (var child in element.Elements())
            {
                var name = child.Name.LocalName;
                var dot = name.LastIndexOf('.');
                if (dot >= 0) style = Property(style, name[(dot + 1)..], PropertyValue(child, name[(dot + 1)..]));
            }
            return style;
        }

        static string PropertyValue(XElement property, string name)
        {
            var nodes = property.DescendantsAndSelf().ToList();
            if (name == "Foreground" && nodes.Select(n => Attr(n, "Color")).OfType<string>().FirstOrDefault() is { } color) return color;
            if (name == "TextDecorations") return string.Join(',', nodes.Select(n => Attr(n, "Location")).OfType<string>());
            return string.Concat(property.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        }

        static Style Property(Style style, string name, string value)
        {
            switch (name)
            {
                case "FontWeight":
                    if (Weight(value) is { } weight) style = style with { Bold = weight >= 600 };
                    break;
                case "FontStyle":
                {
                    var fontStyle = value.Trim();
                    if (fontStyle.Equals("Italic", StringComparison.OrdinalIgnoreCase) || fontStyle.Equals("Oblique", StringComparison.OrdinalIgnoreCase)) style = style with { Italic = true };
                    else if (fontStyle.Equals("Normal", StringComparison.OrdinalIgnoreCase)) style = style with { Italic = false };
                    break;
                }
                case "FontSize":
                    if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0)
                        style = style with { Size = size };
                    break;
                case "Foreground":
                    if (ParseColor(value) is { } hex) style = style with { Color = hex };
                    break;
                case "TextDecorations":
                    if (value.Contains("Underline", StringComparison.OrdinalIgnoreCase)) style = style with { Underline = true };
                    if (value.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase)) style = style with { Strike = true };
                    break;
                case "TextAlignment":
                {
                    var align = value.Trim().ToLowerInvariant();
                    if (align is "left" or "center" or "right" or "justify") style = style with { Align = align };
                    break;
                }
            }
            return style;
        }

        static int? Weight(string value)
        {
            var text = value.Trim();
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return number;
            return text.ToLowerInvariant() switch
            {
                "thin" => 100,
                "extralight" or "ultralight" => 200,
                "light" => 300,
                "normal" or "regular" => 400,
                "medium" => 500,
                "semibold" or "demibold" => 600,
                "bold" => 700,
                "extrabold" or "ultrabold" => 800,
                "black" or "heavy" => 900,
                "extrablack" or "ultrablack" => 950,
                _ => null,
            };
        }

        // Returns RRGGBB (upper case) like the Windows renderer; alpha is ignored there too.
        static string? ParseColor(string value)
        {
            var text = value.Trim();
            if (text.Length == 0) return null;
            if (text.StartsWith('#'))
            {
                var hex = text[1..];
                if (hex.Length == 4) hex = hex[1..];                                  // #ARGB
                if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}")); // #RGB
                if (hex.Length == 8) hex = hex[2..];                                  // #AARRGGBB
                return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : null;
            }
            if (text.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) return null;
            var known = System.Drawing.Color.FromName(text);
            if (!known.IsKnownColor)
                known = System.Drawing.Color.FromName(char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant());
            return known.IsKnownColor ? $"{known.R:X2}{known.G:X2}{known.B:X2}" : null;
        }

        static string? SafeLink(string? navigateUri) =>
            navigateUri is not null && Uri.TryCreate(navigateUri.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" or "mailto"
                ? uri.ToString()
                : null;

        // Same escaping as RichContentRenderer: HTML specials only, UTF-8 text stays readable.
        static string Encode(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
