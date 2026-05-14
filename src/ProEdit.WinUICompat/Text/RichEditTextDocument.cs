using System.Text;
using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.FlowDocument.Documents;
using ProEdit.Layout;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;
using ProEdit.WinUICompat.Bridges;
using ProEdit.WinUICompat.Documents;
using ProEdit.Word.Editor;
using ProEdit.Word.Editor.Editing;
using CompatDocument = ProEdit.WinUICompat.Documents.RichTextDocument;
using CompatTextPointer = ProEdit.WinUICompat.Documents.TextPointer;
using EngineTextPosition = ProEdit.Documents.TextPosition;
using EngineTextRange = ProEdit.Documents.TextRange;

namespace ProEdit.WinUICompat.Text;

public sealed class RichEditTextDocument : ITextDocument
{
    private const int MaxUndoDepth = 512;
    private static readonly IReadOnlyDictionary<string, EmbeddedFlowUiElement> EmptyEmbeddedUiElements =
        new Dictionary<string, EmbeddedFlowUiElement>();

    private readonly Stack<CompatDocument> _undo = new();
    private readonly Stack<CompatDocument> _redo = new();
    private readonly CompatDocument _document = new();
    private readonly ICompatDocumentBridge _documentBridge;
    private readonly SkiaTextMeasurer _textMeasurer;
    private readonly SkiaDocumentRenderer _renderer;
    private readonly EditorServices _services;
    private readonly IClipboardService _clipboardService;
    private EditorController _editor;
    private EditorCommandDispatcher _dispatcher;
    private EditorCommandInputRouter _inputRouter;
    private EditorCommandRouterAdapter _commandRouter;
    private EditorClipboardController _clipboardController;
    private RichEditTextRange _selection;
    private bool _suppressEditorChanged;
    private float _viewportWidth = 800f;
    private float _viewportHeight = 600f;

    public RichEditTextDocument()
        : this(new CompatDocumentBridge(), new SkiaTextMeasurer(), new SkiaDocumentRenderer())
    {
    }

    public RichEditTextDocument(
        ICompatDocumentBridge documentBridge,
        SkiaTextMeasurer textMeasurer,
        SkiaDocumentRenderer renderer)
    {
        _documentBridge = documentBridge ?? throw new ArgumentNullException(nameof(documentBridge));
        _textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _services = new EditorServices();
        _clipboardService = new InMemoryEditorClipboardService();

        EnsureCompatDocumentInitialized();
        _editor = CreateEditorFromCompatDocument(_document);
        _dispatcher = CreateDispatcher();
        _inputRouter = CreateInputRouter(_editor, _dispatcher);
        (_commandRouter, _clipboardController) = CreateCommandPipeline(_editor, _dispatcher, _services, _clipboardService);
        _selection = new RichEditTextRange(this, 0, 0);

        _editor.Changed += OnEditorChanged;
        _editor.UpdateLayout(_viewportWidth, _viewportHeight);
        SyncSelectionFromEditor();
    }

    public event EventHandler? Changed;

    public CompatDocument Document => _document;

    public ITextRange Selection => _selection;

    public int SelectionStartOffset => _selection.StartOffset;

    public int SelectionEndOffset => _selection.EndOffset;

    public bool AcceptsTab { get; set; } = true;

    public bool AcceptsReturn { get; set; } = true;

    public bool IsReadOnly { get; set; }

    public bool IsApplyingInternalDocumentSync { get; private set; }

    public ProEdit.Documents.Document EditorDocument => _editor.Document;

    public DocumentLayout EditorLayout => _editor.Layout;

    public IReadOnlyList<EngineTextRange> EditorSelectionRanges => _editor.SelectionRanges;

    public EngineTextPosition EditorCaret => _editor.Caret;

    public IReadOnlyList<int> DirtyPages => _editor.DirtyPages;

    public long DirtyVersion => _editor.DirtyVersion;

    public IReadOnlyDictionary<string, EmbeddedFlowUiElement> EmbeddedUiElementsById =>
        _documentBridge is ICompatEmbeddedUiBridge embeddedUiBridge
            ? embeddedUiBridge.EmbeddedUiElementsById
            : EmptyEmbeddedUiElements;

    public string EmbeddedUiShapePrefix =>
        _documentBridge is ICompatEmbeddedUiBridge embeddedUiBridge
            ? embeddedUiBridge.EmbeddedUiShapePrefix
            : FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;

