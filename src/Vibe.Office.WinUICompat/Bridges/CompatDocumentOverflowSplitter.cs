using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Rendering.Skia;
using Vibe.Office.WinUICompat.Documents;
using EngineTextPosition = Vibe.Office.Documents.TextPosition;
using EngineTextRange = Vibe.Office.Documents.TextRange;

namespace Vibe.Office.WinUICompat.Bridges;

public readonly record struct CompatDocumentOverflowSplit(
    RichTextDocument VisibleDocument,
    RichTextDocument OverflowDocument,
    bool HasOverflow);

public sealed class CompatDocumentOverflowSplitter
{
    private readonly ICompatDocumentBridge _bridge;
    private readonly DocumentLayouter _layouter;
    private readonly SkiaTextMeasurer _measurer;

    public CompatDocumentOverflowSplitter()
        : this(new CompatDocumentBridge(), new DocumentLayouter(), new SkiaTextMeasurer())
    {
    }

    public CompatDocumentOverflowSplitter(
        ICompatDocumentBridge bridge,
        DocumentLayouter layouter,
        SkiaTextMeasurer measurer)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _layouter = layouter ?? throw new ArgumentNullException(nameof(layouter));
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
    }

    public CompatDocumentOverflowSplit SplitByMaxLines(RichTextDocument source, float viewportWidth, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (maxLines <= 0)
        {
            var clone = CloneCompat(source);
            return new CompatDocumentOverflowSplit(clone, CreateEmptyCompatDocument(), false);
        }

        var engine = _bridge.ToEditorDocument(source);
        var settings = new LayoutSettings
        {
            ViewportWidth = Math.Max(1f, viewportWidth),
            ViewportHeight = 100000f,
            UsePagination = false
        };
        var layout = _layouter.Layout(engine, settings, _measurer);
        var lines = layout.Lines;

        if (lines.Count <= maxLines)
        {
            var clone = CloneCompat(source);
            return new CompatDocumentOverflowSplit(clone, CreateEmptyCompatDocument(), false);
        }

        var overflowLine = lines[maxLines];
        var split = new EngineTextPosition(overflowLine.ParagraphIndex, overflowLine.StartOffset);
        return SplitEngineDocument(engine, split);
    }

    public CompatDocumentOverflowSplit SplitByHeight(RichTextDocument source, float viewportWidth, float viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(source);

        var engine = _bridge.ToEditorDocument(source);
        var settings = new LayoutSettings
        {
            ViewportWidth = Math.Max(1f, viewportWidth),
            ViewportHeight = Math.Max(1f, viewportHeight),
            UsePagination = false
        };

        var layout = _layouter.Layout(engine, settings, _measurer);
        var lines = layout.Lines;
        if (lines.Count == 0)
        {
            var clone = CloneCompat(source);
            return new CompatDocumentOverflowSplit(clone, CreateEmptyCompatDocument(), false);
        }

        var maxHeight = viewportHeight > 1f
            ? viewportHeight
            : Math.Max(1f, layout.LineHeight);

        var visibleLineCount = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Y + line.LineHeight <= maxHeight + 0.5f)
            {
                visibleLineCount++;
            }
            else
            {
                break;
            }
        }

        if (visibleLineCount >= lines.Count)
        {
            var clone = CloneCompat(source);
            return new CompatDocumentOverflowSplit(clone, CreateEmptyCompatDocument(), false);
        }

        if (visibleLineCount <= 0)
        {
            return new CompatDocumentOverflowSplit(CreateEmptyCompatDocument(), CloneCompat(source), HasVisibleContent(source));
        }

        var overflowLine = lines[visibleLineCount];
        var split = new EngineTextPosition(overflowLine.ParagraphIndex, overflowLine.StartOffset);
        return SplitEngineDocument(engine, split);
    }

    private CompatDocumentOverflowSplit SplitEngineDocument(Document engine, EngineTextPosition split)
    {
        if (TrySplitAtBlockBoundary(engine, split, out var blockBoundarySplit))
        {
            return blockBoundarySplit;
        }

        var start = new EngineTextPosition(0, 0);
        var end = GetDocumentEnd(engine);

        var visible = ExtractCompatRange(engine, start, split);
        var overflow = ExtractCompatRange(engine, split, end);
        var hasOverflow = HasVisibleContent(overflow);

        return new CompatDocumentOverflowSplit(visible, overflow, hasOverflow);
    }

    private bool TrySplitAtBlockBoundary(
        Document source,
        EngineTextPosition split,
        out CompatDocumentOverflowSplit result)
    {
        result = default;
        if (!TryResolveBlockSplitIndex(source, split, out var splitBlockIndex))
        {
            return false;
        }

        if (splitBlockIndex <= 0 || splitBlockIndex >= source.Blocks.Count)
        {
            return false;
        }

        var visibleEngine = CloneDocumentSkeleton(source);
        var overflowEngine = CloneDocumentSkeleton(source);

        for (var i = 0; i < splitBlockIndex; i++)
        {
            visibleEngine.Blocks.Add(DocumentClone.CloneBlock(source.Blocks[i]));
        }

        for (var i = splitBlockIndex; i < source.Blocks.Count; i++)
        {
            overflowEngine.Blocks.Add(DocumentClone.CloneBlock(source.Blocks[i]));
        }

        var visible = _bridge.FromEditorDocument(visibleEngine);
        var overflow = _bridge.FromEditorDocument(overflowEngine);
        EnsureCompatDocumentInitialized(visible);
        EnsureCompatDocumentInitialized(overflow);

        result = new CompatDocumentOverflowSplit(visible, overflow, HasVisibleContent(overflow));
        return true;
    }

    private RichTextDocument ExtractCompatRange(Document source, EngineTextPosition start, EngineTextPosition end)
    {
        var sessionDocument = ClipboardDocumentConverter.ToDocument(ClipboardDocumentConverter.FromDocument(source));
        var session = new Vibe.Word.Editor.EditorController(_measurer, sessionDocument);
        session.SetSelection(new EngineTextRange(start, end), SelectionUpdateMode.Replace);

        var clipboard = new NoOpClipboardService();
        var controller = new EditorClipboardController(session, clipboard);
        if (!controller.TryBuildSelectionContent(out var content)
            || content.Kind != ClipboardContentKind.Blocks
            || content.Fragment is null
            || content.Fragment.Blocks.Count == 0)
        {
            return CreateEmptyCompatDocument();
        }

        var fragmentDocument = ClipboardDocumentConverter.ToDocument(content);
        var compat = _bridge.FromEditorDocument(fragmentDocument);
        EnsureCompatDocumentInitialized(compat);
        return compat;
    }

    private static Document CloneDocumentSkeleton(Document source)
    {
        var clone = ClipboardDocumentConverter.ToDocument(ClipboardDocumentConverter.FromDocument(source));
        clone.Blocks.Clear();
        return clone;
    }

    private static bool TryResolveBlockSplitIndex(Document source, EngineTextPosition split, out int splitBlockIndex)
    {
        splitBlockIndex = -1;
        if (split.Offset != 0 || source.ParagraphCount <= 0)
        {
            return false;
        }

        var paragraphIndex = split.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= source.ParagraphCount)
        {
            return false;
        }

        var location = source.GetParagraphLocation(paragraphIndex);
        if (location.IsInTable)
        {
            if (location.RowIndex != 0 || location.ColumnIndex != 0 || location.ParagraphIndexInCell != 0)
            {
                return false;
            }
        }

        splitBlockIndex = location.BlockIndex;
        return true;
    }

    private static EngineTextPosition GetDocumentEnd(Document document)
    {
        var paragraphs = DocumentEditHelpers.BuildParagraphList(document);
        if (paragraphs.Count == 0)
        {
            return new EngineTextPosition(0, 0);
        }

        var lastIndex = paragraphs.Count - 1;
        var lastLength = DocumentEditHelpers.GetParagraphLength(paragraphs[lastIndex]);
        return new EngineTextPosition(lastIndex, lastLength);
    }

    private RichTextDocument CloneCompat(RichTextDocument source)
    {
        var clone = _bridge.FromEditorDocument(_bridge.ToEditorDocument(source));
        EnsureCompatDocumentInitialized(clone);
        return clone;
    }

    private static RichTextDocument CreateEmptyCompatDocument()
    {
        var document = new RichTextDocument();
        document.Blocks.Add(new Paragraph());
        return document;
    }

    private static bool HasVisibleContent(RichTextDocument document)
    {
        for (var i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is Paragraph paragraph)
            {
                for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
                {
                    switch (paragraph.Inlines[inlineIndex])
                    {
                        case Run run when !string.IsNullOrEmpty(run.Text):
                            return true;
                        case LineBreak:
                        case InlineUIContainer:
                            return true;
                    }
                }

                continue;
            }

            return true;
        }

        return false;
    }

    private static void EnsureCompatDocumentInitialized(RichTextDocument document)
    {
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }
    }

    private sealed class NoOpClipboardService : IClipboardService
    {
        private string _text = string.Empty;
        private ClipboardContent _content = ClipboardContent.Empty();

        public bool CanCopy => true;

        public bool CanCut => true;

        public bool CanPaste => true;

        public IReadOnlyList<string> SupportedFormats { get; } = new[] { "text/plain" };

        public bool TryGetText(out string text)
        {
            text = _text;
            return !string.IsNullOrEmpty(text);
        }

        public void SetText(string text)
        {
            _text = text ?? string.Empty;
        }

        public bool TryGetContent(out ClipboardContent content)
        {
            content = _content;
            return content.Kind != ClipboardContentKind.None;
        }

        public void SetContent(ClipboardContent content)
        {
            _content = content ?? ClipboardContent.Empty();
        }
    }
}
