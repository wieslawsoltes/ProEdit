namespace ProEdit.Controls.Skia.Maui;

/// <summary>
/// .NET MAUI control for editing ProEdit documents.
/// </summary>
public sealed class ProEditDocumentEditor : ProEditDocumentControlBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentEditor"/> class.
    /// </summary>
    public ProEditDocumentEditor()
    {
        IsReadOnly = false;
    }

    /// <summary>
    /// Inserts text at the current selection.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    public void InsertText(string text)
    {
        DocumentHost.InsertText(text);
        Refresh();
    }

    /// <summary>
    /// Deletes the character or selection before the caret.
    /// </summary>
    public void Backspace()
    {
        DocumentHost.Backspace();
        Refresh();
    }

    /// <summary>
    /// Deletes the character or selection after the caret.
    /// </summary>
    public void DeleteForward()
    {
        DocumentHost.DeleteForward();
        Refresh();
    }
}