    public bool ConfigureEmbeddedUiElements(
        bool enabled,
        Func<object?, bool>? elementPredicate = null,
        Func<object, bool, (double Width, double Height)?>? sizeResolver = null,
        string? shapePrefix = null)
    {
        if (_documentBridge is not ICompatEmbeddedUiBridge embeddedUiBridge)
        {
            return false;
        }

        var prefix = string.IsNullOrWhiteSpace(shapePrefix)
            ? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
            : shapePrefix;
        if (!embeddedUiBridge.ConfigureEmbeddedUiElements(enabled, prefix, elementPredicate, sizeResolver))
        {
            return false;
        }

        RecreateEditorFromCurrentDocument();
        RaiseChanged();
        return true;
    }

    public string GetText()
    {
        var paragraphs = DocumentEditHelpers.BuildParagraphList(_editor.Document);
        if (paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(DocumentEditHelpers.GetParagraphText(paragraphs[i]));
        }

        return builder.ToString();
    }

    public void SetText(string text)
    {
        PushUndoSnapshot();

        var safeText = text ?? string.Empty;
        var next = BuildCompatDocumentFromPlainText(safeText);
        LoadCompatDocument(next, resetSelectionToEnd: true);
        _redo.Clear();
        RaiseChanged();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Push(CloneCompatDocument(_document));
        TrimStack(_redo);

        var snapshot = _undo.Pop();
        LoadCompatDocument(snapshot, resetSelectionToEnd: true);
        RaiseChanged();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Push(CloneCompatDocument(_document));
        TrimStack(_undo);

        var snapshot = _redo.Pop();
        LoadCompatDocument(snapshot, resetSelectionToEnd: true);
        RaiseChanged();
        return true;
    }

    public ITextRange GetRange(int startOffset, int endOffset)
    {
        NormalizeOffsets(startOffset, endOffset, out var start, out var end);
        return new RichEditTextRange(this, start, end);
    }

    public void SetDocument(CompatDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        PushUndoSnapshot();
        LoadCompatDocument(document, resetSelectionToEnd: true);
        _redo.Clear();
        RaiseChanged();
    }

    public void LoadDocumentSnapshot(CompatDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _undo.Clear();
        _redo.Clear();
        LoadCompatDocument(document, resetSelectionToEnd: false);
        RaiseChanged();
    }

    public CompatDocument GetRangeDocument(int startOffset, int endOffset)
    {
        NormalizeOffsets(startOffset, endOffset, out var start, out var end);
        if (start >= end)
        {
            return CreateEmptyCompatDocument();
        }

        var snapshot = ClipboardDocumentConverter.ToDocument(ClipboardDocumentConverter.FromDocument(_editor.Document));
        var extractor = new EditorController(_textMeasurer, snapshot);
        extractor.SetSelection(ToEditorRange(start, end), SelectionUpdateMode.Replace);

        var clipboardController = new EditorClipboardController(extractor, _clipboardService);
        if (!clipboardController.TryBuildSelectionContent(out var content)
            || content.Kind != ClipboardContentKind.Blocks
            || content.Fragment is null
            || content.Fragment.Blocks.Count == 0)
        {
            return CreateEmptyCompatDocument();
        }

        var extractedDocument = ClipboardDocumentConverter.ToDocument(content);
        var compat = _documentBridge.FromEditorDocument(extractedDocument);
        EnsureCompatDocumentInitialized(compat);
        return compat;
    }

    public bool ReplaceRange(int startOffset, int endOffset, CompatDocument fragmentDocument)
    {
        ArgumentNullException.ThrowIfNull(fragmentDocument);
        if (IsReadOnly)
        {
            return false;
        }

        NormalizeOffsets(startOffset, endOffset, out var start, out var end);
        PushUndoSnapshot();

        var hadSelection = start != end;
        var changed = false;

        _suppressEditorChanged = true;
        try
        {
            _editor.SetSelection(ToEditorRange(start, end), SelectionUpdateMode.Replace);

            var fragment = ClipboardDocumentConverter.FromDocument(_documentBridge.ToEditorDocument(fragmentDocument));
            if (fragment.Kind == ClipboardContentKind.Blocks
                && fragment.Fragment is { Blocks.Count: > 0 })
            {
                changed = _clipboardController.PasteBlocks(fragment.Fragment, ClipboardPasteMode.KeepSource);
            }
            else if (hadSelection)
            {
                _editor.Backspace();
                changed = true;
            }
        }
        finally
        {
            _suppressEditorChanged = false;
        }

        if (!changed)
        {
            if (_undo.Count > 0)
            {
                _undo.Pop();
            }

            return false;
        }

        _redo.Clear();
        SyncCompatFromEditor();
        SyncSelectionFromEditor();
        RaiseChanged();
        return true;
    }

