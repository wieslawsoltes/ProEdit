using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public partial class ThesaurusWindow : Window
{
    private readonly TextBox _lookupBox;
    private readonly Button _lookupButton;
    private readonly ListBox _synonymsList;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly TextBlock _statusText;

    private DocumentView? _view;
    private ISelectionTextService? _selectionTextService;
    private IFindReplaceService? _findReplaceService;

    public ThesaurusWindow()
    {
        InitializeComponent();

        _lookupBox = this.FindControl<TextBox>("LookupBox")!;
        _lookupButton = this.FindControl<Button>("LookupButton")!;
        _synonymsList = this.FindControl<ListBox>("SynonymsList")!;
        _replaceButton = this.FindControl<Button>("ReplaceButton")!;
        _replaceAllButton = this.FindControl<Button>("ReplaceAllButton")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;

        _lookupButton.Click += OnLookupClicked;
        _replaceButton.Click += OnReplaceClicked;
        _replaceAllButton.Click += OnReplaceAllClicked;
        _synonymsList.SelectionChanged += (_, _) => UpdateButtons();

        UpdateButtons();
    }

    public ThesaurusWindow(DocumentView view)
        : this()
    {
        SetDocumentView(view);
    }

    public void SetDocumentView(DocumentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _selectionTextService = null;
        _findReplaceService = null;
        if (_view.TryGetService<ISelectionTextService>(out var selectionTextService))
        {
            _selectionTextService = selectionTextService;
        }

        if (_view.TryGetService<IFindReplaceService>(out var findReplaceService))
        {
            _findReplaceService = findReplaceService;
        }

        if (_selectionTextService is not null && _selectionTextService.TryGetSelectionText(out var text, 64))
        {
            _lookupBox.Text = text;
        }
    }

    private void OnLookupClicked(object? sender, RoutedEventArgs e)
    {
        var word = _lookupBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        _synonymsList.ItemsSource = Array.Empty<string>();
        _statusText.Text = "No thesaurus provider configured.";
        UpdateButtons();
    }

    private void OnReplaceClicked(object? sender, RoutedEventArgs e)
    {
        if (_synonymsList.SelectedItem is not string synonym || _findReplaceService is null || _view is null)
        {
            return;
        }

        var query = new EditorReplaceQuery(_lookupBox.Text ?? string.Empty, synonym, MatchCase: false, WholeWord: true, Wrap: true);
        _findReplaceService.TryReplaceNext(query, out _);
    }

    private void OnReplaceAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_synonymsList.SelectedItem is not string synonym || _findReplaceService is null)
        {
            return;
        }

        var query = new EditorReplaceQuery(_lookupBox.Text ?? string.Empty, synonym, MatchCase: false, WholeWord: true, Wrap: true);
        _findReplaceService.ReplaceAll(query);
    }

    private void UpdateButtons()
    {
        var hasSuggestion = _synonymsList.SelectedItem is string;
        _replaceButton.IsEnabled = hasSuggestion;
        _replaceAllButton.IsEnabled = hasSuggestion;
    }
}
