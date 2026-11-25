using System.Diagnostics;
using System.Text;

namespace BananaNana.Linux.Clipboard;

// Clipboard through wl-clipboard (wl-paste / wl-copy). Only text is saved and restored.
// TODO: wl-copy offers ONE mime type per copy. To put HTML and plain text on the clipboard together, so
// apps that only take plain text still paste something, wl-copy would need several types in one copy
// (or a small Wayland client). Until then Choose() picks a single type.
public sealed class WaylandClipboard
{
    static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(2);

    public sealed record Saved(string MimeType, byte[] Data);

    // Keeps text/html when offered, else text/plain. Null when the clipboard holds no text.
    public Saved? Save()
    {
        var raw = Capture(["--list-types"]);
        if (raw is null) return null;
        var types = Encoding.UTF8.GetString(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? type = types.Contains(ClipboardFormat.Html)
            ? ClipboardFormat.Html
            : types.FirstOrDefault(t => t.StartsWith("text/plain", StringComparison.Ordinal));
        if (type is null) return null;
        var data = Capture(["--no-newline", "--type", type]);
        return data is null ? null : new Saved(type, data);
    }

    public void Set(byte[] data, string mimeType)
    {
        var psi = Info("wl-copy", "--type", mimeType);
        psi.RedirectStandardInput = true;
        // stdout and stderr are inherited: wl-copy forks a background server that keeps the clipboard,
        // and a redirected pipe held by that child would stall us.
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("wl-copy did not start.");
        p.StandardInput.BaseStream.Write(data);
        p.StandardInput.Close();
        if (!p.WaitForExit(ExitTimeout))
            throw new TimeoutException($"wl-copy did not return within {ExitTimeout.TotalSeconds} s.");
    }

    public void Restore(Saved? saved)
    {
        if (saved is not null) Set(saved.Data, saved.MimeType);
    }

    // The clipboard's picture as PNG (or JPEG when there is no PNG), or null when it holds none.
    // Throws when wl-paste cannot be started. The Guide maker uses this when Avalonia's clipboard has no picture.
    public Saved? ReadImage()
    {
        var raw = Capture(["--list-types"]);
        if (raw is null) return null;
        var types = Encoding.UTF8.GetString(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var type = types.FirstOrDefault(t => t == "image/png") ?? types.FirstOrDefault(t => t == "image/jpeg");
        if (type is null) return null;
        var data = Capture(["--no-newline", "--type", type]);
        return data is null || data.Length == 0 ? null : new Saved(type, data);
    }

    // Runs wl-paste and returns its stdout, or null when it fails (for example, an empty clipboard).
    static byte[]? Capture(string[] args)
    {
        var psi = Info("wl-paste", args);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("wl-paste did not start.");
        var stderr = p.StandardError.ReadToEndAsync(); // drained so wl-paste never blocks on a full pipe
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        if (!p.WaitForExit(ExitTimeout))
        {
            p.Kill();
            return null;
        }
        return p.ExitCode == 0 ? ms.ToArray() : null;
    }

    static ProcessStartInfo Info(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }
}
