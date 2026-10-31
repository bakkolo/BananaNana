using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Xunit;

namespace BananaNana.Tests;

public class ImageAnnotationTests
{
    static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    static Color PixelAt(BitmapSource bmp, int x, int y)
    {
        var px = new byte[4];
        new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    [Fact]
    public void Arrow_head_points_at_the_target_and_never_exceeds_the_length() => Sta(() =>
    {
        var g = ImageAnnotation.ArrowGeometry(new Point(0, 50), new Point(100, 50), 3);
        Assert.Equal(new Rect(0, 44, 100, 12), g.Bounds);          // head 12 px long, 12 px wide at the tip end
        Assert.True(g.FillContains(new Point(99, 50)));

        var tiny = ImageAnnotation.ArrowGeometry(new Point(0, 0), new Point(4, 0), 3);
        Assert.True(tiny.Bounds.Width <= 4.01);
        Assert.Same(Geometry.Empty, ImageAnnotation.ArrowGeometry(new Point(5, 5), new Point(5, 5), 3));
    });

    [Fact]
    public void Flatten_merges_shapes_into_the_picture_at_its_pixel_size() => Sta(() =>
    {
        // White 200x100 picture shown shrunk to half size, like the editor's Viewbox does with big screenshots.
        var white = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 200 * 100 * 4).ToArray(), 200 * 4);
        var surface = new Canvas { Width = 200, Height = 100, ClipToBounds = true };
        surface.Children.Add(new Image { Source = white, Width = 200, Height = 100, Stretch = Stretch.Fill });
        var rect = new Rectangle { Width = 40, Height = 40, Fill = Brushes.Red };
        Canvas.SetLeft(rect, 150); Canvas.SetTop(rect, 50);
        surface.Children.Add(rect);
        surface.Children.Add(new Rectangle { Width = 500, Height = 10, Fill = Brushes.Blue });   // overflows: must be clipped, not rescale the result
        var host = new Viewbox { Width = 100, Height = 50, Child = surface };
        host.Measure(new Size(100, 50));
        host.Arrange(new Rect(0, 0, 100, 50));

        var result = ImageAnnotation.Flatten(surface, 200, 100);

        Assert.Equal((200, 100), (result.PixelWidth, result.PixelHeight));
        Assert.Equal(Colors.Red, PixelAt(result, 170, 70));
        Assert.Equal(Colors.White, PixelAt(result, 100, 70));
        Assert.Equal(Colors.Blue, PixelAt(result, 190, 5));
    });
}
