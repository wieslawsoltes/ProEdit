namespace ProEdit.Controls.Skia.Avalonia;

/// <summary>
/// Read-only Avalonia control for viewing ProEdit documents.
/// </summary>
public sealed class ProEditDocumentViewer : ProEditDocumentControlBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentViewer"/> class.
    /// </summary>
    public ProEditDocumentViewer()
    {
        IsReadOnly = true;
    }
}
