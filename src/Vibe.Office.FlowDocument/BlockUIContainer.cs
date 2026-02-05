using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a block that hosts a UI element.
/// </summary>
public sealed class BlockUIContainer : Block
{
    /// <summary>
    /// Identifies the <see cref="Child"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ChildProperty =
        AvaloniaProperty.Register<BlockUIContainer, object?>(nameof(Child));

    /// <summary>
    /// Gets or sets the hosted UI element.
    /// </summary>
    public object? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }
}
