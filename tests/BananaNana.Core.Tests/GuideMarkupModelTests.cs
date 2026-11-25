using Xunit;

namespace BananaNana.Core.Tests;

// Guide step markup (annotations and crop): it survives a save and load, and files written before stage 2 still read
// as having no markup.
public class GuideMarkupModelTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bn-markup-model-tests-" + Guid.NewGuid().ToString("N"));
    readonly GuideStore store;

    public GuideMarkupModelTests() => store = new GuideStore(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void Annotations_and_crop_survive_a_save_and_load()
    {
        var doc = store.Create("Markup");
        doc.Steps.Add(new GuideStep
        {
            Id = Guid.NewGuid(),
            Title = "Step",
            Image = "AAAA",
            ImageType = "png",
            Annotations =
            [
                new Annotation { Kind = AnnotationKind.Arrow, X1 = 1.5, Y1 = 2, X2 = 30.25, Y2 = 40, Color = "#2EA043", Thickness = AnnotationThickness.L },
                new Annotation { Kind = AnnotationKind.Number, X1 = 5, Y1 = 6, Number = 3, Color = "#3B82F6" },
                new Annotation { Kind = AnnotationKind.Text, X1 = 7, Y1 = 8, Text = "Click here\nNow", FontSize = 20 },
                new Annotation { Kind = AnnotationKind.Blur, X1 = 0, Y1 = 0, X2 = 9, Y2 = 9, Thickness = AnnotationThickness.S },
            ],
            Crop = new CropRect { X = 10, Y = 11, W = 300, H = 200 },
        });
        store.Save(doc);

        var step = store.Load(doc.Id).Steps[0];

        Assert.Equal(4, step.Annotations.Count);
        var arrow = step.Annotations[0];
        Assert.Equal(AnnotationKind.Arrow, arrow.Kind);
        Assert.Equal(1.5, arrow.X1);
        Assert.Equal(30.25, arrow.X2);
        Assert.Equal("#2EA043", arrow.Color);
        Assert.Equal(AnnotationThickness.L, arrow.Thickness);
        Assert.Equal(3, step.Annotations[1].Number);
        Assert.Equal("Click here\nNow", step.Annotations[2].Text);
        Assert.Equal(20, step.Annotations[2].FontSize);
        Assert.Equal(AnnotationKind.Blur, step.Annotations[3].Kind);
        Assert.Equal(AnnotationThickness.S, step.Annotations[3].Thickness);
        Assert.NotNull(step.Crop);
        Assert.Equal((10, 11, 300, 200), (step.Crop.X, step.Crop.Y, step.Crop.W, step.Crop.H));
    }

    [Fact]
    public void A_file_from_before_stage_2_loads_with_no_markup()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.PathFor(id), $$"""
            {
              "version": 1,
              "title": "Old guide",
              "intro": "",
              "created": "2026-10-01T10:00:00+00:00",
              "modified": "2026-10-01T10:05:00+00:00",
              "steps": [
                { "id": "{{Guid.NewGuid()}}", "title": "With annotations list", "text": "", "image": null, "imageType": null, "annotations": [] },
                { "id": "{{Guid.NewGuid()}}", "title": "Without the field", "text": "", "image": null, "imageType": null }
              ]
            }
            """);

        var doc = store.Load(id);

        Assert.Equal(2, doc.Steps.Count);
        Assert.Empty(doc.Steps[0].Annotations);
        Assert.Null(doc.Steps[0].Crop);
        Assert.Empty(doc.Steps[1].Annotations);
        Assert.Null(doc.Steps[1].Crop);
    }

    [Fact]
    public void A_step_without_markup_saves_an_empty_list_and_no_crop()
    {
        var doc = store.Create("Plain");
        doc.Steps.Add(new GuideStep { Id = Guid.NewGuid(), Title = "Plain step" });
        store.Save(doc);

        var step = store.Load(doc.Id).Steps[0];

        Assert.Empty(step.Annotations);
        Assert.Null(step.Crop);
    }
}
