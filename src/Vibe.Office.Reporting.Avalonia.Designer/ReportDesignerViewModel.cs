using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Runtime.CompilerServices;
using ReactiveUI;
using Vibe.Office.Printing;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Expressions;
using Vibe.Office.Reporting.Avalonia.Viewer;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// One explorer node shown by the report designer.
/// </summary>
public sealed class ReportDesignerExplorerNodeViewModel : ReactiveObject
{
    private string _label;
    private bool _isSelected;

    internal ReportDesignerExplorerNodeViewModel(
        string label,
        string category,
        object? target,
        Action<ReportDesignerExplorerNodeViewModel> selectAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(selectAction);

        _label = label;
        Category = category;
        Target = target;
        Children = new ObservableCollection<ReportDesignerExplorerNodeViewModel>();
        SelectCommand = ReactiveCommand.Create(() => selectAction(this));
    }

    /// <summary>
    /// Gets the node label.
    /// </summary>
    public string Label
    {
        get => _label;
        internal set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    /// <summary>
    /// Gets the node category label.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the underlying selected target, when applicable.
    /// </summary>
    public object? Target { get; }

    /// <summary>
    /// Gets the child nodes.
    /// </summary>
    public ObservableCollection<ReportDesignerExplorerNodeViewModel> Children { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the node is selected by the designer view model.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// Gets the command that selects the node.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SelectCommand { get; }
}

/// <summary>
/// One positioned report item shown on the design surface.
/// </summary>
public sealed class ReportDesignerCanvasItemViewModel : ReactiveObject
{
    private string _borderBrush = "#9B8B69";
    private string _fillBrush = "#FFF8ED";
    private bool _isSelected;
    private string _label = string.Empty;
    private string _summary = string.Empty;

    internal ReportDesignerCanvasItemViewModel(
        ReportItem item,
        Action<ReportDesignerCanvasItemViewModel> selectAction)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ArgumentNullException.ThrowIfNull(selectAction);
        SelectCommand = ReactiveCommand.Create(() => selectAction(this));
        Refresh();
    }

    /// <summary>
    /// Gets the underlying report item.
    /// </summary>
    public ReportItem Item { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label
    {
        get => _label;
        internal set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    /// <summary>
    /// Gets the summary text.
    /// </summary>
    public string Summary
    {
        get => _summary;
        internal set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    /// <summary>
    /// Gets the left coordinate on the design surface.
    /// </summary>
    public double Left => Math.Max(0d, Item.Bounds.X);

    /// <summary>
    /// Gets the top coordinate on the design surface.
    /// </summary>
    public double Top => Math.Max(0d, Item.Bounds.Y);

    /// <summary>
    /// Gets the displayed width on the design surface.
    /// </summary>
    public double Width => Math.Max(48d, Item.Bounds.Width);

    /// <summary>
    /// Gets the displayed height on the design surface.
    /// </summary>
    public double Height => Math.Max(36d, Item.Bounds.Height);

    /// <summary>
    /// Gets the background brush string.
    /// </summary>
    public string FillBrush
    {
        get => _fillBrush;
        internal set => this.RaiseAndSetIfChanged(ref _fillBrush, value);
    }

    /// <summary>
    /// Gets the border brush string.
    /// </summary>
    public string BorderBrush
    {
        get => _borderBrush;
        internal set => this.RaiseAndSetIfChanged(ref _borderBrush, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item is selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// Gets the select command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SelectCommand { get; }

    internal void Refresh()
    {
        Label = string.IsNullOrWhiteSpace(Item.Name) ? Item.Id : Item.Name;
        Summary = Item switch
        {
            TextItem textItem => string.IsNullOrWhiteSpace(textItem.StaticText)
                ? (string.IsNullOrWhiteSpace(textItem.ValueExpression) ? "Text box" : textItem.ValueExpression!)
                : textItem.StaticText!,
            ChartItem chartItem => $"Chart on {chartItem.DataSetId ?? "no dataset"}",
            TablixItem tablixItem => $"Tablix on {tablixItem.DataSetId ?? "no dataset"}",
            DocumentTemplateItem templateItem => $"Template {templateItem.TemplateId ?? "embedded"}",
            SubreportItem subreportItem => $"Subreport {subreportItem.ReportReferenceId}",
            ImageItem imageItem => imageItem.SourceKind.ToString(),
            ShapeItem shapeItem => shapeItem.Shape.ToString(),
            LineItem => "Line item",
            _ => Item.GetType().Name
        };

        FillBrush = Item switch
        {
            TextItem => "#FFF7D9",
            ChartItem => "#DDF6F0",
            TablixItem => "#E5F0FF",
            DocumentTemplateItem => "#F6E8FF",
            ShapeItem => "#FFEDE2",
            SubreportItem => "#E8F6E8",
            ImageItem => "#FBE9E9",
            _ => "#FFF8ED"
        };

        BorderBrush = IsSelected ? "#3E5C76" : "#9B8B69";
        this.RaisePropertyChanged(nameof(Left));
        this.RaisePropertyChanged(nameof(Top));
        this.RaisePropertyChanged(nameof(Width));
        this.RaisePropertyChanged(nameof(Height));
    }
}

/// <summary>
/// One selectable list entry used by data, parameter, and shared-template editors.
/// </summary>
public sealed class ReportDesignerSelectionEntryViewModel : ReactiveObject
{
    private bool _isSelected;
    private string _subtitle;
    private string _title;

    internal ReportDesignerSelectionEntryViewModel(
        object target,
        string title,
        string subtitle,
        Action<ReportDesignerSelectionEntryViewModel> selectAction)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _title = title ?? string.Empty;
        _subtitle = subtitle ?? string.Empty;
        ArgumentNullException.ThrowIfNull(selectAction);
        SelectCommand = ReactiveCommand.Create(() => selectAction(this));
    }

    /// <summary>
    /// Gets the underlying report object.
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// Gets the primary text.
    /// </summary>
    public string Title
    {
        get => _title;
        internal set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>
    /// Gets the secondary text.
    /// </summary>
    public string Subtitle
    {
        get => _subtitle;
        internal set => this.RaiseAndSetIfChanged(ref _subtitle, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the entry is selected by the designer view model.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// Gets the select command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SelectCommand { get; }
}

/// <summary>
/// One grouping entry shown in the grouping pane.
/// </summary>
public sealed class ReportDesignerGroupingEntryViewModel
{
    internal ReportDesignerGroupingEntryViewModel(string title, string subtitle)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Subtitle = subtitle ?? string.Empty;
    }

    /// <summary>
    /// Gets the primary caption.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the secondary caption.
    /// </summary>
    public string Subtitle { get; }
}

/// <summary>
/// Base type for explicit property-grid entries.
/// </summary>
public abstract class ReportDesignerPropertyViewModel : ReactiveObject
{
    private string? _errorMessage;

    internal ReportDesignerPropertyViewModel(string id, string label, string? description)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description ?? string.Empty;
    }

    /// <summary>
    /// Gets the stable property identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the helper description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets or sets the current validation message.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets a value indicating whether the entry currently has a validation error.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    internal void ClearError()
    {
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(HasError));
    }

    internal void SetError(string? errorMessage)
    {
        ErrorMessage = errorMessage;
        this.RaisePropertyChanged(nameof(HasError));
    }
}

/// <summary>
/// Text property-grid entry.
/// </summary>
public sealed class ReportDesignerTextPropertyViewModel : ReportDesignerPropertyViewModel
{
    private readonly Func<string, string?> _apply;
    private string _value;

    internal ReportDesignerTextPropertyViewModel(
        string id,
        string label,
        string? description,
        string initialValue,
        bool isMultiline,
        Func<string, string?> apply)
        : base(id, label, description)
    {
        _value = initialValue ?? string.Empty;
        IsMultiline = isMultiline;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <summary>
    /// Gets a value indicating whether the editor should use a multiline text box.
    /// </summary>
    public bool IsMultiline { get; }

    /// <summary>
    /// Gets or sets the text value.
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_value, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _value, normalized);
            SetError(_apply(normalized));
        }
    }
}

/// <summary>
/// Boolean property-grid entry.
/// </summary>
public sealed class ReportDesignerBooleanPropertyViewModel : ReportDesignerPropertyViewModel
{
    private readonly Action<bool> _apply;
    private bool _value;

    internal ReportDesignerBooleanPropertyViewModel(
        string id,
        string label,
        string? description,
        bool initialValue,
        Action<bool> apply)
        : base(id, label, description)
    {
        _value = initialValue;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <summary>
    /// Gets or sets the boolean value.
    /// </summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _value, value);
            _apply(value);
            ClearError();
        }
    }
}

/// <summary>
/// One selectable option for a choice property.
/// </summary>
public sealed class ReportDesignerChoiceOptionViewModel
{
    internal ReportDesignerChoiceOptionViewModel(string value, string label)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Label = label ?? throw new ArgumentNullException(nameof(label));
    }

    /// <summary>
    /// Gets the underlying value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; }
}

/// <summary>
/// Choice property-grid entry.
/// </summary>
public sealed class ReportDesignerChoicePropertyViewModel : ReportDesignerPropertyViewModel
{
    private readonly Action<string> _apply;
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _options = new();
    private ReportDesignerChoiceOptionViewModel? _selectedOption;

    internal ReportDesignerChoicePropertyViewModel(
        string id,
        string label,
        string? description,
        IEnumerable<ReportDesignerChoiceOptionViewModel> options,
        string initialValue,
        Action<string> apply)
        : base(id, label, description)
    {
        ArgumentNullException.ThrowIfNull(options);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));

        Options = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_options);
        foreach (var option in options)
        {
            _options.Add(option);
        }

        _selectedOption = _options.FirstOrDefault(candidate =>
            string.Equals(candidate.Value, initialValue, StringComparison.OrdinalIgnoreCase))
            ?? _options.FirstOrDefault();
    }

    /// <summary>
    /// Gets the available options.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> Options { get; }

    /// <summary>
    /// Gets or sets the selected option.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (ReferenceEquals(_selectedOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedOption, value);
            if (value is not null)
            {
                _apply(value.Value);
                ClearError();
            }
        }
    }
}

/// <summary>
/// One editable expression slot exposed by the designer.
/// </summary>
public sealed class ReportDesignerExpressionEntryViewModel : ReactiveObject
{
    private string _text;

    internal ReportDesignerExpressionEntryViewModel(
        string id,
        string label,
        string text,
        Func<string, string?> apply)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        _text = text ?? string.Empty;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    private readonly Func<string, string?> _apply;

    /// <summary>
    /// Gets the stable slot identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets or sets the editable expression text.
    /// </summary>
    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value ?? string.Empty);
    }

    internal string? Apply()
    {
        return _apply(Text);
    }
}

/// <summary>
/// One built-in template gallery item.
/// </summary>
public sealed class ReportDesignerTemplateGalleryItemViewModel : ReactiveObject
{
    internal ReportDesignerTemplateGalleryItemViewModel(
        string id,
        string title,
        string category,
        string description,
        Action<ReportDesignerTemplateGalleryItemViewModel> applyAction)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        ArgumentNullException.ThrowIfNull(applyAction);
        ApplyCommand = ReactiveCommand.Create(() => applyAction(this));
    }

    /// <summary>
    /// Gets the stable gallery identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the gallery category.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the apply command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
}

