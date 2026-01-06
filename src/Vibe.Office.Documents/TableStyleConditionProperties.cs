namespace Vibe.Office.Documents;

public sealed class TableStyleConditionProperties
{
    public TableProperties TableProperties { get; } = new TableProperties();
    public TableCellProperties CellProperties { get; } = new TableCellProperties();
}
