using System.Text;
using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private void DrawShape(SKCanvas canvas, LayoutShape shapeLayout, float lineX, float baseline, float ascent, RenderOptions options, TextStyle defaultStyle)
    {
        var shape = shapeLayout.Shape;
        var width = shapeLayout.Width;
        var height = shapeLayout.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var originX = lineX + shapeLayout.X;
        var originY = baseline - height;
        var properties = shape.Properties;
        var kind = ResolveShapeKind(properties.PresetGeometry);
        var isLine = kind == ShapeKind.Line;
        var fillColor = properties.FillColor;
        var outline = properties.Outline;
        var hasFill = fillColor.HasValue && fillColor.Value.A > 0;
        var hasOutline = outline is not null && outline.IsVisible;
        var rect = new SKRect(0f, 0f, width, height);

        canvas.Save();
        canvas.Translate(originX, originY);
        ApplyShapeTransform(canvas, properties, width, height);

        var path = CreateShapePath(kind, rect);
        if (!hasFill && !hasOutline)
        {
            DrawShapePlaceholder(canvas, rect, options, "Shape");
        }
        else
        {
            if (hasFill && !isLine)
            {
                using var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(fillColor!.Value),
                    IsAntialias = true
                };
                canvas.DrawPath(path, fillPaint);
            }

            if (hasOutline)
            {
                var thickness = MathF.Max(0.5f, GetBorderThickness(outline!));
                using var strokePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = ToSkColor(outline!.Color),
                    StrokeWidth = thickness,
                    IsAntialias = true,
                    StrokeCap = outline.Style == DocBorderStyle.Dotted ? SKStrokeCap.Round : SKStrokeCap.Butt,
                    PathEffect = CreateBorderEffect(outline.Style, thickness)
                };
                canvas.DrawPath(path, strokePaint);
            }
        }

        if (shape.TextBox is { Blocks.Count: > 0 })
        {
            DrawShapeText(canvas, shape.TextBox, rect, options, defaultStyle, TypefaceResolver);
        }

        canvas.Restore();
    }

    private static void ApplyShapeTransform(SKCanvas canvas, ShapeProperties properties, float width, float height)
    {
        if (!properties.FlipHorizontal && !properties.FlipVertical && MathF.Abs(properties.Rotation) < 0.01f)
        {
            return;
        }

        var centerX = width / 2f;
        var centerY = height / 2f;
        canvas.Translate(centerX, centerY);
        if (properties.FlipHorizontal || properties.FlipVertical)
        {
            var scaleX = properties.FlipHorizontal ? -1f : 1f;
            var scaleY = properties.FlipVertical ? -1f : 1f;
            canvas.Scale(scaleX, scaleY);
        }

        if (MathF.Abs(properties.Rotation) >= 0.01f)
        {
            canvas.RotateDegrees(properties.Rotation);
        }

        canvas.Translate(-centerX, -centerY);
    }

    private static void DrawShapePlaceholder(SKCanvas canvas, SKRect rect, RenderOptions options, string label)
    {
        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PlaceholderFillColor),
            IsAntialias = true
        };
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PlaceholderStrokeColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PlaceholderTextColor),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        canvas.DrawRect(rect, fillPaint);
        canvas.DrawRect(rect, borderPaint);
        if (!string.IsNullOrWhiteSpace(label))
        {
            textPaint.TextSize = MathF.Max(8f, MathF.Min(14f, rect.Height / 4f));
            var textY = rect.MidY + textPaint.TextSize * 0.35f;
            canvas.DrawText(label, rect.MidX, textY, textPaint);
        }
    }

    private static void DrawShapeText(
        SKCanvas canvas,
        ShapeTextBox textBox,
        SKRect bounds,
        RenderOptions options,
        TextStyle defaultStyle,
        ISkiaTypefaceResolver? typefaceResolver)
    {
        var padding = textBox.Properties.Padding;
        var left = bounds.Left + padding.Left;
        var top = bounds.Top + padding.Top;
        var right = bounds.Right - padding.Right;
        var bottom = bounds.Bottom - padding.Bottom;
        var width = right - left;
        var height = bottom - top;
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        var textStyle = defaultStyle.Clone();
        textStyle.Color = options.TextColor;
        using var paint = SkiaTextMeasurer.CreatePaint(textStyle, typefaceResolver);
        paint.Color = ToSkColor(options.TextColor);

        var metrics = paint.FontMetrics;
        var ascent = MathF.Max(1f, -metrics.Ascent);
        var descent = MathF.Max(0f, metrics.Descent);
        var lineHeight = ascent + descent;

        var lines = BuildShapeTextLines(textBox.Blocks, width, paint);
        if (lines.Count == 0)
        {
            return;
        }

        var totalHeight = lineHeight * lines.Count;
        var startY = top;
        if (totalHeight < height)
        {
            startY = textBox.Properties.VerticalAlignment switch
            {
                ShapeTextVerticalAlignment.Center => top + (height - totalHeight) / 2f,
                ShapeTextVerticalAlignment.Bottom => top + (height - totalHeight),
                _ => top
            };
        }

        canvas.Save();
        canvas.ClipRect(new SKRect(left, top, right, bottom));

        var y = startY;
        foreach (var line in lines)
        {
            var baseline = y + ascent;
            var lineWidth = paint.MeasureText(line.Text);
            var x = left;
            if (line.Alignment == ParagraphAlignment.Center)
            {
                x = left + (width - lineWidth) / 2f;
            }
            else if (line.Alignment == ParagraphAlignment.Right)
            {
                x = left + (width - lineWidth);
            }

            canvas.DrawText(line.Text, x, baseline, paint);
            y += lineHeight;
            if (y > bottom)
            {
                break;
            }
        }

        canvas.Restore();
    }

    private static List<ShapeTextLine> BuildShapeTextLines(IReadOnlyList<Block> blocks, float maxWidth, SKPaint paint)
    {
        var lines = new List<ShapeTextLine>();
        if (blocks.Count == 0 || maxWidth <= 1f)
        {
            return lines;
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not ParagraphBlock paragraph)
            {
                continue;
            }

            var alignment = paragraph.Properties.Alignment;
            var text = GetParagraphText(paragraph);
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(new ShapeTextLine(string.Empty, alignment));
            }
            else
            {
                var paragraphLines = text.Split('\n');
                for (var p = 0; p < paragraphLines.Length; p++)
                {
                    foreach (var line in WrapShapeText(paragraphLines[p], maxWidth, paint))
                    {
                        lines.Add(new ShapeTextLine(line, alignment));
                    }
                }
            }

            if (index < blocks.Count - 1)
            {
                lines.Add(new ShapeTextLine(string.Empty, alignment));
            }
        }

        return lines;
    }

    private static IEnumerable<string> WrapShapeText(string text, float maxWidth, SKPaint paint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return string.Empty;
            yield break;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        foreach (var word in words)
        {
            var candidate = builder.Length == 0 ? word : $"{builder} {word}";
            var width = paint.MeasureText(candidate);
            if (width <= maxWidth || builder.Length == 0)
            {
                builder.Clear();
                builder.Append(candidate);
            }
            else
            {
                yield return builder.ToString();
                builder.Clear();
                builder.Append(word);
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (!string.IsNullOrEmpty(paragraph.Text))
        {
            return paragraph.Text;
        }

        if (paragraph.Inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString();
    }

    private static ShapeKind ResolveShapeKind(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return ShapeKind.Rectangle;
        }

        var value = preset.Trim();
        return value.ToLowerInvariant() switch
        {
            "rect" or "rectangle" => ShapeKind.Rectangle,
            "roundrect" or "roundrectangle" => ShapeKind.RoundRectangle,
            "ellipse" or "oval" => ShapeKind.Ellipse,
            "line" => ShapeKind.Line,
            "triangle" => ShapeKind.Triangle,
            "diamond" => ShapeKind.Diamond,
            "parallelogram" => ShapeKind.Parallelogram,
            "trapezoid" => ShapeKind.Trapezoid,
            "hexagon" => ShapeKind.Hexagon,
            "octagon" => ShapeKind.Octagon,
            _ => ShapeKind.Unknown
        };
    }

    private static SKPath CreateShapePath(ShapeKind kind, SKRect rect)
    {
        var path = new SKPath();
        var width = rect.Width;
        var height = rect.Height;
        switch (kind)
        {
            case ShapeKind.Rectangle:
                path.AddRect(rect);
                break;
            case ShapeKind.RoundRectangle:
            {
                var radius = MathF.Min(width, height) * 0.12f;
                path.AddRoundRect(rect, radius, radius);
                break;
            }
            case ShapeKind.Ellipse:
                path.AddOval(rect);
                break;
            case ShapeKind.Line:
                path.MoveTo(rect.Left, rect.MidY);
                path.LineTo(rect.Right, rect.MidY);
                break;
            case ShapeKind.Triangle:
                path.MoveTo(rect.MidX, rect.Top);
                path.LineTo(rect.Right, rect.Bottom);
                path.LineTo(rect.Left, rect.Bottom);
                path.Close();
                break;
            case ShapeKind.Diamond:
                path.MoveTo(rect.MidX, rect.Top);
                path.LineTo(rect.Right, rect.MidY);
                path.LineTo(rect.MidX, rect.Bottom);
                path.LineTo(rect.Left, rect.MidY);
                path.Close();
                break;
            case ShapeKind.Parallelogram:
            {
                var offset = width * 0.2f;
                path.MoveTo(rect.Left + offset, rect.Top);
                path.LineTo(rect.Right, rect.Top);
                path.LineTo(rect.Right - offset, rect.Bottom);
                path.LineTo(rect.Left, rect.Bottom);
                path.Close();
                break;
            }
            case ShapeKind.Trapezoid:
            {
                var inset = width * 0.2f;
                path.MoveTo(rect.Left + inset, rect.Top);
                path.LineTo(rect.Right - inset, rect.Top);
                path.LineTo(rect.Right, rect.Bottom);
                path.LineTo(rect.Left, rect.Bottom);
                path.Close();
                break;
            }
            case ShapeKind.Hexagon:
            {
                var dx = width * 0.2f;
                path.MoveTo(rect.Left + dx, rect.Top);
                path.LineTo(rect.Right - dx, rect.Top);
                path.LineTo(rect.Right, rect.MidY);
                path.LineTo(rect.Right - dx, rect.Bottom);
                path.LineTo(rect.Left + dx, rect.Bottom);
                path.LineTo(rect.Left, rect.MidY);
                path.Close();
                break;
            }
            case ShapeKind.Octagon:
            {
                var dx = width * 0.2f;
                var dy = height * 0.2f;
                path.MoveTo(rect.Left + dx, rect.Top);
                path.LineTo(rect.Right - dx, rect.Top);
                path.LineTo(rect.Right, rect.Top + dy);
                path.LineTo(rect.Right, rect.Bottom - dy);
                path.LineTo(rect.Right - dx, rect.Bottom);
                path.LineTo(rect.Left + dx, rect.Bottom);
                path.LineTo(rect.Left, rect.Bottom - dy);
                path.LineTo(rect.Left, rect.Top + dy);
                path.Close();
                break;
            }
            default:
                path.AddRect(rect);
                break;
        }

        return path;
    }

    private sealed record ShapeTextLine(string Text, ParagraphAlignment? Alignment);

    private enum ShapeKind
    {
        Unknown,
        Rectangle,
        RoundRectangle,
        Ellipse,
        Line,
        Triangle,
        Diamond,
        Parallelogram,
        Trapezoid,
        Hexagon,
        Octagon
    }
}
