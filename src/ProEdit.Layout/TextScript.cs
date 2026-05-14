using System.Text;

namespace ProEdit.Layout;

public enum TextScriptKind
{
    Latin,
    EastAsian,
    Complex,
    Neutral
}

public static class TextScript
{
    private static readonly TextScriptData.ScriptRange[] SortedScriptRanges = CreateSortedScriptRanges();

    public static bool IsLatinChar(char ch)
    {
        return IsLatinRune(new Rune(ch));
    }

    public static bool IsLatinChar(Rune rune)
    {
        return IsLatinRune(rune);
    }

    public static bool IsLatinText(ReadOnlySpan<char> text)
    {
        var hasLetter = false;
        var index = 0;
        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            if (Rune.IsLetter(rune))
            {
                hasLetter = true;
                if (!IsLatinRune(rune))
                {
                    return false;
                }
            }

            index += consumed;
        }

        return hasLetter;
    }

    public static TextScriptKind ClassifyRune(Rune rune)
    {
        return ClassifyScript(GetScript(rune.Value));
    }

    public static bool ContainsEastAsian(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            if (IsEastAsianScript(GetScript(rune.Value)))
            {
                return true;
            }

            index += consumed;
        }

        return false;
    }

    public static bool TryGetScriptTag(ReadOnlySpan<char> text, out uint tag)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            var script = GetScript(rune.Value);
            if (!IsNeutralScript(script))
            {
                tag = TextScriptData.ScriptTags[(int)script];
                return true;
            }

            index += consumed;
        }

        tag = 0;
        return false;
    }

    internal static bool IsEastAsianRune(Rune rune)
    {
        return IsEastAsianScript(GetScript(rune.Value));
    }

    internal static bool IsArabicRune(Rune rune)
    {
        return GetScript(rune.Value) == UnicodeScript.Arabic;
    }

    private static bool IsLatinRune(Rune rune)
    {
        return GetScript(rune.Value) == UnicodeScript.Latin;
    }

    private static TextScriptKind ClassifyScript(UnicodeScript script)
    {
        if (IsNeutralScript(script))
        {
            return TextScriptKind.Neutral;
        }

        if (IsEastAsianScript(script))
        {
            return TextScriptKind.EastAsian;
        }

        if (IsComplexScript(script))
        {
            return TextScriptKind.Complex;
        }

        return TextScriptKind.Latin;
    }

    private static bool IsNeutralScript(UnicodeScript script)
    {
        return script == UnicodeScript.Common
               || script == UnicodeScript.Inherited
               || script == UnicodeScript.Unknown;
    }

    private static bool IsEastAsianScript(UnicodeScript script)
    {
        return script == UnicodeScript.Han
               || script == UnicodeScript.Hangul
               || script == UnicodeScript.Hiragana
               || script == UnicodeScript.Katakana
               || script == UnicodeScript.Bopomofo;
    }

    private static bool IsComplexScript(UnicodeScript script)
    {
        return script == UnicodeScript.Adlam
               || script == UnicodeScript.Arabic
               || script == UnicodeScript.Balinese
               || script == UnicodeScript.Bengali
               || script == UnicodeScript.Devanagari
               || script == UnicodeScript.Gujarati
               || script == UnicodeScript.Gurmukhi
               || script == UnicodeScript.HanifiRohingya
               || script == UnicodeScript.Hebrew
               || script == UnicodeScript.Kannada
               || script == UnicodeScript.Khmer
               || script == UnicodeScript.Lao
               || script == UnicodeScript.Lepcha
               || script == UnicodeScript.Malayalam
               || script == UnicodeScript.Mandaic
               || script == UnicodeScript.Myanmar
               || script == UnicodeScript.Nko
               || script == UnicodeScript.OlChiki
               || script == UnicodeScript.Oriya
               || script == UnicodeScript.PhagsPa
               || script == UnicodeScript.Samaritan
               || script == UnicodeScript.Sinhala
               || script == UnicodeScript.Sundanese
               || script == UnicodeScript.SylotiNagri
               || script == UnicodeScript.Syriac
               || script == UnicodeScript.TaiTham
               || script == UnicodeScript.Tamil
               || script == UnicodeScript.Telugu
               || script == UnicodeScript.Thaana
               || script == UnicodeScript.Thai
               || script == UnicodeScript.Tibetan;
    }

    private static UnicodeScript GetScript(int codepoint)
    {
        var ranges = SortedScriptRanges;
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var range = ranges[mid];
            if (codepoint < range.Start)
            {
                hi = mid - 1;
            }
            else if (codepoint > range.End)
            {
                lo = mid + 1;
            }
            else
            {
                return range.Script;
            }
        }

        return TextScriptData.DefaultScript;
    }

    private static TextScriptData.ScriptRange[] CreateSortedScriptRanges()
    {
        var source = TextScriptData.ScriptRanges;
        var ranges = new TextScriptData.ScriptRange[source.Length];
        Array.Copy(source, ranges, source.Length);
        Array.Sort(ranges, static (left, right) =>
        {
            var compare = left.Start.CompareTo(right.Start);
            return compare != 0 ? compare : left.End.CompareTo(right.End);
        });

        return ranges;
    }
}
