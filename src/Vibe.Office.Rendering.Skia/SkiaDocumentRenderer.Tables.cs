using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private static void DrawTableBorders(SKCanvas canvas, TableLayout table, Func<BorderLine, float, SKPaint> paintProvider)
    {
        if (table.Cells.Count == 0)
        {
            return;
        }

        var rowCount = table.Rows;
        var colCount = table.Columns;
        if (rowCount == 0 || colCount == 0)
        {
            return;
        }

        var spacing = table.CellSpacing;
        if (spacing > 0f)
        {
            DrawSeparatedCellBorders(canvas, table, paintProvider);
            return;
        }

        var minCellY = table.Cells.Min(cell => cell.Bounds.Y);
        var minCellX = table.Cells.Min(cell => cell.Bounds.X);
        var topSpacing = minCellY - table.Bounds.Y;
        var leftSpacing = minCellX - table.Bounds.X;

        var rowTops = new float[rowCount];
        var rowBottoms = new float[rowCount];
        var y = table.Bounds.Y + topSpacing;
        for (var row = 0; row < rowCount; row++)
        {
            rowTops[row] = y;
            y += row < table.RowHeights.Count ? table.RowHeights[row] : 0f;
            rowBottoms[row] = y;
            if (row < rowCount - 1)
            {
                y += spacing;
            }
        }

        var colLefts = new float[colCount];
        var colRights = new float[colCount];
        var x = table.Bounds.X + leftSpacing;
        for (var col = 0; col < colCount; col++)
        {
            colLefts[col] = x;
            x += col < table.ColumnWidths.Count ? table.ColumnWidths[col] : 0f;
            colRights[col] = x;
            if (col < colCount - 1)
            {
                x += spacing;
            }
        }

        var minRow = table.Cells.Count == 0 ? 0 : table.Cells.Min(cell => cell.RowIndex);
        var grid = new TableCellLayout[rowCount, colCount];
        foreach (var cell in table.Cells)
        {
            var localRow = cell.RowIndex - minRow;
            if (localRow < 0 || localRow >= rowCount)
            {
                continue;
            }

            var rowSpan = Math.Max(1, cell.RowSpan);
            var colSpan = Math.Max(1, cell.ColumnSpan);
            for (var r = 0; r < rowSpan && localRow + r < rowCount; r++)
            {
                for (var c = 0; c < colSpan && cell.ColumnIndex + c < colCount; c++)
                {
                    grid[localRow + r, cell.ColumnIndex + c] = cell;
                }
            }
        }

        var tableBorders = table.Properties.Borders;

        for (var col = 0; col <= colCount; col++)
        {
            var lineX = spacing > 0f
                ? col switch
                {
                    0 => table.Bounds.X,
                    _ when col == colCount => table.Bounds.Right,
                    _ => (colRights[col - 1] + colLefts[col]) / 2f
                }
                : col == colCount ? colRights[colCount - 1] : colLefts[col];
            for (var row = 0; row < rowCount; row++)
            {
                var leftCell = col == 0 ? null : grid[row, col - 1];
                var rightCell = col == colCount ? null : grid[row, col];
                if (leftCell is not null && rightCell is not null && ReferenceEquals(leftCell, rightCell))
                {
                    continue;
                }

                var border = col switch
                {
                    0 => ResolveBorderLine(rightCell?.Properties.Borders.Left, null, tableBorders.Left),
                    _ when col == colCount => ResolveBorderLine(leftCell?.Properties.Borders.Right, null, tableBorders.Right),
                    _ => ResolveBorderLine(leftCell?.Properties.Borders.Right, rightCell?.Properties.Borders.Left, tableBorders.InsideVertical)
                };

                if (border is null || !border.IsVisible)
                {
                    continue;
                }

                DrawBorderSegment(canvas, border, lineX, rowTops[row], lineX, rowBottoms[row], paintProvider);
            }
        }

        for (var row = 0; row <= rowCount; row++)
        {
            var lineY = spacing > 0f
                ? row switch
                {
                    0 => table.Bounds.Y,
                    _ when row == rowCount => table.Bounds.Bottom,
                    _ => (rowBottoms[row - 1] + rowTops[row]) / 2f
                }
                : row == rowCount ? rowBottoms[rowCount - 1] : rowTops[row];
            for (var col = 0; col < colCount; col++)
            {
                var upperCell = row == 0 ? null : grid[row - 1, col];
                var lowerCell = row == rowCount ? null : grid[row, col];
                if (upperCell is not null && lowerCell is not null && ReferenceEquals(upperCell, lowerCell))
                {
                    continue;
                }

                var border = row switch
                {
                    0 => ResolveBorderLine(lowerCell?.Properties.Borders.Top, null, tableBorders.Top),
                    _ when row == rowCount => ResolveBorderLine(upperCell?.Properties.Borders.Bottom, null, tableBorders.Bottom),
                    _ => ResolveBorderLine(upperCell?.Properties.Borders.Bottom, lowerCell?.Properties.Borders.Top, tableBorders.InsideHorizontal)
                };

                if (border is null || !border.IsVisible)
                {
                    continue;
                }

                DrawBorderSegment(canvas, border, colLefts[col], lineY, colRights[col], lineY, paintProvider);
            }
        }
    }

    private static void DrawSeparatedCellBorders(SKCanvas canvas, TableLayout table, Func<BorderLine, float, SKPaint> paintProvider)
    {
        foreach (var cell in table.Cells)
        {
            if (cell.IsMergeContinuation)
            {
                continue;
            }

            var bounds = cell.Bounds;
            var borders = cell.Properties.Borders;
            if (borders.Top is { IsVisible: true } top)
            {
                DrawBorderSegment(canvas, top, bounds.X, bounds.Y, bounds.Right, bounds.Y, paintProvider);
            }

            if (borders.Bottom is { IsVisible: true } bottom)
            {
                DrawBorderSegment(canvas, bottom, bounds.X, bounds.Bottom, bounds.Right, bounds.Bottom, paintProvider);
            }

            if (borders.Left is { IsVisible: true } left)
            {
                DrawBorderSegment(canvas, left, bounds.X, bounds.Y, bounds.X, bounds.Bottom, paintProvider);
            }

            if (borders.Right is { IsVisible: true } right)
            {
                DrawBorderSegment(canvas, right, bounds.Right, bounds.Y, bounds.Right, bounds.Bottom, paintProvider);
            }
        }
    }

    private static BorderLine? ResolveBorderLine(BorderLine? primary, BorderLine? secondary, BorderLine? fallback = null)
    {
        var primarySet = primary is not null;
        var secondarySet = secondary is not null;
        if (!primarySet && !secondarySet)
        {
            return fallback is { IsVisible: true } ? fallback : null;
        }

        var resolved = ResolveBorderLine(primary, secondary);
        return resolved is { IsVisible: true } ? resolved : null;
    }

    private static BorderLine? ResolveBorderLine(BorderLine? primary, BorderLine? secondary)
    {
        var primaryVisible = primary?.IsVisible == true;
        var secondaryVisible = secondary?.IsVisible == true;
        if (!primaryVisible && !secondaryVisible)
        {
            return primary ?? secondary;
        }

        if (!secondaryVisible)
        {
            return primary;
        }

        if (!primaryVisible)
        {
            return secondary;
        }

        var primaryWeight = GetBorderWeight(primary!);
        var secondaryWeight = GetBorderWeight(secondary!);
        if (primaryWeight > secondaryWeight)
        {
            return primary;
        }

        if (secondaryWeight > primaryWeight)
        {
            return secondary;
        }

        return primary;
    }

    private static void DrawBorderSegment(
        SKCanvas canvas,
        BorderLine border,
        float x1,
        float y1,
        float x2,
        float y2,
        Func<BorderLine, float, SKPaint> paintProvider)
    {
        var thickness = GetBorderThickness(border);
        if (border.Style == DocBorderStyle.Double)
        {
            var lineThickness = MathF.Max(0.5f, thickness / 2f);
            var gap = border.Spacing ?? lineThickness;
            var offset = (lineThickness + gap) / 2f;
            var paint = paintProvider(border, lineThickness);
            if (Math.Abs(x1 - x2) < 0.01f)
            {
                canvas.DrawLine(x1 - offset, y1, x2 - offset, y2, paint);
                canvas.DrawLine(x1 + offset, y1, x2 + offset, y2, paint);
            }
            else
            {
                canvas.DrawLine(x1, y1 - offset, x2, y2 - offset, paint);
                canvas.DrawLine(x1, y1 + offset, x2, y2 + offset, paint);
            }

            return;
        }

        var singlePaint = paintProvider(border, thickness);
        canvas.DrawLine(x1, y1, x2, y2, singlePaint);
    }

    private static float GetBorderThickness(BorderLine border)
    {
        var thickness = MathF.Max(0f, border.Thickness);
        if (border.Style == DocBorderStyle.Hairline)
        {
            return MathF.Max(0.5f, MathF.Min(thickness, 0.5f));
        }

        if (border.Style == DocBorderStyle.Thick)
        {
            return MathF.Max(thickness, 2f);
        }

        return MathF.Max(0.5f, thickness);
    }

    private static float GetBorderWeight(BorderLine border)
    {
        var thickness = GetBorderThickness(border);
        return border.Style == DocBorderStyle.Double ? thickness * 2f : thickness;
    }

    private static SKPathEffect? CreateBorderEffect(DocBorderStyle style, float thickness)
    {
        var unit = MathF.Max(1f, thickness);
        return style switch
        {
            DocBorderStyle.Dotted => SKPathEffect.CreateDash(new[] { unit, unit }, 0),
            DocBorderStyle.Dashed => SKPathEffect.CreateDash(new[] { unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit, unit, unit * 4f, unit * 2f }, 0),
            _ => null
        };
    }

    private readonly record struct BorderPaintKey(DocColor Color, float Thickness, DocBorderStyle Style);
}
