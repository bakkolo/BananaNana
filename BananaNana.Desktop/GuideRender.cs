using SkiaSharp;
namespace BananaNana.Desktop;

// Draws a step's crop and markup onto its picture with SkiaSharp. The result is what the step cards show and what
// stage 3 exports; the stored picture and the annotations are never changed.
// Order: the blur first (it changes the picture's pixels), then the crop, then the other annotations in their order.
public static class GuideRender
{
    // Blur blocks are this many image pixels square: big enough that text cannot be read through them.
    public const int BlurBlockPx = 12;
    const byte HighlightAlpha = 0x59; // about 35 %
    static readonly SKColor FallbackColor = new(0xE5, 0x48, 0x4D);

    // True when the step has a crop or annotations, so its picture must be drawn; otherwise the stored picture is used as it is.
    public static bool HasMarkup(GuideStep step) => step.Crop is not null || step.Annotations.Count > 0;

    // The step's picture with its crop and annotations, as PNG bytes. Throws InvalidOperationException when the step has no picture.
    public static byte[] Flatten(GuideStep step)
    {
        if (step.Image is null) throw new InvalidOperationException("The step has no picture.");
        using var source = Decode(Convert.FromBase64String(step.Image));
        using var result = Render(source, step.Crop, step.Annotations);
        using var image = SKImage.FromBitmap(result);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    // The picture after blur, crop and the other annotations, as a new bitmap of the crop size. The source is not changed.
    public static SKBitmap Render(SKBitmap source, CropRect? crop, IReadOnlyList<Annotation> annotations)
    {
        var area = MarkupMath.ClampCrop(crop, source.Width, source.Height)
            ?? new CropRect { X = 0, Y = 0, W = source.Width, H = source.Height };
        using var work = source.Copy() ?? throw new InvalidOperationException("The picture could not be copied.");
        foreach (var annotation in annotations)
            if (annotation.Kind == AnnotationKind.Blur) Pixelate(work, annotation);

        var output = new SKBitmap(area.W, area.H);
        using var canvas = new SKCanvas(output);
        canvas.DrawBitmap(work, -area.X, -area.Y);
        canvas.Translate(-area.X, -area.Y); // annotations use picture coordinates
        foreach (var annotation in annotations)
            if (annotation.Kind != AnnotationKind.Blur)
                Draw(canvas, annotation, source.Width, source.Height);
        return output;
    }

    static SKBitmap Decode(byte[] data)
    {
        SKBitmap? bitmap;
        try
        {
            bitmap = SKBitmap.Decode(data);
        }
        catch (ArgumentException)
        {
            bitmap = null;
        }
        return bitmap ?? throw new InvalidDataException("The file is not a picture.");
    }

    // Replaces every BlurBlockPx square of the region by its average colour. The blocks start at the region's corner.
    static void Pixelate(SKBitmap bitmap, Annotation blur)
    {
        var (l, t, r, b) = MarkupMath.Normalize(blur.X1, blur.Y1, blur.X2, blur.Y2);
        var region = SKRectI.Intersect(
            new SKRectI((int)Math.Floor(l), (int)Math.Floor(t), (int)Math.Ceiling(r), (int)Math.Ceiling(b)),
            new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        if (region.IsEmpty) return;

        var pixels = bitmap.Pixels;
        int width = bitmap.Width;
        for (int top = region.Top; top < region.Bottom; top += BlurBlockPx)
            for (int left = region.Left; left < region.Right; left += BlurBlockPx)
            {
                int right = Math.Min(left + BlurBlockPx, region.Right);
                int bottom = Math.Min(top + BlurBlockPx, region.Bottom);
                long red = 0, green = 0, blue = 0, alpha = 0;
                int count = 0;
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                    {
                        var c = pixels[y * width + x];
                        red += c.Red; green += c.Green; blue += c.Blue; alpha += c.Alpha;
                        count++;
                    }
                var average = new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count), (byte)(alpha / count));
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                        pixels[y * width + x] = average;
            }
        bitmap.Pixels = pixels;
    }

