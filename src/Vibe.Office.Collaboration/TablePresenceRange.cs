namespace Vibe.Office.Collaboration;

/// <summary>
/// Represents a table selection range for collaboration presence.
/// </summary>
public readonly record struct TablePresenceRange(
    Guid TableId,
    int RowStart,
    int RowEnd,
    int ColumnStart,
    int ColumnEnd)
{
    public TablePresenceRange Normalize()
    {
        var rowStart = RowStart;
        var rowEnd = RowEnd;
        if (rowStart > rowEnd)
        {
            (rowStart, rowEnd) = (rowEnd, rowStart);
        }

        var columnStart = ColumnStart;
        var columnEnd = ColumnEnd;
        if (columnStart > columnEnd)
        {
            (columnStart, columnEnd) = (columnEnd, columnStart);
        }

        if (rowStart == RowStart && rowEnd == RowEnd && columnStart == ColumnStart && columnEnd == ColumnEnd)
        {
            return this;
        }

        return new TablePresenceRange(TableId, rowStart, rowEnd, columnStart, columnEnd);
    }
}
