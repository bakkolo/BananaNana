using Avalonia;
namespace BananaNana.Desktop;

// banananana [--tray] [--guide-maker | --guide-maker-sample | --guide-markup-demo | --guide-markup-demo-edit]: --tray starts
// hidden in the tray. A second launch only brings the running window forward; with a guide-maker flag the running app
// opens the Guide maker instead. What to open at startup or on a forwarded request: nothing extra, the Guide maker, the
// Guide maker with the sample guide, or with the markup demo guide (edit: its step in the markup window as well).
public enum GuideStart { None, Window, Sample, MarkupDemo, MarkupDemoEdit }

public static class Program
{
    public const string GuideMakerArgument = "--guide-maker";
    public const string GuideMakerSampleArgument = "--guide-maker-sample";
    public const string GuideMarkupDemoArgument = "--guide-markup-demo";
    public const string GuideMarkupDemoEditArgument = "--guide-markup-demo-edit";

    public static bool StartInTray { get; private set; }
    public static GuideStart GuideStartup { get; private set; }
    public static SingleInstance? Listener { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains(GuideExportDemo.Argument)) return GuideExportDemo.Run(args); // standalone, no window
        GuideStartup = args.Contains(GuideMarkupDemoEditArgument) ? GuideStart.MarkupDemoEdit
            : args.Contains(GuideMarkupDemoArgument) ? GuideStart.MarkupDemo
            : args.Contains(GuideMakerSampleArgument) ? GuideStart.Sample
            : args.Contains(GuideMakerArgument) ? GuideStart.Window
            : GuideStart.None;
        var command = GuideStartup switch
        {
            GuideStart.Sample => SingleInstance.GuideSampleCommand,
            GuideStart.Window => SingleInstance.GuideCommand,
            GuideStart.MarkupDemo => SingleInstance.GuideMarkupDemoCommand,
            GuideStart.MarkupDemoEdit => SingleInstance.GuideMarkupDemoEditCommand,
            _ => SingleInstance.ShowCommand,
        };
        if (SingleInstance.TryNotifyExisting(command)) return 0;
        StartInTray = args.Contains(Autostart.TrayArgument);
        Listener = SingleInstance.TryListen();
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
