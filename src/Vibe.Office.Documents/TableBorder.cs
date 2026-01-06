using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableBorder
{
    public float Thickness { get; set; } = 1f;
    public DocColor Color { get; set; } = new DocColor(0, 0, 0);
}
