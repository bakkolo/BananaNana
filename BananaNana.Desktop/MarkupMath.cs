namespace BananaNana.Desktop;

// Where the picture sits in the markup window: the picture is fitted to the window (zoom to fit) and centred.
// Left and Top are in view pixels, Scale is view pixels per image pixel. Everything stored is in image pixels.
public readonly record struct ImageFit(double Left, double Top, double Scale)
{
    // Fits a picture of imageWidth x imageHeight into a view of viewWidth x viewHeight, keeping the aspect ratio.
    public static ImageFit Fit(double viewWidth, double viewHeight, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0) return new ImageFit(0, 0, 1);
        double scale = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        return new ImageFit((viewWidth - imageWidth * scale) / 2, (viewHeight - imageHeight * scale) / 2, scale);
    }

    public (double X, double Y) ViewToImage(double viewX, double viewY) =>
        ((viewX - Left) / Scale, (viewY - Top) / Scale);

    public (double X, double Y) ImageToView(double imageX, double imageY) =>
        (Left + imageX * Scale, Top + imageY * Scale);
}

// Helpers the markup window and the renderer share. Plain numbers, so they are tested without a window.
public static class MarkupMath
{
    // The thickness of a line in image pixels: 3 / 6 / 10 px on an 800 px picture, scaled with the longer side.
    public static double ThicknessPx(AnnotationThickness thickness, int imageWidth, int imageHeight)
    {
        double basePx = thickness switch
        {
            AnnotationThickness.S => 3,
            AnnotationThickness.L => 10,
            _ => 6,
        };
        double scale = Math.Max(imageWidth, imageHeight) / 800.0;
        return Math.Max(1, basePx * scale);
    }

    // Radius of a number circle: about 3.5 % of the longer side, never under 10 px.
    public static double NumberRadius(int imageWidth, int imageHeight) =>
        Math.Max(10, 0.035 * Math.Max(imageWidth, imageHeight));

    // The next number for a new Number: one more than the highest existing number (1 when there is none).
    public static int NextNumber(IEnumerable<Annotation> annotations)
    {
        int highest = 0;
        foreach (var a in annotations)
            if (a.Kind == AnnotationKind.Number && a.Number > highest) highest = a.Number;
        return highest + 1;
    }

    // Two corners in any order, as a rectangle with Left <= Right and Top <= Bottom.
    public static (double Left, double Top, double Right, double Bottom) Normalize(double x1, double y1, double x2, double y2) =>
        (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    // A crop kept inside the picture and at least 1 px in each direction. Null (no crop) when the crop covers the whole picture.
    public static CropRect? ClampCrop(CropRect? crop, int imageWidth, int imageHeight)
    {
        if (crop is null) return null;
        int x = Math.Clamp(crop.X, 0, Math.Max(0, imageWidth - 1));
        int y = Math.Clamp(crop.Y, 0, Math.Max(0, imageHeight - 1));
        int w = Math.Clamp(crop.W, 1, imageWidth - x);
        int h = Math.Clamp(crop.H, 1, imageHeight - y);
        return x == 0 && y == 0 && w == imageWidth && h == imageHeight ? null : new CropRect { X = x, Y = y, W = w, H = h };
    }

    // The crop a rectangle drawn from two image points gives, clamped to the picture. Null when it is too small.
    public static CropRect? CropFromDrag(double x1, double y1, double x2, double y2, int imageWidth, int imageHeight)
    {
        var (left, top, right, bottom) = Normalize(x1, y1, x2, y2);
        int x = (int)Math.Round(Math.Clamp(left, 0, imageWidth));
        int y = (int)Math.Round(Math.Clamp(top, 0, imageHeight));
        int w = (int)Math.Round(Math.Clamp(right, 0, imageWidth)) - x;
        int h = (int)Math.Round(Math.Clamp(bottom, 0, imageHeight)) - y;
        if (w < 4 || h < 4) return null;
        return ClampCrop(new CropRect { X = x, Y = y, W = w, H = h }, imageWidth, imageHeight);
    }
}
