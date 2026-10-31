using System.IO;
using System.Text.Json;
using Xunit;

namespace BananaNana.Tests;

public class LibraryStoreTests
{
    // Format written by earlier versions (reflection-based JsonSerializer with WriteIndented).
    const string OldFormat = "{\n  \"Name\": \"My library\",\n  \"Snippets\": [\n    {\n      \"Trigger\": \";asd\",\n      \"Content\": \"UEsDBA==\"\n    }\n  ]\n}";

    [Fact]
    public void Reads_existing_library_files_and_writes_the_same_format()
    {
        var dir = Directory.CreateTempSubdirectory("bn-lib-");
        try
        {
            var path = Path.Combine(dir.FullName, LibraryStore.FileName);
            File.WriteAllText(path, OldFormat);
            var store = new LibraryStore();
            store.Open(path);
            Assert.Equal("My library", store.Library.Name);
            Assert.Equal((";asd", "UEsDBA=="), (store.Library.Snippets[0].Trigger, store.Library.Snippets[0].Content));

            store.Library.Snippets.Add(new Snippet { Trigger = ";zażółć", Content = "" });
            store.Save();
            var reflection = JsonSerializer.Serialize(store.Library, new JsonSerializerOptions { WriteIndented = true });
            Assert.Equal(reflection, File.ReadAllText(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Rejects_duplicate_triggers_ignoring_case()
    {
        var dir = Directory.CreateTempSubdirectory("bn-lib-");
        try
        {
            var path = Path.Combine(dir.FullName, LibraryStore.FileName);
            File.WriteAllText(path, "{\"Name\":\"x\",\"Snippets\":[{\"Trigger\":\";A\",\"Content\":\"\"},{\"Trigger\":\";a\",\"Content\":\"\"}]}");
            Assert.Throws<InvalidDataException>(() => new LibraryStore().Open(path));
        }
        finally { dir.Delete(true); }
    }
}
