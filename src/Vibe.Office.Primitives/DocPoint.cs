namespace Vibe.Office.Primitives;

public readonly struct DocPoint
{
    public float X { get; }
    public float Y { get; }

    public DocPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static DocPoint operator +(DocPoint left, DocPoint right) => new DocPoint(left.X + right.X, left.Y + right.Y);
    public static DocPoint operator -(DocPoint left, DocPoint right) => new DocPoint(left.X - right.X, left.Y - right.Y);
}
