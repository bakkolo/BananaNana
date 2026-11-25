using System.Text.RegularExpressions;
namespace BananaNana.Desktop;

// The library keeps the rich HTML and the plain text side by side. These helpers read the HTML.
public static class SnippetContent
{
    static readonly Regex TagName = new(@"<\s*/?\s*([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled);
    // Tags that plain text can carry anyway: a paragraph or line break is not formatting.
    static readonly HashSet<string> PlainTags = new(StringComparer.OrdinalIgnoreCase) { "html", "head", "body", "meta", "p", "div", "br" };

    static IEnumerable<string> Tags(string? html) =>
        string.IsNullOrEmpty(html) ? [] : TagName.Matches(html).Select(m => m.Groups[1].Value.ToLowerInvariant());

    // Bold, italics, colour, lists, links, tables and so on. Pictures are reported by HasImages.
    public static bool HasFormatting(string? html) => Tags(html).Any(t => t != "img" && !PlainTags.Contains(t));

    public static bool HasImages(string? html) => Tags(html).Contains("img");

    // One line for the list: whitespace collapsed, at most 120 characters.
    public static string Summary(string? text)
    {
        var line = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 120 ? line[..120] + "…" : line;
    }
}
