namespace ProEdit.Documents;

public sealed class TableBlock : Block
{
    public List<TableRow> Rows { get; } = new List<TableRow>();
    public TableProperties Properties { get; } = new TableProperties();
    public string? StyleId { get; set; }

    public TableBlock()
    {
    }

    public TableBlock(IEnumerable<TableRow> rows)
    {
        Rows.AddRange(rows);
    }
}
