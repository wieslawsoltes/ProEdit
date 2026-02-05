namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents an underlined span.
/// </summary>
public sealed class Underline : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class.
    /// </summary>
    public Underline()
    {
        TextDecorations = FlowTextDecorations.Underline;
    }
}
