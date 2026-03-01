using System.Collections.ObjectModel;

namespace Vibe.Office.WinUICompat.Documents;

public sealed class TableRow : DocumentObject
{
    public Collection<TableCell> Cells { get; } = new();
}
