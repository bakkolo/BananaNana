using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace BananaNana.Desktop;

// The picture and its markup inside ImageMarkupWindow, drawn with Avalonia. It only shows the state the window hands it
// and raises pointer events in view coordinates; the window does all the editing. The picture is fitted to the control
// (zoom to fit). A blur shows as a grey block here; the saved picture (GuideRender) really pixelates it.
public sealed class MarkupSurface : Control
{
    const double HandleSize = 8;
    static readonly IBrush Handle = Brushes.White;
    static readonly IPen Outline = new Pen(Brushes.DodgerBlue, 1.5);

    public Bitmap? Picture { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public IReadOnlyList<Annotation> Items { get; set; } = [];
    public CropRect? Crop { get; set; }
    public Annotation? Draft { get; set; }
    public bool DraftIsCrop { get; set; }
    public bool DimCrop { get; set; }
    public Annotation? Selected { get; set; }

    // Pointer events in view coordinates. The int is the click count (2 for a double click).
    public event Action<Point, int>? Pressed;
    public event Action<Point>? Moved;
    public event Action<Point>? Released;

    public ImageFit Fit => ImageFit.Fit(Bounds.Width, Bounds.Height, ImageWidth, ImageHeight);

    // The points that resize an annotation: both ends of an arrow, the four corners of a rectangle, none otherwise.
    public static IReadOnlyList<Point> HandlePoints(Annotation a, ImageFit fit)
    {
        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
                return [ToView(fit, a.X1, a.Y1), ToView(fit, a.X2, a.Y2)];
            case AnnotationKind.Box:
            case AnnotationKind.Highlight:
            case AnnotationKind.Blur:
                var (l, t, r, b) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
                return [ToView(fit, l, t), ToView(fit, r, t), ToView(fit, r, b), ToView(fit, l, b)];
            default:
                return [];
        }
    }

    static Point ToView(ImageFit fit, double x, double y)
    {
        var (vx, vy) = fit.ImageToView(x, y);
        return new Point(vx, vy);
    }

    // Layout changes (window resize) redraw, so the picture keeps fitting the window.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        InvalidateVisual();
        return size;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        Pressed?.Invoke(e.GetPosition(this), e.ClickCount);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Moved?.Invoke(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Released?.Invoke(e.GetPosition(this));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (ImageWidth <= 0 || ImageHeight <= 0) return;
        var fit = Fit;
        var picture = new Rect(fit.Left, fit.Top, ImageWidth * fit.Scale, ImageHeight * fit.Scale);
        if (Picture is not null) context.DrawImage(Picture, picture);

        foreach (var annotation in Items) DrawAnnotation(context, annotation, fit);
        if (Draft is not null && !DraftIsCrop) DrawAnnotation(context, Draft, fit);

        // Crop: the area outside the crop is dimmed while the crop tool is active or a crop is being drawn.
        Rect? dimmed = null;
        if (Draft is not null && DraftIsCrop)
        {
            var (l, t, r, b) = MarkupMath.Normalize(Draft.X1, Draft.Y1, Draft.X2, Draft.Y2);
            dimmed = CropView(fit, l, t, r - l, b - t);
        }
        else if (Crop is { } crop)
        {
            var view = CropView(fit, crop.X, crop.Y, crop.W, crop.H);
            if (DimCrop) dimmed = view;
            context.DrawRectangle(null, new Pen(Brushes.White, 1.5, new DashStyle([5, 4], 0)), new RoundedRect(view));
        }
        if (dimmed is { } area) DrawDimming(context, picture, area);

        if (Selected is { } selected)
        {
            var box = BoundsOf(selected, fit);
            context.DrawRectangle(null, Outline, new RoundedRect(box));
            foreach (var point in HandlePoints(selected, fit))
                context.DrawRectangle(Handle, Outline, new RoundedRect(new Rect(point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize)));
        }
    }

    static Rect CropView(ImageFit fit, double x, double y, double w, double h)
    {
        var (vx, vy) = fit.ImageToView(x, y);
        return new Rect(vx, vy, w * fit.Scale, h * fit.Scale);
    }

