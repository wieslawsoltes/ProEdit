using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorStyleServiceAdapter : IStyleService
{
    private readonly IEditorMutableSession _session;

    public EditorStyleServiceAdapter(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public IReadOnlyList<EditorParagraphStyleInfo> GetParagraphStyles()
    {
        var styles = _session.Document.Styles.ParagraphStyles;
        if (styles.Count == 0)
        {
            return Array.Empty<EditorParagraphStyleInfo>();
        }

        var defaultId = _session.Document.Styles.DefaultParagraphStyleId;
        var list = new List<EditorParagraphStyleInfo>(styles.Count);
        foreach (var entry in styles)
        {
            var name = string.IsNullOrWhiteSpace(entry.Value.Name) ? entry.Key : entry.Value.Name!;
            list.Add(new EditorParagraphStyleInfo(entry.Key, name, string.Equals(entry.Key, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public EditorValue<string> GetCurrentParagraphStyleId()
    {
        var selection = _session.Selection.Normalize();
        if (_session.Document.ParagraphCount == 0)
        {
            return EditorValue<string>.Missing();
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var styleIds = new OptionalEditorValueAccumulator<string>();
        var defaultId = _session.Document.Styles.DefaultParagraphStyleId;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            var styleId = paragraph.StyleId ?? defaultId;
            styleIds.Add(styleId);
        }

        return styleIds.Build();
    }

    public ParagraphStyleDefinition? GetParagraphStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        return _session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out var style) ? style : null;
    }

    public bool ApplyParagraphStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.ParagraphStyles.ContainsKey(styleId))
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var updated = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            if (string.Equals(paragraph.StyleId, styleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paragraph.StyleId = styleId;
            updated = true;
        }

        if (updated)
        {
            _session.RefreshLayout();
        }

        return updated;
    }
}
