using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record TableLayout(
    DocRect Bounds,
    int Rows,
    int Columns,
    IReadOnlyList<float> ColumnWidths,
    IReadOnlyList<float> RowHeights,
    IReadOnlyList<TableCellLayout> Cells,
    TableProperties Properties,
    float CellSpacing,
    bool ContinuesFromPrevious = false,
    bool ContinuesOnNext = false);
