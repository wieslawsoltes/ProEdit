using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public partial class SpellingGrammarWindow : Window
{
    private readonly TextBlock _issueSummaryText;
    private readonly TextBlock _issueKindText;
    private readonly TextBlock _issueMessageText;
    private readonly TextBlock _contextText;
    private readonly ListBox _suggestionsList;
    private readonly Button _changeButton;
    private readonly Button _changeAllButton;
    private readonly Button _autoCorrectButton;
    private readonly Button _ignoreButton;
    private readonly Button _ignoreAllButton;
    private readonly Button _addButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;

    private DocumentView? _view;
    private IProofingService? _proofingService;
    private IProofingProfileRegistry? _profiles;
    private IFindReplaceService? _findReplaceService;
    private IAutoCorrectService? _autoCorrectService;

    private readonly List<ProofingItem> _items = new();
    private int _currentIndex = -1;
    private bool _suppressSelection;

    public SpellingGrammarWindow()
    {
        InitializeComponent();

        _issueSummaryText = this.FindControl<TextBlock>("IssueSummaryText")!;
        _issueKindText = this.FindControl<TextBlock>("IssueKindText")!;
        _issueMessageText = this.FindControl<TextBlock>("IssueMessageText")!;
        _contextText = this.FindControl<TextBlock>("ContextText")!;
        _suggestionsList = this.FindControl<ListBox>("SuggestionsList")!;
        _changeButton = this.FindControl<Button>("ChangeButton")!;
        _changeAllButton = this.FindControl<Button>("ChangeAllButton")!;
        _autoCorrectButton = this.FindControl<Button>("AutoCorrectButton")!;
        _ignoreButton = this.FindControl<Button>("IgnoreButton")!;
        _ignoreAllButton = this.FindControl<Button>("IgnoreAllButton")!;
        _addButton = this.FindControl<Button>("AddButton")!;
        _previousButton = this.FindControl<Button>("PreviousButton")!;
        _nextButton = this.FindControl<Button>("NextButton")!;

        _suggestionsList.SelectionChanged += OnSuggestionSelectionChanged;
        _changeButton.Click += OnChangeClicked;
        _changeAllButton.Click += OnChangeAllClicked;
        _autoCorrectButton.Click += OnAutoCorrectClicked;
        _ignoreButton.Click += OnIgnoreClicked;
        _ignoreAllButton.Click += OnIgnoreAllClicked;
        _addButton.Click += OnAddClicked;
        _previousButton.Click += OnPreviousClicked;
        _nextButton.Click += OnNextClicked;

        UpdateUiState();
    }

    public SpellingGrammarWindow(DocumentView view)
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

        AttachServices();
        _proofingService?.RefreshAll();
        RefreshDiagnostics();
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachView();
        base.OnClosed(e);
    }

    private void DetachView()
    {
        if (_view is not null)
        {
            _view.EditorStateChanged -= OnEditorStateChanged;
        }

        if (_proofingService is not null)
        {
            _proofingService.Updated -= OnProofingUpdated;
        }

        _view = null;
        _proofingService = null;
        _profiles = null;
        _findReplaceService = null;
        _autoCorrectService = null;
    }

    private void AttachServices()
    {
        if (_view is null)
        {
            return;
        }

        if (_view.TryGetService<IProofingService>(out var proofing))
        {
            if (!ReferenceEquals(_proofingService, proofing))
            {
                if (_proofingService is not null)
                {
                    _proofingService.Updated -= OnProofingUpdated;
                }

                _proofingService = proofing;
                _proofingService.Updated += OnProofingUpdated;
            }
        }
        else
        {
            _proofingService = null;
        }

        _profiles = null;
        _findReplaceService = null;
        _autoCorrectService = null;

        if (_view.TryGetService<IProofingProfileRegistry>(out var profiles))
        {
            _profiles = profiles;
        }

        if (_view.TryGetService<IFindReplaceService>(out var findReplaceService))
        {
            _findReplaceService = findReplaceService;
        }

        if (_view.TryGetService<IAutoCorrectService>(out var autoCorrectService))
        {
            _autoCorrectService = autoCorrectService;
        }
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }

        Dispatcher.UIThread.Post(RefreshDiagnostics, DispatcherPriority.Background);
    }

    private void OnProofingUpdated(object? sender, ProofingUpdatedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshDiagnostics, DispatcherPriority.Background);
    }

    private void RefreshDiagnostics()
    {
        _items.Clear();
        _currentIndex = -1;

        if (_view is null || _proofingService is null)
        {
            UpdateUiState();
            return;
        }

        var document = _view.Document;
        var paragraphCount = document.ParagraphCount;
        for (var i = 0; i < paragraphCount; i++)
        {
            var diagnostics = _proofingService.GetParagraphDiagnostics(i);
            if (diagnostics.Count == 0)
            {
                continue;
            }

            var paragraph = document.GetParagraph(i);
            var paragraphText = DocumentEditHelpers.GetParagraphText(paragraph);
            foreach (var diagnostic in diagnostics)
            {
                _items.Add(new ProofingItem(diagnostic, paragraphText));
            }
        }

        if (_items.Count > 1)
        {
            _items.Sort(static (left, right) =>
            {
                var paragraphCompare = left.Diagnostic.ParagraphIndex.CompareTo(right.Diagnostic.ParagraphIndex);
                if (paragraphCompare != 0)
                {
                    return paragraphCompare;
                }

                return left.Diagnostic.StartOffset.CompareTo(right.Diagnostic.StartOffset);
            });
        }

        if (_items.Count > 0)
        {
            _currentIndex = 0;
        }

        UpdateUiState();
    }

    private void UpdateUiState()
    {
        if (_items.Count == 0)
        {
            _issueSummaryText.Text = "No spelling or grammar issues found.";
            _issueKindText.Text = string.Empty;
            _issueMessageText.Text = string.Empty;
            _contextText.Text = string.Empty;
            EnsureContextInlines().Clear();
            _suggestionsList.ItemsSource = Array.Empty<string>();
            SetActionButtonsEnabled(false);
            return;
        }

        if (_currentIndex < 0)
        {
            _currentIndex = 0;
        }
        else if (_currentIndex >= _items.Count)
        {
            _currentIndex = _items.Count - 1;
        }

        var item = _items[_currentIndex];
        var diagnostic = item.Diagnostic;
        var kindLabel = diagnostic.Kind switch
        {
            ProofingIssueKind.Grammar => "Grammar",
            ProofingIssueKind.Style => "Style",
            _ => "Spelling"
        };

        _issueSummaryText.Text = $"{_currentIndex + 1} of {_items.Count}";
        _issueKindText.Text = kindLabel;
        _issueMessageText.Text = string.IsNullOrWhiteSpace(diagnostic.Message) ? diagnostic.Text : diagnostic.Message;

        UpdateContext(item);
        UpdateSuggestions(diagnostic);
        SetActionButtonsEnabled(true);

        SelectDiagnosticRange(diagnostic);
    }

    private void UpdateContext(ProofingItem item)
    {
        var inlines = EnsureContextInlines();
        inlines.Clear();
        var text = item.ParagraphText ?? string.Empty;
        var diagnostic = item.Diagnostic;
        if (diagnostic.StartOffset < 0 || diagnostic.StartOffset >= text.Length || diagnostic.Length <= 0)
        {
            _contextText.Text = text;
            return;
        }

        var start = Math.Clamp(diagnostic.StartOffset, 0, text.Length);
        var length = Math.Clamp(diagnostic.Length, 0, text.Length - start);
        var prefix = text.Substring(0, start);
        var match = text.Substring(start, length);
        var suffix = text.Substring(start + length);

        if (prefix.Length > 0)
        {
            inlines.Add(new Run(prefix));
        }

        if (match.Length > 0)
        {
            inlines.Add(new Run(match)
            {
                Foreground = diagnostic.Kind == ProofingIssueKind.Spelling
                    ? new SolidColorBrush(Color.Parse("#C00"))
                    : new SolidColorBrush(Color.Parse("#0066CC")),
                TextDecorations = TextDecorations.Underline
            });
        }

        if (suffix.Length > 0)
        {
            inlines.Add(new Run(suffix));
        }
    }

    private void UpdateSuggestions(ProofingDiagnostic diagnostic)
    {
        if (_proofingService is null)
        {
            _suggestionsList.ItemsSource = Array.Empty<string>();
            return;
        }

        var suggestions = _proofingService.GetSuggestions(diagnostic, 8);
        if (suggestions.Count == 0)
        {
            _suggestionsList.ItemsSource = new[] { "(No suggestions)" };
            _suggestionsList.SelectedIndex = -1;
            _changeButton.IsEnabled = false;
            _changeAllButton.IsEnabled = false;
            _autoCorrectButton.IsEnabled = false;
            return;
        }

        _suggestionsList.ItemsSource = suggestions;
        _suggestionsList.SelectedIndex = 0;
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        _changeButton.IsEnabled = enabled;
        _changeAllButton.IsEnabled = enabled;
        _autoCorrectButton.IsEnabled = enabled;
        _ignoreButton.IsEnabled = enabled;
        _ignoreAllButton.IsEnabled = enabled;
        _addButton.IsEnabled = enabled;
        _previousButton.IsEnabled = enabled && _items.Count > 1;
        _nextButton.IsEnabled = enabled && _items.Count > 1;
    }

    private void OnSuggestionSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        var hasSuggestion = GetSelectedSuggestion() is not null;
        _changeButton.IsEnabled = _items.Count > 0 && hasSuggestion;
        _changeAllButton.IsEnabled = _items.Count > 0 && hasSuggestion;
        _autoCorrectButton.IsEnabled = _items.Count > 0 && hasSuggestion;
    }

    private async void OnChangeClicked(object? sender, RoutedEventArgs e)
    {
        var suggestion = GetSelectedSuggestion();
        if (suggestion is null)
        {
            return;
        }

        await ApplySuggestionAsync(suggestion, advance: true, changeAll: false);
    }

    private async void OnChangeAllClicked(object? sender, RoutedEventArgs e)
    {
        var suggestion = GetSelectedSuggestion();
        if (suggestion is null)
        {
            return;
        }

        await ApplySuggestionAsync(suggestion, advance: true, changeAll: true);
    }

    private void OnAutoCorrectClicked(object? sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _autoCorrectService is null)
        {
            return;
        }

        var suggestion = GetSelectedSuggestion();
        if (suggestion is null)
        {
            return;
        }

        var diagnostic = _items[_currentIndex].Diagnostic;
        if (_autoCorrectService is AutoCorrectService service)
        {
            service.AddRule(new AutoCorrectRule(diagnostic.Text, suggestion));
        }
    }

    private void OnIgnoreClicked(object? sender, RoutedEventArgs e)
    {
        MoveNext();
    }

    private void OnIgnoreAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_proofingService is null || _items.Count == 0)
        {
            return;
        }

        var diagnostic = _items[_currentIndex].Diagnostic;
        _proofingService.IgnoreWord(diagnostic.Text, diagnostic.Language);
        RefreshDiagnostics();
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        if (_proofingService is null || _items.Count == 0)
        {
            return;
        }

        var diagnostic = _items[_currentIndex].Diagnostic;
        _proofingService.AddToUserDictionary(diagnostic.Text, diagnostic.Language);
        RefreshDiagnostics();
    }

    private void OnPreviousClicked(object? sender, RoutedEventArgs e)
    {
        MovePrevious();
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        MoveNext();
    }

    private void MovePrevious()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex - 1 + _items.Count) % _items.Count;
        UpdateUiState();
    }

    private void MoveNext()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _items.Count;
        UpdateUiState();
    }

    private string? GetSelectedSuggestion()
    {
        if (_suggestionsList.SelectedItem is string suggestion && !string.Equals(suggestion, "(No suggestions)", StringComparison.Ordinal))
        {
            return suggestion;
        }

        return null;
    }

    private async Task ApplySuggestionAsync(string suggestion, bool advance, bool changeAll)
    {
        if (_items.Count == 0 || _view is null)
        {
            return;
        }

        var diagnostic = _items[_currentIndex].Diagnostic;
        if (changeAll && _findReplaceService is { IsAvailable: true })
        {
            var query = new EditorReplaceQuery(diagnostic.Text, suggestion, MatchCase: false, WholeWord: true, Wrap: true);
            _findReplaceService.ReplaceAll(query);
            _proofingService?.RefreshAll();
            RefreshDiagnostics();
            return;
        }

        if (!_view.TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        SelectDiagnosticRange(diagnostic);
        await router.ExecuteAsync(EditorReviewCommandIds.Proofing.ApplySuggestion, suggestion);
        _proofingService?.RefreshParagraph(diagnostic.ParagraphIndex);

        if (advance)
        {
            RefreshDiagnostics();
            MoveNext();
        }
    }

    private void SelectDiagnosticRange(ProofingDiagnostic diagnostic)
    {
        if (_view is null)
        {
            return;
        }

        var start = new TextPosition(diagnostic.ParagraphIndex, diagnostic.StartOffset);
        var end = new TextPosition(diagnostic.ParagraphIndex, diagnostic.StartOffset + diagnostic.Length);
        _suppressSelection = true;
        _view.SelectRange(new TextRange(start, end));
        _suppressSelection = false;
    }

    private InlineCollection EnsureContextInlines()
    {
        if (_contextText.Inlines is null)
        {
            _contextText.Inlines = new InlineCollection();
        }

        return _contextText.Inlines;
    }

    private sealed record ProofingItem(ProofingDiagnostic Diagnostic, string ParagraphText);
}
