using System.Collections.ObjectModel;

namespace ProEdit.WinUICompat.Documents;

public sealed class Table : Block
{
    public Collection<TableColumn> Columns { get; } = new();

    public Collection<TableRowGroup> RowGroups { get; } = new();

    public double? CellSpacing { get; set; }
}
