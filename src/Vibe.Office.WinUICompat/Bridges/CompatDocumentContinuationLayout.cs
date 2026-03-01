using Vibe.Office.Layout;
using Vibe.Office.WinUICompat.Documents;
using Vibe.Office.WinUICompat.Text;

namespace Vibe.Office.WinUICompat.Bridges;

public readonly record struct CompatDocumentContinuationSegment(
    int StartLineIndex,
    int LineCount,
    float StartY,
    float Height,
    bool HasOverflow)
{
    public int EndLineIndex => StartLineIndex + LineCount;

    public bool IsEmpty => LineCount <= 0;
}

public sealed class CompatDocumentContinuationLayout
{
    private const float LayoutViewportHeight = 100000f;
    private readonly RichEditTextDocument _renderDocument;
    private float _layoutWidth = 640f;

    public CompatDocumentContinuationLayout()
        : this(new RichEditTextDocument())
    {
    }

    public CompatDocumentContinuationLayout(RichEditTextDocument renderDocument)
    {
        _renderDocument = renderDocument ?? throw new ArgumentNullException(nameof(renderDocument));
        _renderDocument.IsReadOnly = true;
    }

    public RichEditTextDocument RenderDocument => _renderDocument;

    public int LineCount => _renderDocument.EditorLayout.Lines.Count;

    public float LayoutWidth => _layoutWidth;

    public bool HasLines => LineCount > 0;

    public IReadOnlyList<LayoutLine> Lines => _renderDocument.EditorLayout.Lines;

    public void UpdateSource(RichTextDocument source, float viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(source);

        _layoutWidth = Math.Max(1f, viewportWidth);
        _renderDocument.LoadDocumentSnapshot(source);
        _renderDocument.IsReadOnly = true;
        _renderDocument.UpdateViewport(_layoutWidth, LayoutViewportHeight);
    }

    public CompatDocumentContinuationSegment GetRemainingSegment(int startLineIndex)
    {
        var layout = _renderDocument.EditorLayout;
        var lines = layout.Lines;
        if (lines.Count == 0)
        {
            return CreateEmptySegment(startLineIndex);
        }

        var start = Math.Clamp(startLineIndex, 0, lines.Count);
        if (start >= lines.Count)
        {
            return CreateEmptySegment(start);
        }

        return BuildSegment(layout, start, lines.Count);
    }

    public CompatDocumentContinuationSegment GetSegmentByMaxLines(int startLineIndex, int maxLines)
    {
        var layout = _renderDocument.EditorLayout;
        var lines = layout.Lines;
        if (lines.Count == 0)
        {
            return CreateEmptySegment(startLineIndex);
        }

        var start = Math.Clamp(startLineIndex, 0, lines.Count);
        if (start >= lines.Count)
        {
            return CreateEmptySegment(start);
        }

        if (maxLines <= 0)
        {
            return BuildSegment(layout, start, lines.Count);
        }

        var end = Math.Min(lines.Count, start + maxLines);
        return BuildSegment(layout, start, end);
    }

    public CompatDocumentContinuationSegment GetSegmentByHeight(int startLineIndex, float viewportHeight)
    {
        var layout = _renderDocument.EditorLayout;
        var lines = layout.Lines;
        if (lines.Count == 0)
        {
            return CreateEmptySegment(startLineIndex);
        }

        var start = Math.Clamp(startLineIndex, 0, lines.Count);
        if (start >= lines.Count)
        {
            return CreateEmptySegment(start);
        }

        var availableHeight = Math.Max(1f, viewportHeight);
        var segmentTop = lines[start].Y;
        var segmentBottom = segmentTop + availableHeight;

        var end = start;
        while (end < lines.Count && lines[end].Y + lines[end].LineHeight <= segmentBottom + 0.5f)
        {
            end++;
        }

        if (end <= start)
        {
            end = Math.Min(lines.Count, start + 1);
        }

        return BuildSegment(layout, start, end);
    }

    private static CompatDocumentContinuationSegment BuildSegment(
        DocumentLayout layout,
        int startLineIndex,
        int endLineIndex)
    {
        var lines = layout.Lines;
        if (lines.Count == 0)
        {
            return CreateEmptySegment(startLineIndex);
        }

        var normalizedStart = Math.Clamp(startLineIndex, 0, lines.Count);
        var normalizedEnd = Math.Clamp(endLineIndex, normalizedStart, lines.Count);
        if (normalizedStart >= normalizedEnd)
        {
            return CreateEmptySegment(normalizedStart);
        }

        var startY = lines[normalizedStart].Y;
        var naturalEndY = normalizedEnd < lines.Count
            ? lines[normalizedEnd].Y
            : lines[^1].Y + lines[^1].LineHeight;
        var endY = ExtendSegmentBottomForFloatingObjects(layout, startY, naturalEndY);
        var contentBottom = GetContentBottom(layout);
        var height = Math.Max(1f, endY - startY);
        var lineCount = normalizedEnd - normalizedStart;
        var hasOverflow = endY + 0.5f < contentBottom;

        return new CompatDocumentContinuationSegment(
            normalizedStart,
            lineCount,
            startY,
            height,
            hasOverflow);
    }

    private static float ExtendSegmentBottomForFloatingObjects(DocumentLayout layout, float startY, float endY)
    {
        var extendedEndY = endY;
        ExtendWithFloatingObjects(layout.FloatingObjects, startY, ref extendedEndY);
        ExtendWithFloatingObjects(layout.ExtraFloatingObjects, startY, ref extendedEndY);
        return extendedEndY;
    }

    private static void ExtendWithFloatingObjects(
        IReadOnlyList<FloatingLayoutObject> floatingObjects,
        float startY,
        ref float endY)
    {
        for (var i = 0; i < floatingObjects.Count; i++)
        {
            var bounds = floatingObjects[i].Bounds;
            if (bounds.Top + 0.5f < startY || bounds.Top > endY + 0.5f)
            {
                continue;
            }

            endY = Math.Max(endY, bounds.Bottom);
        }
    }

    private static float GetContentBottom(DocumentLayout layout)
    {
        var lines = layout.Lines;
        var contentBottom = 1f;
        if (lines.Count > 0)
        {
            var last = lines[^1];
            contentBottom = Math.Max(contentBottom, last.Y + last.LineHeight);
        }

        for (var i = 0; i < layout.FloatingObjects.Count; i++)
        {
            contentBottom = Math.Max(contentBottom, layout.FloatingObjects[i].Bounds.Bottom);
        }

        for (var i = 0; i < layout.ExtraFloatingObjects.Count; i++)
        {
            contentBottom = Math.Max(contentBottom, layout.ExtraFloatingObjects[i].Bounds.Bottom);
        }

        return contentBottom;
    }

    private static CompatDocumentContinuationSegment CreateEmptySegment(int startLineIndex)
    {
        return new CompatDocumentContinuationSegment(Math.Max(0, startLineIndex), 0, 0f, 1f, false);
    }
}
