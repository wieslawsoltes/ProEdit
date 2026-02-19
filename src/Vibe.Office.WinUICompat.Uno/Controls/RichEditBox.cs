using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.WinUICompat.Bridges;
using Vibe.Office.WinUICompat.Documents;
using Vibe.Office.WinUICompat.Text;

namespace Vibe.Office.WinUICompat.Controls;

public sealed class RichEditBox : UserControl, IRichEditBoxCollaborationAdapter
{
    private const string OoxmlMimeFormat = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string OoxmlUtiFormat = "org.openxmlformats.wordprocessingml.document";
    private const string OoxmlWindowsFormat = "Office Open XML";
    private const string CollaborationExternalMutationMessage =
        "External RichTextDocument mutations are not supported while collaboration is active. " +
        "Use RichEditBox editing APIs/commands or replace the entire Document to reload content.";

    private static readonly ICompatClipboardBridge ClipboardBridge =
        new UnoClipboardBridge(new InMemoryCompatClipboardBridge());

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(RichEditTextDocument),
            typeof(RichEditBox),
            new PropertyMetadata(null, OnDocumentPropertyChanged));

    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(
            nameof(AcceptsTab),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(true, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(
            nameof(AcceptsReturn),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(true, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsReadOnlyCaretVisibleProperty =
        DependencyProperty.Register(
            nameof(IsReadOnlyCaretVisible),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsDocumentEnabledProperty =
        DependencyProperty.Register(
            nameof(IsDocumentEnabled),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsProofingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsProofingEnabled),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsSpellingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSpellingEnabled),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(true, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsGrammarEnabledProperty =
        DependencyProperty.Register(
            nameof(IsGrammarEnabled),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    public static readonly DependencyProperty IsStyleEnabledProperty =
        DependencyProperty.Register(
            nameof(IsStyleEnabled),
            typeof(bool),
            typeof(RichEditBox),
            new PropertyMetadata(false, OnEditorConfigurationPropertyChanged));

    private readonly Dictionary<Type, object> _services = new();
    private readonly List<ProofingDiagnostic> _proofingDiagnostics = new();
    private readonly TextSelection _selection = new(new TextPointer(0, 0), new TextPointer(0, 0));
    private readonly EngineRichEditHost _editorHost;
    private BlockCollection? _attachedCompatBlocks;
    private RichEditTextDocument? _attachedDocument;
    private bool _isCoercingDocument;
    private bool? _pendingImplicitDocument;
    private bool _isImplicitDocument = true;
    private bool _isCollaborationAttached;
    private int _changeDepth;
    private bool _pendingTextChanged;
    private bool _syncingSelectionFromDocument;
    private string _lastKnownText = string.Empty;
    private event EventHandler? SessionRebuiltInternal;

    public event RoutedEventHandler? TextChanged;

    public event RoutedEventHandler? SelectionChanged;

    public RichEditBox()
    {
        _editorHost = new EngineRichEditHost();
        Content = _editorHost;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
        IsEnabledChanged += OnIsEnabledChanged;

        SetDocument(new RichEditTextDocument(), isImplicit: true);
        AttachDocument(Document);
        ApplyEditorConfiguration();
        SyncSelectionFromDocument(raiseSelectionChanged: false);
        _lastKnownText = Document.GetText();
    }

    public RichEditTextDocument Document
    {
        get
        {
            var document = (RichEditTextDocument?)GetValue(DocumentProperty);
            if (document is not null)
            {
                return document;
            }

            if (_isCoercingDocument)
            {
                return new RichEditTextDocument();
            }

            CoerceDocument();
            return (RichEditTextDocument)GetValue(DocumentProperty);
        }
        set => SetDocument(value, isImplicit: false);
    }

    public RichEditTextDocument TextDocument
    {
        get => Document;
        set => Document = value;
    }

    public bool AcceptsTab
    {
        get => (bool)GetValue(AcceptsTabProperty);
        set => SetValue(AcceptsTabProperty, value);
    }

    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsReadOnlyCaretVisible
    {
        get => (bool)GetValue(IsReadOnlyCaretVisibleProperty);
        set => SetValue(IsReadOnlyCaretVisibleProperty, value);
    }

    public bool IsDocumentEnabled
    {
        get => (bool)GetValue(IsDocumentEnabledProperty);
        set => SetValue(IsDocumentEnabledProperty, value);
    }

    public bool IsProofingEnabled
    {
        get => (bool)GetValue(IsProofingEnabledProperty);
        set => SetValue(IsProofingEnabledProperty, value);
    }

    public bool IsSpellingEnabled
    {
        get => (bool)GetValue(IsSpellingEnabledProperty);
        set => SetValue(IsSpellingEnabledProperty, value);
    }

    public bool IsGrammarEnabled
    {
        get => (bool)GetValue(IsGrammarEnabledProperty);
        set => SetValue(IsGrammarEnabledProperty, value);
    }

    public bool IsStyleEnabled
    {
        get => (bool)GetValue(IsStyleEnabledProperty);
        set => SetValue(IsStyleEnabledProperty, value);
    }

    public TextSelection Selection => _selection;

    public TextPointer CaretPosition
    {
        get => _selection.End;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Select(value, value);
        }
    }

    public void Select(TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        var startOffset = Document.GetOffsetFromTextPointer(start);
        var endOffset = Document.GetOffsetFromTextPointer(end);
        SetSelectionInternal(startOffset, endOffset, raiseSelectionChanged: true, propagateToDocument: true);
    }

    public void BeginChange()
    {
        _changeDepth++;
    }

    public void EndChange()
    {
        if (_changeDepth <= 0)
        {
            throw new InvalidOperationException("EndChange called without matching BeginChange.");
        }

        _changeDepth--;
        if (_changeDepth == 0 && _pendingTextChanged)
        {
            _pendingTextChanged = false;
            TextChanged?.Invoke(this, new RoutedEventArgs());
        }
    }

    public bool Copy()
    {
        var range = Document.GetRange(_selection.Start.Offset, _selection.End.Offset);
        var selectedText = range.GetText();
        var fragment = range.GetDocument();
        return ClipboardBridge.TrySetRichDocument(fragment, selectedText);
    }

    public bool Cut()
    {
        if (IsReadOnly)
        {
            return false;
        }

        var range = Document.GetRange(_selection.Start.Offset, _selection.End.Offset);
        var selectedText = range.GetText();
        var fragment = range.GetDocument();
        ClipboardBridge.TrySetRichDocument(fragment, selectedText);
        range.SetText(string.Empty);
        return true;
    }

    public bool Paste()
    {
        if (IsReadOnly)
        {
            return false;
        }

        var range = Document.GetRange(_selection.Start.Offset, _selection.End.Offset);
        if (ClipboardBridge.TryGetRichDocument(out var fragment))
        {
            return range.SetDocument(fragment);
        }

        if (!ClipboardBridge.TryGetPlainText(out var text))
        {
            return false;
        }

        range.SetText(text);
        return true;
    }

    public bool Undo()
    {
        if (IsReadOnly)
        {
            return false;
        }

        return Document.Undo();
    }

    public bool Redo()
    {
        if (IsReadOnly)
        {
            return false;
        }

        return Document.Redo();
    }

    public bool SelectAll()
    {
        var length = Document.GetText().Length;
        SetSelectionInternal(0, length, raiseSelectionChanged: true, propagateToDocument: true);
        return true;
    }

    public void ReplaceText(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        Document.SetText(text);
    }

    public TextPointer? GetPositionFromPoint(Point point, bool snapToText)
    {
        return Document.GetPositionFromPoint((float)point.X, (float)point.Y, snapToText);
    }

    public ProofingDiagnostic? GetSpellingError(TextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!IsProofingEnabled || !IsSpellingEnabled)
        {
            return null;
        }

        for (var i = 0; i < _proofingDiagnostics.Count; i++)
        {
            var diagnostic = _proofingDiagnostics[i];
            if (diagnostic.Kind != ProofingIssueKind.Spelling || diagnostic.ParagraphIndex != position.ParagraphIndex)
            {
                continue;
            }

            var diagnosticEnd = diagnostic.StartOffset + Math.Max(0, diagnostic.Length);
            if (position.Offset >= diagnostic.StartOffset && position.Offset < diagnosticEnd)
            {
                return diagnostic;
            }
        }

        return null;
    }

    public TextRange? GetSpellingErrorRange(TextPointer position)
    {
        var diagnostic = GetSpellingError(position);
        if (!diagnostic.HasValue)
        {
            return null;
        }

        var value = diagnostic.Value;
        var start = new TextPointer(value.ParagraphIndex, value.StartOffset);
        var end = new TextPointer(value.ParagraphIndex, value.StartOffset + Math.Max(0, value.Length));
        return new TextRange(start, end);
    }

    public TextPointer? GetNextSpellingErrorPosition(TextPointer position, LogicalDirection direction)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!IsProofingEnabled || !IsSpellingEnabled)
        {
            return null;
        }

        if (direction == LogicalDirection.Forward)
        {
            for (var i = 0; i < _proofingDiagnostics.Count; i++)
            {
                var diagnostic = _proofingDiagnostics[i];
                if (diagnostic.Kind != ProofingIssueKind.Spelling)
                {
                    continue;
                }

                if (CompareDiagnosticStartToPosition(diagnostic, position) > 0)
                {
                    return new TextPointer(diagnostic.ParagraphIndex, diagnostic.StartOffset, LogicalDirection.Forward);
                }
            }

            return null;
        }

        for (var i = _proofingDiagnostics.Count - 1; i >= 0; i--)
        {
            var diagnostic = _proofingDiagnostics[i];
            if (diagnostic.Kind != ProofingIssueKind.Spelling)
            {
                continue;
            }

            if (CompareDiagnosticStartToPosition(diagnostic, position) < 0)
            {
                return new TextPointer(diagnostic.ParagraphIndex, diagnostic.StartOffset, LogicalDirection.Backward);
            }
        }

        return null;
    }

    public void SetProofingDiagnostics(IEnumerable<ProofingDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _proofingDiagnostics.Clear();
        _proofingDiagnostics.AddRange(diagnostics);
        _proofingDiagnostics.Sort(static (left, right) =>
        {
            var paragraphCompare = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
            if (paragraphCompare != 0)
            {
                return paragraphCompare;
            }

            return left.StartOffset.CompareTo(right.StartOffset);
        });
    }

    public void ClearProofingDiagnostics()
    {
        _proofingDiagnostics.Clear();
    }

    public bool ShouldSerializeDocument()
    {
        if (!_isImplicitDocument)
        {
            return true;
        }

        return !string.IsNullOrEmpty(Document.GetText());
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new RichEditBoxAutomationPeer(this);
    }

    private static int CompareDiagnosticStartToPosition(ProofingDiagnostic diagnostic, TextPointer position)
    {
        var paragraphCompare = diagnostic.ParagraphIndex.CompareTo(position.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return diagnostic.StartOffset.CompareTo(position.Offset);
    }

    internal string GetAutomationTextValueForTests()
    {
        return Document.GetText();
    }

    private void RaiseTextChanged()
    {
        if (_changeDepth > 0)
        {
            _pendingTextChanged = true;
            return;
        }

        TextChanged?.Invoke(this, new RoutedEventArgs());
    }

    private void SetDocument(RichEditTextDocument document, bool isImplicit)
    {
        ArgumentNullException.ThrowIfNull(document);

        var current = (RichEditTextDocument?)GetValue(DocumentProperty);
        if (ReferenceEquals(current, document))
        {
            if (!isImplicit)
            {
                _isImplicitDocument = false;
            }

            _pendingImplicitDocument = null;
            return;
        }

        _pendingImplicitDocument = isImplicit;
        SetValue(DocumentProperty, document);
    }

    private static void OnDocumentPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not RichEditBox richEditBox)
        {
            return;
        }

        if (args.NewValue is not RichEditTextDocument document)
        {
            richEditBox.CoerceDocument();
            return;
        }

        richEditBox.OnDocumentChanged(document);
    }

    private void CoerceDocument()
    {
        if (_isCoercingDocument)
        {
            return;
        }

        _isCoercingDocument = true;
        try
        {
            SetDocument(new RichEditTextDocument(), isImplicit: true);
        }
        finally
        {
            _isCoercingDocument = false;
        }
    }

    private void OnDocumentChanged(RichEditTextDocument document)
    {
        _isImplicitDocument = _pendingImplicitDocument ?? false;
        _pendingImplicitDocument = null;

        AttachDocument(document);
        ApplyEditorConfiguration();
        SyncSelectionFromDocument(raiseSelectionChanged: true);

        _lastKnownText = Document.GetText();
        RaiseTextChanged();
        SessionRebuiltInternal?.Invoke(this, EventArgs.Empty);
    }

    private void AttachDocument(RichEditTextDocument document)
    {
        if (_attachedDocument is not null)
        {
            _attachedDocument.Changed -= OnDocumentStateChanged;
            if (_attachedCompatBlocks is not null)
            {
                _attachedCompatBlocks.CollectionChanged -= OnCompatBlocksChanged;
            }
        }

        ConfigureDocumentEmbeddedUiSupport(document);
        _attachedDocument = document;
        _attachedDocument.Changed += OnDocumentStateChanged;
        _attachedCompatBlocks = _attachedDocument.Document.Blocks;
        _attachedCompatBlocks.CollectionChanged += OnCompatBlocksChanged;
        _editorHost.Document = document;
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        SyncSelectionFromDocument(raiseSelectionChanged: true);

        var currentText = Document.GetText();
        if (!string.Equals(_lastKnownText, currentText, StringComparison.Ordinal))
        {
            _lastKnownText = currentText;
            RaiseTextChanged();
        }

        _editorHost.InvalidateSurface();
    }

    private void SyncSelectionFromDocument(bool raiseSelectionChanged)
    {
        _syncingSelectionFromDocument = true;
        try
        {
            SetSelectionInternal(
                Document.SelectionStartOffset,
                Document.SelectionEndOffset,
                raiseSelectionChanged,
                propagateToDocument: false);
        }
        finally
        {
            _syncingSelectionFromDocument = false;
        }
    }

    private void SetSelectionInternal(
        int startOffset,
        int endOffset,
        bool raiseSelectionChanged,
        bool propagateToDocument)
    {
        var textLength = Document.GetText().Length;
        var start = Math.Clamp(startOffset, 0, textLength);
        var end = Math.Clamp(endOffset, 0, textLength);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var startPointer = Document.GetTextPointer(start);
        var endPointer = Document.GetTextPointer(end);

        var hasChanged =
            _selection.Start.ParagraphIndex != startPointer.ParagraphIndex
            || _selection.Start.Offset != startPointer.Offset
            || _selection.End.ParagraphIndex != endPointer.ParagraphIndex
            || _selection.End.Offset != endPointer.Offset;

        _selection.Select(startPointer, endPointer);

        if (propagateToDocument && !_syncingSelectionFromDocument)
        {
            Document.SetSelection(start, end);
        }

        if (raiseSelectionChanged && hasChanged)
        {
            SelectionChanged?.Invoke(this, new RoutedEventArgs());
        }
    }

    private static void OnEditorConfigurationPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is RichEditBox richEditBox)
        {
            richEditBox.ApplyEditorConfiguration();
        }
    }

    private void ApplyEditorConfiguration()
    {
        var document = Document;
        document.AcceptsTab = AcceptsTab;
        document.AcceptsReturn = AcceptsReturn;
        document.IsReadOnly = IsReadOnly;

        _editorHost.ShowCaretWhenReadOnly = IsReadOnlyCaretVisible;
        _editorHost.IsEnabled = IsDocumentEnabled || !IsReadOnly;
        _editorHost.IsHitTestVisible = IsDocumentEnabled || !IsReadOnly || IsReadOnlyCaretVisible;
        _editorHost.IsEmbeddedUiInteractive = IsEnabled && IsDocumentEnabled;
        _editorHost.InvalidateSurface();
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ApplyEditorConfiguration();
    }

    private static void ConfigureDocumentEmbeddedUiSupport(RichEditTextDocument document)
    {
        EmbeddedUiDocumentConfigurator.Configure(document);
    }

    private void OnDragOver(object sender, DragEventArgs args)
    {
        if (!IsEnabled || IsReadOnly || !CanAcceptDrop(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs args)
    {
        if (!IsEnabled || IsReadOnly || !CanAcceptDrop(args.DataView))
        {
            return;
        }

        if (TryPasteDropData(args))
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.Handled = true;
        }
    }

    private bool TryPasteDropData(DragEventArgs args)
    {
        if (args.DataView is null)
        {
            return false;
        }

        var point = args.GetPosition(_editorHost);
        var caret = Document.GetPositionFromPoint((float)point.X, (float)point.Y, snapToText: true);
        if (caret is not null)
        {
            Select(caret, caret);
        }

        var range = Document.GetRange(_selection.Start.Offset, _selection.End.Offset);
        if (UnoClipboardBridge.TryBuildRichDocumentFromDataPackageView(args.DataView, out var richDocument))
        {
            return range.SetDocument(richDocument);
        }

        if (!TryGetDropText(args.DataView, out var text))
        {
            return false;
        }

        range.SetText(text);
        return true;
    }

    private static bool CanAcceptDrop(DataPackageView? dataView)
    {
        if (dataView is null)
        {
            return false;
        }

        return dataView.Contains(StandardDataFormats.Text)
               || dataView.Contains(StandardDataFormats.Rtf)
               || dataView.Contains(StandardDataFormats.Html)
               || dataView.Contains("Rich Text Format")
               || dataView.Contains("HTML Format")
               || dataView.Contains("text/html")
               || dataView.Contains(OoxmlMimeFormat)
               || dataView.Contains(OoxmlUtiFormat)
               || dataView.Contains(OoxmlWindowsFormat);
    }

    private static bool TryGetDropText(DataPackageView dataView, out string text)
    {
        text = string.Empty;
        try
        {
            if (!dataView.Contains(StandardDataFormats.Text))
            {
                return false;
            }

            text = dataView.GetTextAsync().AsTask().GetAwaiter().GetResult() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private void OnCompatBlocksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_isCollaborationAttached || Document.IsApplyingInternalDocumentSync)
        {
            return;
        }

        RevertExternalCompatMutation();
        throw new InvalidOperationException(CollaborationExternalMutationMessage);
    }

    private void RevertExternalCompatMutation()
    {
        var snapshot = Document.CreateEditorSnapshotDocument();
        Document.LoadDocumentSnapshot(snapshot);
        _lastKnownText = Document.GetText();
    }

    event EventHandler? IRichEditBoxCollaborationAdapter.SessionRebuilt
    {
        add => SessionRebuiltInternal += value;
        remove => SessionRebuiltInternal -= value;
    }

    void IRichEditBoxCollaborationAdapter.AttachSession(ICollabRealtimeSession session, Func<Guid, string?>? authorResolver)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (authorResolver is not null)
        {
            RegisterService(authorResolver);
        }

        _isCollaborationAttached = true;
    }

    void IRichEditBoxCollaborationAdapter.DetachSession()
    {
        _isCollaborationAttached = false;
    }

    void IRichEditBoxCollaborationAdapter.RegisterService<T>(T service)
    {
        RegisterService(service);
    }

    bool IRichEditBoxCollaborationAdapter.UnregisterService<T>()
    {
        return UnregisterService<T>();
    }

    bool IRichEditBoxCollaborationAdapter.TryGetService<T>(out T service)
    {
        return TryGetService(out service);
    }

    public void RegisterService<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
    }

    public bool UnregisterService<T>() where T : class
    {
        return _services.Remove(typeof(T));
    }

    public bool TryGetService<T>(out T service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var candidate) && candidate is T typed)
        {
            service = typed;
            return true;
        }

        service = null!;
        return false;
    }
}
