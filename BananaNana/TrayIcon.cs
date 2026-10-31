using System.Windows;
using Forms = System.Windows.Forms;
namespace BananaNana;

// Notification-area icon: left click opens the window; the menu offers Open, Pause, Start with Windows, Exit.
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripMenuItem pauseItem, autostartItem;

    public event Action? OpenRequested, PauseToggled, ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Open Banana Nana", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        pauseItem = new Forms.ToolStripMenuItem("Pause expansion", null, (_, _) => PauseToggled?.Invoke());
        autostartItem = new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => Autostart.Enabled = !Autostart.Enabled);
        menu.Items.AddRange([open, pauseItem, autostartItem, new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke())]);
        menu.Opening += (_, _) => autostartItem.Checked = Autostart.Enabled;

        // The .ico is embedded as a WPF resource; ask for the small-icon size so it stays crisp at 100-200 % scaling.
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/BananaNana.ico")).Stream;
        icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Banana Nana",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public void SetPaused(bool paused)
    {
        pauseItem.Checked = paused;
        icon.Text = paused ? "Banana Nana (paused)" : "Banana Nana";
    }

    public void ShowHint(string title, string text) => icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
