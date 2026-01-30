using System.Text;

namespace Vibe.Office.Layout;

public static class TextVerticalOrientation
{
    public static bool IsUprightCluster(ReadOnlySpan<char> cluster)
    {
        if (cluster.IsEmpty)
        {
            return false;
        }

        if (!Utf16Decoder.TryDecodeFromUtf16(cluster, out var rune, out _))
        {
            rune = new Rune(cluster[0]);
        }

        return IsUprightRune(rune);
    }

    public static bool IsUprightRune(Rune rune)
    {
        if (TextScript.IsEastAsianRune(rune))
        {
            return true;
        }

        if (TextEastAsianWidth.IsFullWideOrHalf(rune.Value))
        {
            return true;
        }

        return TextExtendedPictographic.IsExtendedPictographic(rune.Value);
    }
}
