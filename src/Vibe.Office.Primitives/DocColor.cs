namespace Vibe.Office.Primitives;

public readonly struct DocColor : IEquatable<DocColor>
{
    public byte A { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public DocColor(byte r, byte g, byte b, byte a = 255)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    public static DocColor FromArgb(byte a, byte r, byte g, byte b) => new DocColor(r, g, b, a);

    public uint ToArgb()
    {
        return (uint)((A << 24) | (R << 16) | (G << 8) | B);
    }

    public bool Equals(DocColor other) => A == other.A && R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is DocColor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);

    public static bool operator ==(DocColor left, DocColor right) => left.Equals(right);
    public static bool operator !=(DocColor left, DocColor right) => !left.Equals(right);

    public static DocColor Black => new DocColor(0, 0, 0);
    public static DocColor White => new DocColor(255, 255, 255);
    public static DocColor Transparent => new DocColor(0, 0, 0, 0);
    public static DocColor SelectionBlue => new DocColor(51, 153, 255, 128);
}
