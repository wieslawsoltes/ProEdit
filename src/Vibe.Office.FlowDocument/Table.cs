using Avalonia;
using Avalonia.Metadata;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a table block.
/// </summary>
public sealed class Table : Block
{
    /// <summary>
    /// Identifies the <see cref="CellSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> CellSpacingProperty =
        AvaloniaProperty.Register<Table, double?>(nameof(CellSpacing));

    private TableColumn? _current;

    /// <summary>
    /// Gets the table columns.
    /// </summary>
    public TableColumnCollection Columns { get; }

    /// <summary>
    /// Gets the table row groups.
    /// </summary>
    [Content]
    public TableRowGroupCollection RowGroups { get; }

    /// <summary>
    /// Gets or sets cell spacing.
    /// </summary>
    public double? CellSpacing
    {
        get => GetValue(CellSpacingProperty);
        set => SetValue(CellSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the current column metadata.
    /// </summary>
    public TableColumn? Current
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value))
            {
                return;
            }

            _current = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class.
    /// </summary>
    public Table()
    {
        Columns = new TableColumnCollection(this);
        RowGroups = new TableRowGroupCollection(this);
    }
}
