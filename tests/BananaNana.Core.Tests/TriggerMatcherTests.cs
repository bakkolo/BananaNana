using BananaNana.Input;
using Xunit;

namespace BananaNana.Tests;

public class TriggerMatcherTests
{
    static string? Type(TriggerMatcher m, string text)
    {
        string? last = null;
        foreach (var c in text) last = m.OnChar(c) ?? last;
        return last;
    }

    [Fact]
    public void Matches_trigger_followed_by_separator_case_insensitive()
    {
        var m = new TriggerMatcher([";oferta"]);
        Assert.Null(Type(m, ";oferta"));
        Assert.Equal(";oferta", m.OnChar(' '));
        Assert.Equal(";oferta", Type(m, "x ;OFERTA "));
    }

    [Fact]
    public void Does_not_match_inside_a_longer_word()
    {
        var m = new TriggerMatcher(["kot"]);
        Assert.Null(Type(m, "szkot "));
        Assert.Null(Type(m, "koty "));
        Assert.Equal("kot", Type(m, "szkot kot "));
    }

    [Fact]
    public void Backspace_corrects_the_current_word()
    {
        var m = new TriggerMatcher([";oferta"]);
        Type(m, ";ofrt");
        m.OnBackspace(); m.OnBackspace();
        Assert.Equal(";oferta", Type(m, "erta "));
    }

    [Fact]
    public void Backspace_past_the_word_start_blocks_until_next_separator()
    {
        var m = new TriggerMatcher(["ab"]);
        Type(m, "x ");
        m.OnBackspace(); // deleted the space: we are now editing the unseen word "x"
        Assert.Null(Type(m, "ab "));
        Assert.Equal("ab", Type(m, "ab "));
    }

    [Fact]
    public void Reset_starts_a_fresh_word()
    {
        var m = new TriggerMatcher(["ab"]);
        Type(m, "zz");
        m.Reset();
        Assert.Equal("ab", Type(m, "ab "));
    }

    [Fact]
    public void Overlong_word_never_matches_and_buffer_stays_bounded()
    {
        var m = new TriggerMatcher(["ab"]);
        Assert.Null(Type(m, new string('x', TriggerMatcher.MaxWordLength) + "ab "));
        Assert.Equal("ab", Type(m, "ab "));
    }

    [Fact]
    public void Polish_and_unicode_triggers_work()
    {
        var m = new TriggerMatcher([";żółw"]);
        Assert.Equal(";żółw", Type(m, ";ŻÓŁW "));
    }

    [Fact]
    public void Triggers_with_separators_are_ignored_and_custom_separators_work()
    {
        var m = new TriggerMatcher(["a b", "ok"], separators: " .");
        Assert.Null(Type(m, "a b "));
        Assert.Equal("ok", Type(m, "ok."));
    }
}
