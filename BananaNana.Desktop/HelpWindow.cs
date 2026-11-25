using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace BananaNana.Desktop;

// The guide and tips of the Windows Help window, built from the same strings.
public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Width = 640;
        Height = 680;
        MinWidth = 460;
        MinHeight = 420;
        Icon = App.Icon;
        Loc.Changed += Build;
        Closed += (_, _) => Loc.Changed -= Build;
        Build();
    }

    void Build()
    {
        Title = Loc.T("Help.Title");

        var steps = new StackPanel { Spacing = 10 };
        for (int i = 1; i <= 5; i++)
            steps.Children.Add(Card(Loc.T($"Help.S{i}.Title"), Loc.T($"Help.S{i}.Body"), i));

        var tips = new StackPanel { Spacing = 6 };
        tips.Children.Add(new TextBlock { Text = Loc.T("Help.TipsTitle"), FontWeight = FontWeight.SemiBold, FontSize = 15 });
        for (int i = 1; i <= 8; i++)
            tips.Children.Add(new TextBlock { Text = Loc.T($"Tip.{i}"), TextWrapping = TextWrapping.Wrap });
        var tipCard = new Border { Padding = new Thickness(16, 14), Child = tips };
        tipCard.Classes.Add("card");
        steps.Children.Add(tipCard);

        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = Loc.T("Help.Title"), FontSize = 24, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = Loc.T("Help.Intro"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });

        var gotIt = new Button { Content = Loc.T("Help.GotIt"), Padding = new Thickness(22, 7), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        gotIt.Classes.Add("accent");
        gotIt.Click += (_, _) => Close();

        var dock = new DockPanel { Margin = new Thickness(24, 18) };
        DockPanel.SetDock(gotIt, Dock.Bottom);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(gotIt);
        dock.Children.Add(header);
        dock.Children.Add(new ScrollViewer { Content = steps });
        Content = dock;
    }

    static Border Card(string title, string body, int number)
    {
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = $"{number}. {title}", FontWeight = FontWeight.SemiBold, FontSize = 15 });
        text.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        var card = new Border { Padding = new Thickness(16, 14), Child = text };
        card.Classes.Add("card");
        return card;
    }
}
