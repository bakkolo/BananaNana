using System.Text;
namespace BananaNana.Paste;

// Minimal RTF writer: text runs, bullet paragraphs and embedded PNG pictures.
// Non-ASCII characters are written as \uN? escapes so the output is pure ASCII.
public sealed class RtfBuilder
{
    readonly StringBuilder sb = new(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl{\f0\fswiss Calibri;}}\pard\f0\fs22 ");

    public RtfBuilder Text(string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\' or '{' or '}': sb.Append('\\').Append(c); break;
                case '\n': sb.Append(@"\line "); break;
                case '\r': break;
                case '\t': sb.Append(@"\tab "); break;
                case > (char)127: sb.Append(@"\u").Append((short)c).Append('?'); break;
                default: sb.Append(c); break;
            }
        }
        return this;
    }

    public RtfBuilder Bold(string text) => Group(@"\b ", text);
    public RtfBuilder Italic(string text) => Group(@"\i ", text);
    public RtfBuilder Underline(string text) => Group(@"\ul ", text);
    public RtfBuilder Paragraph() => Raw(@"\par\pard ");
    public RtfBuilder Bullet(string text) => Raw(@"\pard\fi-360\li720 \bullet\tab ").Text(text).Raw(@"\par\pard ");

    // RTF picture sizes: \picw/\pich in pixels, \picwgoal/\pichgoal in twips (15 per pixel at 96 DPI).
    public RtfBuilder PngImage(byte[] png, int widthPx, int heightPx) =>
        Raw($@"{{\pict\pngblip\picw{widthPx}\pich{heightPx}\picwgoal{widthPx * 15}\pichgoal{heightPx * 15} ")
            .Raw(Convert.ToHexString(png).ToLowerInvariant())
            .Raw("}");

    public RtfBuilder Raw(string rtf) { sb.Append(rtf); return this; }

    RtfBuilder Group(string control, string text) => Raw("{" + control).Text(text).Raw("}");

    public override string ToString() => sb + "}";
}
