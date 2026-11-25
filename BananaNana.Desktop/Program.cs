using Avalonia;
namespace BananaNana.Desktop;

// banananana [--tray] [--guide-maker | --guide-maker-sample]: --tray starts hidden in the tray. A second launch only
// brings the running window forward; with a guide-maker flag the running app opens the Guide maker instead.
// What to open at startup or on a forwarded request: nothing extra, the Guide maker, or the Guide maker with the sample guide.
public enum GuideStart { None, Window, Sample }

public static class Program
{
    public const string GuideMakerArgument = "--guide-maker";
    public const string GuideMakerSampleArgument = "--guide-maker-sample";

    public static bool StartInTray { get; private set; }
    public static GuideStart GuideStartup { get; private set; }
    public static SingleInstance? Listener { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        GuideStartup = args.Contains(GuideMakerSampleArgument) ? GuideStart.Sample
            : args.Contains(GuideMakerArgument) ? GuideStart.Window
            : GuideStart.None;
        var command = GuideStartup switch
        {
            GuideStart.Sample => SingleInstance.GuideSampleCommand,
            GuideStart.Window => SingleInstance.GuideCommand,
            _ => SingleInstance.ShowCommand,
        };
        if (SingleInstance.TryNotifyExisting(command)) return 0;
        StartInTray = args.Contains(Autostart.TrayArgument);
        Listener = SingleInstance.TryListen();
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
