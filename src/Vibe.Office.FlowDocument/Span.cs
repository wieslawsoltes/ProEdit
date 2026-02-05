using Avalonia.Metadata;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents an inline span that can contain other inlines.
/// </summary>
public class Span : Inline
{
    /// <summary>
    /// Gets the inline collection for the span.
    /// </summary>
    [Content]
    public InlineCollection Inlines { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Span"/> class.
    /// </summary>
    public Span()
    {
        Inlines = new InlineCollection(this);
    }
}