    static void Draw(SKCanvas canvas, Annotation a, int imageWidth, int imageHeight)
    {
        var color = SKColor.TryParse(a.Color, out var parsed) ? parsed : FallbackColor;
        float width = (float)MarkupMath.ThicknessPx(a.Thickness, imageWidth, imageHeight);
        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
                DrawArrow(canvas, a, color, width);
                break;
            case AnnotationKind.Box:
                DrawBox(canvas, a, color, width);
                break;
            case AnnotationKind.Highlight:
                DrawHighlight(canvas, a, color);
                break;
            case AnnotationKind.Number:
                DrawNumber(canvas, a, color, MarkupMath.NumberRadius(imageWidth, imageHeight));
                break;
            case AnnotationKind.Text:
                DrawText(canvas, a, color);
                break;
        }
    }

    // A line from the tail to the tip with a filled triangular head. The head is at most as long as the arrow.
    static void DrawArrow(SKCanvas canvas, Annotation a, SKColor color, float width)
    {
        double dx = a.X2 - a.X1, dy = a.Y2 - a.Y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.5) return;
        double ux = dx / length, uy = dy / length;
        double head = Math.Min(length, Math.Max(10, width * 4));
        double baseX = a.X2 - ux * head, baseY = a.Y2 - uy * head;
        double normalX = -uy * head * 0.5, normalY = ux * head * 0.5;

        using var shaft = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine((float)a.X1, (float)a.Y1, (float)baseX, (float)baseY, shaft);

        using var path = new SKPath();
        path.MoveTo((float)a.X2, (float)a.Y2);
        path.LineTo((float)(baseX + normalX), (float)(baseY + normalY));
        path.LineTo((float)(baseX - normalX), (float)(baseY - normalY));
        path.Close();
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, fill);
    }

    static void DrawBox(SKCanvas canvas, Annotation a, SKColor color, float width)
    {
        var (l, t, r, b) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
        float radius = Math.Max(2, width * 1.5f);
        using var stroke = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawRoundRect(new SKRect((float)l, (float)t, (float)r, (float)b), radius, radius, stroke);
    }

    // A translucent fill, so the picture under it still shows (SrcOver blend at about 35 % alpha).
    static void DrawHighlight(SKCanvas canvas, Annotation a, SKColor color)
    {
        var (l, t, r, b) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
        using var fill = new SKPaint { Color = color.WithAlpha(HighlightAlpha), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect((float)l, (float)t, (float)r, (float)b), fill);
    }

    // A filled circle centred on (X1, Y1) with the number in bold white, centred on it.
    static void DrawNumber(SKCanvas canvas, Annotation a, SKColor color, double radius)
    {
        float cx = (float)a.X1, cy = (float)a.Y1;
        using var circle = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(cx, cy, (float)radius, circle);

        using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), (float)(radius * 1.2));
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        string text = a.Number.ToString();
        float textWidth = white.MeasureText(text);
        var metrics = font.Metrics;
        canvas.DrawText(text, cx - textWidth / 2, cy - (metrics.Ascent + metrics.Descent) / 2, font, white);
    }

    // Bold text from (X1, Y1) as the top-left corner, one line per line break. A halo (black on light colours, white on
    // dark ones) keeps the text readable on any background.
    static void DrawText(SKCanvas canvas, Annotation a, SKColor color)
    {
        if (string.IsNullOrEmpty(a.Text)) return;
        float size = (float)Math.Max(6, a.FontSize);
        using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), size);
        var halo = Luminance(color) > 0.5 ? SKColors.Black : SKColors.White;
        using var haloPaint = new SKPaint { Color = halo, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, size / 5), StrokeJoin = SKStrokeJoin.Round };
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

        var lines = a.Text.Replace("\r\n", "\n").Split('\n');
        float baseline = (float)a.Y1 - font.Metrics.Ascent;
        for (int i = 0; i < lines.Length; i++)
            canvas.DrawText(lines[i], (float)a.X1, baseline + i * size * 1.2f, font, haloPaint);
        for (int i = 0; i < lines.Length; i++)
            canvas.DrawText(lines[i], (float)a.X1, baseline + i * size * 1.2f, font, fill);
    }

    static double Luminance(SKColor c) => (c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114) / 255.0;
}
