using System.Text;
using SkiaSharp;
using Svg.Skia;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;
using ProEdit.Rendering;

namespace ProEdit.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private readonly Dictionary<Guid, SvgPictureInfo> _svgPictureCache = new();
    private readonly Dictionary<SvgRasterKey, SKBitmap> _svgRasterCache = new();
    private readonly HashSet<Guid> _invalidSvgImages = new();
    private readonly Dictionary<Guid, SmartArtLayout> _smartArtLayoutCache = new();
    private void DrawImage(SKCanvas canvas, LayoutImage image, float lineX, float baseline, float lineHeight, float ascent, RenderOptions options)
    {
        var x = lineX + image.X;
        var y = baseline - image.Height;
        if (lineHeight > 0f && ascent > 0f)
        {
            var lineTop = baseline - ascent;
            if (lineHeight > image.Height)
            {
                // Center inline images in the line box to match Word's leading behavior.
                y = lineTop + (lineHeight - image.Height) * 0.5f;
            }
            else
            {
                y = lineTop;
            }
        }
        var dest = new SKRect(x, y, x + image.Width, y + image.Height);
        var effects = image.Image.Effects;
        var rotation = image.Image.Rotation;
        if (MathF.Abs(rotation) >= 0.01f)
        {
            var centerX = dest.MidX;
            var centerY = dest.MidY;
            canvas.Save();
            canvas.Translate(centerX, centerY);
            canvas.RotateDegrees(rotation);
            var rotatedDest = new SKRect(-dest.Width * 0.5f, -dest.Height * 0.5f, dest.Width * 0.5f, dest.Height * 0.5f);
            if (effects?.HasValues == true)
            {
                DrawImageWithEffects(canvas, image, rotatedDest, options, effects);
            }
            else
            {
                DrawImageContent(canvas, image, rotatedDest, options, null);
            }

            canvas.Restore();
            return;
        }

        if (effects?.HasValues == true)
        {
            DrawImageWithEffects(canvas, image, dest, options, effects);
            return;
        }

        DrawImageContent(canvas, image, dest, options, null);
    }

    private void DrawImageWithEffects(SKCanvas canvas, LayoutImage image, SKRect dest, RenderOptions options, DrawingEffects effects)
    {
        using var imagePaint = CreateImagePaint(effects.Color);
        if (effects.Shadow is not null)
        {
            DrawImageShadow(canvas, image, dest, options, effects.Shadow, imagePaint);
        }

        if (effects.Glow is not null)
        {
            DrawImageGlow(canvas, image, dest, options, effects.Glow, imagePaint);
        }

        if (effects.SoftEdge is not null)
        {
            DrawImageSoftEdge(canvas, image, dest, options, effects.SoftEdge, imagePaint);
        }

        DrawImageContent(canvas, image, dest, options, imagePaint);

        if (effects.Reflection is not null)
        {
            DrawImageReflection(canvas, image, dest, options, effects.Reflection, imagePaint);
        }
    }

    private void DrawImageContent(SKCanvas canvas, LayoutImage image, SKRect dest, RenderOptions options, SKPaint? imagePaint)
    {
        if (TryDrawSmartArt(canvas, image, dest, options))
        {
            return;
        }

        if (TryDrawSvg(canvas, image, dest, options, imagePaint))
        {
            return;
        }

        var bitmap = GetBitmap(image.Image);
        if (bitmap is null)
        {
            DrawImagePlaceholder(canvas, image, dest, options);
            return;
        }

        var source = ResolveImageCropRect(image.Image.Crop, bitmap);
        canvas.DrawBitmap(bitmap, source, dest, imagePaint);
    }

    private void DrawImagePlaceholder(SKCanvas canvas, LayoutImage image, SKRect rect, RenderOptions options)
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

        var label = GetPlaceholderLabel(image.Image);

        textPaint.TextSize = MathF.Max(8f, MathF.Min(14f, image.Height / 4f));
        var textY = rect.MidY + textPaint.TextSize * 0.35f;
        canvas.DrawText(label, rect.MidX, textY, textPaint);
    }

    private void DrawImageShadow(
        SKCanvas canvas,
        LayoutImage image,
        SKRect dest,
        RenderOptions options,
        DrawingShadowEffect shadow,
        SKPaint? imagePaint)
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
        var bounds = ExpandRect(
            dest,
            MathF.Max(0f, -dx) + blurRadius * 2f,
            MathF.Max(0f, -dy) + blurRadius * 2f,
            MathF.Max(0f, dx) + blurRadius * 2f,
            MathF.Max(0f, dy) + blurRadius * 2f);

        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateDropShadowOnly(dx, dy, blurRadius, blurRadius, ToSkColor(shadow.Color))
        };
        DrawImageEffectLayer(canvas, bounds, paint, image, dest, options, imagePaint);
    }

    private void DrawImageGlow(
        SKCanvas canvas,
        LayoutImage image,
        SKRect dest,
        RenderOptions options,
        DrawingGlowEffect glow,
        SKPaint? imagePaint)
    {
        var blurRadius = MathF.Max(0f, glow.Radius);
        if (blurRadius <= 0f)
        {
            return;
        }

        var bounds = ExpandRect(dest, blurRadius * 2f, blurRadius * 2f, blurRadius * 2f, blurRadius * 2f);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateDropShadowOnly(0f, 0f, blurRadius, blurRadius, ToSkColor(glow.Color))
        };
        DrawImageEffectLayer(canvas, bounds, paint, image, dest, options, imagePaint);
    }

    private void DrawImageSoftEdge(
        SKCanvas canvas,
        LayoutImage image,
        SKRect dest,
        RenderOptions options,
        DrawingSoftEdgeEffect softEdge,
        SKPaint? imagePaint)
    {
        var blurRadius = MathF.Max(0f, softEdge.Radius);
        if (blurRadius <= 0f)
        {
            return;
        }

        var bounds = ExpandRect(dest, blurRadius * 2f, blurRadius * 2f, blurRadius * 2f, blurRadius * 2f);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius)
        };
        DrawImageEffectLayer(canvas, bounds, paint, image, dest, options, imagePaint);
    }

    private void DrawImageReflection(
        SKCanvas canvas,
        LayoutImage image,
        SKRect dest,
        RenderOptions options,
        DrawingReflectionEffect reflection,
        SKPaint? imagePaint)
    {
        var scaleX = reflection.ScaleX > 0f ? reflection.ScaleX : 1f;
        var scaleY = reflection.ScaleY > 0f ? reflection.ScaleY : 1f;
        var reflectionHeight = dest.Height * scaleY;
        if (reflectionHeight <= 0f)
        {
            return;
        }

        var reflectionTop = dest.Bottom + reflection.Distance;
        var reflectionRect = new SKRect(dest.Left, reflectionTop, dest.Left + dest.Width * scaleX, reflectionTop + reflectionHeight);
        using var layerPaint = new SKPaint();
        if (reflection.BlurRadius > 0f)
        {
            layerPaint.ImageFilter = SKImageFilter.CreateBlur(reflection.BlurRadius, reflection.BlurRadius);
        }

        canvas.SaveLayer(reflectionRect, layerPaint);

        canvas.Save();
        canvas.Scale(scaleX, -scaleY);
        var tx = dest.Left - dest.Left * scaleX;
        var ty = dest.Bottom + reflection.Distance + dest.Bottom * scaleY;
        canvas.Translate(tx, ty);
        DrawImageContent(canvas, image, dest, options, imagePaint);
        canvas.Restore();

        using var maskPaint = CreateReflectionMaskPaint(reflectionRect, reflection.StartOpacity, reflection.EndOpacity);
        canvas.DrawRect(reflectionRect, maskPaint);
        canvas.Restore();
    }

    private void DrawImageEffectLayer(
        SKCanvas canvas,
        SKRect bounds,
        SKPaint paint,
        LayoutImage image,
        SKRect dest,
        RenderOptions options,
        SKPaint? imagePaint)
    {
        canvas.SaveLayer(bounds, paint);
        DrawImageContent(canvas, image, dest, options, imagePaint);
        canvas.Restore();
    }

    private bool TryDrawSmartArt(SKCanvas canvas, LayoutImage image, SKRect dest, RenderOptions options)
    {
        var diagram = image.Image.Diagram;
        if (diagram?.DataPart is null || diagram.DataPart.Length == 0)
        {
            return false;
        }

        if (!_smartArtLayoutCache.TryGetValue(image.Image.Id, out var layout))
        {
            layout = SmartArtLayoutEngine.TryBuildLayout(diagram, image.Width, image.Height);
            if (layout is not null)
            {
                _smartArtLayoutCache[image.Image.Id] = layout;
            }
        }

        if (layout is null || layout.Nodes.Count == 0)
        {
            return false;
        }

        var originX = dest.Left;
        var originY = dest.Top;
        var style = layout.Style;
        var nodeLineColor = style?.NodeLineColor ?? options.PlaceholderStrokeColor;
        var nodeLineWidth = MathF.Max(0.5f, style?.NodeLineWidth ?? 1f);
        var connectorColor = style?.ConnectorColor ?? nodeLineColor;
        var connectorWidth = MathF.Max(0.5f, style?.ConnectorWidth ?? nodeLineWidth);

        using var connectorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(connectorColor),
            StrokeWidth = connectorWidth,
            IsAntialias = true
        };

        var nodeLookup = layout.Nodes.ToDictionary(node => node.Id, node => node);
        foreach (var connector in layout.Connectors)
        {
            if (!nodeLookup.TryGetValue(connector.FromId, out var from)
                || !nodeLookup.TryGetValue(connector.ToId, out var to))
            {
                continue;
            }

            var startX = originX + from.Bounds.X + from.Bounds.Width / 2f;
            var startY = originY + from.Bounds.Y + from.Bounds.Height / 2f;
            var endX = originX + to.Bounds.X + to.Bounds.Width / 2f;
            var endY = originY + to.Bounds.Y + to.Bounds.Height / 2f;
            canvas.DrawLine(startX, startY, endX, endY, connectorPaint);
        }

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(nodeLineColor),
            StrokeWidth = nodeLineWidth,
            IsAntialias = true
        };
        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(style?.TextColor ?? options.TextColor),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            TextSize = style?.TextSize ?? 0f
        };

        var palette = style?.NodeFillPalette;
        var fallbackFill = options.PlaceholderFillColor;

        foreach (var node in layout.Nodes)
        {
            var rect = new SKRect(
                originX + node.Bounds.X,
                originY + node.Bounds.Y,
                originX + node.Bounds.X + node.Bounds.Width,
                originY + node.Bounds.Y + node.Bounds.Height);

            var radius = layout.Kind == SmartArtLayoutKind.Cycle
                || layout.Kind == SmartArtLayoutKind.Relationship
                ? MathF.Min(rect.Width, rect.Height) / 2f
                : MathF.Min(rect.Width, rect.Height) * 0.15f;

            var fillColor = fallbackFill;
            if (palette is { Count: > 0 })
            {
                fillColor = palette[node.Index % palette.Count];
            }

            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(fillColor),
                IsAntialias = true
            };

            if (layout.Kind == SmartArtLayoutKind.Cycle)
            {
                canvas.DrawOval(rect, fillPaint);
                canvas.DrawOval(rect, strokePaint);
            }
            else
            {
                canvas.DrawRoundRect(rect, radius, radius, fillPaint);
                canvas.DrawRoundRect(rect, radius, radius, strokePaint);
            }

            DrawSmartArtText(canvas, rect, node.Text, textPaint);
        }

        return true;
    }

    private static void DrawSmartArtText(SKCanvas canvas, SKRect rect, string text, SKPaint paint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var padding = MathF.Max(4f, rect.Height * 0.12f);
        var width = rect.Width - padding * 2f;
        var height = rect.Height - padding * 2f;
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        var defaultSize = MathF.Max(8f, MathF.Min(14f, rect.Height / 3f));
        var targetSize = paint.TextSize > 0f ? paint.TextSize : defaultSize;
        var maxSize = MathF.Max(6f, rect.Height * 0.45f);
        paint.TextSize = MathF.Min(targetSize, maxSize);
        var lines = WrapText(text, width, paint);
        if (lines.Count == 0)
        {
            return;
        }

        var metrics = paint.FontMetrics;
        var lineHeight = MathF.Max(1f, metrics.Descent - metrics.Ascent);
        var totalHeight = lineHeight * lines.Count;
        var startY = rect.MidY - totalHeight / 2f - metrics.Ascent;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = startY + lineHeight * i;
            canvas.DrawText(lines[i], rect.MidX, y, paint);
        }
    }

    private static List<string> WrapText(string text, float maxWidth, SKPaint paint)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        if (words.Length == 0)
        {
            return lines;
        }

        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            var candidate = current + " " + word;
            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static string GetPlaceholderLabel(ImageInline inline)
    {
        var embedded = inline.EmbeddedObject;
        if (embedded is not null)
        {
            return string.IsNullOrWhiteSpace(embedded.ProgId) ? "Embedded Object" : embedded.ProgId;
        }

        var contentType = inline.ContentType ?? string.Empty;
        if (inline.Diagram is not null || contentType.Contains("diagram", StringComparison.OrdinalIgnoreCase))
        {
            return "SmartArt";
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "Image";
        }

        if (contentType.Contains("ole", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("object", StringComparison.OrdinalIgnoreCase))
        {
            return "OLE Object";
        }

        return "Object";
    }

    private bool TryDrawSvg(SKCanvas canvas, LayoutImage image, SKRect dest, RenderOptions options, SKPaint? imagePaint)
    {
        if (!IsSvgImage(image.Image))
        {
            return false;
        }

        var renderMode = options.SvgRenderMode;
        var needsRaster = imagePaint?.ColorFilter is not null || (image.Image.Crop is { HasValues: true });
        if (needsRaster && renderMode == SvgRenderMode.Native)
        {
            renderMode = SvgRenderMode.Rasterize;
        }

        if (renderMode != SvgRenderMode.Rasterize)
        {
            var info = GetSvgPictureInfo(image.Image);
            if (info is not null && TryDrawSvgPicture(canvas, info, dest))
            {
                return true;
            }

            if (renderMode == SvgRenderMode.Native)
            {
                return false;
            }
        }

        var raster = GetSvgRaster(image.Image, dest.Width, dest.Height, options);
        if (raster is null)
        {
            return false;
        }

        var source = ResolveImageCropRect(image.Image.Crop, raster);
        canvas.DrawBitmap(raster, source, dest, imagePaint);
        return true;
    }

    private bool TryDrawSvgPicture(SKCanvas canvas, SvgPictureInfo info, SKRect dest)
    {
        if (info.Bounds.Width <= 0f || info.Bounds.Height <= 0f)
        {
            return false;
        }

        var scaleX = dest.Width / info.Bounds.Width;
        var scaleY = dest.Height / info.Bounds.Height;
        if (!float.IsFinite(scaleX) || !float.IsFinite(scaleY))
        {
            return false;
        }

        canvas.Save();
        canvas.Translate(dest.Left - info.Bounds.Left * scaleX, dest.Top - info.Bounds.Top * scaleY);
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(info.Picture);
        canvas.Restore();
        return true;
    }

    private SKBitmap? GetBitmap(ImageInline inline)
    {
        if (_invalidImages.Contains(inline.Id))
        {
            return null;
        }

        if (!_imageCache.TryGetValue(inline.Id, out var bitmap))
        {
            if (inline.Data is null || inline.Data.Length == 0)
            {
                _invalidImages.Add(inline.Id);
                return null;
            }

            try
            {
                bitmap = SKBitmap.Decode(inline.Data);
                if (bitmap is not null)
                {
                    _imageCache[inline.Id] = bitmap;
                }
            }
            catch
            {
                _invalidImages.Add(inline.Id);
                return null;
            }
        }

        return bitmap;
    }

    private SKBitmap? GetSvgRaster(ImageInline inline, float width, float height, RenderOptions options)
    {
        if (_invalidSvgImages.Contains(inline.Id))
        {
            return null;
        }

        var scale = options.SvgRasterizationScale <= 0f ? 1f : options.SvgRasterizationScale;
        var pixelWidth = Math.Max(1, (int)MathF.Round(width * scale));
        var pixelHeight = Math.Max(1, (int)MathF.Round(height * scale));
        var key = new SvgRasterKey(inline.Id, pixelWidth, pixelHeight);
        if (_svgRasterCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var raster = TryRasterizeSvg(inline, width, height, scale, options.SvgRasterizer, options.SvgRasterBackgroundColor);
        if (raster is null)
        {
            _invalidSvgImages.Add(inline.Id);
            return null;
        }

        _svgRasterCache[key] = raster;
        return raster;
    }

    private SKBitmap? TryRasterizeSvg(
        ImageInline inline,
        float width,
        float height,
        float scale,
        ISvgRasterizer? rasterizer,
        DocColor backgroundColor)
    {
        if (rasterizer is not null)
        {
            var options = new SvgRasterizationOptions(width, height, scale, backgroundColor);
            if (rasterizer.TryRasterize(inline.Data, options, out var result))
            {
                return DecodeRaster(result.Data);
            }
        }

        var info = GetSvgPictureInfo(inline);
        if (info is null)
        {
            return null;
        }

        return RasterizeSvgPicture(info, width, height, scale, backgroundColor);
    }

    private SKBitmap? RasterizeSvgPicture(SvgPictureInfo info, float width, float height, float scale, DocColor backgroundColor)
    {
        if (info.Bounds.Width <= 0f || info.Bounds.Height <= 0f)
        {
            return null;
        }

        var pixelWidth = Math.Max(1, (int)MathF.Round(width * scale));
        var pixelHeight = Math.Max(1, (int)MathF.Round(height * scale));
        var imageInfo = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        using var rasterCanvas = new SKCanvas(bitmap);

        if (backgroundColor.A > 0)
        {
            rasterCanvas.Clear(ToSkColor(backgroundColor));
        }
        else
        {
            rasterCanvas.Clear(SKColors.Transparent);
        }

        var scaleX = pixelWidth / info.Bounds.Width;
        var scaleY = pixelHeight / info.Bounds.Height;
        rasterCanvas.Scale(scaleX, scaleY);
        rasterCanvas.Translate(-info.Bounds.Left, -info.Bounds.Top);
        rasterCanvas.DrawPicture(info.Picture);
        rasterCanvas.Flush();
        return bitmap;
    }

    private SKBitmap? DecodeRaster(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        try
        {
            return SKBitmap.Decode(data.Span);
        }
        catch
        {
            return null;
        }
    }

    private SvgPictureInfo? GetSvgPictureInfo(ImageInline inline)
    {
        if (_invalidSvgImages.Contains(inline.Id))
        {
            return null;
        }

        if (_svgPictureCache.TryGetValue(inline.Id, out var cached))
        {
            return cached;
        }

        if (inline.Data is null || inline.Data.Length == 0)
        {
            _invalidSvgImages.Add(inline.Id);
            return null;
        }

        try
        {
            using var stream = new MemoryStream(inline.Data, writable: false);
            var svg = new SKSvg();
            var picture = svg.Load(stream);
            if (picture is null)
            {
                _invalidSvgImages.Add(inline.Id);
                return null;
            }

            var bounds = picture.CullRect;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                bounds = new SKRect(0f, 0f, MathF.Max(1f, inline.Width), MathF.Max(1f, inline.Height));
            }

            var info = new SvgPictureInfo(picture, bounds);
            _svgPictureCache[inline.Id] = info;
            return info;
        }
        catch
        {
            _invalidSvgImages.Add(inline.Id);
            return null;
        }
    }

    private static bool IsSvgImage(ImageInline inline)
    {
        if (!string.IsNullOrWhiteSpace(inline.ContentType)
            && inline.ContentType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LooksLikeSvg(inline.Data);
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return false;
        }

        var limit = Math.Min(data.Length, 2048);
        for (var i = 0; i <= limit - 4; i++)
        {
            if (data[i] != (byte)'<')
            {
                continue;
            }

            if (IsSvgTag(data, i + 1, limit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSvgTag(ReadOnlySpan<byte> data, int start, int limit)
    {
        var index = start;
        while (index < limit && IsAsciiWhiteSpace(data[index]))
        {
            index++;
        }

        if (index + 2 >= limit)
        {
            return false;
        }

        return ToLowerAscii(data[index]) == (byte)'s'
            && ToLowerAscii(data[index + 1]) == (byte)'v'
            && ToLowerAscii(data[index + 2]) == (byte)'g';
    }

    private static bool IsAsciiWhiteSpace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    private static byte ToLowerAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    private static SKPaint? CreateImagePaint(DrawingColorEffects? effects)
    {
        if (effects is null || !effects.HasValues)
        {
            return null;
        }

        var matrix = CreateColorMatrix(effects);
        return new SKPaint
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateColorMatrix(matrix)
        };
    }

    private static float[] CreateColorMatrix(DrawingColorEffects effects)
    {
        var matrix = CreateIdentityColorMatrix();

        if (effects.Saturation.HasValue)
        {
            var saturation = MathF.Max(0f, effects.Saturation.Value);
            var saturationMatrix = CreateSaturationMatrix(saturation);
            matrix = MultiplyColorMatrix(saturationMatrix, matrix);
        }

        if (effects.Tint.HasValue)
        {
            var tint = Math.Clamp(effects.Tint.Value, 0f, 1f);
            var tintMatrix = CreateTintMatrix(tint);
            matrix = MultiplyColorMatrix(tintMatrix, matrix);
        }

        if (effects.RecolorDark.HasValue || effects.RecolorLight.HasValue)
        {
            var dark = effects.RecolorDark ?? DocColor.Black;
            var light = effects.RecolorLight ?? DocColor.White;
            var recolorMatrix = CreateDuotoneMatrix(dark, light);
            matrix = MultiplyColorMatrix(recolorMatrix, matrix);
        }

        return matrix;
    }

    private static float[] CreateIdentityColorMatrix()
    {
        return new[]
        {
            1f, 0f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };
    }

    private static float[] CreateTintMatrix(float tint)
    {
        var scale = 1f - tint;
        var offset = 255f * tint;
        return new[]
        {
            scale, 0f, 0f, 0f, offset,
            0f, scale, 0f, 0f, offset,
            0f, 0f, scale, 0f, offset,
            0f, 0f, 0f, 1f, 0f
        };
    }

    private static float[] CreateSaturationMatrix(float saturation)
    {
        const float rw = 0.2126f;
        const float gw = 0.7152f;
        const float bw = 0.0722f;

        var inv = 1f - saturation;
        var r = inv * rw;
        var g = inv * gw;
        var b = inv * bw;

        return new[]
        {
            r + saturation, g, b, 0f, 0f,
            r, g + saturation, b, 0f, 0f,
            r, g, b + saturation, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };
    }

    private static float[] CreateDuotoneMatrix(DocColor dark, DocColor light)
    {
        const float rw = 0.2126f;
        const float gw = 0.7152f;
        const float bw = 0.0722f;

        var dr = light.R - dark.R;
        var dg = light.G - dark.G;
        var db = light.B - dark.B;

        return new[]
        {
            dr * rw, dr * gw, dr * bw, 0f, dark.R,
            dg * rw, dg * gw, dg * bw, 0f, dark.G,
            db * rw, db * gw, db * bw, 0f, dark.B,
            0f, 0f, 0f, 1f, 0f
        };
    }

    private static float[] MultiplyColorMatrix(float[] left, float[] right)
    {
        var result = new float[20];
        for (var row = 0; row < 4; row++)
        {
            var rowOffset = row * 5;
            var left0 = left[rowOffset];
            var left1 = left[rowOffset + 1];
            var left2 = left[rowOffset + 2];
            var left3 = left[rowOffset + 3];
            var left4 = left[rowOffset + 4];

            for (var col = 0; col < 4; col++)
            {
                var colOffset = col;
                result[rowOffset + col] =
                    left0 * right[colOffset]
                    + left1 * right[colOffset + 5]
                    + left2 * right[colOffset + 10]
                    + left3 * right[colOffset + 15];
            }

            result[rowOffset + 4] =
                left4
                + left0 * right[4]
                + left1 * right[9]
                + left2 * right[14]
                + left3 * right[19];
        }

        return result;
    }

    private readonly record struct SvgRasterKey(Guid Id, int Width, int Height);
    private sealed record SvgPictureInfo(SKPicture Picture, SKRect Bounds);
}
