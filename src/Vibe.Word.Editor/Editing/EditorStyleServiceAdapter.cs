using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

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
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return EditorValue<string>.Missing();
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var styleIds = new OptionalEditorValueAccumulator<string>();
        var defaultId = _session.Document.Styles.DefaultParagraphStyleId;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
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

    public TextStyle? GetParagraphStylePreview(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        if (!_session.Document.Styles.ParagraphStyles.ContainsKey(styleId))
        {
            return null;
        }

        var resolver = new DocumentStyleResolver(_session.Document);
        var paragraph = new ParagraphBlock { StyleId = styleId };
        var resolved = resolver.ResolveParagraphTextStyle(paragraph, _session.Document.DefaultTextStyle);
        return resolved;
    }

    public IReadOnlyCollection<string> GetParagraphStylesInUse()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultId = _session.Document.Styles.DefaultParagraphStyleId;
        if (!string.IsNullOrWhiteSpace(defaultId))
        {
            used.Add(defaultId);
        }

        var count = _session.GetParagraphCountFast();
        for (var i = 0; i < count; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var styleId = paragraph.StyleId ?? defaultId;
            if (!string.IsNullOrWhiteSpace(styleId))
            {
                used.Add(styleId);
            }
        }

        return used;
    }

    public EditorDirectFormattingInfo GetDirectFormattingInfo()
    {
        var selection = _session.Selection.Normalize();
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return new EditorDirectFormattingInfo(false, false);
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var hasParagraphFormatting = false;
        var hasCharacterFormatting = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            if (HasParagraphFormatting(paragraph.Properties))
            {
                hasParagraphFormatting = true;
            }

            if (!hasCharacterFormatting && paragraph.Inlines.Count > 0)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is RunInline run && run.Style?.HasValues == true)
                    {
                        hasCharacterFormatting = true;
                        break;
                    }
                }
            }

            if (hasParagraphFormatting && hasCharacterFormatting)
            {
                break;
            }
        }

        return new EditorDirectFormattingInfo(hasParagraphFormatting, hasCharacterFormatting);
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
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var updated = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
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

    public bool RenameParagraphStyle(string styleId, string name)
    {
        if (string.IsNullOrWhiteSpace(styleId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!_session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        var trimmed = name.Trim();
        if (string.Equals(style.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        style.Name = trimmed;
        _session.RefreshLayout();
        return true;
    }

    public bool SetParagraphStyleBasedOn(string styleId, string? basedOnId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        if (string.Equals(styleId, basedOnId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resolved = ResolveStyleIdOrNull(basedOnId);
        if (string.Equals(style.BasedOnId, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        style.BasedOnId = resolved;
        _session.RefreshLayout();
        return true;
    }

    public bool SetParagraphStyleNext(string styleId, string? nextStyleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        if (string.Equals(styleId, nextStyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resolved = ResolveStyleIdOrNull(nextStyleId);
        if (string.Equals(style.NextStyleId, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        style.NextStyleId = resolved;
        _session.RefreshLayout();
        return true;
    }

    public bool SetDefaultParagraphStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.ParagraphStyles.ContainsKey(styleId))
        {
            return false;
        }

        if (string.Equals(_session.Document.Styles.DefaultParagraphStyleId, styleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _session.Document.Styles.DefaultParagraphStyleId = styleId;
        _session.RefreshLayout();
        return true;
    }

    private string? ResolveStyleIdOrNull(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        return _session.Document.Styles.ParagraphStyles.ContainsKey(styleId) ? styleId : null;
    }

    private static bool HasParagraphFormatting(ParagraphProperties properties)
    {
        return properties.Alignment.HasValue
               || properties.SpacingBefore.HasValue
               || properties.SpacingAfter.HasValue
               || properties.SpacingBeforeLines.HasValue
               || properties.SpacingAfterLines.HasValue
               || properties.AutoSpacingBefore.HasValue
               || properties.AutoSpacingAfter.HasValue
               || properties.LineSpacing.HasValue
               || properties.LineSpacingRule.HasValue
               || properties.IndentLeft.HasValue
               || properties.IndentRight.HasValue
               || properties.FirstLineIndent.HasValue
               || properties.TabStops.Count > 0
               || properties.KeepWithNext.HasValue
               || properties.KeepLinesTogether.HasValue
               || properties.WidowControl.HasValue
               || properties.PageBreakBefore.HasValue
               || properties.ContextualSpacing.HasValue
               || properties.Bidi.HasValue
               || properties.TextDirection.HasValue
               || (properties.EastAsianLayout?.HasValues ?? false)
               || properties.ShadingColor.HasValue
               || properties.SuppressLineNumbers.HasValue
               || (properties.DropCap?.HasValues ?? false)
               || (properties.Frame?.HasValues ?? false)
               || properties.Borders.HasAny;
    }
}
