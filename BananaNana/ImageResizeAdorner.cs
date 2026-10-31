using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BananaNana;

// Frame with four corner handles over an image in the editor. Dragging a corner resizes the image with
// its aspect ratio locked; the size is stored as Image.Width/Height, which the XamlPackage keeps and the
// renderer uses when pasting.
public sealed class ImageResizeAdorner : Adorner
{
    public const double MinSize = 16;
    const double HandleSize = 9;

    readonly Image image;
    readonly VisualCollection visuals;
    readonly Thumb[] handles = new Thumb[4];   // top-left, top-right, bottom-right, bottom-left
    readonly double ratio;                      // height / width
    Size startSize;
    Point startMouse;

    public ImageResizeAdorner(Image image) : base(image)
    {
        this.image = image;
        visuals = new VisualCollection(this);
        ratio = image.Source is BitmapSource { PixelWidth: > 0 } b ? (double)b.PixelHeight / b.PixelWidth
            : image.ActualWidth > 0 ? image.ActualHeight / image.ActualWidth : 1;
        for (int i = 0; i < 4; i++)
        {
            int corner = i;
            var handle = new Thumb
            {
                Width = HandleSize, Height = HandleSize,
                Cursor = corner % 2 == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW,
                Background = Brushes.White, BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(1),
            };
            handle.DragStarted += (_, _) => BeginDrag(Mouse.GetPosition(Parent as IInputElement ?? image));
            // The total offset from the drag start is used rather than Thumb's deltas: left/top handles
            // don't move while the image grows to the right, so their deltas would accumulate.
            handle.DragDelta += (_, _) => Drag(corner, Mouse.GetPosition(Parent as IInputElement ?? image) - startMouse);
            handles[i] = handle;
            visuals.Add(handle);
        }
    }

    public static ImageResizeAdorner? Attach(Image image)
    {
        var layer = AdornerLayer.GetAdornerLayer(image);
        if (layer == null) return null;
        var adorner = new ImageResizeAdorner(image);
        layer.Add(adorner);
        return adorner;
    }

    public Image Target => image;

    public void Detach() => AdornerLayer.GetAdornerLayer(image)?.Remove(this);

    internal void BeginDrag(Point mouse)
    {
        startSize = new Size(image.ActualWidth, image.ActualHeight);
        startMouse = mouse;
    }

    // Dragging a corner outwards grows the image; the larger of the two movements wins.
    internal void Drag(int corner, Vector total)
    {
        double dw = corner is 0 or 3 ? -total.X : total.X;
        double dh = corner is 0 or 1 ? -total.Y : total.Y;
        double width = Math.Max(MinSize, startSize.Width + (Math.Abs(dw) >= Math.Abs(dh / ratio) ? dw : dh / ratio));
        image.MaxWidth = image.MaxHeight = double.PositiveInfinity;
        image.Width = Math.Round(width);
        image.Height = Math.Round(width * ratio);
    }

    protected override int VisualChildrenCount => visuals.Count;
    protected override Visual GetVisualChild(int index) => visuals[index];

    protected override Size ArrangeOverride(Size finalSize)
    {
        double w = AdornedElement.RenderSize.Width, h = AdornedElement.RenderSize.Height, o = HandleSize / 2;
        var corners = new[] { new Point(0, 0), new Point(w, 0), new Point(w, h), new Point(0, h) };
        for (int i = 0; i < 4; i++) handles[i].Arrange(new Rect(corners[i].X - o, corners[i].Y - o, HandleSize, HandleSize));
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc) =>
        dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash }, new Rect(AdornedElement.RenderSize));
}

public static class EditorImages
{
    public const double DefaultMaxWidth = 500, DefaultMaxHeight = 350;

    // An Image without an explicit size is stretched by WPF to fill the editor width, so the editor would show
    // a different size than gets pasted. Give every such image its natural size, scaled down to fit the defaults.
    public static void FixSizes(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Image { Source: BitmapSource source } image && (double.IsNaN(image.Width) || double.IsNaN(image.Height)))
            {
                double w = source.PixelWidth * 96 / source.DpiX, h = source.PixelHeight * 96 / source.DpiY;
                double scale = Math.Min(1, Math.Min(Cap(image.MaxWidth, DefaultMaxWidth) / w, Cap(image.MaxHeight, DefaultMaxHeight) / h));
                image.Width = Math.Round(w * scale);
                image.Height = Math.Round(h * scale);
            }
            FixSizes(child);
        }
    }

    static double Cap(double max, double fallback) => double.IsInfinity(max) ? fallback : max;
}
