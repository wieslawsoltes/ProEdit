namespace Vibe.Office.Documents;

public readonly struct TextRange
{
    public TextPosition Start { get; }
    public TextPosition End { get; }

    public TextRange(TextPosition start, TextPosition end)
    {
        Start = start;
        End = end;
    }

    public bool IsEmpty => Start.CompareTo(End) == 0;

    public TextRange Normalize()
    {
        return Start <= End ? this : new TextRange(End, Start);
    }

    public bool Contains(TextPosition position)
    {
        var normalized = Normalize();
        return position >= normalized.Start && position < normalized.End;
    }
}
