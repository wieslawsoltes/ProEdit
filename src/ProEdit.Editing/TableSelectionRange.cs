using ProEdit.Documents;

namespace ProEdit.Editing;

public readonly record struct TableSelectionRange(
    TableBlock Table,
    int RowStart,
    int RowEnd,
    int ColumnStart,
    int ColumnEnd)
{
    public TableSelectionRange Normalize()
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

        return new TableSelectionRange(Table, rowStart, rowEnd, columnStart, columnEnd);
    }
}
