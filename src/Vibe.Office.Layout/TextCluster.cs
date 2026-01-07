using System.Globalization;

namespace Vibe.Office.Layout;

public static class TextCluster
{
    public static int GetNextClusterLength(ReadOnlySpan<char> text, int start)
    {
        if ((uint)start >= (uint)text.Length)
        {
            return 0;
        }

        var slice = text.Slice(start);
        var length = StringInfo.GetNextTextElementLength(slice);
        return length <= 0 ? 1 : length;
    }
}
