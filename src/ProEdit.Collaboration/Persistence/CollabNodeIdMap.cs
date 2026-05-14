using System.Xml.Linq;
using ProEdit.Documents;

namespace ProEdit.Collaboration.Persistence;

internal static class CollabNodeIdMap
{
    public const string MapItemId = "3c5dfd5f-9af2-45a2-9b56-7d143f39a5a0";
    private static readonly XName RootName = "proeditNodeIds";
    private static readonly XName BlocksName = "blocks";
    private static readonly XName InlinesName = "inlines";
    private static readonly XName NodeName = "node";

    public static XDocument Create(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var blocks = EnumerateBlocks(document).Select(block => block.NodeId.ToString("D")).ToArray();
        var inlines = EnumerateInlines(document).Select(inline => inline.NodeId.ToString("D")).ToArray();

        var root = new XElement(RootName,
            new XAttribute("version", 1),
            new XElement(BlocksName, blocks.Select(id => new XElement(NodeName, new XAttribute("id", id)))),
            new XElement(InlinesName, inlines.Select(id => new XElement(NodeName, new XAttribute("id", id)))));

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    public static bool TryApply(Document document, XDocument map)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);

        var root = map.Root;
        if (root is null || root.Name != RootName)
        {
            return false;
        }

        var blocks = root.Element(BlocksName)?.Elements(NodeName)
            .Select(element => ParseGuid(element.Attribute("id")?.Value))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray() ?? Array.Empty<Guid>();

        var inlines = root.Element(InlinesName)?.Elements(NodeName)
            .Select(element => ParseGuid(element.Attribute("id")?.Value))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray() ?? Array.Empty<Guid>();

        var blockTargets = EnumerateBlocks(document).ToArray();
        var inlineTargets = EnumerateInlines(document).ToArray();

        var applied = false;
        if (blocks.Length == blockTargets.Length)
        {
            for (var i = 0; i < blockTargets.Length; i++)
            {
                blockTargets[i].NodeId = blocks[i];
            }

            applied = true;
        }

        if (inlines.Length == inlineTargets.Length)
        {
            for (var i = 0; i < inlineTargets.Length; i++)
            {
                inlineTargets[i].NodeId = inlines[i];
            }

            applied = true;
        }

