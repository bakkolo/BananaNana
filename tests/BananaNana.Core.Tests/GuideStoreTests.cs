using System.Text.Json;
using Xunit;

namespace BananaNana.Core.Tests;

// GuideStore: one <guid>.bnguide file per guide in a folder. Every test works in its own empty temp folder.
public class GuideStoreTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bn-guides-tests-" + Guid.NewGuid().ToString("N"));
    readonly GuideStore store;

    public GuideStoreTests() => store = new GuideStore(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    static GuideStep Step(string title, string text, string? image = null) =>
        new() { Id = Guid.NewGuid(), Title = title, Text = text, Image = image, ImageType = image is null ? null : "png" };

    [Fact]
    public void Create_writes_a_file_named_by_the_guid()
    {
        var doc = store.Create("Printing");

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.True(File.Exists(Path.Combine(folder, doc.Id.ToString("D") + ".bnguide")));
        Assert.Equal("Printing", store.Load(doc.Id).Title);
    }

    [Fact]
    public void List_returns_newest_first_and_skips_other_files()
    {
        var older = store.Create("Older");
        Thread.Sleep(20);
        var newer = store.Create("Newer");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a guide");
        File.WriteAllText(Path.Combine(folder, "NOT-A-GUID.bnguide"), "{}");
        File.WriteAllText(Path.Combine(folder, Guid.NewGuid().ToString("D") + ".bnguide"), "{ broken");

        var list = store.List();

        Assert.Equal(new[] { newer.Id, older.Id }, list.Select(g => g.Id));
        Assert.Equal("Newer", list[0].Title);
    }

    [Fact]
    public void List_of_a_missing_folder_is_empty()
    {
        Assert.Empty(store.List());
    }

    [Fact]
    public void Save_then_load_keeps_title_intro_and_steps()
    {
        var doc = store.Create("First");
        doc.Intro = "Intro line";
        doc.Steps.Add(Step("Open", "Click open", "iVBORw0KGgo="));
        doc.Steps.Add(Step("Save", "Press Ctrl+S"));
        store.Save(doc);

        var loaded = store.Load(doc.Id);

        Assert.Equal(doc.Id, loaded.Id);
        Assert.Equal("Intro line", loaded.Intro);
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Equal("Open", loaded.Steps[0].Title);
        Assert.Equal("iVBORw0KGgo=", loaded.Steps[0].Image);
        Assert.Equal("png", loaded.Steps[0].ImageType);
        Assert.Null(loaded.Steps[1].Image);
        Assert.Empty(loaded.Steps[1].Annotations);
    }

    [Fact]
    public void File_uses_version_1_and_camel_case_keys()
    {
        var doc = store.Create("Shape");
        doc.Steps.Add(Step("One", "Two", "iVBORw0KGgo="));
        store.Save(doc);

        var json = File.ReadAllText(store.PathFor(doc.Id));

        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"steps\"", json);
        Assert.Contains("\"imageType\"", json);
        Assert.Contains("\"annotations\"", json);
        Assert.DoesNotContain("\"Id\"", json);
    }

    [Fact]
    public void Save_sets_modified_and_keeps_created()
    {
        var doc = store.Create("Dates");
        var created = doc.Created;
        doc.Modified = DateTimeOffset.MinValue;

        store.Save(doc);

        Assert.Equal(created, store.Load(doc.Id).Created);
        Assert.True(store.Load(doc.Id).Modified > DateTimeOffset.MinValue.AddDays(1));
    }

    [Fact]
    public void Load_of_a_missing_guide_throws()
    {
        Assert.Throws<FileNotFoundException>(() => store.Load(Guid.NewGuid()));
    }

    [Fact]
    public void Delete_removes_the_file_once()
    {
        var doc = store.Create("Gone");

        Assert.True(store.Delete(doc.Id));
        Assert.False(File.Exists(store.PathFor(doc.Id)));
        Assert.False(store.Delete(doc.Id));
    }

    [Fact]
    public void Save_leaves_no_tmp_file_behind()
    {
        var doc = store.Create("Atomic");
        store.Save(doc);

        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp"));
    }

    [Fact]
    public void A_failed_write_leaves_the_old_file_intact()
    {
        var doc = store.Create("Keep me");
        // A folder sitting where the .tmp file should go makes the write fail before anything is replaced.
        Directory.CreateDirectory(store.PathFor(doc.Id) + ".tmp");
        doc.Title = "Changed";

        Assert.ThrowsAny<Exception>(() => store.Save(doc));

        Assert.Equal("Keep me", store.Load(doc.Id).Title);
    }

    [Fact]
    public void Unicode_text_round_trips()
    {
        var doc = store.Create("Zażółć gęślą jaźń ✓ 日本語 🍌");
        doc.Steps.Add(Step("Ünïcödé", "Ça marche — «oui» ß Ж ก ñ"));
        store.Save(doc);

        var loaded = store.Load(doc.Id);

        Assert.Equal("Zażółć gęślą jaźń ✓ 日本語 🍌", loaded.Title);
        Assert.Equal("Ça marche — «oui» ß Ж ก ñ", loaded.Steps[0].Text);
    }

    [Fact]
    public void Image_bytes_round_trip_through_base64()
    {
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray();
        var doc = store.Create("Pictures");
        doc.Steps.Add(Step("Picture", "", Convert.ToBase64String(bytes)));
        store.Save(doc);

        var loaded = store.Load(doc.Id);

        Assert.Equal(bytes, Convert.FromBase64String(loaded.Steps[0].Image!));
    }

    [Fact]
    public void Save_rejects_an_empty_step_id()
    {
        var doc = store.Create("Bad");
        doc.Steps.Add(new GuideStep { Id = Guid.Empty });

        Assert.Throws<InvalidDataException>(() => store.Save(doc));
    }

    [Fact]
    public void Save_rejects_a_repeated_step_id()
    {
        var doc = store.Create("Bad");
        var id = Guid.NewGuid();
        doc.Steps.Add(new GuideStep { Id = id });
        doc.Steps.Add(new GuideStep { Id = id });

        Assert.Throws<InvalidDataException>(() => store.Save(doc));
    }

    [Fact]
    public void Load_rejects_a_step_id_that_is_not_a_guid()
    {
        var doc = store.Create("Text id");
        doc.Steps.Add(Step("S", ""));
        store.Save(doc);
        var json = File.ReadAllText(store.PathFor(doc.Id)).Replace(doc.Steps[0].Id.ToString("D"), "step-1");
        File.WriteAllText(store.PathFor(doc.Id), json);

        Assert.ThrowsAny<JsonException>(() => store.Load(doc.Id));
        Assert.DoesNotContain(store.List(), g => g.Id == doc.Id);
    }

    [Fact]
    public void Load_rejects_an_unknown_version()
    {
        var doc = store.Create("Future");
        var json = File.ReadAllText(store.PathFor(doc.Id)).Replace("\"version\": 1", "\"version\": 2");
        File.WriteAllText(store.PathFor(doc.Id), json);

        Assert.Throws<InvalidDataException>(() => store.Load(doc.Id));
    }

    [Fact]
    public void Default_folder_is_named_for_the_guides()
    {
        Assert.EndsWith("BananaNana Guides", GuideStore.DefaultFolder());
    }
}
