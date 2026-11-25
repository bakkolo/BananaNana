using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace BananaNana.Desktop;

// Small modal dialogs (Avalonia has no built-in message box). Returns true when the accept button was chosen.
public static class Dialogs
{
    public static Task<bool> Confirm(Window owner, string title, string text, string okText, string cancelText) =>
        Show(owner, title, text, okText, cancelText);

    public static Task<bool> Message(Window owner, string title, string text) =>
        Show(owner, title, text, Loc.T("Common.OK"), null);

    static async Task<bool> Show(Window owner, string title, string text, string okText, string? cancelText)
    {
        bool accepted = false;
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = App.Icon,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, Padding = new Thickness(14, 6) };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = okText, Padding = new Thickness(14, 6) };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => { accepted = true; dialog.Close(); };
        buttons.Children.Add(ok);

        var body = new StackPanel { Margin = new Thickness(22, 18), Spacing = 18 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(buttons);
        dialog.Content = body;

        await dialog.ShowDialog(owner);
        return accepted;
    }
}
