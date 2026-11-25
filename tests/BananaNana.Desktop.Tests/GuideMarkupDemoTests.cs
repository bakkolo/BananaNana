using BananaNana.Desktop;
using SkiaSharp;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The --guide-markup-demo guide: one step with every kind of markup and a 5 % crop, made again on each request.
public class GuideMarkupDemoTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bn-demo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void Demo_has_one_step_with_each_kind_of_markup_and_a_5_percent_crop()
    {
        var store = new GuideStore(folder);

        var id = GuideMarkupDemo.Create(store);

        var doc = store.Load(id);
        Assert.Equal("Markup demo", doc.Title);
        Assert.Single(doc.Steps);
        var step = doc.Steps[0];
        var kinds = step.Annotations.Select(a => a.Kind).ToList();
        Assert.Contains(AnnotationKind.Arrow, kinds);
        Assert.Contains(AnnotationKind.Box, kinds);
        Assert.Contains(AnnotationKind.Highlight, kinds);
        Assert.Contains(AnnotationKind.Text, kinds);
        Assert.Contains(AnnotationKind.Blur, kinds);
        Assert.Equal(new[] { 1, 2 }, step.Annotations.Where(a => a.Kind == AnnotationKind.Number).Select(a => a.Number).ToArray());
        Assert.Equal((40, 22, 720, 406), (step.Crop!.X, step.Crop.Y, step.Crop.W, step.Crop.H));

        using var result = SKBitmap.Decode(GuideRender.Flatten(step))!;
        Assert.Equal((720, 406), (result.Width, result.Height));
    }

    [Fact]
    public void Demo_is_replaced_not_duplicated()
    {
        var store = new GuideStore(folder);

        GuideMarkupDemo.Create(store);
        var second = GuideMarkupDemo.Create(store);

        var guides = store.List().Where(g => g.Title == "Markup demo").ToList();
        Assert.Single(guides);
        Assert.Equal(second, guides[0].Id);
    }
}
