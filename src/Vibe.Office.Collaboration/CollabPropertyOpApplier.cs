using System.Collections.Generic;
using System.Globalization;
using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration;

public sealed class CollabPropertyOpApplier
{
    private readonly DocumentAnchorResolver _resolver = new();
    private readonly Dictionary<PropertyKey, long> _paragraphLamports = new();
    private readonly Dictionary<PropertyKey, long> _inlineLamports = new();

    public bool ApplyParagraph(Document document, SetParagraphPropertiesOp op)
    {
        if (!_resolver.TryResolveParagraph(document, op.ParagraphNodeId, out var paragraph, out _))
        {
            return false;
        }

        var changed = false;
        foreach (var pair in op.Properties)
        {
            var key = pair.Key;
            var value = pair.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var normalizedKey = key.ToLowerInvariant();
            if (!ShouldApply(_paragraphLamports, op.ParagraphNodeId, normalizedKey, op.Lamport))
            {
                continue;
            }

            switch (normalizedKey)
            {
                case "styleid":
                    paragraph.StyleId = string.IsNullOrWhiteSpace(value) ? null : value;
                    changed = true;
                    break;
                case "listkind":
                {
                    if (!Enum.TryParse<ListKind>(value, ignoreCase: true, out var kind))
                    {
                        break;
                    }

                    var listInfo = paragraph.ListInfo ?? new ListInfo(kind);
                    paragraph.ListInfo = new ListInfo(kind, listInfo.Level, listInfo.ListId)
                    {
                        NumberFormat = listInfo.NumberFormat,
                        LevelText = listInfo.LevelText,
                        BulletSymbol = listInfo.BulletSymbol,
                        StartAt = listInfo.StartAt,
                        LeftIndent = listInfo.LeftIndent,
                        HangingIndent = listInfo.HangingIndent,
                        TabStop = listInfo.TabStop
                    };
                    changed = true;
                    break;
                }
                case "listid":
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var listId))
                    {
                        break;
                    }

                    var listInfo = paragraph.ListInfo ?? new ListInfo(ListKind.Bullet);
                    paragraph.ListInfo = new ListInfo(listInfo.Kind, listInfo.Level, listId)
                    {
                        NumberFormat = listInfo.NumberFormat,
                        LevelText = listInfo.LevelText,
                        BulletSymbol = listInfo.BulletSymbol,
                        StartAt = listInfo.StartAt,
                        LeftIndent = listInfo.LeftIndent,
                        HangingIndent = listInfo.HangingIndent,
                        TabStop = listInfo.TabStop
                    };
                    changed = true;
                    break;
                }
                case "listlevel":
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                    {
                        break;
                    }

                    var listInfo = paragraph.ListInfo ?? new ListInfo(ListKind.Bullet);
                    paragraph.ListInfo = new ListInfo(listInfo.Kind, level, listInfo.ListId)
                    {
                        NumberFormat = listInfo.NumberFormat,
                        LevelText = listInfo.LevelText,
                        BulletSymbol = listInfo.BulletSymbol,
                        StartAt = listInfo.StartAt,
                        LeftIndent = listInfo.LeftIndent,
                        HangingIndent = listInfo.HangingIndent,
                        TabStop = listInfo.TabStop
                    };
                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    public bool ApplyInline(Document document, SetInlinePropertiesOp op)
    {
        if (!_resolver.TryResolveInline(document, op.InlineNodeId, out var inline))
        {
            return false;
        }

        var changed = false;
        foreach (var pair in op.Properties)
        {
            var key = pair.Key;
            var value = pair.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var normalizedKey = key.ToLowerInvariant();
            if (!ShouldApply(_inlineLamports, op.InlineNodeId, normalizedKey, op.Lamport))
            {
                continue;
            }

            switch (normalizedKey)
            {
                case "styleid":
                    changed |= ApplyInlineStyle(inline, value);
                    break;
                case "basestyleid":
                    if (inline is RubyInline rubyBase)
                    {
                        rubyBase.BaseStyleId = value;
                        changed = true;
                    }

                    break;
                case "rubystyleid":
                    if (inline is RubyInline rubyStyle)
                    {
                        rubyStyle.RubyStyleId = value;
                        changed = true;
                    }

                    break;
            }
        }

        return changed;
    }

    private static bool ApplyInlineStyle(Inline inline, string? value)
    {
        var styleId = string.IsNullOrWhiteSpace(value) ? null : value;
        switch (inline)
        {
            case RunInline run:
                run.StyleId = styleId;
                return true;
            case EquationInline equation:
                equation.StyleId = styleId;
                return true;
            case FootnoteReferenceInline footnote:
                footnote.StyleId = styleId;
                return true;
            case EndnoteReferenceInline endnote:
                endnote.StyleId = styleId;
                return true;
            case CommentReferenceInline comment:
                comment.StyleId = styleId;
                return true;
        }

        return false;
    }

    private static bool ShouldApply(Dictionary<PropertyKey, long> lamports, Guid nodeId, string key, long lamport)
    {
        var propertyKey = new PropertyKey(nodeId, key);
        if (lamports.TryGetValue(propertyKey, out var current) && current > lamport)
        {
            return false;
        }

        lamports[propertyKey] = lamport;
        return true;
    }

    private readonly record struct PropertyKey(Guid NodeId, string Key);
}
