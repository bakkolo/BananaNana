using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace BananaNana.Desktop;

// Marks up one step's picture: arrows, boxes, highlights, numbers, texts, blurs and a crop. Everything is kept in image
// pixels (see Annotation), so the window size and the zoom do not matter. Done gives the annotations and the crop back
// (the caller stores them in the step); Cancel and closing throw the changes away after a question.
public partial class ImageMarkupWindow : Window
{
    enum Tool { Select, Arrow, Box, Highlight, Number, Text, Blur, Crop }
    enum Gesture { None, Draw, Move, Handle }

    // What undo and redo keep: copies of the annotations and of the crop.
    readonly record struct Snapshot(List<Annotation> Items, CropRect? Crop);

    static readonly (string Hex, string Key)[] Swatches =
    [
        ("#E5484D", "Markup.Red"), ("#F2B600", "Markup.Yellow"), ("#2EA043", "Markup.Green"), ("#3B82F6", "Markup.Blue"),
        ("#7B3F8C", "Markup.Purple"), ("#FFFFFF", "Markup.White"), ("#000000", "Markup.Black"),
    ];
    const string DefaultColor = "#E5484D";
    const string HighlightColor = "#F2B600";
    const double GrabDistance = 7; // view pixels

    readonly int imageWidth, imageHeight;
    readonly List<Annotation> items;
    readonly List<Snapshot> undo = new(), redo = new();
    readonly MarkupSurface surface = new();
    readonly List<(Control Control, string Key)> tips = new();
    readonly Dictionary<Tool, ToggleButton> toolButtons = new();
    readonly Dictionary<AnnotationThickness, ToggleButton> sizeButtons = new();
    readonly List<(Button Button, string Hex)> swatches = new();
    readonly Button undoButton = new(), redoButton = new(), deleteButton = new();

    CropRect? crop;
    Tool tool = Tool.Arrow;
    string color = DefaultColor;
    AnnotationThickness thickness = AnnotationThickness.M;
    Annotation? selected, draft;
    bool draftIsCrop, dirty, closeConfirmed, syncing;
    Gesture gesture;
    Snapshot gestureStart;
    bool gestureMoved;
    double lastX, lastY;
    int handle;
    TextBox? textBox;
    Annotation? textTarget;
    double textX, textY;

    // True when the user changed anything (Done then has something to store).
    public bool Changed => dirty;

    // Copies of the markup as it is now, to store into the step after Done.
    public List<Annotation> Annotations => items.Select(a => a.Clone()).ToList();
    public CropRect? Crop => crop?.Clone();

    // The step must have a picture (the markup button is shown only then).
    public ImageMarkupWindow(GuideStep step)
    {
        InitializeComponent();
        Icon = App.Icon;
        var picture = new Bitmap(new MemoryStream(Convert.FromBase64String(step.Image ?? throw new InvalidOperationException("The step has no picture."))));
        imageWidth = picture.PixelSize.Width;
        imageHeight = picture.PixelSize.Height;
        items = step.Annotations.Select(a => a.Clone()).ToList();
        crop = step.Crop?.Clone();

        surface.Picture = picture;
        surface.ImageWidth = imageWidth;
        surface.ImageHeight = imageHeight;
        surface.Items = items;
        surface.HorizontalAlignment = HorizontalAlignment.Stretch;
        surface.VerticalAlignment = VerticalAlignment.Stretch;
        surface.Pressed += OnPressed;
        surface.Moved += OnMoved;
        surface.Released += OnReleased;
        Host.Children.Insert(0, surface);

        BuildToolbar();
        KeyDown += Window_KeyDown;
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        Retranslate();
        SetTool(Tool.Arrow);
        Refresh();
    }

    // ---- Toolbar ----------------------------------------------------------------------------------------------------

