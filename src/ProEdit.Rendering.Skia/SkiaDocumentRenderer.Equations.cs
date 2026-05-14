using SkiaSharp;
using SkiaSharp.HarfBuzz;
using ProEdit.Documents;
using ProEdit.Layout;

namespace ProEdit.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private static void DrawEquation(
        SKCanvas canvas,
        LayoutEquation equation,
        float lineX,
        float lineBaseline,
        Func<TextStyle, SKPaint> paintProvider,
        Func<TextStyle, SKShaper?> shaperProvider)
    {
        var layout = equation.Layout;
        var originX = lineX + equation.X;
        var originY = lineBaseline - layout.Baseline;
        DrawMathBox(canvas, layout.Root, originX, originY, paintProvider, shaperProvider);
    }

    private static void DrawMathBox(
        SKCanvas canvas,
        MathBox box,
        float x,
        float y,
        Func<TextStyle, SKPaint> paintProvider,
        Func<TextStyle, SKShaper?> shaperProvider)
    {
        if (box.IsHidden || box.Style?.Hidden == true)
        {
            return;
        }

        if (!string.IsNullOrEmpty(box.Text))
        {
            var style = box.Style ?? new TextStyle();
            var paint = paintProvider(style);
            var shaper = shaperProvider(style);
            var baseline = y + box.Baseline;
            if (shaper is null)
            {
                canvas.DrawText(box.Text, x, baseline, paint);
            }
            else
            {
                canvas.DrawShapedText(shaper, box.Text, x, baseline, paint);
            }
        }

        foreach (var child in box.Children)
        {
            DrawMathBox(canvas, child.Box, x + child.X, y + child.Y, paintProvider, shaperProvider);
        }

        switch (box.Element)
        {
            case MathFraction fraction when fraction.HasBar:
                DrawFractionBar(canvas, box, x, y, paintProvider);
                break;
            case MathRadical:
                DrawRadical(canvas, box, x, y, paintProvider);
                break;
            case MathBar bar:
                DrawBar(canvas, box, bar, x, y, paintProvider);
                break;
            case MathBorderBox border:
                DrawBorderBox(canvas, box, border, x, y, paintProvider);
                break;
        }
    }

    private static void DrawFractionBar(SKCanvas canvas, MathBox box, float x, float y, Func<TextStyle, SKPaint> paintProvider)
    {
        if (box.Children.Count < 2)
        {
            return;
        }

        var style = box.Style ?? new TextStyle();
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.FractionGapScale);
        var thickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.FractionBarScale);

        var numerator = box.Children[0];
        var lineY = y + numerator.Y + numerator.Box.Height + gap + thickness * 0.5f;
        var left = x;
        var right = x + box.Width;

        using var paint = CreateMathStrokePaint(paintProvider, style, thickness);
        canvas.DrawLine(left, lineY, right, lineY, paint);
    }

    private static void DrawRadical(SKCanvas canvas, MathBox box, float x, float y, Func<TextStyle, SKPaint> paintProvider)
    {
        if (box.Children.Count == 0)
        {
            return;
        }

        var style = box.Style ?? new TextStyle();
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.RadicalGapScale);
        var symbolWidth = MathF.Max(1f, style.FontSize * MathLayoutMetrics.RadicalWidthScale);
        var thickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.RadicalStrokeScale);

        var radicand = box.Children[0];
        var radicandTop = y + radicand.Y;
        var radicandBottom = radicandTop + radicand.Box.Height;

        var barY = radicandTop;
        var leftX = x + symbolWidth * 0.1f;
        var midX = x + symbolWidth * 0.4f;
        var tipX = x + symbolWidth;
        var midY = radicandBottom - MathF.Max(1f, gap * 0.5f);

        using var paint = CreateMathStrokePaint(paintProvider, style, thickness);
        using var path = new SKPath();
        path.MoveTo(leftX, midY);
        path.LineTo(midX, radicandBottom);
        path.LineTo(tipX, barY);
        canvas.DrawPath(path, paint);
        canvas.DrawLine(tipX, barY, x + box.Width, barY, paint);
    }

    private static void DrawBar(
        SKCanvas canvas,
        MathBox box,
        MathBar bar,
        float x,
        float y,
        Func<TextStyle, SKPaint> paintProvider)
    {
        if (box.Width <= 0f || box.Height <= 0f)
        {
            return;
        }

        var style = box.Style ?? new TextStyle();
        var thickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BarThicknessScale);
        var half = thickness * 0.5f;
        var lineY = bar.Position == MathBarPosition.Bottom
            ? y + box.Height - half
            : y + half;

        using var paint = CreateMathStrokePaint(paintProvider, style, thickness);
        canvas.DrawLine(x, lineY, x + box.Width, lineY, paint);
    }

    private static void DrawBorderBox(
        SKCanvas canvas,
        MathBox box,
        MathBorderBox border,
        float x,
        float y,
        Func<TextStyle, SKPaint> paintProvider)
    {
        if (box.Width <= 0f || box.Height <= 0f)
        {
            return;
        }

        var style = box.Style ?? new TextStyle();
        var thickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BorderThicknessScale);
        var half = thickness * 0.5f;
        var left = x + half;
        var right = x + box.Width - half;
        var top = y + half;
        var bottom = y + box.Height - half;
        var midX = (left + right) * 0.5f;
        var midY = (top + bottom) * 0.5f;

        using var paint = CreateMathStrokePaint(paintProvider, style, thickness);
        if (!border.HideTop)
        {
            canvas.DrawLine(left, top, right, top, paint);
        }

        if (!border.HideBottom)
        {
            canvas.DrawLine(left, bottom, right, bottom, paint);
        }

        if (!border.HideLeft)
        {
            canvas.DrawLine(left, top, left, bottom, paint);
        }

        if (!border.HideRight)
        {
            canvas.DrawLine(right, top, right, bottom, paint);
        }

        if (border.StrikeHorizontal)
        {
            canvas.DrawLine(left, midY, right, midY, paint);
        }

        if (border.StrikeVertical)
        {
            canvas.DrawLine(midX, top, midX, bottom, paint);
        }

        if (border.StrikeDiagonalUp)
        {
            canvas.DrawLine(left, bottom, right, top, paint);
        }

        if (border.StrikeDiagonalDown)
        {
            canvas.DrawLine(left, top, right, bottom, paint);
        }
    }

    private static SKPaint CreateMathStrokePaint(Func<TextStyle, SKPaint> paintProvider, TextStyle style, float thickness)
    {
        var color = paintProvider(style).Color;
        return new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = thickness,
            IsAntialias = true
        };
    }
}
