using System.Net;
using System.Text;
namespace BananaNana;

// One step as the HTML builder needs it: the flattened picture as PNG bytes (null when the step has none) and its
// width in pixels, so the width attribute can be capped.
public sealed record GuideHtmlStep(string Title, string Text, byte[]? Png, int ImageWidth);

// The guide as HTML: used by Copy guide (the fragment), Save as web page (the full document) and Save as snippet
// (the fragment as Html, and PlainText as Text). Pictures come in as bytes, so this file has no drawing code.
public static class GuideHtml
{
    // Pictures are shown at most this wide (CSS pixels), whatever their size.
    public const int MaxImageWidth = 640;

    // The title as h1, the intro, then each step as "<number>. <title>" (h2), its text paragraphs and its picture.
    public static string Fragment(string title, string intro, IReadOnlyList<GuideHtmlStep> steps)
    {
        var sb = new StringBuilder();
        if (title.Trim().Length > 0) sb.Append("<h1>").Append(Enc(title.Trim())).Append("</h1>\n");
        AppendParagraphs(sb, intro);
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            sb.Append("<h2>").Append(i + 1).Append(". ").Append(Enc(step.Title.Trim())).Append("</h2>\n");
            AppendParagraphs(sb, step.Text);
            if (step.Png is { Length: > 0 })
            {
                int width = Math.Min(MaxImageWidth, Math.Max(1, step.ImageWidth));
                sb.Append("<p><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(step.Png))
                  .Append("\" width=\"").Append(width).Append("\" alt=\"").Append(Enc(step.Title)).Append("\"></p>\n");
            }
        }
        return sb.ToString();
    }

    // A complete HTML page around the fragment, with a little inline CSS so it reads well in a browser.
    public static string Document(string title, string intro, IReadOnlyList<GuideHtmlStep> steps)
    {
        const string css = "body{font-family:\"Noto Sans\",Arial,sans-serif;max-width:760px;margin:32px auto;padding:0 16px;color:#222;line-height:1.5}"
            + "h1{font-size:28px;margin:0 0 12px}h2{font-size:20px;margin:28px 0 8px;color:#5b2c6f}"
            + "p{margin:0 0 10px}img{display:block;max-width:100%;height:auto;margin:10px 0;border:1px solid #ddd;border-radius:6px}";
        return "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            + "<title>" + Enc(title.Trim().Length == 0 ? "Guide" : title.Trim()) + "</title>\n<style>" + css + "</style>\n</head>\n<body>\n"
            + Fragment(title, intro, steps)
            + "</body>\n</html>\n";
    }

    // Plain text for the snippet's Text: the title, the intro, then the numbered steps with their text.
    public static string PlainText(string title, string intro, IReadOnlyList<GuideHtmlStep> steps)
    {
        var lines = new List<string>();
        if (title.Trim().Length > 0) lines.Add(title.Trim());
        if (intro.Trim().Length > 0) lines.Add(intro.Trim());
        for (int i = 0; i < steps.Count; i++)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add($"{i + 1}. {steps[i].Title.Trim()}");
            lines.AddRange(Paragraphs(steps[i].Text));
        }
        return string.Join("\n", lines);
    }

    // Lines of text, one paragraph each (a line break starts a new paragraph); empty lines are dropped.
    public static IReadOnlyList<string> Paragraphs(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();

    static void AppendParagraphs(StringBuilder sb, string? text)
    {
        foreach (var line in Paragraphs(text)) sb.Append("<p>").Append(Enc(line)).Append("</p>\n");
    }

    static string Enc(string text) => WebUtility.HtmlEncode(text);
}
