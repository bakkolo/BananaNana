using BananaNana.Input;
namespace BananaNana.Desktop;

public enum TriggerError { Empty, HasSpace, TooLong, Duplicate }

// The Windows editor's rules: no empty or spaced triggers, at most TriggerMatcher.MaxWordLength characters, and
// unique ignoring case. current is the trigger of the snippet being edited, which it may keep.
public static class TriggerRules
{
    public static string Normalize(string? trigger) => (trigger ?? "").Trim();

    public static TriggerError? Validate(string? trigger, IEnumerable<string> existing, string? current = null)
    {
        var t = Normalize(trigger);
        if (t.Length == 0) return TriggerError.Empty;
        if (t.Any(char.IsWhiteSpace)) return TriggerError.HasSpace;
        if (t.Length > TriggerMatcher.MaxWordLength) return TriggerError.TooLong;
        if (existing.Any(e => !string.Equals(e, current, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(e, t, StringComparison.OrdinalIgnoreCase)))
            return TriggerError.Duplicate;
        return null;
    }

    public static string Message(TriggerError error) => error switch
    {
        TriggerError.Empty => Loc.T("Editor.EnterTrigger"),
        TriggerError.HasSpace => Loc.T("Editor.NoSpaces"),
        TriggerError.TooLong => Loc.F("Editor.TooLong", TriggerMatcher.MaxWordLength),
        _ => Loc.T("Main.Duplicate"),
    };
}
