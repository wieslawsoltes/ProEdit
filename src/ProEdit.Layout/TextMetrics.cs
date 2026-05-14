namespace ProEdit.Layout;

public readonly struct TextMetrics
{
    public float Width { get; }
    public float Height { get; }
    public float Ascent { get; }
    public float Descent { get; }

    public TextMetrics(float width, float height, float ascent, float descent)
    {
        Width = width;
        Height = height;
        Ascent = ascent;
        Descent = descent;
    }
}