    public bool ReplaceSelection(CompatDocument fragmentDocument)
    {
        return ReplaceRange(SelectionStartOffset, SelectionEndOffset, fragmentDocument);
    }

    public bool InsertTable(int rows, int columns)
    {
        return ExecuteMutatingCommand(
            EditorInsertCommandIds.Tables.InsertTable,
            new EditorTableInsertRequest(rows, columns));
    }

    public bool ToggleBold()
    {
        return ExecuteMutatingCommand(EditorHomeCommandIds.Font.BoldToggle, payload: null);
    }

    public bool ToggleItalic()
    {
        return ExecuteMutatingCommand(EditorHomeCommandIds.Font.ItalicToggle, payload: null);
    }

    public bool ToggleUnderline()
    {
        return ExecuteMutatingCommand(EditorHomeCommandIds.Font.UnderlineToggle, payload: null);
    }

    public bool ToggleBulletedList()
    {
        return ExecuteMutatingCommand(EditorHomeCommandIds.Paragraph.ListBullets, payload: null);
    }

    public bool ToggleNumberedList()
    {
        return ExecuteMutatingCommand(EditorHomeCommandIds.Paragraph.ListNumbering, payload: null);
    }

    public void SetSelection(int startOffset, int endOffset)
    {
        NormalizeOffsets(startOffset, endOffset, out var start, out var end);
        var range = ToEditorRange(start, end);

        _suppressEditorChanged = true;
        try
        {
            _editor.SetSelection(range, SelectionUpdateMode.Replace);
        }
        finally
        {
            _suppressEditorChanged = false;
        }

        SyncSelectionFromEditor();
        RaiseChanged();
    }

    public CompatTextPointer GetTextPointer(int absoluteOffset, LogicalDirection direction = LogicalDirection.Forward)
    {
        var position = ToEditorPosition(absoluteOffset);
        return new CompatTextPointer(position.ParagraphIndex, position.Offset, direction);
    }

    public int GetOffsetFromTextPointer(CompatTextPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        return ToAbsoluteOffset(new EngineTextPosition(pointer.ParagraphIndex, pointer.Offset));
    }

    public void UpdateViewport(float viewportWidth, float viewportHeight)
    {
        _viewportWidth = Math.Max(1f, viewportWidth);
        _viewportHeight = Math.Max(1f, viewportHeight);
        _editor.UpdateLayout(_viewportWidth, _viewportHeight);
    }

    public void Render(SKCanvas canvas, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(options);

        _renderer.Render(canvas, _editor.Document, _editor.Layout, options);
    }

    public bool HandleTextInput(string text, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (IsReadOnly)
        {
            return false;
        }

        PushUndoSnapshot();
        var handled = _inputRouter.HandleTextInput(text.AsSpan(), modifiers);
        if (!handled)
        {
            _undo.Pop();
            return false;
        }

        _redo.Clear();
        return true;
    }

    public bool HandleKey(EditorKey key, EditorModifiers modifiers = EditorModifiers.None)
    {
        var mutates = IsMutatingKey(key, modifiers);
        if (mutates)
        {
            if (IsReadOnly)
            {
                return false;
            }

            PushUndoSnapshot();
        }

        var handled = _inputRouter.HandleKey(key, EditorKeyEventKind.Down, modifiers);
        if (!handled)
        {
            if (mutates && _undo.Count > 0)
            {
                _undo.Pop();
            }

            return false;
        }

        if (mutates)
        {
            _redo.Clear();
        }

        return true;
    }

    public bool HandlePointer(
        EditorPointerKind kind,
        float x,
        float y,
        EditorPointerButton button,
        EditorModifiers modifiers = EditorModifiers.None,
        int clickCount = 1)
    {
        var pointerEvent = new EditorPointerEvent(kind, x, y, button, modifiers, Math.Max(1, clickCount));
        return _inputRouter.HandlePointer(pointerEvent);
    }

