using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BananaNana.Paste;
using Xunit;

namespace BananaNana.Tests;

public class ImageResizeAdornerTests
{
    static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    // Editor-like setup: a 200x100 px image in a RichTextBox hosted in an off-screen window (adorners need a layer).
    static (Window window, RichTextBox box, Image image) Setup()
    {
        var bmp = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, new byte[200 * 100 * 4], 200 * 4);
        bmp.Freeze();
        var image = new Image { Source = bmp, MaxWidth = 500, MaxHeight = 350 };
        var box = new RichTextBox();
        box.Document.Blocks.Clear();
        box.Document.Blocks.Add(new Paragraph(new InlineUIContainer(image)));
        EditorImages.FixSizes(box.Document);   // as the editor does on every change
        var window = new Window { Content = box, Width = 800, Height = 600, Left = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        window.UpdateLayout();
        return (window, box, image);
    }

    [Fact]
    public void Corner_drag_resizes_with_locked_aspect_ratio() => Sta(() =>
    {
        var (window, _, image) = Setup();
        var adorner = ImageResizeAdorner.Attach(image)!;
        Assert.NotNull(adorner);

        adorner.BeginDrag(new Point(0, 0));
        adorner.Drag(corner: 2, new Vector(100, 10));   // bottom-right outwards, mostly horizontal
        Assert.Equal(300, image.Width);
        Assert.Equal(150, image.Height);

        adorner.Drag(corner: 0, new Vector(50, 0));      // top-left dragged inwards shrinks
        Assert.Equal(150, image.Width);
        Assert.Equal(75, image.Height);

        adorner.Drag(corner: 2, new Vector(-1000, 0));   // never below the minimum
        Assert.Equal(ImageResizeAdorner.MinSize, image.Width);
        window.Close();
    });

    [Fact]
    public void Unsized_images_get_natural_size_capped_to_editor_defaults() => Sta(() =>
    {
        var small = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, new byte[200 * 100 * 4], 200 * 4);
        var big = BitmapSource.Create(1000, 200, 96, 96, PixelFormats.Bgra32, null, new byte[1000 * 200 * 4], 1000 * 4);
        var a = new Image { Source = small };
        var b = new Image { Source = big };
        var sized = new Image { Source = big, Width = 40, Height = 8 };
        var doc = new FlowDocument(new Paragraph(new InlineUIContainer(a)));
        doc.Blocks.Add(new BlockUIContainer(b));
        doc.Blocks.Add(new Paragraph(new InlineUIContainer(sized)));

        EditorImages.FixSizes(doc);

        Assert.Equal((200d, 100d), (a.Width, a.Height));
        Assert.Equal((500d, 100d), (b.Width, b.Height));
        Assert.Equal((40d, 8d), (sized.Width, sized.Height));   // user-chosen sizes are kept
    });

    [Fact]
    public void Resized_size_is_saved_and_used_when_pasting() => Sta(() =>
    {
        var (window, box, image) = Setup();
        var adorner = ImageResizeAdorner.Attach(image)!;
        adorner.BeginDrag(new Point(0, 0));
        adorner.Drag(corner: 2, new Vector(400, 0));     // 600x300: beyond the editor's old 500 px cap

        using var stream = new MemoryStream();
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Save(stream, DataFormats.XamlPackage);
        var html = RichContentRenderer.Render(RichContentRenderer.LoadXamlPackage(Convert.ToBase64String(stream.ToArray()))).Html;

        Assert.Contains("width=\"600\" height=\"300\"", html);
        window.Close();
    });
}
