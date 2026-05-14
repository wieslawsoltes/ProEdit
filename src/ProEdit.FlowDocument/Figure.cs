using Avalonia;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a figure anchored to surrounding text.
/// </summary>
public sealed class Figure : AnchoredBlock
{
    /// <summary>
    /// Identifies the <see cref="Width"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> WidthProperty =
        AvaloniaProperty.Register<Figure, double?>(nameof(Width));

    /// <summary>
    /// Identifies the <see cref="Height"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> HeightProperty =
        AvaloniaProperty.Register<Figure, double?>(nameof(Height));

    /// <summary>
    /// Identifies the <see cref="HorizontalAnchor"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> HorizontalAnchorProperty =
        AvaloniaProperty.Register<Figure, string?>(nameof(HorizontalAnchor));

    /// <summary>
    /// Identifies the <see cref="VerticalAnchor"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> VerticalAnchorProperty =
        AvaloniaProperty.Register<Figure, string?>(nameof(VerticalAnchor));

    /// <summary>
    /// Identifies the <see cref="HorizontalOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> HorizontalOffsetProperty =
        AvaloniaProperty.Register<Figure, double?>(nameof(HorizontalOffset));

    /// <summary>
    /// Identifies the <see cref="VerticalOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> VerticalOffsetProperty =
        AvaloniaProperty.Register<Figure, double?>(nameof(VerticalOffset));

    /// <summary>
    /// Identifies the <see cref="CanDelayPlacement"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> CanDelayPlacementProperty =
        AvaloniaProperty.Register<Figure, bool?>(nameof(CanDelayPlacement));

    /// <summary>
    /// Identifies the <see cref="WrapDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> WrapDirectionProperty =
        AvaloniaProperty.Register<Figure, string?>(nameof(WrapDirection));

    /// <summary>
    /// Gets or sets horizontal anchor metadata.
    /// </summary>
    public string? HorizontalAnchor
    {
        get => GetValue(HorizontalAnchorProperty);
        set => SetValue(HorizontalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets figure width.
    /// </summary>
    public double? Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets figure height.
    /// </summary>
    public double? Height
    {
        get => GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets vertical anchor metadata.
    /// </summary>
    public string? VerticalAnchor
    {
        get => GetValue(VerticalAnchorProperty);
        set => SetValue(VerticalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets horizontal offset.
    /// </summary>
    public double? HorizontalOffset
    {
        get => GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets vertical offset.
    /// </summary>
    public double? VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets whether figure placement may be delayed.
    /// </summary>
    public bool? CanDelayPlacement
    {
        get => GetValue(CanDelayPlacementProperty);
        set => SetValue(CanDelayPlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets wrap direction metadata.
    /// </summary>
    public string? WrapDirection
    {
        get => GetValue(WrapDirectionProperty);
        set => SetValue(WrapDirectionProperty, value);
    }
}
