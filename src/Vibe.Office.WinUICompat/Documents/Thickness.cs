namespace Vibe.Office.WinUICompat.Documents;

public readonly struct Thickness : IEquatable<Thickness>
{
    public Thickness(double uniformLength)
        : this(uniformLength, uniformLength, uniformLength, uniformLength)
    {
    }

    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }

    public double Horizontal => Left + Right;

    public double Vertical => Top + Bottom;

    public bool IsEmpty => Left.Equals(0d) && Top.Equals(0d) && Right.Equals(0d) && Bottom.Equals(0d);

    public bool Equals(Thickness other)
    {
        return Left.Equals(other.Left)
               && Top.Equals(other.Top)
               && Right.Equals(other.Right)
               && Bottom.Equals(other.Bottom);
    }

    public override bool Equals(object? obj)
    {
        return obj is Thickness other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Top, Right, Bottom);
    }

    public override string ToString()
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Left},{Top},{Right},{Bottom}");
    }

    public static bool operator ==(Thickness left, Thickness right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Thickness left, Thickness right)
    {
        return !left.Equals(right);
    }
}
