namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a bold span.
/// </summary>
public sealed class Bold : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Bold"/> class.
    /// </summary>
    public Bold()
    {
        FontWeight = FlowFontWeight.Bold;
    }
}
