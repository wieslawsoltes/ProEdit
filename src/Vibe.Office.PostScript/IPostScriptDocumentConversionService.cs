using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Pdf;

namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Converts PostScript/EPS content to and from the immediate <see cref="Document"/> model.
/// </summary>
public interface IPostScriptDocumentConversionService
{
    /// <summary>
    /// Loads a document from a PostScript/EPS file.
    /// </summary>
    Task<Document> LoadAsync(
        string path,
        PostScriptKind kind,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a document from a PostScript/EPS stream.
    /// </summary>
    Task<Document> LoadAsync(
        Stream sourceStream,
        PostScriptKind kind,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document as PostScript/EPS to a file.
    /// </summary>
    Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        string path,
        PostScriptKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document as PostScript/EPS to a stream.
    /// </summary>
    Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream targetStream,
        PostScriptKind kind,
        CancellationToken cancellationToken = default);
}
