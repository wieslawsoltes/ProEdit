namespace ProEdit.Documents;

public readonly struct TextPosition : IComparable<TextPosition>
{
    public int ParagraphIndex { get; }
    public int Offset { get; }

    public TextPosition(int paragraphIndex, int offset)
    {
        ParagraphIndex = paragraphIndex;
        Offset = offset;
    }

    public int CompareTo(TextPosition other)
    {
        var paragraphCompare = ParagraphIndex.CompareTo(other.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return Offset.CompareTo(other.Offset);
    }

    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;
    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;
    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{ParagraphIndex}:{Offset}";
}
