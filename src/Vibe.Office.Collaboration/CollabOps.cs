namespace Vibe.Office.Collaboration;

/// <summary>
/// Operation kinds supported by the collaboration engine.
/// </summary>
public enum CollabOpKind
{
    InsertText,
    DeleteRange,
    SetParagraphProperties,
    SetInlineProperties,
    InsertBlock,
    DeleteBlock,
    ReplaceBlock,
    ReplaceDocumentResources
}

/// <summary>
/// Base interface for collaboration operations.
/// </summary>
public interface ICollabOp
{
    CollabOpKind Kind { get; }
}

/// <summary>
/// Inserts text at the specified anchor.
/// </summary>
public sealed record InsertTextOp(TextAnchor Anchor, string Text, Guid? AuthorId = null) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.InsertText;
}

/// <summary>
/// Deletes a range of text defined by two anchors.
/// </summary>
public sealed record DeleteRangeOp(TextAnchor Start, TextAnchor End) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.DeleteRange;
}

/// <summary>
/// Updates paragraph properties using a last-writer-wins policy.
/// </summary>
public sealed record SetParagraphPropertiesOp(Guid ParagraphNodeId, IReadOnlyDictionary<string, string> Properties, long Lamport) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.SetParagraphProperties;
}

/// <summary>
/// Updates inline properties using a last-writer-wins policy.
/// </summary>
public sealed record SetInlinePropertiesOp(Guid InlineNodeId, IReadOnlyDictionary<string, string> Properties, long Lamport) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.SetInlineProperties;
}

/// <summary>
/// Inserts a new block into the document structure.
/// </summary>
public sealed record InsertBlockOp(Guid ParentNodeId, PositionToken Position, string BlockType, byte[]? Payload) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.InsertBlock;
}

/// <summary>
/// Deletes a block from the document structure.
/// </summary>
public sealed record DeleteBlockOp(Guid ParentNodeId, PositionToken Position, Guid BlockNodeId) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.DeleteBlock;
}

/// <summary>
/// Replaces a block with updated content.
/// </summary>
public sealed record ReplaceBlockOp(Guid BlockNodeId, byte[] Payload) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.ReplaceBlock;
}

/// <summary>
/// Replaces document-level resources (styles, defaults, properties, etc.).
/// </summary>
public sealed record ReplaceDocumentResourcesOp(byte[] Payload) : ICollabOp
{
    public CollabOpKind Kind => CollabOpKind.ReplaceDocumentResources;
}
