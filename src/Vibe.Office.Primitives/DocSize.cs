namespace Vibe.Office.Primitives;

public readonly struct DocSize
{
    public float Width { get; }
    public float Height { get; }

    public DocSize(float width, float height)
    {
        Width = width;
        Height = height;
    }
}
