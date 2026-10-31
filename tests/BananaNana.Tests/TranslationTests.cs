using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BananaNana.Tests;

// Every language file must match English key for key, keep the same {n} placeholders, and never start a value
// with "{" (XAML would try to read it as a markup extension).
public class TranslationTests
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static string StringsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "BananaNana", "Strings");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("BananaNana/Strings not found above " + AppContext.BaseDirectory);
    }

    static Dictionary<string, string> Load(string code) =>
        XDocument.Load(Path.Combine(StringsDir(), $"Strings.{code}.xaml")).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value);

    static string[] Placeholders(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order().ToArray();

    public static TheoryData<string> Languages => new(BananaNana.Loc.Languages.Select(l => l.Code).Where(c => c != "en"));

    [Fact]
    public void Every_listed_language_has_a_file_and_no_file_is_unlisted()
    {
        var files = Directory.GetFiles(StringsDir(), "Strings.*.xaml").Select(f => Path.GetFileName(f).Split('.')[1]).Order();
        Assert.Equal(BananaNana.Loc.Languages.Select(l => l.Code).Order(), files);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_is_complete_and_keeps_placeholders(string code)
    {
        var en = Load("en");
        var other = Load(code);

        Assert.Empty(en.Keys.Except(other.Keys));      // missing translations
        Assert.Empty(other.Keys.Except(en.Keys));      // unknown (mistyped) keys
        foreach (var (key, text) in other)
        {
            Assert.True(Placeholders(en[key]).SequenceEqual(Placeholders(text)), $"{code}:{key} placeholders differ");
            Assert.False(text.StartsWith('{'), $"{code}:{key} starts with '{{'");
            Assert.False(string.IsNullOrWhiteSpace(text), $"{code}:{key} is empty");
        }
    }

    [Fact]
    public void English_values_never_start_with_a_brace() =>
        Assert.DoesNotContain(Load("en"), kv => kv.Value.StartsWith('{'));

    [Fact]
    public void Main_window_tip_count_matches_the_tips_in_the_strings() =>
        Assert.Equal(8, Load("en").Keys.Count(k => k.StartsWith("Tip.")));
}
