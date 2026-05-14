using ProEdit.Documents;

namespace ProEdit.Editing;

public interface IEditorHistorySnapshotService
{
    EditorSessionSnapshot CaptureSnapshot();
    void RestoreSnapshot(EditorSessionSnapshot snapshot);
    void RecordSnapshot(EditorSessionSnapshot before);
}

public sealed class EditorHistorySnapshotService : IEditorHistorySnapshotService
{
    private readonly IEditorMutableSession _session;
    private readonly EditorCommandHistory _history;

    public EditorHistorySnapshotService(IEditorMutableSession session, EditorCommandHistory history)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public EditorSessionSnapshot CaptureSnapshot()
    {
        var document = DocumentClone.Clone(_session.Document);
        return new EditorSessionSnapshot(document, _session.Selection, _session.Caret);
    }

    public void RestoreSnapshot(EditorSessionSnapshot snapshot)
    {
        DocumentClone.Copy(snapshot.Document, _session.Document);
        _session.RefreshLayout();
        _session.SetSelection(snapshot.Selection);
    }

    public void RecordSnapshot(EditorSessionSnapshot before)
    {
        var after = CaptureSnapshot();
        _history.RecordSnapshot(before, after);
    }
}
