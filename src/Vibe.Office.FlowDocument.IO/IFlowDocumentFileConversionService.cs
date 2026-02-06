using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Converts FlowDocument instances to and from file formats.
/// </summary>
public interface IFlowDocumentFileConversionService
{
    /// <summary>
    /// Gets supported load extensions.
    /// </summary>
    IReadOnlyList<string> SupportedLoadExtensions { get; }

    /// <summary>
    /// Gets supported save extensions.
    /// </summary>
    IReadOnlyList<string> SupportedSaveExtensions { get; }

    /// <summary>
    /// Determines whether the specified path or extension can be loaded.
    /// </summary>
    /// <param name="pathOrExtension">Path or extension to evaluate.</param>
    /// <returns><see langword="true"/> when supported for load; otherwise <see langword="false"/>.</returns>
    bool CanLoad(string pathOrExtension);

    /// <summary>
    /// Determines whether the specified path or extension can be saved.
    /// </summary>
    /// <param name="pathOrExtension">Path or extension to evaluate.</param>
    /// <returns><see langword="true"/> when supported for save; otherwise <see langword="false"/>.</returns>
    bool CanSave(string pathOrExtension);

    /// <summary>
    /// Loads a FlowDocument from the specified file path.
    /// </summary>
    /// <param name="path">Input file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Loaded flow document.</returns>
    Task<FlowDocumentModel> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a FlowDocument to the specified file path.
    /// </summary>
    /// <param name="flowDocument">FlowDocument to save.</param>
    /// <param name="path">Output file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(FlowDocumentModel flowDocument, string path, CancellationToken cancellationToken = default);
}
