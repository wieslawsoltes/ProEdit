using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;
using ProEdit.Rendering;

namespace ProEdit.Rendering.Skia;

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

        if (model is null || (model.Series.Count == 0 && model.HierarchyRoots.Count == 0))
        {
            DrawChartPlaceholder(canvas, rect, options, "Chart");
            return;
        }

        var padding = MathF.Max(6f, rect.Width * 0.05f);
        var titleHeight = 0f;
        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            var titleTextSize = model.TitleTextStyle?.FontSize ?? MathF.Max(10f, MathF.Min(18f, rect.Height * 0.06f));
            var titleAlign = model.TitlePosition switch
            {
                ChartTitlePosition.TopLeft => SKTextAlign.Left,
                ChartTitlePosition.TopRight => SKTextAlign.Right,
                _ => SKTextAlign.Center
            };
            using var titlePaint = CreateTextPaint(model.TitleTextStyle, options.TextColor, titleTextSize, titleAlign);
            var titleX = titleAlign switch
            {
                SKTextAlign.Left => rect.Left + padding,
                SKTextAlign.Right => rect.Right - padding,
                _ => rect.MidX
            };
            var titleY = rect.Top + padding * 0.35f + titlePaint.TextSize;
            canvas.DrawText(model.Title, titleX, titleY, titlePaint);
            titleHeight = titlePaint.TextSize + padding * 0.7f;
        }

        var contentRect = new SKRect(
            rect.Left + padding,
            rect.Top + padding + titleHeight,
            rect.Right - padding,
            rect.Bottom - padding);

        if (contentRect.Width <= 4f || contentRect.Height <= 4f)
        {
            DrawChartPlaceholder(canvas, rect, options, "Chart");
            return;
        }

        var legend = ResolveLegend(model);
        var legendLayout = LayoutLegend(model, legend, contentRect, options);
        if (legendLayout is not null && !legendLayout.Overlay)
        {
            contentRect = ShrinkRectForLegend(contentRect, legendLayout.Bounds, legendLayout.Position);
        }

        ChartAxisLayout? horizontalAxis = null;
        ChartAxisLayout? verticalAxis = null;
        var plotRect = contentRect;

        if (RequiresCartesianAxes(model.Type))
        {
            plotRect = BuildCartesianLayout(model, contentRect, options, out horizontalAxis, out verticalAxis);
        }
        else if (model.Type == ChartType.Radar)
        {
            plotRect = BuildRadarLayout(model, contentRect, options);
        }

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

        if (horizontalAxis is not null || verticalAxis is not null)
        {
            DrawCartesianAxes(canvas, plotRect, horizontalAxis, verticalAxis, options);
        }
        else if (model.Type == ChartType.Radar)
        {
            DrawRadarAxes(canvas, plotRect, model, options);
        }

        switch (model.Type)
        {
            case ChartType.Pie:
                DrawPieChart(canvas, plotRect, model, options);
                break;
            case ChartType.Doughnut:
                DrawDoughnutChart(canvas, plotRect, model, options);
                break;
            case ChartType.Treemap:
                DrawTreemapChart(canvas, plotRect, model, options);
                break;
            case ChartType.Sunburst:
                DrawSunburstChart(canvas, plotRect, model, options);
                break;
            case ChartType.Line:
                DrawLineChart(canvas, plotRect, model, horizontalAxis, verticalAxis, options);
                break;
            case ChartType.Scatter:
                DrawScatterChart(canvas, plotRect, model, horizontalAxis, verticalAxis, options);
                break;
            case ChartType.Area:
                DrawAreaChart(canvas, plotRect, model, horizontalAxis, verticalAxis, options);
                break;
            case ChartType.Radar:
                DrawRadarChart(canvas, plotRect, model, options);
                break;
            case ChartType.Bubble:
                DrawBubbleChart(canvas, plotRect, model, horizontalAxis, verticalAxis, options);
                break;
            default:
                DrawBarChart(canvas, plotRect, model, horizontalAxis, verticalAxis, options);
                break;
        }

        if (legendLayout is not null)
        {
            DrawLegend(canvas, legendLayout, options);
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

    private static void DrawBarChart(
        SKCanvas canvas,
        SKRect plotRect,
        ChartModel model,
        ChartAxisLayout? horizontalAxis,
        ChartAxisLayout? verticalAxis,
        RenderOptions options)
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

        var categories = BuildCategoryLabels(model);
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

        var valueAxis = model.BarDirection == ChartBarDirection.Column ? verticalAxis : horizontalAxis;
        if (valueAxis?.Scale is { } axisScale && model.Stacking != ChartStacking.Percent)
        {
            maxValue = Math.Max(1d, axisScale.Max);
        }

        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));

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

                    var fallback = ResolveChartPaletteColor(model, seriesIndex);
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

                    var labelSettings = point is null ? null : ResolveDataLabels(model, series, point);
                    if (labelSettings is not null && ShouldDrawDataLabel(labelSettings))
                    {
                        var percent = stacking && total > 0d ? rawValue / total : (double?)null;
                        var category = categoryIndex < categories.Length ? categories[categoryIndex] : point?.Category;
                        var text = BuildDataLabelText(labelSettings, category, series.Name, rawValue, percent, point?.Size);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var barRect = new SKRect(barLeft, barTop, barRight, baseY);
                            var labelPoint = ResolveBarLabelPosition(barRect, labelSettings.Position, true);
                            DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                        }
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

                    var fallback = ResolveChartPaletteColor(model, seriesIndex);
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

                    var labelSettings = point is null ? null : ResolveDataLabels(model, series, point);
                    if (labelSettings is not null && ShouldDrawDataLabel(labelSettings))
                    {
                        var percent = stacking && total > 0d ? rawValue / total : (double?)null;
                        var category = categoryIndex < categories.Length ? categories[categoryIndex] : point?.Category;
                        var text = BuildDataLabelText(labelSettings, category, series.Name, rawValue, percent, point?.Size);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var barRect = new SKRect(baseX, barTop, barRight, barBottom);
                            var labelPoint = ResolveBarLabelPosition(barRect, labelSettings.Position, false);
                            DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                        }
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

    private static void DrawLineChart(
        SKCanvas canvas,
        SKRect plotRect,
        ChartModel model,
        ChartAxisLayout? horizontalAxis,
        ChartAxisLayout? verticalAxis,
        RenderOptions options)
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

        var valueScale = verticalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), 0d, maxValue);
        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            if (series.Points.Count == 0)
            {
                continue;
            }

            var fallback = ResolveChartPaletteColor(model, seriesIndex);
            if (!TryResolveLineStyle(series.Style, fallback, 2f, true, out var lineColor, out var lineWidth, out var lineStyle))
            {
                continue;
            }

            using var paint = CreateLinePaint(lineColor, lineWidth, lineStyle);
            using var path = new SKPath();
            var points = ArrayPool<SKPoint>.Shared.Rent(series.Points.Count);
            var pointLength = 0;
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

                var x = GetCategoryPosition(model, plotRect, i, pointCount, true);
                var y = MapValueToY(stackedValue, valueScale, plotRect);
                points[pointLength++] = new SKPoint(x, y);
            }

            AppendLineSeriesPath(path, points, pointLength, series.UseSmoothedLine);
            canvas.DrawPath(path, paint);
            ArrayPool<SKPoint>.Shared.Return(points);

            var drawLabels = series.Points.Count > 0;
            if (drawLabels)
            {
                if (model.Stacking != ChartStacking.None && cumulative.Length > 0)
                {
                    cumulative.AsSpan(0, pointCount).Clear();
                }

                for (var i = 0; i < series.Points.Count; i++)
                {
                    var point = series.Points[i];
                    var rawValue = Math.Max(0d, point.Value);
                    var stackedValue = rawValue;
                    if (model.Stacking != ChartStacking.None)
                    {
                        if (model.Stacking == ChartStacking.Percent)
                        {
                            var total = totals.Length > 0 ? totals[i] : 0d;
                            stackedValue = total > 0d ? rawValue / total : 0d;
                        }

                        if (cumulative.Length > 0)
                        {
                            cumulative[i] += stackedValue;
                            stackedValue = cumulative[i];
                        }
                    }

                    var x = GetCategoryPosition(model, plotRect, i, pointCount, true);
                    var y = MapValueToY(stackedValue, valueScale, plotRect);
                    var labelSettings = ResolveDataLabels(model, series, point);
                    if (labelSettings is null || !ShouldDrawDataLabel(labelSettings))
                    {
                        continue;
                    }

                    var percent = model.Stacking == ChartStacking.Percent && totals.Length > 0 && totals[i] > 0d
                        ? rawValue / totals[i]
                        : (double?)null;
                    var category = i < categories.Length ? categories[i] : point.Category;
                    var text = BuildDataLabelText(labelSettings, category, series.Name, rawValue, percent, point.Size);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var labelPoint = ResolvePointLabelPosition(new SKPoint(x, y), labelSettings.Position);
                        DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                    }
                }
            }
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

    private static void DrawAreaChart(
        SKCanvas canvas,
        SKRect plotRect,
        ChartModel model,
        ChartAxisLayout? horizontalAxis,
        ChartAxisLayout? verticalAxis,
        RenderOptions options)
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

        var valueScale = verticalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), 0d, maxValue);
        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);

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

                var fallback = ResolveChartPaletteColor(model, seriesIndex);
                if (!TryResolveFillColor(series.Style, fallback, true, out var fillColor))
                {
                    continue;
                }

                var fillWithAlpha = ApplyAlpha(fillColor, 160);
                using var paint = CreateFillPaint(fillWithAlpha, series.Style?.Effects?.Shadow);
                using var path = new SKPath();

                var firstX = GetCategoryPosition(model, plotRect, 0, pointCount, true);
                var firstY = MapValueToY(topValues[0], valueScale, plotRect);
                path.MoveTo(firstX, firstY);

                for (var i = 1; i < pointCount; i++)
                {
                    var x = GetCategoryPosition(model, plotRect, i, pointCount, true);
                    var y = MapValueToY(topValues[i], valueScale, plotRect);
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
                        var x = GetCategoryPosition(model, plotRect, i, pointCount, true);
                        var y = MapValueToY(baseline[i], valueScale, plotRect);
                        path.LineTo(x, y);
                    }
                }

                path.Close();
                canvas.DrawPath(path, paint);

                for (var i = 0; i < pointCount; i++)
                {
                    if (i >= series.Points.Count)
                    {
                        continue;
                    }

                    var point = series.Points[i];
                    var labelSettings = ResolveDataLabels(model, series, point);
                    if (labelSettings is null || !ShouldDrawDataLabel(labelSettings))
                    {
                        continue;
                    }

                    var rawValue = Math.Max(0d, point.Value);
                    var percent = model.Stacking == ChartStacking.Percent && totals.Length > 0 && totals[i] > 0d
                        ? rawValue / totals[i]
                        : (double?)null;
                    var category = i < categories.Length ? categories[i] : point.Category;
                    var text = BuildDataLabelText(labelSettings, category, series.Name, rawValue, percent, point.Size);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var x = GetCategoryPosition(model, plotRect, i, pointCount, true);
                    var y = MapValueToY(topValues[i], valueScale, plotRect);
                    var labelPoint = ResolvePointLabelPosition(new SKPoint(x, y), labelSettings.Position);
                    DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                }

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

    private static void DrawScatterChart(
        SKCanvas canvas,
        SKRect plotRect,
        ChartModel model,
        ChartAxisLayout? horizontalAxis,
        ChartAxisLayout? verticalAxis,
        RenderOptions options)
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

        var scaleX = horizontalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), minX, maxX);
        var scaleY = verticalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), minY, maxY);
        var radius = MathF.Max(2f, MathF.Min(plotRect.Width, plotRect.Height) * 0.02f);
        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            var fallback = ResolveChartPaletteColor(model, seriesIndex);
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                var x = MapValueToX(xValue, scaleX, plotRect);
                var y = MapValueToY(yValue, scaleY, plotRect);

                if (!TryResolveFillColor(point.Style, series.Style, fallback, out var fill))
                {
                    continue;
                }

                using var paint = CreateFillPaint(fill, series.Style?.Effects?.Shadow);
                canvas.DrawCircle(x, y, radius, paint);

                var labelSettings = ResolveDataLabels(model, series, point);
                if (labelSettings is null || !ShouldDrawDataLabel(labelSettings))
                {
                    continue;
                }

                var category = i < categories.Length ? categories[i] : point.Category;
                var text = BuildDataLabelText(labelSettings, category, series.Name, yValue, null, point.Size);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var labelPoint = ResolvePointLabelPosition(new SKPoint(x, y), labelSettings.Position);
                    DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                }
            }
        }
    }

    private static void DrawPieChart(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
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

        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);
        var centerX = plotRect.MidX;
        var centerY = plotRect.MidY;
        var radius = MathF.Min(plotRect.Width, plotRect.Height) / 2f;
        var startAngle = -90f;

        for (var i = 0; i < series.Points.Count; i++)
        {
            var value = Math.Max(0, series.Points[i].Value);
            var sweep = (float)(value / total) * 360f;
            var point = series.Points[i];
            var fallback = ResolveChartPaletteColor(model, i);
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

            var labelSettings = ResolveDataLabels(model, series, point);
            if (labelSettings is not null && ShouldDrawDataLabel(labelSettings))
            {
                var category = !string.IsNullOrWhiteSpace(point.Category)
                    ? point.Category
                    : i < categories.Length ? categories[i] : null;
                var percent = value / total;
                var text = BuildDataLabelText(labelSettings, category, series.Name, value, percent, point.Size);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var midAngle = startAngle + sweep / 2f;
                    var radians = midAngle * MathF.PI / 180f;
                    var position = labelSettings.Position == ChartDataLabelPosition.BestFit
                        ? ChartDataLabelPosition.OutsideEnd
                        : labelSettings.Position;
                    var labelRadius = position switch
                    {
                        ChartDataLabelPosition.Center => radius * 0.5f,
                        ChartDataLabelPosition.InsideBase => radius * 0.4f,
                        ChartDataLabelPosition.InsideEnd => radius * 0.85f,
                        ChartDataLabelPosition.OutsideEnd => radius * 1.15f,
                        _ => radius * 0.9f
                    };
                    var labelX = centerX + MathF.Cos(radians) * labelRadius;
                    var labelY = centerY + MathF.Sin(radians) * labelRadius;
                    if (labelSettings.ShowLeaderLines == true && labelRadius > radius)
                    {
                        using var leaderPaint = CreateLinePaint(options.PlaceholderStrokeColor, 1f, DocBorderStyle.Single);
                        var edgeX = centerX + MathF.Cos(radians) * radius;
                        var edgeY = centerY + MathF.Sin(radians) * radius;
                        canvas.DrawLine(edgeX, edgeY, labelX, labelY, leaderPaint);
                    }

                    DrawChartLabel(canvas, text, new SKPoint(labelX, labelY), labelSettings, options, labelTextSize);
                }
            }

            startAngle += sweep;
        }
    }

    private static void DrawDoughnutChart(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
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

        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);
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
            var fallback = ResolveChartPaletteColor(model, i);

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

            var labelSettings = ResolveDataLabels(model, series, point);
            if (labelSettings is not null && ShouldDrawDataLabel(labelSettings))
            {
                var category = !string.IsNullOrWhiteSpace(point.Category)
                    ? point.Category
                    : i < categories.Length ? categories[i] : null;
                var percent = value / total;
                var text = BuildDataLabelText(labelSettings, category, series.Name, value, percent, point.Size);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var midAngle = startAngle + sweep / 2f;
                    var radians = midAngle * MathF.PI / 180f;
                    var midRadius = innerRadius + (radius - innerRadius) * 0.5f;
                    var position = labelSettings.Position == ChartDataLabelPosition.BestFit
                        ? ChartDataLabelPosition.OutsideEnd
                        : labelSettings.Position;
                    var labelRadius = position switch
                    {
                        ChartDataLabelPosition.Center => midRadius,
                        ChartDataLabelPosition.InsideBase => innerRadius + (radius - innerRadius) * 0.3f,
                        ChartDataLabelPosition.InsideEnd => innerRadius + (radius - innerRadius) * 0.8f,
                        ChartDataLabelPosition.OutsideEnd => radius * 1.15f,
                        _ => midRadius
                    };
                    var labelX = centerX + MathF.Cos(radians) * labelRadius;
                    var labelY = centerY + MathF.Sin(radians) * labelRadius;
                    if (labelSettings.ShowLeaderLines == true && labelRadius > radius)
                    {
                        using var leaderPaint = CreateLinePaint(options.PlaceholderStrokeColor, 1f, DocBorderStyle.Single);
                        var edgeX = centerX + MathF.Cos(radians) * radius;
                        var edgeY = centerY + MathF.Sin(radians) * radius;
                        canvas.DrawLine(edgeX, edgeY, labelX, labelY, leaderPaint);
                    }

                    DrawChartLabel(canvas, text, new SKPoint(labelX, labelY), labelSettings, options, labelTextSize);
                }
            }

            startAngle += sweep;
        }
    }

    private static void DrawTreemapChart(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
    {
        if (model.HierarchyRoots.Count == 0)
        {
            DrawChartPlaceholder(canvas, plotRect, options, "Treemap");
            return;
        }

        DrawTreemapNodes(canvas, plotRect, model, model.HierarchyRoots, 0, options, Array.Empty<int>());
    }

    private static void DrawTreemapNodes(
        SKCanvas canvas,
        SKRect rect,
        ChartModel model,
        IReadOnlyList<ChartHierarchyNode> nodes,
        int depth,
        RenderOptions options,
        IReadOnlyList<int> path)
    {
        if (rect.Width <= 4f || rect.Height <= 4f || nodes.Count == 0)
        {
            return;
        }

        var total = 0d;
        for (var index = 0; index < nodes.Count; index++)
        {
            total += Math.Max(0d, nodes[index].Value);
        }

        if (total <= 0d)
        {
            return;
        }

        var horizontal = rect.Width >= rect.Height;
        var cursor = horizontal ? rect.Left : rect.Top;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var value = Math.Max(0d, node.Value);
            if (value <= 0d)
            {
                continue;
            }

            var fraction = (float)(value / total);
            SKRect nodeRect;
            if (horizontal)
            {
                var width = index == nodes.Count - 1 ? rect.Right - cursor : rect.Width * fraction;
                nodeRect = new SKRect(cursor, rect.Top, cursor + width, rect.Bottom);
                cursor += width;
            }
            else
            {
                var height = index == nodes.Count - 1 ? rect.Bottom - cursor : rect.Height * fraction;
                nodeRect = new SKRect(rect.Left, cursor, rect.Right, cursor + height);
                cursor += height;
            }

            var resolvedPath = AppendHierarchyPath(path, index);
            DrawTreemapNode(canvas, nodeRect, model, node, resolvedPath, options);

            if (node.Children.Count > 0)
            {
                var inset = MathF.Max(2f, MathF.Min(nodeRect.Width, nodeRect.Height) * 0.015f);
                var childRect = new SKRect(nodeRect.Left + inset, nodeRect.Top + inset, nodeRect.Right - inset, nodeRect.Bottom - inset);
                DrawTreemapNodes(canvas, childRect, model, node.Children, depth + 1, options, resolvedPath);
            }
        }
    }

    private static void DrawTreemapNode(
        SKCanvas canvas,
        SKRect rect,
        ChartModel model,
        ChartHierarchyNode node,
        IReadOnlyList<int> path,
        RenderOptions options)
    {
        if (rect.Width <= 2f || rect.Height <= 2f)
        {
            return;
        }

        var fill = ResolveHierarchyNodeColor(model, node, path, options);
        using var fillPaint = CreateFillPaint(fill, node.Style?.Effects?.Shadow);
        canvas.DrawRect(rect, fillPaint);

        if (TryResolveLineStyle(node.Style, options.PlaceholderStrokeColor, 1f, true, out var borderColor, out var borderWidth, out var borderDash))
        {
            using var borderPaint = CreateLinePaint(borderColor, borderWidth, borderDash);
            canvas.DrawRect(rect, borderPaint);
        }
        else
        {
            using var borderPaint = CreateLinePaint(new DocColor(211, 211, 211), 0.75f, DocBorderStyle.Single);
            canvas.DrawRect(rect, borderPaint);
        }

        if (rect.Width < 48f || rect.Height < 22f)
        {
            return;
        }

        var textColor = GetContrastingTextColor(fill);
        var textSize = MathF.Max(8f, MathF.Min(14f, MathF.Min(rect.Width, rect.Height) * 0.1f));
        using var labelPaint = CreateTextPaint(node.DataLabel?.TextStyle, textColor, textSize, SKTextAlign.Left);
        var lines = BuildTreemapLabelLines(node, labelPaint, rect.Width - 8f);
        if (lines.Count == 0)
        {
            return;
        }

        var y = rect.Top + 4f + labelPaint.TextSize;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (y > rect.Bottom - 2f)
            {
                break;
            }

            canvas.DrawText(lines[lineIndex], rect.Left + 4f, y, labelPaint);
            y += labelPaint.TextSize * 1.15f;
        }
    }

    private static void DrawSunburstChart(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
    {
        if (model.HierarchyRoots.Count == 0)
        {
            DrawChartPlaceholder(canvas, plotRect, options, "Sunburst");
            return;
        }

        var maxDepth = Math.Max(1, GetHierarchyDepth(model.HierarchyRoots));
        var radius = MathF.Min(plotRect.Width, plotRect.Height) * 0.48f;
        var innerRadius = MathF.Max(14f, radius * 0.18f);
        var ringThickness = MathF.Max(12f, (radius - innerRadius) / maxDepth);
        var center = new SKPoint(plotRect.MidX, plotRect.MidY);
        var total = 0d;
        for (var index = 0; index < model.HierarchyRoots.Count; index++)
        {
            total += Math.Max(0d, model.HierarchyRoots[index].Value);
        }

        if (total <= 0d)
        {
            DrawChartPlaceholder(canvas, plotRect, options, "Sunburst");
            return;
        }

        var startAngle = -90f;
        for (var index = 0; index < model.HierarchyRoots.Count; index++)
        {
            var node = model.HierarchyRoots[index];
            var sweep = (float)(Math.Max(0d, node.Value) / total * 360d);
            if (sweep <= 0f)
            {
                continue;
            }

            DrawSunburstNode(
                canvas,
                model,
                center,
                innerRadius,
                ringThickness,
                node,
                startAngle,
                sweep,
                0,
                AppendHierarchyPath(Array.Empty<int>(), index),
                options);
            startAngle += sweep;
        }
    }

    private static void DrawSunburstNode(
        SKCanvas canvas,
        ChartModel model,
        SKPoint center,
        float innerRadius,
        float ringThickness,
        ChartHierarchyNode node,
        float startAngle,
        float sweepAngle,
        int depth,
        IReadOnlyList<int> path,
        RenderOptions options)
    {
        var ringInner = innerRadius + depth * ringThickness;
        var ringOuter = ringInner + ringThickness;
        var fill = ResolveHierarchyNodeColor(model, node, path, options);
        using (var slicePath = CreateDoughnutSlicePath(center.X, center.Y, ringOuter, ringInner, startAngle, sweepAngle))
        {
            using var fillPaint = CreateFillPaint(fill, node.Style?.Effects?.Shadow);
            canvas.DrawPath(slicePath, fillPaint);

            if (TryResolveLineStyle(node.Style, DocColor.White, 1f, true, out var lineColor, out var lineWidth, out var lineDash))
            {
                using var borderPaint = CreateLinePaint(lineColor, lineWidth, lineDash);
                canvas.DrawPath(slicePath, borderPaint);
            }
            else
            {
                using var borderPaint = CreateLinePaint(DocColor.White, 0.8f, DocBorderStyle.Single);
                canvas.DrawPath(slicePath, borderPaint);
            }
        }

        if (sweepAngle >= 12f)
        {
            var labelRadius = ringInner + (ringOuter - ringInner) * 0.5f;
            var labelAngle = startAngle + sweepAngle * 0.5f;
            var radians = DegreesToRadians(labelAngle);
            var labelPoint = new SKPoint(
                center.X + MathF.Cos(radians) * labelRadius,
                center.Y + MathF.Sin(radians) * labelRadius);
            var labelSettings = node.DataLabel ?? new ChartDataLabelSettings
            {
                ShowValue = true,
                TextStyle = new ChartTextStyle
                {
                    FontSize = MathF.Max(8f, ringThickness * 0.24f),
                    Color = GetContrastingTextColor(fill)
                }
            };
            labelSettings.ShowValue ??= true;
            labelSettings.TextStyle ??= new ChartTextStyle();
            labelSettings.TextStyle.FontSize ??= MathF.Max(8f, ringThickness * 0.24f);
            labelSettings.TextStyle.Color ??= GetContrastingTextColor(fill);
            var labelText = BuildDataLabelText(labelSettings, node.Label, null, node.Value, null, null)
                ?? FormatChartValue(node.Value, labelSettings.NumberFormat);
            DrawChartLabel(
                canvas,
                labelText,
                labelPoint,
                labelSettings,
                options,
                MathF.Max(8f, ringThickness * 0.24f));
        }

        if (node.Children.Count == 0 || node.Value <= 0d)
        {
            return;
        }

        var childStart = startAngle;
        for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            var child = node.Children[childIndex];
            var childSweep = (float)(Math.Max(0d, child.Value) / node.Value * sweepAngle);
            if (childSweep <= 0f)
            {
                continue;
            }

            var childPath = AppendHierarchyPath(path, childIndex);
            DrawSunburstNode(canvas, model, center, innerRadius, ringThickness, child, childStart, childSweep, depth + 1, childPath, options);
            childStart += childSweep;
        }
    }

    private static void DrawRadarChart(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
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

        var valueAxis = model.Axes.FirstOrDefault(axis => axis.Kind == ChartAxisKind.Value);
        var valueScale = valueAxis is null ? ComputeAxisScale(new ChartAxis(), 0d, maxValue) : ComputeAxisScale(valueAxis, 0d, maxValue);
        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);
        var valueRange = valueScale.Max - valueScale.Min;
        if (valueRange <= 0d)
        {
            valueRange = 1d;
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

            var fallback = ResolveChartPaletteColor(model, seriesIndex);
            var hasLine = TryResolveLineStyle(series.Style, fallback, 1.5f, true, out var lineColor, out var lineWidth, out var lineStyle);
            var fillColor = default(DocColor);
            var hasFill = model.RadarStyle == ChartRadarStyle.Filled
                && TryResolveFillColor(series.Style, fallback, true, out fillColor);

            using var path = new SKPath();
            for (var i = 0; i < pointCount; i++)
            {
                var value = i < series.Points.Count ? Math.Max(0d, series.Points[i].Value) : 0d;
                var normalized = (float)Math.Clamp((value - valueScale.Min) / valueRange, 0d, 1d);
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
                    var normalized = (float)Math.Clamp((value - valueScale.Min) / valueRange, 0d, 1d);
                    var angle = -MathF.PI / 2f + i * angleStep;
                    var x = centerX + MathF.Cos(angle) * radius * normalized;
                    var y = centerY + MathF.Sin(angle) * radius * normalized;
                    using var markerPaint = CreateFillPaint(lineColor, series.Style?.Effects?.Shadow);
                    canvas.DrawCircle(x, y, markerRadius, markerPaint);
                }
            }

            for (var i = 0; i < pointCount; i++)
            {
                if (i >= series.Points.Count)
                {
                    continue;
                }

                var point = series.Points[i];
                var labelSettings = ResolveDataLabels(model, series, point);
                if (labelSettings is null || !ShouldDrawDataLabel(labelSettings))
                {
                    continue;
                }

                var rawValue = Math.Max(0d, point.Value);
                var normalized = (float)Math.Clamp((rawValue - valueScale.Min) / valueRange, 0d, 1d);
                var angle = -MathF.PI / 2f + i * angleStep;
                var x = centerX + MathF.Cos(angle) * radius * normalized;
                var y = centerY + MathF.Sin(angle) * radius * normalized;
                var category = i < categories.Length ? categories[i] : point.Category;
                var text = BuildDataLabelText(labelSettings, category, series.Name, rawValue, null, point.Size);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var labelPoint = ResolvePointLabelPosition(new SKPoint(x, y), labelSettings.Position);
                    DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                }
            }
        }
    }

    private static void DrawBubbleChart(
        SKCanvas canvas,
        SKRect plotRect,
        ChartModel model,
        ChartAxisLayout? horizontalAxis,
        ChartAxisLayout? verticalAxis,
        RenderOptions options)
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

        var scaleX = horizontalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), minX, maxX);
        var scaleY = verticalAxis?.Scale ?? ComputeAxisScale(new ChartAxis(), minY, maxY);
        var maxRadius = MathF.Min(plotRect.Width, plotRect.Height) * 0.08f;
        var minRadius = MathF.Max(2f, maxRadius * 0.4f);
        var labelTextSize = MathF.Max(7f, MathF.Min(11f, plotRect.Height * 0.08f));
        var categories = BuildCategoryLabels(model);

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            var fallback = ResolveChartPaletteColor(model, seriesIndex);
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = series.Points[i];
                var xValue = point.XValue ?? i;
                var yValue = point.Value;
                var sizeValue = point.Size ?? 1d;
                var x = MapValueToX(xValue, scaleX, plotRect);
                var y = MapValueToY(yValue, scaleY, plotRect);
                var sizeRatio = (float)((sizeValue - minSize) / (maxSize - minSize));
                var radius = minRadius + sizeRatio * (maxRadius - minRadius);

                if (!TryResolveFillColor(point.Style, series.Style, fallback, out var fill))
                {
                    continue;
                }

                using var paint = CreateFillPaint(ApplyAlpha(fill, 200), series.Style?.Effects?.Shadow);
                canvas.DrawCircle(x, y, radius, paint);

                var labelSettings = ResolveDataLabels(model, series, point);
                if (labelSettings is null || !ShouldDrawDataLabel(labelSettings))
                {
                    continue;
                }

                var category = i < categories.Length ? categories[i] : point.Category;
                var text = BuildDataLabelText(labelSettings, category, series.Name, yValue, null, point.Size);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var labelPoint = ResolvePointLabelPosition(new SKPoint(x, y), labelSettings.Position);
                    DrawChartLabel(canvas, text, labelPoint, labelSettings, options, labelTextSize);
                }
            }
        }
    }

    private static DocColor ResolveChartPaletteColor(ChartModel model, int index)
    {
        var palette = ResolveChartPalette(model.PaletteName);

        return palette[index % palette.Count];
    }

    private static IReadOnlyList<DocColor> ResolveChartPalette(string? paletteName)
    {
        if (string.Equals(paletteName, "EarthTones", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new DocColor(252, 139, 14),
                new DocColor(201, 140, 29),
                new DocColor(194, 83, 4),
                new DocColor(115, 153, 34),
                new DocColor(210, 139, 59),
                new DocColor(153, 107, 56)
            };
        }

        return new[]
        {
            new DocColor(79, 129, 189),
            new DocColor(192, 80, 77),
            new DocColor(155, 187, 89),
            new DocColor(128, 100, 162),
            new DocColor(75, 172, 198),
            new DocColor(247, 150, 70)
        };
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

    private readonly struct ChartValueScale
    {
        public ChartValueScale(double min, double max, double majorUnit, double minorUnit)
        {
            Min = min;
            Max = max;
            MajorUnit = majorUnit;
            MinorUnit = minorUnit;
        }

        public double Min { get; }
        public double Max { get; }
        public double MajorUnit { get; }
        public double MinorUnit { get; }
    }

    private sealed class ChartAxisLayout
    {
        public ChartAxisLayout(ChartModel model, ChartAxis axis, bool isHorizontal, bool isValueAxis)
        {
            Model = model;
            Axis = axis;
            IsHorizontal = isHorizontal;
            IsValueAxis = isValueAxis;
            Labels = Array.Empty<string>();
        }

        public ChartModel Model { get; }
        public ChartAxis Axis { get; }
        public bool IsHorizontal { get; }
        public bool IsValueAxis { get; }
        public ChartValueScale? Scale { get; set; }
        public string[] Labels { get; set; }
        public double[]? TickValues { get; set; }
        public float LabelExtent { get; set; }
        public float TitleExtent { get; set; }
        public float TickLength { get; set; }
        public float LabelTextSize { get; set; }
        public float TitleTextSize { get; set; }
    }

    private readonly struct LegendEntryLayout
    {
        public LegendEntryLayout(
            string text,
            DocColor color,
            SKRect marker,
            SKPoint textOrigin,
            bool useLineMarker,
            float lineWidth,
            DocBorderStyle lineStyle)
        {
            Text = text;
            Color = color;
            Marker = marker;
            TextOrigin = textOrigin;
            UseLineMarker = useLineMarker;
            LineWidth = lineWidth;
            LineStyle = lineStyle;
        }

        public string Text { get; }
        public DocColor Color { get; }
        public SKRect Marker { get; }
        public SKPoint TextOrigin { get; }
        public bool UseLineMarker { get; }
        public float LineWidth { get; }
        public DocBorderStyle LineStyle { get; }
    }

    private sealed class ChartLegendLayout
    {
        public ChartLegendLayout(
            SKRect bounds,
            ChartLegendPosition position,
            bool overlay,
            List<LegendEntryLayout> entries,
            ChartTextStyle? textStyle,
            float textSize,
            float markerSize)
        {
            Bounds = bounds;
            Position = position;
            Overlay = overlay;
            Entries = entries;
            TextStyle = textStyle;
            TextSize = textSize;
            MarkerSize = markerSize;
        }

        public SKRect Bounds { get; }
        public ChartLegendPosition Position { get; }
        public bool Overlay { get; }
        public List<LegendEntryLayout> Entries { get; }
        public ChartTextStyle? TextStyle { get; }
        public float TextSize { get; }
        public float MarkerSize { get; }
    }

    private static ChartLegend? ResolveLegend(ChartModel model)
    {
        if (model.Legend is not null)
        {
            return model.Legend;
        }

        if (model.Type == ChartType.Pie || model.Type == ChartType.Doughnut)
        {
            return new ChartLegend { IsVisible = true, Position = ChartLegendPosition.Right };
        }

        return model.Series.Count > 1 ? new ChartLegend { IsVisible = true, Position = ChartLegendPosition.Right } : null;
    }

    private static ChartLegendLayout? LayoutLegend(ChartModel model, ChartLegend? legend, SKRect available, RenderOptions options)
    {
        if (legend is null || !legend.IsVisible)
        {
            return null;
        }

        var entries = new List<(string Text, DocColor Color, bool UseLineMarker, float LineWidth, DocBorderStyle LineStyle)>();
        if (model.Type == ChartType.Pie || model.Type == ChartType.Doughnut)
        {
            var series = model.Series.FirstOrDefault();
            if (series is not null)
            {
                for (var i = 0; i < series.Points.Count; i++)
                {
                    var point = series.Points[i];
                    var text = point.Category ?? $"Item {i + 1}";
                    var fallback = ResolveChartPaletteColor(model, i);
                    var color = TryResolveFillColor(point.Style, series.Style, fallback, out var fill) ? fill : fallback;
                    entries.Add((text, color, false, 1f, DocBorderStyle.Single));
                }
            }
        }
        else if (model.Type == ChartType.Treemap || model.Type == ChartType.Sunburst)
        {
            for (var index = 0; index < model.HierarchyRoots.Count; index++)
            {
                var node = model.HierarchyRoots[index];
                var text = node.Label ?? $"Item {index + 1}";
                var color = ResolveHierarchyNodeColor(model, node, AppendHierarchyPath(Array.Empty<int>(), index), options);
                entries.Add((text, color, false, 1f, DocBorderStyle.Single));
            }
        }
        else
        {
            for (var i = 0; i < model.Series.Count; i++)
            {
                var series = model.Series[i];
                var text = series.Name ?? $"Series {i + 1}";
                var fallback = ResolveChartPaletteColor(model, i);
                DocColor color;
                if (model.Type == ChartType.Line || model.Type == ChartType.Scatter || model.Type == ChartType.Radar)
                {
                    var hasLineStyle = TryResolveLineStyle(series.Style, fallback, 2f, true, out var lineColor, out var lineWidth, out var lineStyle);
                    color = hasLineStyle
                        ? lineColor
                        : fallback;
                    entries.Add((text, color, true, hasLineStyle ? lineWidth : 2f, hasLineStyle ? lineStyle : DocBorderStyle.Single));
                }
                else
                {
                    color = TryResolveFillColor(series.Style, fallback, true, out var fill) ? fill : fallback;
                    entries.Add((text, color, false, 1f, DocBorderStyle.Single));
                }
            }
        }

        if (entries.Count == 0)
        {
            return null;
        }

        var textSize = legend.TextStyle?.FontSize ?? MathF.Max(8f, available.Height * 0.05f);
        using var textPaint = CreateTextPaint(legend.TextStyle, options.TextColor, textSize, SKTextAlign.Left);
        var markerSize = MathF.Max(6f, textPaint.TextSize * 0.7f);
        var gap = MathF.Max(4f, textPaint.TextSize * 0.4f);
        var padding = MathF.Max(4f, textPaint.TextSize * 0.5f);
        var lineHeight = textPaint.TextSize * 1.25f;

        var entryLayouts = new List<LegendEntryLayout>(entries.Count);
        var bounds = SKRect.Empty;

        if (legend.Position == ChartLegendPosition.Left
            || legend.Position == ChartLegendPosition.Right
            || legend.Position == ChartLegendPosition.Corner)
        {
            var maxTextWidth = 0f;
            foreach (var entry in entries)
            {
                maxTextWidth = MathF.Max(maxTextWidth, textPaint.MeasureText(entry.Text));
            }

            var width = padding * 2f + markerSize + gap + maxTextWidth;
            var height = padding * 2f + entries.Count * lineHeight;
            var left = legend.Position == ChartLegendPosition.Left ? available.Left : available.Right - width;
            var top = legend.Position == ChartLegendPosition.Corner
                ? available.Top
                : available.Top + (available.Height - height) * 0.5f;

            bounds = new SKRect(left, top, left + width, top + height);
            var cursorY = bounds.Top + padding;

            foreach (var entry in entries)
            {
                var marker = new SKRect(bounds.Left + padding, cursorY + (lineHeight - markerSize) * 0.5f,
                    bounds.Left + padding + markerSize, cursorY + (lineHeight - markerSize) * 0.5f + markerSize);
                var textOrigin = new SKPoint(marker.Right + gap, cursorY + textPaint.TextSize);
                entryLayouts.Add(new LegendEntryLayout(entry.Text, entry.Color, marker, textOrigin, entry.UseLineMarker, entry.LineWidth, entry.LineStyle));
                cursorY += lineHeight;
            }
        }
        else
        {
            var cursorX = available.Left + padding;
            var cursorY = available.Top + padding;
            var maxX = cursorX;

            foreach (var entry in entries)
            {
                var textWidth = textPaint.MeasureText(entry.Text);
                var itemWidth = markerSize + gap + textWidth;
                var nextX = cursorX + itemWidth + gap;
                if (nextX > available.Right - padding && cursorX > available.Left + padding)
                {
                    cursorX = available.Left + padding;
                    cursorY += lineHeight;
                }

                var marker = new SKRect(cursorX, cursorY + (lineHeight - markerSize) * 0.5f,
                    cursorX + markerSize, cursorY + (lineHeight - markerSize) * 0.5f + markerSize);
                var textOrigin = new SKPoint(marker.Right + gap, cursorY + textPaint.TextSize);
                entryLayouts.Add(new LegendEntryLayout(entry.Text, entry.Color, marker, textOrigin, entry.UseLineMarker, entry.LineWidth, entry.LineStyle));

                cursorX += itemWidth + gap;
                maxX = MathF.Max(maxX, cursorX);
            }

            var width = MathF.Min(available.Width, MathF.Max(maxX - available.Left + padding, padding * 2f + markerSize));
            var height = MathF.Min(available.Height, cursorY - available.Top + lineHeight + padding);
            var left = legend.Position switch
            {
                ChartLegendPosition.TopLeft or ChartLegendPosition.BottomLeft => available.Left,
                ChartLegendPosition.TopRight or ChartLegendPosition.BottomRight => available.Right - width,
                _ => available.Left + (available.Width - width) * 0.5f
            };
            var top = legend.Position switch
            {
                ChartLegendPosition.Bottom or ChartLegendPosition.BottomLeft or ChartLegendPosition.BottomRight
                    => available.Bottom - height,
                _ => available.Top
            };
            bounds = new SKRect(left, top, left + width, top + height);

            var offsetX = bounds.Left - available.Left;
            var offsetY = bounds.Top - available.Top;
            if (Math.Abs(offsetX) > float.Epsilon || Math.Abs(offsetY) > float.Epsilon)
            {
                for (var i = 0; i < entryLayouts.Count; i++)
                {
                    var entry = entryLayouts[i];
                    var marker = entry.Marker;
                    marker.Offset(offsetX, offsetY);
                    var textOrigin = new SKPoint(entry.TextOrigin.X + offsetX, entry.TextOrigin.Y + offsetY);
                    entryLayouts[i] = new LegendEntryLayout(entry.Text, entry.Color, marker, textOrigin, entry.UseLineMarker, entry.LineWidth, entry.LineStyle);
                }
            }
        }

        return new ChartLegendLayout(bounds, legend.Position, legend.Overlay, entryLayouts, legend.TextStyle, textSize, markerSize);
    }

    private static void DrawLegend(SKCanvas canvas, ChartLegendLayout layout, RenderOptions options)
    {
        using var textPaint = CreateTextPaint(layout.TextStyle, options.TextColor, layout.TextSize, SKTextAlign.Left);
        foreach (var entry in layout.Entries)
        {
            if (entry.UseLineMarker)
            {
                using var linePaint = CreateLinePaint(entry.Color, MathF.Max(1.5f, entry.LineWidth), entry.LineStyle);
                var y = entry.Marker.MidY;
                canvas.DrawLine(entry.Marker.Left, y, entry.Marker.Right, y, linePaint);
            }
            else
            {
                using var markerPaint = CreateFillPaint(entry.Color, null);
                canvas.DrawRect(entry.Marker, markerPaint);
            }

            canvas.DrawText(entry.Text, entry.TextOrigin.X, entry.TextOrigin.Y, textPaint);
        }
    }

    private static SKRect ShrinkRectForLegend(SKRect rect, SKRect legendBounds, ChartLegendPosition position)
    {
        return position switch
        {
            ChartLegendPosition.Left => new SKRect(legendBounds.Right, rect.Top, rect.Right, rect.Bottom),
            ChartLegendPosition.Right => new SKRect(rect.Left, rect.Top, legendBounds.Left, rect.Bottom),
            ChartLegendPosition.Top or ChartLegendPosition.TopLeft or ChartLegendPosition.TopRight
                => new SKRect(rect.Left, legendBounds.Bottom, rect.Right, rect.Bottom),
            ChartLegendPosition.Bottom or ChartLegendPosition.BottomLeft or ChartLegendPosition.BottomRight
                => new SKRect(rect.Left, rect.Top, rect.Right, legendBounds.Top),
            ChartLegendPosition.Corner => new SKRect(rect.Left, rect.Top, legendBounds.Left, rect.Bottom),
            _ => rect
        };
    }

    private static bool RequiresCartesianAxes(ChartType type)
    {
        return type != ChartType.Pie
            && type != ChartType.Doughnut
            && type != ChartType.Radar
            && type != ChartType.Treemap
            && type != ChartType.Sunburst;
    }

    private static SKRect BuildCartesianLayout(
        ChartModel model,
        SKRect contentRect,
        RenderOptions options,
        out ChartAxisLayout? horizontalAxis,
        out ChartAxisLayout? verticalAxis)
    {
        horizontalAxis = null;
        verticalAxis = null;

        var categories = BuildCategoryLabels(model);
        var axes = model.Axes;
        var bottomAxis = axes.FirstOrDefault(axis => axis.Position == ChartAxisPosition.Bottom);
        var topAxis = axes.FirstOrDefault(axis => axis.Position == ChartAxisPosition.Top);
        var leftAxis = axes.FirstOrDefault(axis => axis.Position == ChartAxisPosition.Left);
        var rightAxis = axes.FirstOrDefault(axis => axis.Position == ChartAxisPosition.Right);

        var horizontalAxisModel = bottomAxis ?? topAxis ?? CreateDefaultAxis(ResolveDefaultHorizontalAxisKind(model), ChartAxisPosition.Bottom);
        var verticalAxisModel = leftAxis ?? rightAxis ?? CreateDefaultAxis(ResolveDefaultVerticalAxisKind(model), ChartAxisPosition.Left);

        var labelTextSize = MathF.Max(8f, MathF.Min(12f, contentRect.Height * 0.07f));
        var titleTextSize = MathF.Max(labelTextSize + 1f, MathF.Min(14f, contentRect.Height * 0.08f));

        horizontalAxis = BuildAxisLayout(model, horizontalAxisModel, true, categories, labelTextSize, titleTextSize, options);
        verticalAxis = BuildAxisLayout(model, verticalAxisModel, false, categories, labelTextSize, titleTextSize, options);

        var plotRect = contentRect;
        if (horizontalAxis is not null && horizontalAxis.Axis.IsVisible)
        {
            var extent = horizontalAxis.LabelExtent + horizontalAxis.TitleExtent + horizontalAxis.TickLength + 2f;
            if (horizontalAxis.Axis.Position == ChartAxisPosition.Top)
            {
                plotRect.Top += extent;
            }
            else
            {
                plotRect.Bottom -= extent;
            }
        }

        if (verticalAxis is not null && verticalAxis.Axis.IsVisible)
        {
            var extent = verticalAxis.LabelExtent + verticalAxis.TitleExtent + verticalAxis.TickLength + 2f;
            if (verticalAxis.Axis.Position == ChartAxisPosition.Right)
            {
                plotRect.Right -= extent;
            }
            else
            {
                plotRect.Left += extent;
            }
        }

        if (horizontalAxis is not null
            && horizontalAxis.Axis.IsVisible
            && horizontalAxis.Axis.Kind == ChartAxisKind.Category
            && horizontalAxis.Labels.Length > 0)
        {
            using var labelPaint = CreateTextPaint(horizontalAxis.Axis.LabelTextStyle, options.TextColor, horizontalAxis.LabelTextSize, SKTextAlign.Center);
            var maxLabelWidth = 0f;
            for (var i = 0; i < horizontalAxis.Labels.Length; i++)
            {
                maxLabelWidth = MathF.Max(maxLabelWidth, labelPaint.MeasureText(horizontalAxis.Labels[i]));
            }

            var inset = MathF.Min(plotRect.Width * 0.12f, MathF.Max(0f, maxLabelWidth * 0.5f));
            if (inset > 0f)
            {
                plotRect.Left += inset;
                plotRect.Right -= inset;
            }
        }

        return plotRect;
    }

    private static SKRect BuildRadarLayout(ChartModel model, SKRect contentRect, RenderOptions options)
    {
        var categories = BuildCategoryLabels(model);
        if (categories.Length == 0)
        {
            return contentRect;
        }

        var labelSize = MathF.Max(8f, MathF.Min(12f, contentRect.Height * 0.07f));
        using var labelPaint = CreateTextPaint(null, options.TextColor, labelSize, SKTextAlign.Center);
        var maxLabelWidth = 0f;
        foreach (var label in categories)
        {
            maxLabelWidth = MathF.Max(maxLabelWidth, labelPaint.MeasureText(label));
        }

        var inset = MathF.Max(labelSize, maxLabelWidth * 0.5f);
        return new SKRect(contentRect.Left + inset, contentRect.Top + inset, contentRect.Right - inset, contentRect.Bottom - inset);
    }

    private static ChartAxisLayout BuildAxisLayout(
        ChartModel model,
        ChartAxis axis,
        bool isHorizontal,
        string[] categories,
        float labelTextSize,
        float titleTextSize,
        RenderOptions options)
    {
        var layout = new ChartAxisLayout(model, axis, isHorizontal, axis.Kind == ChartAxisKind.Value)
        {
            LabelTextSize = axis.LabelTextStyle?.FontSize ?? labelTextSize,
            TitleTextSize = axis.TitleTextStyle?.FontSize ?? titleTextSize
        };

        layout.TickLength = axis.MajorTickMark == ChartTickMark.None ? 0f : MathF.Max(3f, layout.LabelTextSize * 0.25f);

        if (layout.IsValueAxis)
        {
            var useXValues = (model.Type == ChartType.Scatter || model.Type == ChartType.Bubble) && isHorizontal;
            var categoryCount = categories.Length;
            var useStacking = !useXValues && model.Stacking != ChartStacking.None;
            var percentStacking = !useXValues && model.Stacking == ChartStacking.Percent;
            var (min, max) = ComputeValueRange(model, useXValues, useStacking, percentStacking, categoryCount);
            var scale = ComputeAxisScale(axis, min, max, percentStacking);
            layout.Scale = scale;
            layout.TickValues = BuildTickValues(scale);
            layout.Labels = BuildValueLabels(layout.TickValues, axis.NumberFormat);
        }
        else
        {
            layout.Labels = categories;
        }

        if (axis.IsVisible && axis.TickLabelPosition != ChartTickLabelPosition.None)
        {
            using var labelPaint = CreateTextPaint(axis.LabelTextStyle, options.TextColor, layout.LabelTextSize, SKTextAlign.Left);
            layout.LabelExtent = MeasureAxisLabelExtent(labelPaint, layout.Labels, isHorizontal);
        }
        else
        {
            layout.LabelExtent = 0f;
        }

        layout.TitleExtent = axis.IsVisible && !string.IsNullOrWhiteSpace(axis.Title) ? layout.TitleTextSize : 0f;
        return layout;
    }

    private static ChartAxis CreateDefaultAxis(ChartAxisKind kind, ChartAxisPosition position)
    {
        return new ChartAxis
        {
            Kind = kind,
            Position = position,
            IsVisible = true
        };
    }

    private static ChartAxisKind ResolveDefaultHorizontalAxisKind(ChartModel model)
    {
        if (model.Type == ChartType.Scatter || model.Type == ChartType.Bubble)
        {
            return ChartAxisKind.Value;
        }

        if (model.Type == ChartType.Bar && model.BarDirection == ChartBarDirection.Bar)
        {
            return ChartAxisKind.Value;
        }

        return ChartAxisKind.Category;
    }

    private static ChartAxisKind ResolveDefaultVerticalAxisKind(ChartModel model)
    {
        if (model.Type == ChartType.Scatter || model.Type == ChartType.Bubble)
        {
            return ChartAxisKind.Value;
        }

        if (model.Type == ChartType.Bar && model.BarDirection == ChartBarDirection.Bar)
        {
            return ChartAxisKind.Category;
        }

        return ChartAxisKind.Value;
    }

    private static (double min, double max) ComputeValueRange(
        ChartModel model,
        bool useXValues,
        bool useStacking,
        bool percentStacking,
        int categoryCount)
    {
        if (useXValues)
        {
            var min = double.MaxValue;
            var max = double.MinValue;
            for (var seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                for (var i = 0; i < series.Points.Count; i++)
                {
                    var point = series.Points[i];
                    var value = point.XValue ?? i;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            if (min == double.MaxValue || max == double.MinValue)
            {
                return (0d, 1d);
            }

            if (Math.Abs(max - min) < double.Epsilon)
            {
                max = min + 1d;
            }

            return (min, max);
        }

        if (percentStacking)
        {
            return (0d, 1d);
        }

        if (useStacking && categoryCount > 0)
        {
            var totals = ArrayPool<double>.Shared.Rent(categoryCount);
            try
            {
                totals.AsSpan(0, categoryCount).Clear();
                for (var seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
                {
                    var series = model.Series[seriesIndex];
                    for (var i = 0; i < series.Points.Count; i++)
                    {
                        totals[i] += Math.Max(0d, series.Points[i].Value);
                    }
                }

                var max = 0d;
                for (var i = 0; i < categoryCount; i++)
                {
                    max = Math.Max(max, totals[i]);
                }

                if (max <= 0d)
                {
                    max = 1d;
                }

                return (0d, max);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(totals);
            }
        }

        var maxValue = 0d;
        for (var seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            for (var i = 0; i < series.Points.Count; i++)
            {
                maxValue = Math.Max(maxValue, series.Points[i].Value);
            }
        }

        if (maxValue <= 0d)
        {
            maxValue = 1d;
        }

        return (0d, maxValue);
    }

    private static ChartValueScale ComputeAxisScale(ChartAxis axis, double min, double max, bool ignoreAxisMinMax = false)
    {
        if (!ignoreAxisMinMax && axis.Minimum.HasValue)
        {
            min = axis.Minimum.Value;
        }

        if (!ignoreAxisMinMax && axis.Maximum.HasValue)
        {
            max = axis.Maximum.Value;
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            max = min + 1d;
        }

        var majorUnit = axis.MajorUnit ?? ComputeNiceStep(max - min, 5);
        if (majorUnit <= 0d)
        {
            majorUnit = 1d;
        }

        var tickCount = (max - min) / majorUnit;
        while (tickCount > 12d)
        {
            majorUnit *= 2d;
            tickCount = (max - min) / majorUnit;
        }

        var minorUnit = axis.MinorUnit ?? majorUnit / 5d;
        if (!ignoreAxisMinMax && !axis.Minimum.HasValue)
        {
            min = Math.Floor(min / majorUnit) * majorUnit;
        }

        if (!ignoreAxisMinMax && !axis.Maximum.HasValue)
        {
            max = Math.Ceiling(max / majorUnit) * majorUnit;
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            max = min + majorUnit;
        }

        return new ChartValueScale(min, max, majorUnit, minorUnit);
    }

    private static double ComputeNiceStep(double range, int targetTicks)
    {
        if (range <= 0d)
        {
            return 1d;
        }

        var roughStep = range / Math.Max(1, targetTicks);
        var exponent = Math.Floor(Math.Log10(roughStep));
        var fraction = roughStep / Math.Pow(10d, exponent);
        var niceFraction = fraction <= 1d ? 1d : fraction <= 2d ? 2d : fraction <= 5d ? 5d : 10d;
        return niceFraction * Math.Pow(10d, exponent);
    }

    private static double[] BuildTickValues(ChartValueScale scale)
    {
        var range = scale.Max - scale.Min;
        if (range <= 0d || scale.MajorUnit <= 0d)
        {
            return Array.Empty<double>();
        }

        var count = (int)Math.Floor(range / scale.MajorUnit + 0.5d) + 1;
        count = Math.Clamp(count, 2, 64);
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = scale.Min + i * scale.MajorUnit;
        }

        return values;
    }

    private static string[] BuildValueLabels(double[] tickValues, string? format)
    {
        if (tickValues.Length == 0)
        {
            return Array.Empty<string>();
        }

        var labels = new string[tickValues.Length];
        for (var i = 0; i < tickValues.Length; i++)
        {
            labels[i] = FormatChartValue(tickValues[i], format);
        }

        return labels;
    }

    private static float MeasureAxisLabelExtent(SKPaint paint, string[] labels, bool isHorizontal)
    {
        if (labels.Length == 0)
        {
            return 0f;
        }

        if (isHorizontal)
        {
            return paint.TextSize;
        }

        var maxWidth = 0f;
        foreach (var label in labels)
        {
            maxWidth = MathF.Max(maxWidth, paint.MeasureText(label));
        }

        return maxWidth;
    }

    private static string[] BuildCategoryLabels(ChartModel model)
    {
        var count = 0;
        foreach (var series in model.Series)
        {
            count = Math.Max(count, series.Points.Count);
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            string? label = null;
            foreach (var series in model.Series)
            {
                if (i < series.Points.Count && !string.IsNullOrWhiteSpace(series.Points[i].Category))
                {
                    label = series.Points[i].Category;
                    break;
                }
            }

            labels[i] = label ?? (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return labels;
    }

    private static List<string> BuildTreemapLabelLines(ChartHierarchyNode node, SKPaint paint, float availableWidth)
    {
        var lines = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(node.Label))
        {
            var label = TrimTextToWidth(node.Label, paint, availableWidth);
            if (!string.IsNullOrWhiteSpace(label))
            {
                lines.Add(label);
            }
        }

        var showValue = node.DataLabel?.ShowValue != false;
        var valueText = showValue ? FormatChartValue(node.Value, node.DataLabel?.NumberFormat) : null;
        if (!string.IsNullOrWhiteSpace(valueText) && paint.MeasureText(valueText) <= availableWidth)
        {
            lines.Add(valueText);
        }

        return lines;
    }

    private static string TrimTextToWidth(string text, SKPaint paint, float availableWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || paint.MeasureText(text) <= availableWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + ellipsis;
            if (paint.MeasureText(candidate) <= availableWidth)
            {
                return candidate;
            }
        }

        return ellipsis;
    }

    private static DocColor ResolveHierarchyNodeColor(ChartModel model, ChartHierarchyNode node, IReadOnlyList<int> path, RenderOptions options)
    {
        if (TryResolveFillColor(node.Style, options.PlaceholderFillColor, false, out var explicitFill))
        {
            return explicitFill;
        }

        var rootIndex = path.Count == 0 ? 0 : path[0];
        var baseColor = ResolveChartPaletteColor(model, rootIndex);
        if (path.Count <= 1)
        {
            return baseColor;
        }

        var factor = MathF.Min(0.45f, (path.Count - 1) * 0.12f);
        return TintColor(baseColor, factor);
    }

    private static DocColor TintColor(DocColor color, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new DocColor(
            (byte)(color.R + (255 - color.R) * factor),
            (byte)(color.G + (255 - color.G) * factor),
            (byte)(color.B + (255 - color.B) * factor),
            color.A);
    }

    private static DocColor GetContrastingTextColor(DocColor fill)
    {
        var luma = (fill.R * 299 + fill.G * 587 + fill.B * 114) / 1000;
        return luma >= 150 ? DocColor.Black : DocColor.White;
    }

    private static int GetHierarchyDepth(IReadOnlyList<ChartHierarchyNode> nodes)
    {
        var maxDepth = 0;
        for (var index = 0; index < nodes.Count; index++)
        {
            maxDepth = Math.Max(maxDepth, GetHierarchyDepth(nodes[index]));
        }

        return maxDepth;
    }

    private static int GetHierarchyDepth(ChartHierarchyNode node)
    {
        var maxDepth = 1;
        for (var index = 0; index < node.Children.Count; index++)
        {
            maxDepth = Math.Max(maxDepth, 1 + GetHierarchyDepth(node.Children[index]));
        }

        return maxDepth;
    }

    private static int[] AppendHierarchyPath(IReadOnlyList<int> path, int index)
    {
        var result = new int[path.Count + 1];
        for (var pathIndex = 0; pathIndex < path.Count; pathIndex++)
        {
            result[pathIndex] = path[pathIndex];
        }

        result[^1] = index;
        return result;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private static float GetCategoryPosition(ChartModel model, SKRect plotRect, int index, int count, bool isHorizontal)
    {
        if (count <= 1)
        {
            return isHorizontal ? plotRect.MidX : plotRect.MidY;
        }

        if (model.Type == ChartType.Bar)
        {
            var useGrouped = (model.BarDirection == ChartBarDirection.Column && isHorizontal)
                             || (model.BarDirection == ChartBarDirection.Bar && !isHorizontal);
            if (useGrouped)
            {
                var size = isHorizontal ? plotRect.Width : plotRect.Height;
                var start = isHorizontal ? plotRect.Left : plotRect.Top;
                var group = size / count;
                return start + group * (index + 0.5f);
            }
        }

        var span = isHorizontal ? plotRect.Width : plotRect.Height;
        var startPos = isHorizontal ? plotRect.Left : plotRect.Top;
        if (model.Type == ChartType.Line || model.Type == ChartType.Area)
        {
            var bucket = span / count;
            return startPos + bucket * (index + 0.5f);
        }

        var step = span / (count - 1);
        return startPos + index * step;
    }

    private static void AppendLineSeriesPath(SKPath path, SKPoint[] points, int count, bool useSmoothCurve)
    {
        if (count <= 0)
        {
            return;
        }

        path.MoveTo(points[0]);
        if (!useSmoothCurve || count < 3)
        {
            for (var i = 1; i < count; i++)
            {
                path.LineTo(points[i]);
            }

            return;
        }

        for (var i = 0; i < count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < count ? points[i + 2] : points[i + 1];

            var c1 = new SKPoint(
                p1.X + ((p2.X - p0.X) / 6f),
                p1.Y + ((p2.Y - p0.Y) / 6f));
            var c2 = new SKPoint(
                p2.X - ((p3.X - p1.X) / 6f),
                p2.Y - ((p3.Y - p1.Y) / 6f));
            path.CubicTo(c1, c2, p2);
        }
    }

    private static void DrawCartesianAxes(SKCanvas canvas, SKRect plotRect, ChartAxisLayout? horizontalAxis, ChartAxisLayout? verticalAxis, RenderOptions options)
    {
        if (horizontalAxis?.IsValueAxis == true)
        {
            DrawAxisGridlines(canvas, plotRect, horizontalAxis, options);
        }

        if (verticalAxis?.IsValueAxis == true)
        {
            DrawAxisGridlines(canvas, plotRect, verticalAxis, options);
        }

        if (horizontalAxis is not null)
        {
            DrawAxisElements(canvas, plotRect, horizontalAxis, options);
        }

        if (verticalAxis is not null)
        {
            DrawAxisElements(canvas, plotRect, verticalAxis, options);
        }
    }

    private static void DrawAxisGridlines(SKCanvas canvas, SKRect plotRect, ChartAxisLayout axisLayout, RenderOptions options)
    {
        if (!axisLayout.IsValueAxis || axisLayout.Scale is null || axisLayout.TickValues is null)
        {
            return;
        }

        var gridStyle = axisLayout.Axis.MajorGridlineStyle;
        if (!TryResolveAxisLineStyle(gridStyle, options.GridlineColor, 1f, out var lineColor, out var lineWidth, out var lineStyle))
        {
            return;
        }

        using var paint = CreateLinePaint(lineColor, lineWidth, lineStyle);
        if (axisLayout.IsHorizontal)
        {
            foreach (var tick in axisLayout.TickValues)
            {
                var x = MapValueToX(tick, axisLayout.Scale.Value, plotRect);
                canvas.DrawLine(x, plotRect.Top, x, plotRect.Bottom, paint);
            }
        }
        else
        {
            foreach (var tick in axisLayout.TickValues)
            {
                var y = MapValueToY(tick, axisLayout.Scale.Value, plotRect);
                canvas.DrawLine(plotRect.Left, y, plotRect.Right, y, paint);
            }
        }
    }

    private static void DrawAxisElements(SKCanvas canvas, SKRect plotRect, ChartAxisLayout axisLayout, RenderOptions options)
    {
        var axis = axisLayout.Axis;
        if (!axis.IsVisible)
        {
            return;
        }

        var axisColor = options.PlaceholderStrokeColor;
        if (!TryResolveAxisLineStyle(axis.LineStyle, axisColor, 1f, out var lineColor, out var lineWidth, out var lineStyle))
        {
            lineColor = axisColor;
            lineWidth = 1f;
            lineStyle = DocBorderStyle.Single;
        }

        using var axisPaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
        var tickLength = axisLayout.TickLength;
        var labelPadding = 2f;

        if (axisLayout.IsHorizontal)
        {
            var axisY = axis.Position == ChartAxisPosition.Top ? plotRect.Top : plotRect.Bottom;
            canvas.DrawLine(plotRect.Left, axisY, plotRect.Right, axisY, axisPaint);

            var tickDir = axis.Position == ChartAxisPosition.Top ? -1f : 1f;
            var labelInside = axis.TickLabelPosition == ChartTickLabelPosition.High;
            var labelDir = labelInside ? -tickDir : tickDir;

            using var labelPaint = CreateTextPaint(axis.LabelTextStyle, options.TextColor, axisLayout.LabelTextSize, SKTextAlign.Center);
            if (axisLayout.IsValueAxis && axisLayout.TickValues is not null && axisLayout.Scale is not null)
            {
                for (var i = 0; i < axisLayout.TickValues.Length; i++)
                {
                    var x = MapValueToX(axisLayout.TickValues[i], axisLayout.Scale.Value, plotRect);
                    DrawAxisTick(canvas, axis.MajorTickMark, axisY, x, tickDir, tickLength, true, axisPaint);
                    if (axis.TickLabelPosition != ChartTickLabelPosition.None && i < axisLayout.Labels.Length)
                    {
                        var labelY = axisY + labelDir * (tickLength + labelPadding + labelPaint.TextSize);
                        canvas.DrawText(axisLayout.Labels[i], x, labelY, labelPaint);
                    }
                }
            }
            else
            {
                for (var i = 0; i < axisLayout.Labels.Length; i++)
                {
                    var x = GetCategoryPosition(axisLayout.Model, plotRect, i, axisLayout.Labels.Length, true);
                    DrawAxisTick(canvas, axis.MajorTickMark, axisY, x, tickDir, tickLength, true, axisPaint);
                    if (axis.TickLabelPosition != ChartTickLabelPosition.None)
                    {
                        var labelY = axisY + labelDir * (tickLength + labelPadding + labelPaint.TextSize);
                        canvas.DrawText(axisLayout.Labels[i], x, labelY, labelPaint);
                    }
                }
            }

            DrawAxisTitle(canvas, axisLayout, plotRect, axisY, tickDir, options);
        }
        else
        {
            var axisX = axis.Position == ChartAxisPosition.Right ? plotRect.Right : plotRect.Left;
            canvas.DrawLine(axisX, plotRect.Top, axisX, plotRect.Bottom, axisPaint);

            var tickDir = axis.Position == ChartAxisPosition.Right ? 1f : -1f;
            var labelInside = axis.TickLabelPosition == ChartTickLabelPosition.High;
            var labelDir = labelInside ? -tickDir : tickDir;

            var align = axis.Position == ChartAxisPosition.Right ? SKTextAlign.Left : SKTextAlign.Right;
            using var labelPaint = CreateTextPaint(axis.LabelTextStyle, options.TextColor, axisLayout.LabelTextSize, align);

            if (axisLayout.IsValueAxis && axisLayout.TickValues is not null && axisLayout.Scale is not null)
            {
                for (var i = 0; i < axisLayout.TickValues.Length; i++)
                {
                    var y = MapValueToY(axisLayout.TickValues[i], axisLayout.Scale.Value, plotRect);
                    DrawAxisTick(canvas, axis.MajorTickMark, axisX, y, tickDir, tickLength, false, axisPaint);
                    if (axis.TickLabelPosition != ChartTickLabelPosition.None && i < axisLayout.Labels.Length)
                    {
                        var labelX = axisX + labelDir * (tickLength + labelPadding);
                        canvas.DrawText(axisLayout.Labels[i], labelX, y + labelPaint.TextSize * 0.35f, labelPaint);
                    }
                }
            }
            else
            {
                for (var i = 0; i < axisLayout.Labels.Length; i++)
                {
                    var y = GetCategoryPosition(axisLayout.Model, plotRect, i, axisLayout.Labels.Length, false);
                    DrawAxisTick(canvas, axis.MajorTickMark, axisX, y, tickDir, tickLength, false, axisPaint);
                    if (axis.TickLabelPosition != ChartTickLabelPosition.None)
                    {
                        var labelX = axisX + labelDir * (tickLength + labelPadding);
                        canvas.DrawText(axisLayout.Labels[i], labelX, y + labelPaint.TextSize * 0.35f, labelPaint);
                    }
                }
            }

            DrawAxisTitle(canvas, axisLayout, plotRect, axisX, tickDir, options);
        }
    }

    private static void DrawAxisTick(SKCanvas canvas, ChartTickMark tickMark, float axisPos, float tickPos, float direction, float length, bool horizontal, SKPaint paint)
    {
        if (tickMark == ChartTickMark.None || length <= 0f)
        {
            return;
        }

        var outside = tickMark == ChartTickMark.Outside || tickMark == ChartTickMark.Cross;
        var inside = tickMark == ChartTickMark.Inside || tickMark == ChartTickMark.Cross;

        if (horizontal)
        {
            if (outside)
            {
                canvas.DrawLine(tickPos, axisPos, tickPos, axisPos + direction * length, paint);
            }

            if (inside)
            {
                canvas.DrawLine(tickPos, axisPos, tickPos, axisPos - direction * length, paint);
            }
        }
        else
        {
            if (outside)
            {
                canvas.DrawLine(axisPos, tickPos, axisPos + direction * length, tickPos, paint);
            }

            if (inside)
            {
                canvas.DrawLine(axisPos, tickPos, axisPos - direction * length, tickPos, paint);
            }
        }
    }

    private static void DrawAxisTitle(SKCanvas canvas, ChartAxisLayout axisLayout, SKRect plotRect, float axisPos, float direction, RenderOptions options)
    {
        var axis = axisLayout.Axis;
        if (string.IsNullOrWhiteSpace(axis.Title))
        {
            return;
        }

        using var titlePaint = CreateTextPaint(axis.TitleTextStyle, options.TextColor, axisLayout.TitleTextSize, SKTextAlign.Center);
        var labelPadding = 2f;

        if (axisLayout.IsHorizontal)
        {
            var labelDir = axis.TickLabelPosition == ChartTickLabelPosition.High ? -direction : direction;
            var titleY = axisPos + labelDir * (axisLayout.TickLength + axisLayout.LabelExtent + labelPadding + titlePaint.TextSize);
            canvas.DrawText(axis.Title, plotRect.MidX, titleY, titlePaint);
        }
        else
        {
            var labelDir = axis.TickLabelPosition == ChartTickLabelPosition.High ? -direction : direction;
            var titleX = axisPos + labelDir * (axisLayout.TickLength + axisLayout.LabelExtent + labelPadding + titlePaint.TextSize);
            var centerY = plotRect.MidY;
            canvas.Save();
            canvas.Translate(titleX, centerY);
            canvas.RotateDegrees(axis.Position == ChartAxisPosition.Right ? 90f : -90f);
            canvas.DrawText(axis.Title, 0f, titlePaint.TextSize * 0.35f, titlePaint);
            canvas.Restore();
        }
    }

    private static void DrawRadarAxes(SKCanvas canvas, SKRect plotRect, ChartModel model, RenderOptions options)
    {
        var categories = BuildCategoryLabels(model);
        var pointCount = categories.Length;
        if (pointCount == 0)
        {
            return;
        }

        var valueAxis = model.Axes.FirstOrDefault(axis => axis.Kind == ChartAxisKind.Value) ?? new ChartAxis();
        var categoryAxis = model.Axes.FirstOrDefault(axis => axis.Kind == ChartAxisKind.Category) ?? valueAxis;
        var (min, max) = ComputeValueRange(model, false, false, false, pointCount);
        var scale = ComputeAxisScale(valueAxis, min, max);
        var tickValues = BuildTickValues(scale);

        var centerX = plotRect.MidX;
        var centerY = plotRect.MidY;
        var radius = MathF.Min(plotRect.Width, plotRect.Height) / 2f;
        var angleStep = MathF.PI * 2f / pointCount;

        if (TryResolveAxisLineStyle(valueAxis.MajorGridlineStyle, options.GridlineColor, 1f, out var gridColor, out var gridWidth, out var gridStyle))
        {
            using var gridPaint = CreateLinePaint(gridColor, gridWidth, gridStyle);
            foreach (var tick in tickValues)
            {
                var ratio = (float)((tick - scale.Min) / (scale.Max - scale.Min));
                if (ratio <= 0f)
                {
                    continue;
                }

                var ringRadius = radius * ratio;
                using var path = new SKPath();
                for (var i = 0; i < pointCount; i++)
                {
                    var angle = -MathF.PI / 2f + i * angleStep;
                    var x = centerX + MathF.Cos(angle) * ringRadius;
                    var y = centerY + MathF.Sin(angle) * ringRadius;
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
                canvas.DrawPath(path, gridPaint);
            }
        }

        if (TryResolveAxisLineStyle(valueAxis.LineStyle, options.PlaceholderStrokeColor, 1f, out var axisColor, out var axisWidth, out var axisStyle))
        {
            using var axisPaint = CreateLinePaint(axisColor, axisWidth, axisStyle);
            for (var i = 0; i < pointCount; i++)
            {
                var angle = -MathF.PI / 2f + i * angleStep;
                var x = centerX + MathF.Cos(angle) * radius;
                var y = centerY + MathF.Sin(angle) * radius;
                canvas.DrawLine(centerX, centerY, x, y, axisPaint);
            }
        }

        var labelSize = MathF.Max(8f, plotRect.Height * 0.06f);
        using var labelPaint = CreateTextPaint(categoryAxis.LabelTextStyle, options.TextColor, labelSize, SKTextAlign.Center);
        var labelOffset = labelPaint.TextSize * 0.6f;
        for (var i = 0; i < pointCount; i++)
        {
            var angle = -MathF.PI / 2f + i * angleStep;
            var x = centerX + MathF.Cos(angle) * (radius + labelOffset);
            var y = centerY + MathF.Sin(angle) * (radius + labelOffset);
            canvas.DrawText(categories[i], x, y + labelPaint.TextSize * 0.35f, labelPaint);
        }
    }

    private static ChartDataLabelSettings? ResolveDataLabels(ChartModel model, ChartSeries series, ChartPoint point)
    {
        return point.DataLabel ?? series.DataLabels ?? model.DataLabels;
    }

    private static bool ShouldDrawDataLabel(ChartDataLabelSettings? settings)
    {
        if (settings is null || settings.IsHidden == true)
        {
            return false;
        }

        return settings.ShowValue == true
               || settings.ShowCategoryName == true
               || settings.ShowSeriesName == true
               || settings.ShowPercent == true
               || settings.ShowBubbleSize == true
               || settings.ShowLegendKey == true;
    }

    private static string? BuildDataLabelText(
        ChartDataLabelSettings settings,
        string? category,
        string? seriesName,
        double value,
        double? percent,
        double? bubbleSize)
    {
        var parts = new List<string>(4);
        if (settings.ShowSeriesName == true && !string.IsNullOrWhiteSpace(seriesName))
        {
            parts.Add(seriesName);
        }

        if (settings.ShowCategoryName == true && !string.IsNullOrWhiteSpace(category))
        {
            parts.Add(category);
        }

        if (settings.ShowValue == true)
        {
            parts.Add(FormatChartValue(value, settings.NumberFormat));
        }

        if (settings.ShowPercent == true && percent.HasValue)
        {
            parts.Add(FormatChartPercent(percent.Value, settings.NumberFormat));
        }

        if (settings.ShowBubbleSize == true && bubbleSize.HasValue)
        {
            parts.Add(FormatChartValue(bubbleSize.Value, settings.NumberFormat));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(", ", parts);
    }

    private static void DrawChartLabel(
        SKCanvas canvas,
        string text,
        SKPoint center,
        ChartDataLabelSettings settings,
        RenderOptions options,
        float defaultTextSize)
    {
        using var textPaint = CreateTextPaint(settings.TextStyle, options.TextColor, defaultTextSize, SKTextAlign.Center);
        var textWidth = textPaint.MeasureText(text);
        var textHeight = textPaint.TextSize;
        var padding = MathF.Max(2f, textPaint.TextSize * 0.2f);
        var rect = new SKRect(
            center.X - textWidth / 2f - padding,
            center.Y - textHeight / 2f - padding,
            center.X + textWidth / 2f + padding,
            center.Y + textHeight / 2f + padding);

        if (settings.ShapeStyle is not null)
        {
            if (TryResolveFillColor(settings.ShapeStyle, options.PlaceholderFillColor, false, out var fill))
            {
                using var fillPaint = CreateFillPaint(fill, settings.ShapeStyle.Effects?.Shadow);
                canvas.DrawRect(rect, fillPaint);
            }

            if (TryResolveLineStyle(settings.ShapeStyle, options.PlaceholderStrokeColor, 1f, false, out var lineColor, out var lineWidth, out var lineStyle))
            {
                using var linePaint = CreateLinePaint(lineColor, lineWidth, lineStyle);
                canvas.DrawRect(rect, linePaint);
            }
        }

        var textY = center.Y + textHeight * 0.35f;
        canvas.DrawText(text, center.X, textY, textPaint);
    }

    private static SKPoint ResolveBarLabelPosition(SKRect barRect, ChartDataLabelPosition position, bool isVertical)
    {
        var centerX = barRect.MidX;
        var centerY = barRect.MidY;
        var offset = 6f;

        if (position == ChartDataLabelPosition.BestFit)
        {
            position = ChartDataLabelPosition.OutsideEnd;
        }

        if (isVertical)
        {
            return position switch
            {
                ChartDataLabelPosition.Center => new SKPoint(centerX, centerY),
                ChartDataLabelPosition.InsideEnd => new SKPoint(centerX, barRect.Top + offset),
                ChartDataLabelPosition.InsideBase => new SKPoint(centerX, barRect.Bottom - offset),
                ChartDataLabelPosition.OutsideEnd => new SKPoint(centerX, barRect.Top - offset),
                ChartDataLabelPosition.Left => new SKPoint(barRect.Left - offset, centerY),
                ChartDataLabelPosition.Right => new SKPoint(barRect.Right + offset, centerY),
                ChartDataLabelPosition.Top => new SKPoint(centerX, barRect.Top - offset),
                ChartDataLabelPosition.Bottom => new SKPoint(centerX, barRect.Bottom + offset),
                _ => new SKPoint(centerX, centerY)
            };
        }

        return position switch
        {
            ChartDataLabelPosition.Center => new SKPoint(centerX, centerY),
            ChartDataLabelPosition.InsideEnd => new SKPoint(barRect.Right - offset, centerY),
            ChartDataLabelPosition.InsideBase => new SKPoint(barRect.Left + offset, centerY),
            ChartDataLabelPosition.OutsideEnd => new SKPoint(barRect.Right + offset, centerY),
            ChartDataLabelPosition.Left => new SKPoint(barRect.Left - offset, centerY),
            ChartDataLabelPosition.Right => new SKPoint(barRect.Right + offset, centerY),
            ChartDataLabelPosition.Top => new SKPoint(centerX, barRect.Top - offset),
            ChartDataLabelPosition.Bottom => new SKPoint(centerX, barRect.Bottom + offset),
            _ => new SKPoint(centerX, centerY)
        };
    }

    private static SKPoint ResolvePointLabelPosition(SKPoint point, ChartDataLabelPosition position)
    {
        var offset = 6f;
        if (position == ChartDataLabelPosition.BestFit)
        {
            position = ChartDataLabelPosition.OutsideEnd;
        }

        return position switch
        {
            ChartDataLabelPosition.Center => point,
            ChartDataLabelPosition.InsideEnd => new SKPoint(point.X, point.Y - offset),
            ChartDataLabelPosition.InsideBase => new SKPoint(point.X, point.Y + offset),
            ChartDataLabelPosition.OutsideEnd => new SKPoint(point.X, point.Y - offset),
            ChartDataLabelPosition.Left => new SKPoint(point.X - offset, point.Y),
            ChartDataLabelPosition.Right => new SKPoint(point.X + offset, point.Y),
            ChartDataLabelPosition.Top => new SKPoint(point.X, point.Y - offset),
            ChartDataLabelPosition.Bottom => new SKPoint(point.X, point.Y + offset),
            _ => point
        };
    }

    private static float MapValueToX(double value, ChartValueScale scale, SKRect rect)
    {
        var range = scale.Max - scale.Min;
        if (range <= 0d)
        {
            range = 1d;
        }

        return rect.Left + (float)((value - scale.Min) / range) * rect.Width;
    }

    private static float MapValueToY(double value, ChartValueScale scale, SKRect rect)
    {
        var range = scale.Max - scale.Min;
        if (range <= 0d)
        {
            range = 1d;
        }

        return rect.Bottom - (float)((value - scale.Min) / range) * rect.Height;
    }

    private static SKPaint CreateTextPaint(ChartTextStyle? style, DocColor fallbackColor, float fallbackSize, SKTextAlign align)
    {
        var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(style?.Color ?? fallbackColor),
            IsAntialias = true,
            TextAlign = align,
            TextSize = style?.FontSize ?? fallbackSize
        };

        var typeface = ResolveTypeface(style);
        if (typeface is not null)
        {
            paint.Typeface = typeface;
        }

        return paint;
    }

    private static SKTypeface? ResolveTypeface(ChartTextStyle? style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.FontFamily))
        {
            return null;
        }

        var weight = style.Bold == true ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.Italic == true ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, slant);
    }

    private static bool TryResolveAxisLineStyle(
        ChartLineStyle? style,
        DocColor fallback,
        float defaultWidth,
        out DocColor color,
        out float width,
        out DocBorderStyle lineStyle)
    {
        color = default;
        width = defaultWidth;
        lineStyle = DocBorderStyle.Single;

        if (style?.IsNone == true)
        {
            return false;
        }

        color = style?.Color ?? fallback;
        if (style?.Width.HasValue == true)
        {
            width = MathF.Max(0.5f, style.Width.Value);
        }

        if (style?.Style.HasValue == true)
        {
            lineStyle = style.Style.Value;
        }

        return true;
    }

    private static string FormatChartValue(double value, string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        var normalized = NormalizeNumberFormat(format);
        if (normalized.Contains("%", StringComparison.Ordinal))
        {
            var inner = normalized.Replace("%", string.Empty);
            var formatted = value * 100d;
            var pattern = string.IsNullOrWhiteSpace(inner) ? "0" : inner;
            try
            {
                return formatted.ToString(pattern, CultureInfo.InvariantCulture) + "%";
            }
            catch (FormatException)
            {
                return formatted.ToString("0", CultureInfo.InvariantCulture) + "%";
            }
        }

        try
        {
            return value.ToString(normalized, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private static string FormatChartPercent(double value, string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "0%" : NormalizeNumberFormat(format);
        if (!normalized.Contains("%", StringComparison.Ordinal))
        {
            normalized = "0%";
        }

        try
        {
            return value.ToString(normalized, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("0%", CultureInfo.InvariantCulture);
        }
    }

    private static string NormalizeNumberFormat(string format)
    {
        var sections = format.Split(';');
        var primary = sections.Length > 0 ? sections[0] : format;
        return primary.Replace("\"", string.Empty).Trim();
    }
}
