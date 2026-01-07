using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed class MathLayout
{
    public MathBox Root { get; }

    public float Width => Root.Width;
    public float Height => Root.Height;
    public float Baseline => Root.Baseline;

    public MathLayout(MathBox root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }
}

public sealed class MathBox
{
    public MathElement Element { get; }
    public float Width { get; }
    public float Height { get; }
    public float Baseline { get; }
    public string? Text { get; }
    public TextStyle? Style { get; }
    public IReadOnlyList<MathBoxChild> Children { get; }

    public MathBox(
        MathElement element,
        float width,
        float height,
        float baseline,
        string? text,
        TextStyle? style,
        IReadOnlyList<MathBoxChild>? children = null)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        Width = width;
        Height = height;
        Baseline = baseline;
        Text = text;
        Style = style;
        Children = children ?? Array.Empty<MathBoxChild>();
    }
}

public readonly record struct MathBoxChild(MathBox Box, float X, float Y);
