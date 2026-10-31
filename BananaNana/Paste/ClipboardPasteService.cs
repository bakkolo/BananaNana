using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
namespace BananaNana.Paste;

// Pastes rendered content into the focused app: snapshot clipboard, set ours, send Ctrl+V, restore.
// Must be called on the WPF UI (STA) thread. WPF's Clipboard calls already retry while another app
// holds the clipboard open (10 tries, 100 ms apart), so there is no extra retry loop here.
public sealed class ClipboardPasteService
{
    // Tag on our SendInput events so the keyboard listener (Stage 3) can ignore its own input.
    public const nuint InjectedInputMarker = 0xBA7A7A;

    // How long the target app gets to read the clipboard before the old content returns.
    // Apps that read asynchronously (e.g. Teams) may need more; make this per-app in Stage 5 settings.
    public TimeSpan RestoreDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    public async Task PasteAsync(RenderedContent content)
    {
        var snapshot = Snapshot();
        Clipboard.SetDataObject(ToDataObject(content), copy: true);
        uint ours = NativeMethods.GetClipboardSequenceNumber();
        if (!SendCtrlV()) throw new InvalidOperationException("SendInput was blocked; the target app is probably elevated (UIPI).");
        await Task.Delay(RestoreDelay);
        // Best effort: restore only if nobody else changed the clipboard meanwhile (e.g. the user copied something).
        if (NativeMethods.GetClipboardSequenceNumber() != ours) return;
        if (snapshot.GetFormats(false).Length == 0) Clipboard.Clear();
        else Clipboard.SetDataObject(snapshot, copy: true);
    }

    // Plain text for apps like Notepad; Word prefers the HTML when both rich formats are present.
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

    static bool SendCtrlV()
    {
        const ushort VK_CONTROL = 0x11, VK_V = 0x56;
        var inputs = new[] { Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true) };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == inputs.Length;
    }

    static NativeMethods.INPUT Key(ushort vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new() { ki = new() { wVk = vk, dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0, dwExtraInfo = InjectedInputMarker } },
    };
}

static class NativeMethods
{
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    // MOUSEINPUT is the largest member and sets the union size SendInput checks via cbSize.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();
}
