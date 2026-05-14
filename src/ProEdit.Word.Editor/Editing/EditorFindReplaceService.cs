using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorFindReplaceService : IFindReplaceService
{
    private readonly IEditorMutableSession _session;
    private EditorFindQuery? _lastFindQuery;
    private EditorReplaceQuery? _lastReplaceQuery;

    public EditorFindReplaceService(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool IsAvailable => _session.Document.ParagraphCount > 0;

    public bool TryFindNext(EditorFindQuery query, out EditorFindResult result)
    {
        if (!TryResolveFindQuery(query, out var resolved))
        {
            result = default;
            return false;
        }

        _lastFindQuery = resolved;
        if (!TryFindRange(resolved, out var range))
        {
            result = default;
            return false;
        }

        _session.SetSelection(range);
        result = new EditorFindResult(true, range);
        return true;
    }

    public bool TryReplaceNext(EditorReplaceQuery query, out EditorFindResult result)
    {
        if (!TryResolveReplaceQuery(query, out var resolved))
        {
            result = default;
            return false;
        }

        _lastReplaceQuery = resolved;
        var findQuery = new EditorFindQuery(resolved.Text, resolved.MatchCase, resolved.WholeWord, resolved.Wrap);
        if (!TryFindRange(findQuery, out var range))
        {
            result = default;
            return false;
        }

        _session.SetSelection(range);
        _session.InsertText(resolved.Replacement);
        result = new EditorFindResult(true, range);
        return true;
    }

    public int ReplaceAll(EditorReplaceQuery query)
    {
        if (!TryResolveReplaceQuery(query, out var resolved))
        {
            return 0;
        }

        _lastReplaceQuery = resolved;
        var findQuery = new EditorFindQuery(resolved.Text, resolved.MatchCase, resolved.WholeWord, false);
        var replaced = 0;
        var start = new TextPosition(0, 0);
        while (TryFindForward(findQuery, start, null, out var range))
        {
            _session.SetSelection(range);
            _session.InsertText(resolved.Replacement);
            replaced++;
            start = new TextPosition(range.Start.ParagraphIndex, range.Start.Offset + resolved.Replacement.Length);
        }

        return replaced;
    }

    private bool TryResolveFindQuery(EditorFindQuery query, out EditorFindQuery resolved)
    {
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            resolved = query;
            return true;
        }

        if (TryGetSelectionText(out var selectionText))
        {
            resolved = new EditorFindQuery(selectionText, query.MatchCase, query.WholeWord, query.Wrap);
            return true;
        }

        if (_lastFindQuery.HasValue && !string.IsNullOrWhiteSpace(_lastFindQuery.Value.Text))
        {
            resolved = _lastFindQuery.Value;
            return true;
        }

        resolved = default;
        return false;
    }

    private bool TryResolveReplaceQuery(EditorReplaceQuery query, out EditorReplaceQuery resolved)
    {
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            resolved = query;
            return true;
        }

        if (_lastReplaceQuery.HasValue && !string.IsNullOrWhiteSpace(_lastReplaceQuery.Value.Text))
        {
            resolved = _lastReplaceQuery.Value;
            return true;
        }

        resolved = default;
        return false;
    }

    private bool TryGetSelectionText(out string text)
    {
        var selection = _session.Selection.Normalize();
        if (selection.IsEmpty)
        {
            text = string.Empty;
            return false;
        }

        if (selection.Start.ParagraphIndex != selection.End.ParagraphIndex)
        {
            text = string.Empty;
            return false;
        }

        var paragraph = _session.Document.GetParagraph(selection.Start.ParagraphIndex);
        var paragraphText = DocumentEditHelpers.GetParagraphText(paragraph);
        var start = Math.Clamp(selection.Start.Offset, 0, paragraphText.Length);
        var end = Math.Clamp(selection.End.Offset, 0, paragraphText.Length);
        if (end <= start)
        {
            text = string.Empty;
            return false;
        }

        text = paragraphText.Substring(start, end - start);
        return !string.IsNullOrWhiteSpace(text);
    }

    private bool TryFindRange(EditorFindQuery query, out TextRange range)
    {
        range = default;
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var start = selection.IsEmpty ? selection.Start : selection.End;
        if (TryFindForward(query, start, null, out range))
        {
            return true;
        }

        if (!query.Wrap)
        {
            return false;
        }

        var wrapStop = start;
        if (wrapStop.ParagraphIndex <= 0 && wrapStop.Offset <= 0)
        {
            return false;
        }

        return TryFindForward(query, new TextPosition(0, 0), wrapStop, out range);
    }

    private bool TryFindForward(EditorFindQuery query, TextPosition start, TextPosition? stop, out TextRange range)
    {
        range = default;
        if (string.IsNullOrEmpty(query.Text))
        {
            return false;
        }

        var paragraphCount = _session.Document.ParagraphCount;
        if (paragraphCount == 0)
        {
            return false;
        }

        var startIndex = Math.Clamp(start.ParagraphIndex, 0, paragraphCount - 1);
        var stopIndex = stop.HasValue ? Math.Clamp(stop.Value.ParagraphIndex, 0, paragraphCount - 1) : paragraphCount - 1;
        if (startIndex > stopIndex)
        {
            return false;
        }

        var comparison = query.MatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        for (var i = startIndex; i <= stopIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            var text = DocumentEditHelpers.GetParagraphText(paragraph);
            if (text.Length == 0)
            {
                continue;
            }

            var startOffset = i == startIndex ? Math.Clamp(start.Offset, 0, text.Length) : 0;
            var endOffset = text.Length;
            if (stop.HasValue && i == stopIndex)
            {
                endOffset = Math.Clamp(stop.Value.Offset, 0, text.Length);
            }

            if (!TryFindInParagraph(text, startOffset, endOffset, query.Text, comparison, query.WholeWord, out var index))
            {
                continue;
            }

            range = new TextRange(new TextPosition(i, index), new TextPosition(i, index + query.Text.Length));
            return true;
        }

        return false;
    }

    private static bool TryFindInParagraph(
        string text,
        int startOffset,
        int endOffset,
        string needle,
        StringComparison comparison,
        bool wholeWord,
        out int index)
    {
        index = -1;
        if (startOffset >= endOffset || string.IsNullOrEmpty(needle))
        {
            return false;
        }

        var maxStart = endOffset - needle.Length;
        if (maxStart < startOffset)
        {
            return false;
        }

        var searchIndex = startOffset;
        while (searchIndex <= maxStart)
        {
            var found = text.IndexOf(needle, searchIndex, endOffset - searchIndex, comparison);
            if (found < 0)
            {
                return false;
            }

            if (!wholeWord || IsWholeWord(text.AsSpan(), found, needle.Length))
            {
                index = found;
                return true;
            }

            searchIndex = found + 1;
        }

        return false;
    }

    private static bool IsWholeWord(ReadOnlySpan<char> text, int index, int length)
    {
        if (index < 0 || index + length > text.Length)
        {
            return false;
        }

        if (index > 0 && IsWordChar(text[index - 1]))
        {
            return false;
        }

        var endIndex = index + length;
        if (endIndex < text.Length && IsWordChar(text[endIndex]))
        {
            return false;
        }

        return true;
    }

    private static bool IsWordChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '\'';
    }
}
