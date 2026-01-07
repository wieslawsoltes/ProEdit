using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
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
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PlaceholderStrokeColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PlaceholderFillColor),
            IsAntialias = true
        };

        canvas.DrawRect(rect, fillPaint);
        canvas.DrawRect(rect, borderPaint);

        var model = chart.Model;
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

        switch (model.Type)
        {
            case ChartType.Pie:
                DrawPieChart(canvas, plotRect, model);
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
        var categoryCount = model.Series.Max(series => series.Points.Count);
        if (categoryCount == 0)
        {
            return;
        }

        var maxValue = model.Series
            .SelectMany(series => series.Points)
            .Max(point => point.Value);
        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        var groupWidth = plotRect.Width / categoryCount;
        var barWidth = groupWidth / Math.Max(1, seriesCount);
        var gap = barWidth * 0.15f;

        for (var categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            var groupStart = plotRect.Left + categoryIndex * groupWidth;
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                var value = categoryIndex < series.Points.Count ? series.Points[categoryIndex].Value : 0d;
                var height = (float)(value / maxValue) * plotRect.Height;
                var barLeft = groupStart + seriesIndex * barWidth + gap;
                var barRight = barLeft + barWidth - gap * 2f;
                var barTop = plotRect.Bottom - height;

                using var barPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = ResolveChartColor(seriesIndex),
                    IsAntialias = true
                };
                canvas.DrawRect(new SKRect(barLeft, barTop, barRight, plotRect.Bottom), barPaint);
            }
        }
    }

    private static void DrawLineChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var maxValue = model.Series
            .SelectMany(series => series.Points)
            .DefaultIfEmpty(new ChartPoint { Value = 1 })
            .Max(point => point.Value);
        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        for (var seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
        {
            var series = model.Series[seriesIndex];
            if (series.Points.Count == 0)
            {
                continue;
            }

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ResolveChartColor(seriesIndex),
                StrokeWidth = 2f,
                IsAntialias = true
            };

            using var path = new SKPath();
            for (var i = 0; i < series.Points.Count; i++)
            {
                var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, series.Points.Count - 1));
                var y = plotRect.Bottom - (float)(series.Points[i].Value / maxValue) * plotRect.Height;
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
    }

    private static void DrawAreaChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var series = model.Series.FirstOrDefault();
        if (series is null || series.Points.Count == 0)
        {
            return;
        }

        var maxValue = series.Points.Max(point => point.Value);
        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ResolveChartColor(0).WithAlpha(160),
            IsAntialias = true
        };

        using var path = new SKPath();
        path.MoveTo(plotRect.Left, plotRect.Bottom);
        for (var i = 0; i < series.Points.Count; i++)
        {
            var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, series.Points.Count - 1));
            var y = plotRect.Bottom - (float)(series.Points[i].Value / maxValue) * plotRect.Height;
            path.LineTo(x, y);
        }
        path.LineTo(plotRect.Right, plotRect.Bottom);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static void DrawScatterChart(SKCanvas canvas, SKRect plotRect, ChartModel model)
    {
        var series = model.Series.FirstOrDefault();
        if (series is null || series.Points.Count == 0)
        {
            return;
        }

        var maxValue = series.Points.Max(point => point.Value);
        if (maxValue <= 0)
        {
            maxValue = 1;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ResolveChartColor(0),
            IsAntialias = true
        };

        for (var i = 0; i < series.Points.Count; i++)
        {
            var x = plotRect.Left + i * (plotRect.Width / Math.Max(1, series.Points.Count - 1));
            var y = plotRect.Bottom - (float)(series.Points[i].Value / maxValue) * plotRect.Height;
            canvas.DrawCircle(x, y, 3f, paint);
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
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ResolveChartColor(i),
                IsAntialias = true
            };

            using var path = new SKPath();
            path.MoveTo(centerX, centerY);
            path.ArcTo(new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius), startAngle, sweep, false);
            path.Close();
            canvas.DrawPath(path, paint);

            startAngle += sweep;
        }
    }

    private static SKColor ResolveChartColor(int index)
    {
        var palette = new[]
        {
            new SKColor(79, 129, 189),
            new SKColor(192, 80, 77),
            new SKColor(155, 187, 89),
            new SKColor(128, 100, 162),
            new SKColor(75, 172, 198),
            new SKColor(247, 150, 70)
        };

        return palette[index % palette.Length];
    }
}
