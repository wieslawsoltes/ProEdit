using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;

namespace ProEdit.Rendering.Skia;

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
        var topFallback = table.ContinuesFromPrevious ? null : tableBorders.Top;
        var bottomFallback = table.ContinuesOnNext ? null : tableBorders.Bottom;

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
                    0 => ResolveBorderLine(lowerCell?.Properties.Borders.Top, null, topFallback),
                    _ when row == rowCount => ResolveBorderLine(upperCell?.Properties.Borders.Bottom, null, bottomFallback),
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
        var rowCount = table.Rows;
        foreach (var cell in table.Cells)
        {
            if (cell.IsMergeContinuation)
            {
                continue;
            }

            var bounds = cell.Bounds;
            var borders = cell.Properties.Borders;
            var rowSpan = Math.Max(1, cell.RowSpan);
            var isTopRow = cell.RowIndex <= 0;
            var isBottomRow = rowCount > 0 && cell.RowIndex + rowSpan - 1 >= rowCount - 1;
            var suppressTop = table.ContinuesFromPrevious && isTopRow;
            var suppressBottom = table.ContinuesOnNext && isBottomRow;
            if (!suppressTop && borders.Top is { IsVisible: true } top)
            {
                DrawBorderSegment(canvas, top, bounds.X, bounds.Y, bounds.Right, bounds.Y, paintProvider);
            }

            if (!suppressBottom && borders.Bottom is { IsVisible: true } bottom)
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
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.01f)
        {
            return;
        }

        var dirX = dx / length;
        var dirY = dy / length;
        var trimStart = 0f;
        var trimEnd = 0f;
        if (border.TailArrow.IsVisible)
        {
            ResolveArrowDimensions(border.TailArrow, thickness, out var tailLength, out _);
            trimStart = MathF.Min(tailLength, length);
        }

        if (border.HeadArrow.IsVisible)
        {
            ResolveArrowDimensions(border.HeadArrow, thickness, out var headLength, out _);
            trimEnd = MathF.Min(headLength, MathF.Max(0f, length - trimStart));
        }

        var trimmedLength = length - trimStart - trimEnd;
        if (trimmedLength > 0.01f)
        {
            var startX = x1 + dirX * trimStart;
            var startY = y1 + dirY * trimStart;
            var endX = x2 - dirX * trimEnd;
            var endY = y2 - dirY * trimEnd;
            var normalX = -dirY;
            var normalY = dirX;
            Span<CompoundStroke> strokes = stackalloc CompoundStroke[3];
            var strokeCount = BuildCompoundStrokes(border, thickness, strokes);
            for (var i = 0; i < strokeCount; i++)
            {
                var stroke = strokes[i];
                var offsetX = normalX * stroke.Offset;
                var offsetY = normalY * stroke.Offset;
                var paint = paintProvider(border, stroke.Thickness);
                canvas.DrawLine(startX + offsetX, startY + offsetY, endX + offsetX, endY + offsetY, paint);
            }
        }

        if (border.TailArrow.IsVisible)
        {
            DrawArrowHead(
                canvas,
                new SKPoint(x1, y1),
                new SKPoint(-dirX, -dirY),
                border.TailArrow,
                ToSkColor(border.Color),
                thickness);
        }

        if (border.HeadArrow.IsVisible)
        {
            DrawArrowHead(
                canvas,
                new SKPoint(x2, y2),
                new SKPoint(dirX, dirY),
                border.HeadArrow,
                ToSkColor(border.Color),
                thickness);
        }
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
        Span<CompoundStroke> strokes = stackalloc CompoundStroke[3];
        var strokeCount = BuildCompoundStrokes(border, thickness, strokes);
        var halfWidth = 0f;
        for (var i = 0; i < strokeCount; i++)
        {
            var stroke = strokes[i];
            var extent = MathF.Abs(stroke.Offset) + stroke.Thickness / 2f;
            if (extent > halfWidth)
            {
                halfWidth = extent;
            }
        }

        return halfWidth > 0f ? halfWidth * 2f : thickness;
    }

    private static SKPathEffect? CreateBorderEffect(BorderLine border, float thickness)
    {
        if (border.DashArray is { Length: > 0 })
        {
            var dash = new float[border.DashArray.Length];
            for (var i = 0; i < border.DashArray.Length; i++)
            {
                dash[i] = MathF.Max(0.1f, border.DashArray[i]);
            }

            return SKPathEffect.CreateDash(dash, border.DashPhase);
        }

        var unit = MathF.Max(1f, thickness);
        return border.Style switch
        {
            DocBorderStyle.Dotted => SKPathEffect.CreateDash(new[] { unit, unit }, 0),
            DocBorderStyle.Dashed => SKPathEffect.CreateDash(new[] { unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit, unit, unit * 4f, unit * 2f }, 0),
            _ => null
        };
    }

    private static SKPathEffect? CreateBorderEffect(DocBorderStyle style, float thickness)
    {
        return CreateBorderEffect(new BorderLine { Style = style }, thickness);
    }

    private static int GetDashHash(BorderLine border)
    {
        if (border.DashArray is null || border.DashArray.Length == 0)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var value in border.DashArray)
        {
            hash.Add(value);
        }

        hash.Add(border.DashPhase);
        return hash.ToHashCode();
    }

    private static DocCompoundLine ResolveCompoundLine(BorderLine border)
    {
        if (border.Compound != DocCompoundLine.Single)
        {
            return border.Compound;
        }

        return border.Style switch
        {
            DocBorderStyle.Double => DocCompoundLine.Double,
            DocBorderStyle.Triple => DocCompoundLine.Triple,
            DocBorderStyle.ThickThin => DocCompoundLine.ThickThin,
            DocBorderStyle.ThinThick => DocCompoundLine.ThinThick,
            DocBorderStyle.ThinThickThin => DocCompoundLine.Triple,
            _ => DocCompoundLine.Single
        };
    }

    private static int BuildCompoundStrokes(BorderLine border, float thickness, Span<CompoundStroke> strokes)
    {
        var compound = ResolveCompoundLine(border);
        if (compound == DocCompoundLine.Double)
        {
            var lineThickness = MathF.Max(0.5f, thickness / 2f);
            var gap = ResolveCompoundGap(border, lineThickness);
            var offset = (lineThickness + gap) / 2f;
            strokes[0] = new CompoundStroke(-offset, lineThickness);
            strokes[1] = new CompoundStroke(offset, lineThickness);
            return 2;
        }

        if (compound == DocCompoundLine.ThickThin || compound == DocCompoundLine.ThinThick)
        {
            var thin = MathF.Max(0.5f, thickness / 2f);
            var thick = MathF.Max(thin, thickness);
            var gap = ResolveCompoundGap(border, thin);
            var thickOffset = thick / 2f + gap / 2f;
            var thinOffset = thin / 2f + gap / 2f;
            if (compound == DocCompoundLine.ThickThin)
            {
                strokes[0] = new CompoundStroke(-thinOffset, thin);
                strokes[1] = new CompoundStroke(thickOffset, thick);
            }
            else
            {
                strokes[0] = new CompoundStroke(-thickOffset, thick);
                strokes[1] = new CompoundStroke(thinOffset, thin);
            }

            return 2;
        }

        if (compound == DocCompoundLine.Triple)
        {
            var thin = MathF.Max(0.5f, thickness / 2f);
            var thick = MathF.Max(thin, thickness);
            var gap = ResolveCompoundGap(border, thin);
            var offset = thick / 2f + gap + thin / 2f;
            strokes[0] = new CompoundStroke(-offset, thin);
            strokes[1] = new CompoundStroke(0f, thick);
            strokes[2] = new CompoundStroke(offset, thin);
            return 3;
        }

        strokes[0] = new CompoundStroke(0f, thickness);
        return 1;
    }

    private static float ResolveCompoundGap(BorderLine border, float fallback)
    {
        if (border.CompoundSpacing.HasValue && border.CompoundSpacing.Value > 0f)
        {
            return MathF.Max(0.5f, border.CompoundSpacing.Value);
        }

        if (border.Spacing.HasValue && border.Spacing.Value > 0f)
        {
            return MathF.Max(0.5f, border.Spacing.Value);
        }

        return MathF.Max(0.5f, fallback);
    }

    private static DocLineCap ResolveStrokeCap(BorderLine border)
    {
        if (border.LineCap != DocLineCap.Flat)
        {
            return border.LineCap;
        }

        return border.Style == DocBorderStyle.Dotted ? DocLineCap.Round : DocLineCap.Flat;
    }

    private static SKStrokeCap ToSkStrokeCap(DocLineCap cap)
    {
        return cap switch
        {
            DocLineCap.Round => SKStrokeCap.Round,
            DocLineCap.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        };
    }

    private static SKStrokeJoin ToSkStrokeJoin(DocLineJoin join)
    {
        return join switch
        {
            DocLineJoin.Round => SKStrokeJoin.Round,
            DocLineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };
    }

    private static void ResolveArrowDimensions(
        DocLineArrow arrow,
        float thickness,
        out float length,
        out float width)
    {
        var lengthScale = ResolveArrowScale(arrow.Length);
        var widthScale = ResolveArrowScale(arrow.Width);
        var baseLength = MathF.Max(2f, thickness * 3f);
        var baseWidth = MathF.Max(2f, thickness * 2f);
        length = baseLength * lengthScale;
        width = baseWidth * widthScale;
    }

    private static float ResolveArrowScale(DocLineArrowSize size)
    {
        return size switch
        {
            DocLineArrowSize.Small => 0.75f,
            DocLineArrowSize.Large => 1.5f,
            _ => 1f
        };
    }

    private static void DrawArrowHead(
        SKCanvas canvas,
        SKPoint tip,
        SKPoint direction,
        DocLineArrow arrow,
        SKColor color,
        float strokeWidth)
    {
        if (arrow.Type == DocLineArrowType.None)
        {
            return;
        }

        var dx = direction.X;
        var dy = direction.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        var px = -dy;
        var py = dx;
        ResolveArrowDimensions(arrow, strokeWidth, out var arrowLength, out var arrowWidth);
        var halfWidth = arrowWidth / 2f;
        static SKPoint Transform(SKPoint tip, float dx, float dy, float px, float py, float x, float y)
        {
            return new SKPoint(tip.X + dx * x + px * y, tip.Y + dy * x + py * y);
        }

        if (arrow.Type == DocLineArrowType.Arrow)
        {
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color,
                StrokeWidth = MathF.Max(1f, strokeWidth),
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
            var left = Transform(tip, dx, dy, px, py, -arrowLength, halfWidth);
            var right = Transform(tip, dx, dy, px, py, -arrowLength, -halfWidth);
            canvas.DrawLine(tip, left, strokePaint);
            canvas.DrawLine(tip, right, strokePaint);
            return;
        }

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true
        };
        using var path = new SKPath();
        switch (arrow.Type)
        {
            case DocLineArrowType.Triangle:
            {
                var left = Transform(tip, dx, dy, px, py, -arrowLength, halfWidth);
                var right = Transform(tip, dx, dy, px, py, -arrowLength, -halfWidth);
                path.MoveTo(tip);
                path.LineTo(left);
                path.LineTo(right);
                path.Close();
                break;
            }
            case DocLineArrowType.Stealth:
            {
                var left = Transform(tip, dx, dy, px, py, -arrowLength, halfWidth);
                var right = Transform(tip, dx, dy, px, py, -arrowLength, -halfWidth);
                var notch = Transform(tip, dx, dy, px, py, -arrowLength * 0.6f, 0f);
                path.MoveTo(tip);
                path.LineTo(left);
                path.LineTo(notch);
                path.LineTo(right);
                path.Close();
                break;
            }
            case DocLineArrowType.Diamond:
            {
                var left = Transform(tip, dx, dy, px, py, -arrowLength * 0.5f, halfWidth);
                var back = Transform(tip, dx, dy, px, py, -arrowLength, 0f);
                var right = Transform(tip, dx, dy, px, py, -arrowLength * 0.5f, -halfWidth);
                path.MoveTo(tip);
                path.LineTo(left);
                path.LineTo(back);
                path.LineTo(right);
                path.Close();
                break;
            }
            case DocLineArrowType.Oval:
            {
                var center = Transform(tip, dx, dy, px, py, -arrowLength * 0.5f, 0f);
                var rect = new SKRect(
                    center.X - arrowLength * 0.5f,
                    center.Y - halfWidth,
                    center.X + arrowLength * 0.5f,
                    center.Y + halfWidth);
                path.AddOval(rect);
                break;
            }
        }

        if (!path.IsEmpty)
        {
            canvas.DrawPath(path, fillPaint);
        }
    }

    private readonly struct CompoundStroke
    {
        public float Offset { get; }
        public float Thickness { get; }

        public CompoundStroke(float offset, float thickness)
        {
            Offset = offset;
            Thickness = thickness;
        }
    }

    private readonly record struct BorderPaintKey(
        DocColor Color,
        float Thickness,
        DocBorderStyle Style,
        DocLineCap LineCap,
        DocLineJoin LineJoin,
        float MiterLimit,
        int DashHash);
}
