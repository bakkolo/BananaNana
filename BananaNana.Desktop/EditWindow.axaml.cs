using Avalonia.Controls;
using Avalonia.Interactivity;
namespace BananaNana.Desktop;

// Edits one snippet's trigger and text. The text box holds plain text. A snippet with formatting or pictures
// keeps them only while its text is unchanged; changing the text saves it as plain text, and the button says so.
public partial class EditWindow : Window
{
    readonly ExpansionService service;
    readonly HtmlSnippet? original;
    readonly bool rich;

    public EditWindow(ExpansionService service, HtmlSnippet? snippet)
    {
        this.service = service;
        original = snippet;
        rich = snippet is not null && (SnippetContent.HasFormatting(snippet.Html) || SnippetContent.HasImages(snippet.Html));
        InitializeComponent();
        TriggerBox.Text = snippet?.Trigger ?? "";
        TextEditor.Text = snippet?.Text ?? "";
        TextEditor.TextChanged += (_, _) => UpdateSaveButton();
        NoticeBorder.IsVisible = rich;
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        Retranslate();
    }

    void Retranslate()
    {
        Title = Loc.T(original is null ? "Main.New" : "Edit.TitleEdit");
        TriggerLabel.Text = Loc.T("Editor.Trigger");
        TriggerHint.Text = Loc.T("Editor.TriggerHint");
        Notice.Text = Loc.T("Edit.FormattingNotice");
        CancelButton.Content = Loc.T("Common.Cancel");
        UpdateSaveButton();
    }

    void UpdateSaveButton() =>
        SaveButton.Content = Loc.T(rich && TextEditor.Text != original?.Text ? "Edit.SavePlain" : "Common.Save");

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    void Save_Click(object? sender, RoutedEventArgs e)
    {
        var trigger = TriggerRules.Normalize(TriggerBox.Text);
        var error = TriggerRules.Validate(trigger, service.Snapshot().Select(s => s.Trigger), original?.Trigger);
        if (error is { } problem)
        {
            ErrorText.Text = TriggerRules.Message(problem);
            return;
        }

        var text = TextEditor.Text ?? "";
        bool textChanged = original is null || text != original.Text;
        // Unchanged text keeps the rich HTML as it is; changed text is saved as plain text.
        var html = textChanged ? "" : original?.Html ?? "";
        try
        {
            service.Upsert(original?.Trigger, new HtmlSnippet { Trigger = trigger, Html = html, Text = text });
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
