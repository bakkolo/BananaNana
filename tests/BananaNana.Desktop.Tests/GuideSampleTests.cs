using SkiaSharp;
using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The sample guide for --guide-maker-sample: made once, two steps, first picture 800 x 450.
public class GuideSampleTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bn-sample-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void Sample_is_created_once_and_has_two_steps_with_one_picture()
    {
        var store = new GuideStore(folder);

        var first = GuideSample.EnsureSample(store);
        var second = GuideSample.EnsureSample(store);

        Assert.Equal(first, second);
        Assert.Single(store.List());
        var doc = store.Load(first);
        Assert.Equal("Sample guide", doc.Title);
        Assert.Equal(2, doc.Steps.Count);
        Assert.Null(doc.Steps[1].Image);
        using var bitmap = SKBitmap.Decode(Convert.FromBase64String(doc.Steps[0].Image!));
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(450, bitmap.Height);
    }
}
