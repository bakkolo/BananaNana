using Avalonia;
namespace BananaNana.Desktop;

// banananana [--tray]: --tray starts hidden in the tray. A second launch only brings the running window forward.
public static class Program
{
    public static bool StartInTray { get; private set; }
    public static SingleInstance? Listener { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (SingleInstance.TryNotifyExisting()) return 0;
        StartInTray = args.Contains(Autostart.TrayArgument);
        Listener = SingleInstance.TryListen();
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
