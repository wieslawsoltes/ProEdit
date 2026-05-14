namespace ProEdit.FlowDocument.Documents;

/// <summary>
/// Provides customization options for FlowDocument conversion.
/// </summary>
public sealed class FlowDocumentConverterOptions
{
    /// <summary>
    /// Default placeholder text for inline UI containers.
    /// </summary>
    public const string DefaultInlineUiPlaceholderText = "\uFFFC";

    /// <summary>
    /// Default placeholder text for block UI containers.
    /// </summary>
    public const string DefaultBlockUiPlaceholderText = "\uFFFC";

    /// <summary>
    /// Prefix used for embedded UI shape names.
    /// </summary>
    public const string DefaultEmbeddedUiShapePrefix = "__flowui:";

    /// <summary>
    /// Gets or sets the placeholder text for inline UI containers.
    /// </summary>
    public string InlineUiPlaceholderText { get; set; } = DefaultInlineUiPlaceholderText;

    /// <summary>
    /// Gets or sets the placeholder text for block UI containers.
    /// </summary>
    public string BlockUiPlaceholderText { get; set; } = DefaultBlockUiPlaceholderText;

    /// <summary>
    /// Gets or sets a value indicating whether UI containers should be represented
    /// as embedded shape placeholders with stable IDs.
    /// </summary>
    public bool EnableEmbeddedUiElements { get; set; }

    /// <summary>
    /// Gets or sets the shape name prefix used for embedded UI containers.
    /// </summary>
    public string EmbeddedUiShapePrefix { get; set; } = DefaultEmbeddedUiShapePrefix;

    /// <summary>
    /// Gets or sets the fallback width used for inline UI placeholders.
    /// </summary>
    public double InlineUiFallbackWidth { get; set; } = 120d;

    /// <summary>
    /// Gets or sets the fallback height used for inline UI placeholders.
    /// </summary>
    public double InlineUiFallbackHeight { get; set; } = 28d;

    /// <summary>
    /// Gets or sets the fallback width used for block UI placeholders.
    /// </summary>
    public double BlockUiFallbackWidth { get; set; } = 320d;

    /// <summary>
    /// Gets or sets the fallback height used for block UI placeholders.
    /// </summary>
    public double BlockUiFallbackHeight { get; set; } = 120d;

    /// <summary>
    /// Gets or sets a factory for inline UI placeholder text.
    /// </summary>
    public Func<object?, string>? InlineUiPlaceholderFactory { get; set; }

    /// <summary>
    /// Gets or sets a factory for block UI placeholder text.
    /// </summary>
    public Func<object?, string>? BlockUiPlaceholderFactory { get; set; }

    /// <summary>
    /// Gets or sets a predicate that determines whether a UI container child should be emitted
    /// as an embedded shape marker when <see cref="EnableEmbeddedUiElements"/> is enabled.
    /// When unset, Avalonia <c>Control</c> children are treated as embeddable.
    /// </summary>
    public Func<object?, bool>? EmbeddedUiElementPredicate { get; set; }

    /// <summary>
    /// Gets or sets a size resolver for embedded UI children.
    /// The boolean argument indicates whether the source container is inline.
    /// Returning <see langword="null"/> falls back to built-in sizing.
    /// </summary>
    public Func<object, bool, (double Width, double Height)?>? EmbeddedUiSizeResolver { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether table cell visual properties
    /// (padding, borders, shading, alignment metadata) should be exported
    /// from FlowDocument cells into the document model.
    /// </summary>
    public bool ExportCellVisualProperties { get; set; } = true;
}
