using Vibe.Office.Documents;
using Vibe.Office.FlowDocument.Documents;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.RichText.Avalonia;

internal sealed class FlowDocumentEditBridge
{
    private readonly FlowDocumentConverter _toDocumentConverter;
    private readonly DocumentToFlowDocumentConverter _toFlowDocumentConverter;
    private readonly Dictionary<string, EmbeddedFlowUiElement> _embeddedUiElementsById = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _anchoredElementIds = new();
    private int _nextAnchorId = 1;
    private int _internalChangeDepth;

    public FlowDocumentEditBridge()
    {
        _toDocumentConverter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true,
            EmbeddedUiShapePrefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
        });

        _toFlowDocumentConverter = new DocumentToFlowDocumentConverter(new DocumentToFlowDocumentConverterOptions
        {
            EmbeddedUiShapePrefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix,
            EmbeddedUiElementsById = _embeddedUiElementsById,
            InlineUiPlaceholderChild = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText,
            BlockUiPlaceholderChild = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText,
            InlineUiPlaceholderText = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText,
            BlockUiPlaceholderText = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText
        });
    }

    public bool IsApplyingInternalChange => _internalChangeDepth > 0;

    public IReadOnlyDictionary<string, EmbeddedFlowUiElement> EmbeddedUiElementsById => _embeddedUiElementsById;

    public IReadOnlyDictionary<Guid, string> AnchoredElementIds => _anchoredElementIds;

    public Document ConvertToEditorDocument(FlowDocumentModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var _ = EnterInternalChange();
        var document = _toDocumentConverter.Convert(source);
        RebuildEmbeddedUiMap(_toDocumentConverter.EmbeddedUiElements);
        RebuildAnchorMap(document);
        return document;
    }

    public void SyncFlowDocumentFromEditor(Document source, FlowDocumentModel target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        using var _ = EnterInternalChange();
        var converted = _toFlowDocumentConverter.Convert(source);
        CopyFlowDocumentProperties(converted, target);
        ReplaceBlocks(converted, target);
        RebuildAnchorMap(source);
    }

    public bool TrySyncFlowDocumentFromEditorIncremental(
        Document source,
        FlowDocumentModel target,
        int? dirtyParagraphIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (!CanApplyIncrementalTopLevelParagraphSync(source, target, dirtyParagraphIndex, out var blockIndex))
        {
            return false;
        }

        using var _ = EnterInternalChange();
        if (!_toFlowDocumentConverter.TryConvertTopLevelBlock(source, blockIndex, out var convertedBlock))
        {
            return false;
        }

        target.Blocks[blockIndex] = convertedBlock;
        RebuildAnchorMap(source);
        return true;
    }

    private IDisposable EnterInternalChange()
    {
        _internalChangeDepth++;
        return new InternalChangeScope(this);
    }

    private static void CopyFlowDocumentProperties(FlowDocumentModel source, FlowDocumentModel target)
    {
        target.FontFamily = source.FontFamily;
        target.FontSize = source.FontSize;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.FontStretch = source.FontStretch;
        target.Foreground = source.Foreground;
        target.Background = source.Background;
        target.TextEffects = source.TextEffects;
        target.Typography = source.Typography;
        target.PageWidth = source.PageWidth;
        target.PageHeight = source.PageHeight;
        target.PagePadding = source.PagePadding;
        target.ColumnWidth = source.ColumnWidth;
        target.ColumnGap = source.ColumnGap;
        target.TextAlignment = source.TextAlignment;
        target.ColumnRuleBrush = source.ColumnRuleBrush;
        target.ColumnRuleWidth = source.ColumnRuleWidth;
        target.FlowDirection = source.FlowDirection;
        target.IsColumnWidthFlexible = source.IsColumnWidthFlexible;
        target.IsHyphenationEnabled = source.IsHyphenationEnabled;
        target.IsOptimalParagraphEnabled = source.IsOptimalParagraphEnabled;
        target.LineHeight = source.LineHeight;
        target.LineStackingStrategy = source.LineStackingStrategy;
        target.MaxPageHeight = source.MaxPageHeight;
        target.MaxPageWidth = source.MaxPageWidth;
        target.MinPageHeight = source.MinPageHeight;
        target.MinPageWidth = source.MinPageWidth;
    }

    private static void ReplaceBlocks(FlowDocumentModel source, FlowDocumentModel target)
    {
        target.Blocks.Clear();
        while (source.Blocks.Count > 0)
        {
            var block = source.Blocks[0];
            source.Blocks.RemoveAt(0);
            target.Blocks.Add(block);
        }
    }

    private static bool CanApplyIncrementalTopLevelParagraphSync(
        Document source,
        FlowDocumentModel target,
        int? dirtyParagraphIndex,
        out int blockIndex)
    {
        blockIndex = -1;
        if (!dirtyParagraphIndex.HasValue)
        {
            return false;
        }

        if (source.Blocks.Count == 0
            || source.Blocks.Count != target.Blocks.Count
            || source.ParagraphCount != source.Blocks.Count)
        {
            return false;
        }

        blockIndex = dirtyParagraphIndex.Value;
        if ((uint)blockIndex >= (uint)source.Blocks.Count)
        {
            return false;
        }

        for (var index = 0; index < source.Blocks.Count; index++)
        {
            if (source.Blocks[index] is not ParagraphBlock paragraph || paragraph.ListInfo is not null)
            {
                return false;
            }
        }

        return true;
    }

    private void RebuildEmbeddedUiMap(IReadOnlyList<EmbeddedFlowUiElement> elements)
    {
        _embeddedUiElementsById.Clear();
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            _embeddedUiElementsById[element.Id] = element;
        }
    }

    private void RebuildAnchorMap(Document document)
    {
        var active = new HashSet<Guid>();
        foreach (var floating in EnumerateFloatingObjects(document.Blocks))
        {
            active.Add(floating.Id);
            if (_anchoredElementIds.ContainsKey(floating.Id))
            {
                continue;
            }

            _anchoredElementIds[floating.Id] = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"anchor:{_nextAnchorId++}");
        }

        if (_anchoredElementIds.Count == active.Count)
        {
            return;
        }

        var stale = new List<Guid>();
        foreach (var id in _anchoredElementIds.Keys)
        {
            if (!active.Contains(id))
            {
                stale.Add(id);
            }
        }

        for (var index = 0; index < stale.Count; index++)
        {
            _anchoredElementIds.Remove(stale[index]);
        }
    }

    private static IEnumerable<FloatingObject> EnumerateFloatingObjects(IReadOnlyList<Block> blocks)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            if (block is ParagraphBlock paragraph)
            {
                for (var floatingIndex = 0; floatingIndex < paragraph.FloatingObjects.Count; floatingIndex++)
                {
                    var floating = paragraph.FloatingObjects[floatingIndex];
                    yield return floating;

                    if (floating.Content is ShapeInline shape && shape.TextBox is not null)
                    {
                        foreach (var nested in EnumerateFloatingObjects(shape.TextBox.Blocks))
                        {
                            yield return nested;
                        }
                    }
                }

                continue;
            }

            if (block is TableBlock table)
            {
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        foreach (var nested in EnumerateFloatingObjects(cell.Blocks))
                        {
                            yield return nested;
                        }
                    }
                }
            }
        }
    }

    private sealed class InternalChangeScope : IDisposable
    {
        private readonly FlowDocumentEditBridge _owner;
        private bool _disposed;

        public InternalChangeScope(FlowDocumentEditBridge owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner._internalChangeDepth = Math.Max(0, _owner._internalChangeDepth - 1);
        }
    }
}
