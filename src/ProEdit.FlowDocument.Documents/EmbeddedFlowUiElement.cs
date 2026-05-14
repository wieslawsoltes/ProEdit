namespace ProEdit.FlowDocument.Documents;

/// <summary>
/// Describes an embedded UI element emitted during FlowDocument conversion.
/// </summary>
public sealed class EmbeddedFlowUiElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedFlowUiElement"/> class.
    /// </summary>
    /// <param name="id">Stable element identifier.</param>
    /// <param name="child">Hosted child object.</param>
    /// <param name="isInline">Whether the source container was inline.</param>
    public EmbeddedFlowUiElement(string id, object child, bool isInline)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("ID is required.", nameof(id)) : id;
        Child = child ?? throw new ArgumentNullException(nameof(child));
        IsInline = isInline;
    }

    /// <summary>
    /// Gets the stable element identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the hosted child object.
    /// </summary>
    public object Child { get; }

    /// <summary>
    /// Gets a value indicating whether the source container was inline.
    /// </summary>
    public bool IsInline { get; }
}
