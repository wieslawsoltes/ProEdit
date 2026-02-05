using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a floating block anchored to surrounding text.
/// </summary>
public sealed class Floater : AnchoredBlock
{
    /// <summary>
    /// Identifies the <see cref="Width"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> WidthProperty =
        AvaloniaProperty.Register<Floater, double?>(nameof(Width));

    /// <summary>
    /// Identifies the <see cref="HorizontalAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> HorizontalAlignmentProperty =
        AvaloniaProperty.Register<Floater, string?>(nameof(HorizontalAlignment));

    /// <summary>
    /// Gets or sets floater width.
    /// </summary>
    public double? Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets horizontal alignment metadata.
    /// </summary>
    public string? HorizontalAlignment
    {
        get => GetValue(HorizontalAlignmentProperty);
        set => SetValue(HorizontalAlignmentProperty, value);
    }
}
