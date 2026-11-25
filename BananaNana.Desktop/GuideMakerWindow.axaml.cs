using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BananaNana.Linux.Clipboard;
namespace BananaNana.Desktop;

// The Guide maker: a list of guides and, inside the same window, one guide with its steps and pictures.
// Changes autosave 800 ms after the last one; Ctrl+S saves now; closing the window saves first.
public partial class GuideMakerWindow : Window
{
    const int AutosaveMs = 800;
    static readonly string[] PictureFormats = ["image/png", "image/jpeg"];
    static GuideMakerWindow? open;

    // The Export menu's items (the menu itself is built in BuildExportMenu).
    readonly MenuItem copyItem = new(), pdfItem = new(), wordItem = new(), webItem = new(), snippetItem = new();

    readonly GuideStore store = new(GuideStore.DefaultFolder());
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(AutosaveMs) };
    GuideDocument? doc;
    bool dirty, loading;
    string statusKey = "";

    // One window at a time: a second request brings the open window forward. The start says what to open in it:
    // the sample guide, or the markup demo (with edit: its step opens in the markup window too).
    public static void ShowOrActivate(GuideStart start = GuideStart.Window)
    {
        if (open is null)
        {
            open = new GuideMakerWindow();
            open.Closed += (_, _) => open = null;
            open.Show();
        }
        else
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Show();
            open.Activate();
        }
        switch (start)
        {
            case GuideStart.Sample: open.OpenSample(); break;
            case GuideStart.MarkupDemo: _ = open.OpenDemoAsync(edit: false); break;
            case GuideStart.MarkupDemoEdit: _ = open.OpenDemoAsync(edit: true); break;
        }
    }

    // Opens the sample guide (made by GuideSample when it does not exist yet).
    void OpenSample()
    {
        if (!SaveNow()) return;
        try
        {
            OpenGuide(GuideSample.EnsureSample(store));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _ = Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
    }

    // --guide-markup-demo: the demo guide is made again and opened. With edit, its step opens in the markup window.
    async Task OpenDemoAsync(bool edit)
    {
        if (!SaveNow()) return;
        Guid id;
        try
        {
            id = GuideMarkupDemo.Create(store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
            return;
        }
        OpenGuide(id);
        if (edit && doc is not null && doc.Steps.Count > 0) await MarkUpAsync(doc.Steps[0].Id);
    }

    public GuideMakerWindow()
    {
        InitializeComponent();
        Icon = App.Icon;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(InputElement.KeyDownEvent, new EventHandler<KeyEventArgs>(OnKeyDown), RoutingStrategies.Tunnel);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        BuildExportMenu();
        Retranslate();
        ShowList();
    }

    // Closing saves first. When the save fails the window stays open, so nothing is lost silently.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!SaveNow()) e.Cancel = true;
        base.OnClosing(e);
    }

    void Retranslate()
    {
        Title = Loc.T("Guide.Title");
        HeadingText.Text = Loc.T("Guide.Title");
        NewLabel.Text = Loc.T("Guide.New");
        BackLabel.Text = Loc.T("Guide.Back");
        TitleBox.Watermark = Loc.T("Guide.TitleWatermark");
        IntroBox.Watermark = Loc.T("Guide.IntroWatermark");
        AddStepButton.Content = Loc.T("Guide.AddStep");
        ExportLabel.Text = Loc.T("Guide.Export");
        copyItem.Header = Loc.T("Guide.ExportCopy");
        pdfItem.Header = Loc.T("Guide.ExportPdf");
        wordItem.Header = Loc.T("Guide.ExportDocx");
        webItem.Header = Loc.T("Guide.ExportHtml");
        snippetItem.Header = Loc.T("Guide.ExportSnippet");
        SetStatus(statusKey);
        if (GuideView.IsVisible && doc is not null) RebuildSteps();
        if (ListView.IsVisible) ShowList();
    }

    // ---- List view -------------------------------------------------------------------------------------------

    void ShowList()
    {
        ListView.IsVisible = true;
        GuideView.IsVisible = false;
        doc = null;
        dirty = false;
        saveTimer.Stop();
        SetStatus("");
        GuideList.Children.Clear();
        IReadOnlyList<GuideSummary> guides;
        try
        {
            guides = store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            guides = [];
        }
        EmptyText.Text = Loc.T("Guide.Empty");
        EmptyText.IsVisible = guides.Count == 0;
        foreach (var guide in guides) GuideList.Children.Add(BuildRow(guide));
    }

    Border BuildRow(GuideSummary guide)
    {
        var row = new Border { Classes = { "card" }, Padding = new Thickness(14, 10) };
        var grid = new DockPanel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        var openButton = new Button { Content = Loc.T("Guide.Open"), Padding = new Thickness(14, 6) };
        openButton.Classes.Add("accent");
        openButton.Click += (_, _) => OpenGuide(guide.Id);
        var deleteButton = new Button { Content = Loc.T("Guide.Delete"), Padding = new Thickness(14, 6) };
        deleteButton.Click += (_, _) => _ = DeleteGuideAsync(guide);
        buttons.Children.Add(openButton);
        buttons.Children.Add(deleteButton);
        grid.Children.Add(buttons);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = guide.Title.Trim().Length == 0 ? Loc.T("Guide.Untitled") : guide.Title;
        text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = Loc.F("Guide.Edited", guide.Modified.LocalDateTime.ToString("g")),
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(1, 3, 0, 0),
        });
        grid.Children.Add(text);
        row.Child = grid;
        return row;
    }

    async Task DeleteGuideAsync(GuideSummary guide)
    {
        var title = guide.Title.Trim().Length == 0 ? Loc.T("Guide.Untitled") : guide.Title;
        bool confirmed = await Dialogs.Confirm(this, Loc.T("Guide.DeleteTitle"), Loc.F("Guide.DeleteConfirm", title),
            Loc.T("Guide.Delete"), Loc.T("Common.Cancel"));
        if (!confirmed) return;
        try
        {
            store.Delete(guide.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
        ShowList();
    }

    async void New_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var created = store.Create("");
            OpenGuide(created.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
    }

    // ---- Guide view -------------------------------------------------------------------------------------------

    void OpenGuide(Guid id)
    {
        if (!SaveNow()) return;
        GuideDocument loaded;
        try
        {
            loaded = store.Load(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _ = Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.OpenFailed"));
            return;
        }
        doc = loaded;
        loading = true;
        TitleBox.Text = doc.Title;
        IntroBox.Text = doc.Intro;
        loading = false;
        dirty = false;
        ListView.IsVisible = false;
        GuideView.IsVisible = true;
        SetStatus("Guide.Saved");
        RebuildSteps();
    }

    void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (SaveNow()) ShowList();
    }

    void Field_Changed(object? sender, TextChangedEventArgs e)
    {
        if (doc is null || loading) return;
        doc.Title = TitleBox.Text ?? "";
        doc.Intro = IntroBox.Text ?? "";
        MarkChanged();
    }

    // Any change restarts the 800 ms timer; the save itself happens when it runs out.
    void MarkChanged()
    {
        if (doc is null || loading) return;
        dirty = true;
        SetStatus("");
        saveTimer.Stop();
        saveTimer.Start();
    }

    // Writes the open guide when it has changes. Returns false when the write failed.
    bool SaveNow()
    {
        saveTimer.Stop();
        if (doc is null || !dirty) return true;
        try
        {
            store.Save(doc);
            dirty = false;
            SetStatus("Guide.Saved");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("Guide.SaveFailed");
            return false;
        }
    }

    void SetStatus(string key)
    {
        statusKey = key;
        StatusText.Text = key.Length == 0 ? "" : Loc.T(key);
    }

    void RebuildSteps()
    {
        StepPanel.Children.Clear();
        if (doc is null) return;
        for (int i = 0; i < doc.Steps.Count; i++) StepPanel.Children.Add(BuildCard(doc.Steps[i], i));
    }

    void RebuildCard(Guid id)
    {
        if (doc is null) return;
        int index = doc.Steps.FindIndex(s => s.Id == id);
        if (index >= 0 && index < StepPanel.Children.Count) StepPanel.Children[index] = BuildCard(doc.Steps[index], index);
    }

    GuideStep? FindStep(Guid id) => doc?.Steps.FirstOrDefault(s => s.Id == id);

    void FocusCardTitle(Guid id)
    {
        var card = StepPanel.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid g && g == id);
        card?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    // ---- Export -----------------------------------------------------------------------------------------------

    // The Export button opens this menu. Every item takes the guide as it is now, unsaved text included.
    void BuildExportMenu()
    {
        copyItem.Click += (_, _) => _ = CopyGuideAsync();
        pdfItem.Click += (_, _) => _ = SaveFileAsync("pdf", "Guide.PdfFilter", GuidePdf.Write);
        wordItem.Click += (_, _) => _ = SaveFileAsync("docx", "Guide.WordFilter", GuideDocx.Write);
        webItem.Click += (_, _) => _ = SaveFileAsync("html", "Guide.WebFilter", WriteHtml);
        snippetItem.Click += (_, _) => _ = AddSnippetAsync();
        var menu = new MenuFlyout();
        menu.Items.Add(copyItem);
        menu.Items.Add(pdfItem);
        menu.Items.Add(wordItem);
        menu.Items.Add(webItem);
        menu.Items.Add(snippetItem);
        ExportButton.Flyout = menu;
    }

    static void WriteHtml(ExportGuide guide, Stream stream)
    {
        var bytes = Encoding.UTF8.GetBytes(GuideExport.Document(guide));
        stream.Write(bytes, 0, bytes.Length);
    }

    // Copy guide: the whole guide as HTML on the clipboard (text/html, through wl-copy).
    async Task CopyGuideAsync()
    {
        if (doc is null) return;
        ExportGuide guide;
        try
        {
            guide = GuideExport.Prepare(doc);
        }
        catch (InvalidDataException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.NotAPicture"));
            return;
        }
        var html = Encoding.UTF8.GetBytes(GuideExport.Fragment(guide));
        try
        {
            await Task.Run(() => new WaylandClipboard().Set(html, ClipboardFormat.Html));
            SetStatus("Guide.Copied");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.ClipboardFailed"));
        }
    }

    // Save as PDF, Word or web page: the guide is read now, and the file dialog starts in Documents with its name.
    async Task SaveFileAsync(string extension, string filterKey, Action<ExportGuide, Stream> write)
    {
        if (doc is null) return;
        ExportGuide guide;
        try
        {
            guide = GuideExport.Prepare(doc);
        }
        catch (InvalidDataException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.NotAPicture"));
            return;
        }
        var picked = await FilePicker.SaveAsync(this, new PickRequest(Loc.T("Guide.ExportSaveTitle"), Loc.T(filterKey), ["*." + extension],
            FilePicker.DocumentsFolder, GuideExport.FileName(doc.Title, extension)));
        if (picked.Path is not { } path) return;
        try
        {
            await Task.Run(() =>
            {
                using var file = File.Create(path);
                write(guide, file);
            });
            SetStatus("Guide.Saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
    }

    // Save as snippet: the guide goes into the Linux library (Html = the HTML, Text = the plain text). The engine
    // reloads the library by itself.
    async Task AddSnippetAsync()
    {
        if (doc is null) return;
        var service = App.Expansion;
        if (service is null || !service.CanEdit)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), service?.LibraryError ?? Loc.T("Guide.SnippetNoLibrary"));
            return;
        }
        ExportGuide guide;
        try
        {
            guide = GuideExport.Prepare(doc);
        }
        catch (InvalidDataException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.NotAPicture"));
            return;
        }
        var trigger = await TriggerPrompt.AskAsync(this, GuideExport.SuggestedTrigger(doc.Title), service.Snapshot().Select(s => s.Trigger).ToList());
        if (trigger is null) return;
        try
        {
            service.Upsert(null, GuideExport.Snippet(guide, trigger));
            SetStatus("Guide.SnippetAdded");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
    }

    // ---- Step cards -------------------------------------------------------------------------------------------

    Border BuildCard(GuideStep step, int index)
    {
        int count = doc!.Steps.Count;
        var card = new Border { Classes = { "card" }, Padding = new Thickness(14, 12), Tag = step.Id };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*") };

        grid.Children.Add(new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
        });

        var body = new StackPanel { Spacing = 10 };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        // Title row with the card toolbar on the right
        var header = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(toolbar, Dock.Right);
        toolbar.Children.Add(ToolButton("IconUp", "Guide.MoveUp", index > 0, () => MoveStep(step.Id, -1)));
        toolbar.Children.Add(ToolButton("IconDown", "Guide.MoveDown", index < count - 1, () => MoveStep(step.Id, 1)));
        toolbar.Children.Add(ToolButton("IconCopy", "Guide.Duplicate", true, () => DuplicateStep(step.Id)));
        toolbar.Children.Add(ToolButton("IconDelete", "Guide.DeleteStep", true, () => _ = DeleteStepAsync(step.Id)));
        header.Children.Add(toolbar);

        var titleBox = new TextBox { Text = step.Title, Watermark = Loc.T("Guide.StepTitleWatermark"), FontWeight = FontWeight.SemiBold };
        titleBox.TextChanged += (_, _) => { step.Title = titleBox.Text ?? ""; MarkChanged(); };
        header.Children.Add(titleBox);
        body.Children.Add(header);

        var textBox = new TextBox
        {
            Text = step.Text,
            Watermark = Loc.T("Guide.StepTextWatermark"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
        };
        textBox.TextChanged += (_, _) => { step.Text = textBox.Text ?? ""; MarkChanged(); };
        body.Children.Add(textBox);

        body.Children.Add(step.Image is null ? BuildDropZone(step.Id) : BuildPicture(step));
        card.Child = grid;
        return card;
    }

    Button ToolButton(string icon, string tipKey, bool enabled, Action action)
    {
        var button = new Button
        {
            Classes = { "icon" },
            IsEnabled = enabled,
            Content = new PathIcon { Data = (Geometry)this.FindResource(icon)!, Width = 14, Height = 14 },
        };
        ToolTip.SetTip(button, Loc.T(tipKey));
        button.Click += (_, _) => action();
        return button;
    }

    // Empty step: a dashed box that takes a paste, a drop or a file.
    Control BuildDropZone(Guid id)
    {
        var grid = new Grid { MinHeight = 120 };
        var frame = new Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            StrokeThickness = 1.5,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 },
        };
        frame.Bind(Shape.StrokeProperty, this.GetResourceObservable("ButtonBorderBrush"));
        grid.Children.Add(frame);

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = Loc.T("Guide.DropHint"), Opacity = 0.75, TextAlignment = TextAlignment.Center });
        var choose = new Button { Content = Loc.T("Guide.Choose"), Padding = new Thickness(14, 6), HorizontalAlignment = HorizontalAlignment.Center };
        choose.Click += (_, _) => _ = ChooseFileAsync(id);
        content.Children.Add(choose);
        grid.Children.Add(content);
        return grid;
    }

    // Filled step: the picture scaled to the card width (Uniform keeps its shape), with Replace and Remove.
    Control BuildPicture(GuideStep step)
    {
        var panel = new StackPanel { Spacing = 8 };
        var bitmap = DecodeStepImage(step);
        if (bitmap is null)
            panel.Children.Add(new TextBlock { Text = Loc.T("Guide.NotAPicture"), Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        else
            panel.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var markUp = new Button { Content = Loc.T("Guide.MarkUp"), Padding = new Thickness(12, 5), IsEnabled = bitmap is not null };
        markUp.Click += (_, _) => _ = MarkUpAsync(step.Id);
        var replace = new Button { Content = Loc.T("Guide.Replace"), Padding = new Thickness(12, 5) };
        replace.Click += (_, _) => _ = ChooseFileAsync(step.Id);
        var remove = new Button { Content = Loc.T("Guide.Remove"), Padding = new Thickness(12, 5) };
        remove.Click += (_, _) => RemoveImage(step.Id);
        row.Children.Add(markUp);
        row.Children.Add(replace);
        row.Children.Add(remove);
        panel.Children.Add(row);
        return panel;
    }

    // The picture as the card shows it: with its crop and markup when there are any (GuideRender), else as stored.
    static Bitmap? DecodeStepImage(GuideStep step)
    {
        if (step.Image is null) return null;
        try
        {
            var bytes = GuideRender.HasMarkup(step) ? GuideRender.Flatten(step) : Convert.FromBase64String(step.Image);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            return null;
        }
    }

    // Opens the markup window for a step with a picture. Done stores the markup and the crop in the step; Cancel changes nothing.
    async Task MarkUpAsync(Guid id)
    {
        var step = FindStep(id);
        if (step?.Image is null) return;
        ImageMarkupWindow window;
        try
        {
            window = new ImageMarkupWindow(step);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.NotAPicture"));
            return;
        }
        bool done = await window.ShowDialog<bool>(this);
        if (!done || !window.Changed || FindStep(id) is null) return;
        step.Annotations = window.Annotations;
        step.Crop = window.Crop;
        RebuildCard(id);
        MarkChanged();
    }

    // ---- Step actions -----------------------------------------------------------------------------------------

    void AddStep_Click(object? sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var step = NewStep();
        doc.Steps.Add(step);
        RebuildSteps();
        MarkChanged();
        FocusCardTitle(step.Id);
    }

    static GuideStep NewStep() => new() { Id = Guid.NewGuid(), Annotations = new() };

    void MoveStep(Guid id, int delta)
    {
        if (doc is null) return;
        int index = doc.Steps.FindIndex(s => s.Id == id);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= doc.Steps.Count) return;
        (doc.Steps[index], doc.Steps[target]) = (doc.Steps[target], doc.Steps[index]);
        RebuildSteps();
        MarkChanged();
        FocusCardTitle(id);
    }

    void DuplicateStep(Guid id)
    {
        if (doc is null) return;
        int index = doc.Steps.FindIndex(s => s.Id == id);
        if (index < 0) return;
        var source = doc.Steps[index];
        var copy = new GuideStep
        {
            Id = Guid.NewGuid(),
            Title = source.Title,
            Text = source.Text,
            Image = source.Image,
            ImageType = source.ImageType,
            Annotations = source.Annotations.Select(a => a.Clone()).ToList(),
            Crop = source.Crop?.Clone(),
        };
        doc.Steps.Insert(index + 1, copy);
        RebuildSteps();
        MarkChanged();
    }

    async Task DeleteStepAsync(Guid id)
    {
        if (doc is null) return;
        int index = doc.Steps.FindIndex(s => s.Id == id);
        if (index < 0) return;
        bool confirmed = await Dialogs.Confirm(this, Loc.T("Guide.DeleteStepTitle"), Loc.F("Guide.DeleteStepConfirm", index + 1),
            Loc.T("Guide.Delete"), Loc.T("Common.Cancel"));
        if (!confirmed || doc is null) return;
        doc.Steps.RemoveAll(s => s.Id == id);
        RebuildSteps();
        MarkChanged();
    }

    void RemoveImage(Guid id)
    {
        var step = FindStep(id);
        if (step is null) return;
        step.Image = null;
        step.ImageType = null;
        RebuildCard(id);
        MarkChanged();
    }

    // Puts a picture into a step: scaled down when wider than 1600 px, then stored as base64.
    async Task<bool> SetImageAsync(GuideStep step, byte[] source)
    {
        try
        {
            var (data, type) = await Task.Run(() => GuideImage.Prepare(source));
            step.Image = Convert.ToBase64String(data);
            step.ImageType = type;
            return true;
        }
        catch (InvalidDataException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), Loc.T("Guide.NotAPicture"));
            return false;
        }
    }

    // A picture in a step that already exists: that card is rebuilt, the others stay as they are.
    async Task SetImageOnStepAsync(Guid id, byte[] source)
    {
        var step = FindStep(id);
        if (step is null) return;
        if (!await SetImageAsync(step, source)) return;
        RebuildCard(id);
        MarkChanged();
    }

    // A new step with a picture at the end of the guide (the quick Ctrl+V workflow).
    async Task AddStepWithImageAsync(byte[] source)
    {
        if (doc is null) return;
        var step = NewStep();
        if (!await SetImageAsync(step, source)) return;
        if (doc is null) return;
        doc.Steps.Add(step);
        RebuildSteps();
        MarkChanged();
    }

    async Task ChooseFileAsync(Guid id)
    {
        var picked = await FilePicker.OpenAsync(this, new PickRequest(Loc.T("Guide.ChoosePicture"), Loc.T("Guide.PictureFilter"), FilePicker.ImagePatterns, FilePicker.PicturesFolder));
        if (picked.Path is not { } path) return;
        await SetImageFromFileAsync(id, path);
    }

    async Task SetImageFromFileAsync(Guid id, string path)
    {
        try
        {
            await SetImageOnStepAsync(id, await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
        }
    }

    // ---- Paste, drop, shortcuts -------------------------------------------------------------------------------

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!GuideView.IsVisible || e.KeyModifiers != KeyModifiers.Control) return;
        if (e.Key == Key.S)
        {
            SaveNow();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            // Not handled here: a text box still pastes its text. A picture on the clipboard is taken as well.
            _ = PasteAsync();
        }
    }

    // A picture goes into the step that has focus; with no step focused it becomes a new step.
    // Pasting in the title or the intro box takes text only.
    async Task PasteAsync()
    {
        if (doc is null) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (IsInHeaderField(focused)) return;
        var target = StepIdAbove(focused);
        var picture = await ReadClipboardPictureAsync();
        if (picture is null || doc is null) return;
        if (target is { } id && FindStep(id) is not null) await SetImageOnStepAsync(id, picture);
        else await AddStepWithImageAsync(picture);
    }

    bool IsInHeaderField(Visual? visual)
    {
        for (var v = visual; v is not null; v = v.GetVisualParent())
            if (v == TitleBox || v == IntroBox) return true;
        return false;
    }

    Guid? StepIdAbove(Visual? visual)
    {
        for (var v = visual; v is not null; v = v.GetVisualParent())
            if (v is Border { Tag: Guid id } && FindStep(id) is not null) return id;
        return null;
    }

    // Avalonia's clipboard first (image/png, then image/jpeg). On Linux, when that gives nothing, wl-paste is asked.
    async Task<byte[]?> ReadClipboardPictureAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                var transfer = await clipboard.TryGetDataAsync();
                if (transfer is not null)
                {
                    foreach (var item in transfer.Items)
                        foreach (var format in PictureFormats)
                        {
                            var raw = await item.TryGetRawAsync(DataFormat.CreateBytesPlatformFormat(format));
                            var bytes = await RawToBytesAsync(raw);
                            if (bytes is { Length: > 0 }) return bytes;
                        }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            // The clipboard may refuse a read; the wl-paste route below still gets its chance.
        }
        return await Task.Run(WaylandPicture);
    }

    static async Task<byte[]?> RawToBytesAsync(object? raw)
    {
        switch (raw)
        {
            case byte[] bytes:
                return bytes;
            case Stream stream:
                using (stream)
                using (var copy = new MemoryStream())
                {
                    await stream.CopyToAsync(copy);
                    return copy.ToArray();
                }
            default:
                return null;
        }
    }

    static byte[]? WaylandPicture()
    {
        try
        {
            return new WaylandClipboard().ReadImage()?.Data;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return null; // wl-paste is not installed or the clipboard holds no picture
        }
    }

    // Drag over: files are accepted anywhere in the window.
    static void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.DataTransfer.TryGetFiles() is { } files && files.Any();
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // A picture dropped on a card goes into that step; anywhere else each picture becomes a new step.
    async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (doc is null) return;
        var paths = (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        var hit = this.InputHitTest(e.GetPosition(this)) as Visual;
        var target = StepIdAbove(hit);
        for (int i = 0; i < paths.Count; i++)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(paths[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Dialogs.Message(this, Loc.T("Guide.Title"), ex.Message);
                continue;
            }
            if (i == 0 && target is { } id) await SetImageOnStepAsync(id, bytes);
            else await AddStepWithImageAsync(bytes);
        }
    }
}
