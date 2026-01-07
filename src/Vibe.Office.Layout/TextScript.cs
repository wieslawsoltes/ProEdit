using System.Text;

namespace Vibe.Office.Layout;

internal enum TextScriptKind
{
    Latin,
    EastAsian,
    Complex,
    Neutral
}

internal static class TextScript
{
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
        var category = Rune.GetUnicodeCategory(rune);
        if (category == System.Globalization.UnicodeCategory.NonSpacingMark
            || category == System.Globalization.UnicodeCategory.SpacingCombiningMark
            || category == System.Globalization.UnicodeCategory.EnclosingMark)
        {
            return TextScriptKind.Neutral;
        }

        if (Rune.IsWhiteSpace(rune) || Rune.IsDigit(rune) || IsSymbolOrPunctuation(category))
        {
            return TextScriptKind.Neutral;
        }

        if (IsEastAsianRune(rune))
        {
            return TextScriptKind.EastAsian;
        }

        if (IsComplexScriptRune(rune))
        {
            return TextScriptKind.Complex;
        }

        if (IsLatinRune(rune))
        {
            return TextScriptKind.Latin;
        }

        return TextScriptKind.Latin;
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

            if (IsEastAsianRune(rune))
            {
                return true;
            }

            index += consumed;
        }

        return false;
    }

    internal static bool IsEastAsianRune(Rune rune)
    {
        return IsEastAsianRuneInternal(rune);
    }

    private static bool IsLatinRune(Rune rune)
    {
        var code = rune.Value;
        return (code >= 0x0041 && code <= 0x005A)
               || (code >= 0x0061 && code <= 0x007A)
               || (code >= 0x00C0 && code <= 0x024F)
               || (code >= 0x1E00 && code <= 0x1EFF);
    }

    private static bool IsEastAsianRuneInternal(Rune rune)
    {
        var code = rune.Value;
        return (code >= 0x1100 && code <= 0x11FF) // Hangul Jamo
               || (code >= 0x3040 && code <= 0x30FF) // Hiragana + Katakana
               || (code >= 0x31F0 && code <= 0x31FF) // Katakana Phonetic Extensions
               || (code >= 0x3130 && code <= 0x318F) // Hangul Compatibility Jamo
               || (code >= 0x3300 && code <= 0x33FF) // CJK Compatibility
               || (code >= 0x3400 && code <= 0x4DBF) // CJK Unified Ideographs Extension A
               || (code >= 0x4E00 && code <= 0x9FFF) // CJK Unified Ideographs
               || (code >= 0xA960 && code <= 0xA97F) // Hangul Jamo Extended-A
               || (code >= 0xAC00 && code <= 0xD7AF) // Hangul Syllables
               || (code >= 0xD7B0 && code <= 0xD7FF) // Hangul Jamo Extended-B
               || (code >= 0xF900 && code <= 0xFAFF) // CJK Compatibility Ideographs
               || (code >= 0xFE30 && code <= 0xFE4F) // CJK Compatibility Forms
               || (code >= 0x20000 && code <= 0x2A6DF) // CJK Extension B
               || (code >= 0x2A700 && code <= 0x2B73F) // CJK Extension C
               || (code >= 0x2B740 && code <= 0x2B81F) // CJK Extension D
               || (code >= 0x2B820 && code <= 0x2CEAF) // CJK Extension E
               || (code >= 0x2CEB0 && code <= 0x2EBEF) // CJK Extension F
               || (code >= 0x2F800 && code <= 0x2FA1F); // CJK Compatibility Ideographs Supplement
    }

    private static bool IsComplexScriptRune(Rune rune)
    {
        var code = rune.Value;
        return (code >= 0x0590 && code <= 0x05FF) // Hebrew
               || (code >= 0x0600 && code <= 0x06FF) // Arabic
               || (code >= 0x0700 && code <= 0x074F) // Syriac
               || (code >= 0x0750 && code <= 0x077F) // Arabic Supplement
               || (code >= 0x0780 && code <= 0x07BF) // Thaana
               || (code >= 0x07C0 && code <= 0x07FF) // NKo
               || (code >= 0x08A0 && code <= 0x08FF) // Arabic Extended-A
               || (code >= 0x0900 && code <= 0x0D7F) // Indic scripts
               || (code >= 0x0E00 && code <= 0x0E7F) // Thai
               || (code >= 0x0E80 && code <= 0x0EFF) // Lao
               || (code >= 0x0F00 && code <= 0x0FFF) // Tibetan
               || (code >= 0x1000 && code <= 0x109F) // Myanmar
               || (code >= 0x1780 && code <= 0x17FF) // Khmer
               || (code >= 0x1A00 && code <= 0x1AFF) // Tai Tham
               || (code >= 0x1B00 && code <= 0x1B7F) // Balinese
               || (code >= 0x1B80 && code <= 0x1BBF) // Sundanese
               || (code >= 0x1C00 && code <= 0x1C4F) // Lepcha
               || (code >= 0x1C50 && code <= 0x1C7F) // Ol Chiki
               || (code >= 0x1CD0 && code <= 0x1CFF) // Vedic Extensions
               || (code >= 0xA800 && code <= 0xA82F) // Syloti Nagri
               || (code >= 0xA840 && code <= 0xA87F) // Phags-pa
               || (code >= 0xFB50 && code <= 0xFDFF) // Arabic Presentation Forms-A
               || (code >= 0xFE70 && code <= 0xFEFF); // Arabic Presentation Forms-B
    }

    private static bool IsSymbolOrPunctuation(System.Globalization.UnicodeCategory category)
    {
        return category == System.Globalization.UnicodeCategory.ConnectorPunctuation
               || category == System.Globalization.UnicodeCategory.DashPunctuation
               || category == System.Globalization.UnicodeCategory.OpenPunctuation
               || category == System.Globalization.UnicodeCategory.ClosePunctuation
               || category == System.Globalization.UnicodeCategory.InitialQuotePunctuation
               || category == System.Globalization.UnicodeCategory.FinalQuotePunctuation
               || category == System.Globalization.UnicodeCategory.OtherPunctuation
               || category == System.Globalization.UnicodeCategory.MathSymbol
               || category == System.Globalization.UnicodeCategory.CurrencySymbol
               || category == System.Globalization.UnicodeCategory.ModifierSymbol
               || category == System.Globalization.UnicodeCategory.OtherSymbol;
    }
}
