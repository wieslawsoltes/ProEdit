using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public sealed class AutoCorrectService : IAutoCorrectService
{
    private readonly Dictionary<string, string> _replacements = new(StringComparer.OrdinalIgnoreCase);

    public AutoCorrectOptions Options { get; } = new AutoCorrectOptions();
    public IReadOnlyList<AutoCorrectRule> Rules => _rules;

    private readonly List<AutoCorrectRule> _rules = new();

    public AutoCorrectService(IEnumerable<AutoCorrectRule>? rules = null)
    {
        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                AddRule(rule);
            }
        }
    }

    public void AddRule(AutoCorrectRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Trigger) || string.IsNullOrWhiteSpace(rule.Replacement))
        {
            return;
        }

        var trigger = rule.Trigger.Trim();
        var replacement = rule.Replacement.Trim();
        _replacements[trigger] = replacement;
        _rules.Add(new AutoCorrectRule(trigger, replacement));
    }

    public bool TryGetReplacement(IEditorMutableSession session, ReadOnlySpan<char> insertedText, out AutoCorrectReplacement replacement)
    {
        replacement = default;
        if (!Options.Enabled || insertedText.IsEmpty)
        {
            return false;
        }

        if (!ContainsWordTerminator(insertedText))
        {
            return false;
        }

        var caret = session.Caret;
        var paragraph = session.Document.GetParagraph(caret.ParagraphIndex);
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (Options.FixDoubleSpace && TryGetDoubleSpaceReplacement(caret, text, out replacement))
        {
            return true;
        }

        var offset = Math.Clamp(caret.Offset - 1, 0, text.Length - 1);
        if (!ProofingTokenizer.TryGetWordAtOffset(text.AsSpan(), offset, out var span))
        {
            return false;
        }

        var word = text.AsSpan(span.Start, span.Length);
        var wordText = word.ToString();
        if (!_replacements.TryGetValue(wordText, out var replacementText))
        {
            return false;
        }

        replacementText = ApplyCasing(wordText, replacementText);
        replacement = new AutoCorrectReplacement(caret.ParagraphIndex, span.Start, span.Length, replacementText);
        return true;
    }

    private static bool ContainsWordTerminator(ReadOnlySpan<char> text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }

            if (ch is '.' or '!' or '?' or ',' or ';' or ':')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDoubleSpaceReplacement(TextPosition caret, string text, out AutoCorrectReplacement replacement)
    {
        replacement = default;
        if (caret.Offset < 2 || caret.Offset > text.Length)
        {
            return false;
        }

        if (text[caret.Offset - 1] != ' ' || text[caret.Offset - 2] != ' ')
        {
            return false;
        }

        replacement = new AutoCorrectReplacement(caret.ParagraphIndex, caret.Offset - 2, 2, " ");
        return true;
    }

    private static string ApplyCasing(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement))
        {
            return replacement;
        }

        if (IsAllUpper(original))
        {
            return replacement.ToUpperInvariant();
        }

        if (char.IsUpper(original[0]))
        {
            var lower = replacement.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        return replacement;
    }

    private static bool IsAllUpper(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetter(ch) && !char.IsUpper(ch))
            {
                return false;
            }
        }

        return true;
    }

    public static AutoCorrectService CreateDefault()
    {
        var service = new AutoCorrectService();
        service.AddRule(new AutoCorrectRule("teh", "the"));
        service.AddRule(new AutoCorrectRule("adn", "and"));
        service.AddRule(new AutoCorrectRule("dont", "don't"));
        service.AddRule(new AutoCorrectRule("cant", "can't"));
        service.AddRule(new AutoCorrectRule("wont", "won't"));
        return service;
    }
}
