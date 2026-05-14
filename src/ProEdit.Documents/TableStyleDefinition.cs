namespace ProEdit.Documents;

public sealed class TableStyleDefinition
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? BasedOnId { get; set; }
    public string? NextStyleId { get; set; }
    public string? LinkedStyleId { get; set; }
    public int? UiPriority { get; set; }
    public bool? QuickStyle { get; set; }
    public bool? SemiHidden { get; set; }
    public bool? UnhideWhenUsed { get; set; }
    public bool? AutoRedefine { get; set; }
    public bool? Hidden { get; set; }
    public bool? Locked { get; set; }
    public bool? PrimaryStyle { get; set; }
    public bool? CustomStyle { get; set; }
    public TableProperties TableProperties { get; } = new TableProperties();
    public TableCellProperties CellProperties { get; } = new TableCellProperties();
    public Dictionary<TableStyleCondition, TableStyleConditionProperties> Conditions { get; } = new();

    public TableStyleDefinition(string id)
    {
        Id = id;
    }
}
