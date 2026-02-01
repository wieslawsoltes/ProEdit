using Vibe.Office.Markdown.Ast;

namespace Vibe.Office.Markdown;

internal static class MarkdownAstUtilities
{
    public static long GetMaxId(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var max = 0L;
        Traverse(document, node =>
        {
            var value = node.Id.Value;
            if (value > max)
            {
                max = value;
            }
        });
        return max;
    }

    public static void ReuseIds(MarkdownDocument previous, MarkdownDocument current, MarkdownTextEdit edit)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var deleteLength = Math.Max(0, edit.Length);
        var insertLength = edit.NewText?.Length ?? 0;
        var delta = insertLength - deleteLength;
        var beforeMap = new Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>>();
        var afterMap = new Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>>();
        var afterOldStart = edit.Start + deleteLength;
        var afterNewStart = edit.Start + insertLength;

        CollectMaps(previous, edit.Start, afterOldStart, delta, beforeMap, afterMap);

        Traverse(current, node =>
        {
            var span = node.Span;
            if (!span.IsKnown)
            {
                return;
            }

            if (span.End <= edit.Start)
            {
                if (TryPop(beforeMap, new MarkdownNodeKey(node.GetType(), span.Start, span.Length), out var id))
                {
                    node.Id = id;
                }
            }
            else if (span.Start >= afterNewStart)
            {
                if (TryPop(afterMap, new MarkdownNodeKey(node.GetType(), span.Start, span.Length), out var id))
                {
                    node.Id = id;
                }
            }
        });
    }

    private static void CollectMaps(
        MarkdownDocument previous,
        int editStart,
        int afterOldStart,
        int delta,
        Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>> beforeMap,
        Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>> afterMap)
    {
        Traverse(previous, node =>
        {
            var span = node.Span;
            if (!span.IsKnown)
            {
                return;
            }

            if (span.End <= editStart)
            {
                Add(beforeMap, new MarkdownNodeKey(node.GetType(), span.Start, span.Length), node.Id);
            }
            else if (span.Start >= afterOldStart)
            {
                var shifted = span.Start + delta;
                Add(afterMap, new MarkdownNodeKey(node.GetType(), shifted, span.Length), node.Id);
            }
        });
    }

    private static void Add(
        Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>> map,
        MarkdownNodeKey key,
        MarkdownNodeId id)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new Stack<MarkdownNodeId>();
            map[key] = list;
        }

        list.Push(id);
    }

    private static bool TryPop(
        Dictionary<MarkdownNodeKey, Stack<MarkdownNodeId>> map,
        MarkdownNodeKey key,
        out MarkdownNodeId id)
    {
        if (map.TryGetValue(key, out var list) && list.Count > 0)
        {
            id = list.Pop();
            return true;
        }

        id = default;
        return false;
    }

    private static void Traverse(MarkdownNode node, Action<MarkdownNode> visitor)
    {
        visitor(node);
        switch (node)
        {
            case MarkdownDocument document:
                foreach (var block in document.Blocks)
                {
                    Traverse(block, visitor);
                }
                break;
            case MarkdownParagraphBlock paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownHeadingBlock heading:
                foreach (var inline in heading.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownBlockQuoteBlock quote:
                foreach (var block in quote.Blocks)
                {
                    Traverse(block, visitor);
                }
                break;
            case MarkdownListBlock list:
                foreach (var item in list.Items)
                {
                    Traverse(item, visitor);
                }
                break;
            case MarkdownListItemBlock item:
                foreach (var block in item.Blocks)
                {
                    Traverse(block, visitor);
                }
                break;
            case MarkdownTableBlock table:
                foreach (var row in table.Rows)
                {
                    Traverse(row, visitor);
                }
                break;
            case MarkdownTableRow row:
                foreach (var cell in row.Cells)
                {
                    Traverse(cell, visitor);
                }
                break;
            case MarkdownTableCell cell:
                foreach (var inline in cell.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownEmphasisInline emphasis:
                foreach (var inline in emphasis.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownStrikethroughInline strike:
                foreach (var inline in strike.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownLinkInline link:
                foreach (var inline in link.Inlines)
                {
                    Traverse(inline, visitor);
                }
                break;
            case MarkdownImageInline image:
                foreach (var inline in image.AltText)
                {
                    Traverse(inline, visitor);
                }
                break;
        }
    }

    private readonly record struct MarkdownNodeKey(Type Type, int Start, int Length);
}
