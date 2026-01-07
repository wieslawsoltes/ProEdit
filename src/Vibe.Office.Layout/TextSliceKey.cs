namespace Vibe.Office.Layout;

internal readonly struct TextSliceKey : IEquatable<TextSliceKey>
{
    private readonly string _source;
    private readonly int _start;
    private readonly int _length;
    private readonly int _hash;

    public TextSliceKey(string source, int start, int length)
    {
        _source = source ?? string.Empty;
        _start = start;
        _length = Math.Max(0, length);
        _hash = ComputeHash(_source.AsSpan(_start, _length));
    }

    public bool Equals(TextSliceKey other)
    {
        if (_hash != other._hash || _length != other._length)
        {
            return false;
        }

        return _source.AsSpan(_start, _length).SequenceEqual(other._source.AsSpan(other._start, other._length));
    }

    public override bool Equals(object? obj) => obj is TextSliceKey other && Equals(other);

    public override int GetHashCode() => _hash;

    private static int ComputeHash(ReadOnlySpan<char> span)
    {
        var hash = new HashCode();
        for (var i = 0; i < span.Length; i++)
        {
            hash.Add(span[i]);
        }

        return hash.ToHashCode();
    }
}
