namespace Vibe.Office.Documents;

public sealed class TableStyleDefinition
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? BasedOnId { get; set; }
    public TableProperties TableProperties { get; } = new TableProperties();
    public TableCellProperties CellProperties { get; } = new TableCellProperties();
    public Dictionary<TableStyleCondition, TableStyleConditionProperties> Conditions { get; } = new();

    public TableStyleDefinition(string id)
    {
        Id = id;
    }
}
