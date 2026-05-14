using System.Collections.ObjectModel;

namespace ProEdit.WinUICompat.Documents;

public sealed class TableRowGroup : DocumentObject
{
    public Collection<TableRow> Rows { get; } = new();
}
