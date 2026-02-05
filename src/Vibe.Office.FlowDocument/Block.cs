using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Base type for FlowDocument block elements.
/// </summary>
public abstract class Block : TextElement
{
    /// <summary>
    /// Identifies the <see cref="Margin"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> MarginProperty =
        AvaloniaProperty.Register<Block, FlowThickness>(nameof(Margin));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<Block, FlowTextAlignment?>(nameof(TextAlignment));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<Block, double?>(nameof(LineHeight));

    /// <summary>
    /// Identifies the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> PaddingProperty =
        AvaloniaProperty.Register<Block, FlowThickness>(nameof(Padding));

    /// <summary>
    /// Identifies the <see cref="BorderThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> BorderThicknessProperty =
        AvaloniaProperty.Register<Block, FlowThickness>(nameof(BorderThickness));

    /// <summary>
    /// Identifies the <see cref="BorderBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BorderBrushProperty =
        AvaloniaProperty.Register<Block, string?>(nameof(BorderBrush));

    /// <summary>
    /// Identifies the <see cref="FlowDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FlowDirectionProperty =
        AvaloniaProperty.Register<Block, string?>(nameof(FlowDirection));

    /// <summary>
    /// Identifies the <see cref="LineStackingStrategy"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LineStackingStrategyProperty =
        AvaloniaProperty.Register<Block, string?>(nameof(LineStackingStrategy));

    /// <summary>
    /// Identifies the <see cref="BreakColumnBefore"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> BreakColumnBeforeProperty =
        AvaloniaProperty.Register<Block, bool?>(nameof(BreakColumnBefore));

    /// <summary>
    /// Identifies the <see cref="BreakPageBefore"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> BreakPageBeforeProperty =
        AvaloniaProperty.Register<Block, bool?>(nameof(BreakPageBefore));

    /// <summary>
    /// Identifies the <see cref="ClearFloaters"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ClearFloatersProperty =
        AvaloniaProperty.Register<Block, string?>(nameof(ClearFloaters));

    /// <summary>
    /// Identifies the <see cref="IsHyphenationEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> IsHyphenationEnabledProperty =
        AvaloniaProperty.Register<Block, bool?>(nameof(IsHyphenationEnabled));

    /// <summary>
    /// Gets or sets the block margin.
    /// </summary>
    public FlowThickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the text alignment for the block.
    /// </summary>
    public FlowTextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the line height for the block.
    /// </summary>
    public double? LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets block padding.
    /// </summary>
    public FlowThickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets block border thickness.
    /// </summary>
    public FlowThickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets block border brush.
    /// </summary>
    public string? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the block flow direction.
    /// </summary>
    public string? FlowDirection
    {
        get => GetValue(FlowDirectionProperty);
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets line stacking strategy metadata.
    /// </summary>
    public string? LineStackingStrategy
    {
        get => GetValue(LineStackingStrategyProperty);
        set => SetValue(LineStackingStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a new column should begin before this block.
    /// </summary>
    public bool? BreakColumnBefore
    {
        get => GetValue(BreakColumnBeforeProperty);
        set => SetValue(BreakColumnBeforeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a new page should begin before this block.
    /// </summary>
    public bool? BreakPageBefore
    {
        get => GetValue(BreakPageBeforeProperty);
        set => SetValue(BreakPageBeforeProperty, value);
    }

    /// <summary>
    /// Gets or sets floater clearing metadata.
    /// </summary>
    public string? ClearFloaters
    {
        get => GetValue(ClearFloatersProperty);
        set => SetValue(ClearFloatersProperty, value);
    }

    /// <summary>
    /// Gets or sets whether hyphenation is enabled.
    /// </summary>
    public bool? IsHyphenationEnabled
    {
        get => GetValue(IsHyphenationEnabledProperty);
        set => SetValue(IsHyphenationEnabledProperty, value);
    }

    /// <summary>
    /// Gets sibling blocks for this block.
    /// </summary>
    public BlockCollection? SiblingBlocks
    {
        get
        {
            if (Parent is FlowDocument document)
            {
                return document.Blocks;
            }

            if (Parent is Section section)
            {
                return section.Blocks;
            }

            if (Parent is ListItem listItem)
            {
                return listItem.Blocks;
            }

            if (Parent is TableCell cell)
            {
                return cell.Blocks;
            }

            if (Parent is AnchoredBlock anchored)
            {
                return anchored.Blocks;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the next sibling block.
    /// </summary>
    public Block? NextBlock
    {
        get
        {
            var siblings = SiblingBlocks;
            if (siblings is null)
            {
                return null;
            }

            var index = siblings.IndexOf(this);
            if (index < 0 || index + 1 >= siblings.Count)
            {
                return null;
            }

            return siblings[index + 1];
        }
    }

    /// <summary>
    /// Gets the previous sibling block.
    /// </summary>
    public Block? PreviousBlock
    {
        get
        {
            var siblings = SiblingBlocks;
            if (siblings is null)
            {
                return null;
            }

            var index = siblings.IndexOf(this);
            if (index <= 0)
            {
                return null;
            }

            return siblings[index - 1];
        }
    }
}
