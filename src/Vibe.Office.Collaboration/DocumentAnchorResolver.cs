using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration;

/// <summary>
/// Resolves anchors against a document by scanning for matching NodeIds.
/// </summary>
public sealed class DocumentAnchorResolver
{
    /// <summary>
    /// Attempts to resolve a paragraph by its NodeId.
    /// </summary>
    public bool TryResolveParagraph(Document document, Guid paragraphNodeId, out ParagraphBlock paragraph, out int paragraphIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = 0;
        foreach (var block in document.Blocks)
        {
            if (block is ParagraphBlock para)
            {
                if (para.NodeId == paragraphNodeId)
                {
                    paragraph = para;
                    paragraphIndex = index;
                    return true;
                }

                index++;
                continue;
            }

            if (block is TableBlock table)
            {
                if (TryResolveParagraphInTable(table, paragraphNodeId, ref index, out paragraph, out paragraphIndex))
                {
                    return true;
                }
            }
        }

        paragraph = null!;
        paragraphIndex = -1;
        return false;
    }

    /// <summary>
    /// Attempts to resolve an anchor to a paragraph and offset.
    /// </summary>
    public bool TryResolveAnchor(Document document, TextAnchor anchor, out ParagraphBlock paragraph, out int offset)
    {
        if (!TryResolveParagraph(document, anchor.NodeId, out paragraph, out _))
        {
            offset = 0;
            return false;
        }

        offset = Math.Max(0, anchor.Offset);
        return true;
    }

    /// <summary>
    /// Attempts to resolve an inline by its NodeId.
    /// </summary>
    public bool TryResolveInline(Document document, Guid inlineNodeId, out Inline inline)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var block in document.Blocks)
        {
            if (TryResolveInlineInBlock(block, inlineNodeId, out inline))
            {
                return true;
            }
        }

        inline = null!;
        return false;
    }

    private static bool TryResolveInlineInBlock(Block block, Guid inlineNodeId, out Inline inline)
    {
        inline = null!;
        switch (block)
        {
            case ParagraphBlock paragraph:
                foreach (var candidate in paragraph.Inlines)
                {
                    if (candidate.NodeId == inlineNodeId)
                    {
                        inline = candidate;
                        return true;
                    }
                }

                return false;
            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var nested in cell.Blocks)
                        {
                            if (TryResolveInlineInBlock(nested, inlineNodeId, out inline))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryResolveParagraphInTable(TableBlock table, Guid paragraphNodeId, ref int index, out ParagraphBlock paragraph, out int paragraphIndex)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (TryResolveParagraphInBlocks(cell.Blocks, paragraphNodeId, ref index, out paragraph, out paragraphIndex))
                {
                    return true;
                }
            }
        }

        paragraph = null!;
        paragraphIndex = -1;
        return false;
    }

    private static bool TryResolveParagraphInBlocks(IReadOnlyList<Block> blocks, Guid paragraphNodeId, ref int index, out ParagraphBlock paragraph, out int paragraphIndex)
    {
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock para)
            {
                if (para.NodeId == paragraphNodeId)
                {
                    paragraph = para;
                    paragraphIndex = index;
                    return true;
                }

                index++;
                continue;
            }

            if (block is TableBlock table)
            {
                if (TryResolveParagraphInTable(table, paragraphNodeId, ref index, out paragraph, out paragraphIndex))
                {
                    return true;
                }
            }
        }

        paragraph = null!;
        paragraphIndex = -1;
        return false;
    }
}
