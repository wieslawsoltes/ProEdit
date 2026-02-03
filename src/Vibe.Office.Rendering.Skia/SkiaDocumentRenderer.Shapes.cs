using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private static readonly DocumentLayouter ShapeTextLayouter = new DocumentLayouter();
    private SkiaTextMeasurer? _shapeTextMeasurer;
    private readonly Dictionary<ShapeImageFill, SKBitmap> _shapeFillImageCache = new();
    private readonly HashSet<ShapeImageFill> _invalidShapeFillImages = new();
    private readonly Dictionary<PatternShaderKey, PatternShaderCacheEntry> _patternShaderCache = new();

    private void DrawShape(
        SKCanvas canvas,
        LayoutShape shapeLayout,
        float lineX,
        float baseline,
        float ascent,
        RenderOptions options,
        TextStyle defaultStyle,
        Document document,
        LayoutSettings layoutSettings)
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
        var fill = properties.Fill;
        var fillColor = properties.FillColor;
        var outline = properties.Outline;
        var rect = new SKRect(0f, 0f, width, height);
        var effects = properties.Effects;

        canvas.Save();
        canvas.Translate(originX, originY);
        ApplyShapeTransform(canvas, properties, width, height);

        var pathData = ShapeGeometryEvaluator.Evaluate(properties, width, height);
        if (pathData.Count == 0)
        {
            canvas.Restore();
            return;
        }

        var paths = new List<ShapePathEntry>(pathData.Count);
        var hasFillPath = false;
        var hasStrokePath = false;
        foreach (var data in pathData)
        {
            if (data.IsFilled)
            {
                hasFillPath = true;
            }

            if (data.IsStroked)
            {
                hasStrokePath = true;
            }

            paths.Add(new ShapePathEntry(data, CreateSkiaPath(data)));
        }

        try
        {
            ShapeTextBox? textBox = null;
            if (shape.TextBox is { Blocks.Count: > 0 } resolvedTextBox)
            {
                textBox = resolvedTextBox;
            }

            var hasText = textBox is not null;
            var hasFill = HasShapeFill(fill, fillColor) && hasFillPath;
            var hasOutline = outline is not null && outline.IsVisible && hasStrokePath;
            if (!hasFill && !hasOutline)
            {
                if (!hasText)
                {
                    DrawShapePlaceholder(canvas, rect, options, "Shape");
                }
            }
            else
            {
                if (effects?.HasValues == true)
                {
                    if (effects.Shadow is not null)
                    {
                        DrawShapeShadow(canvas, paths, fill, fillColor, outline, rect, effects.Shadow);
                    }

                    if (effects.Glow is not null)
                    {
                        DrawShapeGlow(canvas, paths, fill, fillColor, outline, rect, effects.Glow);
                    }

                    if (effects.SoftEdge is not null)
                    {
                        DrawShapeSoftEdge(canvas, paths, fill, fillColor, outline, rect, effects.SoftEdge);
                    }
                }

                DrawShapeGeometry(canvas, paths, fill, fillColor, outline, rect);
            }

            if (textBox is not null)
            {
                var textRect = ResolveShapeTextRect(properties, width, height);
                DrawShapeText(canvas, textBox, textRect, options, document, defaultStyle, layoutSettings, shape.Id);
            }

            if (effects?.Reflection is not null && (hasFill || hasOutline))
            {
                DrawShapeReflection(canvas, paths, rect, fill, fillColor, outline, effects.Reflection);
            }
        }
        finally
        {
            foreach (var entry in paths)
            {
                entry.Path.Dispose();
            }
        }

        canvas.Restore();
    }

    private void DrawShapeGeometry(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        SKRect bounds)
    {
        DrawShapePaths(canvas, paths, fill, fillColor, outline, bounds, null);
    }

    private void DrawShapeShadow(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        SKRect bounds,
        DrawingShadowEffect shadow)
    {
        var blurRadius = MathF.Max(0f, shadow.BlurRadius);
        var distance = MathF.Max(0f, shadow.Distance);
        if (blurRadius <= 0f && distance <= 0f)
        {
            return;
        }

        var angle = shadow.Direction * (MathF.PI / 180f);
        var dx = distance * MathF.Cos(angle);
        var dy = distance * MathF.Sin(angle);
        using var filter = SKImageFilter.CreateDropShadowOnly(dx, dy, blurRadius, blurRadius, ToSkColor(shadow.Color));
        DrawShapePaths(canvas, paths, fill, fillColor, outline, bounds, filter);
    }

    private void DrawShapeGlow(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        SKRect bounds,
        DrawingGlowEffect glow)
    {
        var blurRadius = MathF.Max(0f, glow.Radius);
        if (blurRadius <= 0f)
        {
            return;
        }

        using var filter = SKImageFilter.CreateDropShadowOnly(0f, 0f, blurRadius, blurRadius, ToSkColor(glow.Color));
        DrawShapePaths(canvas, paths, fill, fillColor, outline, bounds, filter);
    }

    private void DrawShapeSoftEdge(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        SKRect bounds,
        DrawingSoftEdgeEffect softEdge)
    {
        var blurRadius = MathF.Max(0f, softEdge.Radius);
        if (blurRadius <= 0f)
        {
            return;
        }

        using var filter = SKImageFilter.CreateBlur(blurRadius, blurRadius);
        DrawShapePaths(canvas, paths, fill, fillColor, outline, bounds, filter);
    }

    private void DrawShapeReflection(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        SKRect rect,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        DrawingReflectionEffect reflection)
    {
        var scaleX = reflection.ScaleX > 0f ? reflection.ScaleX : 1f;
        var scaleY = reflection.ScaleY > 0f ? reflection.ScaleY : 1f;
        var reflectionHeight = rect.Height * scaleY;
        if (reflectionHeight <= 0f)
        {
            return;
        }

        var reflectionTop = rect.Bottom + reflection.Distance;
        var reflectionRect = new SKRect(rect.Left, reflectionTop, rect.Left + rect.Width * scaleX, reflectionTop + reflectionHeight);
        using var layerPaint = new SKPaint();
        if (reflection.BlurRadius > 0f)
        {
            layerPaint.ImageFilter = SKImageFilter.CreateBlur(reflection.BlurRadius, reflection.BlurRadius);
        }

        canvas.SaveLayer(reflectionRect, layerPaint);

        canvas.Save();
        canvas.Scale(scaleX, -scaleY);
        var tx = rect.Left - rect.Left * scaleX;
        var ty = rect.Bottom + reflection.Distance + rect.Bottom * scaleY;
        canvas.Translate(tx, ty);
        DrawShapeGeometry(canvas, paths, fill, fillColor, outline, rect);
        canvas.Restore();

        using var maskPaint = CreateReflectionMaskPaint(reflectionRect, reflection.StartOpacity, reflection.EndOpacity);
        canvas.DrawRect(reflectionRect, maskPaint);
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

    private static SKRect ResolveShapeTextRect(ShapeProperties properties, float width, float height)
    {
        var rect = ShapeGeometryEvaluator.ResolveTextRectangle(properties, width, height);
        return new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom);
    }

    private void DrawShapePaths(
        SKCanvas canvas,
        IReadOnlyList<ShapePathEntry> paths,
        ShapeFill? fill,
        DocColor? fillColor,
        BorderLine? outline,
        SKRect bounds,
        SKImageFilter? filter)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var canFill = HasShapeFill(fill, fillColor);
        var hasOutline = outline is not null && outline.IsVisible;
        if (!canFill && !hasOutline)
        {
            return;
        }

        SKPaint? strokePaint = null;
        if (hasOutline)
        {
            var thickness = MathF.Max(0.5f, GetBorderThickness(outline!));
            var cap = ResolveStrokeCap(outline!);
            strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ToSkColor(outline!.Color),
                StrokeWidth = thickness,
                IsAntialias = true,
                StrokeCap = ToSkStrokeCap(cap),
                StrokeJoin = ToSkStrokeJoin(outline.LineJoin),
                PathEffect = CreateBorderEffect(outline, thickness)
            };
            if (outline.LineJoin == DocLineJoin.Miter && outline.MiterLimit.HasValue && outline.MiterLimit.Value > 0f)
            {
                strokePaint.StrokeMiter = outline.MiterLimit.Value;
            }

            if (filter is not null)
            {
                strokePaint.ImageFilter = filter;
            }
        }

        Dictionary<ShapePathFillMode, SKPaint>? fillPaints = null;
        if (canFill)
        {
            fillPaints = new Dictionary<ShapePathFillMode, SKPaint>();
        }

        var hasArrows = outline is not null && (outline.HeadArrow.IsVisible || outline.TailArrow.IsVisible);
        var hasCompound = outline is not null && ResolveCompoundLine(outline) != DocCompoundLine.Single;
        Dictionary<float, SKPaint>? segmentPaints = null;
        SKPaint GetSegmentPaint(BorderLine border, float thickness)
        {
            segmentPaints ??= new Dictionary<float, SKPaint>();
            if (segmentPaints.TryGetValue(thickness, out var existing))
            {
                return existing;
            }

            var cap = ResolveStrokeCap(border);
            var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ToSkColor(border.Color),
                StrokeWidth = thickness,
                IsAntialias = true,
                StrokeCap = ToSkStrokeCap(cap),
                StrokeJoin = ToSkStrokeJoin(border.LineJoin),
                PathEffect = CreateBorderEffect(border, thickness)
            };
            if (border.LineJoin == DocLineJoin.Miter && border.MiterLimit.HasValue && border.MiterLimit.Value > 0f)
            {
                paint.StrokeMiter = border.MiterLimit.Value;
            }

            if (filter is not null)
            {
                paint.ImageFilter = filter;
            }

            segmentPaints[thickness] = paint;
            return paint;
        }

        try
        {
            foreach (var entry in paths)
            {
                if (canFill && entry.Data.IsFilled && fillPaints is not null)
                {
                    if (!fillPaints.TryGetValue(entry.Data.FillMode, out var fillPaint))
                    {
                        fillPaint = CreateFillPaint(fill, fillColor, bounds, entry.Data.FillMode);
                        if (fillPaint is not null)
                        {
                            if (filter is not null)
                            {
                                fillPaint.ImageFilter = filter;
                            }

                            fillPaints[entry.Data.FillMode] = fillPaint;
                        }
                    }

                    if (fillPaint is not null)
                    {
                        canvas.DrawPath(entry.Path, fillPaint);
                    }
                }

                if (strokePaint is not null && entry.Data.IsStroked)
                {
                    if (outline is not null
                        && (hasArrows || hasCompound)
                        && TryGetLineSegment(entry.Data, out var x1, out var y1, out var x2, out var y2))
                    {
                        DrawBorderSegment(canvas, outline, x1, y1, x2, y2, GetSegmentPaint);
                    }
                    else if (outline is not null && hasArrows)
                    {
                        DrawPathWithArrows(canvas, entry.Path, outline, strokePaint);
                    }
                    else
                    {
                        canvas.DrawPath(entry.Path, strokePaint);
                    }
                }
            }
        }
        finally
        {
            if (fillPaints is not null)
            {
                foreach (var paint in fillPaints.Values)
                {
                    paint.Dispose();
                }
            }

            if (segmentPaints is not null)
            {
                foreach (var paint in segmentPaints.Values)
                {
                    paint.Dispose();
                }
            }

            strokePaint?.Dispose();
        }
    }

    private static bool TryGetLineSegment(ShapePathData data, out float x1, out float y1, out float x2, out float y2)
    {
        x1 = 0f;
        y1 = 0f;
        x2 = 0f;
        y2 = 0f;
        if (data.Segments.Count < 2)
        {
            return false;
        }

        if (data.Segments[0].Kind != ShapePathSegmentKind.MoveTo)
        {
            return false;
        }

        if (data.Segments[1].Kind != ShapePathSegmentKind.LineTo)
        {
            return false;
        }

        x1 = data.Segments[0].X1;
        y1 = data.Segments[0].Y1;
        x2 = data.Segments[1].X1;
        y2 = data.Segments[1].Y1;
        for (var i = 2; i < data.Segments.Count; i++)
        {
            if (data.Segments[i].Kind != ShapePathSegmentKind.Close)
            {
                return false;
            }
        }

        return true;
    }

    private static void DrawPathWithArrows(SKCanvas canvas, SKPath path, BorderLine outline, SKPaint strokePaint)
    {
        if (!outline.HeadArrow.IsVisible && !outline.TailArrow.IsVisible)
        {
            canvas.DrawPath(path, strokePaint);
            return;
        }

        using var measure = new SKPathMeasure(path, false);
        var length = measure.Length;
        if (length <= 0.01f)
        {
            canvas.DrawPath(path, strokePaint);
            return;
        }

        var trimStart = 0f;
        var trimEnd = 0f;
        if (outline.TailArrow.IsVisible)
        {
            ResolveArrowDimensions(outline.TailArrow, strokePaint.StrokeWidth, out var tailLength, out _);
            trimStart = MathF.Min(tailLength, length);
        }

        if (outline.HeadArrow.IsVisible)
        {
            ResolveArrowDimensions(outline.HeadArrow, strokePaint.StrokeWidth, out var headLength, out _);
            trimEnd = MathF.Min(headLength, MathF.Max(0f, length - trimStart));
        }

        var trimmedLength = length - trimStart - trimEnd;
        if (trimmedLength > 0.01f)
        {
            using var trimmed = new SKPath();
            if (measure.GetSegment(trimStart, length - trimEnd, trimmed, true))
            {
                canvas.DrawPath(trimmed, strokePaint);
            }
            else
            {
                canvas.DrawPath(path, strokePaint);
            }
        }

        if (outline.TailArrow.IsVisible
            && measure.GetPositionAndTangent(0f, out var startPoint, out var startTangent))
        {
            DrawArrowHead(
                canvas,
                startPoint,
                new SKPoint(-startTangent.X, -startTangent.Y),
                outline.TailArrow,
                strokePaint.Color,
                strokePaint.StrokeWidth);
        }

        var endSample = length > 0.01f ? length - 0.01f : length;
        if (outline.HeadArrow.IsVisible
            && measure.GetPositionAndTangent(endSample, out var endPoint, out var endTangent))
        {
            DrawArrowHead(
                canvas,
                endPoint,
                endTangent,
                outline.HeadArrow,
                strokePaint.Color,
                strokePaint.StrokeWidth);
        }
    }

    private static SKPath CreateSkiaPath(ShapePathData data)
    {
        var path = new SKPath
        {
            FillType = data.FillRule == ShapePathFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding
        };
        foreach (var segment in data.Segments)
        {
            switch (segment.Kind)
            {
                case ShapePathSegmentKind.MoveTo:
                    path.MoveTo(segment.X1, segment.Y1);
                    break;
                case ShapePathSegmentKind.LineTo:
                    path.LineTo(segment.X1, segment.Y1);
                    break;
                case ShapePathSegmentKind.QuadTo:
                    path.QuadTo(segment.X1, segment.Y1, segment.X2, segment.Y2);
                    break;
                case ShapePathSegmentKind.CubicTo:
                    path.CubicTo(segment.X1, segment.Y1, segment.X2, segment.Y2, segment.X3, segment.Y3);
                    break;
                case ShapePathSegmentKind.ArcTo:
                {
                    var arcRect = new SKRect(segment.X1, segment.Y1, segment.X1 + segment.X2, segment.Y1 + segment.Y2);
                    path.ArcTo(arcRect, segment.StartAngle, segment.SweepAngle, false);
                    break;
                }
                case ShapePathSegmentKind.Close:
                    path.Close();
                    break;
            }
        }

        return path;
    }

    private static bool HasShapeFill(ShapeFill? fill, DocColor? fillColor)
    {
        if (fill is ShapeNoFill)
        {
            return false;
        }

        if (fill is ShapeSolidFill solidFill)
        {
            return solidFill.Color.A > 0;
        }

        if (fill is ShapeGradientFill gradientFill)
        {
            return gradientFill.Stops.Count > 0;
        }

        if (fill is ShapePatternFill patternFill)
        {
            return patternFill.Foreground.A > 0 || patternFill.Background.A > 0;
        }

        if (fill is ShapeImageFill imageFill)
        {
            return imageFill.Data.Length > 0;
        }

        return fillColor.HasValue && fillColor.Value.A > 0;
    }

    private SKPaint? CreateFillPaint(ShapeFill? fill, DocColor? fillColor, SKRect bounds, ShapePathFillMode mode)
    {
        if (fill is ShapeNoFill)
        {
            return null;
        }

        if (fill is ShapeSolidFill solidFill)
        {
            var color = ApplyFillMode(mode, solidFill.Color);
            return new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(color),
                IsAntialias = true
            };
        }

        if (fill is ShapeGradientFill gradientFill)
        {
            var shader = CreateGradientShader(gradientFill, bounds, mode);
            if (shader is null)
            {
                return null;
            }

            return new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = shader,
                IsAntialias = true
            };
        }

        if (fill is ShapePatternFill patternFill)
        {
            var shader = CreatePatternShader(patternFill, mode);
            if (shader is null)
            {
                return null;
            }

            return new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = shader,
                IsAntialias = true
            };
        }

        if (fill is ShapeImageFill imageFill)
        {
            var shader = CreateImageShader(imageFill, bounds);
            if (shader is null)
            {
                return null;
            }

            return new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = shader,
                IsAntialias = true
            };
        }

        if (fillColor.HasValue && fillColor.Value.A > 0)
        {
            var color = ApplyFillMode(mode, fillColor.Value);
            return new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(color),
                IsAntialias = true
            };
        }

        return null;
    }

    private static SKShader? CreateGradientShader(ShapeGradientFill fill, SKRect bounds, ShapePathFillMode mode)
    {
        if (fill.Stops.Count == 0)
        {
            return null;
        }

        var colors = new SKColor[fill.Stops.Count];
        var positions = new float[fill.Stops.Count];
        for (var i = 0; i < fill.Stops.Count; i++)
        {
            var stop = fill.Stops[i];
            var color = ApplyFillMode(mode, stop.Color);
            colors[i] = ToSkColor(color);
            positions[i] = Clamp01(stop.Position);
        }

        if (fill.Type == ShapeGradientType.Radial)
        {
            var rect = fill.FillRect is null ? bounds : ResolveRelativeRect(fill.FillRect, bounds);
            var center = new SKPoint(rect.MidX, rect.MidY);
            var radius = MathF.Max(rect.Width, rect.Height) / 2f;
            return SKShader.CreateRadialGradient(center, radius, colors, positions, SKShaderTileMode.Clamp);
        }

        var angle = fill.Angle;
        var radians = angle * (MathF.PI / 180f);
        var dx = MathF.Cos(radians);
        var dy = MathF.Sin(radians);
        var half = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2f;
        var centerPoint = new SKPoint(bounds.MidX, bounds.MidY);
        var start = new SKPoint(centerPoint.X - dx * half, centerPoint.Y - dy * half);
        var end = new SKPoint(centerPoint.X + dx * half, centerPoint.Y + dy * half);
        return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
    }

    private SKShader? CreatePatternShader(ShapePatternFill fill, ShapePathFillMode mode)
    {
        var pattern = NormalizePatternKey(fill.Pattern);
        if (pattern.Length == 0)
        {
            return null;
        }

        var foreground = ApplyFillMode(mode, fill.Foreground);
        var background = ApplyFillMode(mode, fill.Background);
        var key = new PatternShaderKey(pattern, foreground, background);
        if (_patternShaderCache.TryGetValue(key, out var cached))
        {
            return cached.Shader;
        }

        var bitmap = new SKBitmap(PatternTileSize, PatternTileSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(background));
        DrawPattern(canvas, bitmap, pattern, ToSkColor(foreground));

        var shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        _patternShaderCache[key] = new PatternShaderCacheEntry(bitmap, shader);
        return shader;
    }

    private static void DrawPattern(SKCanvas canvas, SKBitmap bitmap, string pattern, SKColor color)
    {
        switch (pattern)
        {
            case "pct5":
                DrawPercentPattern(bitmap, color, 0.05f);
                return;
            case "pct10":
                DrawPercentPattern(bitmap, color, 0.1f);
                return;
            case "pct20":
                DrawPercentPattern(bitmap, color, 0.2f);
                return;
            case "pct25":
                DrawPercentPattern(bitmap, color, 0.25f);
                return;
            case "pct30":
                DrawPercentPattern(bitmap, color, 0.3f);
                return;
            case "pct40":
                DrawPercentPattern(bitmap, color, 0.4f);
                return;
            case "pct50":
                DrawPercentPattern(bitmap, color, 0.5f);
                return;
            case "pct60":
                DrawPercentPattern(bitmap, color, 0.6f);
                return;
            case "pct70":
                DrawPercentPattern(bitmap, color, 0.7f);
                return;
            case "pct75":
                DrawPercentPattern(bitmap, color, 0.75f);
                return;
            case "pct80":
                DrawPercentPattern(bitmap, color, 0.8f);
                return;
            case "pct90":
                DrawPercentPattern(bitmap, color, 0.9f);
                return;
            case "horz":
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                return;
            case "vert":
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "lthorz":
                DrawLinePattern(canvas, PatternTileSize, true, 6, 1f, color);
                return;
            case "ltvert":
                DrawLinePattern(canvas, PatternTileSize, false, 6, 1f, color);
                return;
            case "dkhorz":
                DrawLinePattern(canvas, PatternTileSize, true, 3, 2f, color);
                return;
            case "dkvert":
                DrawLinePattern(canvas, PatternTileSize, false, 3, 2f, color);
                return;
            case "narhorz":
                DrawLinePattern(canvas, PatternTileSize, true, 2, 1f, color);
                return;
            case "narvert":
                DrawLinePattern(canvas, PatternTileSize, false, 2, 1f, color);
                return;
            case "dashhorz":
                DrawDashPattern(canvas, PatternTileSize, true, 4, 1f, color);
                return;
            case "dashvert":
                DrawDashPattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "cross":
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "dndiag":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 4, 1f, color);
                return;
            case "updiag":
                DrawDiagonalPattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "ltdndiag":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 6, 1f, color);
                return;
            case "ltupdiag":
                DrawDiagonalPattern(canvas, PatternTileSize, false, 6, 1f, color);
                return;
            case "dkdndiag":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 3, 2f, color);
                return;
            case "dkupdiag":
                DrawDiagonalPattern(canvas, PatternTileSize, false, 3, 2f, color);
                return;
            case "wddndiag":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 6, 2f, color);
                return;
            case "wdupdiag":
                DrawDiagonalPattern(canvas, PatternTileSize, false, 6, 2f, color);
                return;
            case "dashdndiag":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 4, 1f, color);
                return;
            case "dashupdiag":
                DrawDiagonalPattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "diagcross":
                DrawDiagonalPattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawDiagonalPattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "smgrid":
                DrawLinePattern(canvas, PatternTileSize, true, 2, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 2, 1f, color);
                return;
            case "lggrid":
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            case "dotgrid":
                DrawPercentPattern(bitmap, color, 0.2f);
                return;
            case "smcheck":
            case "lgcheck":
            case "plaid":
            case "weave":
            case "trellis":
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                DrawDiagonalPattern(canvas, PatternTileSize, true, 4, 1f, color);
                return;
            case "smconfetti":
                DrawPercentPattern(bitmap, color, 0.25f);
                return;
            case "lgconfetti":
                DrawPercentPattern(bitmap, color, 0.4f);
                return;
            case "horzbrick":
            case "diagbrick":
            case "soliddmnd":
            case "opendmnd":
            case "dotdmnd":
            case "sphere":
            case "divot":
            case "shingle":
            case "wave":
            case "zigzag":
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
            default:
                DrawLinePattern(canvas, PatternTileSize, true, 4, 1f, color);
                DrawLinePattern(canvas, PatternTileSize, false, 4, 1f, color);
                return;
        }
    }

    private static void DrawPercentPattern(SKBitmap bitmap, SKColor color, float percent)
    {
        var threshold = (int)MathF.Round(Clamp01(percent) * PatternDither8x8.Length);
        for (var i = 0; i < PatternDither8x8.Length; i++)
        {
            if (PatternDither8x8[i] < threshold)
            {
                var x = i % PatternTileSize;
                var y = i / PatternTileSize;
                bitmap.SetPixel(x, y, color);
            }
        }
    }

    private static void DrawLinePattern(SKCanvas canvas, int size, bool horizontal, int spacing, float thickness, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = false
        };

        for (var offset = 0; offset < size; offset += spacing)
        {
            var position = offset + 0.5f;
            if (horizontal)
            {
                canvas.DrawLine(0f, position, size, position, paint);
            }
            else
            {
                canvas.DrawLine(position, 0f, position, size, paint);
            }
        }
    }

    private static void DrawDashPattern(SKCanvas canvas, int size, bool horizontal, int spacing, float thickness, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = false
        };

        var dash = size / 2f;
        for (var offset = 0; offset < size; offset += spacing)
        {
            var position = offset + 0.5f;
            if (horizontal)
            {
                canvas.DrawLine(0f, position, dash, position, paint);
            }
            else
            {
                canvas.DrawLine(position, 0f, position, dash, paint);
            }
        }
    }

    private static void DrawDiagonalPattern(SKCanvas canvas, int size, bool downward, int spacing, float thickness, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = false
        };

        for (var offset = -size; offset <= size; offset += spacing)
        {
            if (downward)
            {
                canvas.DrawLine(offset, 0f, offset + size, size, paint);
            }
            else
            {
                canvas.DrawLine(offset, size, offset + size, 0f, paint);
            }
        }
    }

    private SKShader? CreateImageShader(ShapeImageFill fill, SKRect bounds)
    {
        var bitmap = GetShapeFillBitmap(fill);
        if (bitmap is null)
        {
            return null;
        }

        var cropRect = ResolveImageCropRect(fill.Crop, bitmap);
        if (cropRect.Width <= 0f || cropRect.Height <= 0f)
        {
            return null;
        }

        if (fill.Mode == ShapeImageFillMode.Stretch)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                return null;
            }

            var stretchScaleX = cropRect.Width / bounds.Width;
            var stretchScaleY = cropRect.Height / bounds.Height;
            var stretchMatrix = CreateBitmapMatrix(stretchScaleX, stretchScaleY, cropRect.Left, cropRect.Top);
            return SKShader.CreateBitmap(bitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, stretchMatrix);
        }

        var tile = fill.Tile;
        var tileScaleX = tile?.ScaleX ?? 1f;
        var tileScaleY = tile?.ScaleY ?? 1f;
        if (tileScaleX <= 0f)
        {
            tileScaleX = 1f;
        }

        if (tileScaleY <= 0f)
        {
            tileScaleY = 1f;
        }

        var offsetX = tile?.OffsetX ?? 0f;
        var offsetY = tile?.OffsetY ?? 0f;
        var scaleX = 1f / tileScaleX;
        var scaleY = 1f / tileScaleY;
        var tileMatrix = CreateBitmapMatrix(scaleX, scaleY, cropRect.Left - offsetX * scaleX, cropRect.Top - offsetY * scaleY);
        return SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, tileMatrix);
    }

    private SKBitmap? GetShapeFillBitmap(ShapeImageFill fill)
    {
        if (_invalidShapeFillImages.Contains(fill))
        {
            return null;
        }

        if (!_shapeFillImageCache.TryGetValue(fill, out var bitmap))
        {
            if (fill.Data.Length == 0)
            {
                _invalidShapeFillImages.Add(fill);
                return null;
            }

            try
            {
                bitmap = SKBitmap.Decode(fill.Data);
                if (bitmap is not null)
                {
                    _shapeFillImageCache[fill] = bitmap;
                }
            }
            catch
            {
                _invalidShapeFillImages.Add(fill);
                return null;
            }
        }

        return bitmap;
    }

    private static SKRect ResolveImageCropRect(ImageCrop? crop, SKBitmap bitmap)
    {
        var left = 0f;
        var top = 0f;
        var right = (float)bitmap.Width;
        var bottom = (float)bitmap.Height;
        if (crop is null || !crop.HasValues)
        {
            return new SKRect(left, top, right, bottom);
        }

        left += bitmap.Width * crop.Left;
        right -= bitmap.Width * crop.Right;
        top += bitmap.Height * crop.Top;
        bottom -= bitmap.Height * crop.Bottom;
        if (right <= left || bottom <= top)
        {
            return new SKRect(0f, 0f, bitmap.Width, bitmap.Height);
        }

        return new SKRect(left, top, right, bottom);
    }

    private static SKRect ResolveRelativeRect(ShapeGradientRect rect, SKRect bounds)
    {
        var left = bounds.Left + bounds.Width * rect.Left;
        var top = bounds.Top + bounds.Height * rect.Top;
        var right = bounds.Right - bounds.Width * rect.Right;
        var bottom = bounds.Bottom - bounds.Height * rect.Bottom;
        return new SKRect(left, top, right, bottom);
    }

    private static SKMatrix CreateBitmapMatrix(float scaleX, float scaleY, float translateX, float translateY)
    {
        return new SKMatrix
        {
            ScaleX = scaleX,
            SkewX = 0f,
            TransX = translateX,
            SkewY = 0f,
            ScaleY = scaleY,
            TransY = translateY,
            Persp0 = 0f,
            Persp1 = 0f,
            Persp2 = 1f
        };
    }

    private static DocColor ApplyFillMode(ShapePathFillMode mode, DocColor color)
    {
        return mode switch
        {
            ShapePathFillMode.Lighten => BlendDocColor(color, DocColor.White, 0.4f),
            ShapePathFillMode.LightenLess => BlendDocColor(color, DocColor.White, 0.2f),
            ShapePathFillMode.Darken => BlendDocColor(color, DocColor.Black, 0.4f),
            ShapePathFillMode.DarkenLess => BlendDocColor(color, DocColor.Black, 0.2f),
            _ => color
        };
    }

    private static DocColor BlendDocColor(DocColor baseColor, DocColor blend, float amount)
    {
        var clamped = Clamp01(amount);
        var r = (byte)MathF.Round(baseColor.R + (blend.R - baseColor.R) * clamped);
        var g = (byte)MathF.Round(baseColor.G + (blend.G - baseColor.G) * clamped);
        var b = (byte)MathF.Round(baseColor.B + (blend.B - baseColor.B) * clamped);
        return new DocColor(r, g, b, baseColor.A);
    }

    private static string NormalizePatternKey(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        var span = pattern.AsSpan().Trim();
        Span<char> buffer = stackalloc char[span.Length];
        var count = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (!char.IsLetterOrDigit(ch))
            {
                continue;
            }

            buffer[count++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer.Slice(0, count));
    }

    private const int PatternTileSize = 8;

    private static readonly byte[] PatternDither8x8 =
    {
        0, 48, 12, 60, 3, 51, 15, 63,
        32, 16, 44, 28, 35, 19, 47, 31,
        8, 56, 4, 52, 11, 59, 7, 55,
        40, 24, 36, 20, 43, 27, 39, 23,
        2, 50, 14, 62, 1, 49, 13, 61,
        34, 18, 46, 30, 33, 17, 45, 29,
        10, 58, 6, 54, 9, 57, 5, 53,
        42, 26, 38, 22, 41, 25, 37, 21
    };

    private readonly struct ShapePathEntry
    {
        public ShapePathData Data { get; }
        public SKPath Path { get; }

        public ShapePathEntry(ShapePathData data, SKPath path)
        {
            Data = data;
            Path = path;
        }
    }

    private readonly struct PatternShaderKey : IEquatable<PatternShaderKey>
    {
        public string Pattern { get; }
        public DocColor Foreground { get; }
        public DocColor Background { get; }

        public PatternShaderKey(string pattern, DocColor foreground, DocColor background)
        {
            Pattern = pattern;
            Foreground = foreground;
            Background = background;
        }

        public bool Equals(PatternShaderKey other)
        {
            return string.Equals(Pattern, other.Pattern, StringComparison.Ordinal)
                   && Foreground.Equals(other.Foreground)
                   && Background.Equals(other.Background);
        }

        public override bool Equals(object? obj) => obj is PatternShaderKey other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Pattern), Foreground, Background);
        }
    }

    private sealed class PatternShaderCacheEntry
    {
        public SKBitmap Bitmap { get; }
        public SKShader Shader { get; }

        public PatternShaderCacheEntry(SKBitmap bitmap, SKShader shader)
        {
            Bitmap = bitmap;
            Shader = shader;
        }
    }

    private void DrawShapeText(
        SKCanvas canvas,
        ShapeTextBox textBox,
        SKRect textBounds,
        RenderOptions options,
        Document document,
        TextStyle defaultStyle,
        LayoutSettings layoutSettings,
        Guid shapeId)
    {
        var padding = textBox.Properties.Padding;
        var left = textBounds.Left + padding.Left;
        var top = textBounds.Top + padding.Top;
        var right = textBounds.Right - padding.Right;
        var bottom = textBounds.Bottom - padding.Bottom;
        var width = right - left;
        var height = bottom - top;
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        var autoFit = textBox.Properties.AutoFit;
        var horizontalOverflow = textBox.Properties.HorizontalOverflow;
        var verticalOverflow = textBox.Properties.VerticalOverflow;

        var shapeDocument = CreateShapeTextDocument(document, textBox, defaultStyle);
        var shapeSettings = layoutSettings.Clone();
        shapeSettings.UsePagination = false;
        shapeSettings.ViewportWidth = width;
        shapeSettings.ViewportHeight = height;
        shapeSettings.PageWidth = width;
        shapeSettings.PageHeight = height;
        shapeSettings.PageGap = 0f;
        shapeSettings.MarginLeft = 0f;
        shapeSettings.MarginRight = 0f;
        shapeSettings.MarginTop = 0f;
        shapeSettings.MarginBottom = 0f;
        shapeSettings.HeaderOffset = 0f;
        shapeSettings.FooterOffset = 0f;
        shapeSettings.Gutter = 0f;

        var measurer = _shapeTextMeasurer ??= new SkiaTextMeasurer();
        measurer.TypefaceResolver = TypefaceResolver;
        measurer.UseHarfBuzz = options.UseHarfBuzz;

        var layout = ShapeTextLayouter.Layout(shapeDocument, shapeSettings, measurer);
        if (layout.Lines.Count == 0 && layout.Tables.Count == 0 && layout.FloatingObjects.Count == 0)
        {
            return;
        }

        var textArea = new DocRect(left, top, width, height);
        if (!ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textArea, out var metrics))
        {
            return;
        }

        var scale = metrics.Scale;
        var startY = metrics.OriginY;
        left = metrics.OriginX;

        var styleResolver = new DocumentStyleResolver(shapeDocument);
        var paintCache = new Dictionary<TextStyleKey, SKPaint>();
        var highlightPaintCache = new Dictionary<DocColor, SKPaint>();
        var borderPaintCache = new Dictionary<BorderPaintKey, SKPaint>();
        var invisibleTextPaintCache = new Dictionary<TextStyleKey, SKPaint>();
        var shaperCache = new Dictionary<TextStyleKey, SKShaper>();
        var typefacePaintCache = new Dictionary<(TextStyleKey StyleKey, SKTypeface Typeface), SKPaint>();
        var typefaceShaperCache = new Dictionary<SKTypeface, SKShaper>();
        var runMetricsCache = new Dictionary<RunMetricsKey, RunMetrics>();
        var canShapeText = options.UseHarfBuzz;
        var fallbackResolver = TypefaceResolver as ISkiaTypefaceFallbackResolver;
        var commentHighlightsByParagraph = layout.CommentHighlightsByParagraph;
        var isShapeTextEditing = options.ShapeTextEditingShapeId.HasValue && options.ShapeTextEditingShapeId.Value == shapeId;

        var selectionRanges = Array.Empty<TextRange>();
        if (isShapeTextEditing)
        {
            if (options.ShapeTextSelectionRanges is { Count: > 0 } providedRanges)
            {
                var list = new List<TextRange>(providedRanges.Count);
                for (var i = 0; i < providedRanges.Count; i++)
                {
                    var normalized = providedRanges[i].Normalize();
                    if (!normalized.IsEmpty)
                    {
                        list.Add(normalized);
                    }
                }

                if (list.Count > 0)
                {
                    list.Sort(static (left, right) =>
                    {
                        var startCompare = left.Start.CompareTo(right.Start);
                        return startCompare != 0 ? startCompare : left.End.CompareTo(right.End);
                    });
                    selectionRanges = list.ToArray();
                }
            }

            if (selectionRanges.Length == 0 && options.ShapeTextSelection is { } singleSelection)
            {
                var normalized = singleSelection.Normalize();
                if (!normalized.IsEmpty)
                {
                    selectionRanges = new[] { normalized };
                }
            }
        }

        var drawSelection = isShapeTextEditing && selectionRanges.Length > 0;
        var caretPosition = options.ShapeTextCaret;
        var drawCaret = isShapeTextEditing && options.ShowShapeTextCaret;

        SKPaint GetRunPaint(TextStyle runStyle)
        {
            var key = new TextStyleKey(runStyle);
            if (paintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, TypefaceResolver);
            paint.Color = ToSkColor(runStyle.Color);
            paintCache[key] = paint;
            return paint;
        }

        SKPaint GetTypefacePaint(TextStyle runStyle, SKTypeface typeface)
        {
            var basePaint = GetRunPaint(runStyle);
            if (ReferenceEquals(basePaint.Typeface, typeface))
            {
                return basePaint;
            }

            var key = (new TextStyleKey(runStyle), typeface);
            if (typefacePaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, null);
            paint.Typeface = typeface;
            paint.Color = ToSkColor(runStyle.Color);
            typefacePaintCache[key] = paint;
            return paint;
        }

        void DisableShaping()
        {
            canShapeText = false;
            foreach (var shaper in shaperCache.Values)
            {
                shaper.Dispose();
            }

            shaperCache.Clear();

            foreach (var shaper in typefaceShaperCache.Values)
            {
                shaper.Dispose();
            }

            typefaceShaperCache.Clear();
        }

        SKShaper? GetRunShaper(TextStyle runStyle)
        {
            if (!canShapeText)
            {
                return null;
            }

            var key = new TextStyleKey(runStyle);
            if (shaperCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            try
            {
                var paint = GetRunPaint(runStyle);
                var typeface = paint.Typeface ?? SKTypeface.Default;
                var shaper = new SKShaper(typeface);
                shaperCache[key] = shaper;
                return shaper;
            }
            catch
            {
                DisableShaping();
                return null;
            }
        }

        SKShaper? GetTypefaceShaper(SKTypeface typeface)
        {
            if (!canShapeText)
            {
                return null;
            }

            if (typefaceShaperCache.TryGetValue(typeface, out var cached))
            {
                return cached;
            }

            try
            {
                var shaper = new SKShaper(typeface);
                typefaceShaperCache[typeface] = shaper;
                return shaper;
            }
            catch
            {
                DisableShaping();
                return null;
            }
        }

        TextShapeInfo ShapeText(string text, TextStyle runStyle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
            }

            void AppendShapeInfo(TextShapeInfo segmentShape, int offsetBase, List<int> offsets, List<float> advances)
            {
                if (segmentShape.ClusterOffsets.Length == 0)
                {
                    return;
                }

                for (var i = 0; i < segmentShape.ClusterOffsets.Length; i++)
                {
                    offsets.Add(segmentShape.ClusterOffsets[i] + offsetBase);
                    var advance = i < segmentShape.ClusterAdvances.Length ? segmentShape.ClusterAdvances[i] : 0f;
                    advances.Add(advance);
                }
            }

            var paint = GetRunPaint(runStyle);
            var textSpan = text.AsSpan();
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            var applyKerning = SkiaTextMeasurer.ShouldApplyKerning(runStyle);
            if (!needsFallback)
            {
                var shaper = applyKerning ? GetRunShaper(runStyle) : null;
                if (applyKerning && shaper is not null)
                {
                    try
                    {
                        if (SkiaTextMeasurer.TryShapeTextSegment(textSpan, runStyle, paint, shaper, out var shaped))
                        {
                            return shaped;
                        }
                    }
                    catch
                    {
                        // fall back to cluster measurement
                    }
                }

                return SkiaTextMeasurer.BuildSimpleShapeInfo(textSpan, paint, runStyle);
            }

            var segments = SkiaTextMeasurer.BuildTypefaceSegments(textSpan, runStyle, paint, fallbackResolver);
            if (segments.Count == 0)
            {
                return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
            }

            var segmentOffsets = new List<int>();
            var segmentAdvances = new List<float>();
            foreach (var segment in segments)
            {
                var segmentSpan = textSpan.Slice(segment.Start, segment.Length);
                var segmentPaint = GetTypefacePaint(runStyle, segment.Typeface);
                if (canShapeText && applyKerning)
                {
                    var shaper = GetTypefaceShaper(segment.Typeface);
                    if (shaper is not null)
                    {
                        try
                        {
                            if (SkiaTextMeasurer.TryShapeTextSegment(segmentSpan, runStyle, segmentPaint, shaper, out var segmentShape))
                            {
                                AppendShapeInfo(segmentShape, segment.Start, segmentOffsets, segmentAdvances);
                                continue;
                            }
                        }
                        catch
                        {
                            // fall back to cluster measurement
                        }
                    }
                }

                var fallbackShape = SkiaTextMeasurer.BuildSimpleShapeInfo(segmentSpan, segmentPaint, runStyle);
                AppendShapeInfo(fallbackShape, segment.Start, segmentOffsets, segmentAdvances);
            }

            return segmentOffsets.Count == 0
                ? new TextShapeInfo(text.Length, Array.Empty<int>(), Array.Empty<float>())
                : new TextShapeInfo(text.Length, segmentOffsets.ToArray(), segmentAdvances.ToArray());
        }

        RunMetrics GetRunMetrics(string text, TextStyle runStyle, float letterSpacing, float gridSpacing)
        {
            if (string.IsNullOrEmpty(text))
            {
                return RunMetrics.Empty;
            }

            var key = new RunMetricsKey(text, runStyle, letterSpacing, gridSpacing);
            if (runMetricsCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var shape = ShapeText(text, runStyle);
            var metrics = new RunMetrics(shape, letterSpacing, gridSpacing);
            runMetricsCache[key] = metrics;
            return metrics;
        }

        float ResolveGridSpacing(DocGridSettings? docGrid)
        {
            if (docGrid is null || !docGrid.HasValues)
            {
                return 0f;
            }

            if (docGrid.CharacterSpace is not > 0f)
            {
                return 0f;
            }

            return !docGrid.Type.HasValue
                || docGrid.Type == DocGridType.LinesAndChars
                || docGrid.Type == DocGridType.SnapToChars
                ? docGrid.CharacterSpace.Value
                : 0f;
        }

        float ResolveLineGridSpacing(int paragraphIndex, int pageIndex)
        {
            if (paragraphIndex >= 0
                && layout.ParagraphSectionIndices.TryGetValue(paragraphIndex, out var sectionIndex)
                && layout.SectionSettings.TryGetValue(sectionIndex, out var section))
            {
                return ResolveGridSpacing(section.ResolveForPage(pageIndex).DocGrid);
            }

            return pageIndex >= 0 && pageIndex < layout.PageSections.Count
                ? ResolveGridSpacing(layout.PageSections[pageIndex].DocGrid)
                : 0f;
        }

        SKPaint GetHighlightPaint(DocColor color)
        {
            if (highlightPaintCache.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(color),
                IsAntialias = true
            };
            highlightPaintCache[color] = paint;
            return paint;
        }

        SKPaint GetBorderPaint(BorderLine border, float thickness)
        {
            var cap = ResolveStrokeCap(border);
            var miterLimit = border.LineJoin == DocLineJoin.Miter && border.MiterLimit.HasValue && border.MiterLimit.Value > 0f
                ? border.MiterLimit.Value
                : 0f;
            var key = new BorderPaintKey(border.Color, thickness, border.Style, cap, border.LineJoin, miterLimit, GetDashHash(border));
            if (borderPaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ToSkColor(border.Color),
                StrokeWidth = thickness,
                IsAntialias = true,
                StrokeCap = ToSkStrokeCap(cap),
                StrokeJoin = ToSkStrokeJoin(border.LineJoin),
                PathEffect = CreateBorderEffect(border, thickness)
            };
            if (miterLimit > 0f)
            {
                paint.StrokeMiter = miterLimit;
            }

            borderPaintCache[key] = paint;
            return paint;
        }

        SKPaint GetInvisibleTextPaint(TextStyle runStyle)
        {
            var key = new TextStyleKey(runStyle);
            if (invisibleTextPaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, TypefaceResolver);
            paint.Color = ToSkColor(options.InvisiblesColor);
            invisibleTextPaintCache[key] = paint;
            return paint;
        }

        using var defaultPaint = SkiaTextMeasurer.CreatePaint(defaultStyle, TypefaceResolver);
        defaultPaint.Color = ToSkColor(options.TextColor);

        using var selectionPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.SelectionColor),
            IsAntialias = true
        };

        using var caretPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.CaretColor),
            IsAntialias = true
        };

        using var invisiblesStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.InvisiblesColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };

        using var invisiblesFillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.InvisiblesColor),
            IsAntialias = true
        };

        void DrawLineRangeRect(
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            float start,
            float end,
            SKPaint paint)
        {
            if (end <= start)
            {
                return;
            }

            if (!DocTextDirectionHelpers.IsVertical(textDirection))
            {
                var rect = new SKRect(lineX + start, lineY, lineX + end, lineY + lineHeight);
                canvas.DrawRect(rect, paint);
                return;
            }

            canvas.Save();
            canvas.Translate(lineX, lineY);
            canvas.RotateDegrees(DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value));
            var localRect = new SKRect(start, 0f, end, lineHeight);
            canvas.DrawRect(localRect, paint);
            canvas.Restore();
        }

        void DrawLineHighlights(
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            float gridSpacing)
        {
            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);
            foreach (var segment in segments)
            {
                if (!segment.IsText || segment.Run is null)
                {
                    continue;
                }

                var run = segment.Run;
                if (run.Style.Hidden || run.Style.HighlightColor is null || string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var highlightPaint = GetHighlightPaint(run.Style.HighlightColor.Value);
                DrawLineRangeRect(lineX, lineY, lineHeight, textDirection, segment.X, segment.X + segment.Width, highlightPaint);
            }
        }

        void DrawCommentHighlights(
            int paragraphIndex,
            int lineStart,
            int lineLength,
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            float gridSpacing)
        {
            if (lineLength <= 0 || commentHighlightsByParagraph.Count == 0 || options.CommentHighlightColor.A == 0)
            {
                return;
            }

            if (!commentHighlightsByParagraph.TryGetValue(paragraphIndex, out var spans))
            {
                return;
            }

            var highlightPaint = GetHighlightPaint(options.CommentHighlightColor);
            var lineEnd = lineStart + lineLength;
            foreach (var span in spans)
            {
                var spanStart = Math.Max(lineStart, span.StartOffset);
                var spanEnd = Math.Min(lineEnd, span.EndOffset);
                if (spanEnd <= spanStart)
                {
                    continue;
                }

                var startOffset = spanStart - lineStart;
                var endOffset = spanEnd - lineStart;
                var highlightX1 = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, startOffset, gridSpacing, GetRunMetrics);
                var highlightX2 = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, endOffset, gridSpacing, GetRunMetrics);
                if (highlightX2 <= highlightX1)
                {
                    continue;
                }

                DrawLineRangeRect(lineX, lineY, lineHeight, textDirection, highlightX1, highlightX2, highlightPaint);
            }
        }

        void DrawLineContent(
            float lineX,
            float lineY,
            float lineHeight,
            float lineAscent,
            string? prefix,
            float prefixWidth,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            IReadOnlyList<LayoutRuby> rubies,
            DocTextDirection? textDirection,
            float gridSpacing)
        {
            var originX = lineX;
            var originY = lineY;
            var restoreTransform = false;
            var rotation = 0f;
            var verticalUpright = false;
            var rotationSin = 0f;
            var rotationCos = 1f;
            if (DocTextDirectionHelpers.IsVertical(textDirection))
            {
                restoreTransform = true;
                canvas.Save();
                canvas.Translate(lineX, lineY);
                rotation = DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value);
                canvas.RotateDegrees(rotation);
                originX = 0f;
                originY = 0f;
                verticalUpright = DocTextDirectionHelpers.UseUprightVerticalForms(textDirection.Value);
                if (verticalUpright)
                {
                    var radians = rotation * (MathF.PI / 180f);
                    rotationSin = MathF.Sin(radians);
                    rotationCos = MathF.Cos(radians);
                }
            }

            var baseline = originY + lineAscent;
            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);
            if (!string.IsNullOrEmpty(prefix))
            {
                var lineWidth = segments.Count > 0 ? segments[^1].X + segments[^1].Width : 0f;
                var prefixX = baseRtl ? originX + lineWidth : originX - prefixWidth;
                var prefixBaseline = originY + lineAscent;
                var prefixShaper = SkiaTextMeasurer.ShouldApplyKerning(defaultStyle) ? GetRunShaper(defaultStyle) : null;
                if (prefixShaper is null)
                {
                    canvas.DrawText(prefix, prefixX, prefixBaseline, defaultPaint);
                }
                else
                {
                    canvas.DrawShapedText(prefixShaper, prefix, prefixX, prefixBaseline, defaultPaint);
                }
            }
            foreach (var segment in segments)
            {
                if (segment.IsTab && segment.Run is not null)
                {
                    var run = segment.Run;
                    if (run.Style.Hidden)
                    {
                        continue;
                    }

                    if (run.TabLeader != TabLeader.None && segment.Width > 0f
                        && TryGetTabLeaderPattern(run.TabLeader, out var pattern))
                    {
                        var paint = GetRunPaint(run.Style);
                        var patternWidth = GetRunMetrics(pattern, run.Style, run.LetterSpacing, gridSpacing).Width;
                        if (patternWidth > 0f)
                        {
                            var inset = MathF.Min(patternWidth * 0.5f, segment.Width * 0.25f);
                            var leaderWidth = segment.Width - inset * 2f;
                            if (leaderWidth > 0.1f)
                            {
                                var count = Math.Max(1, (int)MathF.Ceiling(leaderWidth / patternWidth));
                                var text = RepeatPattern(pattern, count);
                                var startX = originX + segment.X + inset;
                                var clipRect = new SKRect(startX, originY, startX + leaderWidth, originY + lineHeight);
                                var shaper = SkiaTextMeasurer.ShouldApplyKerning(run.Style) ? GetRunShaper(run.Style) : null;
                                canvas.Save();
                                canvas.ClipRect(clipRect);
                                DrawTextWithSpacing(canvas, text, startX, baseline, paint, shaper, run.LetterSpacing, gridSpacing, run.Style);
                                canvas.Restore();
                            }
                        }
                    }

                    continue;
                }

                if (segment.IsText && segment.Run is not null)
                {
                    var run = segment.Run;
                    if (run.Style.Hidden || string.IsNullOrEmpty(run.Text))
                    {
                        continue;
                    }

                    var runBaseline = baseline - run.BaselineOffset;
                    var runPaint = GetRunPaint(run.Style);
                    var segmentText = run.Text.Substring(segment.RunStart, segment.Length);
                    var segmentSpan = segmentText.AsSpan();
                    var segmentX = originX + segment.X;
                    var drawX = segment.IsRtl ? segmentX + segment.Width : segmentX;
                    var baseTypeface = runPaint.Typeface ?? SKTypeface.Default;
                    var applyKerning = SkiaTextMeasurer.ShouldApplyKerning(run.Style);
                    var fallbackSegments = fallbackResolver is not null && !runPaint.ContainsGlyphs(segmentText)
                        ? SkiaTextMeasurer.BuildTypefaceSegments(segmentSpan, run.Style, runPaint, fallbackResolver)
                        : null;

                    var allowUpright = verticalUpright
                        && (fallbackSegments is null
                            || fallbackSegments.Count == 0
                            || (fallbackSegments.Count == 1 && ReferenceEquals(fallbackSegments[0].Typeface, baseTypeface)));
                    if (allowUpright)
                    {
                        var segmentMetrics = GetRunMetrics(segmentText, run.Style, run.LetterSpacing, gridSpacing);
                        var cursor = drawX;
                        var shaper = applyKerning ? GetRunShaper(run.Style) : null;
                        ForEachVerticalTextSegment(segmentSpan, verticalUpright, (start, length, upright) =>
                        {
                            if (length <= 0)
                            {
                                return;
                            }

                            var subText = segmentText.Substring(start, length);
                            var subWidth = segmentMetrics.GetWidth(start + length) - segmentMetrics.GetWidth(start);
                            var subDrawX = segment.IsRtl ? cursor - subWidth : cursor;
                            if (upright)
                            {
                                if (!TryDrawVerticalUprightRun(
                                        canvas,
                                        subText.AsSpan(),
                                        subDrawX,
                                        runBaseline,
                                        rotation,
                                        rotationSin,
                                        rotationCos,
                                        runPaint,
                                        runPaint,
                                        run.Style,
                                        run.LetterSpacing,
                                        gridSpacing))
                                {
                                    DrawTextWithSpacing(canvas, subText, subDrawX, runBaseline, runPaint, shaper, run.LetterSpacing, gridSpacing, run.Style);
                                }
                            }
                            else
                            {
                                DrawTextWithSpacing(canvas, subText, subDrawX, runBaseline, runPaint, shaper, run.LetterSpacing, gridSpacing, run.Style);
                            }

                            cursor = segment.IsRtl ? cursor - subWidth : cursor + subWidth;
                        });
                    }
                    else if (fallbackSegments is null
                             || fallbackSegments.Count == 0
                             || (fallbackSegments.Count == 1 && ReferenceEquals(fallbackSegments[0].Typeface, baseTypeface)))
                    {
                        var shaper = applyKerning ? GetRunShaper(run.Style) : null;
                        DrawTextWithSpacing(canvas, segmentText, drawX, runBaseline, runPaint, shaper, run.LetterSpacing, gridSpacing, run.Style);
                    }
                    else
                    {
                        var segmentMetrics = GetRunMetrics(segmentText, run.Style, run.LetterSpacing, gridSpacing);
                        var metricsWidth = segmentMetrics.Width;
                        var scale = metricsWidth > 0f ? segment.Width / metricsWidth : 1f;

                        foreach (var fallbackSegment in fallbackSegments)
                        {
                            var fallbackText = segmentSpan.Slice(fallbackSegment.Start, fallbackSegment.Length).ToString();
                            var fallbackPaint = GetTypefacePaint(run.Style, fallbackSegment.Typeface);
                            var fallbackShaper = applyKerning ? GetTypefaceShaper(fallbackSegment.Typeface) : null;
                            var localX = segmentMetrics.GetWidth(fallbackSegment.Start) * scale;
                            var fallbackX = segment.IsRtl ? drawX - localX : drawX + localX;

                            DrawTextWithSpacing(canvas, fallbackText, fallbackX, runBaseline, fallbackPaint, fallbackShaper, run.LetterSpacing, gridSpacing, run.Style);
                        }
                    }

                    DrawUnderlineSpan(canvas, runBaseline, segmentX, segment.Width, segmentText, run.Style, run.LetterSpacing, runPaint);
                    DrawStrikeThroughSpan(canvas, runBaseline, segmentX, segment.Width, segmentText, run.Style, runPaint);
                    continue;
                }

                if (segment.Image is not null)
                {
                    DrawImage(canvas, segment.Image with { X = segment.X }, originX, baseline, lineHeight, lineAscent, options);
                }
                else if (segment.Shape is not null)
                {
                    DrawShape(canvas, segment.Shape with { X = segment.X }, originX, baseline, lineAscent, options, defaultStyle, shapeDocument, shapeSettings);
                }
                else if (segment.Chart is not null)
                {
                    DrawChart(canvas, segment.Chart with { X = segment.X }, originX, baseline, options);
                }
                else if (segment.Equation is not null)
                {
                    DrawEquation(
                        canvas,
                        segment.Equation with { X = segment.X },
                        originX,
                        baseline,
                        GetRunPaint,
                        runStyle => SkiaTextMeasurer.ShouldApplyKerning(runStyle) ? GetRunShaper(runStyle) : null);
                }
            }

            if (rubies.Count > 0)
            {
                foreach (var ruby in rubies)
                {
                    if (ruby.Length <= 0 || string.IsNullOrEmpty(ruby.RubyText))
                    {
                        continue;
                    }

                    if (ruby.RubyStyle.Hidden)
                    {
                        continue;
                    }

                    var startX = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, ruby.StartOffset, gridSpacing, GetRunMetrics);
                    var endX = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, ruby.StartOffset + ruby.Length, gridSpacing, GetRunMetrics);
                    var rangeX = MathF.Min(startX, endX);
                    var baseWidth = MathF.Abs(endX - startX);

                    var rubyMetrics = GetRunMetrics(ruby.RubyText, ruby.RubyStyle, 0f, gridSpacing);
                    var rubyWidth = rubyMetrics.Width;
                    var rubyX = originX + rangeX + MathF.Max(0f, (baseWidth - rubyWidth) / 2f);
                    var rubyBaseline = baseline + ruby.BaselineOffset;

                    var rubyPaint = GetRunPaint(ruby.RubyStyle);
                    var applyKerning = SkiaTextMeasurer.ShouldApplyKerning(ruby.RubyStyle);
                    var rubyShaper = applyKerning ? GetRunShaper(ruby.RubyStyle) : null;
                    var rubySpan = ruby.RubyText.AsSpan();
                    var baseTypeface = rubyPaint.Typeface ?? SKTypeface.Default;
                    var fallbackSegments = fallbackResolver is not null && !rubyPaint.ContainsGlyphs(ruby.RubyText)
                        ? SkiaTextMeasurer.BuildTypefaceSegments(rubySpan, ruby.RubyStyle, rubyPaint, fallbackResolver)
                        : null;

                    if (fallbackSegments is null
                        || fallbackSegments.Count == 0
                        || (fallbackSegments.Count == 1 && ReferenceEquals(fallbackSegments[0].Typeface, baseTypeface)))
                    {
                        DrawTextWithSpacing(canvas, ruby.RubyText, rubyX, rubyBaseline, rubyPaint, rubyShaper, 0f, gridSpacing, ruby.RubyStyle);
                    }
                    else
                    {
                        foreach (var fallbackSegment in fallbackSegments)
                        {
                            var fallbackText = rubySpan.Slice(fallbackSegment.Start, fallbackSegment.Length).ToString();
                            var fallbackPaint = GetTypefacePaint(ruby.RubyStyle, fallbackSegment.Typeface);
                            var fallbackShaper = applyKerning ? GetTypefaceShaper(fallbackSegment.Typeface) : null;
                            var localX = rubyMetrics.GetWidth(fallbackSegment.Start);
                            var fallbackX = rubyX + localX;

                            DrawTextWithSpacing(canvas, fallbackText, fallbackX, rubyBaseline, fallbackPaint, fallbackShaper, 0f, gridSpacing, ruby.RubyStyle);
                        }
                    }
                }
            }

            if (restoreTransform)
            {
                canvas.Restore();
            }
        }

        void DrawLineInvisibles(
            float lineX,
            float lineY,
            float lineHeight,
            float lineAscent,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            DocTextDirection? textDirection,
            float gridSpacing,
            bool showParagraphMark,
            float paragraphMarkOffset)
        {
            if (!options.ShowInvisibles)
            {
                return;
            }

            var originX = lineX;
            var originY = lineY;
            var restoreTransform = false;
            if (DocTextDirectionHelpers.IsVertical(textDirection))
            {
                restoreTransform = true;
                canvas.Save();
                canvas.Translate(lineX, lineY);
                canvas.RotateDegrees(DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value));
                originX = 0f;
                originY = 0f;
            }

            var baseline = originY + lineAscent;
            var dotY = baseline - lineAscent * 0.2f;
            var arrowSize = MathF.Max(4f, lineAscent * 0.3f);

            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);

            float MeasureOffsetFromSegments(int length)
            {
                if (length <= 0 || segments.Count == 0)
                {
                    return 0f;
                }

                var totalWidth = segments[^1].X + segments[^1].Width;
                var target = Math.Clamp(length, 0, int.MaxValue);
                VisualSegment? containing = null;
                foreach (var segment in segments)
                {
                    if (target == segment.StartOffset)
                    {
                        containing = segment;
                        break;
                    }

                    if (containing is null && target > segment.StartOffset && target <= segment.StartOffset + segment.Length)
                    {
                        containing = segment;
                    }
                }

                if (containing is not null)
                {
                    var offsetInSegment = Math.Clamp(target - containing.StartOffset, 0, containing.Length);
                    var localX = MeasureSegmentOffset(containing, offsetInSegment, gridSpacing, GetRunMetrics);
                    return containing.X + localX;
                }

                return target <= 0 ? 0f : totalWidth;
            }

            foreach (var segment in segments)
            {
                if (!segment.IsTab)
                {
                    continue;
                }

                if (segment.Run?.Style.Hidden == true)
                {
                    continue;
                }

                var startX = originX + segment.X;
                var endX = startX + segment.Width;
                if (segment.IsRtl)
                {
                    (startX, endX) = (endX, startX);
                }

                var lineEnd = segment.IsRtl
                    ? MathF.Min(startX, endX + arrowSize)
                    : MathF.Max(startX, endX - arrowSize);
                canvas.DrawLine(startX, baseline, lineEnd, baseline, invisiblesStrokePaint);
                canvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline - arrowSize * 0.4f, invisiblesStrokePaint);
                canvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline + arrowSize * 0.4f, invisiblesStrokePaint);
            }

            for (var i = 0; i < lineText.Length; i++)
            {
                var ch = lineText[i];
                if (ch != ' ' && ch != '\u00A0')
                {
                    continue;
                }

                var startX = originX + MeasureOffsetFromSegments(i);
                var endX = originX + MeasureOffsetFromSegments(i + 1);
                var dotX = (startX + endX) / 2f;
                canvas.DrawCircle(dotX, dotY, 1.3f, invisiblesFillPaint);
            }

            if (showParagraphMark)
            {
                var markStyle = defaultStyle;
                for (var i = runs.Count - 1; i >= 0; i--)
                {
                    var run = runs[i];
                    if (!run.IsTab && !string.IsNullOrEmpty(run.Text))
                    {
                        markStyle = run.Style;
                        break;
                    }
                }

                var markPaint = GetInvisibleTextPaint(markStyle);
                canvas.DrawText("\u00B6", originX + paragraphMarkOffset + 2f, baseline, markPaint);
            }

            if (restoreTransform)
            {
                canvas.Restore();
            }
        }

        void DrawFloatingObject(FloatingLayoutObject floating)
        {
            var bounds = floating.Bounds;
            switch (floating.Object.Content)
            {
                case ImageInline image:
                {
                    var layoutImage = new LayoutImage(image, 0f, bounds.Width, bounds.Height, 1);
                    DrawImage(canvas, layoutImage, bounds.X, bounds.Y + bounds.Height, 0f, 0f, options);
                    break;
                }
                case ShapeInline shape:
                {
                    var layoutShape = new LayoutShape(shape, 0f, bounds.Width, bounds.Height, 1);
                    DrawShape(canvas, layoutShape, bounds.X, bounds.Y + bounds.Height, 0f, options, defaultStyle, shapeDocument, shapeSettings);
                    break;
                }
                case ChartInline chart:
                {
                    var layoutChart = new LayoutChart(chart, 0f, bounds.Width, bounds.Height, 1);
                    DrawChart(canvas, layoutChart, bounds.X, bounds.Y + bounds.Height, options);
                    break;
                }
            }
        }

        void DrawFloatingObjects(bool behindText)
        {
            if (layout.FloatingObjects.Count == 0)
            {
                return;
            }

            foreach (var floating in layout.FloatingObjects)
            {
                if (floating.Object.Anchor.BehindText != behindText)
                {
                    continue;
                }

                DrawFloatingObject(floating);
            }
        }

        void DrawParagraphDecorations(int lineStart, int lineEnd)
        {
            if (lineEnd <= lineStart)
            {
                return;
            }

            var handled = new HashSet<int>();
            for (var lineIndex = lineStart; lineIndex < lineEnd; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                if (line.IsInTable)
                {
                    continue;
                }

                var paragraphIndex = line.ParagraphIndex;
                if (!handled.Add(paragraphIndex))
                {
                    continue;
                }

                if (!layout.ParagraphLineRanges.TryGetValue(paragraphIndex, out var range) || range.Count == 0)
                {
                    continue;
                }

                var segmentStart = Math.Clamp(Math.Max(range.Start, lineStart), lineStart, lineEnd);
                var segmentEnd = Math.Clamp(Math.Min(range.End, lineEnd), lineStart, lineEnd);
                if (segmentEnd <= segmentStart)
                {
                    continue;
                }

                var leftEdge = float.MaxValue;
                var rightEdge = float.MinValue;
                var topEdge = float.MaxValue;
                var bottomEdge = float.MinValue;

                for (var i = segmentStart; i < segmentEnd; i++)
                {
                    var segmentLine = layout.Lines[i];
                    if (segmentLine.IsInTable)
                    {
                        continue;
                    }

                    var lineLeft = segmentLine.X - (segmentLine.Prefix is null ? 0f : segmentLine.PrefixWidth);
                    var lineRight = segmentLine.X + segmentLine.Width;
                    leftEdge = MathF.Min(leftEdge, lineLeft);
                    rightEdge = MathF.Max(rightEdge, lineRight);
                    topEdge = MathF.Min(topEdge, segmentLine.Y);
                    bottomEdge = MathF.Max(bottomEdge, segmentLine.Y + segmentLine.LineHeight);
                }

                if (leftEdge >= rightEdge || topEdge >= bottomEdge)
                {
                    continue;
                }

                var paragraph = shapeDocument.GetParagraph(paragraphIndex);
                var properties = styleResolver.ResolveParagraphProperties(paragraph);
                var borders = properties.Borders;
                var leftSpace = borders.Left is { IsVisible: true } leftBorder ? MathF.Max(0f, leftBorder.Spacing ?? 0f) : 0f;
                var rightSpace = borders.Right is { IsVisible: true } rightBorder ? MathF.Max(0f, rightBorder.Spacing ?? 0f) : 0f;
                var topSpace = borders.Top is { IsVisible: true } topBorder ? MathF.Max(0f, topBorder.Spacing ?? 0f) : 0f;
                var bottomSpace = borders.Bottom is { IsVisible: true } bottomBorder ? MathF.Max(0f, bottomBorder.Spacing ?? 0f) : 0f;
                var borderLeft = leftEdge - leftSpace;
                var borderRight = rightEdge + rightSpace;
                var borderTop = topEdge - topSpace;
                var borderBottom = bottomEdge + bottomSpace;

                if (properties.ShadingColor is { } shading)
                {
                    if (borderRight > borderLeft && borderBottom > borderTop)
                    {
                        using var shadingPaint = new SKPaint
                        {
                            Style = SKPaintStyle.Fill,
                            Color = ToSkColor(shading),
                            IsAntialias = true
                        };
                        canvas.DrawRect(new SKRect(borderLeft, borderTop, borderRight, borderBottom), shadingPaint);
                    }
                }

                if (borders.HasAny)
                {
                    var drawTop = range.Start >= lineStart && range.Start < lineEnd;
                    var drawBottom = range.End <= lineEnd && range.End > lineStart;

                    if (drawTop && borders.Top is not null && borders.Top.IsVisible)
                    {
                        DrawBorderSegment(canvas, borders.Top, borderLeft, borderTop, borderRight, borderTop, GetBorderPaint);
                    }

                    if (drawBottom && borders.Bottom is not null && borders.Bottom.IsVisible)
                    {
                        DrawBorderSegment(canvas, borders.Bottom, borderLeft, borderBottom, borderRight, borderBottom, GetBorderPaint);
                    }

                    if (borders.Left is not null && borders.Left.IsVisible)
                    {
                        DrawBorderSegment(canvas, borders.Left, borderLeft, borderTop, borderLeft, borderBottom, GetBorderPaint);
                    }

                    if (borders.Right is not null && borders.Right.IsVisible)
                    {
                        DrawBorderSegment(canvas, borders.Right, borderRight, borderTop, borderRight, borderBottom, GetBorderPaint);
                    }
                }
            }
        }

        var clipContent = autoFit != ShapeTextAutoFit.ShapeToFitText
                          && (horizontalOverflow != ShapeTextOverflow.Overflow || verticalOverflow != ShapeTextOverflow.Overflow);
        var caretDrawn = false;
        canvas.Save();
        if (clipContent)
        {
            canvas.ClipRect(new SKRect(left, top, right, bottom));
        }

        canvas.Translate(left, startY);
        if (scale != 1f)
        {
            canvas.Scale(scale, scale);
        }

        DrawFloatingObjects(true);

        foreach (var table in layout.Tables)
        {
            if (table.Properties.ShadingColor is { } tableShading)
            {
                var tableBounds = table.Bounds;
                var tableRect = new SKRect(tableBounds.X, tableBounds.Y, tableBounds.Right, tableBounds.Bottom);
                using var tablePaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(tableShading),
                    IsAntialias = true
                };
                canvas.DrawRect(tableRect, tablePaint);
            }

            foreach (var cell in table.Cells)
            {
                var cellBounds = cell.Bounds;
                var cellRect = new SKRect(cellBounds.X, cellBounds.Y, cellBounds.Right, cellBounds.Bottom);
                if (cell.Properties.ShadingColor is { } shading)
                {
                    using var shadingPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = ToSkColor(shading),
                        IsAntialias = true
                    };
                    canvas.DrawRect(cellRect, shadingPaint);
                }

                if (cell.Lines.Count > 0)
                {
                    canvas.Save();
                    canvas.ClipRect(cellRect);
                    for (var lineIndex = 0; lineIndex < cell.Lines.Count; lineIndex++)
                    {
                        var line = cell.Lines[lineIndex];
                        var lineGridSpacing = ResolveLineGridSpacing(line.ParagraphIndex, 0);
                        DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                        DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                        if (drawSelection)
                        {
                            for (var selectionIndex = 0; selectionIndex < selectionRanges.Length; selectionIndex++)
                            {
                                var selectionRange = selectionRanges[selectionIndex];
                                if (!TryGetSelectionSpan(selectionRange, line.ParagraphIndex, line.StartOffset, line.Length, out var startOffset, out var endOffset))
                                {
                                    continue;
                                }

                                var selectionX1 = MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, startOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                                var selectionX2 = MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, endOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                                DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, selectionX1, selectionX2, selectionPaint);
                            }
                        }

                        DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, lineGridSpacing);
                        DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, lineGridSpacing, false, 0f);

                        if (drawCaret && !caretDrawn && line.ParagraphIndex == caretPosition.ParagraphIndex)
                        {
                            var lineStartOffset = line.StartOffset;
                            var lineEndOffset = line.StartOffset + line.Length;
                            if (caretPosition.Offset >= lineStartOffset && caretPosition.Offset <= lineEndOffset)
                            {
                                var isLastLine = lineIndex == cell.Lines.Count - 1
                                                 || cell.Lines[lineIndex + 1].ParagraphIndex != line.ParagraphIndex;
                                if (caretPosition.Offset != lineEndOffset || isLastLine)
                                {
                                    var offsetInLine = Math.Clamp(caretPosition.Offset - line.StartOffset, 0, line.Length);
                                    var caretX = MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, offsetInLine, lineGridSpacing, GetRunMetrics);
                                    DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, caretX, caretX + options.CaretThickness, caretPaint);
                                    caretDrawn = true;
                                }
                            }
                        }
                    }

                    canvas.Restore();
                }
            }

            DrawTableBorders(canvas, table, GetBorderPaint);
        }

        DrawParagraphDecorations(0, layout.Lines.Count);

        for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
        {
            var line = layout.Lines[lineIndex];
            if (line.IsInTable)
            {
                continue;
            }

            var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
            var lineGridSpacing = ResolveLineGridSpacing(line.ParagraphIndex, pageIndex);
            DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
            DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
            if (drawSelection)
            {
                for (var selectionIndex = 0; selectionIndex < selectionRanges.Length; selectionIndex++)
                {
                    var selectionRange = selectionRanges[selectionIndex];
                    if (!TryGetSelectionSpan(selectionRange, line, out var startOffset, out var endOffset))
                    {
                        continue;
                    }

                    var selectionX1 = MeasureLineOffset(line, startOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                    var selectionX2 = MeasureLineOffset(line, endOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                    DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, selectionX1, selectionX2, selectionPaint);
                }
            }
            DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, lineGridSpacing);

            var isLastLine = lineIndex == layout.Lines.Count - 1
                             || layout.Lines[lineIndex + 1].ParagraphIndex != line.ParagraphIndex;
            DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, lineGridSpacing, isLastLine, line.Width);

            if (drawCaret && !caretDrawn && line.ParagraphIndex == caretPosition.ParagraphIndex)
            {
                var lineStartOffset = line.StartOffset;
                var lineEndOffset = line.StartOffset + line.Length;
                if (caretPosition.Offset >= lineStartOffset && caretPosition.Offset <= lineEndOffset)
                {
                    if (caretPosition.Offset != lineEndOffset || isLastLine)
                    {
                        var offsetInLine = Math.Clamp(caretPosition.Offset - line.StartOffset, 0, line.Length);
                        var caretX = MeasureLineOffset(line, offsetInLine, lineGridSpacing, GetRunMetrics);
                        DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, caretX, caretX + options.CaretThickness, caretPaint);
                        caretDrawn = true;
                    }
                }
            }
        }

        DrawFloatingObjects(false);

        canvas.Restore();

        foreach (var paint in paintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in typefacePaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in highlightPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in borderPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in invisibleTextPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var shaper in shaperCache.Values)
        {
            shaper.Dispose();
        }

        foreach (var shaper in typefaceShaperCache.Values)
        {
            shaper.Dispose();
        }
    }

    private static Document CreateShapeTextDocument(Document source, ShapeTextBox textBox, TextStyle defaultStyle)
    {
        var shapeDocument = new Document();
        shapeDocument.Blocks.Clear();
        if (textBox.Blocks.Count > 0)
        {
            shapeDocument.Blocks.AddRange(textBox.Blocks);
        }

        CopyTextStyle(defaultStyle, shapeDocument.DefaultTextStyle);
        CopyParagraphStyleProperties(source.DefaultParagraphStyleProperties, shapeDocument.DefaultParagraphStyleProperties);
        if (textBox.Properties.TextDirection.HasValue)
        {
            shapeDocument.DefaultParagraphStyleProperties.TextDirection = textBox.Properties.TextDirection;
        }

        CopyDocumentStyles(source.Styles, shapeDocument.Styles);
        CopyDocumentFonts(source.Fonts, shapeDocument.Fonts);
        CopyThemeColors(source.ThemeColors, shapeDocument.ThemeColors);
        CopyListDefinitions(source.ListDefinitions, shapeDocument.ListDefinitions);
        CopyNotes(source.Footnotes, shapeDocument.Footnotes);
        CopyNotes(source.Endnotes, shapeDocument.Endnotes);
        CopyNotes(source.Comments, shapeDocument.Comments);

        return shapeDocument;
    }

    private static void CopyTextStyle(TextStyle source, TextStyle target)
    {
        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private static void CopyParagraphStyleProperties(ParagraphStyleProperties source, ParagraphStyleProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private static void CopyDocumentStyles(DocumentStyles source, DocumentStyles target)
    {
        target.ParagraphStyles.Clear();
        foreach (var pair in source.ParagraphStyles)
        {
            target.ParagraphStyles[pair.Key] = pair.Value;
        }

        target.CharacterStyles.Clear();
        foreach (var pair in source.CharacterStyles)
        {
            target.CharacterStyles[pair.Key] = pair.Value;
        }

        target.TableStyles.Clear();
        foreach (var pair in source.TableStyles)
        {
            target.TableStyles[pair.Key] = pair.Value;
        }

        target.DefaultParagraphStyleId = source.DefaultParagraphStyleId;
        target.DefaultCharacterStyleId = source.DefaultCharacterStyleId;
        target.DefaultTableStyleId = source.DefaultTableStyleId;
    }

    private static void CopyDocumentFonts(DocumentFonts source, DocumentFonts target)
    {
        target.FontTable.Clear();
        foreach (var pair in source.FontTable)
        {
            target.FontTable[pair.Key] = pair.Value;
        }

        target.Theme.Clear();
        foreach (var pair in source.Theme.Entries)
        {
            target.Theme.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        target.Clear();
        foreach (var pair in source.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyListDefinitions(IReadOnlyDictionary<int, ListDefinition> source, Dictionary<int, ListDefinition> target)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyNotes<T>(IReadOnlyDictionary<int, T> source, Dictionary<int, T> target)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
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
            "line" or "lineinv" or "straightconnector1" => ShapeKind.Line,
            "triangle" => ShapeKind.Triangle,
            "rttriangle" or "righttriangle" => ShapeKind.RightTriangle,
            "diamond" => ShapeKind.Diamond,
            "parallelogram" => ShapeKind.Parallelogram,
            "trapezoid" => ShapeKind.Trapezoid,
            "pentagon" => ShapeKind.Pentagon,
            "hexagon" => ShapeKind.Hexagon,
            "octagon" => ShapeKind.Octagon,
            "star5" or "star" => ShapeKind.Star5,
            "star8" => ShapeKind.Star8,
            "rightarrow" => ShapeKind.ArrowRight,
            "leftarrow" => ShapeKind.ArrowLeft,
            "uparrow" => ShapeKind.ArrowUp,
            "downarrow" => ShapeKind.ArrowDown,
            "chevron" => ShapeKind.Chevron,
            "plus" or "mathplus" => ShapeKind.Plus,
            "cross" or "x" or "mathmultiply" => ShapeKind.Cross,
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
            case ShapeKind.RightTriangle:
                path.MoveTo(rect.Left, rect.Top);
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
            case ShapeKind.Pentagon:
            {
                AddPolygon(path, rect, 5);
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
            case ShapeKind.Star5:
                AddStar(path, rect, 5, 0.45f);
                break;
            case ShapeKind.Star8:
                AddStar(path, rect, 8, 0.45f);
                break;
            case ShapeKind.ArrowRight:
                AddRightArrow(path, rect);
                break;
            case ShapeKind.ArrowLeft:
                AddLeftArrow(path, rect);
                break;
            case ShapeKind.ArrowUp:
                AddUpArrow(path, rect);
                break;
            case ShapeKind.ArrowDown:
                AddDownArrow(path, rect);
                break;
            case ShapeKind.Chevron:
                AddChevron(path, rect);
                break;
            case ShapeKind.Plus:
                AddPlus(path, rect);
                break;
            case ShapeKind.Cross:
                AddCross(path, rect);
                break;
            default:
                path.AddRect(rect);
                break;
        }

        return path;
    }

    private static void AddPolygon(SKPath path, SKRect rect, int sides)
    {
        var cx = rect.MidX;
        var cy = rect.MidY;
        var radius = MathF.Min(rect.Width, rect.Height) * 0.5f;
        for (var i = 0; i < sides; i++)
        {
            var angle = -MathF.PI / 2f + i * (2f * MathF.PI / sides);
            var x = cx + radius * MathF.Cos(angle);
            var y = cy + radius * MathF.Sin(angle);
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
    }

    private static void AddStar(SKPath path, SKRect rect, int points, float innerRatio)
    {
        var cx = rect.MidX;
        var cy = rect.MidY;
        var outer = MathF.Min(rect.Width, rect.Height) * 0.5f;
        var inner = outer * Math.Clamp(innerRatio, 0.1f, 0.9f);
        var total = points * 2;
        for (var i = 0; i < total; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -MathF.PI / 2f + i * (MathF.PI / points);
            var x = cx + radius * MathF.Cos(angle);
            var y = cy + radius * MathF.Sin(angle);
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
    }

    private static void AddRightArrow(SKPath path, SKRect rect)
    {
        var headWidth = rect.Width * 0.35f;
        var shaftHeight = rect.Height * 0.4f;
        var shaftTop = rect.MidY - shaftHeight * 0.5f;
        var shaftBottom = rect.MidY + shaftHeight * 0.5f;

        path.MoveTo(rect.Left, shaftTop);
        path.LineTo(rect.Right - headWidth, shaftTop);
        path.LineTo(rect.Right - headWidth, rect.Top);
        path.LineTo(rect.Right, rect.MidY);
        path.LineTo(rect.Right - headWidth, rect.Bottom);
        path.LineTo(rect.Right - headWidth, shaftBottom);
        path.LineTo(rect.Left, shaftBottom);
        path.Close();
    }

    private static void AddLeftArrow(SKPath path, SKRect rect)
    {
        var headWidth = rect.Width * 0.35f;
        var shaftHeight = rect.Height * 0.4f;
        var shaftTop = rect.MidY - shaftHeight * 0.5f;
        var shaftBottom = rect.MidY + shaftHeight * 0.5f;

        path.MoveTo(rect.Right, shaftTop);
        path.LineTo(rect.Left + headWidth, shaftTop);
        path.LineTo(rect.Left + headWidth, rect.Top);
        path.LineTo(rect.Left, rect.MidY);
        path.LineTo(rect.Left + headWidth, rect.Bottom);
        path.LineTo(rect.Left + headWidth, shaftBottom);
        path.LineTo(rect.Right, shaftBottom);
        path.Close();
    }

    private static void AddUpArrow(SKPath path, SKRect rect)
    {
        var headHeight = rect.Height * 0.35f;
        var shaftWidth = rect.Width * 0.4f;
        var shaftLeft = rect.MidX - shaftWidth * 0.5f;
        var shaftRight = rect.MidX + shaftWidth * 0.5f;

        path.MoveTo(shaftLeft, rect.Bottom);
        path.LineTo(shaftLeft, rect.Top + headHeight);
        path.LineTo(rect.Left, rect.Top + headHeight);
        path.LineTo(rect.MidX, rect.Top);
        path.LineTo(rect.Right, rect.Top + headHeight);
        path.LineTo(shaftRight, rect.Top + headHeight);
        path.LineTo(shaftRight, rect.Bottom);
        path.Close();
    }

    private static void AddDownArrow(SKPath path, SKRect rect)
    {
        var headHeight = rect.Height * 0.35f;
        var shaftWidth = rect.Width * 0.4f;
        var shaftLeft = rect.MidX - shaftWidth * 0.5f;
        var shaftRight = rect.MidX + shaftWidth * 0.5f;

        path.MoveTo(shaftLeft, rect.Top);
        path.LineTo(shaftLeft, rect.Bottom - headHeight);
        path.LineTo(rect.Left, rect.Bottom - headHeight);
        path.LineTo(rect.MidX, rect.Bottom);
        path.LineTo(rect.Right, rect.Bottom - headHeight);
        path.LineTo(shaftRight, rect.Bottom - headHeight);
        path.LineTo(shaftRight, rect.Top);
        path.Close();
    }

    private static void AddChevron(SKPath path, SKRect rect)
    {
        var inset = rect.Width * 0.25f;
        path.MoveTo(rect.Left, rect.Top + rect.Height * 0.15f);
        path.LineTo(rect.Left + inset, rect.Top);
        path.LineTo(rect.Right, rect.MidY);
        path.LineTo(rect.Left + inset, rect.Bottom);
        path.LineTo(rect.Left, rect.Bottom - rect.Height * 0.15f);
        path.LineTo(rect.Right - inset, rect.MidY);
        path.Close();
    }

    private static void AddPlus(SKPath path, SKRect rect)
    {
        var thickness = MathF.Min(rect.Width, rect.Height) * 0.3f;
        var half = thickness * 0.5f;
        var cx = rect.MidX;
        var cy = rect.MidY;

        path.MoveTo(cx - half, rect.Top);
        path.LineTo(cx + half, rect.Top);
        path.LineTo(cx + half, cy - half);
        path.LineTo(rect.Right, cy - half);
        path.LineTo(rect.Right, cy + half);
        path.LineTo(cx + half, cy + half);
        path.LineTo(cx + half, rect.Bottom);
        path.LineTo(cx - half, rect.Bottom);
        path.LineTo(cx - half, cy + half);
        path.LineTo(rect.Left, cy + half);
        path.LineTo(rect.Left, cy - half);
        path.LineTo(cx - half, cy - half);
        path.Close();
    }

    private static void AddCross(SKPath path, SKRect rect)
    {
        var thickness = MathF.Min(rect.Width, rect.Height) * 0.25f;
        var half = thickness * 0.5f;
        var cx = rect.MidX;
        var cy = rect.MidY;

        path.MoveTo(cx - half, rect.Top);
        path.LineTo(cx + half, rect.Top);
        path.LineTo(rect.Right, cy - half);
        path.LineTo(rect.Right, cy + half);
        path.LineTo(cx + half, rect.Bottom);
        path.LineTo(cx - half, rect.Bottom);
        path.LineTo(rect.Left, cy + half);
        path.LineTo(rect.Left, cy - half);
        path.Close();
    }

    private enum ShapeKind
    {
        Unknown,
        Rectangle,
        RoundRectangle,
        Ellipse,
        Line,
        Triangle,
        RightTriangle,
        Diamond,
        Parallelogram,
        Trapezoid,
        Pentagon,
        Hexagon,
        Octagon,
        Star5,
        Star8,
        ArrowRight,
        ArrowLeft,
        ArrowUp,
        ArrowDown,
        Chevron,
        Plus,
        Cross
    }
}
