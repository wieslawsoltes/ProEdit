using ProEdit.Documents;
using ProEdit.FlowDocument.Documents;
using ProEdit.WinUICompat.Converters;
using ProEdit.WinUICompat.Documents;

namespace ProEdit.WinUICompat.Bridges;

public sealed class CompatDocumentBridge : ICompatEmbeddedUiBridge
{
    private readonly CompatFlowDocumentConverter _compatConverter;
    private readonly FlowDocumentConverterOptions? _toEditorOptions;
    private readonly DocumentToFlowDocumentConverterOptions? _fromEditorOptions;
    private readonly FlowDocumentConverter _toEditorConverter;
    private readonly DocumentToFlowDocumentConverter _fromEditorConverter;
    private readonly Dictionary<string, EmbeddedFlowUiElement> _embeddedUiElementsById = new(StringComparer.Ordinal);

    public CompatDocumentBridge()
        : this(options: null)
    {
    }

    public CompatDocumentBridge(CompatDocumentBridgeOptions? options)
        : this(new CompatFlowDocumentConverter(), options)
    {
    }

    public CompatDocumentBridge(
        CompatFlowDocumentConverter compatConverter,
        CompatDocumentBridgeOptions? options)
    {
        _compatConverter = compatConverter ?? throw new ArgumentNullException(nameof(compatConverter));
        options ??= new CompatDocumentBridgeOptions();

        var embeddedPrefix = string.IsNullOrWhiteSpace(options.EmbeddedUiShapePrefix)
            ? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
            : options.EmbeddedUiShapePrefix;

        _toEditorOptions = new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = options.EnableEmbeddedUiElements,
            EmbeddedUiShapePrefix = embeddedPrefix,
            EmbeddedUiElementPredicate = options.EmbeddedUiElementPredicate,
            EmbeddedUiSizeResolver = options.EmbeddedUiSizeResolver
        };

        _fromEditorOptions = new DocumentToFlowDocumentConverterOptions
        {
            EmbeddedUiShapePrefix = embeddedPrefix,
            EmbeddedUiElementsById = _embeddedUiElementsById,
            InlineUiPlaceholderChild = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText,
            BlockUiPlaceholderChild = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText,
            InlineUiPlaceholderText = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText,
            BlockUiPlaceholderText = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText
        };

        _toEditorConverter = new FlowDocumentConverter(_toEditorOptions);
        _fromEditorConverter = new DocumentToFlowDocumentConverter(_fromEditorOptions);
    }

    public CompatDocumentBridge(
        CompatFlowDocumentConverter compatConverter,
        FlowDocumentConverter toEditorConverter,
        DocumentToFlowDocumentConverter fromEditorConverter)
    {
        _compatConverter = compatConverter ?? throw new ArgumentNullException(nameof(compatConverter));
        _toEditorConverter = toEditorConverter ?? throw new ArgumentNullException(nameof(toEditorConverter));
        _fromEditorConverter = fromEditorConverter ?? throw new ArgumentNullException(nameof(fromEditorConverter));
    }

    public IReadOnlyDictionary<string, EmbeddedFlowUiElement> EmbeddedUiElementsById => _embeddedUiElementsById;

    public string EmbeddedUiShapePrefix => _toEditorOptions?.EmbeddedUiShapePrefix
        ?? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;

    public Document ToEditorDocument(RichTextDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var flow = _compatConverter.ToFlowDocument(source);
        var converted = _toEditorConverter.Convert(flow);
        RebuildEmbeddedUiMap(_toEditorConverter.EmbeddedUiElements);
        return converted;
    }

    public RichTextDocument FromEditorDocument(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var flow = _fromEditorConverter.Convert(source);
        return _compatConverter.FromFlowDocument(flow);
    }

    public void SyncFromEditor(Document source, RichTextDocument target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var converted = FromEditorDocument(source);
        target.Blocks.Clear();
        for (var i = 0; i < converted.Blocks.Count; i++)
        {
            target.Blocks.Add(converted.Blocks[i]);
        }
    }

    public bool ConfigureEmbeddedUiElements(
        bool enabled,
        string shapePrefix,
        Func<object?, bool>? elementPredicate,
        Func<object, bool, (double Width, double Height)?>? sizeResolver)
    {
        if (_toEditorOptions is null || _fromEditorOptions is null)
        {
            return false;
        }

        var embeddedPrefix = string.IsNullOrWhiteSpace(shapePrefix)
            ? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
            : shapePrefix;

        var hasChanged =
            _toEditorOptions.EnableEmbeddedUiElements != enabled
            || !string.Equals(_toEditorOptions.EmbeddedUiShapePrefix, embeddedPrefix, StringComparison.Ordinal)
            || !ReferenceEquals(_toEditorOptions.EmbeddedUiElementPredicate, elementPredicate)
            || !ReferenceEquals(_toEditorOptions.EmbeddedUiSizeResolver, sizeResolver);

        if (!hasChanged)
        {
            return false;
        }

        _toEditorOptions.EnableEmbeddedUiElements = enabled;
        _toEditorOptions.EmbeddedUiShapePrefix = embeddedPrefix;
        _toEditorOptions.EmbeddedUiElementPredicate = elementPredicate;
        _toEditorOptions.EmbeddedUiSizeResolver = sizeResolver;
        _fromEditorOptions.EmbeddedUiShapePrefix = embeddedPrefix;

        if (!enabled)
        {
            _embeddedUiElementsById.Clear();
        }

        return true;
    }

    private void RebuildEmbeddedUiMap(IReadOnlyList<EmbeddedFlowUiElement> elements)
    {
        _embeddedUiElementsById.Clear();
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            _embeddedUiElementsById[element.Id] = element;
        }
    }
}
