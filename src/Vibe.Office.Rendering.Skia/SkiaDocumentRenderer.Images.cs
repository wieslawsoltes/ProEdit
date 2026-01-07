using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer
{
    private void DrawImage(SKCanvas canvas, LayoutImage image, float lineX, float baseline, float ascent, RenderOptions options)
    {
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

            var contentType = image.Image.ContentType ?? string.Empty;
            var label = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? "Image"
                : contentType.Contains("ole", StringComparison.OrdinalIgnoreCase) || contentType.Contains("object", StringComparison.OrdinalIgnoreCase)
                    ? "OLE Object"
                    : "Object";

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
}
