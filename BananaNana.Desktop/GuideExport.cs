using SkiaSharp;
namespace BananaNana.Desktop;

// One step ready for any exporter: the flattened picture (crop, blur and marks included) as PNG, with its pixel size.
public sealed record ExportStep(string Title, string Text, byte[]? Png, int Width, int Height);

public sealed record ExportGuide(string Title, string Intro, IReadOnlyList<ExportStep> Steps);

// The shared part of the export: the guide as the exporters read it, file names and the snippet. The PDF, Word and
// web page writers live in GuidePdf, GuideDocx and GuideHtml; the window and --guide-export-demo call these.
public static class GuideExport
{
    public const string SnippetPrefix = ";";
    const int MaxFileNameLength = 80, MaxSlugLength = 30;

    // Every picture goes through GuideRender.Flatten, so the crop, the blur and the marks are in the export.
    // Throws InvalidDataException when a stored picture cannot be read.
    public static ExportGuide Prepare(GuideDocument doc)
    {
        var steps = new List<ExportStep>();
        foreach (var step in doc.Steps)
        {
            if (step.Image is null)
            {
                steps.Add(new ExportStep(step.Title, step.Text, null, 0, 0));
                continue;
            }
            var png = GuideRender.Flatten(step);
            using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidDataException("The picture could not be read.");
            steps.Add(new ExportStep(step.Title, step.Text, png, bitmap.Width, bitmap.Height));
        }
        return new ExportGuide(doc.Title, doc.Intro, steps);
    }

    public static IReadOnlyList<GuideHtmlStep> HtmlSteps(ExportGuide guide) =>
        guide.Steps.Select(s => new GuideHtmlStep(s.Title, s.Text, s.Png, s.Width)).ToList();

    public static string Fragment(ExportGuide guide) => GuideHtml.Fragment(guide.Title, guide.Intro, HtmlSteps(guide));

    public static string Document(ExportGuide guide) => GuideHtml.Document(guide.Title, guide.Intro, HtmlSteps(guide));

    public static string PlainText(ExportGuide guide) => GuideHtml.PlainText(guide.Title, guide.Intro, HtmlSteps(guide));

    // The snippet the Linux library gets: Html is the fragment, Text the plain version.
    public static HtmlSnippet Snippet(ExportGuide guide, string trigger) =>
        new() { Trigger = TriggerRules.Normalize(trigger), Html = Fragment(guide), Text = PlainText(guide) };

    // "Guide title" -> "Guide title.pdf". Characters that file systems reject are replaced; an empty title is "Guide".
    public static string FileName(string title, string extension)
    {
        var cleaned = new string(title.Select(c => c < ' ' || "\\/:*?\"<>|".Contains(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (cleaned.Length > MaxFileNameLength) cleaned = cleaned[..MaxFileNameLength].TrimEnd('.', ' ');
        return (cleaned.Length == 0 ? "Guide" : cleaned) + "." + extension;
    }

    // The trigger the snippet dialog starts with: ";" and a lower case slug of the title ("Guide 1!" -> ";guide-1").
    public static string SuggestedTrigger(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > MaxSlugLength) slug = slug[..MaxSlugLength].TrimEnd('-');
        return SnippetPrefix + (slug.Length == 0 ? "guide" : slug);
    }
}
