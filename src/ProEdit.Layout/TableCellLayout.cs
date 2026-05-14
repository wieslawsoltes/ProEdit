using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record TableCellLayout(
    int RowIndex,
    int ColumnIndex,
    int ColumnSpan,
    int RowSpan,
    DocRect Bounds,
    IReadOnlyList<TableCellLine> Lines,
    IReadOnlyList<TableLayout> Tables,
    TableCellProperties Properties,
    DocThickness Padding,
    bool IsMergeContinuation = false,
    int MergeOriginRowIndex = -1,
    int MergeOriginColumnIndex = -1);
