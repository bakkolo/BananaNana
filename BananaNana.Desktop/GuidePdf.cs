using SkiaSharp;
namespace BananaNana.Desktop;

// The guide as an A4 portrait PDF, drawn with SkiaSharp. Layout runs first and produces pages of draw actions, so the
// footer can say "Page n of m" from the start. Each step is a lilac number circle with its title, the wrapped text,
// then the picture. A step's heading never ends a page alone: the step moves to the next page with its picture.
public static class GuidePdf
{
    public const float PageWidth = 595f, PageHeight = 842f; // A4 in points
    public const float Margin = 40f;

    // Writes the PDF to output (the caller owns and closes the stream).
    public static void Write(ExportGuide guide, Stream output)
    {
        using var faces = new Faces();
        var pages = new Layout(guide, faces).Build();
        using var wstream = new SKManagedWStream(output);
        using var document = SKDocument.CreatePdf(wstream);
        for (int i = 0; i < pages.Count; i++)
        {
            var canvas = document.BeginPage(PageWidth, PageHeight);
            foreach (var draw in pages[i]) draw(canvas);
            faces.DrawFooter(canvas, i + 1, pages.Count);
            document.EndPage();
        }
        document.Close();
    }

    // The advance of text in the font. (SKFont measures glyph ids only; a paint with the same typeface and size measures text.)
    static float Measure(SKFont font, string text)
    {
        using var paint = new SKPaint { Typeface = font.Typeface, TextSize = font.Size, IsAntialias = true };
        return paint.MeasureText(text);
    }

    // The typefaces, fonts and paints. Noto Sans is used when it is installed and has the Polish letters; else the default.
    sealed class Faces : IDisposable
    {
        const string Probe = "ąęśżźćńół ĄĘŚŻŹĆŃÓŁ";
        readonly SKTypeface regular = Face(SKFontStyleWeight.Normal), bold = Face(SKFontStyleWeight.Bold);
        public readonly SKFont Title, Heading, Body, Number, Footer;
        public readonly SKPaint Ink, Grey, Lilac, Border;

        public Faces()
        {
            Title = new SKFont(bold, 24);
            Heading = new SKFont(bold, 15);
            Body = new SKFont(regular, 11);
            Number = new SKFont(bold, 13);
            Footer = new SKFont(regular, 9);
            Ink = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = true };
            Grey = new SKPaint { Color = new SKColor(0x66, 0x66, 0x66), IsAntialias = true };
            Lilac = new SKPaint { Color = new SKColor(0xDD, 0xA0, 0xDD), IsAntialias = true, Style = SKPaintStyle.Fill };
            Border = new SKPaint { Color = new SKColor(0xBB, 0xBB, 0xBB), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };
        }

        public void DrawFooter(SKCanvas canvas, int number, int total)
        {
            string text = $"Page {number} of {total}";
            canvas.DrawText(text, (PageWidth - Measure(Footer, text)) / 2, PageHeight - 22, Footer, Grey);
        }

        public void Dispose()
        {
            Title.Dispose(); Heading.Dispose(); Body.Dispose(); Number.Dispose(); Footer.Dispose();
            Ink.Dispose(); Grey.Dispose(); Lilac.Dispose(); Border.Dispose();
            regular.Dispose(); bold.Dispose();
        }

        static SKTypeface Face(SKFontStyleWeight weight)
        {
            var named = SKTypeface.FromFamilyName("Noto Sans", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            if (named is not null && named.ContainsGlyphs(Probe)) return named;
            return SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        }
    }

    sealed class Layout
    {
        const float CircleSize = 26f, PictureGap = 10f, MinPictureHeight = 120f, LineSpacing = 1.4f;
        const float Top = Margin, Bottom = PageHeight - Margin - 30f;
        const float FullHeight = Bottom - Top, TextWidth = PageWidth - 2 * Margin;

        readonly ExportGuide guide;
        readonly Faces faces;
        readonly List<List<Action<SKCanvas>>> pages = [];
        List<Action<SKCanvas>> page = [];
        float y;

        public Layout(ExportGuide guide, Faces faces)
        {
            this.guide = guide;
            this.faces = faces;
        }

        public List<List<Action<SKCanvas>>> Build()
        {
            NewPage();
            if (guide.Title.Trim().Length > 0)
            {
                Lines(Wrap(guide.Title.Trim(), faces.Title, TextWidth), faces.Title, faces.Ink, Margin);
                y += 10;
            }
            if (guide.Intro.Trim().Length > 0)
            {
                Lines(Wrap(guide.Intro.Trim(), faces.Body, TextWidth), faces.Body, faces.Ink, Margin);
                y += 16;
            }
            for (int i = 0; i < guide.Steps.Count; i++) AddStep(i, guide.Steps[i]);
            return pages;
        }

