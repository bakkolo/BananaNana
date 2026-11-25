using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TheArtOfDev.HtmlRenderer.Avalonia;
using TheArtOfDev.HtmlRenderer.Core.Entities;
namespace BananaNana.Desktop;

// One list row: the trigger and a one-line summary of its text.
public sealed record SnippetRow(string Trigger, string Summary);

public partial class MainWindow : Window
{
    const int TipCount = 8;
    static readonly IBrush StatusOn = new SolidColorBrush(Color.Parse("#2EA043"));
    static readonly IBrush StatusOff = new SolidColorBrush(Color.Parse("#8A8480"));
    readonly ExpansionService service;
    int tip = Random.Shared.Next(TipCount);

    public MainWindow(ExpansionService service)
    {
        this.service = service;
        InitializeComponent();
        Icon = App.Icon;
        Preview.ImageLoad += OnPreviewImageLoad;
        service.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        Retranslate();
        Refresh();
    }

    // The close button hides the window to the tray, so snippets keep expanding. Exit in the tray menu really quits.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.Exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    void Retranslate()
    {
        SearchBox.Watermark = Loc.T("Main.Search");
        ToolTip.SetTip(ImportButton, Loc.T("Main.Import"));
        ToolTip.SetTip(ExportButton, Loc.T("Main.Export"));
        ToolTip.SetTip(GuideButton, Loc.T("Guide.Title"));
        ToolTip.SetTip(SettingsButton, Loc.T("Main.Settings"));
        ToolTip.SetTip(HelpButton, Loc.T("Main.Help"));
        ToolTip.SetTip(PauseButton, Loc.T("Main.PauseTip"));
        NewLabel.Text = Loc.T("Main.New");
        EditLabel.Text = Loc.T("Main.Edit");
        DeleteLabel.Text = Loc.T("Main.Delete");
        NextTipButton.Content = Loc.T("Main.NextTip");
        PreviewCaption.Text = Loc.T("Main.PreviewCaption");
        BannerTitle.Text = Loc.T("Linux.Banner.Title");
        BannerBody.Text = Loc.T("Linux.Banner.Body");
        EmptyText.Text = Loc.T("Main.Empty");
        ShowTip();
        Refresh();
    }

    void Refresh()
    {
        var snippets = service.Snapshot();
        var filter = SearchBox.Text ?? "";
        var selected = (SnippetList.SelectedItem as SnippetRow)?.Trigger;
        var rows = snippets
            .Where(s => s.Trigger.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Trigger, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SnippetRow(s.Trigger, Summary(s)))
            .ToList();
        SnippetList.ItemsSource = rows;
        SnippetList.SelectedItem = rows.FirstOrDefault(r => r.Trigger == selected) ?? rows.FirstOrDefault();
        EmptyText.IsVisible = snippets.Count == 0;

        Title = "Banana Nana | " + service.LibraryName;
        LibraryText.Text = $"{service.LibraryName} · {Loc.F("Main.SnippetCount", snippets.Count)}";

        // Status line and dot use the Windows wording: green while expansion is on, grey when paused or not working.
        var problem = service.Problem;
        string status;
        if (service.LibraryError is { } libraryError) status = Loc.F("Main.LibraryError", libraryError);
        else if (problem is not null) status = Loc.F("Status.Error", problem);
        else status = Loc.T(service.Paused ? "Status.Paused" : "Status.On");
        StatusText.Text = status;
        StatusDot.Fill = problem is null && !service.Paused && service.LibraryError is null ? StatusOn : StatusOff;
        Banner.IsVisible = problem is not null;

        PauseLabel.Text = Loc.T(service.Paused ? "Main.Resume" : "Main.Pause");
        PauseIcon.Data = this.FindResource(service.Paused ? "IconPlay" : "IconPause") as Geometry;
        NewButton.IsEnabled = service.CanEdit;
        ImportButton.IsEnabled = service.CanEdit;
        ExportButton.IsEnabled = service.CanEdit;
        ShowPreview();
    }

