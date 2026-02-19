using System.Collections.ObjectModel;

namespace Vibe.Office.WinUICompat.Documents;

public sealed class TableRowGroup : DocumentObject
{
    public Collection<TableRow> Rows { get; } = new();
}
