using System.Text;
namespace BananaNana.Input;

// Rolling matcher over the word being typed. Holds only the current word (bounded, in memory),
// never a key history. A match fires when a separator follows a word that equals a trigger.
public sealed class TriggerMatcher
{
    public const int MaxWordLength = 64;

    readonly StringBuilder word = new();
    readonly HashSet<string> triggers = new(StringComparer.OrdinalIgnoreCase);
    readonly string separators;
    // Set when the start of the current word is unknown (overflow, or Backspace past what we saw).
    bool unknownWord;

    // Enter is deliberately not a default separator: in Teams it sends the message before we can expand.
    public TriggerMatcher(IEnumerable<string> triggers, string separators = " ")
    {
        this.separators = separators;
        SetTriggers(triggers);
    }

    public void SetTriggers(IEnumerable<string> list)
    {
        triggers.Clear();
        foreach (var t in list)
            if (t.Length is > 0 and <= MaxWordLength && !t.Any(separators.Contains)) triggers.Add(t);
    }

    // Returns the matched trigger (as stored) when c completes one, else null.
    public string? OnChar(char c)
    {
        if (separators.Contains(c))
        {
            string? match = !unknownWord && triggers.TryGetValue(word.ToString(), out var t) ? t : null;
            Reset();
            return match;
        }
        if (unknownWord) return null;
        if (word.Length == MaxWordLength) { word.Clear(); unknownWord = true; return null; }
        word.Append(c);
        return null;
    }

    public void OnBackspace()
    {
        if (word.Length > 0) word.Length--;
        else unknownWord = true;
    }

    // Caret moved or context changed (click, focus switch, Enter, arrows, shortcuts): start a fresh word.
    public void Reset()
    {
        word.Clear();
        unknownWord = false;
    }
}
