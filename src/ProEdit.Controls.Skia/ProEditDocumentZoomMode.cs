namespace ProEdit.Controls.Skia;

/// <summary>
/// Defines the zoom behavior used by a ProEdit document host.
/// </summary>
public enum ProEditDocumentZoomMode
{
    /// <summary>
    /// Uses an explicit zoom value.
    /// </summary>
    Custom,

    /// <summary>
    /// Fits the widest page to the viewport width.
    /// </summary>
    PageWidth,

    /// <summary>
    /// Fits the first page inside the viewport.
    /// </summary>
    WholePage,

    /// <summary>
    /// Fits multiple pages per row inside the viewport width.
    /// </summary>
    MultiplePages
}
