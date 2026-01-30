namespace Vibe.Office.Layout;

internal readonly struct WrapInterval
{
    public WrapInterval(float left, float right)
    {
        Left = left;
        Right = right;
    }

    public float Left { get; }

    public float Right { get; }

    public float Width => Right - Left;
}