    void BuildToolbar()
    {
        var tools = new (Tool Tool, string Icon, string Key)[]
        {
            (Tool.Select, "IconSelect", "Markup.SelectTip"),
            (Tool.Arrow, "IconArrow", "Markup.ArrowTip"),
            (Tool.Box, "IconBox", "Markup.BoxTip"),
            (Tool.Highlight, "IconHighlight", "Markup.HighlightTip"),
            (Tool.Number, "IconNumber", "Markup.NumberTip"),
            (Tool.Text, "IconText", "Markup.TextTip"),
            (Tool.Blur, "IconBlur", "Markup.BlurTip"),
            (Tool.Crop, "IconCrop", "Markup.CropTip"),
        };
        foreach (var (toolKind, icon, key) in tools)
        {
            var button = IconToggle(icon);
            button.IsCheckedChanged += (_, _) =>
            {
                if (syncing) return;
                if (button.IsChecked == true) SetTool(toolKind);
                else if (tool == toolKind) button.IsChecked = true; // a tool stays on until another one is picked
            };
            toolButtons[toolKind] = button;
            tips.Add((button, key));
            Toolbar.Children.Add(button);
        }
        Toolbar.Children.Add(Separator());

        foreach (var (hex, key) in Swatches)
        {
            var swatch = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(3, 0),
                CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(2),
            };
            var chosen = hex;
            swatch.Click += (_, _) => SetColor(chosen);
            swatches.Add((swatch, hex));
            tips.Add((swatch, key));
            Toolbar.Children.Add(swatch);
        }
        Toolbar.Children.Add(Separator());

        foreach (var (size, label) in new[] { (AnnotationThickness.S, "S"), (AnnotationThickness.M, "M"), (AnnotationThickness.L, "L") })
        {
            var button = new ToggleButton { Classes = { "icon" }, Content = label, Width = 30, Margin = new Thickness(0, 0, 4, 0) };
            var chosen = size;
            button.IsCheckedChanged += (_, _) =>
            {
                if (syncing) return;
                if (button.IsChecked == true) SetThickness(chosen);
                else if (thickness == chosen) button.IsChecked = true;
            };
            sizeButtons[size] = button;
            tips.Add((button, size switch { AnnotationThickness.S => "Markup.SizeSTip", AnnotationThickness.L => "Markup.SizeLTip", _ => "Markup.SizeMTip" }));
            Toolbar.Children.Add(button);
        }
        Toolbar.Children.Add(Separator());

