using System.Windows;
using Forms = System.Windows.Forms;
namespace BananaNana;

// Notification-area icon: left click opens the window; the menu offers Open, Pause, Start with Windows, Exit.
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripMenuItem openItem, pauseItem, autostartItem, exitItem;
    bool paused;

    public event Action? OpenRequested, PauseToggled, ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        openItem = new Forms.ToolStripMenuItem("", null, (_, _) => OpenRequested?.Invoke());
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        pauseItem = new Forms.ToolStripMenuItem("", null, (_, _) => PauseToggled?.Invoke());
        autostartItem = new Forms.ToolStripMenuItem("", null, (_, _) => Autostart.Enabled = !Autostart.Enabled);
        exitItem = new Forms.ToolStripMenuItem("", null, (_, _) => ExitRequested?.Invoke());
        menu.Items.AddRange([openItem, pauseItem, autostartItem, new Forms.ToolStripSeparator(), exitItem]);
        menu.Opening += (_, _) => autostartItem.Checked = Autostart.Enabled;

        // The .ico is embedded as a WPF resource; ask for the small-icon size so it stays crisp at 100-200 % scaling.
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/BananaNana.ico")).Stream;
        icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
        Loc.Changed += Relabel;
        Relabel();
    }

    void Relabel()
    {
        openItem.Text = Loc.T("Tray.Open");
        pauseItem.Text = Loc.T("Tray.Pause");
        autostartItem.Text = Loc.T("Tray.Autostart");
        exitItem.Text = Loc.T("Tray.Exit");
        icon.Text = paused ? Loc.T("Tray.Paused") : "Banana Nana";
    }

    public void SetPaused(bool value)
    {
        paused = value;
        pauseItem.Checked = value;
        Relabel();
    }

    public void ShowHint(string title, string text) => icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        Loc.Changed -= Relabel;
        icon.Visible = false;
        icon.Dispose();
    }
}
