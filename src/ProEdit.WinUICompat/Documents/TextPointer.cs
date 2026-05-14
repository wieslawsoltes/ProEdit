namespace ProEdit.WinUICompat.Documents;

public sealed class TextPointer : IComparable<TextPointer>, IEquatable<TextPointer>
{
    public TextPointer(int paragraphIndex, int offset, LogicalDirection direction = LogicalDirection.Forward)
    {
        if (paragraphIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        ParagraphIndex = paragraphIndex;
        Offset = offset;
        LogicalDirection = direction;
    }

    public int ParagraphIndex { get; }

    public int Offset { get; }

    public LogicalDirection LogicalDirection { get; }

    public int CompareTo(TextPointer? other)
    {
        if (other is null)
        {
            return 1;
        }

        var paragraphCompare = ParagraphIndex.CompareTo(other.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return Offset.CompareTo(other.Offset);
    }

    public bool Equals(TextPointer? other)
    {
        return other is not null
            && ParagraphIndex == other.ParagraphIndex
            && Offset == other.Offset
            && LogicalDirection == other.LogicalDirection;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextPointer other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ParagraphIndex, Offset, (int)LogicalDirection);
    }

    public override string ToString()
    {
        return $"{ParagraphIndex}:{Offset}:{LogicalDirection}";
    }
}
