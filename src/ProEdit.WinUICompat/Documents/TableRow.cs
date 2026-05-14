using System.Collections.ObjectModel;

namespace ProEdit.WinUICompat.Documents;

public sealed class TableRow : DocumentObject
{
    public Collection<TableCell> Cells { get; } = new();
}
