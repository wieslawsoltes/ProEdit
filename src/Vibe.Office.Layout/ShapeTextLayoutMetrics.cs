using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public readonly record struct ShapeTextLayoutMetrics(
    DocRect TextBounds,
    DocRect ContentBounds,
    float Scale,
    float OriginX,
    float OriginY,
    bool HasContent);

public static class ShapeTextLayoutHelper
{
    public static bool TryComputeMetrics(
        DocumentLayout layout,
        ShapeTextBox textBox,
        DocRect textBounds,
        out ShapeTextLayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(textBox);

        if (textBounds.Width <= 1f || textBounds.Height <= 1f)
        {
            metrics = default;
            return false;
        }

        var contentLeft = float.PositiveInfinity;
        var contentTop = float.PositiveInfinity;
        var contentRight = float.NegativeInfinity;
        var contentBottom = float.NegativeInfinity;
        var hasContent = false;

        void TrackBounds(float boundLeft, float boundTop, float boundRight, float boundBottom)
        {
            if (boundRight <= boundLeft || boundBottom <= boundTop)
            {
                return;
            }

            if (boundLeft < contentLeft)
            {
                contentLeft = boundLeft;
            }

            if (boundTop < contentTop)
            {
                contentTop = boundTop;
            }

            if (boundRight > contentRight)
            {
                contentRight = boundRight;
            }

            if (boundBottom > contentBottom)
            {
                contentBottom = boundBottom;
            }

            hasContent = true;
        }

        foreach (var line in layout.Lines)
        {
            if (line.IsInTable)
            {
                continue;
            }

            var lineLeft = line.X - (line.Prefix is null ? 0f : line.PrefixWidth);
            var lineRight = line.X + line.Width;
            var lineBottom = line.Y + line.LineHeight;
            TrackBounds(lineLeft, line.Y, lineRight, lineBottom);
        }

        foreach (var table in layout.Tables)
        {
            var tableBounds = table.Bounds;
            TrackBounds(tableBounds.X, tableBounds.Y, tableBounds.Right, tableBounds.Bottom);
        }

        foreach (var floating in layout.FloatingObjects)
        {
            var floatingBounds = floating.Bounds;
            TrackBounds(floatingBounds.X, floatingBounds.Y, floatingBounds.Right, floatingBounds.Bottom);
        }

        if (!hasContent)
        {
            metrics = new ShapeTextLayoutMetrics(
                textBounds,
                new DocRect(0f, 0f, 0f, 0f),
                1f,
                textBounds.X,
                textBounds.Y,
                false);
            return true;
        }

        var contentWidth = MathF.Max(0f, contentRight - contentLeft);
        var contentHeight = MathF.Max(0f, contentBottom - contentTop);
        var extentLeft = MathF.Min(0f, contentLeft);
        var extentTop = MathF.Min(0f, contentTop);
        var extentRight = MathF.Max(0f, contentRight);
        var extentBottom = MathF.Max(0f, contentBottom);
        var occupiedWidth = MathF.Max(0f, extentRight - extentLeft);
        var occupiedHeight = MathF.Max(0f, extentBottom - extentTop);
        var scale = 1f;
        if (textBox.Properties.AutoFit == ShapeTextAutoFit.TextToFitShape && occupiedWidth > 0f && occupiedHeight > 0f)
        {
            var scaleX = textBounds.Width / occupiedWidth;
            var scaleY = textBounds.Height / occupiedHeight;
            var targetScale = MathF.Min(scaleX, scaleY);
            if (targetScale > 0f && targetScale < 1f)
            {
                scale = targetScale;
            }
        }

        var effectiveOccupiedHeight = occupiedHeight * scale;
        var originX = textBounds.X - (extentLeft * scale);
        var originY = textBounds.Y - (extentTop * scale);
        if (effectiveOccupiedHeight < textBounds.Height)
        {
            var alignmentOffset = textBox.Properties.VerticalAlignment switch
            {
                ShapeTextVerticalAlignment.Center => (textBounds.Height - effectiveOccupiedHeight) / 2f,
                ShapeTextVerticalAlignment.Bottom => textBounds.Height - effectiveOccupiedHeight,
                _ => 0f
            };

            originY += alignmentOffset;
        }

        metrics = new ShapeTextLayoutMetrics(
            textBounds,
            new DocRect(contentLeft, contentTop, contentWidth, contentHeight),
            scale,
            originX,
            originY,
            true);
        return true;
    }
}
