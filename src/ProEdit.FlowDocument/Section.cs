using Avalonia;
using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a section block containing nested blocks.
/// </summary>
public sealed class Section : Block
{
    /// <summary>
    /// Identifies the <see cref="HasTrailingParagraphBreakOnPaste"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> HasTrailingParagraphBreakOnPasteProperty =
        AvaloniaProperty.Register<Section, bool?>(nameof(HasTrailingParagraphBreakOnPaste));

    /// <summary>
    /// Gets the block collection for the section.
    /// </summary>
    [Content]
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets trailing-paragraph-break paste behavior metadata.
    /// </summary>
    public bool? HasTrailingParagraphBreakOnPaste
    {
        get => GetValue(HasTrailingParagraphBreakOnPasteProperty);
        set => SetValue(HasTrailingParagraphBreakOnPasteProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Section"/> class.
    /// </summary>
    public Section()
    {
        Blocks = new BlockCollection(this);
    }
}
