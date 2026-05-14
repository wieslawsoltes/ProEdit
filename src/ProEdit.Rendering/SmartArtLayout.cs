using ProEdit.Primitives;

namespace ProEdit.Rendering;

public sealed class SmartArtLayout
{
    public SmartArtLayoutKind Kind { get; }
    public IReadOnlyList<SmartArtNodeLayout> Nodes { get; }
    public IReadOnlyList<SmartArtConnectorLayout> Connectors { get; }
    public SmartArtStyle? Style { get; }

    public SmartArtLayout(
        SmartArtLayoutKind kind,
        IReadOnlyList<SmartArtNodeLayout> nodes,
        IReadOnlyList<SmartArtConnectorLayout> connectors,
        SmartArtStyle? style)
    {
        Kind = kind;
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        Style = style;
    }
}

public enum SmartArtLayoutKind
{
    List,
    Process,
    Cycle,
    Hierarchy,
    Matrix,
    Relationship,
    Pyramid
}

public readonly record struct SmartArtNodeLayout(
    string Id,
    string Text,
    DocRect Bounds,
    int Level,
    int Index);

public readonly record struct SmartArtConnectorLayout(
    string FromId,
    string ToId);

public sealed class SmartArtStyle
{
    public IReadOnlyList<DocColor> NodeFillPalette { get; }
    public DocColor? NodeLineColor { get; set; }
    public float? NodeLineWidth { get; set; }
    public DocColor? ConnectorColor { get; set; }
    public float? ConnectorWidth { get; set; }
    public DocColor? TextColor { get; set; }
    public float? TextSize { get; set; }

    public SmartArtStyle(IReadOnlyList<DocColor> nodeFillPalette)
    {
        NodeFillPalette = nodeFillPalette ?? Array.Empty<DocColor>();
    }
}
