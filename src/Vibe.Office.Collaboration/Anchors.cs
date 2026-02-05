namespace Vibe.Office.Collaboration;

/// <summary>
/// Bias used to resolve ambiguous insertions at the same offset.
/// </summary>
public enum AnchorBias
{
    /// <summary>
    /// Anchor resolves before concurrent insertions at the same offset.
    /// </summary>
    Before,

    /// <summary>
    /// Anchor resolves after concurrent insertions at the same offset.
    /// </summary>
    After
}

/// <summary>
/// Identifies a stable position within a node using its NodeId and an offset.
/// </summary>
public readonly record struct TextAnchor(Guid NodeId, int Offset, AnchorBias Bias)
{
    /// <summary>
    /// Creates a new anchor pointing before inserts at the given offset.
    /// </summary>
    public static TextAnchor Before(Guid nodeId, int offset) => new(nodeId, offset, AnchorBias.Before);

    /// <summary>
    /// Creates a new anchor pointing after inserts at the given offset.
    /// </summary>
    public static TextAnchor After(Guid nodeId, int offset) => new(nodeId, offset, AnchorBias.After);
}

/// <summary>
/// Defines an ordered position token used by list CRDTs to establish a total order.
/// </summary>
public readonly record struct PositionToken(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Represents a text range using anchors.
/// </summary>
public readonly record struct AnchorRange(TextAnchor Start, TextAnchor End)
{
    public bool IsEmpty => Start.NodeId == End.NodeId && Start.Offset == End.Offset && Start.Bias == End.Bias;
}
