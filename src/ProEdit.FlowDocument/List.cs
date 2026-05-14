using Avalonia;
using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a list block.
/// </summary>
public sealed class List : Block
{
    /// <summary>
    /// Identifies the <see cref="MarkerStyle"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowListMarkerStyle> MarkerStyleProperty =
        AvaloniaProperty.Register<List, FlowListMarkerStyle>(nameof(MarkerStyle), FlowListMarkerStyle.Disc);

    /// <summary>
    /// Identifies the <see cref="StartIndex"/> property.
    /// </summary>
    public static readonly StyledProperty<int?> StartIndexProperty =
        AvaloniaProperty.Register<List, int?>(nameof(StartIndex));

    /// <summary>
    /// Identifies the <see cref="MarkerOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> MarkerOffsetProperty =
        AvaloniaProperty.Register<List, double?>(nameof(MarkerOffset));

    /// <summary>
    /// Gets the list items.
    /// </summary>
    [Content]
    public ListItemCollection ListItems { get; }

    /// <summary>
    /// Gets or sets the list marker style.
    /// </summary>
    public FlowListMarkerStyle MarkerStyle
    {
        get => GetValue(MarkerStyleProperty);
        set => SetValue(MarkerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the starting index for numbered lists.
    /// </summary>
    public int? StartIndex
    {
        get => GetValue(StartIndexProperty);
        set => SetValue(StartIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets the marker offset.
    /// </summary>
    public double? MarkerOffset
    {
        get => GetValue(MarkerOffsetProperty);
        set => SetValue(MarkerOffsetProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="List"/> class.
    /// </summary>
    public List()
    {
        ListItems = new ListItemCollection(this);
    }
}
