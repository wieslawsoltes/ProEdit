using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorFindReplaceServiceAdapter : IFindReplaceService
{
    private readonly Func<EditorFindQuery, EditorFindResult>? _findNext;
    private readonly Func<EditorReplaceQuery, EditorFindResult>? _replaceNext;
    private readonly Func<EditorReplaceQuery, int>? _replaceAll;

    public EditorFindReplaceServiceAdapter(
        Func<EditorFindQuery, EditorFindResult>? findNext = null,
        Func<EditorReplaceQuery, EditorFindResult>? replaceNext = null,
        Func<EditorReplaceQuery, int>? replaceAll = null)
    {
        _findNext = findNext;
        _replaceNext = replaceNext;
        _replaceAll = replaceAll;
    }

    public bool IsAvailable => _findNext is not null;

    public bool TryFindNext(EditorFindQuery query, out EditorFindResult result)
    {
        if (_findNext is null)
        {
            result = default;
            return false;
        }

        result = _findNext(query);
        return result.Found;
    }

    public bool TryReplaceNext(EditorReplaceQuery query, out EditorFindResult result)
    {
        if (_replaceNext is null)
        {
            result = default;
            return false;
        }

        result = _replaceNext(query);
        return result.Found;
    }

    public int ReplaceAll(EditorReplaceQuery query)
    {
        return _replaceAll?.Invoke(query) ?? 0;
    }
}
