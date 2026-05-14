using System.Text;

namespace ProEdit.Layout;

public static class Utf16Decoder
{
    public static bool TryDecodeFromUtf16(ReadOnlySpan<char> text, out Rune rune, out int charsConsumed)
    {
        if (text.IsEmpty)
        {
            rune = default;
            charsConsumed = 0;
            return false;
        }

        var first = text[0];
        if (char.IsHighSurrogate(first) && text.Length > 1 && char.IsLowSurrogate(text[1]))
        {
            if (Rune.TryCreate(first, text[1], out rune))
            {
                charsConsumed = 2;
                return true;
            }
        }

        if (Rune.TryCreate(first, out rune))
        {
            charsConsumed = 1;
            return true;
        }

        rune = default;
        charsConsumed = 1;
        return false;
    }
}