    public CompatTextPointer? GetPositionFromPoint(float x, float y, bool snapToText)
    {
        if (!snapToText && !IsPointWithinAnyLayoutLine(x, y))
        {
            return null;
        }

        var previousSelection = _editor.Selection;
        _suppressEditorChanged = true;
        try
        {
            _editor.SetCaretFromPoint(x, y, false);
            var caret = _editor.Caret;
            return new CompatTextPointer(caret.ParagraphIndex, caret.Offset);
        }
        finally
        {
            _editor.SetSelection(previousSelection, SelectionUpdateMode.Replace);
            _suppressEditorChanged = false;
            SyncSelectionFromEditor();
        }
    }

    public bool TryGetCaretPoint(out float x, out float y, out int lineIndex)
    {
        if (_editor.TryGetCaretPoint(out var point, out lineIndex))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0f;
        y = 0f;
        lineIndex = -1;
        return false;
    }

    public CompatDocument CreateEditorSnapshotDocument()
    {
        var snapshot = _documentBridge.FromEditorDocument(_editor.Document);
        EnsureCompatDocumentInitialized(snapshot);
        return snapshot;
    }

    internal void ReplaceRange(int startOffset, int endOffset, string replacement)
    {
        NormalizeOffsets(startOffset, endOffset, out var start, out var end);
        PushUndoSnapshot();

        _suppressEditorChanged = true;
        try
        {
            var range = ToEditorRange(start, end);
            _editor.SetSelection(range, SelectionUpdateMode.Replace);
            InsertTextWithParagraphBreaks(replacement ?? string.Empty);
        }
        finally
        {
            _suppressEditorChanged = false;
        }

        SyncCompatFromEditor();
        SyncSelectionFromEditor();
        _redo.Clear();
        RaiseChanged();
    }

    internal string GetRangeText(int startOffset, int endOffset)
    {
        var text = GetText();
        var start = Math.Clamp(startOffset, 0, text.Length);
        var end = Math.Clamp(endOffset, 0, text.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return text.Substring(start, end - start);
    }

    private void InsertTextWithParagraphBreaks(string text)
    {
        if (text.Length == 0)
        {
            if (!_editor.Selection.IsEmpty)
            {
                _editor.Backspace();
            }

            return;
        }

        var span = text.AsSpan();
        var segmentStart = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var value = span[i];
            if (value != '\r' && value != '\n')
            {
                continue;
            }

            var segment = span.Slice(segmentStart, i - segmentStart);
            if (!segment.IsEmpty)
            {
                _editor.InsertText(segment.ToString());
            }
            else if (!_editor.Selection.IsEmpty)
            {
                _editor.Backspace();
            }

            _editor.InsertParagraphBreak();

            if (value == '\r' && i + 1 < span.Length && span[i + 1] == '\n')
            {
                i++;
            }

            segmentStart = i + 1;
        }

        if (segmentStart < span.Length)
        {
            _editor.InsertText(span.Slice(segmentStart).ToString());
        }
    }

    private bool ExecuteMutatingCommand(string commandId, object? payload)
    {
        if (IsReadOnly)
        {
            return false;
        }

        PushUndoSnapshot();

        bool handled;
        _suppressEditorChanged = true;
        try
        {
            handled = _commandRouter.ExecuteAsync(commandId, payload, context: null, recordHistory: true)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            _suppressEditorChanged = false;
        }

        if (!handled)
        {
            if (_undo.Count > 0)
            {
                _undo.Pop();
            }

            return false;
        }

        _redo.Clear();
        SyncCompatFromEditor();
        SyncSelectionFromEditor();
        RaiseChanged();
        return true;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorChanged)
        {
            return;
        }

