using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public sealed class EditorLayoutService
{
    private readonly Document _document;
    private readonly ITextMeasurer _measurer;
    private readonly DocumentLayouter _layouter = new DocumentLayouter();

    public LayoutSettings Settings { get; } = new LayoutSettings();
    public DocumentLayout Layout { get; private set; }
    public IProofingSpanProvider? ProofingSpans { get; set; }

    public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    public EditorLayoutService(Document document, ITextMeasurer measurer)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        Layout = _layouter.Layout(_document, Settings, _measurer, ProofingSpans);
    }

    public IReadOnlyList<int> UpdateViewport(float viewportWidth, float viewportHeight)
    {
        Settings.ViewportWidth = viewportWidth;
        Settings.ViewportHeight = viewportHeight;
        return Reflow(null);
    }

    public IReadOnlyList<int> RefreshLayout(int? dirtyParagraphIndex)
    {
        return Reflow(dirtyParagraphIndex);
    }

    public IReadOnlyList<int> ComputeDirtyPagesForSelection(TextRange previousSelection, TextRange currentSelection)
    {
        if (Layout.Pages.Count == 0)
        {
            return Array.Empty<int>();
        }

        var previousRange = GetPageRangeForSelection(previousSelection);
        var currentRange = GetPageRangeForSelection(currentSelection);
        var start = Math.Min(previousRange.Start, currentRange.Start);
        var end = Math.Max(previousRange.End, currentRange.End);
        var count = Math.Max(0, end - start + 1);
        return count == 0 ? Array.Empty<int>() : Enumerable.Range(start, count).ToArray();
    }

    private IReadOnlyList<int> Reflow(int? dirtyParagraphIndex)
    {
        var previousLayout = Layout;
        Layout = _layouter.Layout(_document, Settings, _measurer, previousLayout, dirtyParagraphIndex, ProofingSpans);
        var dirtyPages = ComputeDirtyPages(previousLayout, Layout, dirtyParagraphIndex);
        LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(dirtyPages));
        return dirtyPages;
    }

    private IReadOnlyList<int> ComputeDirtyPages(DocumentLayout? previousLayout, DocumentLayout currentLayout, int? dirtyParagraphIndex)
    {
        if (currentLayout.Pages.Count == 0)
        {
            return Array.Empty<int>();
        }

        if (previousLayout is null || !dirtyParagraphIndex.HasValue || dirtyParagraphIndex.Value < 0)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        if (!previousLayout.ParagraphLineRanges.TryGetValue(dirtyParagraphIndex.Value, out var range) || range.Count == 0)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        var startPage = previousLayout.LineIndex.GetPageForLine(range.Start);
        if (startPage < 0 || startPage >= currentLayout.Pages.Count)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        return Enumerable.Range(startPage, currentLayout.Pages.Count - startPage).ToArray();
    }

    private PageRange GetPageRangeForSelection(TextRange selection)
    {
        var startPage = GetPageForPosition(selection.Start);
        var endPage = GetPageForPosition(selection.End);
        if (startPage > endPage)
        {
            (startPage, endPage) = (endPage, startPage);
        }

        return new PageRange(startPage, endPage);
    }

    private int GetPageForPosition(TextPosition position)
    {
        if (Layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(Layout, position, out _);
        var pageIndex = Layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 0 : pageIndex;
    }

    private readonly struct PageRange
    {
        public int Start { get; }
        public int End { get; }

        public PageRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }
}

public sealed class LayoutChangedEventArgs : EventArgs
{
    public IReadOnlyList<int> DirtyPages { get; }

    public LayoutChangedEventArgs(IReadOnlyList<int> dirtyPages)
    {
        DirtyPages = dirtyPages;
    }
}
