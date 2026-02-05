namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents an italic span.
/// </summary>
public sealed class Italic : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Italic"/> class.
    /// </summary>
    public Italic()
    {
        FontStyle = FlowFontStyle.Italic;
    }
}
