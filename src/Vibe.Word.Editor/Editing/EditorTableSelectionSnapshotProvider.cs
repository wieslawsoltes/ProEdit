using System;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorTableSelectionSnapshotProvider : ITableSelectionSnapshotProvider
{
    private readonly IEditorSession _session;

    public EditorTableSelectionSnapshotProvider(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool TryGetSnapshot(out EditorTableSelectionSnapshot snapshot)
    {
        snapshot = default;
        var document = _session.Document;
        if (document.ParagraphCount == 0)
        {
            return false;
        }

        if (!TryResolveTableSelection(out var table, out var rowStart, out var rowEnd, out var columnStart, out var columnEnd))
        {
            return false;
        }

        var rowIndex = Math.Clamp(rowStart, 0, Math.Max(0, table.Rows.Count - 1));
        var columnCount = GetColumnCount(table);
        if (columnCount <= 0)
        {
            return false;
        }

        var columnIndex = Math.Clamp(columnStart, 0, columnCount - 1);
        var caretLocation = document.GetParagraphLocation(Math.Clamp(_session.Caret.ParagraphIndex, 0, document.ParagraphCount - 1));
        if (caretLocation.IsInTable && ReferenceEquals(caretLocation.Table, table))
        {
            rowIndex = Math.Clamp(caretLocation.RowIndex, 0, Math.Max(0, table.Rows.Count - 1));
            columnIndex = Math.Clamp(caretLocation.ColumnIndex, 0, columnCount - 1);
        }

        TableLayout? layout = null;
        if (TryGetTableLayout(table, out var resolvedLayout))
        {
            layout = resolvedLayout;
        }

        snapshot = new EditorTableSelectionSnapshot(
            table,
            rowIndex,
            columnIndex,
            rowStart,
            rowEnd,
            columnStart,
            columnEnd,
            layout);
        return true;
    }

    private bool TryResolveTableSelection(
        out TableBlock table,
        out int rowStart,
        out int rowEnd,
        out int columnStart,
        out int columnEnd)
    {
        table = null!;
        rowStart = 0;
        rowEnd = 0;
        columnStart = 0;
        columnEnd = 0;

        var ranges = _session.TableSelections;
        if (ranges.Count > 0)
        {
            var range = ranges[0].Normalize();
            table = range.Table;
            rowStart = range.RowStart;
            rowEnd = range.RowEnd;
            columnStart = range.ColumnStart;
            columnEnd = range.ColumnEnd;
            return true;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var startLocation = _session.Document.GetParagraphLocation(startIndex);
        var endLocation = _session.Document.GetParagraphLocation(endIndex);
        if (!startLocation.IsInTable || !endLocation.IsInTable || startLocation.Table is null || endLocation.Table is null)
        {
            return false;
        }

        if (!ReferenceEquals(startLocation.Table, endLocation.Table))
        {
            return false;
        }

        table = startLocation.Table;
        rowStart = Math.Min(startLocation.RowIndex, endLocation.RowIndex);
        rowEnd = Math.Max(startLocation.RowIndex, endLocation.RowIndex);
        columnStart = Math.Min(startLocation.ColumnIndex, endLocation.ColumnIndex);
        columnEnd = Math.Max(startLocation.ColumnIndex, endLocation.ColumnIndex);
        return true;
    }

    private bool TryGetTableLayout(TableBlock table, out TableLayout layout)
    {
        layout = null!;
        if (_session.Layout.Tables.Count == 0)
        {
            return false;
        }

        var tableIndex = -1;
        var seen = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is not TableBlock current)
            {
                continue;
            }

            if (ReferenceEquals(current, table))
            {
                tableIndex = seen;
                break;
            }

            seen++;
        }

        if (tableIndex < 0)
        {
            return false;
        }

        var currentIndex = -1;
        foreach (var candidate in _session.Layout.Tables)
        {
            if (!candidate.ContinuesFromPrevious)
            {
                currentIndex++;
            }

            if (currentIndex == tableIndex)
            {
                layout = candidate;
                return true;
            }

            if (currentIndex > tableIndex)
            {
                break;
            }
        }

        return false;
    }

    private static int GetColumnCount(TableBlock table)
    {
        var maxColumns = 0;
        foreach (var row in table.Rows)
        {
            var columnCount = Math.Max(0, row.Properties.GridBefore ?? 0);
            foreach (var cell in row.Cells)
            {
                columnCount += Math.Max(1, cell.ColumnSpan);
            }

            columnCount += Math.Max(0, row.Properties.GridAfter ?? 0);
            if (columnCount > maxColumns)
            {
                maxColumns = columnCount;
            }
        }

        return maxColumns;
    }
}
