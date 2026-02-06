using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System.Text;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Word.Avalonia;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.RichText.Avalonia;

public sealed class RichTextBox : TemplatedControl, IRichTextBoxCollaborationAdapter
{
    private const string PartDocumentHost = "PART_DocumentHost";
    private const string PartEmbeddedHost = "PART_EmbeddedHost";
    private const string CollaborationExternalMutationMessage =
        "External FlowDocument mutations are not supported while collaboration is active. " +
        "Use RichTextBox editing APIs/commands or replace the entire Document to reload content.";

    private static readonly AttachedProperty<WeakReference<RichTextBox>?> DocumentOwnerProperty =
        AvaloniaProperty.RegisterAttached<RichTextBox, FlowDocumentModel, WeakReference<RichTextBox>?>("DocumentOwner");

    public static readonly DirectProperty<RichTextBox, FlowDocumentModel> DocumentProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, FlowDocumentModel>(
            nameof(Document),
            box => box.Document,
            (box, value) => box.SetDocument(value));

    public static readonly DirectProperty<RichTextBox, bool> AcceptsTabProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(AcceptsTab),
            box => box.AcceptsTab,
            (box, value) => box.SetAcceptsTab(value),
            true);

    public static readonly DirectProperty<RichTextBox, bool> AcceptsReturnProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(AcceptsReturn),
            box => box.AcceptsReturn,
            (box, value) => box.SetAcceptsReturn(value),
            true);

    public static readonly DirectProperty<RichTextBox, bool> IsReadOnlyProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsReadOnly),
            box => box.IsReadOnly,
            (box, value) => box.SetReadOnly(value),
            false);

    public static readonly DirectProperty<RichTextBox, bool> IsReadOnlyCaretVisibleProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsReadOnlyCaretVisible),
            box => box.IsReadOnlyCaretVisible,
            (box, value) => box.SetReadOnlyCaretVisible(value),
            false);

    public static readonly DirectProperty<RichTextBox, bool> IsDocumentEnabledProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsDocumentEnabled),
            box => box.IsDocumentEnabled,
            (box, value) => box.SetDocumentEnabled(value),
            false);

    public static readonly DirectProperty<RichTextBox, bool> IsProofingEnabledProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsProofingEnabled),
            box => box.IsProofingEnabled,
            (box, value) => box.SetProofingEnabled(value),
            false);

    public static readonly DirectProperty<RichTextBox, bool> IsSpellingEnabledProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsSpellingEnabled),
            box => box.IsSpellingEnabled,
            (box, value) => box.SetSpellingEnabled(value),
            true);

    public static readonly DirectProperty<RichTextBox, bool> IsGrammarEnabledProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsGrammarEnabled),
            box => box.IsGrammarEnabled,
            (box, value) => box.SetGrammarEnabled(value),
            false);

    public static readonly DirectProperty<RichTextBox, bool> IsStyleEnabledProperty =
        AvaloniaProperty.RegisterDirect<RichTextBox, bool>(
            nameof(IsStyleEnabled),
            box => box.IsStyleEnabled,
            (box, value) => box.SetStyleEnabled(value),
            false);

    private readonly DocumentView _documentView;
    private readonly FlowDocumentEditBridge _bridge;
    private readonly FlowTextSelection _selection;
    private ContentControl? _documentHost;
    private Canvas? _embeddedHost;
    private FlowDocumentModel _document;
    private bool _acceptsReturn = true;
    private bool _acceptsTab = true;
    private bool _isReadOnly;
    private bool _isReadOnlyCaretVisible;
    private bool _isDocumentEnabled;
    private bool _isProofingEnabled;
    private bool _isSpellingEnabled = true;
    private bool _isGrammarEnabled;
    private bool _isStyleEnabled;
    private TextRange _lastSelection;
    private DocumentSignature _lastDocumentSignature;
    private bool _hasSelectionSnapshot;
    private bool _hasDocumentSignature;
    private bool _suppressEditorStateChanged;
    private bool _suppressSelectionChanged;
    private int _changeDepth;
    private IDisposable? _batchEditScope;
    private bool _hasPendingTextChanged;
    private readonly Dictionary<string, Control> _embeddedControlsById = new(StringComparer.Ordinal);
    private DocumentLayout? _embeddedLayout;
    private Vector _embeddedScrollOffset;
    private float _embeddedZoomFactor = 1f;
    private bool _hasEmbeddedViewportSnapshot;
    private bool _suppressProofingSync;
    private bool _isImplicitDocument = true;
    private bool _isCollaborationAttached;
    private event EventHandler? EditorSessionRebuiltInternal;

    public event EventHandler? TextChanged;
    public event EventHandler? SelectionChanged;

    public RichTextBox()
    {
        _bridge = new FlowDocumentEditBridge();
        _documentView = new DocumentView();
        _documentView.AcceptsReturn = _acceptsReturn;
        _documentView.AcceptsTab = _acceptsTab;
        _documentView.IsReadOnly = _isReadOnly;
        _documentView.IsReadOnlyCaretVisible = _isReadOnlyCaretVisible;
        _documentView.EditorStateChanged += OnEditorStateChanged;
        _documentView.ZoomChanged += OnDocumentViewportChanged;
        _documentView.ScrollInvalidated += OnDocumentViewportChanged;
        _documentView.LayoutUpdated += OnDocumentLayoutUpdated;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        _document = new FlowDocumentModel();
        _selection = FlowTextSelection.CreateForSelection(this);
        AttachDocument(_document);
        LoadDocumentIntoEditor(_document, raiseTextChanged: false);
        _lastSelection = _documentView.Selection.Normalize();
        _lastDocumentSignature = CaptureDocumentSignature(_documentView.Document);
        _hasSelectionSnapshot = true;
        _hasDocumentSignature = true;
        ApplyProofingStateToService();
        SyncProofingStateFromService();
    }

    public FlowDocumentModel Document
    {
        get => _document;
        set => SetDocument(value);
    }

    public bool AcceptsTab
    {
        get => _acceptsTab;
        set => SetAcceptsTab(value);
    }

    public bool AcceptsReturn
    {
        get => _acceptsReturn;
        set => SetAcceptsReturn(value);
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetReadOnly(value);
    }

    public bool IsReadOnlyCaretVisible
    {
        get => _isReadOnlyCaretVisible;
        set => SetReadOnlyCaretVisible(value);
    }

    public bool IsDocumentEnabled
    {
        get => _isDocumentEnabled;
        set => SetDocumentEnabled(value);
    }

    public bool IsProofingEnabled
    {
        get => _isProofingEnabled;
        set => SetProofingEnabled(value);
    }

    public bool IsSpellingEnabled
    {
        get => _isSpellingEnabled;
        set => SetSpellingEnabled(value);
    }

    public bool IsGrammarEnabled
    {
        get => _isGrammarEnabled;
        set => SetGrammarEnabled(value);
    }

    public bool IsStyleEnabled
    {
        get => _isStyleEnabled;
        set => SetStyleEnabled(value);
    }

    public FlowTextSelection Selection => _selection;

    public FlowTextPointer CaretPosition
    {
        get => CreatePointer(GetNormalizedSelection().End);
        set => SetCaretPosition(value);
    }

    public void BeginChange()
    {
        if (_changeDepth == 0 && _documentView.TryGetService<IEditorBatchEdit>(out var batchEdit))
        {
            _batchEditScope = batchEdit.BeginBatchEdit();
        }

        _changeDepth++;
    }

    public void EndChange()
    {
        if (_changeDepth <= 0)
        {
            throw new InvalidOperationException("EndChange called without a matching BeginChange.");
        }

        _changeDepth--;
        if (_changeDepth > 0)
        {
            return;
        }

        _batchEditScope?.Dispose();
        _batchEditScope = null;

        if (_hasPendingTextChanged)
        {
            _hasPendingTextChanged = false;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool Copy()
    {
        return TryExecuteEditorCommand(EditorHomeCommandIds.Clipboard.Copy, payload: null, recordHistory: false);
    }

    public bool Cut()
    {
        if (_isReadOnly)
        {
            return false;
        }

        return TryExecuteEditorCommand(EditorHomeCommandIds.Clipboard.Cut, payload: null, recordHistory: true);
    }

    public bool Paste()
    {
        if (_isReadOnly)
        {
            return false;
        }

        return TryExecuteEditorCommand(EditorHomeCommandIds.Clipboard.Paste, payload: null, recordHistory: true);
    }

    public bool Undo()
    {
        if (_isReadOnly || !_documentView.TryGetService<IUndoRedoService>(out var undoRedo) || !undoRedo.CanUndo)
        {
            return false;
        }

        undoRedo.UndoAsync().GetAwaiter().GetResult();
        return true;
    }

    public bool Redo()
    {
        if (_isReadOnly || !_documentView.TryGetService<IUndoRedoService>(out var undoRedo) || !undoRedo.CanRedo)
        {
            return false;
        }

        undoRedo.RedoAsync().GetAwaiter().GetResult();
        return true;
    }

    public bool SelectAll()
    {
        return TryExecuteEditorCommand(EditorHomeCommandIds.Editing.SelectAll, payload: null, recordHistory: false);
    }

    public FlowTextPointer? GetPositionFromPoint(Point point, bool snapToText)
    {
        if (!snapToText && !_documentView.IsTextHitAtViewPoint(point))
        {
            return null;
        }

        var selection = _documentView.Selection.Normalize();
        var previousSuppress = _suppressSelectionChanged;
        _suppressSelectionChanged = true;
        try
        {
            _documentView.SetCaretFromViewPoint(point);
            return CreatePointer(_documentView.Caret);
        }
        finally
        {
            _documentView.SelectRange(selection, ensureVisible: false);
            _suppressSelectionChanged = previousSuppress;
        }
    }

    public ProofingDiagnostic? GetSpellingError(FlowTextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var textPosition = GetValidatedPosition(position);
        if (!_documentView.TryGetService<IProofingService>(out var proofing)
            || !proofing.TryGetDiagnosticAt(textPosition, out var diagnostic)
            || diagnostic.Kind != ProofingIssueKind.Spelling)
        {
            return null;
        }

        return diagnostic;
    }

    public FlowTextRange? GetSpellingErrorRange(FlowTextPointer position)
    {
        var diagnostic = GetSpellingError(position);
        if (!diagnostic.HasValue)
        {
            return null;
        }

        var start = CreatePointer(new TextPosition(diagnostic.Value.ParagraphIndex, diagnostic.Value.StartOffset));
        var end = CreatePointer(new TextPosition(diagnostic.Value.ParagraphIndex, diagnostic.Value.StartOffset + diagnostic.Value.Length));
        return new FlowTextRange(start, end);
    }

    public FlowTextPointer? GetNextSpellingErrorPosition(FlowTextPointer position, FlowLogicalDirection direction)
    {
        ArgumentNullException.ThrowIfNull(position);
        var start = GetValidatedPosition(position);
        if (!_documentView.TryGetService<IProofingService>(out var proofing))
        {
            return null;
        }

        var paragraphCount = _documentView.Document.ParagraphCount;
        if (paragraphCount <= 0)
        {
            return null;
        }

        if (direction == FlowLogicalDirection.Forward)
        {
            for (var paragraphIndex = start.ParagraphIndex; paragraphIndex < paragraphCount; paragraphIndex++)
            {
                var diagnostics = proofing.GetParagraphDiagnostics(paragraphIndex);
                for (var i = 0; i < diagnostics.Count; i++)
                {
                    var diagnostic = diagnostics[i];
                    if (diagnostic.Kind != ProofingIssueKind.Spelling)
                    {
                        continue;
                    }

                    if (paragraphIndex == start.ParagraphIndex && diagnostic.StartOffset <= start.Offset)
                    {
                        continue;
                    }

                    return CreatePointer(new TextPosition(paragraphIndex, diagnostic.StartOffset));
                }
            }

            return null;
        }

        for (var paragraphIndex = start.ParagraphIndex; paragraphIndex >= 0; paragraphIndex--)
        {
            var diagnostics = proofing.GetParagraphDiagnostics(paragraphIndex);
            for (var i = diagnostics.Count - 1; i >= 0; i--)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic.Kind != ProofingIssueKind.Spelling)
                {
                    continue;
                }

                if (paragraphIndex == start.ParagraphIndex && diagnostic.StartOffset >= start.Offset)
                {
                    continue;
                }

                return CreatePointer(new TextPosition(paragraphIndex, diagnostic.StartOffset));
            }
        }

        return null;
    }

    public bool ShouldSerializeDocument()
    {
        if (!_isImplicitDocument)
        {
            return true;
        }

        return !IsFlowDocumentStructurallyEmpty(_document);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _documentHost = e.NameScope.Find<ContentControl>(PartDocumentHost);
        if (_documentHost is not null)
        {
            _documentHost.Content = _documentView;
        }

        _embeddedHost = e.NameScope.Find<Canvas>(PartEmbeddedHost);
        SyncEmbeddedUiLayout(force: true);
        UpdateEmbeddedUiInteractivity();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (!_documentView.IsFocused)
        {
            _documentView.Focus();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || !IsEnabled || _isReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (!_documentView.IsFocused)
        {
            _documentView.Focus();
        }

        if (_documentView.HandleHostedTextInput(e.Text))
        {
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new RichTextBoxAutomationPeer(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!IsEnabled || _isReadOnly || !CanAcceptDrop(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!IsEnabled || _isReadOnly || !CanAcceptDrop(e.DataTransfer))
        {
            return;
        }

        _documentView.Focus();
        _documentView.SetCaretFromViewPoint(e.GetPosition(_documentView));
        if (TryPasteDropData(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsEnabledProperty)
        {
            _documentView.IsEnabled = IsEnabled;
            UpdateEmbeddedUiInteractivity();
        }
    }

    private void SetDocument(FlowDocumentModel value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (ReferenceEquals(value, _document))
        {
            return;
        }

        var previous = _document;

        AttachDocument(value);
        SetAndRaise(DocumentProperty, ref _document, value);
        DetachDocument(previous);
        _isImplicitDocument = false;

        LoadDocumentIntoEditor(value, raiseTextChanged: true);
    }

    private void AttachDocument(FlowDocumentModel document)
    {
        var currentOwnerReference = document.GetValue(DocumentOwnerProperty);
        if (currentOwnerReference is not null
            && currentOwnerReference.TryGetTarget(out var currentOwner)
            && !ReferenceEquals(currentOwner, this))
        {
            throw new InvalidOperationException("This FlowDocument is already attached to another RichTextBox.");
        }

        document.SetValue(DocumentOwnerProperty, new WeakReference<RichTextBox>(this));
        document.Changed += OnFlowDocumentChanged;
    }

    private void DetachDocument(FlowDocumentModel document)
    {
        document.Changed -= OnFlowDocumentChanged;

        var currentOwnerReference = document.GetValue(DocumentOwnerProperty);
        if (currentOwnerReference is not null
            && currentOwnerReference.TryGetTarget(out var currentOwner)
            && ReferenceEquals(currentOwner, this))
        {
            document.ClearValue(DocumentOwnerProperty);
        }
    }

    private void OnFlowDocumentChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _document) && !_bridge.IsApplyingInternalChange)
        {
            if (_isCollaborationAttached)
            {
                RevertExternalFlowDocumentMutation();
                throw new InvalidOperationException(CollaborationExternalMutationMessage);
            }

            LoadDocumentIntoEditor(_document, raiseTextChanged: true);
        }
    }

    private void RevertExternalFlowDocumentMutation()
    {
        _bridge.SyncFlowDocumentFromEditor(_documentView.Document, _document);
        _lastSelection = _documentView.Selection.Normalize();
        _hasSelectionSnapshot = true;
        _lastDocumentSignature = CaptureDocumentSignature(_documentView.Document);
        _hasDocumentSignature = true;
    }

    private void SetAcceptsTab(bool value)
    {
        if (value == _acceptsTab)
        {
            return;
        }

        SetAndRaise(AcceptsTabProperty, ref _acceptsTab, value);
        _documentView.AcceptsTab = value;
    }

    private void SetAcceptsReturn(bool value)
    {
        if (value == _acceptsReturn)
        {
            return;
        }

        SetAndRaise(AcceptsReturnProperty, ref _acceptsReturn, value);
        _documentView.AcceptsReturn = value;
    }

    private void SetReadOnly(bool value)
    {
        if (value == _isReadOnly)
        {
            return;
        }

        SetAndRaise(IsReadOnlyProperty, ref _isReadOnly, value);
        _documentView.IsReadOnly = value;
    }

    private void SetReadOnlyCaretVisible(bool value)
    {
        if (value == _isReadOnlyCaretVisible)
        {
            return;
        }

        SetAndRaise(IsReadOnlyCaretVisibleProperty, ref _isReadOnlyCaretVisible, value);
        _documentView.IsReadOnlyCaretVisible = value;
    }

    private void SetDocumentEnabled(bool value)
    {
        if (value == _isDocumentEnabled)
        {
            return;
        }

        SetAndRaise(IsDocumentEnabledProperty, ref _isDocumentEnabled, value);
        UpdateEmbeddedUiInteractivity();
    }

    private void SetProofingEnabled(bool value)
    {
        if (value == _isProofingEnabled)
        {
            return;
        }

        SetAndRaise(IsProofingEnabledProperty, ref _isProofingEnabled, value);
        if (!_suppressProofingSync)
        {
            ApplyProofingStateToService();
        }
    }

    private void SetSpellingEnabled(bool value)
    {
        if (value == _isSpellingEnabled)
        {
            return;
        }

        SetAndRaise(IsSpellingEnabledProperty, ref _isSpellingEnabled, value);
        if (!_suppressProofingSync)
        {
            ApplyProofingStateToService();
        }
    }

    private void SetGrammarEnabled(bool value)
    {
        if (value == _isGrammarEnabled)
        {
            return;
        }

        SetAndRaise(IsGrammarEnabledProperty, ref _isGrammarEnabled, value);
        if (!_suppressProofingSync)
        {
            ApplyProofingStateToService();
        }
    }

    private void SetStyleEnabled(bool value)
    {
        if (value == _isStyleEnabled)
        {
            return;
        }

        SetAndRaise(IsStyleEnabledProperty, ref _isStyleEnabled, value);
        if (!_suppressProofingSync)
        {
            ApplyProofingStateToService();
        }
    }

    private void LoadDocumentIntoEditor(FlowDocumentModel source, bool raiseTextChanged)
    {
        var converted = _bridge.ConvertToEditorDocument(source);
        _suppressEditorStateChanged = true;
        try
        {
            _documentView.LoadDocument(converted);
        }
        finally
        {
            _suppressEditorStateChanged = false;
        }

        _lastSelection = _documentView.Selection.Normalize();
        _lastDocumentSignature = CaptureDocumentSignature(_documentView.Document);
        _hasSelectionSnapshot = true;
        _hasDocumentSignature = true;
        _embeddedLayout = null;
        _hasEmbeddedViewportSnapshot = false;
        SyncEmbeddedUiLayout(force: true);
        ApplyProofingStateToService();
        SyncProofingStateFromService();
        EditorSessionRebuiltInternal?.Invoke(this, EventArgs.Empty);

        if (raiseTextChanged)
        {
            RaiseTextChanged();
        }
    }

    private void ApplyProofingStateToService()
    {
        if (!_documentView.TryGetService<IProofingToggleService>(out var toggle))
        {
            return;
        }

        toggle.SetEnabled(_isProofingEnabled);
        if (!_isProofingEnabled)
        {
            return;
        }

        toggle.SetSpellingEnabled(_isSpellingEnabled);
        toggle.SetGrammarEnabled(_isGrammarEnabled);
        toggle.SetStyleEnabled(_isStyleEnabled);
    }

    private void SyncProofingStateFromService()
    {
        if (!_documentView.TryGetService<IProofingToggleService>(out var toggle))
        {
            return;
        }

        _suppressProofingSync = true;
        try
        {
            SetProofingEnabled(toggle.IsEnabled);
            SetSpellingEnabled(toggle.IsSpellingEnabled);
            SetGrammarEnabled(toggle.IsGrammarEnabled);
            SetStyleEnabled(toggle.IsStyleEnabled);
        }
        finally
        {
            _suppressProofingSync = false;
        }
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorStateChanged)
        {
            return;
        }

        var hasChangeInfo = _documentView.TryGetService<IEditorChangeInfo>(out var changeInfo);

        var selection = _documentView.Selection.Normalize();
        if (!_hasSelectionSnapshot || !selection.Equals(_lastSelection))
        {
            _lastSelection = selection;
            _hasSelectionSnapshot = true;
            if (!_suppressSelectionChanged)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (TryHandleEditorContentChange(changeInfo))
        {
            SyncEmbeddedUiLayout(force: true);
            return;
        }

        if (hasChangeInfo && changeInfo!.LastChangeKind == EditorChangeKind.Selection)
        {
            SyncEmbeddedUiLayout(force: false);
            return;
        }

        var signature = CaptureDocumentSignature(_documentView.Document);
        if (!_hasDocumentSignature || !signature.Equals(_lastDocumentSignature))
        {
            _lastDocumentSignature = signature;
            _hasDocumentSignature = true;
            _bridge.SyncFlowDocumentFromEditor(_documentView.Document, _document);
            RaiseTextChanged();
        }

        SyncEmbeddedUiLayout(force: false);
    }

    private bool TryHandleEditorContentChange(IEditorChangeInfo? changeInfo)
    {
        if (changeInfo is null || changeInfo.LastChangeKind != EditorChangeKind.Content)
        {
            return false;
        }

        _lastDocumentSignature = CaptureDocumentSignature(_documentView.Document);
        _hasDocumentSignature = true;
        if (!_bridge.TrySyncFlowDocumentFromEditorIncremental(
                _documentView.Document,
                _document,
                changeInfo.LastDirtyParagraphIndex))
        {
            _bridge.SyncFlowDocumentFromEditor(_documentView.Document, _document);
        }

        RaiseTextChanged();
        return true;
    }

    private void OnDocumentViewportChanged(object? sender, EventArgs e)
    {
        SyncEmbeddedUiLayout(force: false);
    }

    private void OnDocumentLayoutUpdated(object? sender, EventArgs e)
    {
        SyncEmbeddedUiLayout(force: false);
    }

    private bool CanAcceptDrop(IDataTransfer dataTransfer)
    {
        return RichTextClipboardDataTransferParser.ContainsSupportedFormats(dataTransfer);
    }

    private bool TryPasteDropData(IDataTransfer dataTransfer)
    {
        if (!_documentView.TryGetService<IClipboardService>(out var clipboard))
        {
            return false;
        }

        if (!RichTextClipboardDataTransferParser.TryBuildClipboardContent(dataTransfer, out var content)
            || content.Kind == ClipboardContentKind.None)
        {
            return false;
        }

        clipboard.SetContent(content);
        return TryExecuteEditorCommand(EditorHomeCommandIds.Clipboard.Paste, payload: null, recordHistory: true);
    }

    private void SyncEmbeddedUiLayout(bool force)
    {
        if (_embeddedHost is null)
        {
            return;
        }

        var embeddedElements = _bridge.EmbeddedUiElementsById;
        if (embeddedElements.Count == 0)
        {
            ClearEmbeddedUiControls();
            return;
        }

        var layout = _documentView.Layout;
        var zoomFactor = _documentView.ZoomFactor;
        var effectiveOffset = _documentView.EffectiveScrollOffset;
        if (!force
            && _hasEmbeddedViewportSnapshot
            && ReferenceEquals(_embeddedLayout, layout)
            && Math.Abs(_embeddedZoomFactor - zoomFactor) < 0.001f
            && Math.Abs(_embeddedScrollOffset.X - effectiveOffset.X) < 0.5
            && Math.Abs(_embeddedScrollOffset.Y - effectiveOffset.Y) < 0.5)
        {
            return;
        }

        _embeddedLayout = layout;
        _embeddedZoomFactor = zoomFactor;
        _embeddedScrollOffset = effectiveOffset;
        _hasEmbeddedViewportSnapshot = true;

        var placements = CollectEmbeddedUiPlacements(layout, zoomFactor, effectiveOffset, embeddedElements);
        ApplyEmbeddedUiPlacements(placements, embeddedElements);
        UpdateEmbeddedUiInteractivity();
    }

    private static Dictionary<string, Rect> CollectEmbeddedUiPlacements(
        DocumentLayout layout,
        float zoomFactor,
        Vector effectiveOffset,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements)
    {
        var placements = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var lines = layout.Lines;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var shapes = line.Shapes;
            for (var shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
            {
                var shape = shapes[shapeIndex];
                if (!TryGetEmbeddedElementId(shape.Shape.Name, out var id)
                    || placements.ContainsKey(id)
                    || !embeddedElements.TryGetValue(id, out var element)
                    || element.Child is not Control)
                {
                    continue;
                }

                var bounds = ComputeInlineShapeBounds(line, shape);
                placements[id] = ToViewRect(bounds, zoomFactor, effectiveOffset);
            }
        }

        return placements;
    }

    private void ApplyEmbeddedUiPlacements(
        IReadOnlyDictionary<string, Rect> placements,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements)
    {
        if (_embeddedHost is null)
        {
            return;
        }

        if (placements.Count == 0)
        {
            ClearEmbeddedUiControls();
            return;
        }

        var staleIds = new List<string>();
        foreach (var id in _embeddedControlsById.Keys)
        {
            if (!placements.ContainsKey(id) || !embeddedElements.TryGetValue(id, out var element) || element.Child is not Control)
            {
                staleIds.Add(id);
            }
        }

        for (var i = 0; i < staleIds.Count; i++)
        {
            RemoveEmbeddedControl(staleIds[i]);
        }

        foreach (var pair in placements)
        {
            if (!embeddedElements.TryGetValue(pair.Key, out var element) || element.Child is not Control sourceControl)
            {
                continue;
            }

            if (_embeddedControlsById.TryGetValue(pair.Key, out var currentControl)
                && !ReferenceEquals(currentControl, sourceControl))
            {
                RemoveEmbeddedControl(pair.Key);
            }

            if (!_embeddedControlsById.TryGetValue(pair.Key, out var control))
            {
                control = sourceControl;
                _embeddedControlsById[pair.Key] = control;
            }

            AttachEmbeddedControl(control);
            var bounds = pair.Value;
            Canvas.SetLeft(control, bounds.X);
            Canvas.SetTop(control, bounds.Y);
            control.Width = Math.Max(1d, bounds.Width);
            control.Height = Math.Max(1d, bounds.Height);
        }
    }

    private void AttachEmbeddedControl(Control control)
    {
        if (_embeddedHost is null)
        {
            return;
        }

        if (ReferenceEquals(control.Parent, _embeddedHost))
        {
            return;
        }

        DetachControlFromParent(control);
        _embeddedHost.Children.Add(control);
    }

    private void RemoveEmbeddedControl(string id)
    {
        if (_embeddedControlsById.Remove(id, out var control))
        {
            DetachControlFromParent(control);
        }
    }

    private void ClearEmbeddedUiControls()
    {
        var ids = new List<string>(_embeddedControlsById.Keys);
        for (var i = 0; i < ids.Count; i++)
        {
            RemoveEmbeddedControl(ids[i]);
        }
    }

    private static void DetachControlFromParent(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, control):
                contentControl.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
        }
    }

    private void UpdateEmbeddedUiInteractivity()
    {
        var interactive = IsEnabled && _isDocumentEnabled;
        if (_embeddedHost is not null)
        {
            _embeddedHost.IsHitTestVisible = interactive;
        }

        foreach (var control in _embeddedControlsById.Values)
        {
            control.IsEnabled = interactive;
            control.IsHitTestVisible = interactive;
        }
    }

    private static bool TryGetEmbeddedElementId(string? shapeName, out string id)
    {
        var prefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;
        if (!string.IsNullOrWhiteSpace(shapeName)
            && shapeName.StartsWith(prefix, StringComparison.Ordinal))
        {
            id = shapeName.Substring(prefix.Length);
            return id.Length > 0;
        }

        id = string.Empty;
        return false;
    }

    private static Rect ToViewRect(DocRect docRect, float zoomFactor, Vector effectiveOffset)
    {
        var x = docRect.X * zoomFactor - effectiveOffset.X;
        var y = docRect.Y * zoomFactor - effectiveOffset.Y;
        var width = Math.Max(1f, docRect.Width * zoomFactor);
        var height = Math.Max(1f, docRect.Height * zoomFactor);
        return new Rect(x, y, width, height);
    }

    private static DocRect ComputeInlineShapeBounds(LayoutLine line, LayoutShape shape)
    {
        var width = shape.Width;
        var height = shape.Height;
        if (!DocTextDirectionHelpers.IsVertical(line.TextDirection))
        {
            var baseline = line.Y + line.Ascent;
            return new DocRect(line.X + shape.X, baseline - height, width, height);
        }

        var baseRotation = DocTextDirectionHelpers.GetRotationDegrees(line.TextDirection!.Value);
        var baselineLocal = line.Ascent;
        var left = shape.X;
        var top = baselineLocal - height;
        var right = left + width;
        var bottom = top + height;

        var radians = baseRotation * (MathF.PI / 180f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var p1 = RotatePoint(left, top, cos, sin, line.X, line.Y);
        var p2 = RotatePoint(right, top, cos, sin, line.X, line.Y);
        var p3 = RotatePoint(right, bottom, cos, sin, line.X, line.Y);
        var p4 = RotatePoint(left, bottom, cos, sin, line.X, line.Y);

        var minX = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
        var maxX = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
        var minY = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
        var maxY = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));
        return new DocRect(minX, minY, MathF.Max(0f, maxX - minX), MathF.Max(0f, maxY - minY));
    }

    private static DocPoint RotatePoint(float x, float y, float cos, float sin, float originX, float originY)
    {
        return new DocPoint(
            originX + (x * cos) - (y * sin),
            originY + (x * sin) + (y * cos));
    }

    private static DocumentSignature CaptureDocumentSignature(Document document)
    {
        var paragraphCount = document.ParagraphCount;
        var blockCount = document.Blocks.Count;
        var totalLength = 0;

        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = document.GetParagraph(i);
            totalLength += DocumentEditHelpers.GetParagraphLength(paragraph);
        }

        return new DocumentSignature(paragraphCount, blockCount, totalLength);
    }

    private readonly record struct DocumentSignature(int ParagraphCount, int BlockCount, int TotalLength);

    internal static bool TryGetOwner(FlowDocumentModel document, out RichTextBox owner)
    {
        owner = null!;
        var reference = document.GetValue(DocumentOwnerProperty);
        if (reference is null || !reference.TryGetTarget(out var target))
        {
            return false;
        }

        owner = target;
        return true;
    }

    internal FlowDocumentModel DocumentForRanges => _document;

    internal TextRange GetSelectionRangeForRanges() => GetNormalizedSelection();

    internal FlowTextPointer CreatePointer(TextPosition position)
    {
        var clamped = ClampPosition(position);
        return new FlowTextPointer(_document, clamped.ParagraphIndex, clamped.Offset);
    }

    internal TextRange NormalizeRange(TextRange range)
    {
        var normalized = range.Normalize();
        var start = ClampPosition(normalized.Start);
        var end = ClampPosition(normalized.End);
        if (start.CompareTo(end) > 0)
        {
            (start, end) = (end, start);
        }

        return new TextRange(start, end);
    }

    internal void SelectRangeForRanges(TextRange range, bool ensureVisible = false)
    {
        _documentView.SelectRange(NormalizeRange(range), ensureVisible);
    }

    internal void ApplyRangePropertyValue(
        TextRange range,
        FlowTextRangeProperty property,
        object? value,
        bool preserveCurrentSelection)
    {
        var command = BuildFormattingCommand(property, value);
        ExecuteOnRange(range, preserveCurrentSelection, () =>
        {
            if (!TryExecuteEditorCommand(command.CommandId, command.Payload))
            {
                throw new InvalidOperationException($"Unable to execute '{command.CommandId}' for property '{property}'.");
            }
        });
    }

    internal object? GetRangePropertyValue(
        TextRange range,
        FlowTextRangeProperty property,
        bool preserveCurrentSelection)
    {
        object? result = null;
        ExecuteOnRange(range, preserveCurrentSelection, () =>
        {
            if (!_documentView.TryGetService<IFormattingState>(out var formattingState))
            {
                result = null;
                return;
            }

            var snapshot = formattingState.GetSnapshot();
            result = property switch
            {
                FlowTextRangeProperty.FontFamily => ResolveValue(snapshot.FontFamily),
                FlowTextRangeProperty.FontSize => ResolveValue(snapshot.FontSize),
                FlowTextRangeProperty.FontWeight => ResolveValue(snapshot.Bold, bold => bold ? DocFontWeight.Bold : DocFontWeight.Normal),
                FlowTextRangeProperty.FontStyle => ResolveValue(snapshot.Italic, italic => italic ? DocFontStyle.Italic : DocFontStyle.Normal),
                FlowTextRangeProperty.UnderlineStyle => ResolveValue(snapshot.UnderlineStyle),
                FlowTextRangeProperty.UnderlineColor => ResolveValue(snapshot.UnderlineColor),
                FlowTextRangeProperty.Foreground => ResolveValue(snapshot.FontColor),
                FlowTextRangeProperty.Highlight => ResolveValue(snapshot.HighlightColor),
                FlowTextRangeProperty.Strikethrough => ResolveValue(snapshot.Strikethrough),
                FlowTextRangeProperty.SmallCaps => ResolveValue(snapshot.SmallCaps),
                FlowTextRangeProperty.Caps => ResolveValue(snapshot.Caps),
                FlowTextRangeProperty.VerticalPosition => ResolveValue(snapshot.VerticalPosition),
                FlowTextRangeProperty.TextOutline => ResolveValue(snapshot.TextOutline),
                FlowTextRangeProperty.TextShadow => ResolveValue(snapshot.TextShadow),
                FlowTextRangeProperty.TextEmboss => ResolveValue(snapshot.TextEmboss),
                FlowTextRangeProperty.TextImprint => ResolveValue(snapshot.TextImprint),
                FlowTextRangeProperty.LetterSpacing => ResolveValue(snapshot.LetterSpacing),
                FlowTextRangeProperty.HorizontalScale => ResolveValue(snapshot.HorizontalScale),
                FlowTextRangeProperty.BaselineOffset => ResolveValue(snapshot.BaselineOffset),
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported range property.")
            };
        });

        return result;
    }

    private static object? ResolveValue<T>(EditorValue<T> value)
    {
        if (value.IsMixed)
        {
            return FlowTextRange.MixedValue;
        }

        return value.HasValue ? value.Value : null;
    }

    private static object? ResolveValue<TInput, TOutput>(EditorValue<TInput> value, Func<TInput, TOutput> selector)
    {
        if (value.IsMixed)
        {
            return FlowTextRange.MixedValue;
        }

        if (!value.HasValue)
        {
            return null;
        }

        return selector(value.Value!);
    }

    private (string CommandId, object? Payload) BuildFormattingCommand(FlowTextRangeProperty property, object? value)
    {
        return property switch
        {
            FlowTextRangeProperty.FontFamily => (EditorHomeCommandIds.Font.FamilySet, GetRequiredString(value, property)),
            FlowTextRangeProperty.FontSize => (EditorHomeCommandIds.Font.SizeSet, GetRequiredFloat(value, property)),
            FlowTextRangeProperty.FontWeight => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: GetRequiredFontWeight(value, property),
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.FontStyle => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: GetRequiredFontStyle(value, property),
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.UnderlineStyle => (EditorHomeCommandIds.Font.UnderlineStyleSet, GetRequiredUnderlineStyle(value, property)),
            FlowTextRangeProperty.UnderlineColor => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: GetOptionalColor(value, property),
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.Foreground => (EditorHomeCommandIds.Font.ColorSet, GetOptionalColor(value, property)),
            FlowTextRangeProperty.Highlight => (EditorHomeCommandIds.Font.HighlightSet, GetOptionalColor(value, property)),
            FlowTextRangeProperty.Strikethrough => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: GetRequiredBool(value, property),
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.SmallCaps => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: GetRequiredBool(value, property),
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.Caps => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: GetRequiredBool(value, property),
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.VerticalPosition => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: GetRequiredVerticalPosition(value, property),
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.TextOutline => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: GetRequiredBool(value, property),
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.TextShadow => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: GetRequiredBool(value, property),
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.TextEmboss => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: GetRequiredBool(value, property),
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.TextImprint => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: GetRequiredBool(value, property),
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.LetterSpacing => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: GetRequiredFloat(value, property),
                HorizontalScale: null,
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.HorizontalScale => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: GetRequiredFloat(value, property),
                BaselineOffset: null,
                OpenTypeFeatures: null)),
            FlowTextRangeProperty.BaselineOffset => (EditorHomeCommandIds.Font.DialogApply, new EditorFontDialogOptions(
                FontFamily: null,
                FontSize: null,
                FontWeight: null,
                FontStyle: null,
                UnderlineStyle: null,
                UnderlineColor: null,
                FontColor: null,
                Strikethrough: null,
                SmallCaps: null,
                Caps: null,
                VerticalPosition: null,
                TextOutline: null,
                TextShadow: null,
                TextEmboss: null,
                TextImprint: null,
                LetterSpacing: null,
                HorizontalScale: null,
                BaselineOffset: GetRequiredFloat(value, property),
                OpenTypeFeatures: null)),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported range property.")
        };
    }

    private void ExecuteOnRange(TextRange range, bool preserveCurrentSelection, Action action)
    {
        var normalized = NormalizeRange(range);
        if (!preserveCurrentSelection)
        {
            _documentView.SelectRange(normalized, ensureVisible: false);
            action();
            return;
        }

        var previous = GetNormalizedSelection();
        var previousSuppress = _suppressSelectionChanged;
        _suppressSelectionChanged = true;
        try
        {
            _documentView.SelectRange(normalized, ensureVisible: false);
            action();
        }
        finally
        {
            _documentView.SelectRange(previous, ensureVisible: false);
            _suppressSelectionChanged = previousSuppress;
        }
    }

    private bool TryExecuteEditorCommand(string commandId, object? payload, bool recordHistory = true)
    {
        if (!_documentView.TryGetService<IEditorCommandRouter>(out var router))
        {
            return false;
        }

        if (!router.CanExecute(commandId, payload, context: null))
        {
            return false;
        }

        return router.ExecuteAsync(commandId, payload, context: null, recordHistory: recordHistory).GetAwaiter().GetResult();
    }

    private void SetCaretPosition(FlowTextPointer pointer)
    {
        if (!ReferenceEquals(pointer.Document, _document))
        {
            throw new InvalidOperationException("CaretPosition must reference the current RichTextBox.Document.");
        }

        var position = ClampPosition(pointer.ToTextPosition());
        _documentView.SelectRange(new TextRange(position, position), ensureVisible: false);
    }

    private TextRange GetNormalizedSelection()
    {
        return NormalizeRange(_documentView.Selection);
    }

    private TextPosition ClampPosition(TextPosition position)
    {
        var editorDocument = _documentView.Document;
        var paragraphCount = editorDocument.ParagraphCount;
        if (paragraphCount <= 0)
        {
            return new TextPosition(0, 0);
        }

        var paragraphIndex = Math.Clamp(position.ParagraphIndex, 0, paragraphCount - 1);
        var paragraph = editorDocument.GetParagraph(paragraphIndex);
        var maxOffset = DocumentEditHelpers.GetParagraphLength(paragraph);
        var offset = Math.Clamp(position.Offset, 0, maxOffset);
        return new TextPosition(paragraphIndex, offset);
    }

    private TextPosition GetValidatedPosition(FlowTextPointer pointer)
    {
        if (!ReferenceEquals(pointer.Document, _document))
        {
            throw new InvalidOperationException("Pointer must reference the current RichTextBox.Document.");
        }

        return ClampPosition(pointer.ToTextPosition());
    }

    private static bool IsFlowDocumentStructurallyEmpty(FlowDocumentModel document)
    {
        if (document.Blocks.Count == 0)
        {
            return true;
        }

        if (document.Blocks.Count != 1 || document.Blocks[0] is not Paragraph paragraph)
        {
            return false;
        }

        if (paragraph.Inlines.Count == 0)
        {
            return true;
        }

        if (paragraph.Inlines.Count != 1 || paragraph.Inlines[0] is not Run run)
        {
            return false;
        }

        return string.IsNullOrEmpty(run.Text);
    }

    private void RaiseTextChanged()
    {
        if (_changeDepth > 0)
        {
            _hasPendingTextChanged = true;
            return;
        }

        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string GetRequiredString(object? value, FlowTextRangeProperty property)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new ArgumentException($"Property '{property}' requires a non-empty string value.", nameof(value));
    }

    private static float GetRequiredFloat(object? value, FlowTextRangeProperty property)
    {
        return value switch
        {
            float number => number,
            double number => (float)number,
            int number => number,
            long number => number,
            decimal number => (float)number,
            _ => throw new ArgumentException($"Property '{property}' requires a numeric value.", nameof(value))
        };
    }

    private static bool GetRequiredBool(object? value, FlowTextRangeProperty property)
    {
        if (value is bool flag)
        {
            return flag;
        }

        throw new ArgumentException($"Property '{property}' requires a Boolean value.", nameof(value));
    }

    private static DocFontWeight GetRequiredFontWeight(object? value, FlowTextRangeProperty property)
    {
        return value switch
        {
            DocFontWeight weight => weight,
            FlowFontWeight flowWeight => flowWeight == FlowFontWeight.Bold ? DocFontWeight.Bold : DocFontWeight.Normal,
            _ => throw new ArgumentException(
                $"Property '{property}' requires {nameof(DocFontWeight)} or {nameof(FlowFontWeight)} value.",
                nameof(value))
        };
    }

    private static DocFontStyle GetRequiredFontStyle(object? value, FlowTextRangeProperty property)
    {
        return value switch
        {
            DocFontStyle style => style,
            FlowFontStyle flowStyle => flowStyle == FlowFontStyle.Italic ? DocFontStyle.Italic : DocFontStyle.Normal,
            _ => throw new ArgumentException(
                $"Property '{property}' requires {nameof(DocFontStyle)} or {nameof(FlowFontStyle)} value.",
                nameof(value))
        };
    }

    private static DocUnderlineStyle GetRequiredUnderlineStyle(object? value, FlowTextRangeProperty property)
    {
        if (value is DocUnderlineStyle style)
        {
            return style;
        }

        throw new ArgumentException($"Property '{property}' requires a {nameof(DocUnderlineStyle)} value.", nameof(value));
    }

    private static DocVerticalPosition GetRequiredVerticalPosition(object? value, FlowTextRangeProperty property)
    {
        if (value is DocVerticalPosition verticalPosition)
        {
            return verticalPosition;
        }

        throw new ArgumentException($"Property '{property}' requires a {nameof(DocVerticalPosition)} value.", nameof(value));
    }

    private static DocColor? GetOptionalColor(object? value, FlowTextRangeProperty property)
    {
        return value switch
        {
            null => null,
            DocColor docColor => docColor,
            Color avaloniaColor => new DocColor(avaloniaColor.R, avaloniaColor.G, avaloniaColor.B, avaloniaColor.A),
            _ => throw new ArgumentException(
                $"Property '{property}' requires null, {nameof(DocColor)}, or {nameof(Color)} value.",
                nameof(value))
        };
    }

    internal bool HasDocumentPresenterForTests => _documentHost?.Content is DocumentView;
    internal TextRange SelectionForTests => _documentView.Selection;
    internal Document EditorDocumentForTests => _documentView.Document;
    internal int EmbeddedControlCountForTests => _embeddedControlsById.Count;
    internal DocumentView DocumentViewForTests => _documentView;
    internal Size AutomationExtentForTests => _documentView.Extent;
    internal Size AutomationViewportForTests => _documentView.Viewport;
    internal Vector AutomationOffsetForTests
    {
        get => _documentView.Offset;
        set => _documentView.Offset = value;
    }
    internal Size AutomationLineScrollSizeForTests => _documentView.ScrollSize;
    internal Size AutomationPageScrollSizeForTests => _documentView.PageScrollSize;

    internal string GetAutomationTextValueForTests()
    {
        var document = _documentView.Document;
        if (document.ParagraphCount <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < document.ParagraphCount; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(DocumentEditHelpers.GetParagraphText(document.GetParagraph(i)));
        }

        return builder.ToString();
    }

    internal bool TryGetFirstEmbeddedControlForTests(out Control control)
    {
        foreach (var candidate in _embeddedControlsById.Values)
        {
            control = candidate;
            return true;
        }

        control = null!;
        return false;
    }

    internal bool TryGetFirstEmbeddedControlBoundsForTests(out Rect bounds)
    {
        if (!TryGetFirstEmbeddedControlForTests(out var control))
        {
            bounds = default;
            return false;
        }

        var width = control.Bounds.Width > 0d ? control.Bounds.Width : control.Width;
        var height = control.Bounds.Height > 0d ? control.Bounds.Height : control.Height;
        bounds = new Rect(Canvas.GetLeft(control), Canvas.GetTop(control), Math.Max(0d, width), Math.Max(0d, height));
        return true;
    }

    internal static bool TryBuildClipboardContentFromDataTransferForTests(IDataTransfer dataTransfer, out ClipboardContent content)
    {
        return RichTextClipboardDataTransferParser.TryBuildClipboardContent(dataTransfer, out content);
    }

    event EventHandler? IRichTextBoxCollaborationAdapter.EditorSessionRebuilt
    {
        add => EditorSessionRebuiltInternal += value;
        remove => EditorSessionRebuiltInternal -= value;
    }

    void IRichTextBoxCollaborationAdapter.AttachSession(
        ICollabRealtimeSession session,
        Func<Guid, string?>? authorResolver)
    {
        ArgumentNullException.ThrowIfNull(session);
        _documentView.EnableCollaboration(session, authorResolver);
        _isCollaborationAttached = true;
    }

    void IRichTextBoxCollaborationAdapter.DetachSession()
    {
        _documentView.DisableCollaboration();
        _isCollaborationAttached = false;
    }

    void IRichTextBoxCollaborationAdapter.RegisterService<T>(T service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _documentView.RegisterService(service);
    }

    bool IRichTextBoxCollaborationAdapter.UnregisterService<T>()
    {
        return _documentView.UnregisterService<T>();
    }

    bool IRichTextBoxCollaborationAdapter.TryGetService<T>(out T service)
    {
        return _documentView.TryGetService(out service);
    }

    internal void ReplaceEditorDocumentForTests(Document document)
    {
        _documentView.LoadDocument(document);
        OnEditorStateChanged(this, EventArgs.Empty);
    }
}
