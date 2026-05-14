using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class TableBorder
{
    public float Thickness { get; set; } = 1f;
    public DocColor Color { get; set; } = new DocColor(0, 0, 0);
}
