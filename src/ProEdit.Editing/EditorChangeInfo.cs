namespace ProEdit.Editing;

public enum EditorChangeKind
{
    Content,
    Selection
}

public interface IEditorChangeInfo
{
    EditorChangeKind LastChangeKind { get; }
    int? LastDirtyParagraphIndex { get; }
}
