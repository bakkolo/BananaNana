using Avalonia.Controls;
using Avalonia.Interactivity;
namespace BananaNana.Desktop;

// Changes only the trigger; the text and any formatting stay as they are.
public partial class RenameWindow : Window
{
    readonly ExpansionService service;
    readonly HtmlSnippet original;

    public RenameWindow(ExpansionService service, HtmlSnippet original)
    {
        this.service = service;
        this.original = original;
        InitializeComponent();
        TriggerBox.Text = original.Trigger;
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        Retranslate();
    }

    void Retranslate()
    {
        Title = Loc.T("Main.Rename");
        TriggerLabel.Text = Loc.T("Editor.Trigger");
        TriggerHint.Text = Loc.T("Editor.TriggerHint");
        CancelButton.Content = Loc.T("Common.Cancel");
        SaveButton.Content = Loc.T("Common.Save");
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    void Save_Click(object? sender, RoutedEventArgs e)
    {
        var trigger = TriggerRules.Normalize(TriggerBox.Text);
        var error = TriggerRules.Validate(trigger, service.Snapshot().Select(s => s.Trigger), original.Trigger);
        if (error is { } problem)
        {
            ErrorText.Text = TriggerRules.Message(problem);
            return;
        }
        try
        {
            service.Upsert(original.Trigger, new HtmlSnippet { Trigger = trigger, Html = original.Html, Text = original.Text });
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
