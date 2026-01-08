using System.Text;
using SkiaSharp;
using Svg.Skia;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private readonly Dictionary<Guid, SvgPictureInfo> _svgPictureCache = new();
    private readonly Dictionary<SvgRasterKey, SKBitmap> _svgRasterCache = new();
    private readonly HashSet<Guid> _invalidSvgImages = new();
    private readonly Dictionary<Guid, SmartArtLayout> _smartArtLayoutCache = new();
    private void DrawImage(SKCanvas canvas, LayoutImage image, float lineX, float baseline, float ascent, RenderOptions options)
    {
        if (TryDrawSmartArt(canvas, image, lineX, baseline, options))
        {
            return;
        }

        if (TryDrawSvg(canvas, image, lineX, baseline, options))
        {
            return;
        }

        var bitmap = GetBitmap(image.Image);
        if (bitmap is null)
        {
            var placeholderX = lineX + image.X;
            var placeholderY = baseline - image.Height;
            var rect = new SKRect(placeholderX, placeholderY, placeholderX + image.Width, placeholderY + image.Height);
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
            return;
        }

        var x = lineX + image.X;
        var y = baseline - image.Height;
        var dest = new SKRect(x, y, x + image.Width, y + image.Height);
        canvas.DrawBitmap(bitmap, dest);
    }

    private bool TryDrawSmartArt(SKCanvas canvas, LayoutImage image, float lineX, float baseline, RenderOptions options)
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

        var originX = lineX + image.X;
        var originY = baseline - image.Height;
        using var connectorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PlaceholderStrokeColor),
            StrokeWidth = 1f,
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

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PlaceholderFillColor),
            IsAntialias = true
        };
        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PlaceholderStrokeColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.TextColor),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        foreach (var node in layout.Nodes)
        {
            var rect = new SKRect(
                originX + node.Bounds.X,
                originY + node.Bounds.Y,
                originX + node.Bounds.X + node.Bounds.Width,
                originY + node.Bounds.Y + node.Bounds.Height);

            var radius = layout.Kind == SmartArtLayoutKind.Cycle
                ? MathF.Min(rect.Width, rect.Height) / 2f
                : MathF.Min(rect.Width, rect.Height) * 0.15f;

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

        paint.TextSize = MathF.Max(8f, MathF.Min(14f, rect.Height / 3f));
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

    private bool TryDrawSvg(SKCanvas canvas, LayoutImage image, float lineX, float baseline, RenderOptions options)
    {
        if (!IsSvgImage(image.Image))
        {
            return false;
        }

        var renderMode = options.SvgRenderMode;
        if (renderMode != SvgRenderMode.Rasterize)
        {
            var info = GetSvgPictureInfo(image.Image);
            if (info is not null && TryDrawSvgPicture(canvas, info, image, lineX, baseline))
            {
                return true;
            }

            if (renderMode == SvgRenderMode.Native)
            {
                return false;
            }
        }

        var raster = GetSvgRaster(image.Image, image.Width, image.Height, options);
        if (raster is null)
        {
            return false;
        }

        var x = lineX + image.X;
        var y = baseline - image.Height;
        var dest = new SKRect(x, y, x + image.Width, y + image.Height);
        canvas.DrawBitmap(raster, dest);
        return true;
    }

    private bool TryDrawSvgPicture(SKCanvas canvas, SvgPictureInfo info, LayoutImage image, float lineX, float baseline)
    {
        if (info.Bounds.Width <= 0f || info.Bounds.Height <= 0f)
        {
            return false;
        }

        var x = lineX + image.X;
        var y = baseline - image.Height;
        var scaleX = image.Width / info.Bounds.Width;
        var scaleY = image.Height / info.Bounds.Height;
        if (!float.IsFinite(scaleX) || !float.IsFinite(scaleY))
        {
            return false;
        }

        canvas.Save();
        canvas.Translate(x - info.Bounds.Left * scaleX, y - info.Bounds.Top * scaleY);
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

    private readonly record struct SvgRasterKey(Guid Id, int Width, int Height);
    private sealed record SvgPictureInfo(SKPicture Picture, SKRect Bounds);
}
