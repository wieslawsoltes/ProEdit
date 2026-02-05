using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents an inline element that hosts a UI element.
/// </summary>
public sealed class InlineUIContainer : Inline
{
    /// <summary>
    /// Identifies the <see cref="Child"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ChildProperty =
        AvaloniaProperty.Register<InlineUIContainer, object?>(nameof(Child));

    /// <summary>
    /// Gets or sets the hosted UI element.
    /// </summary>
    public object? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }
}
