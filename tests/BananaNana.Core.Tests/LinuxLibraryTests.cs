using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BananaNana.Tests;

public class LinuxLibraryTests
{
    const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    // A XamlPackage holding one bold run, built in the test.
    static string Package(string xaml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Xaml/Document.xaml").Open(), new UTF8Encoding(false));
            writer.Write("<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + xaml + "</FlowDocument>");
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    // A Windows library file in the BananaNana.library.json format.
    static string WindowsLibrary(params (string Trigger, string Content)[] snippets) =>
        JsonSerializer.Serialize(new
        {
            Name = "Windows",
            Snippets = snippets.Select(s => new { Trigger = s.Trigger, Content = s.Content }),
        });

    static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static string TempDir()
    {
        var dir = Directory.CreateTempSubdirectory("bn-import-");
        return dir.FullName;
    }

    [Fact]
    public void Import_into_an_empty_library_takes_everything_and_reports_the_bad_snippet()
    {
        var dir = TempDir();
        try
        {
            var source = Path.Combine(dir, "BananaNana.library.json");
            File.WriteAllText(source, WindowsLibrary(
                (";a", Package("<Paragraph><Bold>Alpha</Bold></Paragraph>")),
                (";bad", "%%% not base64"),
                (";b", Package("<Paragraph>Beta</Paragraph>"))));
            var before = Sha256(source);
            var target = Path.Combine(dir, "out", LinuxLibrary.FileName);
            var library = LinuxLibrary.Open(target);

            var report = library.ImportFromWindows(source);

            Assert.Equal(new[] { ";a", ";b" }, report.Imported);
            Assert.Empty(report.Skipped);
            var failure = Assert.Single(report.Failed);
            Assert.Equal(";bad", failure.Trigger);
            Assert.Contains(";bad", failure.Reason);

            var reloaded = LinuxLibrary.Open(target);
            Assert.Equal(2, reloaded.Snippets.Count);
            Assert.Equal("<p><b>Alpha</b></p>", reloaded.Snippets[0].Html);
            Assert.Equal("Alpha", reloaded.Snippets[0].Text);
            Assert.Equal("Beta", reloaded.Snippets[1].Text);
            Assert.Equal(before, Sha256(source));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Import_into_a_non_empty_library_skips_existing_triggers_and_reports_them()
    {
        var dir = TempDir();
        try
        {
            var target = Path.Combine(dir, LinuxLibrary.FileName);
            File.WriteAllText(target, "{\"Name\":\"x\",\"Snippets\":[{\"Trigger\":\";A\",\"Html\":\"\",\"Text\":\"mine\"}]}");
            var source = Path.Combine(dir, "BananaNana.library.json");
            File.WriteAllText(source, WindowsLibrary((";a", Package("<Paragraph>theirs</Paragraph>")), (";new", Package("<Paragraph>new</Paragraph>"))));
            var before = Sha256(source);
            var library = LinuxLibrary.Open(target);

            var report = library.ImportFromWindows(source);

            Assert.Equal(new[] { ";new" }, report.Imported);
            var skipped = Assert.Single(report.Skipped);
            Assert.Equal(";a", skipped.Trigger);
            Assert.Contains("already", skipped.Reason);
            Assert.Empty(report.Failed);
            Assert.Equal(new[] { ";A", ";new" }, LinuxLibrary.Open(target).Snippets.Select(s => s.Trigger));
            Assert.Equal("mine", LinuxLibrary.Open(target).Snippets[0].Text);
            Assert.Equal(before, Sha256(source));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Duplicate_triggers_in_the_windows_file_are_skipped_once()
    {
        var dir = TempDir();
        try
        {
            var source = Path.Combine(dir, "BananaNana.library.json");
            File.WriteAllText(source, WindowsLibrary((";x", Package("<Paragraph>one</Paragraph>")), (";X", Package("<Paragraph>two</Paragraph>"))));
            var target = Path.Combine(dir, LinuxLibrary.FileName);

            var report = LinuxLibrary.Open(target).ImportFromWindows(source);

            Assert.Equal(new[] { ";x" }, report.Imported);
            Assert.Equal(";X", Assert.Single(report.Skipped).Trigger);
            Assert.Equal("one", LinuxLibrary.Open(target).Snippets[0].Text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Empty_trigger_is_reported_as_failed()
    {
        var dir = TempDir();
        try
        {
            var source = Path.Combine(dir, "BananaNana.library.json");
            File.WriteAllText(source, WindowsLibrary(("  ", Package("<Paragraph>x</Paragraph>")), (";ok", Package("<Paragraph>ok</Paragraph>"))));

            var report = LinuxLibrary.Open(Path.Combine(dir, LinuxLibrary.FileName)).ImportFromWindows(source);

            Assert.Equal(new[] { ";ok" }, report.Imported);
            Assert.Single(report.Failed);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_library_file_is_an_empty_library_and_invalid_file_is_rejected_unchanged()
    {
        var dir = TempDir();
        try
        {
            var target = Path.Combine(dir, LinuxLibrary.FileName);
            Assert.True(LinuxLibrary.Open(target).IsEmpty);

            File.WriteAllText(target, "{\"Snippets\":[{\"Trigger\":\"\"}]}");
            Assert.Throws<InvalidDataException>(() => LinuxLibrary.Open(target));
            Assert.Equal("{\"Snippets\":[{\"Trigger\":\"\"}]}", File.ReadAllText(target));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Saved_library_uses_the_linux_format_and_no_temp_file_is_left()
    {
        var dir = TempDir();
        try
        {
            var target = Path.Combine(dir, LinuxLibrary.FileName);
            var library = LinuxLibrary.Open(target);
            library.Data.Snippets.Add(new HtmlSnippet { Trigger = ";img", Html = $"<img src=\"data:image/png;base64,{PngBase64}\">", Text = "" });
            library.Save();

            var json = File.ReadAllText(target);
            using var doc = JsonDocument.Parse(json);
            var snippet = doc.RootElement.GetProperty("Snippets")[0];
            Assert.Equal(";img", snippet.GetProperty("Trigger").GetString());
            Assert.True(snippet.TryGetProperty("Html", out _));
            Assert.True(snippet.TryGetProperty("Text", out _));
            Assert.False(File.Exists(target + ".tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
