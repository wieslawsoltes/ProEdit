namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Represents format-specific validation errors for FlowDocument file conversions.
/// </summary>
public sealed class FlowDocumentFileFormatException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileFormatException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    public FlowDocumentFileFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileFormatException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public FlowDocumentFileFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
