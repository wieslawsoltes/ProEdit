namespace ProEdit.Controls.Skia.Maui;

/// <summary>
/// Read-only .NET MAUI control for viewing ProEdit documents.
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
