using Avalonia.Metadata;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a table row.
/// </summary>
public sealed class TableRow : TextElement
{
    /// <summary>
    /// Gets the cells in the row.
    /// </summary>
    [Content]
    public TableCellCollection Cells { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRow"/> class.
    /// </summary>
    public TableRow()
    {
        Cells = new TableCellCollection(this);
    }
}
