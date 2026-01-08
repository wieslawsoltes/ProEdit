using System.Buffers;
using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private void DrawChart(SKCanvas canvas, LayoutChart chartLayout, float lineX, float baseline, RenderOptions options)
    {
        var chart = chartLayout.Chart;
        var width = chartLayout.Width;
        var height = chartLayout.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var x = lineX + chartLayout.X;
        var y = baseline - height;
        var rect = new SKRect(x, y, x + width, y + height);
        DrawChartContent(canvas, rect, chart, options);
    }

    private static void DrawChartContent(SKCanvas canvas, SKRect rect, ChartInline chart, RenderOptions options)
    {
        var model = chart.Model;
        var chartAreaStyle = model?.ChartAreaStyle;
        var useFallbackArea = chartAreaStyle is null;
        if (TryResolveFillColor(chartAreaStyle, options.PlaceholderFillColor, useFallbackArea, out var areaFill))
        {
            using var fillPaint = CreateFillPaint(areaFill, chartAreaStyle?.Effects?.Shadow);
            canvas.DrawRect(rect, fillPaint);
        }

        if (TryResolveLineStyle(chartAreaStyle, options.PlaceholderStrokeColor, 1f, useFallbackArea, out var areaLine, out var areaWidth, out var areaDash))
        {
            using var borderPaint = CreateLinePaint(areaLine, areaWidth, areaDash);
            canvas.DrawRect(rect, borderPaint);
        }

        if (model is null || model.Series.Count == 0)
        {
            DrawChartPlaceholder(canvas, rect, options, "Chart");
            return;
        }

        var padding = MathF.Max(6f, rect.Width * 0.05f);
        var titleHeight = 0f;
        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            using var titlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(options.PlaceholderTextColor),
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                TextSize = MathF.Max(10f, rect.Height * 0.08f)
            };
            var titleY = rect.Top + titlePaint.TextSize + 2f;
            canvas.DrawText(model.Title, rect.MidX, titleY, titlePaint);
            titleHeight = titlePaint.TextSize + padding * 0.5f;
        }

        var plotRect = new SKRect(
            rect.Left + padding,
            rect.Top + padding + titleHeight,
            rect.Right - padding,
            rect.Bottom - padding);

        if (plotRect.Width <= 4f || plotRect.Height <= 4f)
        {
            DrawChartPlaceholder(canvas, rect, options, "Chart");
            return;
        }

        var plotAreaStyle = model.PlotAreaStyle;
        if (TryResolveFillColor(plotAreaStyle, options.PlaceholderFillColor, false, out var plotFill))
        {
            using var plotFillPaint = CreateFillPaint(plotFill, plotAreaStyle?.Effects?.Shadow);
            canvas.DrawRect(plotRect, plotFillPaint);
        }

        if (TryResolveLineStyle(plotAreaStyle, options.PlaceholderStrokeColor, 1f, false, out var plotLine, out var plotWidth, out var plotDash))
        {
            using var plotBorderPaint = CreateLinePaint(plotLine, plotWidth, plotDash);
            canvas.DrawRect(plotRect, plotBorderPaint);
        }

        switch (model.Type)
        {
            case ChartType.Pie:
                DrawPieChart(canvas, plotRect, model);
                break;
            case ChartType.Doughnut:
                DrawDoughnutChart(canvas, plotRect, model);
                break;
            case ChartType.Line:
                DrawLineChart(canvas, plotRect, model);
                break;
            case ChartType.Scatter:
                DrawScatterChart(canvas, plotRect, model);
                break;
            case ChartType.Area:
                DrawAreaChart(canvas, plotRect, model);
                break;
            case ChartType.Radar:
                DrawRadarChart(canvas, plotRect, model);
                break;
            case ChartType.Bubble:
                DrawBubbleChart(canvas, plotRect, model);
                break;
            default:
                DrawBarChart(canvas, plotRect, model);
                break;
        }
    }

    private static void DrawChartPlaceholder(SKCanvas canvas, SKRect rect, RenderOptions options, string label)
    {
        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PlaceholderTextColor),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            TextSize = MathF.Max(9f, MathF.Min(14f, rect.Height / 4f))
        };

        var textY = rect.MidY + textPaint.TextSize * 0.35f;
        canvas.DrawText(label, rect.MidX, textY, textPaint);
    }

    private static void DrawBarChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        var categoryCount = 0;
        for (var i = 0; i < seriesCount; i++)
        {
            categoryCount = Math.Max(categoryCount, model.Series[i].Points.Count);
        }
        if (categoryCount == 0)
        {
            return;
        }

        var stacking = model.Stacking != ChartStacking.None;
        var totals = stacking ? ArrayPool<double>.Shared.Rent(categoryCount) : Array.Empty<double>();
        if (stacking)
        {
            totals.AsSpan(0, categoryCount).Clear();
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
                {
                    var value = Math.Max(0d, series.Points[pointIndex].Value);
                    totals[pointIndex] += value;
                }
            }
        }

        var maxValue = 0d;
        if (stacking)
        {
            if (model.Stacking == ChartStacking.Percent)
            {
                maxValue = 1d;
            }
            else
            {
                for (var i = 0; i < categoryCount; i++)
                {
                    maxValue = Math.Max(maxValue, totals[i]);
                }
            }
        }
        else
        {
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
                {
                    maxValue = Math.Max(maxValue, series.Points[pointIndex].Value);
                }
            }
        }

        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        var slotCount = stacking ? 1 : Math.Max(1, seriesCount);
        if (model.BarDirection == ChartBarDirection.Column)
        {
            var groupWidth = plotRect.Width / categoryCount;
            var barWidth = groupWidth / slotCount;
            var gap = barWidth * 0.15f;

            for (var categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                var groupStart = plotRect.Left + categoryIndex * groupWidth;
                var baseY = plotRect.Bottom;
                var total = stacking && model.Stacking == ChartStacking.Percent ? totals[categoryIndex] : maxValue;
                for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    var series = model.Series[seriesIndex];
                    var point = categoryIndex < series.Points.Count ? series.Points[categoryIndex] : null;
                    var rawValue = point?.Value ?? 0d;
                    if (rawValue <= 0d)
                    {
                        continue;
                    }

                    var normalized = stacking
                        ? model.Stacking == ChartStacking.Percent && total > 0d ? rawValue / total : rawValue
                        : rawValue;
                    var height = (float)(normalized / maxValue) * plotRect.Height;
                    var barLeft = groupStart + (stacking ? 0 : seriesIndex * barWidth) + gap;
                    var barRight = barLeft + barWidth - gap * 2f;
                    var barTop = baseY - height;

                    var fallback = ResolveChartPaletteColor(seriesIndex);
                    if (TryResolveFillColor(point?.Style, series.Style, fallback, out var fill))
                    {
                        using var barPaint = CreateFillPaint(fill, series.Style?.Effects?.Shadow);
                        canvas.DrawRect(new SKRect(barLeft, barTop, barRight, baseY), barPaint);
                    }

                    if (TryResolveLineStyle(point?.Style, series.Style, fallback, 1f, false, out var lineColor, out var lineWidth, out var lineStyle))
                    {
                        using var barLinePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                        canvas.DrawRect(new SKRect(barLeft, barTop, barRight, baseY), barLinePaint);
                    }

                    if (stacking)
                    {
                        baseY = barTop;
                    }
                }
            }
        }
        else
        {
            var groupHeight = plotRect.Height / categoryCount;
            var barHeight = groupHeight / slotCount;
            var gap = barHeight * 0.15f;

            for (var categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                var groupTop = plotRect.Top + categoryIndex * groupHeight;
                var baseX = plotRect.Left;
                var total = stacking && model.Stacking == ChartStacking.Percent ? totals[categoryIndex] : maxValue;
                for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    var series = model.Series[seriesIndex];
                    var point = categoryIndex < series.Points.Count ? series.Points[categoryIndex] : null;
                    var rawValue = point?.Value ?? 0d;
                    if (rawValue <= 0d)
                    {
                        continue;
                    }

                    var normalized = stacking
                        ? model.Stacking == ChartStacking.Percent && total > 0d ? rawValue / total : rawValue
                        : rawValue;
                    var width = (float)(normalized / maxValue) * plotRect.Width;
                    var barTop = groupTop + (stacking ? 0 : seriesIndex * barHeight) + gap;
                    var barBottom = barTop + barHeight - gap * 2f;
                    var barRight = baseX + width;

                    var fallback = ResolveChartPaletteColor(seriesIndex);
                    if (TryResolveFillColor(point?.Style, series.Style, fallback, out var fill))
                    {
                        using var barPaint = CreateFillPaint(fill, series.Style?.Effects?.Shadow);
                        canvas.DrawRect(new SKRect(baseX, barTop, barRight, barBottom), barPaint);
                    }

                    if (TryResolveLineStyle(point?.Style, series.Style, fallback, 1f, false, out var lineColor, out var lineWidth, out var lineStyle))
                    {
                        using var barLinePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                        canvas.DrawRect(new SKRect(baseX, barTop, barRight, barBottom), barLinePaint);
                    }

                    if (stacking)
                    {
                        baseX = barRight;
                    }
                }
            }
        }

        if (totals.Length > 0)
        {
            ArrayPool<double>.Shared.Return(totals);
        }
    }

    private static void DrawLineChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        var pointCount = 0;
        for (var i = 0; i < seriesCount; i++)
        {
            pointCount = Math.Max(pointCount, model.Series[i].Points.Count);
        }

        if (pointCount == 0)
        {
            return;
        }

        var totals = model.Stacking != ChartStacking.None ? ArrayPool<double>.Shared.Rent(pointCount) : Array.Empty<double>();
        var cumulative = model.Stacking != ChartStacking.None ? ArrayPool<double>.Shared.Rent(pointCount) : Array.Empty<double>();
        if (totals.Length > 0)
        {
            totals.AsSpan(0, pointCount).Clear();
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var i = 0; i < series.Points.Count; i++)
                {
                    totals[i] += Math.Max(0d, series.Points[i].Value);
                }
            }
        }

        if (cumulative.Length > 0)
        {
            cumulative.AsSpan(0, pointCount).Clear();
        }

        var maxValue = 0d;
        if (model.Stacking == ChartStacking.None)
        {
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var i = 0; i < series.Points.Count; i++)
                {
                    maxValue = Math.Max(maxValue, series.Points[i].Value);
                }
            }
        }
        else if (model.Stacking == ChartStacking.Stacked)
        {
            for (var i = 0; i < pointCount; i++)
            {
                maxValue = Math.Max(maxValue, totals[i]);
            }
        }
        else
        {
            maxValue = 1d;
        }

        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            if (series.Points.Count == 0)
            {
                continue;
            }

            var fallback = ResolveChartPaletteColor(seriesIndex);
            if (!TryResolveLineStyle(series.Style, fallback, 2f, true, out var lineColor, out var lineWidth, out var lineStyle))
            {
                continue;
            }

            using var paint = CreateLinePaint(lineColor, lineWidth, lineStyle);
            using var path = new SKPath();
            for (var i = 0; i < series.Points.Count; i++)
            {
                var value = Math.Max(0d, series.Points[i].Value);
                var stackedValue = value;
                if (model.Stacking != ChartStacking.None)
                {
                    if (model.Stacking == ChartStacking.Percent)
                    {
                        var total = totals[i];
                        stackedValue = total > 0d ? value / total : 0d;
                    }

                    cumulative[i] += stackedValue;
                    stackedValue = cumulative[i];
                }

                var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, series.Points.Count - 1));
                var y = plotRect.Bottom - (float)(stackedValue / maxValue) * plotRect.Height;
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            canvas.DrawPath(path, paint);
        }

        if (totals.Length > 0)
        {
            ArrayPool<double>.Shared.Return(totals);
        }

        if (cumulative.Length > 0)
        {
            ArrayPool<double>.Shared.Return(cumulative);
        }
    }

    private static void DrawAreaChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        var pointCount = 0;
        for (var i = 0; i < seriesCount; i++)
        {
            pointCount = Math.Max(pointCount, model.Series[i].Points.Count);
        }

        if (pointCount == 0)
        {
            return;
        }

        var totals = model.Stacking != ChartStacking.None ? ArrayPool<double>.Shared.Rent(pointCount) : Array.Empty<double>();
        var baseline = model.Stacking != ChartStacking.None ? ArrayPool<double>.Shared.Rent(pointCount) : Array.Empty<double>();
        if (totals.Length > 0)
        {
            totals.AsSpan(0, pointCount).Clear();
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var i = 0; i < series.Points.Count; i++)
                {
                    totals[i] += Math.Max(0d, series.Points[i].Value);
                }
            }
        }

        if (baseline.Length > 0)
        {
            baseline.AsSpan(0, pointCount).Clear();
        }

        var maxValue = 0d;
        if (model.Stacking == ChartStacking.None)
        {
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var i = 0; i < series.Points.Count; i++)
                {
                    maxValue = Math.Max(maxValue, series.Points[i].Value);
                }
            }
        }
        else if (model.Stacking == ChartStacking.Stacked)
        {
            for (var i = 0; i < pointCount; i++)
            {
                maxValue = Math.Max(maxValue, totals[i]);
            }
        }
        else
        {
            maxValue = 1d;
        }

        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        var topValues = ArrayPool<double>.Shared.Rent(pointCount);
        try
        {
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                if (series.Points.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < pointCount; i++)
                {
                    var value = i < series.Points.Count ? Math.Max(0d, series.Points[i].Value) : 0d;
                    var stackedValue = value;
                    if (model.Stacking != ChartStacking.None)
                    {
                        if (model.Stacking == ChartStacking.Percent)
                        {
                            var total = totals[i];
                            stackedValue = total > 0d ? value / total : 0d;
                        }

                        topValues[i] = baseline[i] + stackedValue;
                    }
                    else
                    {
                        topValues[i] = stackedValue;
                    }
                }

                var fallback = ResolveChartPaletteColor(seriesIndex);
                if (!TryResolveFillColor(series.Style, fallback, true, out var fillColor))
                {
                    continue;
                }

                var fillWithAlpha = ApplyAlpha(fillColor, 160);
                using var paint = CreateFillPaint(fillWithAlpha, series.Style?.Effects?.Shadow);
                using var path = new SKPath();

                var firstX = plotRect.Left;
                var firstY = plotRect.Bottom - (float)(topValues[0] / maxValue) * plotRect.Height;
                path.MoveTo(firstX, firstY);

                for (var i = 1; i < pointCount; i++)
                {
                    var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, pointCount - 1));
                    var y = plotRect.Bottom - (float)(topValues[i] / maxValue) * plotRect.Height;
                    path.LineTo(x, y);
                }

                if (model.Stacking == ChartStacking.None)
                {
                    path.LineTo(plotRect.Right, plotRect.Bottom);
                    path.LineTo(plotRect.Left, plotRect.Bottom);
                }
                else
                {
                    for (var i = pointCount - 1; i >= 0; i--)
                    {
                        var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, pointCount - 1));
                        var y = plotRect.Bottom - (float)(baseline[i] / maxValue) * plotRect.Height;
                        path.LineTo(x, y);
                    }
                }

                path.Close();
                canvas.DrawPath(path, paint);

                if (model.Stacking != ChartStacking.None)
                {
                    for (var i = 0; i < pointCount; i++)
                    {
                        baseline[i] = topValues[i];
                    }
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(topValues);
        }

        if (totals.Length > 0)
        {
            ArrayPool<double>.Shared.Return(totals);
        }

        if (baseline.Length > 0)
        {
            ArrayPool<double>.Shared.Return(baseline);
        }
    }

    private static void DrawScatterChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        if (seriesCount == 0)
        {
            return;
        }

        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                minX = Math.Min(minX, xValue);
                maxX = Math.Max(maxX, xValue);
                minY = Math.Min(minY, yValue);
                maxY = Math.Max(maxY, yValue);
            }
        }

        if (minX == double.MaxValue || minY == double.MaxValue)
        {
            return;
        }

        if (Math.Abs(maxX - minX) < double.Epsilon)
        {
            maxX = minX + 1d;
        }

        if (Math.Abs(maxY - minY) < double.Epsilon)
        {
            maxY = minY + 1d;
        }

        var radius = MathF.Max(2f, MathF.Min(plotRect.Width, plotRect.Height) * 0.02f);
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            var fallback = ResolveChartPaletteColor(seriesIndex);
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                var x = plotRect.Left + (float)((xValue - minX) / (maxX - minX)) * plotRect.Width;
                var y = plotRect.Bottom - (float)((yValue - minY) / (maxY - minY)) * plotRect.Height;

                if (!TryResolveFillColor(point.Style, series.Style, fallback, out var fill))
                {
                    continue;
                }

                using var paint = CreateFillPaint(fill, series.Style?.Effects?.Shadow);
                canvas.DrawCircle(x, y, radius, paint);
            }
        }
    }

    private static void DrawPieChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var series = model.Series.FirstOrDefault();
        if (series is null || series.Points.Count == 0)
        {
            return;
        }

        var total = series.Points.Sum(point => Math.Max(0, point.Value));
        if (total <= 0)
        {
            return;
        }

        var centerX = plotRect.MidX;
        var centerY = plotRect.MidY;
        var radius = MathF.Min(plotRect.Width, plotRect.Height) / 2f;
        var startAngle = -90f;

        for (var i = 0; i < series.Points.Count; i++)
        {
            var value = Math.Max(0, series.Points[i].Value);
            var sweep = (float)(value / total) * 360f;
            var point = series.Points[i];
            var fallback = ResolveChartPaletteColor(i);
            if (TryResolveFillColor(point.Style, series.Style, fallback, out var fillColor))
            {
                using var paint = CreateFillPaint(fillColor, series.Style?.Effects?.Shadow);
                using var path = new SKPath();
                path.MoveTo(centerX, centerY);
                path.ArcTo(new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius), startAngle, sweep, false);
                path.Close();
                canvas.DrawPath(path, paint);
            }

            if (TryResolveLineStyle(point.Style, series.Style, fallback, 1f, false, out var lineColor, out var lineWidth, out var lineStyle))
            {
                using var linePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                using var path = new SKPath();
                path.MoveTo(centerX, centerY);
                path.ArcTo(new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius), startAngle, sweep, false);
                path.Close();
                canvas.DrawPath(path, linePaint);
            }

            startAngle += sweep;
        }
    }

    private static void DrawDoughnutChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var series = model.Series.FirstOrDefault();
        if (series is null || series.Points.Count == 0)
        {
            return;
        }

        var total = series.Points.Sum(point => Math.Max(0, point.Value));
        if (total <= 0)
        {
            return;
        }

        var centerX = plotRect.MidX;
        var centerY = plotRect.MidY;
        var radius = MathF.Min(plotRect.Width, plotRect.Height) / 2f;
        var innerRadius = MathF.Max(0f, radius * model.DoughnutHoleSize);
        var startAngle = -90f;

        for (var i = 0; i < series.Points.Count; i++)
        {
            var value = Math.Max(0, series.Points[i].Value);
            var sweep = (float)(value / total) * 360f;
            var point = series.Points[i];
            var fallback = ResolveChartPaletteColor(i);

            if (TryResolveFillColor(point.Style, series.Style, fallback, out var fillColor))
            {
                using var paint = CreateFillPaint(fillColor, series.Style?.Effects?.Shadow);
                using var path = CreateDoughnutSlicePath(centerX, centerY, radius, innerRadius, startAngle, sweep);
                canvas.DrawPath(path, paint);
            }

            if (TryResolveLineStyle(point.Style, series.Style, fallback, 1f, false, out var lineColor, out var lineWidth, out var lineStyle))
            {
                using var linePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                using var path = CreateDoughnutSlicePath(centerX, centerY, radius, innerRadius, startAngle, sweep);
                canvas.DrawPath(path, linePaint);
            }

            startAngle += sweep;
        }
    }

    private static void DrawRadarChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        var pointCount = 0;
        for (var i = 0; i < seriesCount; i++)
        {
            pointCount = Math.Max(pointCount, model.Series[i].Points.Count);
        }

        if (pointCount == 0)
        {
            return;
        }

        var maxValue = 0d;
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            for (var i = 0; i < series.Points.Count; i++)
            {
                maxValue = Math.Max(maxValue, series.Points[i].Value);
            }
        }

        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        var centerX = plotRect.MidX;
        var centerY = plotRect.MidY;
        var radius = MathF.Min(plotRect.Width, plotRect.Height) / 2f;
        var angleStep = (MathF.PI * 2f) / pointCount;

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            if (series.Points.Count == 0)
            {
                continue;
            }

            var fallback = ResolveChartPaletteColor(seriesIndex);
            var hasLine = TryResolveLineStyle(series.Style, fallback, 1.5f, true, out var lineColor, out var lineWidth, out var lineStyle);
            var fillColor = default(DocColor);
            var hasFill = model.RadarStyle == ChartRadarStyle.Filled
                && TryResolveFillColor(series.Style, fallback, true, out fillColor);

            using var path = new SKPath();
            for (var i = 0; i < pointCount; i++)
            {
                var value = i < series.Points.Count ? Math.Max(0d, series.Points[i].Value) : 0d;
                var normalized = (float)(value / maxValue);
                var angle = -MathF.PI / 2f + i * angleStep;
                var x = centerX + MathF.Cos(angle) * radius * normalized;
                var y = centerY + MathF.Sin(angle) * radius * normalized;
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            path.Close();

            if (hasFill)
            {
                using var fillPaint = CreateFillPaint(ApplyAlpha(fillColor, 160), series.Style?.Effects?.Shadow);
                canvas.DrawPath(path, fillPaint);
            }

            if (hasLine)
            {
                using var linePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                canvas.DrawPath(path, linePaint);
            }

            if (model.RadarStyle == ChartRadarStyle.Marker)
            {
                var markerRadius = MathF.Max(2f, radius * 0.03f);
                for (var i = 0; i < pointCount; i++)
                {
                    var value = i < series.Points.Count ? Math.Max(0d, series.Points[i].Value) : 0d;
                    var normalized = (float)(value / maxValue);
                    var angle = -MathF.PI / 2f + i * angleStep;
                    var x = centerX + MathF.Cos(angle) * radius * normalized;
                    var y = centerY + MathF.Sin(angle) * radius * normalized;
                    using var markerPaint = CreateFillPaint(lineColor, series.Style?.Effects?.Shadow);
                    canvas.DrawCircle(x, y, markerRadius, markerPaint);
                }
            }
        }
    }

    private static void DrawBubbleChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var seriesCount = model.Series.Count;
        if (seriesCount == 0)
        {
            return;
        }

        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        var minSize = double.MaxValue;
        var maxSize = double.MinValue;
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                var sizeValue = point.Size ?? 1d;
                minX = Math.Min(minX, xValue);
                maxX = Math.Max(maxX, xValue);
                minY = Math.Min(minY, yValue);
                maxY = Math.Max(maxY, yValue);
                minSize = Math.Min(minSize, sizeValue);
                maxSize = Math.Max(maxSize, sizeValue);
            }
        }

        if (minX == double.MaxValue || minY == double.MaxValue)
        {
            return;
        }

        if (Math.Abs(maxX - minX) < double.Epsilon)
        {
            maxX = minX + 1d;
        }

        if (Math.Abs(maxY - minY) < double.Epsilon)
        {
            maxY = minY + 1d;
        }

        if (Math.Abs(maxSize - minSize) < double.Epsilon)
        {
            maxSize = minSize + 1d;
        }

        var maxRadius = MathF.Min(plotRect.Width, plotRect.Height) * 0.08f;
        var minRadius = MathF.Max(2f, maxRadius * 0.4f);

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            var fallback = ResolveChartPaletteColor(seriesIndex);
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                var sizeValue = point.Size ?? 1d;
                var x = plotRect.Left + (float)((xValue - minX) / (maxX - minX)) * plotRect.Width;
                var y = plotRect.Bottom - (float)((yValue - minY) / (maxY - minY)) * plotRect.Height;
                var sizeRatio = (float)((sizeValue - minSize) / (maxSize - minSize));
                var radius = minRadius + sizeRatio * (maxRadius - minRadius);

                if (!TryResolveFillColor(point.Style, series.Style, fallback, out var fill))
                {
                    continue;
                }

                using var paint = CreateFillPaint(ApplyAlpha(fill, 200), series.Style?.Effects?.Shadow);
                canvas.DrawCircle(x, y, radius, paint);
            }
        }
    }

    private static DocColor ResolveChartPaletteColor(int index)
    {
        var palette = new[]
        {
            new DocColor(79, 129, 189),
            new DocColor(192, 80, 77),
            new DocColor(155, 187, 89),
            new DocColor(128, 100, 162),
            new DocColor(75, 172, 198),
            new DocColor(247, 150, 70)
        };

        return palette[index % palette.Length];
    }

    private static bool TryResolveFillColor(ChartStyle? style, DocColor fallback, bool fallbackWhenUnset, out DocColor color)
    {
        color = default;
        if (style?.Fill?.IsNone == true)
        {
            return false;
        }

        if (style?.Fill?.Color.HasValue == true)
        {
            color = style.Fill.Color.Value;
            return true;
        }

        if (fallbackWhenUnset)
        {
            color = fallback;
            return true;
        }

        return false;
    }

    private static bool TryResolveFillColor(ChartStyle? primary, ChartStyle? secondary, DocColor fallback, out DocColor color)
    {
        if (primary?.Fill?.IsNone == true)
        {
            color = default;
            return false;
        }

        if (TryResolveFillColor(primary, fallback, false, out color))
        {
            return true;
        }

        return TryResolveFillColor(secondary, fallback, true, out color);
    }

    private static bool TryResolveLineStyle(
        ChartStyle? style,
        DocColor fallback,
        float defaultWidth,
        bool fallbackWhenUnset,
        out DocColor color,
        out float width,
        out DocBorderStyle lineStyle)
    {
        color = default;
        width = defaultWidth;
        lineStyle = DocBorderStyle.Single;

        if (style?.Line?.IsNone == true)
        {
            return false;
        }

        if (style?.Line?.Color.HasValue == true)
        {
            color = style.Line.Color.Value;
        }
        else if (fallbackWhenUnset)
        {
            color = fallback;
        }
        else
        {
            return false;
        }

        if (style?.Line?.Width.HasValue == true)
        {
            width = MathF.Max(0.5f, style.Line.Width.Value);
        }

        if (style?.Line?.Style.HasValue == true)
        {
            lineStyle = style.Line.Style.Value;
        }

        return true;
    }

    private static bool TryResolveLineStyle(
        ChartStyle? primary,
        ChartStyle? secondary,
        DocColor fallback,
        float defaultWidth,
        bool fallbackWhenUnset,
        out DocColor color,
        out float width,
        out DocBorderStyle lineStyle)
    {
        if (primary?.Line?.IsNone == true)
        {
            color = default;
            width = defaultWidth;
            lineStyle = DocBorderStyle.Single;
            return false;
        }

        if (TryResolveLineStyle(primary, fallback, defaultWidth, false, out color, out width, out lineStyle))
        {
            return true;
        }

        return TryResolveLineStyle(secondary, fallback, defaultWidth, fallbackWhenUnset, out color, out width, out lineStyle);
    }

    private static SKPaint CreateFillPaint(DocColor color, ChartShadowEffect? shadow)
    {
        var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(color),
            IsAntialias = true
        };

        ApplyShadow(paint, shadow);
        return paint;
    }

    private static SKPaint CreateLinePaint(DocColor color, float width, DocBorderStyle lineStyle)
    {
        var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(color),
            StrokeWidth = MathF.Max(0.5f, width),
            IsAntialias = true,
            StrokeCap = lineStyle == DocBorderStyle.Dotted ? SKStrokeCap.Round : SKStrokeCap.Butt,
            PathEffect = CreateBorderEffect(lineStyle, width)
        };

        return paint;
    }

    private static void ApplyShadow(SKPaint paint, ChartShadowEffect? shadow)
    {
        if (shadow is null || shadow.BlurRadius <= 0f)
        {
            return;
        }

        var angle = shadow.Direction * (MathF.PI / 180f);
        var dx = shadow.Distance * MathF.Cos(angle);
        var dy = shadow.Distance * MathF.Sin(angle);
        paint.ImageFilter = SKImageFilter.CreateDropShadow(dx, dy, shadow.BlurRadius, shadow.BlurRadius, ToSkColor(shadow.Color));
    }

    private static DocColor ApplyAlpha(DocColor color, byte alpha)
    {
        return new DocColor(color.R, color.G, color.B, alpha);
    }

    private static SKPath CreateDoughnutSlicePath(float centerX, float centerY, float outerRadius, float innerRadius, float startAngle, float sweepAngle)
    {
        var path = new SKPath();
        if (innerRadius <= 0f)
        {
            path.MoveTo(centerX, centerY);
            path.ArcTo(new SKRect(centerX - outerRadius, centerY - outerRadius, centerX + outerRadius, centerY + outerRadius), startAngle, sweepAngle, false);
            path.Close();
            return path;
        }

        var outerRect = new SKRect(centerX - outerRadius, centerY - outerRadius, centerX + outerRadius, centerY + outerRadius);
        var innerRect = new SKRect(centerX - innerRadius, centerY - innerRadius, centerX + innerRadius, centerY + innerRadius);

        var startRad = startAngle * MathF.PI / 180f;
        var endRad = (startAngle + sweepAngle) * MathF.PI / 180f;
        var outerStart = new SKPoint(centerX + outerRadius * MathF.Cos(startRad), centerY + outerRadius * MathF.Sin(startRad));
        var innerEnd = new SKPoint(centerX + innerRadius * MathF.Cos(endRad), centerY + innerRadius * MathF.Sin(endRad));

        path.MoveTo(outerStart);
        path.ArcTo(outerRect, startAngle, sweepAngle, false);
        path.LineTo(innerEnd);
        path.ArcTo(innerRect, startAngle + sweepAngle, -sweepAngle, false);
        path.Close();
        return path;
    }
}
