using ProEdit.Html.Ast;

namespace ProEdit.Html;

internal static class HtmlAstUtilities
{
    public static long GetMaxId(HtmlDocument document)
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

    public static void ReuseIds(HtmlDocument previous, HtmlDocument current, HtmlTextEdit edit)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var deleteLength = Math.Max(0, edit.Length);
        var insertLength = edit.NewText?.Length ?? 0;
        var delta = insertLength - deleteLength;
        var beforeMap = new Dictionary<HtmlNodeKey, Stack<HtmlNodeId>>();
        var afterMap = new Dictionary<HtmlNodeKey, Stack<HtmlNodeId>>();
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
                if (TryPop(beforeMap, CreateKey(node, span), out var id))
                {
                    node.Id = id;
                }
            }
            else if (span.Start >= afterNewStart)
            {
                if (TryPop(afterMap, CreateKey(node, span), out var id))
                {
                    node.Id = id;
                }
            }
        });
    }

    private static void CollectMaps(
        HtmlDocument previous,
        int editStart,
        int afterOldStart,
        int delta,
        Dictionary<HtmlNodeKey, Stack<HtmlNodeId>> beforeMap,
        Dictionary<HtmlNodeKey, Stack<HtmlNodeId>> afterMap)
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
                Add(beforeMap, CreateKey(node, span), node.Id);
            }
            else if (span.Start >= afterOldStart)
            {
                var shifted = span.Start + delta;
                var shiftedSpan = new HtmlTextSpan(shifted, span.Length);
                Add(afterMap, CreateKey(node, shiftedSpan), node.Id);
            }
        });
    }

    private static HtmlNodeKey CreateKey(HtmlNode node, HtmlTextSpan span)
    {
        var name = node is HtmlElementNode element ? element.Name : string.Empty;
        return new HtmlNodeKey(node.GetType(), span.Start, span.Length, name);
    }

    private static void Add(
        Dictionary<HtmlNodeKey, Stack<HtmlNodeId>> map,
        HtmlNodeKey key,
        HtmlNodeId id)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new Stack<HtmlNodeId>();
            map[key] = list;
        }

        list.Push(id);
    }

    private static bool TryPop(
        Dictionary<HtmlNodeKey, Stack<HtmlNodeId>> map,
        HtmlNodeKey key,
        out HtmlNodeId id)
    {
        if (map.TryGetValue(key, out var list) && list.Count > 0)
        {
            id = list.Pop();
            return true;
        }

        id = default;
        return false;
    }

    private static void Traverse(HtmlNode node, Action<HtmlNode> visitor)
    {
        visitor(node);
        switch (node)
        {
            case HtmlDocument document:
                foreach (var child in document.Children)
                {
                    Traverse(child, visitor);
                }
                break;
            case HtmlElementNode element:
                foreach (var child in element.Children)
                {
                    Traverse(child, visitor);
                }
                break;
        }
    }

    private readonly record struct HtmlNodeKey(Type Type, int Start, int Length, string Name);
}
