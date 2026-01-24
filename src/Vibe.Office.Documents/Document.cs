namespace Vibe.Office.Documents;

public sealed class Document
{
    public List<Block> Blocks { get; } = new List<Block>();
    public List<DocumentSection> Sections { get; } = new List<DocumentSection>();
    public HeaderFooter Header { get; } = new HeaderFooter();
    public HeaderFooter Footer { get; } = new HeaderFooter();
    public HeaderFooter FirstHeader { get; } = new HeaderFooter();
    public HeaderFooter FirstFooter { get; } = new HeaderFooter();
    public HeaderFooter EvenHeader { get; } = new HeaderFooter();
    public HeaderFooter EvenFooter { get; } = new HeaderFooter();
    public SectionProperties SectionProperties { get; } = new SectionProperties();
    public bool MirrorMargins { get; set; }
    public bool GutterAtTop { get; set; }
    public bool EvenAndOddHeaders { get; set; }
    public TextStyle DefaultTextStyle { get; } = new TextStyle();
    public ParagraphStyleProperties DefaultParagraphStyleProperties { get; } = new ParagraphStyleProperties();
    public DocumentStyles Styles { get; } = new DocumentStyles();
    public DocumentFonts Fonts { get; } = new DocumentFonts();
    public DocumentThemeColorMap ThemeColors { get; } = new DocumentThemeColorMap();
    public DocumentRevisions Revisions { get; } = new DocumentRevisions();
    public Dictionary<int, ListDefinition> ListDefinitions { get; } = new();
    public Dictionary<int, FootnoteDefinition> Footnotes { get; } = new();
    public Dictionary<int, EndnoteDefinition> Endnotes { get; } = new();
    public Dictionary<int, CommentDefinition> Comments { get; } = new();
    public bool TrackChangesEnabled { get; set; }
    public string? CitationStyle { get; set; }
    public MailMergeData? MailMergeData { get; set; }

    public Document()
    {
        Sections.Add(new DocumentSection(SectionProperties, Header, Footer, FirstHeader, FirstFooter, EvenHeader, EvenFooter));
        Blocks.Add(new ParagraphBlock());
    }

    public int SectionCount => Sections.Count == 0 ? 1 : Sections.Count;

    public DocumentSection GetSection(int sectionIndex)
    {
        if (Sections.Count == 0)
        {
            Sections.Add(new DocumentSection(SectionProperties, Header, Footer, FirstHeader, FirstFooter, EvenHeader, EvenFooter));
        }

        if (sectionIndex < 0 || sectionIndex >= Sections.Count)
        {
            return Sections[0];
        }

        return Sections[sectionIndex];
    }

    public int ParagraphCount
    {
        get
        {
            var count = 0;
            foreach (var block in Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock:
                        count++;
                        break;
                    case TableBlock table:
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                count += cell.Paragraphs.Count;
                            }
                        }

                        break;
                }
            }

            return count;
        }
    }

    public ParagraphBlock GetParagraph(int paragraphIndex) => GetParagraph(paragraphIndex, out _);

    public ParagraphBlock GetParagraph(int paragraphIndex, out int blockIndex)
    {
        var location = GetParagraphLocation(paragraphIndex);
        blockIndex = location.BlockIndex;
        return location.Paragraph;
    }

    public ParagraphLocation GetParagraphLocation(int paragraphIndex)
    {
        if (paragraphIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        }

        var count = 0;
        for (var i = 0; i < Blocks.Count; i++)
        {
            switch (Blocks[i])
            {
                case ParagraphBlock paragraph:
                {
                    if (count == paragraphIndex)
                    {
                        return new ParagraphLocation(paragraph, i);
                    }

                    count++;
                    break;
                }
                case TableBlock table:
                {
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        var row = table.Rows[rowIndex];
                        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
                        {
                            var cell = row.Cells[columnIndex];
                            for (var paragraphIndexInCell = 0; paragraphIndexInCell < cell.Paragraphs.Count; paragraphIndexInCell++)
                            {
                                var paragraph = cell.Paragraphs[paragraphIndexInCell];
                                if (count == paragraphIndex)
                                {
                                    return new ParagraphLocation(paragraph, i, table, cell, rowIndex, columnIndex, paragraphIndexInCell);
                                }

                                count++;
                            }
                        }
                    }

                    break;
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
    }

    public void InsertParagraph(int paragraphIndex, ParagraphBlock paragraph)
    {
        if (paragraphIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        }

        if (paragraphIndex >= ParagraphCount)
        {
            Blocks.Add(paragraph);
            return;
        }

        var location = GetParagraphLocation(paragraphIndex);
        if (location.IsInTable)
        {
            var cell = location.Cell ?? throw new InvalidOperationException("Table cell not found.");
            cell.Paragraphs.Insert(location.ParagraphIndexInCell, paragraph);
            return;
        }

        Blocks.Insert(location.BlockIndex, paragraph);
    }

    public void RemoveParagraphAt(int paragraphIndex)
    {
        var location = GetParagraphLocation(paragraphIndex);
        RemoveParagraphAt(location);
    }

    public void RemoveParagraphAt(ParagraphLocation location)
    {
        if (location.IsInTable)
        {
            var cell = location.Cell ?? throw new InvalidOperationException("Table cell not found.");
            if (location.ParagraphIndexInCell >= 0 && location.ParagraphIndexInCell < cell.Paragraphs.Count)
            {
                cell.Paragraphs.RemoveAt(location.ParagraphIndexInCell);
            }

            return;
        }

        if (location.BlockIndex >= 0 && location.BlockIndex < Blocks.Count)
        {
            Blocks.RemoveAt(location.BlockIndex);
        }
    }

    public void InsertParagraphAfter(ParagraphLocation location, ParagraphBlock paragraph)
    {
        if (location.IsInTable)
        {
            var cell = location.Cell ?? throw new InvalidOperationException("Table cell not found.");
            var insertIndex = Math.Clamp(location.ParagraphIndexInCell + 1, 0, cell.Paragraphs.Count);
            cell.Paragraphs.Insert(insertIndex, paragraph);
            return;
        }

        var blockInsertIndex = Math.Clamp(location.BlockIndex + 1, 0, Blocks.Count);
        Blocks.Insert(blockInsertIndex, paragraph);
    }
}
