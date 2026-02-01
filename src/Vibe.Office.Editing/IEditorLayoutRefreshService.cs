namespace Vibe.Office.Editing;

public interface IEditorLayoutRefreshService
{
    void RefreshLayout(int? dirtyParagraphIndex);
}
