using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

public class TriggerRulesTests
{
    [Theory]
    [InlineData("", TriggerError.Empty)]
    [InlineData("   ", TriggerError.Empty)]
    [InlineData(";a b", TriggerError.HasSpace)]
    [InlineData(";a\tb", TriggerError.HasSpace)]
    public void Empty_and_spaced_triggers_are_refused(string trigger, TriggerError expected) =>
        Assert.Equal(expected, TriggerRules.Validate(trigger, []));

    [Fact]
    public void Triggers_are_limited_to_the_matcher_length()
    {
        Assert.Null(TriggerRules.Validate(new string('a', 64), []));
        Assert.Equal(TriggerError.TooLong, TriggerRules.Validate(new string('a', 65), []));
    }

    [Fact]
    public void Duplicates_are_refused_ignoring_case()
    {
        Assert.Equal(TriggerError.Duplicate, TriggerRules.Validate(";OFFER", [";offer", ";sig"]));
    }

    [Fact]
    public void A_snippet_may_keep_its_own_trigger_in_any_case()
    {
        Assert.Null(TriggerRules.Validate(";Offer", [";offer", ";sig"], current: ";offer"));
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed_before_checking()
    {
        Assert.Equal(";sig", TriggerRules.Normalize("  ;sig  "));
        Assert.Null(TriggerRules.Validate("  ;sig  ", []));
    }

    [Fact]
    public void A_new_unique_trigger_is_valid()
    {
        Assert.Null(TriggerRules.Validate(";thanks", [";offer"]));
    }
}
