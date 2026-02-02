namespace Vibe.Office.Editing;

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
