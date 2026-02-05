using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a hyperlink span.
/// </summary>
public sealed class Hyperlink : Span
{
    /// <summary>
    /// Identifies the <see cref="NavigateUri"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> NavigateUriProperty =
        AvaloniaProperty.Register<Hyperlink, string?>(nameof(NavigateUri));

    /// <summary>
    /// Identifies the <see cref="TargetName"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TargetNameProperty =
        AvaloniaProperty.Register<Hyperlink, string?>(nameof(TargetName));

    /// <summary>
    /// Identifies the <see cref="ToolTip"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ToolTipProperty =
        AvaloniaProperty.Register<Hyperlink, string?>(nameof(ToolTip));

    /// <summary>
    /// Identifies the <see cref="Command"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> CommandProperty =
        AvaloniaProperty.Register<Hyperlink, object?>(nameof(Command));

    /// <summary>
    /// Identifies the <see cref="CommandParameter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<Hyperlink, object?>(nameof(CommandParameter));

    /// <summary>
    /// Identifies the <see cref="CommandTarget"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> CommandTargetProperty =
        AvaloniaProperty.Register<Hyperlink, object?>(nameof(CommandTarget));

    /// <summary>
    /// Gets or sets the navigation URI for the hyperlink.
    /// </summary>
    public string? NavigateUri
    {
        get => GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    /// <summary>
    /// Gets or sets the target name for the hyperlink.
    /// </summary>
    public string? TargetName
    {
        get => GetValue(TargetNameProperty);
        set => SetValue(TargetNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip for the hyperlink.
    /// </summary>
    internal string? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    /// <summary>
    /// Gets or sets command metadata.
    /// </summary>
    public object? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets command parameter metadata.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets command target metadata.
    /// </summary>
    public object? CommandTarget
    {
        get => GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }
}
