using System.Xml.Linq;

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
    public DocumentProperties Properties { get; } = new DocumentProperties();
    public DocumentCompatibilitySettings Compatibility { get; } = new DocumentCompatibilitySettings();
    public Dictionary<string, XDocument> CustomXmlParts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DocumentRevisions Revisions { get; } = new DocumentRevisions();
    public DocumentMacros Macros { get; } = new DocumentMacros();
    public Dictionary<int, ListDefinition> ListDefinitions { get; } = new();
    public Dictionary<int, FootnoteDefinition> Footnotes { get; } = new();
    public Dictionary<int, EndnoteDefinition> Endnotes { get; } = new();
    public NoteSeparatorDefinition FootnoteSeparators { get; } = new NoteSeparatorDefinition();
    public NoteSeparatorDefinition EndnoteSeparators { get; } = new NoteSeparatorDefinition();
    public Dictionary<int, CommentDefinition> Comments { get; } = new();
    public bool TrackChangesEnabled { get; set; }
    public string? CitationStyle { get; set; }
    public CitationSourceCatalog CitationSources { get; } = new CitationSourceCatalog();
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
                        count += CountParagraphsInTable(table);

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
                    if (TryFindParagraphInTable(table, i, paragraphIndex, ref count, out var location))
                    {
                        return location;
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

    private static int CountParagraphsInTable(TableBlock table)
    {
        var count = 0;
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                count += CountParagraphsInBlocks(cell.Blocks);
            }
        }

        return count;
    }

    private static int CountParagraphsInBlocks(IReadOnlyList<Block> blocks)
    {
        var count = 0;
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock:
                    count++;
                    break;
                case TableBlock table:
                    count += CountParagraphsInTable(table);
                    break;
            }
        }

        return count;
    }

    private static bool TryFindParagraphInTable(
        TableBlock table,
        int blockIndex,
        int targetIndex,
        ref int count,
        out ParagraphLocation location)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var columnIndex = Math.Max(0, row.Properties.GridBefore ?? 0);
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                if (TryFindParagraphInCell(table, cell, rowIndex, columnIndex, blockIndex, targetIndex, ref count, out location))
                {
                    return true;
                }

                columnIndex += Math.Max(1, cell.ColumnSpan);
            }
        }

        location = default;
        return false;
    }

    private static bool TryFindParagraphInCell(
        TableBlock table,
        TableCell cell,
        int rowIndex,
        int columnIndex,
        int blockIndex,
        int targetIndex,
        ref int count,
        out ParagraphLocation location)
    {
        var paragraphIndexInCell = 0;
        foreach (var block in cell.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    if (count == targetIndex)
                    {
                        location = new ParagraphLocation(paragraph, blockIndex, table, cell, rowIndex, columnIndex, paragraphIndexInCell);
                        return true;
                    }

                    count++;
                    paragraphIndexInCell++;
                    break;
                case TableBlock nestedTable:
                    if (TryFindParagraphInTable(nestedTable, blockIndex, targetIndex, ref count, out location))
                    {
                        return true;
                    }

                    break;
            }
        }

        location = default;
        return false;
    }
}
