using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a table column.
/// </summary>
public sealed class TableColumn : FlowElement
{
    /// <summary>
    /// Identifies the <see cref="Width"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> WidthProperty =
        AvaloniaProperty.Register<TableColumn, double?>(nameof(Width));

    /// <summary>
    /// Identifies the <see cref="Background"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BackgroundProperty =
        AvaloniaProperty.Register<TableColumn, string?>(nameof(Background));

    /// <summary>
    /// Gets or sets the column width in points.
    /// </summary>
    public double? Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets column background metadata.
    /// </summary>
    public string? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }
}
