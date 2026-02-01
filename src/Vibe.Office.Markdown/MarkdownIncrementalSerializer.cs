using System.Text;
using Vibe.Office.Markdown.Ast;

namespace Vibe.Office.Markdown;

public sealed class MarkdownIncrementalSerializer
{
    private readonly MarkdownSerializer _serializer;

    public MarkdownIncrementalSerializer(MarkdownOptions? options = null)
    {
        _serializer = new MarkdownSerializer(options);
    }

    public string Serialize(MarkdownDocument document)
    {
        return _serializer.Serialize(document);
    }

    public MarkdownTextUpdate SerializeIncremental(
        MarkdownDocument current,
        MarkdownDocument previous,
        string? previousText)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        previousText ??= string.Empty;

        if (!TryComputeIncrementalText(previous, current, previousText, out var updatedText))
        {
            updatedText = _serializer.Serialize(current);
        }

        var edits = MarkdownTextDiff.ComputeSingleEdit(previousText, updatedText);
        return new MarkdownTextUpdate(updatedText, edits);
    }

    private bool TryComputeIncrementalText(
        MarkdownDocument previous,
        MarkdownDocument current,
        string previousText,
        out string updatedText)
    {
        updatedText = string.Empty;

        var oldBlocks = previous.Blocks;
        var newBlocks = current.Blocks;
        var oldCount = oldBlocks.Count;
        var newCount = newBlocks.Count;

        var prefix = 0;
        while (prefix < oldCount && prefix < newCount && oldBlocks[prefix].Id == newBlocks[prefix].Id)
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldCount - prefix
            && suffix < newCount - prefix
            && oldBlocks[oldCount - 1 - suffix].Id == newBlocks[newCount - 1 - suffix].Id)
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

        if (!TryGetSpanRange(oldBlocks, oldStart, oldEnd, previousText.Length, out var replaceStart, out var replaceEnd))
        {
            return false;
        }

        var segment = SerializeBlocks(newBlocks, newStart, newEnd);
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
        IReadOnlyList<MarkdownBlock> blocks,
        int startIndex,
        int endIndex,
        int textLength,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;

        if (blocks.Count == 0 || startIndex < 0 || endIndex < 0 || startIndex >= blocks.Count || endIndex >= blocks.Count)
        {
            return false;
        }

        var first = blocks[startIndex];
        var last = blocks[endIndex];

        if (!first.Span.IsKnown || !last.Span.IsKnown)
        {
            return false;
        }

        start = Math.Clamp(first.Span.Start, 0, textLength);
        end = Math.Clamp(last.Span.End, start, textLength);
        return start <= end;
    }

    private string SerializeBlocks(IReadOnlyList<MarkdownBlock> blocks, int startIndex, int endIndex)
    {
        if (blocks.Count == 0 || startIndex > endIndex)
        {
            return string.Empty;
        }

        var slice = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        for (var i = startIndex; i <= endIndex && i < blocks.Count; i++)
        {
            slice.Blocks.Add(blocks[i]);
        }

        return _serializer.Serialize(slice);
    }
}
