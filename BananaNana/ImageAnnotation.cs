using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BananaNana;

// Drawing helpers for ImageEditorWindow, kept separate so they can be unit-tested without the window.
public static class ImageAnnotation
{
    // Renders the canvas (picture + shapes) at the picture's pixel size. A VisualBrush with an absolute
    // viewbox ignores where the canvas sits on screen and how much the Viewbox scaled it.
    public static BitmapSource Flatten(Visual surface, int pixelWidth, int pixelHeight)
    {
        var area = new Rect(0, 0, pixelWidth, pixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(surface) { Viewbox = area, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }, null, area);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // Shaft plus a filled triangular head; the head never grows longer than the arrow itself.
    public static Geometry ArrowGeometry(Point from, Point to, double thickness)
    {
        var dir = to - from;
        double length = dir.Length;
        if (length < 0.5) return Geometry.Empty;
        dir /= length;
        double head = Math.Min(length, Math.Max(10, thickness * 4));
        var normal = new Vector(-dir.Y, dir.X) * head * 0.5;
        var headBase = to - dir * head;
        var tip = new PathFigure(to, [new LineSegment(headBase + normal, true), new LineSegment(headBase - normal, true)], closed: true);
        return new GeometryGroup { Children = { new LineGeometry(from, headBase), new PathGeometry([tip]) } };
    }
}
