using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Html;

namespace ProEdit.Word.Avalonia;

public partial class HtmlSourceWindow : Window
{
    private readonly TextBox _htmlEditor;
    private readonly Button _applyButton;
    private readonly Button _refreshButton;
    private readonly CheckBox _autoSyncCheckBox;
    private readonly DispatcherTimer _applyTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _proofingTimer;

    private DocumentView? _view;
    private HtmlDualViewSync _sync;
    private HtmlSyncState? _state;
    private bool _suppressTextChange;
    private bool _suppressDocumentRefresh;
    private bool _pendingApply;
    private CancellationTokenSource? _proofingCts;
    private IReadOnlyList<ProofingSourceDiagnostic> _proofingDiagnostics = Array.Empty<ProofingSourceDiagnostic>();

    public HtmlSourceWindow()
    {
        InitializeComponent();

        _htmlEditor = this.FindControl<TextBox>("HtmlEditor")!;
        _applyButton = this.FindControl<Button>("ApplyButton")!;
        _refreshButton = this.FindControl<Button>("RefreshButton")!;
        _autoSyncCheckBox = this.FindControl<CheckBox>("AutoSyncCheckBox")!;

        _htmlEditor.TextChanged += OnHtmlTextChanged;
        _applyButton.Click += OnApplyClicked;
        _refreshButton.Click += OnRefreshClicked;

        _applyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _applyTimer.Tick += OnApplyTimerTick;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        _proofingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _proofingTimer.Tick += OnProofingTimerTick;

        _sync = new HtmlDualViewSync(CreateHtmlOptions());
    }

    public HtmlSourceWindow(DocumentView view)
        : this()
    {
        SetDocumentView(view);
    }

    public void SetDocumentView(DocumentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!ReferenceEquals(_view, view))
        {
            DetachView();
            _view = view;
            _view.EditorStateChanged += OnEditorStateChanged;
        }

        RefreshFromDocument();
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachView();
        _applyTimer.Stop();
        _refreshTimer.Stop();
        _proofingTimer.Stop();
        CancelProofing();
        base.OnClosed(e);
    }

    private void DetachView()
    {
        if (_view is not null)
        {
            _view.EditorStateChanged -= OnEditorStateChanged;
        }

        _view = null;
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        if (_suppressDocumentRefresh)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshFromDocument();
    }

    private void RefreshFromDocument()
    {
        if (_view is null)
        {
            return;
        }

        var options = CreateHtmlOptions();
        var html = HtmlDocumentConverter.ToHtml(_view.Document, options);
        var ast = HtmlDocumentConverter.ParseAst(html.AsSpan(), options);
        _state = new HtmlSyncState(html, ast, DocumentClone.Clone(_view.Document));
        SetEditorText(html);
        SetDirty(false);
        ScheduleProofing();
    }

    private void OnHtmlTextChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressTextChange)
        {
            return;
        }

        SetDirty(true);
        ScheduleProofing();
        if (_autoSyncCheckBox.IsChecked == true)
        {
            _applyTimer.Stop();
            _applyTimer.Start();
        }
    }

    private async void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        await ApplySourceAsync();
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshFromDocument();
    }

    private async void OnApplyTimerTick(object? sender, EventArgs e)
    {
        _applyTimer.Stop();
        if (_pendingApply)
        {
            await ApplySourceAsync();
        }
    }

    private async Task ApplySourceAsync()
    {
        if (_view is null)
        {
            return;
        }

        var text = _htmlEditor.Text ?? string.Empty;
        _state ??= _sync.Initialize(text);

        _state = _sync.ApplyHtmlEdit(_state, text);
        _suppressDocumentRefresh = true;
        await _view.LoadDocumentAsync(DocumentClone.Clone(_state.Document));
        _suppressDocumentRefresh = false;

        SetEditorText(_state.HtmlText);
        SetDirty(false);
        ScheduleProofing();
    }

    private void SetEditorText(string text)
    {
        _suppressTextChange = true;
        _htmlEditor.Text = text;
        _suppressTextChange = false;
    }

    private void SetDirty(bool isDirty)
    {
        _pendingApply = isDirty;
        _applyButton.IsEnabled = isDirty;
    }

    private void ScheduleProofing()
    {
        _proofingTimer.Stop();
        _proofingTimer.Start();
    }

    private async void OnProofingTimerTick(object? sender, EventArgs e)
    {
        _proofingTimer.Stop();
        await RefreshProofingAsync();
    }

    private async Task RefreshProofingAsync()
    {
        if (_view is null)
        {
            _proofingDiagnostics = Array.Empty<ProofingSourceDiagnostic>();
            return;
        }

        if (!_view.TryGetService<IProofingProfileRegistry>(out var profiles))
        {
            _proofingDiagnostics = Array.Empty<ProofingSourceDiagnostic>();
            return;
        }

        _view.TryGetService<ILanguageDetector>(out var detector);
        var html = _htmlEditor.Text ?? string.Empty;
        CancelProofing();
        _proofingCts = new CancellationTokenSource();

        try
        {
            var diagnostics = await HtmlSourceProofing.AnalyzeAsync(
                html,
                profiles,
                CreateHtmlOptions(),
                language: null,
                languageDetector: detector,
                cancellationToken: _proofingCts.Token);
            _proofingDiagnostics = diagnostics;
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled refreshes.
        }
    }

    private void CancelProofing()
    {
        if (_proofingCts is null)
        {
            return;
        }

        _proofingCts.Cancel();
        _proofingCts.Dispose();
        _proofingCts = null;
    }

    private static HtmlOptions CreateHtmlOptions()
    {
        return new HtmlOptions
        {
            AllowScripts = false,
            AllowStyles = true,
            NormalizeLineEndings = true,
            PreserveUnknownElements = true,
            PrettyPrint = true
        };
    }
}