        void AddStep(int index, ExportStep step)
        {
            float textX = Margin + CircleSize + 12;
            float headingLineHeight = faces.Heading.Size * LineSpacing;
            var headingLines = Wrap(step.Title.Trim().Length == 0 ? " " : step.Title.Trim(), faces.Heading, PageWidth - Margin - textX);
            float headingHeight = Math.Max(CircleSize, headingLines.Count * headingLineHeight);
            var bodyLines = GuideHtml.Paragraphs(step.Text).SelectMany(p => Wrap(p, faces.Body, TextWidth)).ToList();
            float bodyHeight = bodyLines.Count * faces.Body.Size * LineSpacing;
            float textBlock = headingHeight + 6 + bodyHeight + (bodyLines.Count > 0 ? 10 : 0);

            if (step.Png is null)
            {
                PlaceHeading(index, headingLines, headingLineHeight, bodyLines.FirstOrDefault());
                Lines(bodyLines, faces.Body, faces.Ink, Margin);
                y += 14;
                return;
            }

            // A picture keeps the full text width; its height follows the aspect ratio.
            float pictureHeight = TextWidth * step.Height / step.Width;
            float needed = textBlock + pictureHeight + PictureGap;
            if (Bottom - y >= needed)
            {
                PlaceHeading(index, headingLines, headingLineHeight, bodyLines.FirstOrDefault());
                Lines(bodyLines, faces.Body, faces.Ink, Margin);
                y += bodyLines.Count > 0 ? 10 : 0;
                PlacePicture(step, pictureHeight);
            }
            else if (needed <= FullHeight)
            {
                NewPage();
                AddStep(index, step); // fits on a fresh page: it is laid out again there
            }
            else
            {
                // Taller than a page: the heading and text start a new page and the picture takes what is left
                // (a page of its own when too little is left), scaled down to fit.
                NewPage();
                PlaceHeading(index, headingLines, headingLineHeight, bodyLines.FirstOrDefault());
                Lines(bodyLines, faces.Body, faces.Ink, Margin);
                y += bodyLines.Count > 0 ? 10 : 0;
                if (Bottom - y < MinPictureHeight) NewPage();
                PlacePicture(step, Math.Min(pictureHeight, Bottom - y));
            }
        }

        // The number circle and the step title, with the first line of the text kept on the same page.
        void PlaceHeading(int index, List<string> headingLines, float headingLineHeight, string? firstBodyLine)
        {
            float firstBody = firstBodyLine is null ? 0 : faces.Body.Size * LineSpacing;
            if (Bottom - y < Math.Max(CircleSize, headingLines.Count * headingLineHeight) + 6 + firstBody) NewPage();
            float top = y, centerX = Margin + CircleSize / 2, centerY = top + CircleSize / 2;
            string number = (index + 1).ToString();
            var lilac = faces.Lilac;
            var ink = faces.Ink;
            var numberFont = faces.Number;
            page.Add(c =>
            {
                c.DrawCircle(centerX, centerY, CircleSize / 2, lilac);
                float width = Measure(numberFont, number);
                var m = numberFont.Metrics;
                c.DrawText(number, centerX - width / 2, centerY - (m.Ascent + m.Descent) / 2, numberFont, ink);
            });
            float textX = Margin + CircleSize + 12;
            // The title starts level with the circle; a longer title runs on below it.
            float lineTop = top + (CircleSize - headingLineHeight) / 2;
            foreach (var line in headingLines)
            {
                Line(line, faces.Heading, faces.Ink, textX, lineTop, headingLineHeight);
                lineTop += headingLineHeight;
            }
            y = top + Math.Max(CircleSize, headingLines.Count * headingLineHeight) + 6;
        }

        void PlacePicture(ExportStep step, float maxHeight)
        {
            float height = Math.Min(TextWidth * step.Height / step.Width, maxHeight);
            float width = height * step.Width / step.Height;
            var rect = SKRect.Create(Margin, y, width, height);
            byte[] png = step.Png!;
            var border = faces.Border;
            page.Add(c =>
            {
                using var image = SKImage.FromEncodedData(png);
                c.DrawImage(image, rect);
                c.DrawRect(rect, border);
            });
            y += height + PictureGap;
        }

        // Writes lines from x, one after the other; a line that does not fit starts a new page. The first line may
        // start at firstTop (the heading uses that to centre its line on the circle).
        void Lines(IReadOnlyList<string> lines, SKFont font, SKPaint paint, float x)
        {
            float height = font.Size * LineSpacing;
            foreach (var line in lines)
            {
                if (Bottom - y < height) NewPage();
                Line(line, font, paint, x, y, height);
                y += height;
            }
        }

        // One line of text whose box starts at top; the glyphs are centred in the box.
        void Line(string text, SKFont font, SKPaint paint, float x, float top, float height)
        {
            var m = font.Metrics;
            float baseline = top + (height - (m.Descent - m.Ascent)) / 2 - m.Ascent;
            page.Add(c => c.DrawText(text, x, baseline, font, paint));
        }

        void NewPage()
        {
            page = [];
            pages.Add(page);
            y = Top;
        }

        // Word wrap with the font's own measurements. A word wider than the line is broken into characters.
        static List<string> Wrap(string text, SKFont font, float width)
        {
            var lines = new List<string>();
            foreach (var paragraph in GuideHtml.Paragraphs(text))
            {
                string line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > 0 && Measure(font, line + " " + word) <= width)
                    {
                        line += " " + word;
                        continue;
                    }
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = "";
                    }
                    foreach (char c in word)
                    {
                        if (line.Length > 0 && Measure(font, line + c) > width)
                        {
                            lines.Add(line);
                            line = "";
                        }
                        line += c;
                    }
                }
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }
    }
}
