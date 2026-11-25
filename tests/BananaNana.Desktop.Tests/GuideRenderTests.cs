using BananaNana.Desktop;
using SkiaSharp;
using Xunit;

namespace BananaNana.Desktop.Tests;

// GuideRender: crop, blur, highlight and the number and text drawing, checked on the pixels of the flattened picture.
public class GuideRenderTests
{
    static string Base64(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(png.ToArray());
    }

    static SKBitmap Solid(int width, int height, SKColor colour)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        return bitmap;
    }

    // Black and white 1 px squares: any blur of a region turns it into grey blocks.
    static SKBitmap Checkerboard(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Black : SKColors.White);
        return bitmap;
    }

    static GuideStep Step(SKBitmap picture, CropRect? crop = null, params Annotation[] annotations) => new()
    {
        Id = Guid.NewGuid(),
        Image = Base64(picture),
        ImageType = "png",
        Crop = crop,
        Annotations = annotations.ToList(),
    };

    static SKBitmap Decode(byte[] png) => SKBitmap.Decode(png)!;

    [Fact]
    public void Flatten_gives_the_crop_size()
    {
        var step = Step(Solid(800, 450, SKColors.SteelBlue), new CropRect { X = 40, Y = 22, W = 720, H = 406 });

        using var result = Decode(GuideRender.Flatten(step));

        Assert.Equal(720, result.Width);
        Assert.Equal(406, result.Height);
    }

    [Fact]
    public void Flatten_without_a_crop_keeps_the_picture_size()
    {
        var step = Step(Solid(300, 200, SKColors.Orange));

        using var result = Decode(GuideRender.Flatten(step));

        Assert.Equal(300, result.Width);
        Assert.Equal(200, result.Height);
    }

    [Fact]
    public void Flatten_takes_the_crop_from_the_picture_position()
    {
        // The top-left pixel of the crop is the picture pixel at (X, Y).
        var picture = Solid(100, 100, SKColors.Black);
        picture.SetPixel(30, 40, SKColors.Lime);
        var step = Step(picture, new CropRect { X = 30, Y = 40, W = 20, H = 20 });

        using var result = Decode(GuideRender.Flatten(step));

        Assert.Equal(SKColors.Lime, result.GetPixel(0, 0));
    }

    [Fact]
    public void Blur_changes_the_pixels_of_its_region_and_leaves_the_rest_alone()
    {
        var picture = Checkerboard(200, 200);
        var blur = new Annotation { Kind = AnnotationKind.Blur, X1 = 20, Y1 = 20, X2 = 80, Y2 = 80 };

        using var result = GuideRender.Render(picture, null, [blur]);

        // Inside: the 12 px block holding (25, 25) is one colour now, and it is not the black or white it was.
        var inside = result.GetPixel(25, 25);
        Assert.Equal(inside, result.GetPixel(26, 26));
        Assert.NotEqual(picture.GetPixel(25, 25), inside);
        Assert.NotEqual(SKColors.Black, inside);
        Assert.NotEqual(SKColors.White, inside);
        // Outside the region nothing changed, next to it included.
        Assert.Equal(picture.GetPixel(5, 5), result.GetPixel(5, 5));
        Assert.Equal(picture.GetPixel(150, 150), result.GetPixel(150, 150));
        Assert.Equal(picture.GetPixel(81, 81), result.GetPixel(81, 81));
        // The stored picture itself is not touched.
        Assert.Equal(SKColors.Black, picture.GetPixel(25, 25));
    }

    [Fact]
    public void Blur_is_applied_before_the_crop_and_the_other_marks()
    {
        // A box drawn over a blurred part keeps its colour: the blur is under it.
        var picture = Checkerboard(800, 800); // 800 px so that a 3 px line is a full 3 px wide
        var blur = new Annotation { Kind = AnnotationKind.Blur, X1 = 10, Y1 = 10, X2 = 60, Y2 = 60 };
        var box = new Annotation { Kind = AnnotationKind.Box, X1 = 20, Y1 = 20, X2 = 50, Y2 = 50, Color = "#E5484D", Thickness = AnnotationThickness.S };

        using var result = GuideRender.Render(picture, null, [box, blur]);

        Assert.Equal(new SKColor(0xE5, 0x48, 0x4D), result.GetPixel(20, 35)); // the box's left edge, not blurred
    }

    [Fact]
    public void Highlight_blends_with_the_picture_underneath()
    {
        var step = Step(Solid(100, 100, SKColors.White),
            null,
            new Annotation { Kind = AnnotationKind.Highlight, X1 = 10, Y1 = 10, X2 = 50, Y2 = 50, Color = "#FFFF00" });

        using var result = Decode(GuideRender.Flatten(step));

        // Yellow at about 35 % over white: red and green stay 255, blue drops by 35 %.
        double alpha = 0x59 / 255.0;
        var inside = result.GetPixel(20, 20);
        Assert.InRange(inside.Red, 253, 255);
        Assert.InRange(inside.Green, 253, 255);
        Assert.InRange(inside.Blue, 255 * (1 - alpha) - 3, 255 * (1 - alpha) + 3);
        Assert.Equal(SKColors.White, result.GetPixel(90, 90));
    }

    [Fact]
    public void Number_circle_is_drawn_at_its_centre_with_the_radius_for_the_picture_size()
    {
        // 800 x 450: the radius is 3.5 % of the longer side, 28 px.
        var step = Step(Solid(800, 450, SKColors.White), null,
            new Annotation { Kind = AnnotationKind.Number, X1 = 400, Y1 = 225, Number = 1, Color = "#2EA043" });

        using var result = Decode(GuideRender.Flatten(step));

        var green = new SKColor(0x2E, 0xA0, 0x43);
        // Inside the circle, away from the digit.
        var inside = result.GetPixel(383, 208);
        Assert.InRange(Math.Abs(inside.Red - green.Red) + Math.Abs(inside.Green - green.Green) + Math.Abs(inside.Blue - green.Blue), 0, 12);
        // Just outside the radius, and far outside, stays white.
        Assert.Equal(SKColors.White, result.GetPixel(400 + 34, 225));
        Assert.Equal(SKColors.White, result.GetPixel(10, 10));
    }

    [Fact]
    public void Arrow_draws_its_shaft_between_the_tail_and_the_tip()
    {
        // 800 x 450 so that the small line is 3 px wide and its middle pixels are fully covered.
        var step = Step(Solid(800, 450, SKColors.White), null,
            new Annotation { Kind = AnnotationKind.Arrow, X1 = 100, Y1 = 225, X2 = 700, Y2 = 225, Color = "#3B82F6", Thickness = AnnotationThickness.S });

        using var result = Decode(GuideRender.Flatten(step));

        Assert.Equal(new SKColor(0x3B, 0x82, 0xF6), result.GetPixel(400, 225));
        Assert.Equal(SKColors.White, result.GetPixel(400, 100));
    }

    [Fact]
    public void Text_and_a_crop_flatten_without_error_and_keep_the_crop_size()
    {
        var step = Step(Solid(800, 450, SKColors.DarkSlateGray), new CropRect { X = 40, Y = 22, W = 720, H = 406 },
            new Annotation { Kind = AnnotationKind.Text, X1 = 300, Y1 = 200, Text = "Click here", FontSize = 32, Color = "#E5484D" });

        using var result = Decode(GuideRender.Flatten(step));

        Assert.Equal(720, result.Width);
        Assert.Equal(406, result.Height);
    }

    [Fact]
    public void HasMarkup_is_false_for_a_plain_step_and_true_with_a_crop_or_annotations()
    {
        Assert.False(GuideRender.HasMarkup(new GuideStep()));
        Assert.True(GuideRender.HasMarkup(new GuideStep { Crop = new CropRect { W = 1, H = 1 } }));
        Assert.True(GuideRender.HasMarkup(new GuideStep { Annotations = [new Annotation()] }));
    }
}
