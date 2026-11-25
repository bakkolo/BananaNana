namespace BananaNana.Linux.Clipboard;

public static class ClipboardFormat
{
    public const string Html = "text/html";
    public const string Plain = "text/plain;charset=utf-8";

    // Plain text when the HTML carries no formatting or images (it equals the plain text), else HTML.
    public static (string MimeType, string Payload) Choose(string html, string plainText) =>
        string.IsNullOrEmpty(html) || html == plainText ? (Plain, plainText) : (Html, html);
}