/// <summary>
/// View model for the Avalonia paginated-report designer.
/// </summary>
public sealed class ReportDesignerViewModel : ReactiveObject, IDisposable
{
    private readonly IReportExpressionCompiler _expressionCompiler;
    private readonly IReadOnlyList<ReportDesignerChoiceOptionViewModel> _providerOptions;
    private readonly ObservableCollection<ReportDesignerCanvasItemViewModel> _canvasItems = new();
    private readonly ObservableCollection<ReportDesignerSelectionEntryViewModel> _dataSetEntries = new();
    private readonly ObservableCollection<ReportDesignerSelectionEntryViewModel> _dataSourceEntries = new();
    private readonly ObservableCollection<ReportDesignerExplorerNodeViewModel> _explorerNodes = new();
    private readonly ObservableCollection<ReportDesignerExpressionEntryViewModel> _expressionEntries = new();
    private readonly ObservableCollection<ReportDesignerTemplateGalleryItemViewModel> _galleryItems = new();
    private readonly Dictionary<ReportItem, ReportDesignerCanvasItemViewModel> _itemCanvasMap = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ReportItem, ReportSection> _itemSectionMap = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, ReportDesignerExplorerNodeViewModel> _objectExplorerMap = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, ReportDesignerSelectionEntryViewModel> _objectSelectionEntryMap = new(ReferenceEqualityComparer.Instance);
    private readonly ObservableCollection<ReportDesignerSelectionEntryViewModel> _parameterEntries = new();
    private readonly ObservableCollection<ReportDesignerPropertyViewModel> _propertyEntries = new();
    private readonly ObservableCollection<ReportDesignerGroupingEntryViewModel> _rowGroupEntries = new();
    private readonly ObservableCollection<ReportDesignerGroupingEntryViewModel> _columnGroupEntries = new();
    private readonly ObservableCollection<ReportDesignerSelectionEntryViewModel> _sharedTemplateEntries = new();
    private ReportDesignerCanvasItemViewModel? _selectedCanvasItem;
    private ReportDesignerExpressionEntryViewModel? _selectedExpressionEntry;
    private ReportDesignerExplorerNodeViewModel? _selectedExplorerNode;
    private ReportDesignerTemplateGalleryItemViewModel? _selectedGalleryItem;
    private ReportDesignerSelectionEntryViewModel? _selectedParameterEntry;
    private ReportDesignerSelectionEntryViewModel? _selectedDataSourceEntry;
    private ReportDesignerSelectionEntryViewModel? _selectedDataSetEntry;
    private ReportDesignerSelectionEntryViewModel? _selectedSharedTemplateEntry;
    private ReportSection? _selectedSection;
    private object? _selectedTarget;
    private string? _expressionStatusMessage;
    private bool _isBusy;
    private bool _isPreviewDirty = true;
    private int _selectedCenterTabIndex;
    private int _selectedInspectorTabIndex;
    private string? _statusMessage;
    private bool _suppressSelectionSynchronization;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerViewModel" /> class.
    /// </summary>
    /// <param name="source">The editable report source.</param>
    /// <param name="previewSessionService">The preview session service.</param>
    /// <param name="expressionCompiler">The expression compiler used by the expression editor.</param>
    /// <param name="connectorCatalog">The available connector catalog used by the data-source editor.</param>
    public ReportDesignerViewModel(
        ReportViewerSource source,
        IReportViewerSessionService? previewSessionService = null,
        IReportExpressionCompiler? expressionCompiler = null,
        ReportDataConnectorCatalog? connectorCatalog = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _expressionCompiler = expressionCompiler ?? new ReportExpressionCompiler();
        connectorCatalog ??= ReportDataConnectorCatalog.CreateDefault();
        AvailableConnectors = connectorCatalog.ListConnectors();
        _providerOptions = CreateProviderOptions(AvailableConnectors);
        PreviewViewModel = new ReportViewerViewModel(previewSessionService ?? new ReportViewerSessionService());

        ExplorerNodes = new ReadOnlyObservableCollection<ReportDesignerExplorerNodeViewModel>(_explorerNodes);
        DesignItems = new ReadOnlyObservableCollection<ReportDesignerCanvasItemViewModel>(_canvasItems);
        ParameterEntries = new ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel>(_parameterEntries);
        DataSourceEntries = new ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel>(_dataSourceEntries);
        DataSetEntries = new ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel>(_dataSetEntries);
        SharedTemplateEntries = new ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel>(_sharedTemplateEntries);
        PropertyEntries = new ReadOnlyObservableCollection<ReportDesignerPropertyViewModel>(_propertyEntries);
        RowGroupEntries = new ReadOnlyObservableCollection<ReportDesignerGroupingEntryViewModel>(_rowGroupEntries);
        ColumnGroupEntries = new ReadOnlyObservableCollection<ReportDesignerGroupingEntryViewModel>(_columnGroupEntries);
        ExpressionEntries = new ReadOnlyObservableCollection<ReportDesignerExpressionEntryViewModel>(_expressionEntries);
        TemplateGalleryItems = new ReadOnlyObservableCollection<ReportDesignerTemplateGalleryItemViewModel>(_galleryItems);

        RefreshPreviewCommand = ReactiveCommand.CreateFromTask(RefreshPreviewAsync);
        AddSectionCommand = ReactiveCommand.Create(AddSection);
        AddTextItemCommand = ReactiveCommand.Create(AddTextItem);
        AddChartItemCommand = ReactiveCommand.Create(AddChartItem);
        AddTablixItemCommand = ReactiveCommand.Create(AddTablixItem);
        AddTemplateItemCommand = ReactiveCommand.Create(AddTemplateItem);
        AddParameterCommand = ReactiveCommand.Create(AddParameter);
        AddDataSourceCommand = ReactiveCommand.Create(AddDataSource);
        AddDataSetCommand = ReactiveCommand.Create(AddDataSet);
        AddSharedTemplateCommand = ReactiveCommand.Create(AddSharedTemplate);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelected);
        ApplySelectedExpressionCommand = ReactiveCommand.Create(ApplySelectedExpression);
        ApplySelectedTemplateCommand = ReactiveCommand.Create(ApplySelectedTemplate);

