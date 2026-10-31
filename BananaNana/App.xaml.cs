using System.Windows;
namespace BananaNana;

public partial class App : Application
{
    Mutex? instance;
    EventWaitHandle? showRequest;

    // One instance per user session: a second launch (e.g. autostart + double-click) only asks the running
    // one to show its window, so two copies never react to the same trigger. BANANANANA_INSTANCE gives a
    // separate, isolated instance name (used to test a copy while another one is running).
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var name = Environment.GetEnvironmentVariable("BANANANANA_INSTANCE") is { Length: > 0 } custom ? custom : "BananaNana";
        instance = new Mutex(true, $@"Local\{name}.Instance", out bool first);
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        if (!first)
        {
            showRequest.Set();
            Shutdown();
            return;
        }
        var settings = AppSettings.Load();
        Loc.Apply(settings.Language);
        var window = new MainWindow(settings);
        ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) => Dispatcher.BeginInvoke(window.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
        if (e.Args.Contains(Autostart.TrayArgument)) return;
        window.Show();
        // First start: open the guide (with the language picker) once.
        if (!settings.WelcomeShown)
        {
            settings.WelcomeShown = true;
            settings.Save();
            window.ShowHelp(welcome: true);
        }
    }
}
