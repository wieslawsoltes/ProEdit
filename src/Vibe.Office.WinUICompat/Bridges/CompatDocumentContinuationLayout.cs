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
        var lines = _renderDocument.EditorLayout.Lines;
        if (lines.Count == 0)
        {
            return CreateEmptySegment(startLineIndex);
        }

        var start = Math.Clamp(startLineIndex, 0, lines.Count);
        if (start >= lines.Count)
        {
            return CreateEmptySegment(start);
        }

        return BuildSegment(lines, start, lines.Count);
    }

    public CompatDocumentContinuationSegment GetSegmentByMaxLines(int startLineIndex, int maxLines)
    {
        var lines = _renderDocument.EditorLayout.Lines;
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
            return BuildSegment(lines, start, lines.Count);
        }

        var end = Math.Min(lines.Count, start + maxLines);
        return BuildSegment(lines, start, end);
    }

    public CompatDocumentContinuationSegment GetSegmentByHeight(int startLineIndex, float viewportHeight)
    {
        var lines = _renderDocument.EditorLayout.Lines;
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

        return BuildSegment(lines, start, end);
    }

    private static CompatDocumentContinuationSegment BuildSegment(
        IReadOnlyList<LayoutLine> lines,
        int startLineIndex,
        int endLineIndex)
    {
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
        var endY = normalizedEnd < lines.Count
            ? lines[normalizedEnd].Y
            : lines[^1].Y + lines[^1].LineHeight;
        var height = Math.Max(1f, endY - startY);
        var lineCount = normalizedEnd - normalizedStart;
        var hasOverflow = normalizedEnd < lines.Count;

        return new CompatDocumentContinuationSegment(
            normalizedStart,
            lineCount,
            startY,
            height,
            hasOverflow);
    }

    private static CompatDocumentContinuationSegment CreateEmptySegment(int startLineIndex)
    {
        return new CompatDocumentContinuationSegment(Math.Max(0, startLineIndex), 0, 0f, 1f, false);
    }
}
