namespace ProEdit.Primitives;

public readonly struct DocThickness : IEquatable<DocThickness>
{
    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public DocThickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static DocThickness Uniform(float value) => new DocThickness(value, value, value, value);

    public bool Equals(DocThickness other) => Left.Equals(other.Left)
                                             && Top.Equals(other.Top)
                                             && Right.Equals(other.Right)
                                             && Bottom.Equals(other.Bottom);
    public override bool Equals(object? obj) => obj is DocThickness other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(DocThickness left, DocThickness right) => left.Equals(right);
    public static bool operator !=(DocThickness left, DocThickness right) => !left.Equals(right);
}
