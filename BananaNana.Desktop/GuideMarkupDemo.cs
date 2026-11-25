using SkiaSharp;
namespace BananaNana.Desktop;

// The guide behind --guide-markup-demo and --guide-markup-demo-edit, for checking the markup tools by hand. Every
// request makes it again: an old "Markup demo" guide is deleted first. The step has the generated 800 x 450 sample
// picture (a blue field with a white box), with dark bars inside the box so that the blur has something to hide.
// The markup is one annotation of each kind (two numbers) plus a crop that trims 5 % from each edge.
public static class GuideMarkupDemo
{
    public const string Title = "Markup demo";
    const int Width = 800, Height = 450;

    // Returns the id of the new demo guide.
    public static Guid Create(GuideStore store)
    {
        foreach (var old in store.List().Where(g => g.Title == Title)) store.Delete(old.Id);
        var doc = store.Create(Title);
        doc.Intro = "Made by --guide-markup-demo to check the markup tools.";
        doc.Steps.Add(new GuideStep
        {
            Id = Guid.NewGuid(),
            Title = "Mark up the picture",
            Text = "An arrow, a box, a highlight, two numbers, a text and a blur, with a crop of 5 % on each edge.",
            Image = Picture(),
            ImageType = "png",
            Annotations = Markup(),
            Crop = new CropRect { X = Width * 5 / 100, Y = Height * 5 / 100, W = Width - 2 * (Width * 5 / 100), H = Height - 2 * (Height * 5 / 100) },
        });
        store.Save(doc);
        return doc.Id;
    }

    static List<Annotation> Markup() =>
    [
        new() { Kind = AnnotationKind.Arrow, X1 = 120, Y1 = 90, X2 = 260, Y2 = 150, Color = "#E5484D" },
        new() { Kind = AnnotationKind.Box, X1 = 60, Y1 = 300, X2 = 190, Y2 = 420, Color = "#3B82F6", Thickness = AnnotationThickness.L },
        new() { Kind = AnnotationKind.Highlight, X1 = 230, Y1 = 290, X2 = 420, Y2 = 320, Color = "#F2B600" },
        new() { Kind = AnnotationKind.Number, X1 = 160, Y1 = 150, Number = 1, Color = "#E5484D" },
        new() { Kind = AnnotationKind.Number, X1 = 640, Y1 = 130, Number = 2, Color = "#E5484D" },
        new() { Kind = AnnotationKind.Text, X1 = 560, Y1 = 360, Text = "Click here", FontSize = 32, Color = "#E5484D" },
        new() { Kind = AnnotationKind.Blur, X1 = 240, Y1 = 120, X2 = 400, Y2 = 200 },
    ];

    static string Picture()
    {
        using var bitmap = new SKBitmap(Width, Height);
        bitmap.Erase(new SKColor(0x2E, 0x6F, 0xB7));
        using var canvas = new SKCanvas(bitmap);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = false };
        using var ink = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = false };
        canvas.DrawRect(SKRect.Create(200, 100, 400, 250), white);
        for (int line = 0; line < 4; line++)
            canvas.DrawRect(SKRect.Create(230, 130 + line * 18, 300 - line * 30, 8), ink);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(png.ToArray());
    }
}
