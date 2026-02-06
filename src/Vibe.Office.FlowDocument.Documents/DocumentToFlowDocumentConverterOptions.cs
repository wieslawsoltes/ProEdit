namespace Vibe.Office.FlowDocument.Documents;

/// <summary>
/// Provides customization options for converting <see cref="Vibe.Office.Documents.Document"/>
/// instances back into <see cref="Vibe.Office.FlowDocument.FlowDocument"/> instances.
/// </summary>
public sealed class DocumentToFlowDocumentConverterOptions
{
    /// <summary>
    /// Gets or sets the shape name prefix used to detect embedded UI container markers.
    /// </summary>
    public string EmbeddedUiShapePrefix { get; set; } = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;

    /// <summary>
    /// Gets or sets known embedded UI elements keyed by stable element ID.
    /// </summary>
    public IReadOnlyDictionary<string, EmbeddedFlowUiElement>? EmbeddedUiElementsById { get; set; }

    /// <summary>
    /// Gets or sets a factory that resolves an embedded UI child by ID.
    /// The boolean argument indicates whether the request is inline.
    /// </summary>
    public Func<string, bool, object?>? EmbeddedUiElementFactory { get; set; }

    /// <summary>
    /// Gets or sets the fallback child object for unresolved inline UI containers.
    /// </summary>
    public object? InlineUiPlaceholderChild { get; set; } = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText;

    /// <summary>
    /// Gets or sets the fallback child object for unresolved block UI containers.
    /// </summary>
    public object? BlockUiPlaceholderChild { get; set; } = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText;

    /// <summary>
    /// Gets or sets the placeholder text used when inline non-text objects are flattened to text.
    /// </summary>
    public string InlineUiPlaceholderText { get; set; } = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText;

    /// <summary>
    /// Gets or sets the placeholder text used when block non-text objects are flattened to text.
    /// </summary>
    public string BlockUiPlaceholderText { get; set; } = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText;
}
