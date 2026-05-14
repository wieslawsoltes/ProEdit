namespace ProEdit.Editing;

/// <summary>
/// Allows batching multiple editor mutations into a single layout refresh.
/// </summary>
public interface IEditorBatchEdit
{
    /// <summary>
    /// Begins a batch edit scope that defers layout refresh until disposed.
    /// </summary>
    IDisposable BeginBatchEdit();
}
