using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The markup window's coordinate conversion and the small rules next to it (numbers, thickness, crop clamping).
public class MarkupMathTests
{
    [Fact]
    public void A_picture_wider_than_the_view_is_fitted_and_centred_vertically()
    {
        // 800 x 400 picture in a 400 x 300 view: scale 0.5, height 200, 50 px of space above and below.
        var fit = ImageFit.Fit(400, 300, 800, 400);

        Assert.Equal(0.5, fit.Scale, 9);
        Assert.Equal(0, fit.Left, 9);
        Assert.Equal(50, fit.Top, 9);
    }

    [Fact]
    public void A_tall_picture_is_centred_horizontally_when_the_view_is_wide()
    {
        var fit = ImageFit.Fit(1000, 300, 400, 300);

        Assert.Equal(1, fit.Scale, 9);
        Assert.Equal(300, fit.Left, 9);
        Assert.Equal(0, fit.Top, 9);
    }

    [Fact]
    public void View_and_image_coordinates_convert_both_ways()
    {
        var fit = ImageFit.Fit(1000, 700, 800, 450);

        var (vx, vy) = fit.ImageToView(100, 50);
        var (ix, iy) = fit.ViewToImage(vx, vy);

        Assert.Equal(100, ix, 9);
        Assert.Equal(50, iy, 9);
        Assert.Equal((0.0, 0.0), fit.ViewToImage(fit.Left, fit.Top));
    }

    [Fact]
    public void A_point_keeps_its_image_coordinates_when_the_window_is_resized()
    {
        // The same picture point (the centre) in two window sizes: the view points differ, the image point does not.
        var small = ImageFit.Fit(500, 400, 800, 450);
        var large = ImageFit.Fit(1400, 900, 800, 450);
        var (cx1, cy1) = small.ImageToView(400, 225);
        var (cx2, cy2) = large.ImageToView(400, 225);

        Assert.NotEqual(cx1, cx2);
        var (ix1, iy1) = small.ViewToImage(cx1, cy1);
        var (ix2, iy2) = large.ViewToImage(cx2, cy2);
        Assert.Equal(400, ix1, 9);
        Assert.Equal(225, iy1, 9);
        Assert.Equal(400, ix2, 9);
        Assert.Equal(225, iy2, 9);
    }

    [Fact]
    public void Thickness_is_3_6_or_10_px_on_an_800_px_picture_and_scales_with_the_picture()
    {
        Assert.Equal(3, MarkupMath.ThicknessPx(AnnotationThickness.S, 800, 450), 9);
        Assert.Equal(6, MarkupMath.ThicknessPx(AnnotationThickness.M, 800, 450), 9);
        Assert.Equal(10, MarkupMath.ThicknessPx(AnnotationThickness.L, 800, 450), 9);
        Assert.Equal(12, MarkupMath.ThicknessPx(AnnotationThickness.M, 1600, 900), 9);
    }

    [Fact]
    public void The_next_number_is_one_more_than_the_highest_number()
    {
        Assert.Equal(1, MarkupMath.NextNumber([]));
        Assert.Equal(1, MarkupMath.NextNumber([new Annotation { Kind = AnnotationKind.Text, Number = 9 }]));
        Assert.Equal(6, MarkupMath.NextNumber([
            new Annotation { Kind = AnnotationKind.Number, Number = 1 },
            new Annotation { Kind = AnnotationKind.Number, Number = 5 },
            new Annotation { Kind = AnnotationKind.Box },
        ]));
    }

    [Fact]
    public void A_crop_is_kept_inside_the_picture()
    {
        var clamped = MarkupMath.ClampCrop(new CropRect { X = 700, Y = -5, W = 500, H = 10 }, 800, 450);

        Assert.NotNull(clamped);
        Assert.Equal((700, 0, 100, 10), (clamped.X, clamped.Y, clamped.W, clamped.H));
    }

    [Fact]
    public void A_crop_of_the_whole_picture_means_no_crop()
    {
        Assert.Null(MarkupMath.ClampCrop(new CropRect { X = 0, Y = 0, W = 800, H = 450 }, 800, 450));
    }

    [Fact]
    public void A_crop_drawn_backwards_gives_the_same_rectangle_and_a_tiny_drag_gives_none()
    {
        var forward = MarkupMath.CropFromDrag(100, 50, 300, 250, 800, 450);
        var backward = MarkupMath.CropFromDrag(300, 250, 100, 50, 800, 450);

        Assert.NotNull(forward);
        Assert.Equal((100, 50, 200, 200), (forward.X, forward.Y, forward.W, forward.H));
        Assert.Equal((forward.X, forward.Y, forward.W, forward.H), (backward!.X, backward.Y, backward.W, backward.H));
        Assert.Null(MarkupMath.CropFromDrag(100, 100, 102, 300, 800, 450));
    }
}