    // Four rectangles around the crop, inside the picture, at 55 % black.
    static void DrawDimming(DrawingContext context, Rect picture, Rect keep)
    {
        var dim = new SolidColorBrush(Colors.Black, 0.55);
        var top = Math.Max(picture.Top, keep.Top);
        var bottom = Math.Min(picture.Bottom, keep.Bottom);
        context.DrawRectangle(dim, null, new RoundedRect(new Rect(picture.Left, picture.Top, picture.Width, Math.Max(0, top - picture.Top))));
        context.DrawRectangle(dim, null, new RoundedRect(new Rect(picture.Left, bottom, picture.Width, Math.Max(0, picture.Bottom - bottom))));
        context.DrawRectangle(dim, null, new RoundedRect(new Rect(picture.Left, top, Math.Max(0, keep.Left - picture.Left), Math.Max(0, bottom - top))));
        context.DrawRectangle(dim, null, new RoundedRect(new Rect(keep.Right, top, Math.Max(0, picture.Right - keep.Right), Math.Max(0, bottom - top))));
    }

    // The box around an annotation in view coordinates, used for the selection outline.
    Rect BoundsOf(Annotation a, ImageFit fit)
    {
        switch (a.Kind)
        {
            case AnnotationKind.Number:
                var (cx, cy) = fit.ImageToView(a.X1, a.Y1);
                double radius = ViewRadius(fit);
                return new Rect(cx - radius, cy - radius, radius * 2, radius * 2).Inflate(3);
            case AnnotationKind.Text:
                var (tx, ty) = fit.ImageToView(a.X1, a.Y1);
                var lines = a.Text.Replace("\r\n", "\n").Split('\n');
                double size = a.FontSize * fit.Scale;
                double width = lines.Max(l => l.Length) * size * 0.6;
                return new Rect(tx, ty, Math.Max(10, width), size * 1.2 * lines.Length).Inflate(3);
            default:
                var (l, t, r, b) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
                var p1 = ToView(fit, l, t);
                var p2 = ToView(fit, r, b);
                return new Rect(p1, p2).Inflate(3);
        }
    }

    // The radius of a number circle in view pixels: the picture's radius (MarkupMath) times the view scale.
    double ViewRadius(ImageFit fit) => MarkupMath.NumberRadius(ImageWidth, ImageHeight) * fit.Scale;

    void DrawAnnotation(DrawingContext context, Annotation a, ImageFit fit)
    {
        var color = Color.TryParse(a.Color, out var parsed) ? parsed : Colors.Red;
        var brush = new SolidColorBrush(color);
        double width = Math.Max(1.5, MarkupMath.ThicknessPx(a.Thickness, ImageWidth, ImageHeight) * fit.Scale);
        var pen = new Pen(brush, width) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
                DrawArrow(context, a, fit, brush, pen, width);
                break;
            case AnnotationKind.Box:
                var (bl, bt, br, bb) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
                var box = new Rect(ToView(fit, bl, bt), ToView(fit, br, bb));
                context.DrawRectangle(null, pen, new RoundedRect(box, Math.Max(2, width * 1.5)));
                break;
            case AnnotationKind.Highlight:
                var (hl, ht, hr, hb) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
                context.DrawRectangle(new SolidColorBrush(color, 0.35), null, new RoundedRect(new Rect(ToView(fit, hl, ht), ToView(fit, hr, hb))));
                break;
            case AnnotationKind.Blur:
                var (ul, ut, ur, ub) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
                var blurBox = new Rect(ToView(fit, ul, ut), ToView(fit, ur, ub));
                context.DrawRectangle(new SolidColorBrush(Colors.Gray, 0.7), null, new RoundedRect(blurBox));
                break;
            case AnnotationKind.Number:
                var (cx, cy) = fit.ImageToView(a.X1, a.Y1);
                double radius = ViewRadius(fit);
                context.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
                var number = MakeText(a.Number.ToString(), radius * 1.2, Brushes.White, true);
                context.DrawText(number, new Point(cx - number.Width / 2, cy - number.Height / 2));
                break;
            case AnnotationKind.Text:
                var (tx, ty) = fit.ImageToView(a.X1, a.Y1);
                var text = MakeText(a.Text, a.FontSize * fit.Scale, brush, true);
                context.DrawText(text, new Point(tx, ty));
                break;
        }
    }

    static void DrawArrow(DrawingContext context, Annotation a, ImageFit fit, IBrush brush, IPen pen, double width)
    {
        var tail = ToView(fit, a.X1, a.Y1);
        var tip = ToView(fit, a.X2, a.Y2);
        var dx = tip.X - tail.X;
        var dy = tip.Y - tail.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.5) return;
        double ux = dx / length, uy = dy / length;
        double head = Math.Min(length, Math.Max(10, width * 4));
        var headBase = new Point(tip.X - ux * head, tip.Y - uy * head);
        var normal = new Vector(-uy * head * 0.5, ux * head * 0.5);
        context.DrawLine(pen, tail, headBase);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(headBase + normal);
            g.LineTo(headBase - normal);
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    static FormattedText MakeText(string text, double size, IBrush brush, bool bold) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);
}
