using System;
using System.Collections.Generic;
using System.Linq;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorTableStyleServiceAdapter : ITableStyleService
{
    private readonly IEditorMutableSession _session;

    public EditorTableStyleServiceAdapter(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public IReadOnlyList<EditorTableStyleInfo> GetTableStyles()
    {
        var styles = _session.Document.Styles.TableStyles.Values;
        var items = new List<EditorTableStyleInfo>(styles.Count);
        foreach (var style in styles.OrderBy(style => style.Name ?? style.Id, StringComparer.OrdinalIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(style.Name) ? style.Id : style.Name!;
            var isDefault = string.Equals(style.Id, _session.Document.Styles.DefaultTableStyleId, StringComparison.OrdinalIgnoreCase);
            items.Add(new EditorTableStyleInfo(style.Id, name, isDefault));
        }

        return items;
    }

    public string? GetCurrentTableStyleId()
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return _session.Document.Styles.DefaultTableStyleId;
        }

        var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var location = _session.Document.GetParagraphLocation(paragraphIndex);
        if (!location.IsInTable || location.Table is null)
        {
            return _session.Document.Styles.DefaultTableStyleId;
        }

        return location.Table.StyleId ?? _session.Document.Styles.DefaultTableStyleId;
    }

    public string? GetDefaultTableStyleId()
    {
        return _session.Document.Styles.DefaultTableStyleId;
    }
}