        EnsureMinimumStructure();
        BuildTemplateGallery();
        RebuildDesignerState(ReportDefinition.Sections.FirstOrDefault());
    }

    /// <summary>
    /// Gets the live editable report source.
    /// </summary>
    public ReportViewerSource Source { get; }

    /// <summary>
    /// Gets the live editable report definition.
    /// </summary>
    public ReportDefinition ReportDefinition => Source.ReportDefinition;

    /// <summary>
    /// Gets the connectors available to the data-source editor.
    /// </summary>
    public IReadOnlyList<ReportDataConnectorDefinition> AvailableConnectors { get; }

    /// <summary>
    /// Gets the embedded preview view model.
    /// </summary>
    public ReportViewerViewModel PreviewViewModel { get; }

    /// <summary>
    /// Gets the explorer nodes.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerExplorerNodeViewModel> ExplorerNodes { get; }

    /// <summary>
    /// Gets the design-surface items for the currently selected section.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerCanvasItemViewModel> DesignItems { get; }

    /// <summary>
    /// Gets the parameter editor entries.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel> ParameterEntries { get; }

    /// <summary>
    /// Gets the data-source editor entries.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel> DataSourceEntries { get; }

    /// <summary>
    /// Gets the dataset editor entries.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel> DataSetEntries { get; }

    /// <summary>
    /// Gets the shared-template editor entries.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerSelectionEntryViewModel> SharedTemplateEntries { get; }

    /// <summary>
    /// Gets the explicit property-grid entries for the current selection.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerPropertyViewModel> PropertyEntries { get; }

    /// <summary>
    /// Gets the row grouping entries for the selected tablix.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerGroupingEntryViewModel> RowGroupEntries { get; }

    /// <summary>
    /// Gets the column grouping entries for the selected tablix.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerGroupingEntryViewModel> ColumnGroupEntries { get; }

    /// <summary>
    /// Gets the expression slots for the current selection.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerExpressionEntryViewModel> ExpressionEntries { get; }

    /// <summary>
    /// Gets the built-in template gallery items.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerTemplateGalleryItemViewModel> TemplateGalleryItems { get; }

    /// <summary>
    /// Gets or sets the selected explorer node.
    /// </summary>
    public ReportDesignerExplorerNodeViewModel? SelectedExplorerNode
    {
        get => _selectedExplorerNode;
        set
        {
            if (ReferenceEquals(_selectedExplorerNode, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedExplorerNode, value);
            if (!_suppressSelectionSynchronization)
            {
                SelectTarget(value?.Target ?? ReportDefinition);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected design-surface item.
    /// </summary>
    public ReportDesignerCanvasItemViewModel? SelectedCanvasItem
    {
        get => _selectedCanvasItem;
        set
        {
            if (ReferenceEquals(_selectedCanvasItem, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedCanvasItem, value);
            if (!_suppressSelectionSynchronization && value is not null)
            {
                SelectTarget(value.Item);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected parameter entry.
    /// </summary>
    public ReportDesignerSelectionEntryViewModel? SelectedParameterEntry
    {
        get => _selectedParameterEntry;
        set
        {
            if (ReferenceEquals(_selectedParameterEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedParameterEntry, value);
            if (!_suppressSelectionSynchronization && value is not null)
            {
                SelectTarget(value.Target);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source entry.
    /// </summary>
    public ReportDesignerSelectionEntryViewModel? SelectedDataSourceEntry
    {
        get => _selectedDataSourceEntry;
        set
        {
            if (ReferenceEquals(_selectedDataSourceEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceEntry, value);
            if (!_suppressSelectionSynchronization && value is not null)
            {
                SelectTarget(value.Target);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset entry.
    /// </summary>
    public ReportDesignerSelectionEntryViewModel? SelectedDataSetEntry
    {
        get => _selectedDataSetEntry;
        set
        {
            if (ReferenceEquals(_selectedDataSetEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetEntry, value);
            if (!_suppressSelectionSynchronization && value is not null)
            {
                SelectTarget(value.Target);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected shared-template entry.
    /// </summary>
    public ReportDesignerSelectionEntryViewModel? SelectedSharedTemplateEntry
    {
        get => _selectedSharedTemplateEntry;
        set
        {
            if (ReferenceEquals(_selectedSharedTemplateEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSharedTemplateEntry, value);
            if (!_suppressSelectionSynchronization && value is not null)
            {
                SelectTarget(value.Target);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected expression entry.
    /// </summary>
    public ReportDesignerExpressionEntryViewModel? SelectedExpressionEntry
    {
        get => _selectedExpressionEntry;
        set
        {
            if (ReferenceEquals(_selectedExpressionEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedExpressionEntry, value);
            this.RaisePropertyChanged(nameof(SelectedExpressionLabel));
            this.RaisePropertyChanged(nameof(SelectedExpressionText));
            this.RaisePropertyChanged(nameof(CanApplyExpression));
        }
    }

    /// <summary>
    /// Gets or sets the selected gallery item.
    /// </summary>
    public ReportDesignerTemplateGalleryItemViewModel? SelectedGalleryItem
    {
        get => _selectedGalleryItem;
        set
        {
            if (ReferenceEquals(_selectedGalleryItem, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedGalleryItem, value);
            this.RaisePropertyChanged(nameof(CanApplyTemplate));
        }
    }

    /// <summary>
    /// Gets or sets the selected center tab index.
    /// </summary>
    public int SelectedCenterTabIndex
    {
        get => _selectedCenterTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedCenterTabIndex, Math.Clamp(value, 0, 2));
    }

    /// <summary>
    /// Gets or sets the selected inspector tab index.
    /// </summary>
    public int SelectedInspectorTabIndex
    {
        get => _selectedInspectorTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedInspectorTabIndex, Math.Clamp(value, 0, 3));
    }

    /// <summary>
    /// Gets the selected section display name.
    /// </summary>
    public string CurrentSectionName => string.IsNullOrWhiteSpace(_selectedSection?.Name)
        ? (_selectedSection?.Id ?? "No section")
        : _selectedSection!.Name;

    /// <summary>
    /// Gets the current design-surface width.
    /// </summary>
    public double SurfaceWidth => _selectedSection?.PageSettings.Width ?? 816d;

    /// <summary>
    /// Gets the current design-surface height.
    /// </summary>
    public double SurfaceHeight => _selectedSection?.PageSettings.Height ?? 1056d;

    /// <summary>
    /// Gets the current selection title.
    /// </summary>
    public string SelectedObjectTitle => DescribeTarget(_selectedTarget).title;

    /// <summary>
    /// Gets the current selection type label.
    /// </summary>
    public string SelectedObjectType => DescribeTarget(_selectedTarget).type;

    /// <summary>
    /// Gets the selected expression label.
    /// </summary>
    public string SelectedExpressionLabel => SelectedExpressionEntry?.Label ?? "No expression selected";

    /// <summary>
    /// Gets or sets the selected expression text.
    /// </summary>
    public string SelectedExpressionText
    {
        get => SelectedExpressionEntry?.Text ?? string.Empty;
        set
        {
            if (SelectedExpressionEntry is null)
            {
                return;
            }

            SelectedExpressionEntry.Text = value ?? string.Empty;
            this.RaisePropertyChanged(nameof(SelectedExpressionText));
        }
    }

    /// <summary>
    /// Gets the current design-surface size text.
    /// </summary>
    public string SurfaceDisplayText => $"{SurfaceWidth:0} x {SurfaceHeight:0}";

    /// <summary>
    /// Gets a value indicating whether the grouping pane should be shown.
    /// </summary>
    public bool IsGroupingVisible => GetSelectedTablix() is not null;

    /// <summary>
    /// Gets the grouping status caption.
    /// </summary>
    public string GroupingStatusText => GetSelectedTablix() is TablixItem tablix
        ? $"Grouping for {DescribeTarget(tablix).title}"
        : "Select a tablix to inspect row and column groups.";

    /// <summary>
    /// Gets or sets the designer status message.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>
    /// Gets or sets the expression-editor status message.
    /// </summary>
    public string? ExpressionStatusMessage
    {
        get => _expressionStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _expressionStatusMessage, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the designer is performing a background operation.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the preview is out of date with the current design.
    /// </summary>
    public bool IsPreviewDirty
    {
        get => _isPreviewDirty;
        private set => this.RaiseAndSetIfChanged(ref _isPreviewDirty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the current selection can be removed.
    /// </summary>
    public bool CanRemoveSelected => _selectedTarget is ReportSection
        or ReportItem
        or ReportParameterDefinition
        or ReportDataSourceDefinition
        or ReportDataSetDefinition
        or ReportSharedTemplateDefinition;

    /// <summary>
    /// Gets a value indicating whether the selected gallery item can be applied.
    /// </summary>
    public bool CanApplyTemplate => SelectedGalleryItem is not null;

    /// <summary>
    /// Gets a value indicating whether the selected expression can be applied.
    /// </summary>
    public bool CanApplyExpression => SelectedExpressionEntry is not null;

    /// <summary>
    /// Gets the preview refresh command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshPreviewCommand { get; }

    /// <summary>
    /// Gets the add-section command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddSectionCommand { get; }

    /// <summary>
    /// Gets the add-text command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddTextItemCommand { get; }

    /// <summary>
    /// Gets the add-chart command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddChartItemCommand { get; }

    /// <summary>
    /// Gets the add-tablix command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddTablixItemCommand { get; }

    /// <summary>
    /// Gets the add-template-item command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddTemplateItemCommand { get; }

    /// <summary>
    /// Gets the add-parameter command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddParameterCommand { get; }

    /// <summary>
    /// Gets the add-data-source command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddDataSourceCommand { get; }

    /// <summary>
    /// Gets the add-dataset command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddDataSetCommand { get; }

    /// <summary>
    /// Gets the add-shared-template command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddSharedTemplateCommand { get; }

    /// <summary>
    /// Gets the remove-selection command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RemoveSelectedCommand { get; }

    /// <summary>
    /// Gets the apply-expression command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ApplySelectedExpressionCommand { get; }

    /// <summary>
    /// Gets the apply-template command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ApplySelectedTemplateCommand { get; }

    /// <summary>
    /// Refreshes the preview tab from the current report definition.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when preview refresh finishes.</returns>
    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Refreshing preview...";

        try
        {
            var previousSnapshot = PreviewViewModel.CurrentSnapshot;
            await PreviewViewModel.LoadAsync(BuildPreviewSource(), cancellationToken);
            var previewUpdated = !ReferenceEquals(previousSnapshot, PreviewViewModel.CurrentSnapshot);
            IsPreviewDirty = !previewUpdated;

            StatusMessage = previewUpdated
                ? PreviewViewModel.StatusMessage
                : (PreviewViewModel.StatusMessage ?? "Preview refresh failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        PreviewViewModel.Dispose();
    }

    private void AddSection()
    {
        var section = new ReportSection
        {
            Id = CreateUniqueId("section", ReportDefinition.Sections.Select(static section => section.Id)),
            Name = $"Section {ReportDefinition.Sections.Count + 1}"
        };
        ReportDefinition.Sections.Add(section);
        RebuildDesignerState(section);
        SelectedInspectorTabIndex = 0;
        MarkDirty("Added section.");
    }

    private void AddTextItem()
    {
        var section = EnsureSelectedSection();
        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = "Text Box",
            StaticText = "New text",
            Bounds = CreateNextBounds(section, 320f, 70f)
        };
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added text item.");
    }

    private void AddChartItem()
    {
        var section = EnsureSelectedSection();
        var dataSetId = EnsureDataSetForGallery();
        var item = new ChartItem
        {
            Id = CreateUniqueId("chart", EnumerateItemIds()),
            Name = "Chart",
            DataSetId = dataSetId,
            CategoryExpression = "Fields.Category",
            TitleExpression = "'Chart'",
            Bounds = CreateNextBounds(section, 360f, 220f)
        };
        item.Series.Add(new ReportChartSeriesDefinition
        {
            NameExpression = "'Series'",
            ValueExpression = "Fields.Value"
        });
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added chart item.");
    }

    private void AddTablixItem()
    {
        var section = EnsureSelectedSection();
        var dataSetId = EnsureDataSetForGallery();
        var item = CreateDefaultTablix(
            CreateUniqueId("tablix", EnumerateItemIds()),
            dataSetId,
            section);
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added tablix item.");
    }

    private void AddTemplateItem()
    {
        var section = EnsureSelectedSection();
        var templateDefinition = EnsureSharedTemplate("narrative", ReportDocumentTemplateFormat.Markdown, "# Narrative\n\n{{Title}}");
        var item = new DocumentTemplateItem
        {
            Id = CreateUniqueId("templateItem", EnumerateItemIds()),
            Name = "Narrative Block",
            TemplateId = templateDefinition.Id,
            TemplateFormat = templateDefinition.Format,
            Bounds = CreateNextBounds(section, 360f, 180f)
        };
        item.Bindings["Title"] = "Parameters.Title";
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added document-template item.");
    }

    private void AddParameter()
    {
        var parameter = new ReportParameterDefinition
        {
            Id = CreateUniqueId("parameter", ReportDefinition.Parameters.Select(static parameter => parameter.Id)),
            DisplayName = $"Parameter {ReportDefinition.Parameters.Count + 1}",
            Prompt = "Prompt",
            DataType = ReportParameterDataType.String,
            Visibility = ReportParameterVisibility.Visible
        };
        ReportDefinition.Parameters.Add(parameter);
        RebuildDesignerState(parameter);
        SelectedInspectorTabIndex = 1;
        MarkDirty("Added parameter.");
    }

    private void AddDataSource()
    {
        var dataSource = new ReportDataSourceDefinition
        {
            Id = CreateUniqueId("dataSource", ReportDefinition.DataSources.Select(static dataSource => dataSource.Id)),
            ProviderId = ReportProviderIds.InMemory,
            ConnectionName = "sample"
        };
        dataSource.Options["sourceKey"] = "sample";
        ReportDefinition.DataSources.Add(dataSource);
        RebuildDesignerState(dataSource);
        SelectedInspectorTabIndex = 2;
        MarkDirty("Added data source.");
    }

    private void AddDataSet()
    {
        var dataSet = new ReportDataSetDefinition
        {
            Id = CreateUniqueId("dataSet", ReportDefinition.DataSets.Select(static dataSet => dataSet.Id)),
            DataSourceId = EnsureDataSourceForDataSet(),
            Query = "items"
        };
        dataSet.ExpectedFields.Add(new ReportFieldDefinition
        {
            Name = "Category",
            DataType = ReportParameterDataType.String
        });
        dataSet.ExpectedFields.Add(new ReportFieldDefinition
        {
            Name = "Value",
            DataType = ReportParameterDataType.Decimal
        });
        ReportDefinition.DataSets.Add(dataSet);
        RebuildDesignerState(dataSet);
        SelectedInspectorTabIndex = 2;
        MarkDirty("Added dataset.");
    }

    private void AddSharedTemplate()
    {
        var template = new ReportSharedTemplateDefinition
        {
            Id = CreateUniqueId("template", ReportDefinition.SharedTemplates.Select(static template => template.Id)),
            Format = ReportDocumentTemplateFormat.Markdown,
            IsEmbedded = true,
            Content = "# Template\n\nBody"
        };
        ReportDefinition.SharedTemplates.Add(template);
        RebuildDesignerState(template);
        SelectedInspectorTabIndex = 3;
        MarkDirty("Added shared template.");
    }

    private void RemoveSelected()
    {
        var selectedTarget = _selectedTarget;
        if (selectedTarget is null)
        {
            return;
        }

        object? fallbackSelection = _selectedSection ?? ReportDefinition.Sections.FirstOrDefault();

        switch (selectedTarget)
        {
            case ReportSection section:
                ReportDefinition.Sections.Remove(section);
                if (ReportDefinition.Sections.Count == 0)
                {
                    EnsureMinimumStructure();
                }

                fallbackSelection = ReportDefinition.Sections.FirstOrDefault();
                break;
            case ReportItem item when _itemSectionMap.TryGetValue(item, out var itemSection):
                if (!itemSection.BodyItems.Remove(item))
                {
                    itemSection.HeaderItems.Remove(item);
                    itemSection.FooterItems.Remove(item);
                }

                fallbackSelection = itemSection;
                break;
            case ReportParameterDefinition parameter:
                ReportDefinition.Parameters.Remove(parameter);
                fallbackSelection = ReportDefinition;
                break;
            case ReportDataSourceDefinition dataSource:
                ReportDefinition.DataSources.Remove(dataSource);
                foreach (var dataSet in ReportDefinition.DataSets)
                {
                    if (string.Equals(dataSet.DataSourceId, dataSource.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        dataSet.DataSourceId = ReportDefinition.DataSources.FirstOrDefault()?.Id ?? string.Empty;
                    }
                }

                fallbackSelection = ReportDefinition;
                break;
            case ReportDataSetDefinition dataSet:
                ReportDefinition.DataSets.Remove(dataSet);
                foreach (var itemCandidate in EnumerateItems())
                {
                    switch (itemCandidate)
                    {
                        case ChartItem chartItem when string.Equals(chartItem.DataSetId, dataSet.Id, StringComparison.OrdinalIgnoreCase):
                            chartItem.DataSetId = ReportDefinition.DataSets.FirstOrDefault()?.Id;
                            break;
                        case TablixItem tablixItem when string.Equals(tablixItem.DataSetId, dataSet.Id, StringComparison.OrdinalIgnoreCase):
                            tablixItem.DataSetId = ReportDefinition.DataSets.FirstOrDefault()?.Id;
                            break;
                    }
                }

                fallbackSelection = ReportDefinition;
                break;
            case ReportSharedTemplateDefinition template:
                ReportDefinition.SharedTemplates.Remove(template);
                foreach (var itemCandidate in EnumerateItems().OfType<DocumentTemplateItem>())
                {
                    if (string.Equals(itemCandidate.TemplateId, template.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        itemCandidate.TemplateId = null;
                    }
                }

                fallbackSelection = ReportDefinition;
                break;
            default:
                return;
        }

        RebuildDesignerState(fallbackSelection);
        MarkDirty("Removed selection.");
    }

    private void ApplySelectedExpression()
    {
        if (SelectedExpressionEntry is null)
        {
            return;
        }

        var error = SelectedExpressionEntry.Apply();
        ExpressionStatusMessage = error ?? "Expression applied.";
        if (error is null)
        {
            MarkDirty("Expression updated.");
            RefreshLightweightViews();
        }
    }

    private void ApplySelectedTemplate()
    {
        if (SelectedGalleryItem is null)
        {
            return;
        }

        ApplyGalleryItem(SelectedGalleryItem);
    }

    private void ApplyGalleryItem(ReportDesignerTemplateGalleryItemViewModel galleryItem)
    {
        switch (galleryItem.Id)
        {
            case "executive-title":
                ApplyExecutiveTitleTemplate();
                break;
            case "summary-table":
                ApplySummaryTableTemplate();
                break;
            case "narrative-brief":
                ApplyNarrativeBriefTemplate();
                break;
        }

        SelectedCenterTabIndex = 0;
    }

    private void ApplyExecutiveTitleTemplate()
    {
        var section = EnsureSelectedSection();
        var banner = new ShapeItem
        {
            Id = CreateUniqueId("shape", EnumerateItemIds()),
            Name = "Header Accent",
            Shape = ReportShapeKind.RoundedRectangle,
            Bounds = new ReportItemBounds(36f, 36f, section.PageSettings.Width - 72f, 84f)
        };
        var title = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = "Executive Title",
            ValueExpression = "Parameters.Title",
            FormatString = null,
            Bounds = new ReportItemBounds(56f, 54f, section.PageSettings.Width - 112f, 42f)
        };
        section.BodyItems.Add(banner);
        section.BodyItems.Add(title);
        RebuildDesignerState(title);
        MarkDirty("Applied executive title template.");
    }

    private void ApplySummaryTableTemplate()
    {
        var section = EnsureSelectedSection();
        var tablix = CreateDefaultTablix(
            CreateUniqueId("tablix", EnumerateItemIds()),
            EnsureDataSetForGallery(),
            section);
        tablix.Name = "Summary Table";
        section.BodyItems.Add(tablix);
        RebuildDesignerState(tablix);
        MarkDirty("Applied summary table template.");
    }

    private void ApplyNarrativeBriefTemplate()
    {
        var section = EnsureSelectedSection();
        var template = EnsureSharedTemplate(
            "briefing",
            ReportDocumentTemplateFormat.Markdown,
            "# Briefing\n\n{{Title}}\n\nPrepared for {{Audience}}");

        var item = new DocumentTemplateItem
        {
            Id = CreateUniqueId("templateItem", EnumerateItemIds()),
            Name = "Narrative Brief",
            TemplateId = template.Id,
            TemplateFormat = template.Format,
            Bounds = CreateNextBounds(section, 420f, 200f)
        };
        item.Bindings["Title"] = "Parameters.Title";
        item.Bindings["Audience"] = "'Leadership'";
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        MarkDirty("Applied narrative brief template.");
    }

    private void BuildTemplateGallery()
    {
        _galleryItems.Clear();
        _galleryItems.Add(new ReportDesignerTemplateGalleryItemViewModel(
            "executive-title",
            "Executive Title",
            "Cover",
            "Adds a wide title treatment for narrative reports and board packs.",
            ApplyGalleryItem));
        _galleryItems.Add(new ReportDesignerTemplateGalleryItemViewModel(
            "summary-table",
            "Summary Table",
            "Data",
            "Adds a dataset-backed tablix with a header row and one detail row.",
            ApplyGalleryItem));
        _galleryItems.Add(new ReportDesignerTemplateGalleryItemViewModel(
            "narrative-brief",
            "Narrative Brief",
            "Narrative",
            "Adds a markdown-backed document template block with bindings.",
            ApplyGalleryItem));
    }

    private void EnsureMinimumStructure()
    {
        if (ReportDefinition.Sections.Count > 0)
        {
            return;
        }

        ReportDefinition.Sections.Add(new ReportSection
        {
            Id = "section1",
            Name = "Section 1"
        });
    }

    private ReportSection EnsureSelectedSection()
    {
        if (_selectedSection is not null)
        {
            return _selectedSection;
        }

        EnsureMinimumStructure();
        _selectedSection = ReportDefinition.Sections[0];
        return _selectedSection;
    }

    private void RebuildDesignerState(object? selectionTarget)
    {
        object preservedTarget = selectionTarget
            ?? _selectedTarget
            ?? (object?)ReportDefinition.Sections.FirstOrDefault()
            ?? ReportDefinition;
        RebuildMapsAndCollections();
        SelectTarget(preservedTarget);
    }

    private void RebuildMapsAndCollections()
    {
        _itemSectionMap.Clear();
        _objectExplorerMap.Clear();
        _objectSelectionEntryMap.Clear();
        _itemCanvasMap.Clear();

        _explorerNodes.Clear();
        _parameterEntries.Clear();
        _dataSourceEntries.Clear();
        _dataSetEntries.Clear();
        _sharedTemplateEntries.Clear();
        _canvasItems.Clear();

        var root = new ReportDesignerExplorerNodeViewModel(
            string.IsNullOrWhiteSpace(ReportDefinition.Name) ? "Report" : ReportDefinition.Name,
            "Report",
            ReportDefinition,
            SelectExplorerNode);
        _explorerNodes.Add(root);
        _objectExplorerMap[ReportDefinition] = root;

        var sectionsGroup = CreateGroupNode("Sections");
        root.Children.Add(sectionsGroup);
        foreach (var section in ReportDefinition.Sections)
        {
            var sectionNode = CreateTargetNode(section);
            sectionsGroup.Children.Add(sectionNode);

            var headerGroup = CreateGroupNode("Header");
            sectionNode.Children.Add(headerGroup);
            foreach (var item in section.HeaderItems)
            {
                RegisterItem(item, section, headerGroup);
            }

            var bodyGroup = CreateGroupNode("Body");
            sectionNode.Children.Add(bodyGroup);
            foreach (var item in section.BodyItems)
            {
                RegisterItem(item, section, bodyGroup);
            }

            var footerGroup = CreateGroupNode("Footer");
            sectionNode.Children.Add(footerGroup);
            foreach (var item in section.FooterItems)
            {
                RegisterItem(item, section, footerGroup);
            }
        }

        var parameterGroup = CreateGroupNode("Parameters");
        root.Children.Add(parameterGroup);
        foreach (var parameter in ReportDefinition.Parameters)
        {
            var node = CreateTargetNode(parameter);
            parameterGroup.Children.Add(node);
            RegisterSelectionEntry(
                parameter,
                parameter.DisplayName,
                DescribeParameterEntry(parameter),
                _parameterEntries);
        }

        var dataSourceGroup = CreateGroupNode("Data Sources");
        root.Children.Add(dataSourceGroup);
        foreach (var dataSource in ReportDefinition.DataSources)
        {
            var node = CreateTargetNode(dataSource);
            dataSourceGroup.Children.Add(node);
            RegisterSelectionEntry(
                dataSource,
                dataSource.Id,
                DescribeDataSourceEntry(dataSource),
                _dataSourceEntries);
        }

        var dataSetGroup = CreateGroupNode("Data Sets");
        root.Children.Add(dataSetGroup);
        foreach (var dataSet in ReportDefinition.DataSets)
        {
            var node = CreateTargetNode(dataSet);
            dataSetGroup.Children.Add(node);
            RegisterSelectionEntry(
                dataSet,
                dataSet.Id,
                DescribeDataSetEntry(dataSet),
                _dataSetEntries);
        }

        var templateGroup = CreateGroupNode("Shared Templates");
        root.Children.Add(templateGroup);
        foreach (var template in ReportDefinition.SharedTemplates)
        {
            var node = CreateTargetNode(template);
            templateGroup.Children.Add(node);
            RegisterSelectionEntry(
                template,
                template.Id,
                DescribeTemplateEntry(template),
                _sharedTemplateEntries);
        }

        RebuildCanvasItems();
    }

    private void RebuildCanvasItems()
    {
        _canvasItems.Clear();
        _itemCanvasMap.Clear();

        var section = EnsureSelectedSection();
        foreach (var item in section.BodyItems)
        {
            var canvasItem = new ReportDesignerCanvasItemViewModel(item, SelectCanvasItem);
            _canvasItems.Add(canvasItem);
            _itemCanvasMap[item] = canvasItem;
        }

        this.RaisePropertyChanged(nameof(CurrentSectionName));
        this.RaisePropertyChanged(nameof(SurfaceWidth));
        this.RaisePropertyChanged(nameof(SurfaceHeight));
        this.RaisePropertyChanged(nameof(SurfaceDisplayText));
    }

    private void RebuildPropertiesAndExpressions()
    {
        BuildPropertyEntries();
        BuildGroupingEntries();
        BuildExpressionEntries();
        this.RaisePropertyChanged(nameof(CanApplyExpression));
        this.RaisePropertyChanged(nameof(CanRemoveSelected));
        this.RaisePropertyChanged(nameof(SelectedObjectTitle));
        this.RaisePropertyChanged(nameof(SelectedObjectType));
        this.RaisePropertyChanged(nameof(IsGroupingVisible));
        this.RaisePropertyChanged(nameof(GroupingStatusText));
    }

    private void BuildGroupingEntries()
    {
        _rowGroupEntries.Clear();
        _columnGroupEntries.Clear();

        var tablix = GetSelectedTablix();
        if (tablix is null)
        {
            return;
        }

        if (tablix.RowMembers.Count == 0)
        {
            _rowGroupEntries.Add(new ReportDesignerGroupingEntryViewModel("Details", "No explicit row groups."));
        }
        else
        {
            foreach (var member in tablix.RowMembers)
            {
                AppendGroupingEntry(member, depth: 0, _rowGroupEntries);
            }
        }

        if (tablix.Columns.Count == 0)
        {
            _columnGroupEntries.Add(new ReportDesignerGroupingEntryViewModel("Static Columns", "No column hierarchy."));
            return;
        }

        for (var columnIndex = 0; columnIndex < tablix.Columns.Count; columnIndex++)
        {
            var column = tablix.Columns[columnIndex];
            var title = $"Column {(columnIndex + 1):00}";
            if (!string.IsNullOrWhiteSpace(column.Id))
            {
                title = $"{title} · {column.Id}";
            }

            _columnGroupEntries.Add(new ReportDesignerGroupingEntryViewModel(
                title,
                $"{column.Width:0.#} pt static column"));
        }
    }

    private TablixItem? GetSelectedTablix()
    {
        return _selectedTarget switch
        {
            TablixItem tablix => tablix,
            _ => null
        };
    }

    private static void AppendGroupingEntry(
        ReportTablixMemberDefinition member,
        int depth,
        ICollection<ReportDesignerGroupingEntryViewModel> target)
    {
        var prefix = new string(' ', depth * 2);
        var title = member.Kind switch
        {
            ReportTablixMemberKind.Group => $"{prefix}{member.GroupName ?? "Group"}",
            ReportTablixMemberKind.Details => $"{prefix}Details",
            _ => $"{prefix}Static"
        };

        var subtitle = member.Kind switch
        {
            ReportTablixMemberKind.Group when !string.IsNullOrWhiteSpace(member.GroupExpression)
                => member.GroupExpression!,
            ReportTablixMemberKind.Details => "Detail rows",
            _ => member.RowDefinitionIndex.HasValue
                ? $"Row {member.RowDefinitionIndex.Value + 1}"
                : "Static member"
        };

        target.Add(new ReportDesignerGroupingEntryViewModel(title, subtitle));
        for (var index = 0; index < member.Members.Count; index++)
        {
            AppendGroupingEntry(member.Members[index], depth + 1, target);
        }
    }

    private void BuildPropertyEntries()
    {
        _propertyEntries.Clear();

        switch (_selectedTarget)
        {
            case ReportDefinition reportDefinition:
                AddTextProperty("report.id", "Id", "Stable report identifier.", reportDefinition.Id, false, value =>
                {
                    reportDefinition.Id = NormalizeRequired(value, "Report id");
                    OnModelChanged("Updated report id.");
                    return null;
                });
                AddTextProperty("report.name", "Name", "Display name used by hosts.", reportDefinition.Name, false, value =>
                {
                    reportDefinition.Name = value.Trim();
                    OnModelChanged("Updated report name.");
                    return null;
                });
                AddTextProperty("report.description", "Description", "Optional authoring description.", reportDefinition.Description ?? string.Empty, true, value =>
                {
                    reportDefinition.Description = NormalizeOptional(value);
                    OnModelChanged("Updated report description.");
                    return null;
                });
                break;
            case ReportSection section:
                AddTextProperty("section.id", "Id", "Stable section identifier.", section.Id, false, value =>
                {
                    section.Id = NormalizeRequired(value, "Section id");
                    OnModelChanged("Updated section id.");
                    return null;
                });
                AddTextProperty("section.name", "Name", "Section display name.", section.Name, false, value =>
                {
                    section.Name = value.Trim();
                    OnModelChanged("Updated section name.");
                    return null;
                });
                AddChoiceProperty("section.orientation", "Orientation", "Page orientation.", section.PageSettings.Orientation.ToString(), CreateEnumOptions<ReportPageOrientation>(), value =>
                {
                    section.PageSettings.Orientation = Enum.Parse<ReportPageOrientation>(value, ignoreCase: true);
                    OnModelChanged("Updated section orientation.");
                });
                AddTextProperty("section.width", "Width", "Page width in DIPs.", FormatFloat(section.PageSettings.Width), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.Width = parsed;
                        OnModelChanged("Updated section width.");
                    }));
                AddTextProperty("section.height", "Height", "Page height in DIPs.", FormatFloat(section.PageSettings.Height), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.Height = parsed;
                        OnModelChanged("Updated section height.");
                    }));
                AddTextProperty("section.marginLeft", "Margin Left", "Left page margin.", FormatFloat(section.PageSettings.MarginLeft), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.MarginLeft = parsed;
                        OnModelChanged("Updated section margin.");
                    }));
                AddTextProperty("section.marginTop", "Margin Top", "Top page margin.", FormatFloat(section.PageSettings.MarginTop), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.MarginTop = parsed;
                        OnModelChanged("Updated section margin.");
                    }));
                AddTextProperty("section.marginRight", "Margin Right", "Right page margin.", FormatFloat(section.PageSettings.MarginRight), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.MarginRight = parsed;
                        OnModelChanged("Updated section margin.");
                    }));
                AddTextProperty("section.marginBottom", "Margin Bottom", "Bottom page margin.", FormatFloat(section.PageSettings.MarginBottom), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.MarginBottom = parsed;
                        OnModelChanged("Updated section margin.");
                    }));
                AddTextProperty("section.columnCount", "Columns", "Number of columns.", section.PageSettings.ColumnCount.ToString(CultureInfo.InvariantCulture), false, value =>
                    TryApplyInt(value, parsed =>
                    {
                        section.PageSettings.ColumnCount = Math.Max(1, parsed);
                        OnModelChanged("Updated section columns.");
                    }));
                AddTextProperty("section.columnGap", "Column Gap", "Gap between columns.", FormatFloat(section.PageSettings.ColumnGap), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        section.PageSettings.ColumnGap = parsed;
                        OnModelChanged("Updated section column gap.");
                    }));
                break;
            case ReportParameterDefinition parameter:
                AddTextProperty("parameter.id", "Id", "Stable parameter identifier.", parameter.Id, false, value =>
                {
                    parameter.Id = NormalizeRequired(value, "Parameter id");
                    OnModelChanged("Updated parameter id.");
                    return null;
                });
                AddTextProperty("parameter.displayName", "Display Name", "Shown to users and designers.", parameter.DisplayName, false, value =>
                {
                    parameter.DisplayName = value.Trim();
                    OnModelChanged("Updated parameter display name.");
                    return null;
                });
                AddTextProperty("parameter.prompt", "Prompt", "Prompt shown in the preview pane.", parameter.Prompt ?? string.Empty, false, value =>
                {
                    parameter.Prompt = NormalizeOptional(value);
                    OnModelChanged("Updated parameter prompt.");
                    return null;
                });
                AddChoiceProperty("parameter.dataType", "Data Type", "Expected value type.", parameter.DataType.ToString(), CreateEnumOptions<ReportParameterDataType>(), value =>
                {
                    parameter.DataType = Enum.Parse<ReportParameterDataType>(value, ignoreCase: true);
                    OnModelChanged("Updated parameter data type.");
                });
                AddChoiceProperty("parameter.visibility", "Visibility", "Prompt visibility.", parameter.Visibility.ToString(), CreateEnumOptions<ReportParameterVisibility>(), value =>
                {
                    parameter.Visibility = Enum.Parse<ReportParameterVisibility>(value, ignoreCase: true);
                    OnModelChanged("Updated parameter visibility.");
                });
                AddBooleanProperty("parameter.multiValue", "Multi Value", "Allows multiple values.", parameter.IsMultiValue, value =>
                {
                    parameter.IsMultiValue = value;
                    OnModelChanged("Updated parameter cardinality.");
                });
                AddBooleanProperty("parameter.allowNull", "Allow Null", "Allows null values.", parameter.AllowNull, value =>
                {
                    parameter.AllowNull = value;
                    OnModelChanged("Updated parameter nullability.");
                });
                AddTextProperty("parameter.availableValuesDataSetId", "Values Dataset", "Dataset used for available values.", parameter.AvailableValuesDataSetId ?? string.Empty, false, value =>
                {
                    parameter.AvailableValuesDataSetId = NormalizeOptional(value);
                    OnModelChanged("Updated parameter available-values dataset.");
                    return null;
                });
                AddTextProperty("parameter.valueField", "Value Field", "Field used as the stored value.", parameter.ValueField ?? string.Empty, false, value =>
                {
                    parameter.ValueField = NormalizeOptional(value);
                    OnModelChanged("Updated parameter value field.");
                    return null;
                });
                AddTextProperty("parameter.labelField", "Label Field", "Field used as the display label.", parameter.LabelField ?? string.Empty, false, value =>
                {
                    parameter.LabelField = NormalizeOptional(value);
                    OnModelChanged("Updated parameter label field.");
                    return null;
                });
                break;
            case ReportDataSourceDefinition dataSource:
                AddTextProperty("dataSource.id", "Id", "Stable data-source identifier.", dataSource.Id, false, value =>
                {
                    dataSource.Id = NormalizeRequired(value, "Data source id");
                    OnModelChanged("Updated data source id.");
                    return null;
                });
                AddChoiceProperty("dataSource.providerId", "Provider", "Built-in data provider identifier.", dataSource.ProviderId, _providerOptions, value =>
                {
                    dataSource.ProviderId = value;
                    OnModelChanged("Updated data provider.");
                });
                AddTextProperty("dataSource.connectionName", "Connection Name", "Host-defined connection or logical source name.", dataSource.ConnectionName ?? string.Empty, false, value =>
                {
                    dataSource.ConnectionName = NormalizeOptional(value);
                    OnModelChanged("Updated connection name.");
                    return null;
                });
                AddChoiceProperty("dataSource.credentialMode", "Credential Mode", "How credentials are supplied.", dataSource.CredentialMode.ToString(), CreateEnumOptions<ReportCredentialMode>(), value =>
                {
                    dataSource.CredentialMode = Enum.Parse<ReportCredentialMode>(value, ignoreCase: true);
                    OnModelChanged("Updated credential mode.");
                });
                AddTextProperty("dataSource.timeoutSeconds", "Timeout", "Optional timeout in seconds.", dataSource.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, false, value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        dataSource.TimeoutSeconds = null;
                        OnModelChanged("Cleared data source timeout.");
                        return null;
                    }

                    return TryApplyInt(value, parsed =>
                    {
                        dataSource.TimeoutSeconds = parsed;
                        OnModelChanged("Updated data source timeout.");
                    });
                });
                AddTextProperty("dataSource.options", "Options", "One key=value pair per line.", SerializeDictionary(dataSource.Options), true, value =>
                {
                    if (!TryParseDictionary(value, out var options, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    dataSource.Options.Clear();
                    foreach (var pair in options)
                    {
                        dataSource.Options[pair.Key] = pair.Value;
                    }

                    OnModelChanged("Updated data source options.");
                    return null;
                });
                break;
            case ReportDataSetDefinition dataSet:
                AddTextProperty("dataSet.id", "Id", "Stable dataset identifier.", dataSet.Id, false, value =>
                {
                    dataSet.Id = NormalizeRequired(value, "Dataset id");
                    OnModelChanged("Updated dataset id.");
                    return null;
                });
                AddTextProperty("dataSet.dataSourceId", "Data Source", "Backing data-source identifier.", dataSet.DataSourceId, false, value =>
                {
                    dataSet.DataSourceId = NormalizeRequired(value, "Data source id");
                    OnModelChanged("Updated dataset source.");
                    return null;
                });
                AddTextProperty("dataSet.query", "Query", "Provider-specific query or path.", dataSet.Query, true, value =>
                {
                    dataSet.Query = value;
                    OnModelChanged("Updated dataset query.");
                    return null;
                });
                AddTextProperty("dataSet.fields", "Expected Fields", "One field per line as Name:Type.", SerializeExpectedFields(dataSet.ExpectedFields), true, value =>
                {
                    if (!TryParseExpectedFields(value, out var fields, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    dataSet.ExpectedFields = fields;
                    OnModelChanged("Updated dataset fields.");
                    return null;
                });
                break;
            case ReportSharedTemplateDefinition template:
                AddTextProperty("template.id", "Id", "Stable shared-template identifier.", template.Id, false, value =>
                {
                    template.Id = NormalizeRequired(value, "Template id");
                    OnModelChanged("Updated template id.");
                    return null;
                });
                AddChoiceProperty("template.format", "Format", "Embedded narrative format.", template.Format.ToString(), CreateEnumOptions<ReportDocumentTemplateFormat>(), value =>
                {
                    template.Format = Enum.Parse<ReportDocumentTemplateFormat>(value, ignoreCase: true);
                    OnModelChanged("Updated template format.");
                });
                AddBooleanProperty("template.isEmbedded", "Embedded", "Whether the content is embedded in the report.", template.IsEmbedded, value =>
                {
                    template.IsEmbedded = value;
                    OnModelChanged("Updated template storage mode.");
                });
                AddTextProperty("template.source", "Source", "Path or URI when not embedded.", template.Source ?? string.Empty, false, value =>
                {
                    template.Source = NormalizeOptional(value);
                    OnModelChanged("Updated template source.");
                    return null;
                });
                AddTextProperty("template.content", "Content", "Embedded markdown, HTML, DOCX marker, or Vibe fragment.", template.Content ?? string.Empty, true, value =>
                {
                    template.Content = NormalizeOptional(value);
                    OnModelChanged("Updated template content.");
                    return null;
                });
                break;
            case ReportItem item:
                BuildItemPropertyEntries(item);
                break;
        }
    }

    private void BuildItemPropertyEntries(ReportItem item)
    {
        AddTextProperty("item.id", "Id", "Stable report item identifier.", item.Id, false, value =>
        {
            item.Id = NormalizeRequired(value, "Item id");
            OnModelChanged("Updated item id.");
            return null;
        });
        AddTextProperty("item.name", "Name", "Display name shown in the explorer.", item.Name, false, value =>
        {
            item.Name = value.Trim();
            OnModelChanged("Updated item name.");
            return null;
        });
        AddTextProperty("item.styleName", "Style", "Optional named style.", item.StyleName ?? string.Empty, false, value =>
        {
            item.StyleName = NormalizeOptional(value);
            OnModelChanged("Updated item style.");
            return null;
        });
        AddTextProperty("item.x", "X", "Left position in DIPs.", FormatFloat(item.Bounds.X), false, value =>
            TryApplyFloat(value, parsed =>
            {
                item.Bounds = item.Bounds with { X = parsed };
                OnModelChanged("Updated item position.");
            }));
        AddTextProperty("item.y", "Y", "Top position in DIPs.", FormatFloat(item.Bounds.Y), false, value =>
            TryApplyFloat(value, parsed =>
            {
                item.Bounds = item.Bounds with { Y = parsed };
                OnModelChanged("Updated item position.");
            }));
        AddTextProperty("item.width", "Width", "Item width in DIPs.", FormatFloat(item.Bounds.Width), false, value =>
            TryApplyFloat(value, parsed =>
            {
                item.Bounds = item.Bounds with { Width = parsed };
                OnModelChanged("Updated item width.");
            }));
        AddTextProperty("item.height", "Height", "Item height in DIPs.", FormatFloat(item.Bounds.Height), false, value =>
            TryApplyFloat(value, parsed =>
            {
                item.Bounds = item.Bounds with { Height = parsed };
                OnModelChanged("Updated item height.");
            }));

        switch (item)
        {
            case TextItem textItem:
                AddTextProperty("text.staticText", "Static Text", "Rendered when no value expression is used.", textItem.StaticText ?? string.Empty, true, value =>
                {
                    textItem.StaticText = NormalizeOptional(value);
                    OnModelChanged("Updated text content.");
                    return null;
                });
                AddTextProperty("text.formatString", "Format String", "Optional format string.", textItem.FormatString ?? string.Empty, false, value =>
                {
                    textItem.FormatString = NormalizeOptional(value);
                    OnModelChanged("Updated text format string.");
                    return null;
                });
                AddBooleanProperty("text.canGrow", "Can Grow", "Allows the text box to grow vertically.", textItem.CanGrow, value =>
                {
                    textItem.CanGrow = value;
                    OnModelChanged("Updated text growth.");
                });
                AddBooleanProperty("text.canShrink", "Can Shrink", "Allows the text box to shrink vertically.", textItem.CanShrink, value =>
                {
                    textItem.CanShrink = value;
                    OnModelChanged("Updated text shrinking.");
                });
                break;
            case ImageItem imageItem:
                AddChoiceProperty("image.sourceKind", "Source Kind", "How the image source is resolved.", imageItem.SourceKind.ToString(), CreateEnumOptions<ReportImageSourceKind>(), value =>
                {
                    imageItem.SourceKind = Enum.Parse<ReportImageSourceKind>(value, ignoreCase: true);
                    OnModelChanged("Updated image source kind.");
                });
                AddTextProperty("image.mimeType", "MIME Type", "Optional MIME type.", imageItem.MimeType ?? string.Empty, false, value =>
                {
                    imageItem.MimeType = NormalizeOptional(value);
                    OnModelChanged("Updated image MIME type.");
                    return null;
                });
                AddChoiceProperty("image.sizingMode", "Sizing", "Image sizing behavior.", imageItem.SizingMode.ToString(), CreateEnumOptions<ReportSizingMode>(), value =>
                {
                    imageItem.SizingMode = Enum.Parse<ReportSizingMode>(value, ignoreCase: true);
                    OnModelChanged("Updated image sizing.");
                });
                break;
            case LineItem lineItem:
                AddTextProperty("line.x2", "X2", "Line end X coordinate.", FormatFloat(lineItem.X2), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        lineItem.X2 = parsed;
                        OnModelChanged("Updated line endpoint.");
                    }));
                AddTextProperty("line.y2", "Y2", "Line end Y coordinate.", FormatFloat(lineItem.Y2), false, value =>
                    TryApplyFloat(value, parsed =>
                    {
                        lineItem.Y2 = parsed;
                        OnModelChanged("Updated line endpoint.");
                    }));
                break;
            case ShapeItem shapeItem:
                AddChoiceProperty("shape.kind", "Shape", "Rendered shape kind.", shapeItem.Shape.ToString(), CreateEnumOptions<ReportShapeKind>(), value =>
                {
                    shapeItem.Shape = Enum.Parse<ReportShapeKind>(value, ignoreCase: true);
                    OnModelChanged("Updated shape kind.");
                });
                break;
            case ChartItem chartItem:
                AddTextProperty("chart.dataSetId", "Dataset", "Backing dataset identifier.", chartItem.DataSetId ?? string.Empty, false, value =>
                {
                    chartItem.DataSetId = NormalizeOptional(value);
                    OnModelChanged("Updated chart dataset.");
                    return null;
                });
                AddTextProperty("chart.series", "Series", "One line per series as Name|Value|Color.", SerializeChartSeries(chartItem.Series), true, value =>
                {
                    if (!TryParseChartSeries(value, out var series, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    chartItem.Series = series;
                    OnModelChanged("Updated chart series.");
                    return null;
                });
                break;
            case TablixItem tablixItem:
                AddTextProperty("tablix.dataSetId", "Dataset", "Backing dataset identifier.", tablixItem.DataSetId ?? string.Empty, false, value =>
                {
                    tablixItem.DataSetId = NormalizeOptional(value);
                    OnModelChanged("Updated tablix dataset.");
                    return null;
                });
                AddBooleanProperty("tablix.repeatHeaderRows", "Repeat Header", "Repeats header rows on every page.", tablixItem.RepeatHeaderRows, value =>
                {
                    tablixItem.RepeatHeaderRows = value;
                    OnModelChanged("Updated tablix repeat-header behavior.");
                });
                AddTextProperty("tablix.columns", "Columns", "One line per column as Id:Width.", SerializeColumns(tablixItem.Columns), true, value =>
                {
                    if (!TryParseColumns(value, out var columns, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    tablixItem.Columns = columns;
                    OnModelChanged("Updated tablix columns.");
                    return null;
                });
                AddTextProperty("tablix.rows", "Rows", "Use H: for headers and D: for detail rows. Cells are pipe-separated.", SerializeRows(tablixItem.Rows), true, value =>
                {
                    if (!TryParseRows(value, tablixItem.Rows, out var rows, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    tablixItem.Rows = rows;
                    OnModelChanged("Updated tablix rows.");
                    return null;
                });
                break;
            case SubreportItem subreportItem:
                AddTextProperty("subreport.reportReferenceId", "Report Id", "Referenced subreport identifier.", subreportItem.ReportReferenceId, false, value =>
                {
                    subreportItem.ReportReferenceId = NormalizeRequired(value, "Subreport id");
                    OnModelChanged("Updated subreport reference.");
                    return null;
                });
                AddTextProperty("subreport.parameters", "Parameters", "One binding per line as Parameter=Expression.", SerializeParameterBindings(subreportItem.Parameters), true, value =>
                {
                    if (!TryParseParameterBindings(value, out var parameters, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    subreportItem.Parameters = parameters;
                    OnModelChanged("Updated subreport parameters.");
                    return null;
                });
                break;
            case DocumentTemplateItem templateItem:
                AddTextProperty("documentTemplate.templateId", "Template Id", "Referenced shared template identifier.", templateItem.TemplateId ?? string.Empty, false, value =>
                {
                    templateItem.TemplateId = NormalizeOptional(value);
                    OnModelChanged("Updated template reference.");
                    return null;
                });
                AddChoiceProperty("documentTemplate.templateFormat", "Template Format", "Format used when embedded content is present.", templateItem.TemplateFormat.ToString(), CreateEnumOptions<ReportDocumentTemplateFormat>(), value =>
                {
                    templateItem.TemplateFormat = Enum.Parse<ReportDocumentTemplateFormat>(value, ignoreCase: true);
                    OnModelChanged("Updated template format.");
                });
                AddTextProperty("documentTemplate.embeddedContent", "Embedded Content", "Inline markdown, HTML, DOCX marker, or Vibe fragment.", templateItem.EmbeddedContent ?? string.Empty, true, value =>
                {
                    templateItem.EmbeddedContent = NormalizeOptional(value);
                    OnModelChanged("Updated embedded template content.");
                    return null;
                });
                AddTextProperty("documentTemplate.bindings", "Bindings", "One binding per line as Name=Expression.", SerializeDictionary(templateItem.Bindings), true, value =>
                {
                    if (!TryParseDictionary(value, out var bindings, out var errorMessage))
                    {
                        return errorMessage;
                    }

                    templateItem.Bindings.Clear();
                    foreach (var pair in bindings)
                    {
                        templateItem.Bindings[pair.Key] = pair.Value;
                    }

                    OnModelChanged("Updated template bindings.");
                    return null;
                });
                break;
        }
    }

    private void BuildExpressionEntries()
    {
        _expressionEntries.Clear();

        switch (_selectedTarget)
        {
            case ReportSection section:
                AddExpressionEntry("section.visibilityExpression", "Section Visibility", section.VisibilityExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        section.VisibilityExpression = normalized;
                    }, "Updated section visibility expression."));
                AddExpressionEntry("section.bookmarkExpression", "Section Bookmark", section.BookmarkExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        section.BookmarkExpression = normalized;
                    }, "Updated section bookmark expression."));
                break;
            case ReportParameterDefinition parameter:
                AddExpressionEntry("parameter.defaultValueExpression", "Default Value", parameter.DefaultValueExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        parameter.DefaultValueExpression = normalized;
                    }, "Updated parameter default expression."));
                break;
            case ReportDataSetDefinition dataSet:
                for (var index = 0; index < dataSet.Parameters.Count; index++)
                {
                    var parameterIndex = index;
                    var parameterDefinition = dataSet.Parameters[index];
                    AddExpressionEntry($"dataSet.parameter.{parameterIndex}", $"Dataset Parameter: {parameterDefinition.Name}", parameterDefinition.ValueExpression, value =>
                        TryApplyExpression(value, allowEmpty: false, "Dataset parameter expression is required.", normalized =>
                        {
                            parameterDefinition.ValueExpression = normalized!;
                        }, "Updated dataset parameter expression."));
                }

                for (var index = 0; index < dataSet.CalculatedFields.Count; index++)
                {
                    var field = dataSet.CalculatedFields[index];
                    AddExpressionEntry($"dataSet.calculated.{index}", $"Calculated Field: {field.Name}", field.Expression, value =>
                        TryApplyExpression(value, allowEmpty: false, "Calculated field expression is required.", normalized =>
                        {
                            field.Expression = normalized!;
                        }, "Updated calculated-field expression."));
                }

                for (var index = 0; index < dataSet.Filters.Count; index++)
                {
                    var filter = dataSet.Filters[index];
                    AddExpressionEntry($"dataSet.filter.expression.{index}", $"Filter {index + 1}: Left", filter.Expression, value =>
                        TryApplyExpression(value, allowEmpty: false, "Filter expression is required.", normalized =>
                        {
                            filter.Expression = normalized!;
                        }, "Updated filter expression."));
                    AddExpressionEntry($"dataSet.filter.value.{index}", $"Filter {index + 1}: Right", filter.ValueExpression, value =>
                        TryApplyExpression(value, allowEmpty: false, "Filter value expression is required.", normalized =>
                        {
                            filter.ValueExpression = normalized!;
                        }, "Updated filter value expression."));
                }

                for (var index = 0; index < dataSet.Sorts.Count; index++)
                {
                    var sort = dataSet.Sorts[index];
                    AddExpressionEntry($"dataSet.sort.{index}", $"Sort {index + 1}", sort.Expression, value =>
                        TryApplyExpression(value, allowEmpty: false, "Sort expression is required.", normalized =>
                        {
                            sort.Expression = normalized!;
                        }, "Updated sort expression."));
                }
                break;
            case ReportItem item:
                AddExpressionEntry("item.visibilityExpression", "Visibility", item.VisibilityExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        item.VisibilityExpression = normalized;
                    }, "Updated item visibility expression."));
                AddExpressionEntry("item.bookmarkExpression", "Bookmark", item.BookmarkExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        item.BookmarkExpression = normalized;
                    }, "Updated item bookmark expression."));
                AddExpressionEntry("item.tooltipExpression", "Tooltip", item.TooltipExpression, value =>
                    TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                    {
                        item.TooltipExpression = normalized;
                    }, "Updated item tooltip expression."));

                switch (item)
                {
                    case TextItem textItem:
                        AddExpressionEntry("text.valueExpression", "Value", textItem.ValueExpression, value =>
                            TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                            {
                                textItem.ValueExpression = normalized;
                            }, "Updated text value expression."));
                        break;
                    case ImageItem imageItem:
                        AddExpressionEntry("image.valueExpression", "Source", imageItem.ValueExpression, value =>
                            TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                            {
                                imageItem.ValueExpression = normalized;
                            }, "Updated image source expression."));
                        break;
                    case ChartItem chartItem:
                        AddExpressionEntry("chart.categoryExpression", "Category", chartItem.CategoryExpression, value =>
                            TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                            {
                                chartItem.CategoryExpression = normalized;
                            }, "Updated chart category expression."));
                        AddExpressionEntry("chart.titleExpression", "Title", chartItem.TitleExpression, value =>
                            TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                            {
                                chartItem.TitleExpression = normalized;
                            }, "Updated chart title expression."));
                        for (var index = 0; index < chartItem.Series.Count; index++)
                        {
                            var series = chartItem.Series[index];
                            AddExpressionEntry($"chart.series.name.{index}", $"Series {index + 1} Name", series.NameExpression, value =>
                                TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                                {
                                    series.NameExpression = normalized;
                                }, "Updated chart series expression."));
                            AddExpressionEntry($"chart.series.value.{index}", $"Series {index + 1} Value", series.ValueExpression, value =>
                                TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                                {
                                    series.ValueExpression = normalized;
                                }, "Updated chart series expression."));
                            AddExpressionEntry($"chart.series.color.{index}", $"Series {index + 1} Color", series.ColorExpression, value =>
                                TryApplyExpression(value, allowEmpty: true, "Expression is required.", normalized =>
                                {
                                    series.ColorExpression = normalized;
                                }, "Updated chart series expression."));
                        }
                        break;
                }

                if (item.DrillthroughAction is not null)
                {
                    for (var index = 0; index < item.DrillthroughAction.Parameters.Count; index++)
                    {
                        var parameterBinding = item.DrillthroughAction.Parameters[index];
                        AddExpressionEntry($"item.drillthrough.{index}", $"Drillthrough: {parameterBinding.ParameterId}", parameterBinding.ValueExpression, value =>
                            TryApplyExpression(value, allowEmpty: false, "Drillthrough parameter expression is required.", normalized =>
                            {
                                parameterBinding.ValueExpression = normalized!;
                            }, "Updated drillthrough parameter expression."));
                    }
                }
                break;
        }

        SelectedExpressionEntry = _expressionEntries.FirstOrDefault();
        ExpressionStatusMessage = _expressionEntries.Count == 0 ? "No expressions for the current selection." : null;
    }

    private void AddExpressionEntry(string id, string label, string? initialText, Func<string, string?> apply)
    {
        _expressionEntries.Add(new ReportDesignerExpressionEntryViewModel(
            id,
            label,
            initialText ?? string.Empty,
            apply));
    }

    private void AddTextProperty(string id, string label, string? description, string initialValue, bool isMultiline, Func<string, string?> apply)
    {
        _propertyEntries.Add(new ReportDesignerTextPropertyViewModel(id, label, description, initialValue, isMultiline, apply));
    }

    private void AddBooleanProperty(string id, string label, string? description, bool initialValue, Action<bool> apply)
    {
        _propertyEntries.Add(new ReportDesignerBooleanPropertyViewModel(id, label, description, initialValue, apply));
    }

    private void AddChoiceProperty(
        string id,
        string label,
        string? description,
        string initialValue,
        IEnumerable<ReportDesignerChoiceOptionViewModel> options,
        Action<string> apply)
    {
        _propertyEntries.Add(new ReportDesignerChoicePropertyViewModel(id, label, description, options, initialValue, apply));
    }

    private void RegisterItem(ReportItem item, ReportSection section, ReportDesignerExplorerNodeViewModel parentNode)
    {
        _itemSectionMap[item] = section;
        var node = CreateTargetNode(item);
        parentNode.Children.Add(node);
    }

    private ReportDesignerExplorerNodeViewModel CreateGroupNode(string label)
    {
        return new ReportDesignerExplorerNodeViewModel(label, "Group", null, SelectExplorerNode);
    }

    private ReportDesignerExplorerNodeViewModel CreateTargetNode(object target)
    {
        var description = DescribeTarget(target);
        var node = new ReportDesignerExplorerNodeViewModel(description.title, description.type, target, SelectExplorerNode);
        _objectExplorerMap[target] = node;
        return node;
    }

    private void RegisterSelectionEntry(
        object target,
        string title,
        string subtitle,
        ObservableCollection<ReportDesignerSelectionEntryViewModel> entries)
    {
        var entry = new ReportDesignerSelectionEntryViewModel(target, title, subtitle, SelectSelectionEntry);
        entries.Add(entry);
        _objectSelectionEntryMap[target] = entry;
    }

    private void SelectExplorerNode(ReportDesignerExplorerNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        SelectTarget(node.Target ?? ReportDefinition);
    }

    private void SelectCanvasItem(ReportDesignerCanvasItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        SelectTarget(item.Item);
    }

    private void SelectSelectionEntry(ReportDesignerSelectionEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SelectTarget(entry.Target);
    }

    private void SelectTarget(object? target)
    {
        _selectedTarget = target ?? ReportDefinition;
        _selectedSection = _selectedTarget switch
        {
            ReportSection section => section,
            ReportItem item when _itemSectionMap.TryGetValue(item, out var ownerSection) => ownerSection,
            _ => _selectedSection ?? ReportDefinition.Sections.FirstOrDefault()
        };

        if (_selectedSection is not null)
        {
            RebuildCanvasItems();
        }

        _suppressSelectionSynchronization = true;
        try
        {
            foreach (var node in _objectExplorerMap.Values)
            {
                node.IsSelected = ReferenceEquals(node.Target, _selectedTarget);
            }

            SelectedExplorerNode = _objectExplorerMap.TryGetValue(_selectedTarget, out var explorerNode)
                ? explorerNode
                : _objectExplorerMap.GetValueOrDefault(ReportDefinition);

            foreach (var canvasItem in _canvasItems)
            {
                canvasItem.IsSelected = ReferenceEquals(canvasItem.Item, _selectedTarget);
                canvasItem.Refresh();
            }

            SelectedCanvasItem = _selectedTarget is ReportItem selectedItem && _itemCanvasMap.TryGetValue(selectedItem, out var canvasSelection)
                ? canvasSelection
                : null;

            foreach (var selectionEntry in _objectSelectionEntryMap.Values)
            {
                selectionEntry.IsSelected = ReferenceEquals(selectionEntry.Target, _selectedTarget);
            }

            SelectedParameterEntry = GetSelectionEntry<ReportParameterDefinition>(_selectedTarget);
            SelectedDataSourceEntry = GetSelectionEntry<ReportDataSourceDefinition>(_selectedTarget);
            SelectedDataSetEntry = GetSelectionEntry<ReportDataSetDefinition>(_selectedTarget);
            SelectedSharedTemplateEntry = GetSelectionEntry<ReportSharedTemplateDefinition>(_selectedTarget);
        }
        finally
        {
            _suppressSelectionSynchronization = false;
        }

        RebuildPropertiesAndExpressions();
        StatusMessage = $"Selected {SelectedObjectType.ToLowerInvariant()}: {SelectedObjectTitle}.";
    }

    private ReportDesignerSelectionEntryViewModel? GetSelectionEntry<TTarget>(object? target)
        where TTarget : class
    {
        return target is TTarget && _objectSelectionEntryMap.TryGetValue(target, out var entry)
            ? entry
            : null;
    }

    private void RefreshLightweightViews()
    {
        foreach (var pair in _objectExplorerMap)
        {
            pair.Value.Label = DescribeTarget(pair.Key).title;
        }

        foreach (var entry in _parameterEntries)
        {
            if (entry.Target is ReportParameterDefinition parameter)
            {
                entry.Title = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName;
                entry.Subtitle = DescribeParameterEntry(parameter);
            }
        }

        foreach (var entry in _dataSourceEntries)
        {
            if (entry.Target is ReportDataSourceDefinition dataSource)
            {
                entry.Title = dataSource.Id;
                entry.Subtitle = DescribeDataSourceEntry(dataSource);
            }
        }

        foreach (var entry in _dataSetEntries)
        {
            if (entry.Target is ReportDataSetDefinition dataSet)
            {
                entry.Title = dataSet.Id;
                entry.Subtitle = DescribeDataSetEntry(dataSet);
            }
        }

        foreach (var entry in _sharedTemplateEntries)
        {
            if (entry.Target is ReportSharedTemplateDefinition template)
            {
                entry.Title = template.Id;
                entry.Subtitle = DescribeTemplateEntry(template);
            }
        }

        foreach (var canvasItem in _canvasItems)
        {
            canvasItem.Refresh();
        }

        this.RaisePropertyChanged(nameof(CurrentSectionName));
        this.RaisePropertyChanged(nameof(SurfaceWidth));
        this.RaisePropertyChanged(nameof(SurfaceHeight));
        this.RaisePropertyChanged(nameof(SurfaceDisplayText));
        this.RaisePropertyChanged(nameof(SelectedObjectTitle));
        this.RaisePropertyChanged(nameof(SelectedObjectType));
        this.RaisePropertyChanged(nameof(IsGroupingVisible));
        this.RaisePropertyChanged(nameof(GroupingStatusText));
    }

    private void OnModelChanged(string message)
    {
        MarkDirty(message);
        RefreshLightweightViews();
    }

    private void MarkDirty(string message)
    {
        IsPreviewDirty = true;
        StatusMessage = message + " Preview is out of date.";
    }

    private ReportViewerSource BuildPreviewSource()
    {
        var previewSource = new ReportViewerSource
        {
            ReportDefinition = ReportDefinition,
            ProviderRegistry = Source.ProviderRegistry,
            HostDataRegistry = Source.HostDataRegistry,
            LayoutSettings = Source.LayoutSettings.Clone(),
            PreviewDpi = Source.PreviewDpi,
            DefaultPrintSettings = Source.DefaultPrintSettings?.Clone(),
            Culture = Source.Culture,
            UiCulture = Source.UiCulture,
            TimeZone = Source.TimeZone
        };

        foreach (var pair in Source.ReferencedReports)
        {
            previewSource.ReferencedReports[pair.Key] = pair.Value;
        }

        foreach (var pair in Source.Globals)
        {
            previewSource.Globals[pair.Key] = pair.Value;
        }

        return previewSource;
    }

    private string EnsureDataSourceForDataSet()
    {
        if (ReportDefinition.DataSources.Count == 0)
        {
            AddDataSource();
        }

        return ReportDefinition.DataSources[0].Id;
    }

    private string EnsureDataSetForGallery()
    {
        if (ReportDefinition.DataSets.Count == 0)
        {
            AddDataSet();
        }

        return ReportDefinition.DataSets[0].Id;
    }

    private ReportSharedTemplateDefinition EnsureSharedTemplate(
        string prefix,
        ReportDocumentTemplateFormat format,
        string content)
    {
        var existing = ReportDefinition.SharedTemplates.FirstOrDefault(template =>
            string.Equals(template.Id, prefix, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var template = new ReportSharedTemplateDefinition
        {
            Id = CreateUniqueId(prefix, ReportDefinition.SharedTemplates.Select(static template => template.Id)),
            Format = format,
            IsEmbedded = true,
            Content = content
        };
        ReportDefinition.SharedTemplates.Add(template);
        return template;
    }

    private static TablixItem CreateDefaultTablix(string id, string dataSetId, ReportSection section)
    {
        var tablix = new TablixItem
        {
            Id = id,
            Name = "Tablix",
            DataSetId = dataSetId,
            RepeatHeaderRows = true,
            Bounds = new ReportItemBounds(36f, Math.Max(120f, section.BodyItems.Count * 96f + 36f), 480f, 180f)
        };
        tablix.Columns.Add(new ReportTablixColumnDefinition
        {
            Id = "col1",
            Width = 220f
        });
        tablix.Columns.Add(new ReportTablixColumnDefinition
        {
            Id = "col2",
            Width = 220f
        });
        tablix.Rows.Add(new ReportTablixRowDefinition
        {
            Id = "header",
            IsHeader = true,
            Cells =
            {
                new ReportTablixCellDefinition { Text = "Category" },
                new ReportTablixCellDefinition { Text = "Value" }
            }
        });
        tablix.Rows.Add(new ReportTablixRowDefinition
        {
            Id = "detail",
            Cells =
            {
                new ReportTablixCellDefinition { ValueExpression = "Fields.Category" },
                new ReportTablixCellDefinition { ValueExpression = "Fields.Value" }
            }
        });
        return tablix;
    }

    private static ReportItemBounds CreateNextBounds(ReportSection section, float width, float height)
    {
        var y = 36f;
        foreach (var item in section.BodyItems)
        {
            y = Math.Max(y, item.Bounds.Y + item.Bounds.Height + 24f);
        }

        return new ReportItemBounds(36f, y, width, height);
    }

    private IEnumerable<ReportItem> EnumerateItems()
    {
        foreach (var section in ReportDefinition.Sections)
        {
            foreach (var item in section.HeaderItems)
            {
                yield return item;
            }

            foreach (var item in section.BodyItems)
            {
                yield return item;
            }

            foreach (var item in section.FooterItems)
            {
                yield return item;
            }
        }
    }

    private IEnumerable<string> EnumerateItemIds()
    {
        return EnumerateItems().Select(static item => item.Id);
    }

    private static string CreateUniqueId(string prefix, IEnumerable<string> existingIds)
    {
        var used = new HashSet<string>(existingIds.Where(static value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(prefix))
        {
            return prefix;
        }

        var index = 1;
        while (used.Contains(prefix + index.ToString(CultureInfo.InvariantCulture)))
        {
            index++;
        }

        return prefix + index.ToString(CultureInfo.InvariantCulture);
    }

    private static (string title, string type) DescribeTarget(object? target)
    {
        return target switch
        {
            ReportDefinition reportDefinition => (string.IsNullOrWhiteSpace(reportDefinition.Name) ? "Report" : reportDefinition.Name, "Report"),
            ReportSection section => (string.IsNullOrWhiteSpace(section.Name) ? section.Id : section.Name, "Section"),
            ReportParameterDefinition parameter => (string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName, "Parameter"),
            ReportDataSourceDefinition dataSource => (dataSource.Id, "Data Source"),
            ReportDataSetDefinition dataSet => (dataSet.Id, "Data Set"),
            ReportSharedTemplateDefinition template => (template.Id, "Shared Template"),
            ReportItem item => (string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name, item.GetType().Name.Replace("Item", string.Empty, StringComparison.Ordinal)),
            _ => ("Nothing Selected", "Selection")
        };
    }

    private static string DescribeParameterEntry(ReportParameterDefinition parameter)
    {
        var prompt = string.IsNullOrWhiteSpace(parameter.Prompt)
            ? "No prompt"
            : parameter.Prompt!;
        return $"{parameter.DataType} · {parameter.Visibility} · {prompt}";
    }

    private static string DescribeDataSourceEntry(ReportDataSourceDefinition dataSource)
    {
        var connection = string.IsNullOrWhiteSpace(dataSource.ConnectionName)
            ? "Unbound connection"
            : dataSource.ConnectionName!;
        return $"{dataSource.ProviderId} · {connection}";
    }

    private static string DescribeDataSetEntry(ReportDataSetDefinition dataSet)
    {
        var fieldCount = dataSet.ExpectedFields.Count;
        var source = string.IsNullOrWhiteSpace(dataSet.DataSourceId)
            ? "No data source"
            : dataSet.DataSourceId!;
        return $"{source} · {fieldCount} field(s)";
    }

    private static string DescribeTemplateEntry(ReportSharedTemplateDefinition template)
    {
        var storage = template.IsEmbedded ? "Embedded" : "External";
        return $"{template.Format} · {storage}";
    }

    private string? ValidateExpressionOrNull(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var compilation = _expressionCompiler.Compile(expression);
        if (!compilation.HasErrors)
        {
            return null;
        }

        return compilation.Diagnostics.First(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error).Message;
    }

    private string? TryApplyExpression(
        string value,
        bool allowEmpty,
        string requiredMessage,
        Action<string?> assign,
        string successMessage)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (!allowEmpty)
            {
                return requiredMessage;
            }

            assign(null);
            OnModelChanged(successMessage);
            return null;
        }

        var errorMessage = ValidateExpressionOrNull(normalized);
        if (errorMessage is not null)
        {
            return errorMessage;
        }

        assign(normalized);
        OnModelChanged(successMessage);
        return null;
    }

    private static string NormalizeRequired(string value, string displayName)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? displayName.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase) : normalized;
    }

    private static string? NormalizeOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private string? TryApplyFloat(string value, Action<float> apply)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return "Enter a valid number.";
        }

        apply(parsed);
        return null;
    }

    private string? TryApplyInt(string value, Action<int> apply)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return "Enter a valid integer.";
        }

        apply(parsed);
        return null;
    }

    private static IReadOnlyList<ReportDesignerChoiceOptionViewModel> CreateEnumOptions<TEnum>()
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Select(static value => new ReportDesignerChoiceOptionViewModel(value.ToString(), value.ToString()))
            .ToArray();
    }

    private static IReadOnlyList<ReportDesignerChoiceOptionViewModel> CreateProviderOptions(
        IReadOnlyList<ReportDataConnectorDefinition> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        return connectors
            .Select(static connector => new ReportDesignerChoiceOptionViewModel(connector.ProviderId, connector.DisplayName))
            .ToArray();
    }

    private static string SerializeDictionary(IReadOnlyDictionary<string, string> dictionary)
    {
        return string.Join(Environment.NewLine, dictionary.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private static bool TryParseDictionary(
        string text,
        out Dictionary<string, string> dictionary,
        out string? errorMessage)
    {
        dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        errorMessage = null;
        foreach (var line in SplitLines(text))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                errorMessage = "Each line must use key=value.";
                return false;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = "Option keys cannot be empty.";
                return false;
            }

            dictionary[key] = value;
        }

        return true;
    }

    private static string SerializeExpectedFields(IReadOnlyList<ReportFieldDefinition> fields)
    {
        return string.Join(Environment.NewLine, fields.Select(static field => $"{field.Name}:{field.DataType}"));
    }

    private static bool TryParseExpectedFields(
        string text,
        out List<ReportFieldDefinition> fields,
        out string? errorMessage)
    {
        fields = new List<ReportFieldDefinition>();
        errorMessage = null;
        foreach (var line in SplitLines(text))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                errorMessage = "Fields must use Name:Type.";
                return false;
            }

            var name = line[..separatorIndex].Trim();
            var typeText = line[(separatorIndex + 1)..].Trim();
            if (!Enum.TryParse<ReportParameterDataType>(typeText, ignoreCase: true, out var dataType))
            {
                errorMessage = $"Unknown field type '{typeText}'.";
                return false;
            }

            fields.Add(new ReportFieldDefinition
            {
                Name = name,
                DataType = dataType
            });
        }

        return true;
    }

    private static string SerializeChartSeries(IReadOnlyList<ReportChartSeriesDefinition> series)
    {
        return string.Join(Environment.NewLine, series.Select(static item =>
            $"{item.NameExpression ?? string.Empty}|{item.ValueExpression ?? string.Empty}|{item.ColorExpression ?? string.Empty}"));
    }

    private static bool TryParseChartSeries(
        string text,
        out List<ReportChartSeriesDefinition> series,
        out string? errorMessage)
    {
        series = new List<ReportChartSeriesDefinition>();
        errorMessage = null;
        foreach (var line in SplitLines(text))
        {
            var parts = line.Split('|');
            if (parts.Length < 2)
            {
                errorMessage = "Series lines must use Name|Value|Color.";
                return false;
            }

            series.Add(new ReportChartSeriesDefinition
            {
                NameExpression = NormalizeOptional(parts[0]),
                ValueExpression = NormalizeOptional(parts[1]),
                ColorExpression = parts.Length > 2 ? NormalizeOptional(parts[2]) : null
            });
        }

        return true;
    }

    private static string SerializeColumns(IReadOnlyList<ReportTablixColumnDefinition> columns)
    {
        return string.Join(Environment.NewLine, columns.Select(static column => $"{column.Id}:{FormatFloat(column.Width)}"));
    }

    private static bool TryParseColumns(
        string text,
        out List<ReportTablixColumnDefinition> columns,
        out string? errorMessage)
    {
        columns = new List<ReportTablixColumnDefinition>();
        errorMessage = null;
        foreach (var line in SplitLines(text))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                errorMessage = "Columns must use Id:Width.";
                return false;
            }

            var id = line[..separatorIndex].Trim();
            var widthText = line[(separatorIndex + 1)..].Trim();
            if (!float.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
            {
                errorMessage = "Column width must be numeric.";
                return false;
            }

            columns.Add(new ReportTablixColumnDefinition
            {
                Id = id,
                Width = width
            });
        }

        return true;
    }

    private static string SerializeRows(IReadOnlyList<ReportTablixRowDefinition> rows)
    {
        return string.Join(Environment.NewLine, rows.Select(static row =>
            $"{(row.IsHeader ? "H" : "D")}:{string.Join("|", row.Cells.Select(static cell => cell.Text ?? cell.ValueExpression ?? string.Empty))}"));
    }

    private static bool TryParseRows(
        string text,
        IReadOnlyList<ReportTablixRowDefinition> existingRows,
        out List<ReportTablixRowDefinition> rows,
        out string? errorMessage)
    {
        rows = new List<ReportTablixRowDefinition>();
        errorMessage = null;
        var rowIndex = 1;
        foreach (var line in SplitLines(text))
        {
            if (line.Length < 3 || line[1] != ':')
            {
                errorMessage = "Rows must use H: or D: prefixes.";
                return false;
            }

            var isHeader = char.ToUpperInvariant(line[0]) switch
            {
                'H' => true,
                'D' => false,
                _ => throw new InvalidOperationException()
            };
            var content = line[2..];
            var existingRow = rowIndex - 1 < existingRows.Count ? existingRows[rowIndex - 1] : null;
            var row = new ReportTablixRowDefinition
            {
                Id = existingRow?.Id ?? ("row" + rowIndex.ToString(CultureInfo.InvariantCulture)),
                IsHeader = isHeader
            };
            var cellIndex = 0;
            foreach (var cellText in content.Split('|'))
            {
                var normalized = cellText.Trim();
                var existingCell = existingRow is not null && cellIndex < existingRow.Cells.Count
                    ? existingRow.Cells[cellIndex]
                    : null;
                row.Cells.Add(new ReportTablixCellDefinition
                {
                    Text = isHeader ? normalized : null,
                    ValueExpression = isHeader ? null : NormalizeOptional(normalized),
                    FormatString = existingCell?.FormatString,
                    StyleName = existingCell?.StyleName,
                    RowSpan = existingCell?.RowSpan ?? 1,
                    ColumnSpan = existingCell?.ColumnSpan ?? 1
                });
                cellIndex++;
            }

            rows.Add(row);
            rowIndex++;
        }

        return true;
    }

    private static string SerializeParameterBindings(IReadOnlyList<ReportParameterBinding> parameters)
    {
        return string.Join(Environment.NewLine, parameters.Select(static parameter => $"{parameter.ParameterId}={parameter.ValueExpression}"));
    }

    private static bool TryParseParameterBindings(
        string text,
        out List<ReportParameterBinding> parameters,
        out string? errorMessage)
    {
        parameters = new List<ReportParameterBinding>();
        errorMessage = null;
        foreach (var line in SplitLines(text))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                errorMessage = "Bindings must use Parameter=Expression.";
                return false;
            }

            parameters.Add(new ReportParameterBinding
            {
                ParameterId = line[..separatorIndex].Trim(),
                ValueExpression = line[(separatorIndex + 1)..].Trim()
            });
        }

        return true;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line));
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>, IEqualityComparer<ReportItem>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);

        bool IEqualityComparer<ReportItem>.Equals(ReportItem? x, ReportItem? y) => ReferenceEquals(x, y);

        int IEqualityComparer<ReportItem>.GetHashCode(ReportItem obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
