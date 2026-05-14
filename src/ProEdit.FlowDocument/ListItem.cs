using Avalonia;
using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents an item within a list.
/// </summary>
public sealed class ListItem : TextElement
{
    /// <summary>
    /// Identifies the <see cref="Margin"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> MarginProperty =
        AvaloniaProperty.Register<ListItem, FlowThickness>(nameof(Margin));

    /// <summary>
    /// Identifies the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> PaddingProperty =
        AvaloniaProperty.Register<ListItem, FlowThickness>(nameof(Padding));

    /// <summary>
    /// Identifies the <see cref="BorderThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> BorderThicknessProperty =
        AvaloniaProperty.Register<ListItem, FlowThickness>(nameof(BorderThickness));

    /// <summary>
    /// Identifies the <see cref="BorderBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BorderBrushProperty =
        AvaloniaProperty.Register<ListItem, string?>(nameof(BorderBrush));

    /// <summary>
    /// Identifies the <see cref="FlowDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FlowDirectionProperty =
        AvaloniaProperty.Register<ListItem, string?>(nameof(FlowDirection));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<ListItem, double?>(nameof(LineHeight));

    /// <summary>
    /// Identifies the <see cref="LineStackingStrategy"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LineStackingStrategyProperty =
        AvaloniaProperty.Register<ListItem, string?>(nameof(LineStackingStrategy));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<ListItem, FlowTextAlignment?>(nameof(TextAlignment));

    /// <summary>
    /// Gets the block collection for the list item.
    /// </summary>
    [Content]
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets the margin.
    /// </summary>
    public FlowThickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding.
    /// </summary>
    public FlowThickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public FlowThickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    public string? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets flow direction metadata.
    /// </summary>
    public string? FlowDirection
    {
        get => GetValue(FlowDirectionProperty);
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets line height metadata.
    /// </summary>
    public double? LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
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
    /// Gets or sets text alignment metadata.
    /// </summary>
    public FlowTextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets parent list if available.
    /// </summary>
    public List? List => Parent as List;

    /// <summary>
    /// Gets sibling list items.
    /// </summary>
    public ListItemCollection? SiblingListItems => List?.ListItems;

    /// <summary>
    /// Gets next sibling list item.
    /// </summary>
    public ListItem? NextListItem
    {
        get
        {
            var siblings = SiblingListItems;
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
    /// Gets previous sibling list item.
    /// </summary>
    public ListItem? PreviousListItem
    {
        get
        {
            var siblings = SiblingListItems;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ListItem"/> class.
    /// </summary>
    public ListItem()
    {
        Blocks = new BlockCollection(this);
    }
}
