namespace Vibe.Office.Editing;

public static class ProofingTokenizer
{
    public static List<ProofingWordSpan> CollectWordSpans(ReadOnlySpan<char> text)
    {
        var result = new List<ProofingWordSpan>();
        if (text.IsEmpty)
        {
            return result;
        }

        var start = -1;
        var hasLetter = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (IsWordChar(ch))
            {
                if (start < 0)
                {
                    start = i;
                    hasLetter = false;
                }

                if (char.IsLetter(ch))
                {
                    hasLetter = true;
                }

                continue;
            }

            if (start >= 0)
            {
                var length = i - start;
                if (length > 0 && hasLetter)
                {
                    result.Add(new ProofingWordSpan(start, length));
                }

                start = -1;
                hasLetter = false;
            }
        }

        if (start >= 0)
        {
            var length = text.Length - start;
            if (length > 0 && hasLetter)
            {
                result.Add(new ProofingWordSpan(start, length));
            }
        }

        return result;
    }

    public static bool TryGetWordAtOffset(ReadOnlySpan<char> text, int offset, out ProofingWordSpan span)
    {
        span = default;
        if (text.IsEmpty || offset < 0)
        {
            return false;
        }

        if (offset >= text.Length)
        {
            offset = text.Length - 1;
        }

        if (!IsWordChar(text[offset]))
        {
            if (offset > 0 && IsWordChar(text[offset - 1]))
            {
                offset -= 1;
            }
            else
            {
                return false;
            }
        }

        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return false;
        }

        var hasLetter = false;
        for (var i = start; i < end; i++)
        {
            if (char.IsLetter(text[i]))
            {
                hasLetter = true;
                break;
            }
        }

        if (!hasLetter)
        {
            return false;
        }

        span = new ProofingWordSpan(start, end - start);
        return true;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch);
}

public readonly record struct ProofingWordSpan(int Start, int Length);
