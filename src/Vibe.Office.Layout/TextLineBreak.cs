using System.Globalization;

namespace Vibe.Office.Layout;

internal enum LineBreakClass
{
    AI,
    AK,
    AL,
    AP,
    AS,
    B2,
    BA,
    BB,
    BK,
    CB,
    CJ,
    CL,
    CM,
    CP,
    CR,
    EB,
    EM,
    EX,
    GL,
    H2,
    H3,
    HL,
    HY,
    ID,
    IN,
    IS,
    JL,
    JV,
    JT,
    LF,
    NL,
    NS,
    NU,
    OP,
    PO,
    PR,
    QU,
    RI,
    SA,
    SG,
    SP,
    SY,
    VF,
    VI,
    WJ,
    XX,
    ZW,
    ZWJ
}

internal static class TextLineBreak
{
    public static LineBreakClass GetLineBreakClass(int codepoint)
    {
        var ranges = TextLineBreakData.LineBreakRanges;
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
                return range.Class;
            }
        }

        return TextLineBreakData.DefaultClass;
    }

    public static LineBreakClass ResolveLineBreakClass(LineBreakClass klass, UnicodeCategory category)
    {
        return klass switch
        {
            LineBreakClass.AI or LineBreakClass.SG or LineBreakClass.XX => LineBreakClass.AL,
            LineBreakClass.SA => category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                ? LineBreakClass.CM
                : LineBreakClass.AL,
            LineBreakClass.CJ => LineBreakClass.NS,
            _ => klass
        };
    }
}
