using System.Text;
using ProEdit.Html.Ast;

namespace ProEdit.Html;

public sealed class HtmlIncrementalSerializer
{
    private readonly HtmlAstSerializer _serializer;

    public HtmlIncrementalSerializer(HtmlOptions? options = null)
    {
        _serializer = new HtmlAstSerializer(options);
    }

    public string Serialize(HtmlDocument document)
    {
        return _serializer.Serialize(document);
    }

    public HtmlTextUpdate SerializeIncremental(
        HtmlDocument current,
        HtmlDocument previous,
        string? previousText)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        previousText ??= string.Empty;

        if (!TryComputeIncrementalText(previous, current, previousText, out var updatedText))
        {
            updatedText = _serializer.Serialize(current);
        }

        var edits = HtmlTextDiff.ComputeSingleEdit(previousText, updatedText);
        return new HtmlTextUpdate(updatedText, edits);
    }

    private bool TryComputeIncrementalText(
        HtmlDocument previous,
        HtmlDocument current,
        string previousText,
        out string updatedText)
    {
        updatedText = string.Empty;

        var oldNodes = previous.Children;
        var newNodes = current.Children;
        var oldCount = oldNodes.Count;
        var newCount = newNodes.Count;

        var prefix = 0;
        while (prefix < oldCount && prefix < newCount && oldNodes[prefix].Id == newNodes[prefix].Id)
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldCount - prefix
            && suffix < newCount - prefix
            && oldNodes[oldCount - 1 - suffix].Id == newNodes[newCount - 1 - suffix].Id)
        {
            suffix++;
        }

        if (prefix == oldCount && prefix == newCount)
        {
            updatedText = previousText;
            return true;
        }

        var oldStart = prefix;
        var oldEnd = oldCount - suffix - 1;
        var newStart = prefix;
        var newEnd = newCount - suffix - 1;

        if (!TryGetSpanRange(oldNodes, oldStart, oldEnd, previousText.Length, out var replaceStart, out var replaceEnd))
        {
            return false;
        }

        var segment = SerializeNodes(newNodes, newStart, newEnd);
        var builder = new StringBuilder(previousText.Length - (replaceEnd - replaceStart) + segment.Length);
        if (replaceStart > 0)
        {
            builder.Append(previousText.AsSpan(0, replaceStart));
        }

        builder.Append(segment);

        if (replaceEnd < previousText.Length)
        {
            builder.Append(previousText.AsSpan(replaceEnd));
        }

        updatedText = builder.ToString();
        return true;
    }

    private static bool TryGetSpanRange(
        IReadOnlyList<HtmlNode> nodes,
        int startIndex,
        int endIndex,
        int textLength,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;

        if (nodes.Count == 0 || startIndex < 0 || endIndex < 0 || startIndex >= nodes.Count || endIndex >= nodes.Count)
        {
            return false;
        }

        var first = nodes[startIndex];
        var last = nodes[endIndex];

        if (!first.Span.IsKnown || !last.Span.IsKnown)
        {
            return false;
        }

        start = Math.Clamp(first.Span.Start, 0, textLength);
        end = Math.Clamp(last.Span.End, start, textLength);
        return start <= end;
    }

    private string SerializeNodes(IReadOnlyList<HtmlNode> nodes, int startIndex, int endIndex)
    {
        if (nodes.Count == 0 || startIndex > endIndex)
        {
            return string.Empty;
        }

        var slice = new HtmlDocument(new HtmlNodeId(1), HtmlTextSpan.Unknown);
        for (var i = startIndex; i <= endIndex && i < nodes.Count; i++)
        {
            slice.Children.Add(nodes[i]);
        }

        return _serializer.Serialize(slice);
    }
}
