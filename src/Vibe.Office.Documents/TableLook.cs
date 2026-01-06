namespace Vibe.Office.Documents;

public sealed class TableLook
{
    public bool FirstRow { get; set; }
    public bool LastRow { get; set; }
    public bool FirstColumn { get; set; }
    public bool LastColumn { get; set; }
    public bool BandedRows { get; set; } = true;
    public bool BandedColumns { get; set; }

    public TableLook Clone()
    {
        return new TableLook
        {
            FirstRow = FirstRow,
            LastRow = LastRow,
            FirstColumn = FirstColumn,
            LastColumn = LastColumn,
            BandedRows = BandedRows,
            BandedColumns = BandedColumns
        };
    }
}
