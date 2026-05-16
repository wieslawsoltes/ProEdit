namespace ProEdit.Controls.Skia;

/// <summary>
/// Describes the document extent in document-space units.
/// </summary>
/// <param name="Width">The extent width.</param>
/// <param name="Height">The extent height.</param>
public readonly record struct ProEditDocumentExtent(float Width, float Height);
