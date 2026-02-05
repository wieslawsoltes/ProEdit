using Avalonia.Metadata;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a group of table rows.
/// </summary>
public sealed class TableRowGroup : TextElement
{
    /// <summary>
    /// Gets the rows in the group.
    /// </summary>
    [Content]
    public TableRowCollection Rows { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowGroup"/> class.
    /// </summary>
    public TableRowGroup()
    {
        Rows = new TableRowCollection(this);
    }
}
