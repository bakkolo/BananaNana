using System.Globalization;
using System.Text;
namespace BananaNana.Paste;

// Builds the Windows "HTML Format" clipboard payload. All offsets are UTF-8 byte positions.
// https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format
public static class CfHtml
{
    const string HeaderTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
    const string Prefix = "<html><body>\r\n<!--StartFragment-->";
    const string Suffix = "<!--EndFragment-->\r\n</body></html>";
    static readonly UTF8Encoding Utf8 = new(false);

    // Returns the payload plus a trailing NUL, which EndHTML does not count.
    public static byte[] Build(string fragmentHtml)
    {
        int startHtml = Utf8.GetByteCount(Header(0, 0, 0, 0));
        int startFragment = startHtml + Utf8.GetByteCount(Prefix);
        int endFragment = startFragment + Utf8.GetByteCount(fragmentHtml);
        int endHtml = endFragment + Utf8.GetByteCount(Suffix);
        return Utf8.GetBytes(Header(startHtml, endHtml, startFragment, endFragment) + Prefix + fragmentHtml + Suffix + "\0");
    }

    static string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
        string.Format(CultureInfo.InvariantCulture, HeaderTemplate, startHtml, endHtml, startFragment, endFragment);
}
