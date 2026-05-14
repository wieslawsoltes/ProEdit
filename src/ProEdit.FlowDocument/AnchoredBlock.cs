using Avalonia;
using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Base type for anchored blocks such as figures and floaters.
/// </summary>
public abstract class AnchoredBlock : Inline
{
    /// <summary>
    /// Identifies the <see cref="Margin"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> MarginProperty =
        AvaloniaProperty.Register<AnchoredBlock, FlowThickness>(nameof(Margin));

    /// <summary>
    /// Identifies the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> PaddingProperty =
        AvaloniaProperty.Register<AnchoredBlock, FlowThickness>(nameof(Padding));

    /// <summary>
    /// Identifies the <see cref="BorderThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> BorderThicknessProperty =
        AvaloniaProperty.Register<AnchoredBlock, FlowThickness>(nameof(BorderThickness));

    /// <summary>
    /// Identifies the <see cref="BorderBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BorderBrushProperty =
        AvaloniaProperty.Register<AnchoredBlock, string?>(nameof(BorderBrush));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<AnchoredBlock, FlowTextAlignment?>(nameof(TextAlignment));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<AnchoredBlock, double?>(nameof(LineHeight));

    /// <summary>
    /// Identifies the <see cref="LineStackingStrategy"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LineStackingStrategyProperty =
        AvaloniaProperty.Register<AnchoredBlock, string?>(nameof(LineStackingStrategy));

    /// <summary>
    /// Gets the block collection contained within the anchored block.
    /// </summary>
    [Content]
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets the anchored block margin.
    /// </summary>
    public FlowThickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets anchored block padding.
    /// </summary>
    public FlowThickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets anchored block border thickness.
    /// </summary>
    public FlowThickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets anchored block border brush.
    /// </summary>
    public string? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
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
    /// Initializes a new instance of the <see cref="AnchoredBlock"/> class.
    /// </summary>
    protected AnchoredBlock()
    {
        Blocks = new BlockCollection(this);
    }
}
