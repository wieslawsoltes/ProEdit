namespace Vibe.Office.Layout;

internal static class TextScript
{
    public static bool IsLatinChar(char ch)
    {
        var code = (int)ch;
        return (code >= 0x0041 && code <= 0x005A)
               || (code >= 0x0061 && code <= 0x007A)
               || (code >= 0x00C0 && code <= 0x024F)
               || (code >= 0x1E00 && code <= 0x1EFF);
    }
}