        SyncCompatFromEditor();
        SyncSelectionFromEditor();
        RaiseChanged();
    }

    private void SyncCompatFromEditor()
    {
        var previous = IsApplyingInternalDocumentSync;
        IsApplyingInternalDocumentSync = true;
        try
        {
            _documentBridge.SyncFromEditor(_editor.Document, _document);
            EnsureCompatDocumentInitialized();
        }
        finally
        {
            IsApplyingInternalDocumentSync = previous;
        }
    }

    private void SyncSelectionFromEditor()
    {
        var selection = _editor.Selection.Normalize();
        var start = ToAbsoluteOffset(selection.Start);
        var end = ToAbsoluteOffset(selection.End);
        _selection = new RichEditTextRange(this, start, end);
    }

    private void LoadCompatDocument(CompatDocument source, bool resetSelectionToEnd)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clone = CloneCompatDocument(source);
        var previous = IsApplyingInternalDocumentSync;
        IsApplyingInternalDocumentSync = true;
        try
        {
            _document.Blocks.Clear();
            for (var i = 0; i < clone.Blocks.Count; i++)
            {
                _document.Blocks.Add(clone.Blocks[i]);
            }
        }
        finally
        {
            IsApplyingInternalDocumentSync = previous;
        }

        EnsureCompatDocumentInitialized();
        RecreateEditorFromCurrentDocument();

        if (resetSelectionToEnd)
        {
            var end = GetText().Length;
            SetSelection(end, end);
        }
        else
        {
            SyncSelectionFromEditor();
        }
    }

    private void RecreateEditorFromCurrentDocument()
    {
        _editor.Changed -= OnEditorChanged;
        _editor = CreateEditorFromCompatDocument(_document);
        _dispatcher = CreateDispatcher();
        _inputRouter = CreateInputRouter(_editor, _dispatcher);
        (_commandRouter, _clipboardController) = CreateCommandPipeline(_editor, _dispatcher, _services, _clipboardService);
        _editor.Changed += OnEditorChanged;
        _editor.UpdateLayout(_viewportWidth, _viewportHeight);
        SyncSelectionFromEditor();
    }

    private EditorController CreateEditorFromCompatDocument(CompatDocument source)
    {
        var engineDocument = _documentBridge.ToEditorDocument(source);
        return new EditorController(_textMeasurer, engineDocument);
    }

    private static EditorCommandDispatcher CreateDispatcher()
    {
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        var context = new EditorModuleContext(services, dispatcher);
        new BasicEditingModule().Register(context);
        return dispatcher;
    }

    private EditorCommandInputRouter CreateInputRouter(EditorController editor, EditorCommandDispatcher dispatcher)
    {
        return new EditorCommandInputRouter(
            dispatcher,
            editor,
            undoRedo: null,
            clipboard: null,
            tableSelectionProvider: null,
            contentControls: null,
            autoCorrect: null,
            acceptsTabProvider: () => AcceptsTab,
            acceptsReturnProvider: () => AcceptsReturn,
            isReadOnlyProvider: () => IsReadOnly);
    }

    private static (EditorCommandRouterAdapter Router, EditorClipboardController ClipboardController) CreateCommandPipeline(
        EditorController editor,
        EditorCommandDispatcher dispatcher,
        EditorServices services,
        IClipboardService clipboardService)
    {
        var router = new EditorCommandRouterAdapter(dispatcher, editor);
        new EditorHomeCommandMap(router, editor, services).Register();
        new EditorInsertCommandMap(router, editor, services).Register();
        var clipboardController = new EditorClipboardController(editor, clipboardService);
        return (router, clipboardController);
    }

    private EngineTextRange ToEditorRange(int startOffset, int endOffset)
    {
        var start = ToEditorPosition(startOffset);
        var end = ToEditorPosition(endOffset);
        return new EngineTextRange(start, end);
    }

    private EngineTextPosition ToEditorPosition(int absoluteOffset)
    {
        var paragraphs = DocumentEditHelpers.BuildParagraphList(_editor.Document);
        if (paragraphs.Count == 0)
        {
            return new EngineTextPosition(0, 0);
        }

        var clamped = Math.Clamp(absoluteOffset, 0, GetAbsoluteLength(paragraphs));
        var running = 0;

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var length = DocumentEditHelpers.GetParagraphLength(paragraphs[i]);
            if (clamped <= running + length)
            {
                return new EngineTextPosition(i, clamped - running);
            }

            running += length;
            if (i + 1 < paragraphs.Count)
            {
                if (clamped == running)
                {
                    return new EngineTextPosition(i, length);
                }

                running += 1;
            }
        }

        var lastIndex = paragraphs.Count - 1;
        return new EngineTextPosition(lastIndex, DocumentEditHelpers.GetParagraphLength(paragraphs[lastIndex]));
    }

    private int ToAbsoluteOffset(EngineTextPosition position)
    {
        var paragraphs = DocumentEditHelpers.BuildParagraphList(_editor.Document);
        if (paragraphs.Count == 0)
        {
            return 0;
        }

        if (position.ParagraphIndex <= 0)
        {
            var firstLength = DocumentEditHelpers.GetParagraphLength(paragraphs[0]);
            return Math.Clamp(position.Offset, 0, firstLength);
        }

        if (position.ParagraphIndex >= paragraphs.Count)
        {
            return GetAbsoluteLength(paragraphs);
        }

        var paragraphIndex = position.ParagraphIndex;
        var offset = 0;

        for (var i = 0; i < paragraphIndex; i++)
        {
            offset += DocumentEditHelpers.GetParagraphLength(paragraphs[i]);
            offset += 1;
        }

        var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraphs[paragraphIndex]);
        offset += Math.Clamp(position.Offset, 0, paragraphLength);
        return offset;
    }

    private bool IsPointWithinAnyLayoutLine(float x, float y)
    {
        var lines = _editor.Layout.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineLeft = line.X;
            var lineRight = line.X + line.Width;
            var lineTop = line.Y;
            var lineBottom = line.Y + line.LineHeight;
            if (x >= lineLeft && x <= lineRight && y >= lineTop && y <= lineBottom)
            {
                return true;
            }
        }

        return false;
    }

    private void PushUndoSnapshot()
    {
        _undo.Push(CloneCompatDocument(_document));
        TrimStack(_undo);
    }

    private CompatDocument CloneCompatDocument(CompatDocument source)
    {
        return _documentBridge.FromEditorDocument(_documentBridge.ToEditorDocument(source));
    }

    private static void TrimStack(Stack<CompatDocument> stack)
    {
        if (stack.Count <= MaxUndoDepth)
        {
            return;
        }

        var items = stack.ToArray();
        var count = Math.Min(MaxUndoDepth, items.Length);
        stack.Clear();
        for (var i = count - 1; i >= 0; i--)
        {
            stack.Push(items[i]);
        }
    }

    private static int GetAbsoluteLength(IReadOnlyList<ParagraphBlock> paragraphs)
    {
        var length = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            length += DocumentEditHelpers.GetParagraphLength(paragraphs[i]);
            if (i + 1 < paragraphs.Count)
            {
                length += 1;
            }
        }

        return length;
    }

    private static bool IsMutatingKey(EditorKey key, EditorModifiers modifiers)
    {
        var hasCommandModifier = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (hasCommandModifier)
        {
            return false;
        }

        return key is EditorKey.Backspace or EditorKey.Delete or EditorKey.Enter or EditorKey.Tab;
    }

    private static CompatDocument BuildCompatDocumentFromPlainText(string text)
    {
        var document = new CompatDocument();
        var normalized = NormalizeLineEndings(text);
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            document.Blocks.Add(new Paragraph(lines[i]));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }

        return document;
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static CompatDocument CreateEmptyCompatDocument()
    {
        var document = new CompatDocument();
        document.Blocks.Add(new Paragraph());
        return document;
    }

    private static void EnsureCompatDocumentInitialized(CompatDocument document)
    {
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }
    }

    private void EnsureCompatDocumentInitialized()
    {
        EnsureCompatDocumentInitialized(_document);
    }

    private void NormalizeOffsets(int startOffset, int endOffset, out int start, out int end)
    {
        var length = GetText().Length;
        start = Math.Clamp(startOffset, 0, length);
        end = Math.Clamp(endOffset, 0, length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class InMemoryEditorClipboardService : IClipboardService
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
            return !string.IsNullOrEmpty(_text);
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
            _content = content;
        }
    }

    private sealed class RichEditTextRange : ITextRange
    {
        private readonly RichEditTextDocument _owner;

        public RichEditTextRange(RichEditTextDocument owner, int startOffset, int endOffset)
        {
            _owner = owner;
            StartOffset = startOffset;
            EndOffset = endOffset;
        }

        public int StartOffset { get; }

        public int EndOffset { get; }

        public string GetText()
        {
            return _owner.GetRangeText(StartOffset, EndOffset);
        }

        public void SetText(string text)
        {
            _owner.ReplaceRange(StartOffset, EndOffset, text);
        }

        public CompatDocument GetDocument()
        {
            return _owner.GetRangeDocument(StartOffset, EndOffset);
        }

        public bool SetDocument(CompatDocument document)
        {
            return _owner.ReplaceRange(StartOffset, EndOffset, document);
        }
    }
}
