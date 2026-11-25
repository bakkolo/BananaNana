using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
namespace BananaNana.Desktop;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static WindowIcon? Icon { get; private set; }
    // True once Exit was chosen: the main window then really closes instead of hiding to the tray.
    public static bool Exiting { get; private set; }

    ExpansionService? expansion;
    SystemTheme? systemTheme;
    MainWindow? main;
    TrayIcon? tray;
    NativeMenuItem? openItem, guideItem, pauseItem, exitItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window hides it to the tray; only Exit ends the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            Settings = AppSettings.Load();
            Loc.Apply(Settings.Language);

            // Light or dark follows the desktop, and switches while the app runs. Set before any window opens.
            systemTheme = new SystemTheme();
            RequestedThemeVariant = SystemTheme.Current();
            systemTheme.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                var variant = SystemTheme.Current();
                if (RequestedThemeVariant != variant) RequestedThemeVariant = variant;
            });
            Icon = new WindowIcon(LoadIcon()); // the window icon keeps the bitmap, so it is not disposed here

            expansion = new ExpansionService(LinuxLibrary.DefaultPath, Settings.StartPaused);
            expansion.Changed += () => Dispatcher.UIThread.Post(UpdateTray);
            main = new MainWindow(expansion);
            BuildTray();
            Loc.Changed += UpdateTray;

            if (Program.Listener is { } listener) listener.OnShow = () => Dispatcher.UIThread.Post(ShowMain);
            if (!Program.StartInTray) main.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ShowMain()
    {
        if (Current is App app && app.main is { } window)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }

    public static void ExitApp()
    {
        Exiting = true;
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    void BuildTray()
    {
        openItem = new NativeMenuItem();
        openItem.Click += (_, _) => ShowMain();
        guideItem = new NativeMenuItem();
        guideItem.Click += (_, _) => GuideMakerWindow.ShowOrActivate();
        pauseItem = new NativeMenuItem();
        pauseItem.Click += (_, _) => { if (expansion is not null) expansion.Paused = !expansion.Paused; };
        exitItem = new NativeMenuItem();
        exitItem.Click += (_, _) => ExitApp();

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(guideItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        tray = new TrayIcon { Icon = Icon, Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
        UpdateTray();
    }

    void UpdateTray()
    {
        if (tray is null || expansion is null) return;
        bool paused = expansion.Paused;
        openItem!.Header = Loc.T("Tray.Open");
        guideItem!.Header = Loc.T("Guide.Title");
        pauseItem!.Header = Loc.T(paused ? "Tray.Resume" : "Tray.Pause");
        exitItem!.Header = Loc.T("Tray.Exit");
        tray.ToolTipText = Loc.T(paused ? "Tray.Paused" : "Tray.Open");
    }

    void Cleanup()
    {
        systemTheme?.Dispose();
        tray?.Dispose();
        expansion?.Dispose();
        Program.Listener?.Dispose();
    }

    static Bitmap LoadIcon()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("banananana.png")
            ?? throw new FileNotFoundException("The embedded icon is missing.");
        return new Bitmap(stream);
    }
}
