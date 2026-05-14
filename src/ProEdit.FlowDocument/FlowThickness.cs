using System.ComponentModel;
using System.Globalization;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents thickness values for margins or padding.
/// </summary>
[TypeConverter(typeof(FlowThicknessConverter))]
public readonly struct FlowThickness : IEquatable<FlowThickness>
{
    /// <summary>
    /// Gets the left thickness.
    /// </summary>
    public double Left { get; }

    /// <summary>
    /// Gets the top thickness.
    /// </summary>
    public double Top { get; }

    /// <summary>
    /// Gets the right thickness.
    /// </summary>
    public double Right { get; }

    /// <summary>
    /// Gets the bottom thickness.
    /// </summary>
    public double Bottom { get; }

    /// <summary>
    /// Gets the sum of left and right.
    /// </summary>
    public double Horizontal => Left + Right;

    /// <summary>
    /// Gets the sum of top and bottom.
    /// </summary>
    public double Vertical => Top + Bottom;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowThickness"/> struct.
    /// </summary>
    public FlowThickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>
    /// Creates a uniform thickness value.
    /// </summary>
    public static FlowThickness Uniform(double value) => new FlowThickness(value, value, value, value);

    /// <summary>
    /// Gets a value indicating whether all sides are zero.
    /// </summary>
    public bool IsEmpty => Left.Equals(0d) && Top.Equals(0d) && Right.Equals(0d) && Bottom.Equals(0d);

    /// <inheritdoc />
    public bool Equals(FlowThickness other)
    {
        return Left.Equals(other.Left)
               && Top.Equals(other.Top)
               && Right.Equals(other.Right)
               && Bottom.Equals(other.Bottom);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FlowThickness other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    /// <summary>
    /// Parses a thickness string into a <see cref="FlowThickness"/> value.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="thickness">The parsed thickness value.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string? text, out FlowThickness thickness)
    {
        thickness = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            if (TryParseDouble(parts[0], out var uniform))
            {
                thickness = Uniform(uniform);
                return true;
            }

            return false;
        }

        if (parts.Length == 2)
        {
            if (TryParseDouble(parts[0], out var horizontal) && TryParseDouble(parts[1], out var vertical))
            {
                thickness = new FlowThickness(horizontal, vertical, horizontal, vertical);
                return true;
            }

            return false;
        }

        if (parts.Length == 4)
        {
            if (TryParseDouble(parts[0], out var left)
                && TryParseDouble(parts[1], out var top)
                && TryParseDouble(parts[2], out var right)
                && TryParseDouble(parts[3], out var bottom))
            {
                thickness = new FlowThickness(left, top, right, bottom);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Left},{Top},{Right},{Bottom}");
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(FlowThickness left, FlowThickness right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(FlowThickness left, FlowThickness right) => !left.Equals(right);
}
