using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BananaNana.Paste;

public sealed record RenderedContent(string PlainText, string Rtf, string Html);

// Turns an editor FlowDocument into the clipboard formats other apps read (docs/COMPAT.md):
// plain text, RTF (WPF's own exporter, which keeps images) and an HTML fragment with base64 images.
public static class RichContentRenderer
{
    // Note: images larger than their displayed size are downscaled in the document itself first.
    public static RenderedContent Render(FlowDocument doc)
    {
        foreach (var image in Images(doc)) DownscaleToDisplaySize(image);
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        using var rtf = new MemoryStream();
        range.Save(rtf, DataFormats.Rtf);
        var html = new StringBuilder();
        var ctx = new Context(doc, BodyFontSize(doc));
        foreach (var block in doc.Blocks) AppendBlock(html, block, ctx);
        // WPF writes RTF as ASCII with \uN escapes; Latin1 maps bytes to chars one to one either way.
        return new(range.Text.TrimEnd('\r', '\n'), Encoding.Latin1.GetString(rtf.ToArray()), html.ToString());
    }

    // Loads content stored by RichContent.Save (base64 XamlPackage) into a detached document.
    public static FlowDocument LoadXamlPackage(string base64)
    {
        var doc = new FlowDocument();
        using var stream = new MemoryStream(Convert.FromBase64String(base64));
        new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, DataFormats.XamlPackage);
        return doc;
    }

    // Only deviations from these are written, so pasted text takes the target app's own default font.
    sealed record Context(FlowDocument Doc, double BodyFontSize);

    // The font size covering the most characters counts as body text.
    static double BodyFontSize(FlowDocument doc)
    {
        var sizes = new Dictionary<double, int>();
        for (var p = doc.ContentStart; p != null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text && p.Parent is Run run)
                sizes[run.FontSize] = sizes.GetValueOrDefault(run.FontSize) + run.Text.Length;
        return sizes.Count == 0 ? doc.FontSize : sizes.MaxBy(s => s.Value).Key;
    }

    static void AppendBlock(StringBuilder html, Block block, Context ctx)
    {
        var doc = ctx.Doc;
        switch (block)
        {
            case Paragraph p:
                html.Append("<p").Append(p.TextAlignment == doc.TextAlignment ? "" : AlignStyle(p.TextAlignment)).Append('>');
                AppendInlines(html, p.Inlines, ctx);
                html.Append("</p>");
                break;
            case List list:
                var tag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                    or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? "ol" : "ul";
                html.Append('<').Append(tag).Append('>');
                foreach (var item in list.ListItems)
                {
                    html.Append("<li>");
                    // A single paragraph is unwrapped, otherwise Word adds paragraph spacing to every item.
                    if (item.Blocks.Count == 1 && item.Blocks.FirstBlock is Paragraph only) AppendInlines(html, only.Inlines, ctx);
                    else foreach (var child in item.Blocks) AppendBlock(html, child, ctx);
                    html.Append("</li>");
                }
                html.Append("</").Append(tag).Append('>');
                break;
            case Section section:
                foreach (var child in section.Blocks) AppendBlock(html, child, ctx);
                break;
            case Table table:
                html.Append("<table style=\"border-collapse:collapse\">");
                foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
                {
                    html.Append("<tr>");
                    foreach (var cell in row.Cells)
                    {
                        html.Append("<td style=\"border:1px solid #999;padding:2px 6px\">");
                        foreach (var child in cell.Blocks) AppendBlock(html, child, ctx);
                        html.Append("</td>");
                    }
                    html.Append("</tr>");
                }
                html.Append("</table>");
                break;
            case BlockUIContainer { Child: Image image }:
                html.Append("<p>");
                AppendImage(html, image);
                html.Append("</p>");
                break;
        }
    }

    static void AppendInlines(StringBuilder html, InlineCollection inlines, Context ctx)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run: AppendRun(html, run, ctx); break;
                case LineBreak: html.Append("<br>"); break;
                // Only web and mail links are written; anything else (javascript:, file:, from an imported
                // library) is pasted as its plain text.
                case Hyperlink { NavigateUri: { IsAbsoluteUri: true, Scheme: "http" or "https" or "mailto" } } link:
                    html.Append("<a href=\"").Append(Encode(link.NavigateUri.ToString())).Append("\">");
                    AppendInlines(html, link.Inlines, ctx);
                    html.Append("</a>");
                    break;
                // Bold/Italic/Underline elements are Spans; their styling is picked up per run below.
                case Span span: AppendInlines(html, span.Inlines, ctx); break;
                case InlineUIContainer { Child: Image image }: AppendImage(html, image); break;
            }
        }
    }

    // Styles come from each run's effective (inherited) values, so formatting applied by the editor's
    // toggle commands (local values on runs) and by Bold/Italic elements both render the same way.
    static void AppendRun(StringBuilder html, Run run, Context ctx)
    {
        if (run.Text.Length == 0) return;
        var tags = new List<string>();
        if (run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight()) tags.Add("b");
        if (run.FontStyle != FontStyles.Normal) tags.Add("i");
        if (run.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true) tags.Add("u");
        if (run.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true) tags.Add("s");

        var css = new List<string>();
        if (run.Foreground is SolidColorBrush fg && !(ctx.Doc.Foreground is SolidColorBrush docFg && docFg.Color == fg.Color))
            css.Add($"color:#{fg.Color.R:X2}{fg.Color.G:X2}{fg.Color.B:X2}");
        if (Math.Abs(run.FontSize - ctx.BodyFontSize) > 0.1)
            css.Add(FormattableString.Invariant($"font-size:{run.FontSize:0.#}px"));

        if (css.Count > 0) html.Append("<span style=\"").Append(string.Join(';', css)).Append("\">");
        foreach (var tag in tags) html.Append('<').Append(tag).Append('>');
        html.Append(Encode(run.Text));
        for (int i = tags.Count - 1; i >= 0; i--) html.Append("</").Append(tags[i]).Append('>');
        if (css.Count > 0) html.Append("</span>");
    }

    // Size matches what the editor shows: explicit Width/Height, else pixel size capped by MaxWidth/MaxHeight.
    static Size DisplaySize(Image image, BitmapSource source)
    {
        double w = double.IsNaN(image.Width) ? source.PixelWidth : image.Width;
        double h = double.IsNaN(image.Height) ? source.PixelHeight : image.Height;
        double scale = Math.Min(1, Math.Min(image.MaxWidth / w, image.MaxHeight / h));
        return new Size(Math.Round(w * scale), Math.Round(h * scale));
    }

    // A full-resolution screenshot shown at 500 px wide can be megabytes on the clipboard; Teams then drops
    // the paste. Pixels beyond the displayed size are never visible after pasting, so they are not sent.
    static void DownscaleToDisplaySize(Image image)
    {
        if (image.Source is not BitmapSource source) return;
        var size = DisplaySize(image, source);
        double scale = Math.Min(size.Width / source.PixelWidth, size.Height / source.PixelHeight);
        if (scale >= 1) return;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var png = new MemoryStream();
        encoder.Save(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(png.ToArray());
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        image.Source = bitmap;
        image.Width = size.Width;
        image.Height = size.Height;
    }

    static IEnumerable<Image> Images(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Image image) yield return image;
            foreach (var nested in Images(child)) yield return nested;
        }
    }

    static void AppendImage(StringBuilder html, Image image)
    {
        if (image.Source is not BitmapSource source) return;
        var size = DisplaySize(image, source);
        double w = size.Width, h = size.Height, scale = 1;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var png = new MemoryStream();
        encoder.Save(png);
        html.Append("<img src=\"data:image/png;base64,").Append(Convert.ToBase64String(png.ToArray()))
            .Append(FormattableString.Invariant($"\" width=\"{Math.Round(w * scale)}\" height=\"{Math.Round(h * scale)}\">"));
    }

    // Escapes only HTML specials; the payload is UTF-8, so Polish letters stay readable.
    static string Encode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    static string AlignStyle(TextAlignment align) => align switch
    {
        TextAlignment.Center => " style=\"text-align:center\"",
        TextAlignment.Right => " style=\"text-align:right\"",
        TextAlignment.Justify => " style=\"text-align:justify\"",
        _ => "",
    };
}
