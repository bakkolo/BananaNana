using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BananaNana.Input;
using BananaNana.Paste;
using Image = System.Windows.Controls.Image;

namespace PasteLab;

// Stage 1 gate (docs/PLAN.md): which clipboard formats make Word, Outlook and Teams show
// formatted text and an image together? Each variant puts the same sample in a different format.
public partial class MainWindow : Window
{
    const int ImgW = 240, ImgH = 96;
    const string PlainText = "Banana Nana test – kursywa, podkreślenie. Zażółć gęślą jaźń.\r\n• Pierwszy punkt\r\n• Drugi punkt\r\n• Trzeci punkt\r\n[obraz]";

    readonly byte[] png = CreatePng();
    readonly string pngPath = Path.Combine(Path.GetTempPath(), "BananaNana", "pastelab.png");

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        File.WriteAllBytes(pngPath, png);

        AddVariant("A", "HTML, base64 data: image", () => Data(html: Html("data:image/png;base64," + Convert.ToBase64String(png))));
        AddVariant("B", "HTML, file:/// image", () => Data(html: Html(new Uri(pngPath).AbsoluteUri)));
        AddVariant("C", "RTF, \\pngblip image", () => Data(rtf: Rtf()));
        AddVariant("D", "Text + RTF + HTML (data:) together", () => Data(rtf: Rtf(), html: Html("data:image/png;base64," + Convert.ToBase64String(png))));
        AddVariant("E", "WPF built-in RTF export (TextRange.Save)", () => Data(rtf: WpfRtf()));
        AddVariant("F", "Image only (bitmap + PNG)", ImageOnly);
        Editor.Document = SampleDocument();
    }

    void AddVariant(string id, string label, Func<DataObject> build)
    {
        var button = new Button { Content = $"{id}.  {label}", Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            try
            {
                var data = build();
                Clipboard.SetDataObject(data, copy: true);
                Status.Text = $"Variant {id} is on the clipboard ({string.Join(", ", data.GetFormats(false))}). Paste it now.";
            }
            catch (Exception ex) when (ex is ExternalException or COMException)
            {
                Status.Text = $"Clipboard busy, try again: {ex.Message}";
            }
        };
        Variants.Children.Add(button);
    }

    // Plain text always goes along so Notepad gets something; rich apps prefer RTF/HTML over it.
    static DataObject Data(string? rtf = null, string? html = null)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, PlainText, false);
        if (rtf != null) data.SetData(DataFormats.Rtf, rtf, false);
        // A MemoryStream is written to the clipboard byte for byte, so the CF_HTML offsets stay valid.
        if (html != null) data.SetData(DataFormats.Html, new MemoryStream(CfHtml.Build(html)), false);
        return data;
    }

    static string Html(string imgSrc) =>
        "<p><b>Banana Nana</b> test – <i>kursywa</i>, <u>podkreślenie</u>. Zażółć gęślą jaźń.</p>" +
        "<ul><li>Pierwszy punkt</li><li>Drugi punkt</li><li>Trzeci punkt</li></ul>" +
        $"<p><img src=\"{imgSrc}\" width=\"{ImgW}\" height=\"{ImgH}\" alt=\"Banana Nana\"></p>";

    string Rtf() => new RtfBuilder()
        .Bold("Banana Nana").Text(" test – ").Italic("kursywa").Text(", ").Underline("podkreślenie").Text(". Zażółć gęślą jaźń.").Paragraph()
        .Bullet("Pierwszy punkt").Bullet("Drugi punkt").Bullet("Trzeci punkt")
        .PngImage(png, ImgW, ImgH).Paragraph()
        .ToString();

    // Answers the open research question: does WPF's own RTF export keep images on .NET 8+?
    string WpfRtf()
    {
        using var stream = new MemoryStream();
        var doc = SampleDocument();
        new TextRange(doc.ContentStart, doc.ContentEnd).Save(stream, DataFormats.Rtf);
        var rtf = Encoding.ASCII.GetString(stream.ToArray());
        Dispatcher.BeginInvoke(() => Status.Text += rtf.Contains(@"\pict") ? "  WPF RTF export DID include the image." : "  WPF RTF export DROPPED the image.");
        return rtf;
    }

    // Same path as the real app: editor → XamlPackage (stored form) → reloaded document → renderer.
    RenderedContent RenderEditor()
    {
        using var stream = new MemoryStream();
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(stream, DataFormats.XamlPackage);
        return RichContentRenderer.Render(RichContentRenderer.LoadXamlPackage(Convert.ToBase64String(stream.ToArray())));
    }

    static string Describe(RenderedContent c) =>
        $"text {c.PlainText.Length} chars, RTF {c.Rtf.Length / 1024} KB ({(c.Rtf.Contains(@"\pict") ? "with" : "NO")} image), " +
        $"HTML {c.Html.Length / 1024} KB ({Regex.Count(c.Html, "<img ")} images)";

    void Render_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = RenderEditor();
            Clipboard.SetDataObject(ClipboardPasteService.ToDataObject(content), copy: true);
            Status.Text = "G is on the clipboard: " + Describe(content) + ". Paste it now.";
            Inspector.Text = content.Html;
        }
        catch (Exception ex) when (ex is ExternalException or COMException)
        {
            Status.Text = $"Clipboard busy, try again: {ex.Message}";
        }
    }

    async void DelayedPaste_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            var content = RenderEditor();
            for (int s = 3; s > 0; s--) { Status.Text = $"Click into the target app… pasting in {s}"; await Task.Delay(1000); }
            await new ClipboardPasteService().PasteAsync(content);
            Status.Text = "H pasted: " + Describe(content) + ". Your previous clipboard has been restored.";
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            Status.Text = "Paste failed: " + ex.Message;
        }
        finally { button.IsEnabled = true; }
    }

    // Stage 3 live test: Raw Input listener + matcher drive the paste engine in any app.
    // No password-field or elevated-app checks yet (Stage 4); PasteLab itself is skipped.
    KeyboardInputService? listener;
    readonly TriggerMatcher matcher = new([";bn"]);
    readonly ClipboardPasteService paster = new();

    void Live_Changed(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsChecked == true)
        {
            listener = new KeyboardInputService();
            listener.CharTyped += c => { if (matcher.OnChar(c) is { } trigger) Expand(trigger); };
            listener.Backspace += matcher.OnBackspace;
            listener.Reset += matcher.Reset;
            listener.Start();
            Status.Text = $"Live: type {TriggerBox.Text} and a space in any other app.";
        }
        else
        {
            listener?.Dispose();
            listener = null;
            matcher.Reset();
            Status.Text = "Live test off.";
        }
    }

    void Trigger_Changed(object sender, TextChangedEventArgs e) => matcher.SetTriggers([TriggerBox.Text.Trim()]);

    async void Expand(string trigger)
    {
        if (IsActive) return;
        try
        {
            var content = RenderEditor();
            await paster.PasteAsync(content, backspaces: trigger.Length + 1);
            Status.Text = $"Expanded {trigger}: {Describe(content)}.";
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            Status.Text = $"Expansion of {trigger} failed: {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        listener?.Dispose();
        base.OnClosed(e);
    }

    FlowDocument SampleDocument()
    {
        var line = new Paragraph();
        line.Inlines.Add(new Bold(new Run("Banana Nana")));
        line.Inlines.Add(new Run(" test – "));
        line.Inlines.Add(new Italic(new Run("kursywa")));
        line.Inlines.Add(new Run(", "));
        line.Inlines.Add(new Underline(new Run("podkreślenie")));
        line.Inlines.Add(new Run(". Zażółć gęślą jaźń."));
        var list = new List { MarkerStyle = TextMarkerStyle.Disc };
        foreach (var item in new[] { "Pierwszy punkt", "Drugi punkt", "Trzeci punkt" })
            list.ListItems.Add(new ListItem(new Paragraph(new Run(item))));
        var picture = new Paragraph(new InlineUIContainer(new Image { Source = LoadBitmap(png), Width = ImgW, Height = ImgH }));
        var doc = new FlowDocument(line);
        doc.Blocks.Add(list);
        doc.Blocks.Add(picture);
        return doc;
    }

    DataObject ImageOnly()
    {
        var data = new DataObject();
        data.SetImage(LoadBitmap(png));
        data.SetData("PNG", new MemoryStream(png), false);
        return data;
    }

    void Inspect_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        try
        {
            var data = Clipboard.GetDataObject();
            var formats = data?.GetFormats(false) ?? [];
            sb.AppendLine("Formats: " + string.Join(", ", formats));
            foreach (var format in new[] { DataFormats.Html, DataFormats.Rtf, DataFormats.UnicodeText })
            {
                if (!formats.Contains(format)) continue;
                var text = data!.GetData(format) switch { string s => s, MemoryStream m => Encoding.UTF8.GetString(m.ToArray()), var o => o?.ToString() ?? "" };
                sb.AppendLine().AppendLine($"=== {format} ({text.Length.ToString("N0", CultureInfo.InvariantCulture)} chars)");
                sb.AppendLine(text.Length > 4000 ? text[..4000] + "\r\n… (truncated)" : text);
            }
        }
        catch (Exception ex) when (ex is ExternalException or COMException)
        {
            sb.AppendLine("Clipboard busy: " + ex.Message);
        }
        Inspector.Text = sb.ToString();
    }

    static BitmapImage LoadBitmap(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    static byte[] CreatePng()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), new Pen(Brushes.SaddleBrown, 3), new Rect(1.5, 1.5, ImgW - 3, ImgH - 3), 16, 16);
            var text = new FormattedText("Banana Nana", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 30, Brushes.SaddleBrown, 1.0);
            dc.DrawText(text, new Point((ImgW - text.Width) / 2, (ImgH - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(ImgW, ImgH, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