    static string Summary(HtmlSnippet snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet.Text))
            return Loc.T(SnippetContent.HasImages(snippet.Html) ? "Main.SummaryImage" : "Main.SummaryEmpty");
        return SnippetContent.Summary(snippet.Text);
    }

    HtmlSnippet? Selected() => (SnippetList.SelectedItem as SnippetRow) is { } row ? service.Find(row.Trigger) : null;

    void ShowPreview()
    {
        var snippet = Selected();
        PreviewTitle.Text = snippet?.Trigger ?? "";
        Preview.Text = PreviewHtml(snippet);
        bool active = snippet is not null && service.CanEdit;
        EditButton.IsEnabled = active;
        DeleteButton.IsEnabled = active;
    }

    // The rich HTML when the snippet has it; plain text is shown as paragraphs so the preview looks the same either way.
    static string PreviewHtml(HtmlSnippet? snippet)
    {
        if (snippet is null) return "";
        if (!string.IsNullOrEmpty(snippet.Html)) return snippet.Html;
        return "<p>" + WebUtility.HtmlEncode(snippet.Text).Replace("\n", "<br>") + "</p>";
    }

    // The renderer may not decode inline pictures itself: data:image/...;base64 URIs are decoded here.
    static void OnPreviewImageLoad(object? sender, HtmlRendererRoutedEventArgs<HtmlImageLoadEventArgs> e)
    {
        var args = e.Event;
        const string prefix = "data:";
        if (!args.Src.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        int comma = args.Src.IndexOf(',');
        if (comma < 0 || !args.Src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(args.Src[(comma + 1)..]));
            args.Callback(new Bitmap(stream));
            args.Handled = true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // A picture that cannot be decoded stays as the renderer shows it (its error icon).
        }
    }

    void ShowTip() => TipText.Text = Loc.T($"Tip.{tip + 1}");

    void Search_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e) => Refresh();

    void List_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowPreview();

    void List_DoubleTapped(object? sender, TappedEventArgs e) => _ = EditSelected();

    void NextTip_Click(object? sender, RoutedEventArgs e)
    {
        tip = (tip + 1) % TipCount;
        ShowTip();
    }

    void Pause_Click(object? sender, RoutedEventArgs e) => service.Paused = !service.Paused;

    void Guide_Click(object? sender, RoutedEventArgs e) => GuideMakerWindow.ShowOrActivate();

    void Settings_Click(object? sender, RoutedEventArgs e) => _ = new SettingsWindow(service).ShowDialog(this);

    void Help_Click(object? sender, RoutedEventArgs e) => new HelpWindow().Show(this);

    void New_Click(object? sender, RoutedEventArgs e) => _ = OpenEditor(null);

    void Edit_Click(object? sender, RoutedEventArgs e) => _ = EditSelected();

    Task EditSelected() => Selected() is { } snippet ? OpenEditor(snippet) : Task.CompletedTask;

    async Task OpenEditor(HtmlSnippet? snippet)
    {
        await new EditWindow(service, snippet).ShowDialog(this);
        Refresh();
    }

    // Import: choose a Windows BananaNana.library.json; the report says what came over.
    async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var picked = await FilePicker.OpenAsync(this, new PickRequest(Loc.T("Main.ImportTitle"), Loc.T("Main.LibraryFilter"), ["*.json"], FilePicker.DocumentsFolder));
        if (picked.Path is not { } path) return;
        try
        {
            var report = service.Import(path);
            await Dialogs.Message(this, Loc.T("Main.ImportTitle"), ReportText(report));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
        {
            await Dialogs.Message(this, Loc.T("Main.ImportTitle"), ex.Message);
        }
        Refresh();
    }

    // Export: save a copy of the Linux library file.
    async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var picked = await FilePicker.SaveAsync(this, new PickRequest(Loc.T("Main.Export"), Loc.T("Main.LibraryFilter"), ["*.json"], FilePicker.DocumentsFolder, LinuxLibrary.FileName));
        if (picked.Path is not { } path) return;
        try
        {
            service.Export(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Message(this, Loc.T("Main.Export"), ex.Message);
        }
    }

    // The report is shown in English for now: its wording has no key in the shared Strings files yet.
    static string ReportText(ImportReport report)
    {
        var lines = new List<string> { $"Imported: {report.Imported.Count}", $"Skipped: {report.Skipped.Count}", $"Failed: {report.Failed.Count}" };
        lines.AddRange(report.Skipped.Select(i => $"  {i.Trigger}: {i.Reason}"));
        lines.AddRange(report.Failed.Select(i => $"  {i.Trigger}: {i.Reason}"));
        return string.Join(Environment.NewLine, lines);
    }

    async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected() is not { } snippet) return;
        bool confirmed = await Dialogs.Confirm(this, Loc.T("Main.DeleteTitle"), Loc.F("Main.DeleteConfirm", snippet.Trigger),
            Loc.T("Main.Delete"), Loc.T("Common.Cancel"));
        if (!confirmed) return;
        try
        {
            service.Remove(snippet.Trigger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            await Dialogs.Message(this, "Banana Nana", ex.Message);
        }
        Refresh();
    }
}
