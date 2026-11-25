using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
namespace BananaNana.Desktop;

// One list row: the trigger and a one-line summary of its text.
public sealed record SnippetRow(string Trigger, string Summary);

public partial class MainWindow : Window
{
    const int TipCount = 8;
    readonly ExpansionService service;
    int tip = Random.Shared.Next(TipCount);

    public MainWindow(ExpansionService service)
    {
        this.service = service;
        InitializeComponent();
        Icon = App.Icon;
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
        SettingsButton.Content = Loc.T("Main.Settings");
        HelpButton.Content = Loc.T("Main.Help");
        NewButton.Content = Loc.T("Main.New");
        EditButton.Content = Loc.T("Main.Edit");
        RenameButton.Content = Loc.T("Main.Rename");
        DeleteButton.Content = Loc.T("Main.Delete");
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

        var problem = service.Problem;
        Banner.IsVisible = problem is not null;
        StatusText.Text = service.LibraryError is { } libraryError ? Loc.F("Main.LibraryError", libraryError)
            : problem is not null ? Loc.F("Status.Error", problem)
            : service.Paused ? Loc.T("Status.Paused")
            : Loc.F("Status.Listening", service.KeyboardCount);
        PauseButton.Content = Loc.T(service.Paused ? "Main.Resume" : "Main.Pause");
        NewButton.IsEnabled = service.CanEdit;
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
        Preview.Text = snippet?.Text ?? "";
        var notes = new List<string>();
        if (snippet is not null && SnippetContent.HasFormatting(snippet.Html)) notes.Add(Loc.T("Main.HasFormatting"));
        if (snippet is not null && SnippetContent.HasImages(snippet.Html)) notes.Add(Loc.T("Main.HasImages"));
        Notes.Text = string.Join("  ·  ", notes);
        bool active = snippet is not null && service.CanEdit;
        EditButton.IsEnabled = active;
        RenameButton.IsEnabled = active;
        DeleteButton.IsEnabled = active;
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

    async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected() is not { } snippet) return;
        await new RenameWindow(service, snippet).ShowDialog(this);
        Refresh();
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
