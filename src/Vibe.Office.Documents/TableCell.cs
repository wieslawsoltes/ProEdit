namespace Vibe.Office.Documents;

public sealed class TableCell
{
    public List<ParagraphBlock> Paragraphs { get; } = new List<ParagraphBlock>();
    public TableCellProperties Properties { get; } = new TableCellProperties();
    public ContentControlProperties? ContentControl { get; set; }
    public List<MetadataContainer> Metadata { get; } = new List<MetadataContainer>();
    public int ColumnSpan { get; set; } = 1;
    public TableCellVerticalMerge VerticalMerge { get; set; } = TableCellVerticalMerge.None;

    public TableCell()
    {
    }

    public TableCell(IEnumerable<ParagraphBlock> paragraphs)
    {
        Paragraphs.AddRange(paragraphs);
    }
}
