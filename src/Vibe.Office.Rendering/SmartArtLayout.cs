using Vibe.Office.Primitives;

namespace Vibe.Office.Rendering;

public sealed class SmartArtLayout
{
    public SmartArtLayoutKind Kind { get; }
    public IReadOnlyList<SmartArtNodeLayout> Nodes { get; }
    public IReadOnlyList<SmartArtConnectorLayout> Connectors { get; }

    public SmartArtLayout(
        SmartArtLayoutKind kind,
        IReadOnlyList<SmartArtNodeLayout> nodes,
        IReadOnlyList<SmartArtConnectorLayout> connectors)
    {
        Kind = kind;
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
    }
}

public enum SmartArtLayoutKind
{
    List,
    Process,
    Cycle,
    Hierarchy,
    Matrix
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
