using System.Xml.Linq;
using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The Windows app's translation files, read by the Linux loader. Every language must match English key for key.
public class LocTests
{
    public static TheoryData<string> Codes => new(Loc.Languages.Select(l => l.Code));

    public static TheoryData<string> OtherCodes => new(Loc.Languages.Select(l => l.Code).Where(c => c != "en"));

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_language_file_loads(string code)
    {
        var strings = Loc.Load(code);
        Assert.NotEmpty(strings);
        Assert.False(strings.Values.Any(string.IsNullOrWhiteSpace), $"{code} has an empty string");
    }

    [Theory]
    [MemberData(nameof(OtherCodes))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Loc.Load("en");
        var other = Loc.Load(code);
        Assert.Empty(english.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(OtherCodes))]
    public void Placeholders_match_english(string code)
    {
        var english = Loc.Load("en");
        foreach (var (key, text) in Loc.Load(code))
            Assert.Equal(Placeholders(english[key]), Placeholders(text));
    }

    [Fact]
    public void Linux_keys_are_defined_in_english()
    {
        var english = Loc.Load("en");
        foreach (var key in new[] { "Linux.Autostart", "Linux.StartPaused", "Linux.Banner.Title", "Linux.Banner.Body", "Status.Listening", "Edit.FormattingNotice", "Edit.SavePlain" })
            Assert.Contains(key, english.Keys);
    }

    [Fact]
    public void Unknown_key_is_returned_as_is()
    {
        Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));
    }

    [Fact]
    public void Parse_reads_strings_whatever_the_namespace_prefix()
    {
        var xml = XDocument.Parse("""
            <ResourceDictionary xmlns="urn:wpf" xmlns:x="urn:xaml" xmlns:s="urn:sys">
              <s:String x:Key="A">one</s:String>
              <s:String x:Key="B">two&#x0a;lines</s:String>
            </ResourceDictionary>
            """);
        var strings = Loc.Parse(xml);
        Assert.Equal("one", strings["A"]);
        Assert.Equal("two\nlines", strings["B"]);
    }

    [Fact]
    public void Parse_rejects_a_duplicate_key()
    {
        var xml = XDocument.Parse("""<R xmlns:x="urn:xaml" xmlns:s="urn:sys"><s:String x:Key="A">1</s:String><s:String x:Key="A">2</s:String></R>""");
        Assert.Throws<InvalidDataException>(() => Loc.Parse(xml));
    }

    static string[] Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
}
