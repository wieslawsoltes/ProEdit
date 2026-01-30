namespace Vibe.Office.Documents;

public sealed class TableCell
{
    public List<Block> Blocks { get; } = new List<Block>();
    public TableCellParagraphCollection Paragraphs { get; }
    public TableCellProperties Properties { get; } = new TableCellProperties();
    public ContentControlProperties? ContentControl { get; set; }
    public List<MetadataContainer> Metadata { get; } = new List<MetadataContainer>();
    public int ColumnSpan { get; set; } = 1;
    public TableCellVerticalMerge VerticalMerge { get; set; } = TableCellVerticalMerge.None;

    public TableCell()
    {
        Paragraphs = new TableCellParagraphCollection(Blocks);
    }

    public TableCell(IEnumerable<ParagraphBlock> paragraphs)
        : this()
    {
        Paragraphs.AddRange(paragraphs);
    }
}
