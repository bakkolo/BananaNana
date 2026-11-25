using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace BananaNana.Desktop;

public partial class SettingsWindow : Window
{
    readonly ExpansionService service;
    bool languageReady;

    public SettingsWindow(ExpansionService service)
    {
        this.service = service;
        InitializeComponent();
        LanguageBox.ItemsSource = Loc.Languages.Select(l => l.Name).ToList();
        LanguageBox.SelectedIndex = Array.FindIndex(Loc.Languages, l => l.Code == Loc.Current);
        languageReady = true;
        AutostartBox.IsChecked = Autostart.IsEnabled();
        StartPausedBox.IsChecked = App.Settings.StartPaused;
        Loc.Changed += Retranslate;
        Closed += (_, _) => Loc.Changed -= Retranslate;
        Retranslate();
    }

    void Retranslate()
    {
        Title = Loc.T("Set.Title");
        Heading.Text = Loc.T("Set.Title");
        LanguageLabel.Text = Loc.T("Set.Language");
        LanguageHint.Text = Loc.T("Set.LanguageHint");
        AutostartLabel.Text = Loc.T("Linux.Autostart");
        AutostartHint.Text = Loc.T("Set.AutostartHint");
        StartPausedLabel.Text = Loc.T("Linux.StartPaused");
        LibraryName.Text = Loc.F("Set.Library", service.LibraryName);
        LibraryPath.Text = service.LibraryPath;
        OpenFolderButton.Content = Loc.T("Set.OpenFolder");
        CloseButton.Content = Loc.T("Common.Close");
    }

    void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!languageReady || LanguageBox.SelectedIndex < 0) return;
        var code = Loc.Languages[LanguageBox.SelectedIndex].Code;
        if (code == Loc.Current) return;
        Loc.Apply(code);
        App.Settings.Language = code;
        App.Settings.Save();
    }

    void Autostart_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The program path is unknown.");
            Autostart.SetEnabled(AutostartBox.IsChecked == true, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorText.Text = Loc.T("Linux.AutostartFailed") + ex.Message;
            AutostartBox.IsChecked = Autostart.IsEnabled();
        }
    }

    void StartPaused_Click(object? sender, RoutedEventArgs e)
    {
        App.Settings.StartPaused = StartPausedBox.IsChecked == true;
        App.Settings.Save();
    }

    void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(service.LibraryPath) ?? "/";
        if (!Directory.Exists(folder))
        {
            ErrorText.Text = Loc.T("Set.NoFolder") + folder;
            return;
        }
        try { Process.Start(new ProcessStartInfo("xdg-open", folder) { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ErrorText.Text = ex.Message; }
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
