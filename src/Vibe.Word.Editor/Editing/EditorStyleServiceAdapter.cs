using System;
using System.Collections.Generic;
using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorStyleServiceAdapter : IStyleManagerService
{
    private readonly IEditorMutableSession _session;

    public event EventHandler? StylesChanged;

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
        return resolver.ResolveParagraphTextStyle(paragraph, _session.Document.DefaultTextStyle);
    }

    public IReadOnlyCollection<string> GetParagraphStylesInUse()
    {
        return BuildParagraphStylesInUse();
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

        if (!_session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out var definition))
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

        if (TryResolveLinkedCharacterStyle(definition, out var linkedStyleId)
            && TryApplyLinkedCharacterStyle(selection, paragraphCount, linkedStyleId))
        {
            RefreshLayout(startIndex);
            return true;
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
            RefreshLayout(startIndex);
        }

        return updated;
    }

    private void RefreshLayout(int dirtyParagraphIndex)
    {
        if (_session is IEditorLayoutRefreshService refresh)
        {
            refresh.RefreshLayout(dirtyParagraphIndex);
            return;
        }

        _session.RefreshLayout();
    }

    private bool TryResolveLinkedCharacterStyle(ParagraphStyleDefinition definition, out string linkedStyleId)
    {
        linkedStyleId = string.Empty;
        if (string.IsNullOrWhiteSpace(definition.LinkedStyleId))
        {
            return false;
        }

        var candidate = definition.LinkedStyleId.Trim();
        if (_session.Document.Styles.CharacterStyles.ContainsKey(candidate))
        {
            linkedStyleId = candidate;
            return true;
        }

        return false;
    }

    private bool TryApplyLinkedCharacterStyle(TextRange selection, int paragraphCount, string linkedStyleId)
    {
        if (selection.IsEmpty)
        {
            return false;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex != endIndex)
        {
            return false;
        }

        var paragraph = _session.GetParagraphFast(startIndex);
        var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
        var startOffset = Math.Clamp(selection.Start.Offset, 0, paragraphLength);
        var endOffset = Math.Clamp(selection.End.Offset, 0, paragraphLength);

        if (startOffset == 0 && endOffset >= paragraphLength)
        {
            return false;
        }

        if (startOffset == endOffset)
        {
            return false;
        }

        var range = new TextRange(
            new TextPosition(startIndex, startOffset),
            new TextPosition(endIndex, endOffset));
        return ApplyCharacterStyleToRange(range, linkedStyleId);
    }

    public bool RenameParagraphStyle(string styleId, string name)
    {
        return RenameStyle(EditorStyleType.Paragraph, styleId, name);
    }

    public bool SetParagraphStyleBasedOn(string styleId, string? basedOnId)
    {
        return SetStyleBasedOn(EditorStyleType.Paragraph, styleId, basedOnId);
    }

    public bool SetParagraphStyleNext(string styleId, string? nextStyleId)
    {
        return SetStyleNext(EditorStyleType.Paragraph, styleId, nextStyleId);
    }

    public bool SetDefaultParagraphStyle(string styleId)
    {
        return SetDefaultStyle(EditorStyleType.Paragraph, styleId);
    }

    public IReadOnlyList<EditorStyleInfo> GetStyles(EditorStyleType? type = null)
    {
        var styles = _session.Document.Styles;
        var list = new List<EditorStyleInfo>();

        HashSet<string>? paragraphInUse = null;
        HashSet<string>? characterInUse = null;
        HashSet<string>? tableInUse = null;

        if (type is null || type == EditorStyleType.Paragraph)
        {
            paragraphInUse = BuildParagraphStylesInUse();
            foreach (var entry in styles.ParagraphStyles)
            {
                var definition = entry.Value;
                var name = string.IsNullOrWhiteSpace(definition.Name) ? entry.Key : definition.Name!;
                list.Add(new EditorStyleInfo(
                    entry.Key,
                    name,
                    EditorStyleType.Paragraph,
                    string.Equals(entry.Key, styles.DefaultParagraphStyleId, StringComparison.OrdinalIgnoreCase),
                    paragraphInUse.Contains(entry.Key),
                    definition.QuickStyle == true,
                    definition.Hidden == true,
                    definition.SemiHidden == true,
                    definition.UnhideWhenUsed == true,
                    definition.Locked == true,
                    definition.CustomStyle == true,
                    definition.UiPriority));
            }
        }

        if (type is null || type == EditorStyleType.Character)
        {
            characterInUse = BuildCharacterStylesInUse();
            foreach (var entry in styles.CharacterStyles)
            {
                var definition = entry.Value;
                var name = string.IsNullOrWhiteSpace(definition.Name) ? entry.Key : definition.Name!;
                list.Add(new EditorStyleInfo(
                    entry.Key,
                    name,
                    EditorStyleType.Character,
                    string.Equals(entry.Key, styles.DefaultCharacterStyleId, StringComparison.OrdinalIgnoreCase),
                    characterInUse.Contains(entry.Key),
                    definition.QuickStyle == true,
                    definition.Hidden == true,
                    definition.SemiHidden == true,
                    definition.UnhideWhenUsed == true,
                    definition.Locked == true,
                    definition.CustomStyle == true,
                    definition.UiPriority));
            }
        }

        if (type is null || type == EditorStyleType.Table)
        {
            tableInUse = BuildTableStylesInUse();
            foreach (var entry in styles.TableStyles)
            {
                var definition = entry.Value;
                var name = string.IsNullOrWhiteSpace(definition.Name) ? entry.Key : definition.Name!;
                list.Add(new EditorStyleInfo(
                    entry.Key,
                    name,
                    EditorStyleType.Table,
                    string.Equals(entry.Key, styles.DefaultTableStyleId, StringComparison.OrdinalIgnoreCase),
                    tableInUse.Contains(entry.Key),
                    definition.QuickStyle == true,
                    definition.Hidden == true,
                    definition.SemiHidden == true,
                    definition.UnhideWhenUsed == true,
                    definition.Locked == true,
                    definition.CustomStyle == true,
                    definition.UiPriority));
            }
        }

        list.Sort(CompareStyleInfo);
        return list;
    }

    public EditorValue<string> GetCurrentCharacterStyleId()
    {
        var selection = _session.Selection.Normalize();
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return EditorValue<string>.Missing();
        }

        var defaultId = _session.Document.Styles.DefaultCharacterStyleId;
        if (selection.IsEmpty)
        {
            var paragraphIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
            var paragraph = _session.GetParagraphFast(paragraphIndex);
            var styleId = GetRunStyleIdAtCaret(paragraph, selection.Start.Offset, defaultId);
            return styleId is null ? EditorValue<string>.Missing() : EditorValue<string>.FromValue(styleId);
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var styleIds = new OptionalEditorValueAccumulator<string>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;
            AddRunStylesInRange(paragraph, Math.Clamp(startOffset, 0, paragraphLength), Math.Clamp(endOffset, 0, paragraphLength), defaultId, ref styleIds);
        }

        return styleIds.Build();
    }

    public EditorValue<string> GetCurrentTableStyleId()
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

        var defaultId = _session.Document.Styles.DefaultTableStyleId;
        var styleIds = new OptionalEditorValueAccumulator<string>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var location = _session.Document.GetParagraphLocation(i);
            if (location.IsInTable && location.Table is not null)
            {
                styleIds.Add(location.Table.StyleId ?? defaultId);
            }
            else
            {
                styleIds.Add(null);
            }
        }

        return styleIds.Build();
    }

    public ParagraphStyleDefinition? GetParagraphStyleDefinition(string styleId)
    {
        return GetParagraphStyle(styleId);
    }

    public CharacterStyleDefinition? GetCharacterStyleDefinition(string styleId)
    {
        return TryGetCharacterStyle(styleId, out var style) ? style : null;
    }

    public TableStyleDefinition? GetTableStyleDefinition(string styleId)
    {
        return TryGetTableStyle(styleId, out var style) ? style : null;
    }

    public TextStyle? GetStylePreview(EditorStyleType type, string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        var resolver = new DocumentStyleResolver(_session.Document);
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!_session.Document.Styles.ParagraphStyles.ContainsKey(styleId))
                {
                    return null;
                }

                return resolver.ResolveParagraphTextStyle(new ParagraphBlock { StyleId = styleId }, _session.Document.DefaultTextStyle);
            case EditorStyleType.Character:
                if (!_session.Document.Styles.CharacterStyles.ContainsKey(styleId))
                {
                    return null;
                }

                var defaultParagraph = new ParagraphBlock { StyleId = _session.Document.Styles.DefaultParagraphStyleId };
                var paragraphStyle = resolver.ResolveParagraphTextStyle(defaultParagraph, _session.Document.DefaultTextStyle);
                return resolver.ResolveRunStyle(styleId, null, paragraphStyle);
            default:
                return null;
        }
    }

    public bool ApplyStyle(EditorStyleType type, string styleId)
    {
        return type switch
        {
            EditorStyleType.Paragraph => ApplyParagraphStyle(styleId),
            EditorStyleType.Character => ApplyCharacterStyle(styleId),
            EditorStyleType.Table => ApplyTableStyle(styleId),
            _ => false
        };
    }

    public bool RenameStyle(EditorStyleType type, string styleId, string name)
    {
        if (string.IsNullOrWhiteSpace(styleId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (string.Equals(paragraph.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                paragraph.Name = trimmed;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (string.Equals(character.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                character.Name = trimmed;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (string.Equals(table.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                table.Name = trimmed;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStyleBasedOn(EditorStyleType type, string styleId, string? basedOnId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var resolved = ResolveStyleIdOrNull(type, basedOnId);
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (string.Equals(paragraph.BasedOnId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                paragraph.BasedOnId = resolved;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (string.Equals(character.BasedOnId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                character.BasedOnId = resolved;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (string.Equals(table.BasedOnId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                table.BasedOnId = resolved;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStyleNext(EditorStyleType type, string styleId, string? nextStyleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var resolved = ResolveStyleIdOrNull(type, nextStyleId);
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (string.Equals(paragraph.NextStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                paragraph.NextStyleId = resolved;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (string.Equals(character.NextStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                character.NextStyleId = resolved;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (string.Equals(table.NextStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                table.NextStyleId = resolved;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetDefaultStyle(EditorStyleType type, string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var styles = _session.Document.Styles;
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!styles.ParagraphStyles.ContainsKey(styleId))
                {
                    return false;
                }

                if (string.Equals(styles.DefaultParagraphStyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                styles.DefaultParagraphStyleId = styleId;
                break;
            case EditorStyleType.Character:
                if (!styles.CharacterStyles.ContainsKey(styleId))
                {
                    return false;
                }

                if (string.Equals(styles.DefaultCharacterStyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                styles.DefaultCharacterStyleId = styleId;
                break;
            case EditorStyleType.Table:
                if (!styles.TableStyles.ContainsKey(styleId))
                {
                    return false;
                }

                if (string.Equals(styles.DefaultTableStyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                styles.DefaultTableStyleId = styleId;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStyleQuickStyle(EditorStyleType type, string styleId, bool? quickStyle)
    {
        return SetStyleFlag(type, styleId, quickStyle, StyleFlag.QuickStyle);
    }

    public bool SetStyleHidden(EditorStyleType type, string styleId, bool? hidden)
    {
        return SetStyleFlag(type, styleId, hidden, StyleFlag.Hidden);
    }

    public bool SetStyleSemiHidden(EditorStyleType type, string styleId, bool? semiHidden)
    {
        return SetStyleFlag(type, styleId, semiHidden, StyleFlag.SemiHidden);
    }

    public bool SetStyleUnhideWhenUsed(EditorStyleType type, string styleId, bool? unhideWhenUsed)
    {
        return SetStyleFlag(type, styleId, unhideWhenUsed, StyleFlag.UnhideWhenUsed);
    }

    public bool SetStyleAutoRedefine(EditorStyleType type, string styleId, bool? autoRedefine)
    {
        return SetStyleFlag(type, styleId, autoRedefine, StyleFlag.AutoRedefine);
    }

    public bool SetStyleLocked(EditorStyleType type, string styleId, bool? locked)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph))
                {
                    return false;
                }

                if (paragraph.Locked == locked)
                {
                    return false;
                }

                paragraph.Locked = locked;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character))
                {
                    return false;
                }

                if (character.Locked == locked)
                {
                    return false;
                }

                character.Locked = locked;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table))
                {
                    return false;
                }

                if (table.Locked == locked)
                {
                    return false;
                }

                table.Locked = locked;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStyleLinkedStyle(EditorStyleType type, string styleId, string? linkedStyleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var resolved = string.IsNullOrWhiteSpace(linkedStyleId) ? null : linkedStyleId.Trim();
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (string.Equals(paragraph.LinkedStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                paragraph.LinkedStyleId = resolved;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (string.Equals(character.LinkedStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                character.LinkedStyleId = resolved;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (string.Equals(table.LinkedStyleId, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                table.LinkedStyleId = resolved;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStylePrimaryStyle(EditorStyleType type, string styleId, bool? primaryStyle)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (paragraph.PrimaryStyle == primaryStyle)
                {
                    return false;
                }

                paragraph.PrimaryStyle = primaryStyle;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (character.PrimaryStyle == primaryStyle)
                {
                    return false;
                }

                character.PrimaryStyle = primaryStyle;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (table.PrimaryStyle == primaryStyle)
                {
                    return false;
                }

                table.PrimaryStyle = primaryStyle;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStyleCustomStyle(EditorStyleType type, string styleId, bool? customStyle)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (paragraph.CustomStyle == customStyle)
                {
                    return false;
                }

                paragraph.CustomStyle = customStyle;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (character.CustomStyle == customStyle)
                {
                    return false;
                }

                character.CustomStyle = customStyle;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (table.CustomStyle == customStyle)
                {
                    return false;
                }

                table.CustomStyle = customStyle;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool SetStylePriority(EditorStyleType type, string styleId, int? priority)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                if (paragraph.UiPriority == priority)
                {
                    return false;
                }

                paragraph.UiPriority = priority;
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                if (character.UiPriority == priority)
                {
                    return false;
                }

                character.UiPriority = priority;
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                if (table.UiPriority == priority)
                {
                    return false;
                }

                table.UiPriority = priority;
                break;
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool UpdateParagraphStyleProperties(string styleId, TextStyleProperties? runProperties, ParagraphStyleProperties? paragraphProperties)
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

        ApplyTextStyleProperties(style.RunProperties, runProperties);
        ApplyParagraphStyleProperties(style.ParagraphProperties, paragraphProperties);
        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool UpdateCharacterStyleProperties(string styleId, TextStyleProperties? runProperties)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.CharacterStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        ApplyTextStyleProperties(style.RunProperties, runProperties);
        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool UpdateTableStyleProperties(string styleId, TableProperties? tableProperties, TableCellProperties? cellProperties)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.TableStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        ApplyTableProperties(style.TableProperties, tableProperties);
        ApplyTableCellProperties(style.CellProperties, cellProperties);
        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool UpdateTableStyleConditions(string styleId, IReadOnlyDictionary<TableStyleCondition, TableStyleConditionProperties>? conditions)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.TableStyles.TryGetValue(styleId, out var style))
        {
            return false;
        }

        if (style.Locked == true)
        {
            return false;
        }

        if (!ApplyTableStyleConditions(style.Conditions, conditions))
        {
            return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public bool CreateStyle(EditorStyleCreateOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            return false;
        }

        var styles = _session.Document.Styles;
        var name = options.Name.Trim();
        var styleId = ResolveCreateStyleId(options.Type, options.StyleId, name);
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        switch (options.Type)
        {
            case EditorStyleType.Paragraph:
            {
                var definition = new ParagraphStyleDefinition(styleId)
                {
                    Name = name,
                    BasedOnId = ResolveStyleIdOrNull(EditorStyleType.Paragraph, options.BasedOnId),
                    NextStyleId = ResolveStyleIdOrNull(EditorStyleType.Paragraph, options.NextStyleId),
                    LinkedStyleId = ResolveLinkedStyleId(EditorStyleType.Paragraph, options.LinkedStyleId),
                    QuickStyle = options.QuickStyle,
                    AutoRedefine = options.AutoRedefine,
                    CustomStyle = true
                };

                ApplyTextStyleProperties(definition.RunProperties, options.RunProperties);
                ApplyParagraphStyleProperties(definition.ParagraphProperties, options.ParagraphProperties);
                styles.ParagraphStyles[styleId] = definition;
                break;
            }
            case EditorStyleType.Character:
            {
                var definition = new CharacterStyleDefinition(styleId)
                {
                    Name = name,
                    BasedOnId = ResolveStyleIdOrNull(EditorStyleType.Character, options.BasedOnId),
                    NextStyleId = ResolveStyleIdOrNull(EditorStyleType.Character, options.NextStyleId),
                    LinkedStyleId = ResolveLinkedStyleId(EditorStyleType.Character, options.LinkedStyleId),
                    QuickStyle = options.QuickStyle,
                    AutoRedefine = options.AutoRedefine,
                    CustomStyle = true
                };

                ApplyTextStyleProperties(definition.RunProperties, options.RunProperties);
                styles.CharacterStyles[styleId] = definition;
                break;
            }
            case EditorStyleType.Table:
            {
                var definition = new TableStyleDefinition(styleId)
                {
                    Name = name,
                    BasedOnId = ResolveStyleIdOrNull(EditorStyleType.Table, options.BasedOnId),
                    NextStyleId = ResolveStyleIdOrNull(EditorStyleType.Table, options.NextStyleId),
                    LinkedStyleId = ResolveLinkedStyleId(EditorStyleType.Table, options.LinkedStyleId),
                    QuickStyle = options.QuickStyle,
                    AutoRedefine = options.AutoRedefine,
                    CustomStyle = true
                };

                ApplyTableProperties(definition.TableProperties, options.TableProperties);
                ApplyTableCellProperties(definition.CellProperties, options.TableCellProperties);
                styles.TableStyles[styleId] = definition;
                break;
            }
            default:
                return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    public EditorStyleInspectorSnapshot GetStyleInspectorSnapshot()
    {
        var paragraphStyle = GetCurrentParagraphStyleId();
        var characterStyle = GetCurrentCharacterStyleId();
        var tableStyle = GetCurrentTableStyleId();
        var direct = GetDirectFormattingInfo();

        var paragraphDirect = direct.HasParagraphFormatting
            ? "Direct paragraph formatting"
            : "None";
        var characterDirect = direct.HasCharacterFormatting
            ? "Direct character formatting"
            : "None";

        return new EditorStyleInspectorSnapshot(
            paragraphStyle,
            characterStyle,
            tableStyle,
            paragraphDirect,
            characterDirect);
    }

    public bool ClearDirectFormatting()
    {
        var selection = _session.Selection.Normalize();
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var changed = false;
        if (selection.IsEmpty)
        {
            var paragraphIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
            var paragraph = _session.GetParagraphFast(paragraphIndex);
            changed |= ClearParagraphFormatting(paragraph);
            changed |= ClearRunFormattingAtCaret(paragraph, selection.Start.Offset);
        }
        else
        {
            var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
            var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
            if (startIndex > endIndex)
            {
                (startIndex, endIndex) = (endIndex, startIndex);
            }

            for (var i = startIndex; i <= endIndex; i++)
            {
                var paragraph = _session.GetParagraphFast(i);
                if (ClearParagraphFormatting(paragraph))
                {
                    changed = true;
                }

                if (ClearRunFormattingInRange(paragraph, selection, i, startIndex, endIndex))
                {
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }

    private enum StyleFlag
    {
        QuickStyle,
        Hidden,
        SemiHidden,
        UnhideWhenUsed,
        AutoRedefine
    }

    private bool SetStyleFlag(EditorStyleType type, string styleId, bool? value, StyleFlag flag)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        bool updated;
        switch (type)
        {
            case EditorStyleType.Paragraph:
                if (!TryGetParagraphStyle(styleId, out var paragraph) || paragraph.Locked == true)
                {
                    return false;
                }

                updated = SetFlagValue(flag,
                    () => GetFlagValue(flag, paragraph),
                    v => SetFlagValue(flag, paragraph, v),
                    value);
                break;
            case EditorStyleType.Character:
                if (!TryGetCharacterStyle(styleId, out var character) || character.Locked == true)
                {
                    return false;
                }

                updated = SetFlagValue(flag,
                    () => GetFlagValue(flag, character),
                    v => SetFlagValue(flag, character, v),
                    value);
                break;
            case EditorStyleType.Table:
                if (!TryGetTableStyle(styleId, out var table) || table.Locked == true)
                {
                    return false;
                }

                updated = SetFlagValue(flag,
                    () => GetFlagValue(flag, table),
                    v => SetFlagValue(flag, table, v),
                    value);
                break;
            default:
                return false;
        }

        if (!updated)
        {
            return false;
        }

        _session.RefreshLayout();
        NotifyStylesChanged();
        return true;
    }

    private static bool? GetFlagValue(StyleFlag flag, ParagraphStyleDefinition style)
    {
        return flag switch
        {
            StyleFlag.QuickStyle => style.QuickStyle,
            StyleFlag.Hidden => style.Hidden,
            StyleFlag.SemiHidden => style.SemiHidden,
            StyleFlag.UnhideWhenUsed => style.UnhideWhenUsed,
            StyleFlag.AutoRedefine => style.AutoRedefine,
            _ => null
        };
    }

    private static bool? GetFlagValue(StyleFlag flag, CharacterStyleDefinition style)
    {
        return flag switch
        {
            StyleFlag.QuickStyle => style.QuickStyle,
            StyleFlag.Hidden => style.Hidden,
            StyleFlag.SemiHidden => style.SemiHidden,
            StyleFlag.UnhideWhenUsed => style.UnhideWhenUsed,
            StyleFlag.AutoRedefine => style.AutoRedefine,
            _ => null
        };
    }

    private static bool? GetFlagValue(StyleFlag flag, TableStyleDefinition style)
    {
        return flag switch
        {
            StyleFlag.QuickStyle => style.QuickStyle,
            StyleFlag.Hidden => style.Hidden,
            StyleFlag.SemiHidden => style.SemiHidden,
            StyleFlag.UnhideWhenUsed => style.UnhideWhenUsed,
            StyleFlag.AutoRedefine => style.AutoRedefine,
            _ => null
        };
    }

    private static void SetFlagValue(StyleFlag flag, ParagraphStyleDefinition style, bool? value)
    {
        switch (flag)
        {
            case StyleFlag.QuickStyle:
                style.QuickStyle = value;
                break;
            case StyleFlag.Hidden:
                style.Hidden = value;
                break;
            case StyleFlag.SemiHidden:
                style.SemiHidden = value;
                break;
            case StyleFlag.UnhideWhenUsed:
                style.UnhideWhenUsed = value;
                break;
            case StyleFlag.AutoRedefine:
                style.AutoRedefine = value;
                break;
            default:
                break;
        }
    }

    private static void SetFlagValue(StyleFlag flag, CharacterStyleDefinition style, bool? value)
    {
        switch (flag)
        {
            case StyleFlag.QuickStyle:
                style.QuickStyle = value;
                break;
            case StyleFlag.Hidden:
                style.Hidden = value;
                break;
            case StyleFlag.SemiHidden:
                style.SemiHidden = value;
                break;
            case StyleFlag.UnhideWhenUsed:
                style.UnhideWhenUsed = value;
                break;
            case StyleFlag.AutoRedefine:
                style.AutoRedefine = value;
                break;
            default:
                break;
        }
    }

    private static void SetFlagValue(StyleFlag flag, TableStyleDefinition style, bool? value)
    {
        switch (flag)
        {
            case StyleFlag.QuickStyle:
                style.QuickStyle = value;
                break;
            case StyleFlag.Hidden:
                style.Hidden = value;
                break;
            case StyleFlag.SemiHidden:
                style.SemiHidden = value;
                break;
            case StyleFlag.UnhideWhenUsed:
                style.UnhideWhenUsed = value;
                break;
            case StyleFlag.AutoRedefine:
                style.AutoRedefine = value;
                break;
            default:
                break;
        }
    }

    private static bool SetFlagValue(StyleFlag flag, Func<bool?> getter, Action<bool?> setter, bool? value)
    {
        var current = getter();
        if (current == value)
        {
            return false;
        }

        setter(value);
        return true;
    }

    private bool ApplyCharacterStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.CharacterStyles.ContainsKey(styleId))
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var changed = selection.IsEmpty
            ? ApplyCharacterStyleAtCaret(selection.Start, styleId)
            : ApplyCharacterStyleToRange(selection, styleId);

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }

    private bool ApplyCharacterStyleAtCaret(TextPosition caret, string styleId)
    {
        var paragraphCount = _session.GetParagraphCountFast();
        var paragraphIndex = Math.Clamp(caret.ParagraphIndex, 0, paragraphCount - 1);
        var paragraph = _session.GetParagraphFast(paragraphIndex);
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var offset = Math.Clamp(caret.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        var position = 0;
        RunInline? target = null;

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inline is not RunInline run)
            {
                continue;
            }

            if (offset >= inlineStart && offset <= inlineEnd)
            {
                target = run;
                break;
            }

            target = run;
        }

        if (target is null)
        {
            return false;
        }

        if (string.Equals(target.StyleId, styleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        target.StyleId = styleId;
        return true;
    }

    private bool ApplyCharacterStyleToRange(TextRange range, string styleId)
    {
        var paragraphCount = _session.GetParagraphCountFast();
        var startIndex = Math.Clamp(range.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(range.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var changed = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? range.Start.Offset : 0;
            var endOffset = i == endIndex ? range.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (startOffset >= endOffset)
            {
                continue;
            }

            changed |= ApplyCharacterStyleToParagraphRange(paragraph, startOffset, endOffset, styleId);
        }

        return changed;
    }

    private bool ApplyCharacterStyleToParagraphRange(ParagraphBlock paragraph, int startOffset, int endOffset, string styleId)
    {
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count + 2);
        var changed = false;
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inline is not RunInline run)
            {
                newInlines.Add(inline);
                continue;
            }

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                newInlines.Add(run);
                continue;
            }

            var selectionStart = Math.Max(startOffset, inlineStart) - inlineStart;
            var selectionEnd = Math.Min(endOffset, inlineEnd) - inlineStart;

            if (selectionStart <= 0 && selectionEnd >= length)
            {
                if (!string.Equals(run.StyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    run.StyleId = styleId;
                    changed = true;
                }

                newInlines.Add(run);
                continue;
            }

            if (selectionStart > 0)
            {
                newInlines.Add(CloneRunSlice(run, 0, selectionStart));
            }

            if (selectionEnd > selectionStart)
            {
                newInlines.Add(CloneRunSlice(run, selectionStart, selectionEnd - selectionStart, styleId));
                changed = true;
            }

            if (selectionEnd < length)
            {
                newInlines.Add(CloneRunSlice(run, selectionEnd, length - selectionEnd));
            }
        }

        if (changed)
        {
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(newInlines);
        }

        return changed;
    }

    private bool ApplyTableStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (!_session.Document.Styles.TableStyles.ContainsKey(styleId))
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

        var changed = false;
        var tables = new HashSet<TableBlock>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var location = _session.Document.GetParagraphLocation(i);
            if (!location.IsInTable || location.Table is null)
            {
                continue;
            }

            if (!tables.Add(location.Table))
            {
                continue;
            }

            if (string.Equals(location.Table.StyleId, styleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            location.Table.StyleId = styleId;
            changed = true;
        }

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }

    private static RunInline CloneRunSlice(RunInline run, int start, int length, string? styleId = null)
    {
        var buffer = run.Text.SliceBuffer(start, length);
        return new RunInline(buffer, run.Style)
        {
            StyleId = styleId ?? run.StyleId,
            Hyperlink = run.Hyperlink
        };
    }

    private static void EnsureParagraphInlines(ParagraphBlock paragraph)
    {
        DocumentEditHelpers.EnsureParagraphInlines(paragraph);
    }

    private static int CompareStyleInfo(EditorStyleInfo left, EditorStyleInfo right)
    {
        var typeCompare = left.Type.CompareTo(right.Type);
        if (typeCompare != 0)
        {
            return typeCompare;
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyStylesChanged()
    {
        StylesChanged?.Invoke(this, EventArgs.Empty);
    }

    private HashSet<string> BuildParagraphStylesInUse()
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

    private HashSet<string> BuildCharacterStylesInUse()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultId = _session.Document.Styles.DefaultCharacterStyleId;
        if (!string.IsNullOrWhiteSpace(defaultId))
        {
            used.Add(defaultId);
        }

        var count = _session.GetParagraphCountFast();
        for (var i = 0; i < count; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            if (paragraph.Inlines.Count == 0)
            {
                continue;
            }

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is RunInline run && !string.IsNullOrWhiteSpace(run.StyleId))
                {
                    used.Add(run.StyleId);
                }
            }
        }

        return used;
    }

    private HashSet<string> BuildTableStylesInUse()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultId = _session.Document.Styles.DefaultTableStyleId;
        if (!string.IsNullOrWhiteSpace(defaultId))
        {
            used.Add(defaultId);
        }

        foreach (var block in _session.Document.Blocks)
        {
            if (block is not TableBlock table)
            {
                continue;
            }

            var styleId = table.StyleId ?? defaultId;
            if (!string.IsNullOrWhiteSpace(styleId))
            {
                used.Add(styleId);
            }
        }

        return used;
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

    private string? ResolveStyleIdOrNull(EditorStyleType type, string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        var styles = _session.Document.Styles;
        return type switch
        {
            EditorStyleType.Paragraph => styles.ParagraphStyles.ContainsKey(styleId) ? styleId : null,
            EditorStyleType.Character => styles.CharacterStyles.ContainsKey(styleId) ? styleId : null,
            EditorStyleType.Table => styles.TableStyles.ContainsKey(styleId) ? styleId : null,
            _ => null
        };
    }

    private string? ResolveLinkedStyleId(EditorStyleType type, string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        var resolvedType = ResolveLinkedStyleType(type);
        return ResolveStyleIdOrNull(resolvedType, styleId);
    }

    private static EditorStyleType ResolveLinkedStyleType(EditorStyleType type)
    {
        return type switch
        {
            EditorStyleType.Paragraph => EditorStyleType.Character,
            EditorStyleType.Character => EditorStyleType.Paragraph,
            _ => type
        };
    }

    private static void ApplyTextStyleProperties(TextStyleProperties target, TextStyleProperties? source)
    {
        if (source is null)
        {
            ClearTextStyleProperties(target);
            return;
        }

        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private static void ClearTextStyleProperties(TextStyleProperties target)
    {
        target.FontFamily = null;
        target.FontFamilyAscii = null;
        target.FontFamilyHighAnsi = null;
        target.FontFamilyEastAsia = null;
        target.FontFamilyComplexScript = null;
        target.FontSize = null;
        target.FontSizeComplexScript = null;
        target.FontWeight = null;
        target.FontStyle = null;
        target.Color = null;
        target.ThemeColor = null;
        target.ThemeTint = null;
        target.ThemeShade = null;
        target.VerticalPosition = null;
        target.BaselineOffset = null;
        target.LetterSpacing = null;
        target.HorizontalScale = null;
        target.Kerning = null;
        target.Caps = null;
        target.SmallCaps = null;
        target.Underline = null;
        target.UnderlineStyle = null;
        target.UnderlineColor = null;
        target.UnderlineThemeColor = null;
        target.UnderlineThemeTint = null;
        target.UnderlineThemeShade = null;
        target.Strikethrough = null;
        target.HighlightColor = null;
        target.Hidden = null;
        target.ThemeFontAscii = null;
        target.ThemeFontHighAnsi = null;
        target.ThemeFontEastAsia = null;
        target.ThemeFontComplexScript = null;
        target.Language = null;
        target.LanguageEastAsia = null;
        target.LanguageBidi = null;
        target.EastAsianLayout = null;
        target.OpenTypeFeatures = null;
        target.Effects = null;
    }

    private static void ApplyParagraphStyleProperties(ParagraphStyleProperties target, ParagraphStyleProperties? source)
    {
        if (source is null)
        {
            ClearParagraphStyleProperties(target);
            return;
        }

        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.TabStops.Clear();
        foreach (var tab in source.TabStops)
        {
            target.TabStops.Add(tab.Clone());
        }

        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static void ClearParagraphStyleProperties(ParagraphStyleProperties target)
    {
        target.Alignment = null;
        target.SpacingBefore = null;
        target.SpacingAfter = null;
        target.SpacingBeforeLines = null;
        target.SpacingAfterLines = null;
        target.AutoSpacingBefore = null;
        target.AutoSpacingAfter = null;
        target.LineSpacing = null;
        target.LineSpacingRule = null;
        target.IndentLeft = null;
        target.IndentRight = null;
        target.FirstLineIndent = null;
        target.TabStops.Clear();
        target.KeepWithNext = null;
        target.KeepLinesTogether = null;
        target.WidowControl = null;
        target.PageBreakBefore = null;
        target.ContextualSpacing = null;
        target.Bidi = null;
        target.TextDirection = null;
        target.EastAsianLayout = null;
        target.ShadingColor = null;
        target.SuppressLineNumbers = null;
        target.DropCap = null;
        target.Frame = null;
        target.Borders.Top = null;
        target.Borders.Bottom = null;
        target.Borders.Left = null;
        target.Borders.Right = null;
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties? source)
    {
        if (source is null)
        {
            ClearTableProperties(target);
            return;
        }

        target.Width = source.Width;
        target.WidthUnit = source.WidthUnit;
        target.Indent = source.Indent;
        target.IndentUnit = source.IndentUnit;
        target.Alignment = source.Alignment;
        target.LayoutMode = source.LayoutMode;
        target.CellSpacing = source.CellSpacing;
        target.CellSpacingUnit = source.CellSpacingUnit;
        target.CellPadding = source.CellPadding;
        target.ShadingColor = source.ShadingColor;
        target.Look = source.Look?.Clone();
        target.FloatingAnchor = source.FloatingAnchor is null ? null : CloneFloatingAnchor(source.FloatingAnchor);

        target.ColumnWidths.Clear();
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.Borders.InsideHorizontal = source.Borders.InsideHorizontal?.Clone();
        target.Borders.InsideVertical = source.Borders.InsideVertical?.Clone();
    }

    private static void ClearTableProperties(TableProperties target)
    {
        target.Width = null;
        target.WidthUnit = null;
        target.Indent = null;
        target.IndentUnit = null;
        target.Alignment = null;
        target.LayoutMode = null;
        target.CellSpacing = null;
        target.CellSpacingUnit = null;
        target.CellPadding = null;
        target.ShadingColor = null;
        target.Look = null;
        target.FloatingAnchor = null;
        target.ColumnWidths.Clear();
        target.Borders.Top = null;
        target.Borders.Bottom = null;
        target.Borders.Left = null;
        target.Borders.Right = null;
        target.Borders.InsideHorizontal = null;
        target.Borders.InsideVertical = null;
    }

    private static void ApplyTableCellProperties(TableCellProperties target, TableCellProperties? source)
    {
        if (source is null)
        {
            ClearTableCellProperties(target);
            return;
        }

        target.Padding = source.Padding;
        target.ShadingColor = source.ShadingColor;
        target.VerticalAlignment = source.VerticalAlignment;
        target.TextDirection = source.TextDirection;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static bool ApplyTableStyleConditions(
        Dictionary<TableStyleCondition, TableStyleConditionProperties> target,
        IReadOnlyDictionary<TableStyleCondition, TableStyleConditionProperties>? source)
    {
        if (source is null)
        {
            if (target.Count == 0)
            {
                return false;
            }

            target.Clear();
            return true;
        }

        if (source.Count == 0 && target.Count == 0)
        {
            return false;
        }

        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = CloneTableStyleCondition(pair.Value);
        }

        return true;
    }

    private static TableStyleConditionProperties CloneTableStyleCondition(TableStyleConditionProperties source)
    {
        var clone = new TableStyleConditionProperties();
        ApplyTableProperties(clone.TableProperties, source.TableProperties);
        ApplyTableCellProperties(clone.CellProperties, source.CellProperties);
        return clone;
    }

    private static void ClearTableCellProperties(TableCellProperties target)
    {
        target.Padding = null;
        target.ShadingColor = null;
        target.VerticalAlignment = null;
        target.TextDirection = null;
        target.Borders.Top = null;
        target.Borders.Bottom = null;
        target.Borders.Left = null;
        target.Borders.Right = null;
    }

    private static FloatingAnchor CloneFloatingAnchor(FloatingAnchor source)
    {
        return new FloatingAnchor
        {
            HorizontalReference = source.HorizontalReference,
            VerticalReference = source.VerticalReference,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            WrapStyle = source.WrapStyle,
            WrapSide = source.WrapSide,
            WrapPolygon = source.WrapPolygon is null ? null : new FloatingWrapPolygon(source.WrapPolygon.Points.ToArray()),
            BehindText = source.BehindText,
            AllowOverlap = source.AllowOverlap,
            ZOrder = source.ZOrder,
            Distance = source.Distance,
            AnchorOffset = source.AnchorOffset
        };
    }

    private static string? GetRunStyleIdAtCaret(ParagraphBlock paragraph, int offset, string? defaultId)
    {
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return defaultId;
        }

        var position = 0;
        RunInline? lastRun = null;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            if (inline is RunInline run)
            {
                if (offset >= position && offset < position + length)
                {
                    return run.StyleId ?? defaultId;
                }

                lastRun = run;
            }

            position += length;
        }

        return lastRun?.StyleId ?? defaultId;
    }

    private static void AddRunStylesInRange(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        string? defaultId,
        ref OptionalEditorValueAccumulator<string> accumulator)
    {
        if (startOffset >= endOffset)
        {
            return;
        }

        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            accumulator.Add(defaultId);
            return;
        }

        var position = 0;
        var foundRun = false;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                continue;
            }

            if (inline is RunInline run)
            {
                accumulator.Add(run.StyleId ?? defaultId);
                foundRun = true;
            }
        }

        if (!foundRun)
        {
            accumulator.Add(defaultId);
        }
    }

    private static bool ClearParagraphFormatting(ParagraphBlock paragraph)
    {
        if (!HasParagraphFormatting(paragraph.Properties))
        {
            return false;
        }

        ClearParagraphDirectProperties(paragraph.Properties);
        return true;
    }

    private static void ClearParagraphDirectProperties(ParagraphProperties properties)
    {
        properties.Alignment = null;
        properties.SpacingBefore = null;
        properties.SpacingAfter = null;
        properties.SpacingBeforeLines = null;
        properties.SpacingAfterLines = null;
        properties.AutoSpacingBefore = null;
        properties.AutoSpacingAfter = null;
        properties.LineSpacing = null;
        properties.LineSpacingRule = null;
        properties.IndentLeft = null;
        properties.IndentRight = null;
        properties.FirstLineIndent = null;
        properties.TabStops.Clear();
        properties.KeepWithNext = null;
        properties.KeepLinesTogether = null;
        properties.WidowControl = null;
        properties.PageBreakBefore = null;
        properties.ContextualSpacing = null;
        properties.Bidi = null;
        properties.TextDirection = null;
        properties.EastAsianLayout = null;
        properties.ShadingColor = null;
        properties.SuppressLineNumbers = null;
        properties.DropCap = null;
        properties.Frame = null;
        properties.Borders.Top = null;
        properties.Borders.Bottom = null;
        properties.Borders.Left = null;
        properties.Borders.Right = null;
    }

    private static bool ClearRunFormattingAtCaret(ParagraphBlock paragraph, int offset)
    {
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var position = 0;
        RunInline? target = null;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            if (inline is RunInline run)
            {
                if (offset >= position && offset < position + length)
                {
                    target = run;
                    break;
                }

                target = run;
            }

            position += length;
        }

        if (target is null || target.Style is null)
        {
            return false;
        }

        target.Style = null;
        return true;
    }

    private static bool ClearRunFormattingInRange(ParagraphBlock paragraph, TextRange selection, int paragraphIndex, int startIndex, int endIndex)
    {
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
        var startOffset = paragraphIndex == startIndex ? selection.Start.Offset : 0;
        var endOffset = paragraphIndex == endIndex ? selection.End.Offset : paragraphLength;
        startOffset = Math.Clamp(startOffset, 0, paragraphLength);
        endOffset = Math.Clamp(endOffset, 0, paragraphLength);
        if (startOffset >= endOffset)
        {
            return false;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count + 2);
        var changed = false;
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inline is not RunInline run)
            {
                newInlines.Add(inline);
                continue;
            }

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                newInlines.Add(run);
                continue;
            }

            var selectionStart = Math.Max(startOffset, inlineStart) - inlineStart;
            var selectionEnd = Math.Min(endOffset, inlineEnd) - inlineStart;

            if (selectionStart <= 0 && selectionEnd >= length)
            {
                if (run.Style is not null)
                {
                    run.Style = null;
                    changed = true;
                }

                newInlines.Add(run);
                continue;
            }

            if (selectionStart > 0)
            {
                newInlines.Add(CloneRunSlice(run, 0, selectionStart));
            }

            if (selectionEnd > selectionStart)
            {
                var selected = CloneRunSlice(run, selectionStart, selectionEnd - selectionStart);
                if (selected.Style is not null)
                {
                    selected.Style = null;
                    changed = true;
                }

                newInlines.Add(selected);
            }

            if (selectionEnd < length)
            {
                newInlines.Add(CloneRunSlice(run, selectionEnd, length - selectionEnd));
            }
        }

        if (changed)
        {
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(newInlines);
        }

        return changed;
    }

    private string ResolveCreateStyleId(EditorStyleType type, string? requestedId, string name)
    {
        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            var trimmed = requestedId.Trim();
            if (!StyleExists(type, trimmed))
            {
                return trimmed;
            }
        }

        var baseId = BuildStyleId(name);
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "Style";
        }

        var candidate = baseId;
        var counter = 0;
        while (StyleExists(type, candidate))
        {
            counter++;
            candidate = string.Concat(baseId, counter.ToString(CultureInfo.InvariantCulture));
        }

        return candidate;
    }

    private bool StyleExists(EditorStyleType type, string styleId)
    {
        var styles = _session.Document.Styles;
        return type switch
        {
            EditorStyleType.Paragraph => styles.ParagraphStyles.ContainsKey(styleId),
            EditorStyleType.Character => styles.CharacterStyles.ContainsKey(styleId),
            EditorStyleType.Table => styles.TableStyles.ContainsKey(styleId),
            _ => false
        };
    }

    private static string BuildStyleId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                continue;
            }
        }

        return length == 0 ? string.Empty : new string(buffer.Slice(0, length));
    }

    private bool TryGetParagraphStyle(string styleId, out ParagraphStyleDefinition style)
    {
        style = null!;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        return _session.Document.Styles.ParagraphStyles.TryGetValue(styleId, out style!);
    }

    private bool TryGetCharacterStyle(string styleId, out CharacterStyleDefinition style)
    {
        style = null!;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        return _session.Document.Styles.CharacterStyles.TryGetValue(styleId, out style!);
    }

    private bool TryGetTableStyle(string styleId, out TableStyleDefinition style)
    {
        style = null!;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        return _session.Document.Styles.TableStyles.TryGetValue(styleId, out style!);
    }
}
