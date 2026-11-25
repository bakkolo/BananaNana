using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace BananaNana.Desktop;

// Asks for the trigger of a new snippet, with the editor's rules (TriggerRules): not empty, no spaces, unique ignoring
// case. OK stays off until the trigger is valid. Returns the trigger, or null when cancelled.
public static class TriggerPrompt
{
    public static async Task<string?> AskAsync(Window owner, string suggested, IReadOnlyList<string> existing)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = Loc.T("Guide.SnippetTitle"),
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = App.Icon,
        };

        var hint = new TextBlock { Text = Loc.T("Guide.SnippetHint"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75 };
        var box = new TextBox { Text = suggested, Watermark = Loc.T("Editor.Trigger") };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cancel = new Button { Content = Loc.T("Common.Cancel"), Padding = new Thickness(14, 6) };
        var ok = new Button { Content = Loc.T("Guide.SnippetAdd"), Padding = new Thickness(14, 6) };
        ok.Classes.Add("accent");

        void Check()
        {
            var trigger = TriggerRules.Normalize(box.Text);
            var problem = TriggerRules.Validate(trigger, existing);
            error.Text = problem is { } p ? TriggerRules.Message(p) : "";
            ok.IsEnabled = problem is null;
        }

        box.TextChanged += (_, _) => Check();
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) =>
        {
            result = TriggerRules.Normalize(box.Text);
            dialog.Close();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 12,
            Children = { hint, box, error, buttons },
        };
        Check();

        await dialog.ShowDialog(owner);
        return result;
    }
}
