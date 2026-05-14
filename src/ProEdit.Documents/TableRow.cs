namespace ProEdit.Documents;

public sealed class TableRow
{
    public List<TableCell> Cells { get; } = new List<TableCell>();
    public TableRowProperties Properties { get; } = new TableRowProperties();
    public ContentControlProperties? ContentControl { get; set; }
    public List<MetadataContainer> Metadata { get; } = new List<MetadataContainer>();

    public TableRow()
    {
    }

    public TableRow(IEnumerable<TableCell> cells)
    {
        Cells.AddRange(cells);
    }
}
