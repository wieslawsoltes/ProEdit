using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vibe.Office.Documents;
using Vibe.Office.Html;

namespace Vibe.Word.Avalonia;

public partial class HtmlSourceWindow : Window
{
    private readonly TextBox _htmlEditor;
    private readonly Button _applyButton;
    private readonly Button _refreshButton;
    private readonly CheckBox _autoSyncCheckBox;
    private readonly DispatcherTimer _applyTimer;
    private readonly DispatcherTimer _refreshTimer;

    private DocumentView? _view;
    private HtmlDualViewSync _sync;
    private HtmlSyncState? _state;
    private bool _suppressTextChange;
    private bool _suppressDocumentRefresh;
    private bool _pendingApply;

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
    }

    private void OnHtmlTextChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressTextChange)
        {
            return;
        }

        SetDirty(true);
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

    private static HtmlOptions CreateHtmlOptions()
    {
        return new HtmlOptions
        {
            AllowScripts = false,
            AllowStyles = true,
            NormalizeLineEndings = true,
            PreserveUnknownElements = true
        };
    }
}
