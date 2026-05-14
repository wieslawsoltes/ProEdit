namespace ProEdit.Documents;

public readonly struct ParagraphLocation
{
    public ParagraphBlock Paragraph { get; }
    public int BlockIndex { get; }
    public TableBlock? Table { get; }
    public TableCell? Cell { get; }
    public int RowIndex { get; }
    public int ColumnIndex { get; }
    public int ParagraphIndexInCell { get; }

    public bool IsInTable => Table is not null;

    public ParagraphLocation(ParagraphBlock paragraph, int blockIndex)
    {
        Paragraph = paragraph;
        BlockIndex = blockIndex;
        Table = null;
        Cell = null;
        RowIndex = -1;
        ColumnIndex = -1;
        ParagraphIndexInCell = -1;
    }

    public ParagraphLocation(
        ParagraphBlock paragraph,
        int blockIndex,
        TableBlock table,
        TableCell cell,
        int rowIndex,
        int columnIndex,
        int paragraphIndexInCell)
    {
        Paragraph = paragraph;
        BlockIndex = blockIndex;
        Table = table;
        Cell = cell;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        ParagraphIndexInCell = paragraphIndexInCell;
    }

    public bool IsSameContainer(ParagraphLocation other)
    {
        if (!IsInTable && !other.IsInTable)
        {
            return true;
        }

        if (IsInTable && other.IsInTable)
        {
            return ReferenceEquals(Cell, other.Cell);
        }

        return false;
    }
}
