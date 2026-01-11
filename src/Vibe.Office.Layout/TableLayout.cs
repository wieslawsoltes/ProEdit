using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record TableLayout(
    DocRect Bounds,
    int Rows,
    int Columns,
    IReadOnlyList<float> ColumnWidths,
    IReadOnlyList<float> RowHeights,
    IReadOnlyList<TableCellLayout> Cells,
    TableProperties Properties,
    float CellSpacing);
