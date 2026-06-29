using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using Avalonia;
using Avalonia.Media.Imaging;
using ReactiveUI;
using ProEdit.Printing;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Export;

namespace ProEdit.Reporting.Avalonia.Viewer;

/// <summary>
/// Represents one selectable available-value entry.
/// </summary>
public sealed class ReportViewerAvailableValueViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerAvailableValueViewModel" /> class.
    /// </summary>
    /// <param name="value">The underlying value.</param>
    /// <param name="label">The display label.</param>
    public ReportViewerAvailableValueViewModel(object? value, string label)
    {
        Value = value;
        Label = label ?? string.Empty;
    }

    /// <summary>
    /// Gets the underlying value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; }
}

/// <summary>
/// Represents one editable report parameter in the viewer.
/// </summary>
public sealed class ReportViewerParameterViewModel : ReactiveObject
{
    private readonly ObservableCollection<ReportViewerAvailableValueViewModel> _availableValues = new();
    private bool _booleanValue;
    private bool _isNull;
    private ReportViewerAvailableValueViewModel? _selectedAvailableValue;
    private string _textValue = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerParameterViewModel" /> class.
    /// </summary>
    /// <param name="state">The resolved parameter state.</param>
    public ReportViewerParameterViewModel(ReportViewerParameterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Definition = state.Definition;
        AvailableValues = new ReadOnlyObservableCollection<ReportViewerAvailableValueViewModel>(_availableValues);
        UpdateAvailableValues(state.AvailableValues);
        ApplyResolvedValue(state.ResolvedValue);
    }

    /// <summary>
    /// Gets the parameter definition.
    /// </summary>
    public ReportParameterDefinition Definition { get; }

