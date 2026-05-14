using ProEdit.Documents;

namespace ProEdit.Editing;

public interface ITextContainerNormalizer
{
    void EnsureParagraphInlines(ParagraphBlock paragraph);
    void EnsureBlocksInlines(IReadOnlyList<Block> blocks);
    void EnsureDocumentInlines(Document document);
}

public sealed class TextContainerNormalizer : ITextContainerNormalizer
{
    public void EnsureParagraphInlines(ParagraphBlock paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        DocumentEditHelpers.EnsureParagraphInlines(paragraph);
        EnsureInlineContainers(paragraph);
    }

    public void EnsureBlocksInlines(IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            switch (block)
            {
                case ParagraphBlock paragraph:
                    EnsureParagraphInlines(paragraph);
                    break;
                case TableBlock table:
                    EnsureTableInlines(table);
                    break;
            }
        }
    }

    public void EnsureDocumentInlines(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureBlocksInlines(document.Blocks);
        EnsureHeaderFooter(document.Header);
        EnsureHeaderFooter(document.Footer);
        EnsureHeaderFooter(document.FirstHeader);
        EnsureHeaderFooter(document.FirstFooter);
        EnsureHeaderFooter(document.EvenHeader);
        EnsureHeaderFooter(document.EvenFooter);

        var sections = document.Sections;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            EnsureHeaderFooter(section.Header);
            EnsureHeaderFooter(section.Footer);
            EnsureHeaderFooter(section.FirstHeader);
            EnsureHeaderFooter(section.FirstFooter);
            EnsureHeaderFooter(section.EvenHeader);
            EnsureHeaderFooter(section.EvenFooter);
        }

        foreach (var footnote in document.Footnotes.Values)
        {
            EnsureBlocksInlines(footnote.Blocks);
        }

        foreach (var endnote in document.Endnotes.Values)
        {
            EnsureBlocksInlines(endnote.Blocks);
        }

        EnsureBlocksInlines(document.FootnoteSeparators.SeparatorBlocks);
        EnsureBlocksInlines(document.FootnoteSeparators.ContinuationSeparatorBlocks);
        EnsureBlocksInlines(document.EndnoteSeparators.SeparatorBlocks);
        EnsureBlocksInlines(document.EndnoteSeparators.ContinuationSeparatorBlocks);

        foreach (var comment in document.Comments.Values)
        {
            EnsureBlocksInlines(comment.Blocks);
        }
    }

    private void EnsureHeaderFooter(HeaderFooter headerFooter)
    {
        ArgumentNullException.ThrowIfNull(headerFooter);
        EnsureBlocksInlines(headerFooter.Blocks);
    }

    private void EnsureTableInlines(TableBlock table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var rows = table.Rows;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.Cells;
            for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var cell = cells[cellIndex];
                var paragraphs = cell.Paragraphs;
                for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
                {
                    EnsureParagraphInlines(paragraphs[paragraphIndex]);
                }
            }
        }
    }

    private void EnsureInlineContainers(ParagraphBlock paragraph)
    {
        var inlines = paragraph.Inlines;
        for (var i = 0; i < inlines.Count; i++)
        {
            EnsureInlineContainer(inlines[i]);
        }

        var floatingObjects = paragraph.FloatingObjects;
        for (var i = 0; i < floatingObjects.Count; i++)
        {
            EnsureInlineContainer(floatingObjects[i].Content);
        }
    }

    private void EnsureInlineContainer(Inline inline)
    {
        if (inline is ShapeInline shape && shape.TextBox is { } textBox)
        {
            EnsureBlocksInlines(textBox.Blocks);
        }
    }
}