        return applied;
    }

    public static bool TryAttach(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var map = Create(document);
        document.CustomXmlParts[MapItemId] = map;
        return true;
    }

    public static bool TryExtract(Document document, out XDocument? map)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.CustomXmlParts.TryGetValue(MapItemId, out var existing))
        {
            map = existing;
            return true;
        }

        map = null;
        return false;
    }

    public static void Remove(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.CustomXmlParts.Remove(MapItemId);
    }

    private static Guid? ParseGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value.Trim(), out var id) ? id : null;
    }

    private static IEnumerable<Block> EnumerateBlocks(Document document)
    {
        foreach (var block in EnumerateBlocks(document.Blocks))
        {
            yield return block;
        }
        foreach (var block in EnumerateSectionBlocks(document))
        {
            yield return block;
        }

        foreach (var block in EnumerateNoteBlocks(document.Footnotes))
        {
            yield return block;
        }

        foreach (var block in EnumerateNoteBlocks(document.Endnotes))
        {
            yield return block;
        }

        foreach (var block in EnumerateCommentBlocks(document.Comments))
        {
            yield return block;
        }

        foreach (var block in EnumerateBlocks(document.FootnoteSeparators.SeparatorBlocks))
        {
            yield return block;
        }

        foreach (var block in EnumerateBlocks(document.FootnoteSeparators.ContinuationSeparatorBlocks))
        {
            yield return block;
        }

        foreach (var block in EnumerateBlocks(document.EndnoteSeparators.SeparatorBlocks))
        {
            yield return block;
        }

        foreach (var block in EnumerateBlocks(document.EndnoteSeparators.ContinuationSeparatorBlocks))
        {
            yield return block;
        }
    }

    private static IEnumerable<Block> EnumerateSectionBlocks(Document document)
    {
        if (document.Sections.Count == 0)
        {
            foreach (var block in EnumerateBlocks(document.Header.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(document.Footer.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(document.FirstHeader.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(document.FirstFooter.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(document.EvenHeader.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(document.EvenFooter.Blocks))
            {
                yield return block;
            }

            yield break;
        }

        foreach (var section in document.Sections)
        {
            foreach (var block in EnumerateBlocks(section.Header.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(section.Footer.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(section.FirstHeader.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(section.FirstFooter.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(section.EvenHeader.Blocks))
            {
                yield return block;
            }

            foreach (var block in EnumerateBlocks(section.EvenFooter.Blocks))
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<Block> EnumerateBlocks(IReadOnlyList<Block> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;

            if (block is TableBlock table)
            {
                foreach (var tableBlock in EnumerateTableBlocks(table))
                {
                    yield return tableBlock;
                }
            }
        }
    }

    private static IEnumerable<Block> EnumerateTableBlocks(TableBlock table)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var block in EnumerateBlocks(cell.Blocks))
                {
                    yield return block;
                }
            }
        }
    }

    private static IEnumerable<Block> EnumerateNoteBlocks<T>(Dictionary<int, T> notes) where T : class
    {
        foreach (var pair in notes.OrderBy(pair => pair.Key))
        {
            switch (pair.Value)
            {
                case FootnoteDefinition footnote:
                    foreach (var block in EnumerateBlocks(footnote.Blocks))
                    {
                        yield return block;
                    }

                    break;
                case EndnoteDefinition endnote:
                    foreach (var block in EnumerateBlocks(endnote.Blocks))
                    {
                        yield return block;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Block> EnumerateCommentBlocks(Dictionary<int, CommentDefinition> comments)
    {
        foreach (var pair in comments.OrderBy(pair => pair.Key))
        {
            foreach (var block in EnumerateBlocks(pair.Value.Blocks))
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(Document document)
    {
        foreach (var inline in EnumerateInlines(document.Blocks))
        {
            yield return inline;
        }
        foreach (var inline in EnumerateSectionInlines(document))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateNoteInlines(document.Footnotes))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateNoteInlines(document.Endnotes))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateCommentInlines(document.Comments))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateInlines(document.FootnoteSeparators.SeparatorBlocks))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateInlines(document.FootnoteSeparators.ContinuationSeparatorBlocks))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateInlines(document.EndnoteSeparators.SeparatorBlocks))
        {
            yield return inline;
        }

        foreach (var inline in EnumerateInlines(document.EndnoteSeparators.ContinuationSeparatorBlocks))
        {
            yield return inline;
        }
    }

    private static IEnumerable<Inline> EnumerateSectionInlines(Document document)
    {
        if (document.Sections.Count == 0)
        {
            foreach (var inline in EnumerateInlines(document.Header.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(document.Footer.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(document.FirstHeader.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(document.FirstFooter.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(document.EvenHeader.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(document.EvenFooter.Blocks))
            {
                yield return inline;
            }

            yield break;
        }

        foreach (var section in document.Sections)
        {
            foreach (var inline in EnumerateInlines(section.Header.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(section.Footer.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(section.FirstHeader.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(section.FirstFooter.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(section.EvenHeader.Blocks))
            {
                yield return inline;
            }

            foreach (var inline in EnumerateInlines(section.EvenFooter.Blocks))
            {
                yield return inline;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(IReadOnlyList<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    yield return inline;
                }
            }

            if (block is TableBlock table)
            {
                foreach (var inline in EnumerateInlines(table))
                {
                    yield return inline;
                }
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(TableBlock table)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var inline in EnumerateInlines(cell.Blocks))
                {
                    yield return inline;
                }
            }
        }
    }

    private static IEnumerable<Inline> EnumerateNoteInlines<T>(Dictionary<int, T> notes) where T : class
    {
        foreach (var pair in notes.OrderBy(pair => pair.Key))
        {
            switch (pair.Value)
            {
                case FootnoteDefinition footnote:
                    foreach (var inline in EnumerateInlines(footnote.Blocks))
                    {
                        yield return inline;
                    }

                    break;
                case EndnoteDefinition endnote:
                    foreach (var inline in EnumerateInlines(endnote.Blocks))
                    {
                        yield return inline;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateCommentInlines(Dictionary<int, CommentDefinition> comments)
    {
        foreach (var pair in comments.OrderBy(pair => pair.Key))
        {
            foreach (var inline in EnumerateInlines(pair.Value.Blocks))
            {
                yield return inline;
            }
        }
    }
}
