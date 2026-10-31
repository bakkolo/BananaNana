using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace BananaNana;

// Simple annotation editor: arrows, rectangles and a highlighter drawn over a picture, then flattened
// into a new picture at the original pixel size. Shapes live on a canvas in image pixel coordinates.
public partial class ImageEditorWindow : Window
{
    enum Tool { Select, Arrow, Rectangle, Highlight }

    readonly int pixelWidth, pixelHeight;
    readonly double thickness;
    Tool tool = Tool.Arrow;
    Brush brush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    Shape? drawing, selected;
    Point start, dragLast;

    public BitmapSource? Result { get; private set; }

    public ImageEditorWindow(BitmapSource source)
    {
        InitializeComponent();
        pixelWidth = source.PixelWidth;
        pixelHeight = source.PixelHeight;
        Surface.Width = Picture.Width = pixelWidth;
        Surface.Height = Picture.Height = pixelHeight;
        Picture.Source = source;
        // Line width scales with the picture so arrows look the same on small and large screenshots.
        thickness = Math.Max(3, Math.Round(Math.Max(pixelWidth, pixelHeight) / 200.0));
    }

    void Tool_Checked(object sender, RoutedEventArgs e)
    {
        tool = Enum.Parse<Tool>((string)((FrameworkElement)sender).Tag);
        if (Surface != null) Surface.Cursor = tool == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        if (tool != Tool.Select) Select(null);
    }

    void Color_Checked(object sender, RoutedEventArgs e) => brush = ((Control)sender).Background;

    void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (tool == Tool.Select)
        {
            Select(e.OriginalSource as Shape);
            if (selected != null) { dragLast = p; Surface.CaptureMouse(); }
            return;
        }
        Select(null);
        start = p;
        drawing = tool switch
        {
            Tool.Arrow => new Path { Fill = brush, Stroke = brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, Data = ImageAnnotation.ArrowGeometry(p, p, thickness) },
            Tool.Rectangle => new Rectangle { Stroke = brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round },
            _ => new Polyline { Stroke = brush, StrokeThickness = thickness * 5, Opacity = 0.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Points = { p } },
        };
        drawing.RenderTransform = new TranslateTransform();   // used by Select to move the shape later
        if (drawing is Rectangle) { Canvas.SetLeft(drawing, p.X); Canvas.SetTop(drawing, p.Y); }
        Surface.Children.Add(drawing);
        Surface.CaptureMouse();
    }

    void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Surface.IsMouseCaptured) return;
        var p = e.GetPosition(Surface);
        if (tool == Tool.Select && selected?.RenderTransform is TranslateTransform move)
        {
            move.X += p.X - dragLast.X;
            move.Y += p.Y - dragLast.Y;
            dragLast = p;
            return;
        }
        switch (drawing)
        {
            case Path arrow: arrow.Data = ImageAnnotation.ArrowGeometry(start, p, thickness); break;
            case Rectangle rect:
                Canvas.SetLeft(rect, Math.Min(start.X, p.X)); Canvas.SetTop(rect, Math.Min(start.Y, p.Y));
                rect.Width = Math.Abs(p.X - start.X); rect.Height = Math.Abs(p.Y - start.Y);
                break;
            case Polyline line: line.Points.Add(p); break;
        }
    }

    void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Surface.ReleaseMouseCapture();
        // A plain click without dragging would leave an invisible dot; drop it.
        if (drawing != null && (e.GetPosition(Surface) - start).Length < 3 && drawing is not Polyline) Surface.Children.Remove(drawing);
        drawing = null;
    }

    void Select(Shape? shape)
    {
        if (selected != null) selected.Effect = null;
        selected = shape;
        if (selected != null) selected.Effect = new DropShadowEffect { Color = Colors.DodgerBlue, ShadowDepth = 0, BlurRadius = 12, Opacity = 1 };
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    // Children[0] is the picture itself; shapes follow in drawing order.
    void Undo()
    {
        if (Surface.Children.Count <= 1) return;
        var last = Surface.Children[^1];
        if (ReferenceEquals(last, selected)) Select(null);
        Surface.Children.Remove(last);
    }

    void DeleteSelected()
    {
        if (selected == null) return;
        Surface.Children.Remove(selected);
        selected = null;
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); e.Handled = true; }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        Select(null);   // the selection glow must not end up in the picture
        Result = ImageAnnotation.Flatten(Surface, pixelWidth, pixelHeight);
        DialogResult = true;
    }
}
