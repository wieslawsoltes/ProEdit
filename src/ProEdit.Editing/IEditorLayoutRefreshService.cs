namespace ProEdit.Editing;

public interface IEditorLayoutRefreshService
{
    void RefreshLayout(int? dirtyParagraphIndex);
}
