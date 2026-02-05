using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a run of text.
/// </summary>
public sealed class Run : Inline
{
    /// <summary>
    /// Identifies the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<Run, string>(nameof(Text), string.Empty);

    /// <summary>
    /// Gets or sets the run text.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class.
    /// </summary>
    public Run()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class with text.
    /// </summary>
    /// <param name="text">The run text.</param>
    public Run(string text)
    {
        SetCurrentValue(TextProperty, text ?? string.Empty);
    }
}