        undoButton.Classes.Add("icon");
        undoButton.Content = new PathIcon { Data = IconGeometry("IconUndo"), Width = 14, Height = 14 };
        undoButton.Click += (_, _) => Undo();
        redoButton.Classes.Add("icon");
        redoButton.Content = new PathIcon { Data = IconGeometry("IconRedo"), Width = 14, Height = 14 };
        redoButton.Click += (_, _) => Redo();
        deleteButton.Classes.Add("icon");
        deleteButton.Content = new PathIcon { Data = IconGeometry("IconDelete"), Width = 14, Height = 14 };
        deleteButton.Click += (_, _) => DeleteSelected();
        tips.Add((undoButton, "Markup.UndoTip"));
        tips.Add((redoButton, "Markup.RedoTip"));
        tips.Add((deleteButton, "Markup.DeleteTip"));
        Toolbar.Children.Add(undoButton);
        Toolbar.Children.Add(redoButton);
        Toolbar.Children.Add(deleteButton);
    }

    ToggleButton IconToggle(string icon)
    {
        var button = new ToggleButton { Classes = { "icon" }, Content = new PathIcon { Data = IconGeometry(icon), Width = 14, Height = 14 } };
        return button;
    }

    static Control Separator() => new Border { Width = 1, Height = 22, Margin = new Thickness(6, 0), Background = new SolidColorBrush(Colors.Gray, 0.4) };

    Avalonia.Media.Geometry IconGeometry(string name) => (Avalonia.Media.Geometry)this.FindResource(name)!;

    void Retranslate()
    {
        Title = Loc.T("Markup.Title");
        DoneButton.Content = Loc.T("Markup.Done");
        CancelButton.Content = Loc.T("Common.Cancel");
        ResetCropButton.Content = Loc.T("Markup.ResetCrop");
        foreach (var (control, key) in tips) ToolTip.SetTip(control, Loc.T(key));
    }

    void SetTool(Tool next)
    {
        FinishText(commit: true);
        tool = next;
        if (next == Tool.Highlight) color = HighlightColor; // a highlight is yellow unless the user changes it
        if (next != Tool.Select) selected = null;
        syncing = true;
        foreach (var (kind, button) in toolButtons) button.IsChecked = kind == next;
        syncing = false;
        Refresh();
    }

    // Colour: applies to the selected annotation too, and to the next one drawn.
    void SetColor(string hex)
    {
        color = hex;
        if (selected is { } target && target.Color != hex)
        {
            Change(() => target.Color = hex);
            return;
        }
        Refresh();
    }

    void SetThickness(AnnotationThickness size)
    {
        thickness = size;
        SyncSizeButtons();
        if (selected is { } target && target.Thickness != size)
        {
            Change(() => target.Thickness = size);
            return;
        }
        Refresh();
    }

    void SyncSizeButtons()
    {
        syncing = true;
        foreach (var (kind, button) in sizeButtons) button.IsChecked = kind == thickness;
        syncing = false;
    }

    // Brings the toolbar state, the surface and the buttons in line with the markup.
    void Refresh()
    {
        surface.Items = items;
        surface.Crop = crop;
        surface.Selected = selected;
        surface.Draft = draft;
        surface.DraftIsCrop = draftIsCrop;
        surface.DimCrop = tool == Tool.Crop;
        foreach (var (button, hex) in swatches)
            button.BorderBrush = hex == color ? (IBrush)this.FindResource("AccentText")! : Brushes.Transparent;
        SyncSizeButtons();
        undoButton.IsEnabled = undo.Count > 0 && gesture == Gesture.None;
        redoButton.IsEnabled = redo.Count > 0 && gesture == Gesture.None;
        deleteButton.IsEnabled = selected is not null;
        ResetCropButton.IsVisible = crop is not null;
        surface.InvalidateVisual();
    }

    // ---- Changes, undo and redo -------------------------------------------------------------------------------------

    Snapshot Take() => new(items.Select(a => a.Clone()).ToList(), crop?.Clone());

    // Runs one change: the state before it goes on the undo stack and the redo stack is cleared.
    void Change(Action mutate)
    {
        undo.Add(Take());
        redo.Clear();
        mutate();
        dirty = true;
        Refresh();
    }

    void Restore(Snapshot state)
    {
        items.Clear();
        items.AddRange(state.Items.Select(a => a.Clone()));
        crop = state.Crop?.Clone();
        selected = null;
        draft = null;
        draftIsCrop = false;
        Refresh();
    }

    void Undo()
    {
        if (undo.Count == 0 || gesture != Gesture.None) return;
        redo.Add(Take());
        var previous = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Restore(previous);
        dirty = true;
    }

    void Redo()
    {
        if (redo.Count == 0 || gesture != Gesture.None) return;
        undo.Add(Take());
        var next = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        Restore(next);
        dirty = true;
    }

    void DeleteSelected()
    {
        if (selected is not { } target) return;
        selected = null;
        Change(() => items.Remove(target));
    }

    // ---- Pointer ----------------------------------------------------------------------------------------------------

    (double X, double Y) ImagePoint(Point view)
    {
        var (x, y) = surface.Fit.ViewToImage(view.X, view.Y);
        return (Math.Clamp(x, 0, imageWidth), Math.Clamp(y, 0, imageHeight));
    }

    void OnPressed(Point view, int clicks)
    {
        var (x, y) = ImagePoint(view);
        switch (tool)
        {
            case Tool.Select:
                PressSelect(view, x, y, clicks);
                break;
            case Tool.Number:
                Change(() => items.Add(new Annotation
                {
                    Kind = AnnotationKind.Number, X1 = x, Y1 = y, Number = MarkupMath.NextNumber(items), Color = color, Thickness = thickness,
                }));
                break;
            case Tool.Text:
                OpenTextEditor(x, y, null);
                break;
            default:
                gesture = Gesture.Draw;
                draftIsCrop = tool == Tool.Crop;
                draft = new Annotation { Kind = KindOf(tool), X1 = x, Y1 = y, X2 = x, Y2 = y, Color = color, Thickness = thickness };
                Refresh();
                break;
        }
    }

    void PressSelect(Point view, double x, double y, int clicks)
    {
        if (selected is { } current && HandleAt(current, view) is { } index)
        {
            gesture = Gesture.Handle;
            handle = index;
        }
        else if (HitItem(view) is { } hit)
        {
            selected = hit;
            if (clicks >= 2 && hit.Kind == AnnotationKind.Text)
            {
                gesture = Gesture.None;
                OpenTextEditor(hit.X1, hit.Y1, hit);
                return;
            }
            gesture = Gesture.Move;
            lastX = x;
            lastY = y;
        }
        else
        {
            selected = null;
            gesture = Gesture.None;
        }
        gestureStart = Take();
        gestureMoved = false;
        Refresh();
    }

    void OnMoved(Point view)
    {
        if (gesture == Gesture.None) return;
        var (x, y) = ImagePoint(view);
        switch (gesture)
        {
            case Gesture.Draw when draft is not null:
                draft.X2 = x;
                draft.Y2 = y;
                surface.InvalidateVisual();
                break;
            case Gesture.Move when selected is not null:
                MoveBy(selected, x - lastX, y - lastY);
                gestureMoved |= x != lastX || y != lastY;
                lastX = x;
                lastY = y;
                surface.InvalidateVisual();
                break;
            case Gesture.Handle when selected is not null:
                ResizeTo(selected, handle, x, y);
                gestureMoved = true;
                surface.InvalidateVisual();
                break;
        }
    }

    void OnReleased(Point view)
    {
        switch (gesture)
        {
            case Gesture.Draw:
                FinishDraw();
                break;
            case Gesture.Move:
            case Gesture.Handle:
                // The move or resize is one undo step, taken from the state before the press.
                if (gestureMoved)
                {
                    undo.Add(gestureStart);
                    redo.Clear();
                    dirty = true;
                }
                gesture = Gesture.None;
                Refresh();
                break;
        }
    }

    void FinishDraw()
    {
        var shape = draft;
        var wasCrop = draftIsCrop;
        gesture = Gesture.None;
        draft = null;
        draftIsCrop = false;
        if (shape is null) { Refresh(); return; }
        bool big = Math.Abs(shape.X2 - shape.X1) >= 3 || Math.Abs(shape.Y2 - shape.Y1) >= 3;
        if (!big)
        {
            Refresh();
            return;
        }
        if (wasCrop)
        {
            var next = MarkupMath.CropFromDrag(shape.X1, shape.Y1, shape.X2, shape.Y2, imageWidth, imageHeight);
            if (next is not null) Change(() => crop = next);
            else Refresh();
            return;
        }
        Change(() => items.Add(shape));
    }

    // Esc: cancels a drawing, or puts back a move or resize that is in progress.
    void CancelGesture()
    {
        switch (gesture)
        {
            case Gesture.Draw:
                draft = null;
                draftIsCrop = false;
                gesture = Gesture.None;
                Refresh();
                break;
            case Gesture.Move:
            case Gesture.Handle:
                gesture = Gesture.None;
                Restore(gestureStart);
                break;
        }
    }

    static AnnotationKind KindOf(Tool tool) => tool switch
    {
        Tool.Arrow => AnnotationKind.Arrow,
        Tool.Highlight => AnnotationKind.Highlight,
        Tool.Blur => AnnotationKind.Blur,
        Tool.Text => AnnotationKind.Text,
        Tool.Number => AnnotationKind.Number,
        _ => AnnotationKind.Box, // Box, and the crop frame
    };

    // ---- Hit testing and handles (view pixels, the picture is fitted by ImageFit) -----------------------------------

    Annotation? HitItem(Point view)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (Hits(items[i], view)) return items[i];
        return null;
    }

    bool Hits(Annotation a, Point view)
    {
        var fit = surface.Fit;
        var (ax, ay) = fit.ImageToView(a.X1, a.Y1);
        var (bx, by) = fit.ImageToView(a.X2, a.Y2);
        double tolerance = GrabDistance;
        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
                double width = MarkupMath.ThicknessPx(a.Thickness, imageWidth, imageHeight) * fit.Scale;
                return DistanceToSegment(view.X, view.Y, ax, ay, bx, by) <= tolerance + width / 2;
            case AnnotationKind.Number:
                double radius = MarkupMath.NumberRadius(imageWidth, imageHeight) * fit.Scale;
                return Hypot(view.X - ax, view.Y - ay) <= radius + tolerance;
            case AnnotationKind.Text:
                var lines = a.Text.Replace("\r\n", "\n").Split('\n');
                double size = a.FontSize * fit.Scale;
                double textWidth = Math.Max(10, lines.Max(l => l.Length) * size * 0.6);
                double textHeight = size * 1.2 * lines.Length;
                return view.X >= ax - tolerance && view.X <= ax + textWidth + tolerance
                    && view.Y >= ay - tolerance && view.Y <= ay + textHeight + tolerance;
            default:
                var (l, t, r, b) = MarkupMath.Normalize(ax, ay, bx, by);
                return view.X >= l - tolerance && view.X <= r + tolerance && view.Y >= t - tolerance && view.Y <= b + tolerance;
        }
    }

    static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        return Hypot(px - (ax + t * dx), py - (ay + t * dy));
    }

    int? HandleAt(Annotation a, Point view)
    {
        var points = MarkupSurface.HandlePoints(a, surface.Fit);
        for (int i = 0; i < points.Count; i++)
            if (Math.Abs(points[i].X - view.X) <= GrabDistance && Math.Abs(points[i].Y - view.Y) <= GrabDistance) return i;
        return null;
    }

    static void MoveBy(Annotation a, double dx, double dy)
    {
        a.X1 += dx;
        a.Y1 += dy;
        if (a.Kind is AnnotationKind.Number or AnnotationKind.Text) return;
        a.X2 += dx;
        a.Y2 += dy;
    }

    // Arrow: the dragged end moves. Rectangle: the dragged corner moves, the opposite corner stays.
    static void ResizeTo(Annotation a, int index, double x, double y)
    {
        if (a.Kind == AnnotationKind.Arrow)
        {
            if (index == 0) { a.X1 = x; a.Y1 = y; }
            else { a.X2 = x; a.Y2 = y; }
            return;
        }
        var (l, t, r, b) = MarkupMath.Normalize(a.X1, a.Y1, a.X2, a.Y2);
        switch (index)
        {
            case 0: l = x; t = y; break;
            case 1: r = x; t = y; break;
            case 2: r = x; b = y; break;
            default: l = x; b = y; break;
        }
        a.X1 = l; a.Y1 = t; a.X2 = r; a.Y2 = b;
    }

    // ---- Text -------------------------------------------------------------------------------------------------------

    double DefaultFontSize() => Math.Round(Math.Max(14, Math.Min(imageWidth, imageHeight) * 0.06));

    // A text box at the picture point (new text) or over an existing text. Enter keeps the text, Esc or leaving the box drops it.
    void OpenTextEditor(double x, double y, Annotation? existing)
    {
        FinishText(commit: true);
        textTarget = existing;
        textX = existing?.X1 ?? x;
        textY = existing?.Y1 ?? y;
        var fit = surface.Fit;
        var (vx, vy) = fit.ImageToView(textX, textY);
        double fontSize = existing?.FontSize ?? DefaultFontSize();
        var box = new TextBox
        {
            Text = existing?.Text ?? "",
            Watermark = Loc.T("Markup.TextWatermark"),
            Foreground = new SolidColorBrush(Color.Parse(existing?.Color ?? color)),
            FontWeight = FontWeight.Bold,
            FontSize = Math.Max(10, fontSize * fit.Scale),
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(vx, vy, 0, 0),
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FinishText(commit: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { FinishText(commit: false); e.Handled = true; }
        };
        box.LostFocus += (_, _) => FinishText(commit: true);
        textBox = box;
        Host.Children.Add(box);
        box.Focus();
    }

    // Closes the text box. Committing a new text adds it; committing over an existing one changes its text (an empty one is removed).
    void FinishText(bool commit)
    {
        var box = textBox;
        if (box is null) return;
        textBox = null; // first, so the focus loss caused by removing the box does nothing
        Host.Children.Remove(box);
        var target = textTarget;
        textTarget = null;
        if (!commit) return;
        string text = (box.Text ?? "").Replace("\r", "").Trim('\n');
        if (target is not null)
        {
            if (text.Trim().Length == 0) { selected = null; Change(() => items.Remove(target)); return; }
            if (text != target.Text) Change(() => target.Text = text);
            return;
        }
        if (text.Trim().Length == 0) return;
        double x = textX, y = textY;
        Change(() => items.Add(new Annotation
        {
            Kind = AnnotationKind.Text, X1 = x, Y1 = y, Text = text, Color = color, Thickness = thickness, FontSize = DefaultFontSize(),
        }));
    }

    // ---- Window -----------------------------------------------------------------------------------------------------

    void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (textBox is not null) return; // the text box handles its own keys
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Escape)
        {
            CancelGesture();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && selected is not null)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z && !shift)
        {
            Undo();
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift)))
        {
            Redo();
            e.Handled = true;
        }
    }

    void ResetCrop_Click(object? sender, RoutedEventArgs e)
    {
        if (crop is null) return;
        Change(() => crop = null);
    }

    void Done_Click(object? sender, RoutedEventArgs e)
    {
        FinishText(commit: true);
        closeConfirmed = true;
        Close(true);
    }

    async void Cancel_Click(object? sender, RoutedEventArgs e) => await CancelAsync();

    async Task CancelAsync()
    {
        FinishText(commit: false);
        if (!dirty)
        {
            closeConfirmed = true;
            Close(false);
            return;
        }
        bool discard = await Dialogs.Confirm(this, Loc.T("Markup.Title"), Loc.T("Markup.DiscardText"), Loc.T("Markup.Discard"), Loc.T("Common.Cancel"));
        if (!discard) return;
        closeConfirmed = true;
        Close(false);
    }

    // The window's close button counts as Cancel: with changes, the question comes first.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (closeConfirmed || !dirty)
        {
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        _ = CancelAsync();
    }
}
