namespace Vibe.Office.Layout;

public readonly struct TextSlice : IEquatable<TextSlice>
{
    public static TextSlice Empty => new TextSlice(string.Empty, 0, 0);

    public string Source { get; }
    public int Start { get; }
    public int Length { get; }

    public TextSlice(string source, int start, int length)
    {
        Source = source ?? string.Empty;
        if (start < 0)
        {
            start = 0;
        }

        if (length < 0)
        {
            length = 0;
        }

        if (start > Source.Length)
        {
            start = Source.Length;
            length = 0;
        }
        else if (start + length > Source.Length)
        {
            length = Source.Length - start;
        }

        Start = start;
        Length = length;
    }

    public bool IsEmpty => Length == 0 || Source.Length == 0;

    public ReadOnlySpan<char> Span => IsEmpty ? ReadOnlySpan<char>.Empty : Source.AsSpan(Start, Length);

    public override string ToString()
    {
        return IsEmpty ? string.Empty : Source.Substring(Start, Length);
    }

    public bool Equals(TextSlice other)
    {
        return ReferenceEquals(Source, other.Source)
               && Start == other.Start
               && Length == other.Length;
    }

    public override bool Equals(object? obj) => obj is TextSlice other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Source, Start, Length);
}
