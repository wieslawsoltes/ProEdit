namespace Vibe.Office.Editing;

public static class EditorTableCommandIds
{
    public static class Rows
    {
        public const string InsertAbove = "table.rows.insertAbove";
        public const string InsertBelow = "table.rows.insertBelow";
        public const string Delete = "table.rows.delete";
    }

    public static class Columns
    {
        public const string InsertLeft = "table.columns.insertLeft";
        public const string InsertRight = "table.columns.insertRight";
        public const string Delete = "table.columns.delete";
    }

    public static class Delete
    {
        public const string Table = "table.delete.table";
    }

    public static class Merge
    {
        public const string Cells = "table.merge.cells";
        public const string Split = "table.merge.split";
    }

    public static class Layout
    {
        public const string AutoFitContents = "table.layout.autofit.contents";
        public const string AutoFitWindow = "table.layout.autofit.window";
        public const string FixedColumnWidth = "table.layout.autofit.fixed";
        public const string DistributeColumns = "table.layout.distribute.columns";
        public const string DistributeRows = "table.layout.distribute.rows";
        public const string ColumnWidthsSet = "table.layout.columns.set";
        public const string RepeatHeaderRows = "table.layout.repeatHeaderRows";
    }

    public static class Alignment
    {
        public const string AlignTop = "table.align.top";
        public const string AlignMiddle = "table.align.middle";
        public const string AlignBottom = "table.align.bottom";
    }
}
