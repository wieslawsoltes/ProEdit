using Avalonia;
using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a table cell.
/// </summary>
public sealed class TableCell : TextElement
{
    /// <summary>
    /// Identifies the <see cref="ColumnSpan"/> property.
    /// </summary>
    public static readonly StyledProperty<int> ColumnSpanProperty =
        AvaloniaProperty.Register<TableCell, int>(nameof(ColumnSpan), 1);

    /// <summary>
    /// Identifies the <see cref="RowSpan"/> property.
    /// </summary>
    public static readonly StyledProperty<int> RowSpanProperty =
        AvaloniaProperty.Register<TableCell, int>(nameof(RowSpan), 1);

    /// <summary>
    /// Identifies the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> PaddingProperty =
        AvaloniaProperty.Register<TableCell, FlowThickness>(nameof(Padding));

    /// <summary>
    /// Identifies the <see cref="BorderThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> BorderThicknessProperty =
        AvaloniaProperty.Register<TableCell, FlowThickness>(nameof(BorderThickness));

    /// <summary>
    /// Identifies the <see cref="BorderBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BorderBrushProperty =
        AvaloniaProperty.Register<TableCell, string?>(nameof(BorderBrush));

    /// <summary>
    /// Identifies the <see cref="FlowDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FlowDirectionProperty =
        AvaloniaProperty.Register<TableCell, string?>(nameof(FlowDirection));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<TableCell, double?>(nameof(LineHeight));

    /// <summary>
    /// Identifies the <see cref="LineStackingStrategy"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LineStackingStrategyProperty =
        AvaloniaProperty.Register<TableCell, string?>(nameof(LineStackingStrategy));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<TableCell, FlowTextAlignment?>(nameof(TextAlignment));

    /// <summary>
    /// Gets the block collection for the cell.
    /// </summary>
    [Content]
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets the column span.
    /// </summary>
    public int ColumnSpan
    {
        get => Math.Max(1, GetValue(ColumnSpanProperty));
        set => SetValue(ColumnSpanProperty, Math.Max(1, value));
    }

    /// <summary>
    /// Gets or sets the row span.
    /// </summary>
    public int RowSpan
    {
        get => Math.Max(1, GetValue(RowSpanProperty));
        set => SetValue(RowSpanProperty, Math.Max(1, value));
    }

    /// <summary>
    /// Gets or sets cell padding.
    /// </summary>
    public FlowThickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets cell border thickness.
    /// </summary>
    public FlowThickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets cell border brush.
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
    /// Initializes a new instance of the <see cref="TableCell"/> class.
    /// </summary>
    public TableCell()
    {
        Blocks = new BlockCollection(this);
    }
}
