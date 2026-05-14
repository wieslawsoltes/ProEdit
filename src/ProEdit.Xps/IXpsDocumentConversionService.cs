using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Pdf;

namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Converts XPS/OXPS content to and from the immediate <see cref="Document"/> model.
/// </summary>
public interface IXpsDocumentConversionService
{
    /// <summary>
    /// Loads a document from an XPS/OXPS file.
    /// </summary>
    Task<Document> LoadAsync(
        string path,
        XpsFlavor flavor,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a document from an XPS/OXPS stream.
    /// </summary>
    Task<Document> LoadAsync(
        Stream sourceStream,
        XpsFlavor flavor,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document as XPS/OXPS to a file.
    /// </summary>
    Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        string path,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document as XPS/OXPS to a stream.
    /// </summary>
    Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream targetStream,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default);
}
