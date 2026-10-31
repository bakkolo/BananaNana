using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using BananaNana.Input;
namespace BananaNana.Paste;

// Pastes rendered content into the focused app: snapshot clipboard, set ours, send Ctrl+V, restore.
// Must be called on the WPF UI (STA) thread. WPF's Clipboard calls already retry while another app
// holds the clipboard open (10 tries, 100 ms apart), so there is no extra retry loop here.
public sealed class ClipboardPasteService
{
    // How long the target app gets to read the clipboard before the old content returns.
    // Apps that read asynchronously (e.g. Teams) may need more; make this per-app in Stage 5 settings.
    public TimeSpan RestoreDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    // backspaces: how many typed characters (trigger + separator) to delete before pasting.
    public async Task PasteAsync(RenderedContent content, int backspaces = 0)
    {
        var snapshot = Snapshot();
        Clipboard.SetDataObject(ToDataObject(content), copy: true);
        uint ours = GetClipboardSequenceNumber();
        if (!InputInjector.BackspacesThenPaste(backspaces))
            throw new InvalidOperationException("SendInput was blocked; the target app is probably elevated (UIPI).");
        // Large pastes take the target longer to read (Teams reads asynchronously): +1 ms per KB on the clipboard.
        await Task.Delay(RestoreDelay + TimeSpan.FromMilliseconds((content.Html.Length + content.Rtf.Length) / 1024.0));
        // Best effort: restore only if nobody else changed the clipboard meanwhile (e.g. the user copied something).
        if (GetClipboardSequenceNumber() != ours) return;
        if (snapshot.GetFormats(false).Length == 0) Clipboard.Clear();
        else Clipboard.SetDataObject(snapshot, copy: true);
    }

    // Plain text for apps like Notepad; Word and Teams use the HTML (docs/COMPAT.md).
    public static DataObject ToDataObject(RenderedContent content)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, content.PlainText, false);
        data.SetData(DataFormats.Rtf, content.Rtf, false);
        // A MemoryStream is written byte for byte, which keeps the CF_HTML offsets valid.
        data.SetData(DataFormats.Html, new MemoryStream(CfHtml.Build(content.Html)), false);
        return data;
    }

    static DataObject Snapshot()
    {
        var copy = new DataObject();
        var current = Clipboard.GetDataObject();
        if (current == null) return copy;
        foreach (var format in current.GetFormats(false))
        {
            try
            {
                if (current.GetData(format, false) is { } value) copy.SetData(format, value, false);
            }
            // Some formats (delay-rendered, GDI handles, private formats) cannot be read back; skip them.
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException or NotSupportedException or OutOfMemoryException) { }
        }
        return copy;
    }

    [DllImport("user32.dll")]
    static extern uint GetClipboardSequenceNumber();
}