    /// <summary>
    /// Gets the parameter identifier.
    /// </summary>
    public string Id => Definition.Id;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Definition.DisplayName) ? Definition.Id : Definition.DisplayName;

    /// <summary>
    /// Gets the prompt text.
    /// </summary>
    public string Prompt => string.IsNullOrWhiteSpace(Definition.Prompt) ? DisplayName : Definition.Prompt!;

    /// <summary>
    /// Gets a value indicating whether the parameter is visible in the viewer pane.
    /// </summary>
    public bool IsPromptVisible => Definition.Visibility == ReportParameterVisibility.Visible;

    /// <summary>
    /// Gets a value indicating whether the parameter is multi-value.
    /// </summary>
    public bool IsMultiValue => Definition.IsMultiValue;

    /// <summary>
    /// Gets a value indicating whether the parameter allows null.
    /// </summary>
    public bool AllowNull => Definition.AllowNull;

    /// <summary>
    /// Gets a value indicating whether the parameter uses a single-select list.
    /// </summary>
    public bool UsesAvailableValueSelection => _availableValues.Count > 0 && !Definition.IsMultiValue;

    /// <summary>
    /// Gets a value indicating whether the parameter uses a boolean editor.
    /// </summary>
    public bool UsesBooleanEditor => Definition.DataType == ReportParameterDataType.Boolean && !UsesAvailableValueSelection;

    /// <summary>
    /// Gets a value indicating whether the parameter uses free-form text entry.
    /// </summary>
    public bool UsesTextEditor => !UsesAvailableValueSelection && !UsesBooleanEditor;

    /// <summary>
    /// Gets helper text for the current editor mode.
    /// </summary>
    public string HelperText => Definition.IsMultiValue
        ? "Separate values with ';' or new lines."
        : (UsesAvailableValueSelection ? "Select one value." : string.Empty);

    /// <summary>
    /// Gets the resolved available values.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerAvailableValueViewModel> AvailableValues { get; }

    /// <summary>
    /// Gets or sets the selected available value.
    /// </summary>
    public ReportViewerAvailableValueViewModel? SelectedAvailableValue
    {
        get => _selectedAvailableValue;
        set => this.RaiseAndSetIfChanged(ref _selectedAvailableValue, value);
    }

    /// <summary>
    /// Gets or sets the free-form text value.
    /// </summary>
    public string TextValue
    {
        get => _textValue;
        set => this.RaiseAndSetIfChanged(ref _textValue, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the boolean value.
    /// </summary>
    public bool BooleanValue
    {
        get => _booleanValue;
        set => this.RaiseAndSetIfChanged(ref _booleanValue, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the parameter should resolve to null.
    /// </summary>
    public bool IsNull
    {
        get => _isNull;
        set => this.RaiseAndSetIfChanged(ref _isNull, value);
    }

    /// <summary>
    /// Updates the available values.
    /// </summary>
    /// <param name="availableValues">The new values.</param>
    public void UpdateAvailableValues(IEnumerable<ReportParameterAvailableValue> availableValues)
    {
        ArgumentNullException.ThrowIfNull(availableValues);

        object? selectedValue = SelectedAvailableValue?.Value;
        _availableValues.Clear();
        foreach (var availableValue in availableValues)
        {
            _availableValues.Add(new ReportViewerAvailableValueViewModel(availableValue.Value, availableValue.Label));
        }

        this.RaisePropertyChanged(nameof(UsesAvailableValueSelection));
        this.RaisePropertyChanged(nameof(UsesBooleanEditor));
        this.RaisePropertyChanged(nameof(UsesTextEditor));
        this.RaisePropertyChanged(nameof(HelperText));

        if (selectedValue is not null)
        {
            SelectedAvailableValue = _availableValues.FirstOrDefault(candidate => ValuesEqual(candidate.Value, selectedValue));
        }
    }

    /// <summary>
    /// Applies a resolved value to the editor state.
    /// </summary>
    /// <param name="value">The resolved value.</param>
    public void ApplyResolvedValue(ReportParameterValue? value)
    {
        if (value is null || value.IsNull || value.Values.Count == 0)
        {
            IsNull = true;
            SelectedAvailableValue = null;
            TextValue = string.Empty;
            BooleanValue = false;
            return;
        }

        IsNull = false;
        if (UsesAvailableValueSelection)
        {
            var scalar = value.GetScalarValue();
            SelectedAvailableValue = _availableValues.FirstOrDefault(candidate => ValuesEqual(candidate.Value, scalar));
        }

        if (UsesBooleanEditor)
        {
            BooleanValue = value.GetScalarValue() switch
            {
                bool flag => flag,
                string text when bool.TryParse(text, out var parsed) => parsed,
                _ => false
            };
        }

        TextValue = Definition.IsMultiValue
            ? string.Join("; ", value.Values.Select(static item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty))
            : Convert.ToString(value.GetScalarValue(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Creates the currently supplied parameter value.
    /// </summary>
    /// <returns>The supplied value.</returns>
    public ReportParameterValue CreateSuppliedValue()
    {
        if (IsNull)
        {
            return new ReportParameterValue
            {
                IsNull = true
            };
        }

        if (UsesAvailableValueSelection && SelectedAvailableValue is not null)
        {
            return CreateValue(SelectedAvailableValue.Value);
        }

        if (UsesBooleanEditor)
        {
            return CreateValue(BooleanValue);
        }

        if (Definition.IsMultiValue)
        {
            var value = new ReportParameterValue();
            foreach (var item in SplitValues(TextValue))
            {
                value.Values.Add(item);
            }

            value.IsNull = value.Values.Count == 0;
            return value;
        }

        return CreateValue(string.IsNullOrWhiteSpace(TextValue) ? null : TextValue.Trim());
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static ReportParameterValue CreateValue(object? value)
    {
        return ReportParameterValue.FromScalar(value);
    }

    private static IReadOnlyList<string> SplitValues(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}

/// <summary>
/// Represents one rendered preview page.
/// </summary>
public sealed class ReportViewerPageViewModel : ReactiveObject, IDisposable
{
    private Bitmap? _bitmap;
    private double _displayHeight;
    private double _displayWidth;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerPageViewModel" /> class.
    /// </summary>
    /// <param name="page">The preview page.</param>
    public ReportViewerPageViewModel(PrintPreviewPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        using var stream = new MemoryStream(page.ImageBytes, writable: false);
        _bitmap = new Bitmap(stream);
        DisplayWidth = page.Width;
        DisplayHeight = page.Height;
    }

    /// <summary>
    /// Gets the preview page payload.
    /// </summary>
    public PrintPreviewPage Page { get; }

    /// <summary>
    /// Gets the 0-based page index.
    /// </summary>
    public int PageIndex => Math.Max(0, Page.PageNumber - 1);

    /// <summary>
    /// Gets the 1-based page number.
    /// </summary>
    public int PageNumber => Page.PageNumber;

    /// <summary>
    /// Gets the page bitmap.
    /// </summary>
    public Bitmap? Bitmap
    {
        get => _bitmap;
        private set => this.RaiseAndSetIfChanged(ref _bitmap, value);
    }

    /// <summary>
    /// Gets or sets the display width.
    /// </summary>
    public double DisplayWidth
    {
        get => _displayWidth;
        private set => this.RaiseAndSetIfChanged(ref _displayWidth, value);
    }

    /// <summary>
    /// Gets or sets the display height.
    /// </summary>
    public double DisplayHeight
    {
        get => _displayHeight;
        private set => this.RaiseAndSetIfChanged(ref _displayHeight, value);
    }

    /// <summary>
    /// Gets the thumbnail width.
    /// </summary>
    public double ThumbnailWidth => 96;

    /// <summary>
    /// Gets the thumbnail height.
    /// </summary>
    public double ThumbnailHeight => 132;

    /// <summary>
    /// Applies a new zoom factor.
    /// </summary>
    /// <param name="zoomFactor">The zoom factor.</param>
    public void ApplyZoom(float zoomFactor)
    {
        var clamped = Math.Clamp(zoomFactor, 0.25f, 4f);
        DisplayWidth = Page.Width * clamped;
        DisplayHeight = Page.Height * clamped;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Bitmap?.Dispose();
        Bitmap = null;
    }
}

/// <summary>
/// Represents one document map entry shown by the viewer.
/// </summary>
public sealed class ReportViewerDocumentMapEntryViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerDocumentMapEntryViewModel" /> class.
    /// </summary>
    /// <param name="entry">The entry metadata.</param>
    /// <param name="navigate">The navigation callback.</param>
    public ReportViewerDocumentMapEntryViewModel(ReportViewerDocumentMapEntry entry, Action<ReportViewerDocumentMapEntry> navigate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(navigate);
        Entry = entry;
        NavigateCommand = ReactiveCommand.Create(
            () => navigate(entry),
            outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    /// <summary>
    /// Gets the underlying entry metadata.
    /// </summary>
    public ReportViewerDocumentMapEntry Entry { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label => Entry.Label;

    /// <summary>
    /// Gets the indentation level.
    /// </summary>
    public int Level => Entry.Level;

    /// <summary>
    /// Gets the indentation width in pixels.
    /// </summary>
    public double IndentWidth => Level * 12d;

    /// <summary>
    /// Gets the indentation margin.
    /// </summary>
    public Thickness IndentMargin => new Thickness(IndentWidth, 0, 0, 0);

    /// <summary>
    /// Gets the 1-based page number text.
    /// </summary>
    public string PageText => $"Page {Entry.PageIndex + 1}";

    /// <summary>
    /// Gets the navigation command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
}

/// <summary>
/// Represents one search result shown by the viewer.
/// </summary>
public sealed class ReportViewerSearchResultViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerSearchResultViewModel" /> class.
    /// </summary>
    /// <param name="entry">The search entry.</param>
    /// <param name="navigate">The navigation callback.</param>
    public ReportViewerSearchResultViewModel(ReportViewerSearchEntry entry, Action<ReportViewerSearchEntry> navigate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(navigate);
        Entry = entry;
        NavigateCommand = ReactiveCommand.Create(
            () => navigate(entry),
            outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    /// <summary>
    /// Gets the search entry metadata.
    /// </summary>
    public ReportViewerSearchEntry Entry { get; }

    /// <summary>
    /// Gets the search snippet.
    /// </summary>
    public string Snippet => Entry.Text;

    /// <summary>
    /// Gets the 1-based page number text.
    /// </summary>
    public string PageText => $"Page {Entry.PageIndex + 1}";

    /// <summary>
    /// Gets the navigation command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
}

/// <summary>
/// Represents one diagnostic displayed by the viewer.
/// </summary>
public sealed class ReportViewerDiagnosticViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerDiagnosticViewModel" /> class.
    /// </summary>
    /// <param name="diagnostic">The diagnostic.</param>
    public ReportViewerDiagnosticViewModel(ReportDiagnostic diagnostic)
    {
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// Gets the underlying diagnostic.
    /// </summary>
    public ReportDiagnostic Diagnostic { get; }

    /// <summary>
    /// Gets the severity label.
    /// </summary>
    public string Severity => Diagnostic.Severity.ToString();

    /// <summary>
    /// Gets the diagnostic code.
    /// </summary>
    public string Code => Diagnostic.Code;

    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message => Diagnostic.Message;

    /// <summary>
    /// Gets the optional path text.
    /// </summary>
    public string Path => Diagnostic.Path ?? string.Empty;
}

/// <summary>
/// Represents one drillthrough action displayed by the viewer.
/// </summary>
public sealed class ReportViewerDrillthroughItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerDrillthroughItemViewModel" /> class.
    /// </summary>
    /// <param name="entry">The drillthrough entry.</param>
    /// <param name="navigate">The drillthrough callback.</param>
    public ReportViewerDrillthroughItemViewModel(ReportViewerDrillthroughEntry entry, Func<ReportViewerDrillthroughEntry, Task> navigate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(navigate);
        Entry = entry;
        NavigateCommand = ReactiveCommand.CreateFromTask(
            () => navigate(entry),
            outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    /// <summary>
    /// Gets the underlying drillthrough entry.
    /// </summary>
    public ReportViewerDrillthroughEntry Entry { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label => Entry.Label;

    /// <summary>
    /// Gets the tooltip or target report text.
    /// </summary>
    public string Description => string.IsNullOrWhiteSpace(Entry.Tooltip)
        ? Entry.Action.ReportReferenceId
        : Entry.Tooltip!;

    /// <summary>
    /// Gets the 1-based page number text.
    /// </summary>
    public string PageText => $"Page {Entry.PageIndex + 1}";

    /// <summary>
    /// Gets the navigation command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
}

/// <summary>
/// View model for the Avalonia report viewer surface.
/// </summary>
public sealed partial class ReportViewerViewModel : ReactiveObject, IDisposable
{
    private readonly ObservableCollection<ReportViewerDiagnosticViewModel> _diagnostics = new();
    private readonly ObservableCollection<ReportViewerDocumentMapEntryViewModel> _documentMapEntries = new();
    private readonly ObservableCollection<ReportViewerDrillthroughItemViewModel> _drillthroughItems = new();
    private readonly ObservableCollection<ReportViewerPageViewModel> _pages = new();
    private readonly ObservableCollection<ReportViewerParameterViewModel> _parameters = new();
    private readonly ObservableCollection<ReportViewerSearchResultViewModel> _searchResults = new();
    private readonly IReportViewerSessionService _sessionService;
    private readonly Stack<ViewerNavigationFrame> _navigationStack = new();
    private readonly Dictionary<string, ReportParameterValue> _nonPromptParameters = new(StringComparer.OrdinalIgnoreCase);
    private ReportViewerExecutionSnapshot? _currentSnapshot;
    private ReportViewerSource? _source;
    private ReportViewerState? _pendingState;
    private bool _canGoBack;
    private bool _canNavigateNext;
    private bool _canNavigatePrevious;
    private bool _canPrint;
    private bool _canRefresh;
    private bool _canRunDrillthrough;
    private bool _canSearch;
    private bool _canExport;
    private bool _isBusy;
    private string? _lastExportPath;
    private string? _lastPrintedOutputPath;
    private int _selectedPaneIndex;
    private ReportViewerDocumentMapEntryViewModel? _selectedDocumentMapEntry;
    private ReportViewerDrillthroughItemViewModel? _selectedDrillthroughItem;
    private ReportExportFormat _selectedExportFormat;
    private ReportViewerPageViewModel? _selectedPage;
    private ReportViewerSearchResultViewModel? _selectedSearchResult;
    private string _searchQuery = string.Empty;
    private string? _statusMessage;
    private float _zoomFactor = 1f;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerViewModel" /> class.
    /// </summary>
    /// <param name="sessionService">The viewer session service.</param>
    public ReportViewerViewModel(IReportViewerSessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        Parameters = new ReadOnlyObservableCollection<ReportViewerParameterViewModel>(_parameters);
        Pages = new ReadOnlyObservableCollection<ReportViewerPageViewModel>(_pages);
        DocumentMapEntries = new ReadOnlyObservableCollection<ReportViewerDocumentMapEntryViewModel>(_documentMapEntries);
        SearchResults = new ReadOnlyObservableCollection<ReportViewerSearchResultViewModel>(_searchResults);
        Diagnostics = new ReadOnlyObservableCollection<ReportViewerDiagnosticViewModel>(_diagnostics);
        DrillthroughItems = new ReadOnlyObservableCollection<ReportViewerDrillthroughItemViewModel>(_drillthroughItems);
        ExportFormats = new[]
        {
            ReportExportFormat.Pdf,
            ReportExportFormat.Docx,
            ReportExportFormat.Html,
            ReportExportFormat.Rtf,
            ReportExportFormat.Markdown,
            ReportExportFormat.Csv,
            ReportExportFormat.Xlsx,
            ReportExportFormat.Xps,
            ReportExportFormat.Ps
        };
        ZoomLevels = new[] { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        SelectedExportFormat = ReportExportFormat.Pdf;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        ApplyParametersCommand = ReactiveCommand.CreateFromTask(ApplyParametersAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        ResetParametersCommand = ReactiveCommand.CreateFromTask(ResetParametersAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        PreviousPageCommand = ReactiveCommand.Create(
            () => NavigateToPage(SelectedPage is null ? 0 : SelectedPage.PageIndex - 1),
            outputScheduler: RxSchedulers.MainThreadScheduler);
        NextPageCommand = ReactiveCommand.Create(
            () => NavigateToPage(SelectedPage is null ? 0 : SelectedPage.PageIndex + 1),
            outputScheduler: RxSchedulers.MainThreadScheduler);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        PrintCommand = ReactiveCommand.CreateFromTask(PrintAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        GoBackCommand = ReactiveCommand.CreateFromTask(GoBackAsync, outputScheduler: RxSchedulers.MainThreadScheduler);
        InitializeLayoutCommands();

        UpdateCommandState();
    }

    /// <summary>
    /// Gets the available parameter editors.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerParameterViewModel> Parameters { get; }

    /// <summary>
    /// Gets the rendered preview pages.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerPageViewModel> Pages { get; }

    /// <summary>
    /// Gets the document map entries.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerDocumentMapEntryViewModel> DocumentMapEntries { get; }

    /// <summary>
    /// Gets the search results.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerSearchResultViewModel> SearchResults { get; }

    /// <summary>
    /// Gets the diagnostics.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerDiagnosticViewModel> Diagnostics { get; }

    /// <summary>
    /// Gets the drillthrough items.
    /// </summary>
    public ReadOnlyObservableCollection<ReportViewerDrillthroughItemViewModel> DrillthroughItems { get; }

    /// <summary>
    /// Gets the supported export formats.
    /// </summary>
    public IReadOnlyList<ReportExportFormat> ExportFormats { get; }

    /// <summary>
    /// Gets the supported zoom levels.
    /// </summary>
    public IReadOnlyList<float> ZoomLevels { get; }

    /// <summary>
    /// Gets the current snapshot.
    /// </summary>
    public ReportViewerExecutionSnapshot? CurrentSnapshot
    {
        get => _currentSnapshot;
        private set => this.RaiseAndSetIfChanged(ref _currentSnapshot, value);
    }

    /// <summary>
    /// Gets the current source.
    /// </summary>
    public ReportViewerSource? Source
    {
        get => _source;
        private set => this.RaiseAndSetIfChanged(ref _source, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether one asynchronous viewer operation is running.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>
    /// Gets or sets the selected page.
    /// </summary>
    public ReportViewerPageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (ReferenceEquals(_selectedPage, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedPage, value);
            this.RaisePropertyChanged(nameof(PageDisplayText));
            UpdateCommandState();
        }
    }

    /// <summary>
    /// Gets the current page display text.
    /// </summary>
    public string PageDisplayText => SelectedPage is null
        ? "Page 0 of 0"
        : $"Page {SelectedPage.PageNumber} of {Pages.Count}";

    /// <summary>
    /// Gets or sets the selected export format.
    /// </summary>
    public ReportExportFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => this.RaiseAndSetIfChanged(ref _selectedExportFormat, value);
    }

    /// <summary>
    /// Gets or sets the selected pane index.
    /// </summary>
    public int SelectedPaneIndex
    {
        get => _selectedPaneIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 4);
            if (_selectedPaneIndex == clamped)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedPaneIndex, clamped);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public float ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            var clamped = Math.Clamp(value, 0.25f, 4f);
            if (Math.Abs(_zoomFactor - clamped) < 0.0001f)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _zoomFactor, clamped);
            for (var index = 0; index < _pages.Count; index++)
            {
                _pages[index].ApplyZoom(clamped);
            }
        }
    }

    /// <summary>
    /// Gets or sets the current search query.
    /// </summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_searchQuery, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _searchQuery, normalized);
            RebuildSearchResults();
        }
    }

    /// <summary>
    /// Gets or sets the selected search result.
    /// </summary>
    public ReportViewerSearchResultViewModel? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set => this.RaiseAndSetIfChanged(ref _selectedSearchResult, value);
    }

    /// <summary>
    /// Gets or sets the selected document map entry.
    /// </summary>
    public ReportViewerDocumentMapEntryViewModel? SelectedDocumentMapEntry
    {
        get => _selectedDocumentMapEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedDocumentMapEntry, value);
    }

    /// <summary>
    /// Gets or sets the selected drillthrough item.
    /// </summary>
    public ReportViewerDrillthroughItemViewModel? SelectedDrillthroughItem
    {
        get => _selectedDrillthroughItem;
        set => this.RaiseAndSetIfChanged(ref _selectedDrillthroughItem, value);
    }

    /// <summary>
    /// Gets the last export path.
    /// </summary>
    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => this.RaiseAndSetIfChanged(ref _lastExportPath, value);
    }

    /// <summary>
    /// Gets the last printed output path when the fallback PDF pipeline is used.
    /// </summary>
    public string? LastPrintedOutputPath
    {
        get => _lastPrintedOutputPath;
        private set => this.RaiseAndSetIfChanged(ref _lastPrintedOutputPath, value);
    }

    /// <summary>
    /// Gets a value indicating whether refresh can execute.
    /// </summary>
    public bool CanRefresh
    {
        get => _canRefresh;
        private set => this.RaiseAndSetIfChanged(ref _canRefresh, value);
    }

    /// <summary>
    /// Gets a value indicating whether export can execute.
    /// </summary>
    public bool CanExport
    {
        get => _canExport;
        private set => this.RaiseAndSetIfChanged(ref _canExport, value);
    }

    /// <summary>
    /// Gets a value indicating whether print can execute.
    /// </summary>
    public bool CanPrint
    {
        get => _canPrint;
        private set => this.RaiseAndSetIfChanged(ref _canPrint, value);
    }

    /// <summary>
    /// Gets a value indicating whether backward navigation can execute.
    /// </summary>
    public bool CanGoBack
    {
        get => _canGoBack;
        private set => this.RaiseAndSetIfChanged(ref _canGoBack, value);
    }

    /// <summary>
    /// Gets a value indicating whether previous-page navigation can execute.
    /// </summary>
    public bool CanNavigatePrevious
    {
        get => _canNavigatePrevious;
        private set => this.RaiseAndSetIfChanged(ref _canNavigatePrevious, value);
    }

    /// <summary>
    /// Gets a value indicating whether next-page navigation can execute.
    /// </summary>
    public bool CanNavigateNext
    {
        get => _canNavigateNext;
        private set => this.RaiseAndSetIfChanged(ref _canNavigateNext, value);
    }

    /// <summary>
    /// Gets a value indicating whether the viewer has drillthrough actions.
    /// </summary>
    public bool CanRunDrillthrough
    {
        get => _canRunDrillthrough;
        private set => this.RaiseAndSetIfChanged(ref _canRunDrillthrough, value);
    }

    /// <summary>
    /// Gets a value indicating whether search results are available.
    /// </summary>
    public bool CanSearch
    {
        get => _canSearch;
        private set => this.RaiseAndSetIfChanged(ref _canSearch, value);
    }

    /// <summary>
    /// Gets the refresh command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>
    /// Gets the apply-parameters command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ApplyParametersCommand { get; }

    /// <summary>
    /// Gets the reset-parameters command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetParametersCommand { get; }

    /// <summary>
    /// Gets the previous-page command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

    /// <summary>
    /// Gets the next-page command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }

    /// <summary>
    /// Gets the export command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    /// <summary>
    /// Gets the print command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> PrintCommand { get; }

    /// <summary>
    /// Gets the back-navigation command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> GoBackCommand { get; }

    /// <summary>
    /// Loads a new viewer source.
    /// </summary>
    /// <param name="source">The viewer source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the source is loaded.</returns>
    public ValueTask LoadAsync(ReportViewerSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ResetLayoutInitialization();
        return new ValueTask(LoadCoreAsync(
            source,
            new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase),
            resetBackStack: true,
            null,
            cancellationToken));
    }

    /// <summary>
    /// Captures the current persisted state.
    /// </summary>
    /// <returns>The persisted state.</returns>
    public ReportViewerState CaptureState()
    {
        return new ReportViewerState
        {
            ActivePane = (ReportViewerPane)Math.Clamp(SelectedPaneIndex, 0, 4),
            SelectedPageIndex = SelectedPage?.PageIndex ?? 0,
            ZoomFactor = ZoomFactor,
            SearchQuery = SearchQuery,
            SelectedBookmark = SelectedDocumentMapEntry?.Entry.Bookmark,
            LeftDrawerState = LeftDrawerState,
            IsThumbnailTrayOpen = IsThumbnailTrayOpen
        };
    }

    /// <summary>
    /// Applies one persisted viewer state.
    /// </summary>
    /// <param name="state">The persisted state.</param>
    public void ApplyState(ReportViewerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (CurrentSnapshot is null)
        {
            _pendingState = state;
            return;
        }

        ApplyStateCore(state);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ClearPages();
    }

    private async Task<bool> LoadCoreAsync(
        ReportViewerSource source,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        bool resetBackStack,
        ReportViewerState? stateToRestore,
        CancellationToken cancellationToken)
    {
        StatusMessage = "Resolving parameters...";
        IsBusy = true;
        UpdateCommandState();

        try
        {
            var requestedState = stateToRestore ?? _pendingState;
            var restoredState = requestedState ?? CaptureState();
            var effectiveParameters = CloneParameters(suppliedParameters);
            var parameterResolution = await _sessionService.ResolveParametersAsync(source, effectiveParameters, cancellationToken);
            if (TrySeedDefaultAvailableValues(parameterResolution, effectiveParameters))
            {
                parameterResolution = await _sessionService.ResolveParametersAsync(source, effectiveParameters, cancellationToken);
            }

            StatusMessage = "Rendering preview...";
            var snapshot = await _sessionService.ExecuteAsync(source, effectiveParameters, cancellationToken);
            if (resetBackStack)
            {
                _navigationStack.Clear();
            }

            Source = source;
            RebuildParameters(parameterResolution);
            ApplyDefaultLayoutState(parameterResolution, requestedState);
            if (requestedState is null)
            {
                restoredState = CaptureState();
            }

            ApplySnapshot(snapshot);
            ApplyStateCore(restoredState);
            _pendingState = null;
            StatusMessage = snapshot.HasErrors
                ? "Report rendered with diagnostics."
                : $"Report rendered. {Pages.Count} page(s).";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Report operation was canceled.";
            return false;
        }
        catch (Exception ex)
        {
            AppendDiagnostics(new[]
            {
                CreateViewerFailureDiagnostic(ex, "$viewer.load")
            });
            StatusMessage = CreateFailureMessage(ex, "Report load failed.");
            return false;
        }
        finally
        {
            IsBusy = false;
            UpdateCommandState();
        }
    }

    /// <summary>
    /// Re-executes the current report source.
    /// </summary>
    /// <returns>A task that completes when refresh finishes.</returns>
    public async Task RefreshAsync()
    {
        if (!CanRefresh || Source is null)
        {
            return;
        }

        _ = await LoadCoreAsync(Source, BuildSuppliedParameters(), resetBackStack: false, CaptureState(), CancellationToken.None);
    }

    /// <summary>
    /// Applies the current parameter values and refreshes the preview.
    /// </summary>
    /// <returns>A task that completes when refresh finishes.</returns>
    public async Task ApplyParametersAsync()
    {
        if (!CanRefresh || Source is null)
        {
            return;
        }

        SelectedPaneIndex = (int)ReportViewerPane.Parameters;
        _ = await LoadCoreAsync(Source, BuildSuppliedParameters(), resetBackStack: false, CaptureState(), CancellationToken.None);
    }

    /// <summary>
    /// Resets promptable parameters to their default resolved values.
    /// </summary>
    /// <returns>A task that completes when reset finishes.</returns>
    public async Task ResetParametersAsync()
    {
        if (!CanRefresh || Source is null)
        {
            return;
        }

        _ = await LoadCoreAsync(
            Source,
            new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase),
            resetBackStack: false,
            CaptureState(),
            CancellationToken.None);
    }

    /// <summary>
    /// Exports the current snapshot to a temporary output file.
    /// </summary>
    /// <returns>A task that completes when export finishes.</returns>
    public async Task ExportAsync()
    {
        if (!CanExport || CurrentSnapshot is null || Source is null)
        {
            return;
        }

        IsBusy = true;
        UpdateCommandState();
        LastExportPath = null;
        string? filePath = null;
        try
        {
            filePath = CreateOutputPath(Source.ReportDefinition.Name, SelectedExportFormat);
            var request = new ReportExportRequest
            {
                Format = SelectedExportFormat
            };

            switch (SelectedExportFormat)
            {
                case ReportExportFormat.Csv:
                    request.Profile = new CsvReportExportProfile();
                    break;
                case ReportExportFormat.Xlsx:
                    request.Profile = new XlsxReportExportProfile();
                    break;
            }

            ReportExportResult result;
            await using (var stream = File.Create(filePath))
            {
                result = await _sessionService.ExportAsync(CurrentSnapshot, request, stream, CancellationToken.None);
            }

            AppendDiagnostics(result.Diagnostics);
            if (result.HasErrors)
            {
                TryDeleteFile(filePath);
                StatusMessage = "Export failed.";
            }
            else
            {
                LastExportPath = filePath;
                StatusMessage = $"Exported {SelectedExportFormat} to {filePath}.";
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(filePath);
            AppendDiagnostics(new[]
            {
                CreateViewerFailureDiagnostic(ex, "$viewer.export")
            });
            StatusMessage = CreateFailureMessage(ex, "Export failed.");
        }
        finally
        {
            IsBusy = false;
            UpdateCommandState();
        }
    }

    /// <summary>
    /// Produces print output for the current snapshot.
    /// </summary>
    /// <returns>A task that completes when printing finishes.</returns>
    public async Task PrintAsync()
    {
        if (!CanPrint || CurrentSnapshot is null || Source is null)
        {
            return;
        }

        IsBusy = true;
        UpdateCommandState();
        LastPrintedOutputPath = null;
        string? suggestedOutputPath = null;
        try
        {
            var settings = Source.DefaultPrintSettings?.Clone() ?? new PrintSettings
            {
                OutputKind = PrintOutputKind.Pdf,
                OutputPath = CreateOutputPath(Source.ReportDefinition.Name + "-print", ReportExportFormat.Pdf)
            };

            if (settings.OutputKind == PrintOutputKind.Pdf && string.IsNullOrWhiteSpace(settings.OutputPath))
            {
                settings.OutputPath = CreateOutputPath(Source.ReportDefinition.Name + "-print", ReportExportFormat.Pdf);
            }

            suggestedOutputPath = settings.OutputPath;
            var result = await _sessionService.PrintAsync(CurrentSnapshot, settings, CancellationToken.None);
            if (result.Succeeded)
            {
                LastPrintedOutputPath = result.OutputPath;
                StatusMessage = $"Print output created{(string.IsNullOrWhiteSpace(result.OutputPath) ? "." : $" at {result.OutputPath}.")}";
            }
            else
            {
                TryDeleteFile(result.OutputPath);
                if (!string.Equals(result.OutputPath, suggestedOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(suggestedOutputPath);
                }

                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "Print failed."
                    : result.Message;
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(suggestedOutputPath);
            AppendDiagnostics(new[]
            {
                CreateViewerFailureDiagnostic(ex, "$viewer.print")
            });
            StatusMessage = CreateFailureMessage(ex, "Print failed.");
        }
        finally
        {
            IsBusy = false;
            UpdateCommandState();
        }
    }

    /// <summary>
    /// Navigates back to the previous drillthrough frame.
    /// </summary>
    /// <returns>A task that completes when navigation finishes.</returns>
    public async Task GoBackAsync()
    {
        if (!CanGoBack || _navigationStack.Count == 0)
        {
            return;
        }

        var frame = _navigationStack.Peek();
        if (await LoadCoreAsync(frame.Source, frame.Parameters, resetBackStack: false, frame.State, CancellationToken.None))
        {
            _navigationStack.Pop();
        }

        UpdateCommandState();
    }

    private void RebuildParameters(ReportViewerParameterResolutionResult parameterResolution)
    {
        _nonPromptParameters.Clear();
        _parameters.Clear();
        for (var index = 0; index < parameterResolution.Parameters.Count; index++)
        {
            var state = parameterResolution.Parameters[index];
            var viewModel = new ReportViewerParameterViewModel(state);
            if (viewModel.IsPromptVisible)
            {
                _parameters.Add(viewModel);
            }
            else if (state.ResolvedValue is not null)
            {
                _nonPromptParameters[state.Definition.Id] = CloneParameterValue(state.ResolvedValue);
            }
        }

        ReplaceDiagnostics(parameterResolution.Diagnostics);
        this.RaisePropertyChanged(nameof(Parameters));
    }

    private void ApplySnapshot(ReportViewerExecutionSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        ClearPages();

        for (var index = 0; index < snapshot.PreviewPages.Count; index++)
        {
            var pageViewModel = new ReportViewerPageViewModel(snapshot.PreviewPages[index]);
            pageViewModel.ApplyZoom(ZoomFactor);
            _pages.Add(pageViewModel);
        }

        SelectedPage = _pages.Count > 0 ? _pages[0] : null;

        _documentMapEntries.Clear();
        for (var index = 0; index < snapshot.DocumentMapEntries.Count; index++)
        {
            _documentMapEntries.Add(new ReportViewerDocumentMapEntryViewModel(snapshot.DocumentMapEntries[index], NavigateToDocumentMapEntry));
        }

        _drillthroughItems.Clear();
        for (var index = 0; index < snapshot.DrillthroughEntries.Count; index++)
        {
            _drillthroughItems.Add(new ReportViewerDrillthroughItemViewModel(snapshot.DrillthroughEntries[index], NavigateToDrillthroughAsync));
        }

        ReplaceDiagnostics(snapshot.ExecutionResult.Diagnostics);
        RebuildSearchResults();
        UpdateCommandState();
    }

    private void ReplaceDiagnostics(IEnumerable<ReportDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Add(new ReportViewerDiagnosticViewModel(diagnostic));
        }
    }

    private void AppendDiagnostics(IEnumerable<ReportDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Add(new ReportViewerDiagnosticViewModel(diagnostic));
        }
    }

    private void RebuildSearchResults()
    {
        _searchResults.Clear();
        if (CurrentSnapshot is null)
        {
            UpdateCommandState();
            return;
        }

        var query = SearchQuery.Trim();
        IEnumerable<ReportViewerSearchEntry> matches = CurrentSnapshot.SearchEntries;
        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(entry => entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var entry in matches.Take(100))
        {
            _searchResults.Add(new ReportViewerSearchResultViewModel(entry, NavigateToSearchEntry));
        }

        UpdateCommandState();
    }

    private void NavigateToPage(int pageIndex)
    {
        if (_pages.Count == 0)
        {
            SelectedPage = null;
            return;
        }

        var clamped = Math.Clamp(pageIndex, 0, _pages.Count - 1);
        SelectedPage = _pages[clamped];
    }

    private void NavigateToDocumentMapEntry(ReportViewerDocumentMapEntry entry)
    {
        NavigateToPage(entry.PageIndex);
        SelectedDocumentMapEntry = _documentMapEntries.FirstOrDefault(candidate => ReferenceEquals(candidate.Entry, entry));
        SelectedPaneIndex = (int)ReportViewerPane.Outline;
    }

    private void NavigateToSearchEntry(ReportViewerSearchEntry entry)
    {
        NavigateToPage(entry.PageIndex);
        SelectedSearchResult = _searchResults.FirstOrDefault(candidate => ReferenceEquals(candidate.Entry, entry));
        SelectedPaneIndex = (int)ReportViewerPane.Search;
    }

    /// <summary>
    /// Navigates to one drillthrough target.
    /// </summary>
    /// <param name="entry">The drillthrough entry.</param>
    /// <returns>A task that completes when navigation finishes.</returns>
    public async Task NavigateToDrillthroughAsync(ReportViewerDrillthroughEntry entry)
    {
        if (Source is null)
        {
            return;
        }

        if (!Source.ReferencedReports.TryGetValue(entry.Action.ReportReferenceId, out var reportDefinition))
        {
            StatusMessage = $"Referenced report '{entry.Action.ReportReferenceId}' was not found.";
            return;
        }

        var frame = new ViewerNavigationFrame(Source, BuildSuppliedParameters(), CaptureState());

        var nextSource = new ReportViewerSource
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = Source.ProviderRegistry,
            HostDataRegistry = Source.HostDataRegistry,
            LayoutSettings = Source.LayoutSettings.Clone(),
            PreviewDpi = Source.PreviewDpi,
            DefaultPrintSettings = Source.DefaultPrintSettings?.Clone(),
            Culture = Source.Culture,
            UiCulture = Source.UiCulture,
            TimeZone = Source.TimeZone
        };
        CopyReports(Source.ReferencedReports, nextSource.ReferencedReports);
        CopyValues(Source.Globals, nextSource.Globals);

        ResetLayoutInitialization();
        if (await LoadCoreAsync(nextSource, entry.Action.Parameters, resetBackStack: false, null, CancellationToken.None))
        {
            _navigationStack.Push(frame);
        }

        UpdateCommandState();
    }

    private void ApplyStateCore(ReportViewerState state)
    {
        SelectedPaneIndex = (int)state.ActivePane;
        ZoomFactor = state.ZoomFactor <= 0f ? 1f : state.ZoomFactor;
        SearchQuery = state.SearchQuery ?? string.Empty;
        LeftDrawerState = state.LeftDrawerState;
        IsThumbnailTrayOpen = state.IsThumbnailTrayOpen;

        if (!string.IsNullOrWhiteSpace(state.SelectedBookmark))
        {
            SelectedDocumentMapEntry = _documentMapEntries.FirstOrDefault(candidate =>
                string.Equals(candidate.Entry.Bookmark, state.SelectedBookmark, StringComparison.OrdinalIgnoreCase));
            if (SelectedDocumentMapEntry is not null)
            {
                NavigateToDocumentMapEntry(SelectedDocumentMapEntry.Entry);
                return;
            }
        }

        NavigateToPage(state.SelectedPageIndex);
    }

    private Dictionary<string, ReportParameterValue> BuildSuppliedParameters()
    {
        var suppliedParameters = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _nonPromptParameters)
        {
            suppliedParameters[pair.Key] = CloneParameterValue(pair.Value);
        }

        for (var index = 0; index < _parameters.Count; index++)
        {
            suppliedParameters[_parameters[index].Id] = _parameters[index].CreateSuppliedValue();
        }

        return suppliedParameters;
    }

    private static Dictionary<string, ReportParameterValue> CloneParameters(
        IReadOnlyDictionary<string, ReportParameterValue> source)
    {
        var clone = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            clone[pair.Key] = CloneParameterValue(pair.Value);
        }

        return clone;
    }

    private static bool TrySeedDefaultAvailableValues(
        ReportViewerParameterResolutionResult resolution,
        IDictionary<string, ReportParameterValue> suppliedParameters)
    {
        var changed = false;
        for (var index = 0; index < resolution.Parameters.Count; index++)
        {
            var state = resolution.Parameters[index];
            if (suppliedParameters.ContainsKey(state.Definition.Id)
                || state.Definition.Visibility != ReportParameterVisibility.Visible
                || state.Definition.IsMultiValue
                || state.AvailableValues.Count == 0)
            {
                continue;
            }

            if (HasMatchingAvailableValue(state.ResolvedValue, state.AvailableValues))
            {
                continue;
            }

            suppliedParameters[state.Definition.Id] = ReportParameterValue.FromScalar(state.AvailableValues[0].Value);
            changed = true;
        }

        return changed;
    }

    private static bool HasMatchingAvailableValue(
        ReportParameterValue? resolvedValue,
        IReadOnlyList<ReportParameterAvailableValue> availableValues)
    {
        if (resolvedValue is null || resolvedValue.IsNull || resolvedValue.Values.Count == 0)
        {
            return false;
        }

        for (var valueIndex = 0; valueIndex < resolvedValue.Values.Count; valueIndex++)
        {
            var isMatched = false;
            for (var availableIndex = 0; availableIndex < availableValues.Count; availableIndex++)
            {
                var left = Convert.ToString(resolvedValue.Values[valueIndex], CultureInfo.InvariantCulture);
                var right = Convert.ToString(availableValues[availableIndex].Value, CultureInfo.InvariantCulture);
                if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                isMatched = true;
                break;
            }

            if (!isMatched)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateCommandState()
    {
        CanRefresh = !IsBusy && Source is not null;
        CanExport = !IsBusy && CurrentSnapshot?.ExecutionResult.Document is not null;
        CanPrint = !IsBusy && CurrentSnapshot?.ExecutionResult.Document is not null;
        CanGoBack = !IsBusy && _navigationStack.Count > 0;
        CanNavigatePrevious = !IsBusy && SelectedPage is not null && SelectedPage.PageIndex > 0;
        CanNavigateNext = !IsBusy && SelectedPage is not null && SelectedPage.PageIndex < _pages.Count - 1;
        CanRunDrillthrough = !IsBusy && _drillthroughItems.Count > 0;
        CanSearch = !IsBusy && _searchResults.Count > 0;
    }

    private void ClearPages()
    {
        for (var index = 0; index < _pages.Count; index++)
        {
            _pages[index].Dispose();
        }

        _pages.Clear();
        SelectedPage = null;
    }

    private static string CreateOutputPath(string reportName, ReportExportFormat format)
    {
        var safeName = SanitizePathSegment(string.IsNullOrWhiteSpace(reportName) ? "report" : reportName);
        var extension = format switch
        {
            ReportExportFormat.Pdf => ".pdf",
            ReportExportFormat.Docx => ".docx",
            ReportExportFormat.Html => ".html",
            ReportExportFormat.Rtf => ".rtf",
            ReportExportFormat.Markdown => ".md",
            ReportExportFormat.Xps => ".xps",
            ReportExportFormat.Ps => ".ps",
            ReportExportFormat.Csv => ".csv",
            ReportExportFormat.Xlsx => ".xlsx",
            _ => ".bin"
        };

        return Path.Combine(Path.GetTempPath(), $"{safeName}{extension}");
    }

    private static string SanitizePathSegment(string text)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            builder[index] = invalidCharacters.Contains(text[index]) ? '_' : text[index];
        }

        return new string(builder).Trim();
    }

    private static ReportDiagnostic CreateViewerFailureDiagnostic(Exception exception, string path)
    {
        return new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ViewerOperationFailed,
            exception.Message,
            path);
    }

    private static string CreateFailureMessage(Exception exception, string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? fallbackMessage
            : exception.Message;
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void CopyReports(
        IReadOnlyDictionary<string, ReportDefinition> source,
        IDictionary<string, ReportDefinition> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyValues(
        IReadOnlyDictionary<string, object?> source,
        IDictionary<string, object?> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static ReportParameterValue CloneParameterValue(ReportParameterValue value)
    {
        var clone = new ReportParameterValue
        {
            IsNull = value.IsNull
        };
        for (var index = 0; index < value.Values.Count; index++)
        {
            clone.Values.Add(value.Values[index]);
        }

        for (var index = 0; index < value.Labels.Count; index++)
        {
            clone.Labels.Add(value.Labels[index]);
        }

        return clone;
    }

    private sealed record ViewerNavigationFrame(
        ReportViewerSource Source,
        IReadOnlyDictionary<string, ReportParameterValue> Parameters,
        ReportViewerState State);
}
