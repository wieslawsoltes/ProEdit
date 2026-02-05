namespace Vibe.Office.FlowDocument;

/// <summary>
/// Defines text decoration flags for FlowDocument text.
/// </summary>
[Flags]
public enum FlowTextDecorations
{
    /// <summary>
    /// No decorations.
    /// </summary>
    None = 0,

    /// <summary>
    /// Underline decoration.
    /// </summary>
    Underline = 1,

    /// <summary>
    /// Strikethrough decoration.
    /// </summary>
    Strikethrough = 2
}
