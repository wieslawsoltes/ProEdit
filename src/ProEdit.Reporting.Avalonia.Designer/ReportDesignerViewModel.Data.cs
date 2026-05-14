using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Text.RegularExpressions;
using ReactiveUI;
using ProEdit.Reporting.Avalonia.Viewer;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Expressions;

namespace ProEdit.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private static readonly Regex QueryParameterTokenRegex =
        new(@"(?<![\w])(?<marker>@|:|\$)(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private readonly ReportDesignerDataRuntimeService _dataRuntimeService = new(new ReportExpressionCompiler());
    private readonly ObservableCollection<ReportDesignerDataNodeViewModel> _reportDataNodes = new();
    private readonly Dictionary<object, ReportDesignerDataNodeViewModel> _dataTargetNodeMap = new(ReferenceEqualityComparer.Instance);
    private readonly ObservableCollection<ReportDesignerDataPreviewColumnViewModel> _dataPreviewColumns = new();
    private readonly ObservableCollection<ReportDesignerDataPreviewRowViewModel> _dataPreviewRows = new();
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _selectedDataSourceConnectionOptions = new();
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _selectedDataSourceSourceKeyOptions = new();
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _selectedDataSetSourceOptions = new();
    private readonly IReadOnlyList<ReportDesignerChoiceOptionViewModel> _credentialModeOptions =
        CreateEnumOptions<ReportCredentialMode>();

    private ReportDesignerDataNodeViewModel? _selectedDataNode;
    private ReportDesignerChoiceOptionViewModel? _selectedDataSetSourceOption;
    private ReportDesignerChoiceOptionViewModel? _selectedDataSourceConnectionOption;
    private ReportDesignerChoiceOptionViewModel? _selectedDataSourceSourceKeyOption;
    private ReportDesignerChoiceOptionViewModel? _selectedDataSourceProviderOption;
    private ReportDesignerChoiceOptionViewModel? _selectedDataSourceCredentialModeOption;
    private string _dataWorkspaceStatusMessage = "Select a data source or dataset to edit and preview live data.";
    private bool _isDataPreviewBusy;
    private bool _suppressDataWorkspaceEditorUpdates;
    private bool _suppressDataWorkspaceSelectionFromTarget;
    private string? _previewDataSetId;
    private string _selectedDataSourceConnectionName = string.Empty;
    private string _selectedDataSourceSourceKey = string.Empty;
    private string _selectedDataSourceConnectionString = string.Empty;
    private string _selectedDataSourceBaseAddress = string.Empty;
    private string _selectedDataSourceProviderInvariantName = string.Empty;
    private string _selectedDataSourceOptionsText = string.Empty;
    private string _selectedDataSourceTimeoutText = string.Empty;
    private string _selectedDataSetQueryText = string.Empty;
    private string _selectedDataSetFieldsText = string.Empty;
    private string _selectedDataSetCalculatedFieldsText = string.Empty;
    private string _selectedDataSetParametersText = string.Empty;
    private string _selectedDataSetFiltersText = string.Empty;
    private string _selectedDataSetSortsText = string.Empty;

    private readonly record struct DataNodeSelectionState(
        ReportDesignerDataNodeKind Kind,
        object? SelectionTarget,
        string? TargetIdentity);

    /// <summary>
    /// Gets the hierarchical Report Data workspace nodes.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerDataNodeViewModel> ReportDataNodes { get; private set; } = null!;

    /// <summary>
    /// Gets the live preview columns for the selected dataset.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerDataPreviewColumnViewModel> DataPreviewColumns { get; private set; } = null!;

    /// <summary>
    /// Gets the live preview rows for the selected dataset.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerDataPreviewRowViewModel> DataPreviewRows { get; private set; } = null!;

    /// <summary>
    /// Gets the filtered host connection options for the selected data source.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> SelectedDataSourceConnectionOptions { get; private set; } = null!;

    /// <summary>
    /// Gets the filtered host source-key options for the selected data source.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> SelectedDataSourceSourceKeyOptions { get; private set; } = null!;

    /// <summary>
    /// Gets the available report data-source options for the selected dataset.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> SelectedDataSetSourceOptions { get; private set; } = null!;

    /// <summary>
    /// Gets the available credential-mode options.
    /// </summary>
    public IReadOnlyList<ReportDesignerChoiceOptionViewModel> CredentialModeOptions => _credentialModeOptions;

    /// <summary>
    /// Gets the available data-source connector choices.
    /// </summary>
    public IReadOnlyList<ReportDesignerChoiceOptionViewModel> DataSourceProviderOptions => _providerOptions;

    /// <summary>
    /// Gets or sets the selected Report Data node.
    /// </summary>
    public ReportDesignerDataNodeViewModel? SelectedDataNode
    {
        get => _selectedDataNode;
        set
        {
            if (ReferenceEquals(_selectedDataNode, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataNode, value);

            foreach (var node in EnumerateDataNodes(_reportDataNodes))
            {
                node.IsSelected = ReferenceEquals(node, value);
            }

            if (value is null)
            {
                RefreshDataWorkspaceEditors();
                RaiseGroupingCapabilityPropertiesChanged();
                return;
            }

            var preserveTemplateSelection = SelectedInspectorTabIndex == 4 && HasSelectedTemplateItem;
            var preserveReportItemSelection = _selectedTarget is ReportItem or ReportDesignerTablixMemberSelectionTarget
                && value.Kind is ReportDesignerDataNodeKind.Parameter
                    or ReportDesignerDataNodeKind.DataSet
                    or ReportDesignerDataNodeKind.QueryField
                    or ReportDesignerDataNodeKind.CalculatedField
                    or ReportDesignerDataNodeKind.BuiltInField
                    or ReportDesignerDataNodeKind.ImageResource;
            if (!_suppressSelectionSynchronization && !preserveTemplateSelection && !preserveReportItemSelection)
            {
                _suppressDataWorkspaceSelectionFromTarget = true;
                try
                {
                    SelectTarget(value.SelectionTarget ?? value.Target ?? ReportDefinition);
                }
                finally
                {
                    _suppressDataWorkspaceSelectionFromTarget = false;
                }
            }

            if (!preserveTemplateSelection)
            {
                SelectedInspectorTabIndex = value.Kind == ReportDesignerDataNodeKind.Parameter ? 1 : 2;
            }

            RefreshDataWorkspaceEditors();
            RaiseGroupingCapabilityPropertiesChanged();
        }
    }

    /// <summary>
    /// Gets the current data-workspace status message.
    /// </summary>
    public string DataWorkspaceStatusMessage
    {
        get => _dataWorkspaceStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _dataWorkspaceStatusMessage, value ?? string.Empty);
    }

    /// <summary>
    /// Gets a value indicating whether the designer is previewing a dataset.
    /// </summary>
    public bool IsDataPreviewBusy
    {
        get => _isDataPreviewBusy;
        private set => this.RaiseAndSetIfChanged(ref _isDataPreviewBusy, value);
    }

    /// <summary>
    /// Gets a value indicating whether a data source is currently selected for editing.
    /// </summary>
    public bool HasSelectedDataSource => GetSelectedDataSourceForEditing() is not null;

    /// <summary>
    /// Gets a value indicating whether a dataset is currently selected for editing.
    /// </summary>
    public bool HasSelectedDataSet => GetSelectedDataSetForEditing() is not null;

    /// <summary>
    /// Gets a value indicating whether a field node is currently selected.
    /// </summary>
    public bool HasSelectedDataField => TryGetSelectedDataField(out _, out _, out _) is not null
        || TryGetSelectedBuiltInField(out _) is not null
        || TryGetSelectedImageResource(out _) is not null;

    /// <summary>
    /// Gets a value indicating whether the data workspace placeholder should be shown.
    /// </summary>
    public bool ShowDataWorkspacePlaceholder => !HasSelectedDataSource && !HasSelectedDataSet && !HasSelectedDataField;

    /// <summary>
    /// Gets the current data workspace title.
    /// </summary>
    public string SelectedDataWorkspaceTitle => SelectedDataNode?.Title ?? "Report Data";

    /// <summary>
    /// Gets the current data workspace subtitle.
    /// </summary>
    public string SelectedDataWorkspaceSubtitle => SelectedDataNode?.Subtitle ?? "Select a node to edit live data bindings.";

    /// <summary>
    /// Gets the selected field name.
    /// </summary>
    public string SelectedDataFieldName => TryGetSelectedDataField(out _, out var fieldName, out _)?.Title
        ?? fieldName
        ?? TryGetSelectedBuiltInField(out var builtInField)?.Label
        ?? TryGetSelectedImageResource(out var imageResource)?.Label
        ?? "No field selected";

    /// <summary>
    /// Gets the selected field type text.
    /// </summary>
    public string SelectedDataFieldType => TryGetSelectedDataField(out _, out _, out var dataType) is not null
        ? dataType.ToString()
        : TryGetSelectedBuiltInField(out _) is not null
            ? "Built-in Field"
            : TryGetSelectedImageResource(out _) is not null
                ? "Image Resource"
                : string.Empty;

    /// <summary>
    /// Gets the expression that will be inserted for the selected field.
    /// </summary>
    public string SelectedDataFieldExpression => TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is not null
        ? CreateReportFieldExpression(dataSet!.Id, fieldName!)
        : TryGetSelectedBuiltInField(out var builtInField) is not null
            ? builtInField!.Expression
            : TryGetSelectedImageResource(out var imageResource) is not null
                ? DescribeImageResourceExpression(imageResource!)
                : string.Empty;

    /// <summary>
    /// Gets a value indicating whether a live preview can run for the current dataset.
    /// </summary>
    public bool CanPreviewSelectedDataSet => GetSelectedDataSetForEditing() is not null && !IsDataPreviewBusy;

    /// <summary>
    /// Gets a value indicating whether the current dataset fields can be refreshed from live execution.
    /// </summary>
    public bool CanRefreshSelectedDataSetFields => CanPreviewSelectedDataSet;

    /// <summary>
    /// Gets a value indicating whether the selected field can be inserted into the current design selection.
    /// </summary>
    public bool CanInsertSelectedField => HasSelectedDataField;

    /// <summary>
    /// Gets a value indicating whether the selected dataset can bind to the current design selection.
    /// </summary>
    public bool CanBindSelectedDataSet => GetSelectedDataSetForEditing() is not null;

    /// <summary>
    /// Gets a value indicating whether the selected data source uses host or inline source keys.
    /// </summary>
    public bool ShowSelectedDataSourceSourceKey => GetSelectedDataSourceConnectorCategory() is ReportDataConnectorCategory.InMemory or ReportDataConnectorCategory.File;

    /// <summary>
    /// Gets a value indicating whether the selected data source uses connection names.
    /// </summary>
    public bool ShowSelectedDataSourceConnectionName => GetSelectedDataSourceConnectorCategory() is ReportDataConnectorCategory.Database or ReportDataConnectorCategory.Api;

    /// <summary>
    /// Gets a value indicating whether the selected data source supports inline connection strings.
    /// </summary>
    public bool ShowSelectedDataSourceConnectionString => GetSelectedDataSourceConnectorCategory() == ReportDataConnectorCategory.Database;

    /// <summary>
    /// Gets a value indicating whether the selected data source supports inline base addresses.
    /// </summary>
    public bool ShowSelectedDataSourceBaseAddress => GetSelectedDataSourceConnectorCategory() == ReportDataConnectorCategory.Api;

    /// <summary>
    /// Gets a value indicating whether the selected data source supports ADO.NET provider invariants.
    /// </summary>
    public bool ShowSelectedDataSourceProviderInvariant => GetSelectedDataSourceConnectorCategory() == ReportDataConnectorCategory.Database;

    /// <summary>
    /// Gets the preview summary for the selected dataset.
    /// </summary>
    public string DataPreviewSummary => _previewDataSetId is null
        ? "No live dataset preview loaded."
        : $"{DataPreviewRows.Count} row(s) · {DataPreviewColumns.Count} field(s)";

    /// <summary>
    /// Gets or sets the selected data-source provider option.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedDataSourceProviderOption
    {
        get => _selectedDataSourceProviderOption;
        set
        {
            if (ReferenceEquals(_selectedDataSourceProviderOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceProviderOption, value);
            if (_suppressDataWorkspaceEditorUpdates || value is null || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            dataSource.ProviderId = value.Value;
            UpdateDataSourceEditorOptions(dataSource);
            OnModelChanged("Updated data-source connector.");
            RebuildDataWorkspace();
            this.RaisePropertyChanged(nameof(ShowSelectedDataSourceSourceKey));
            this.RaisePropertyChanged(nameof(ShowSelectedDataSourceConnectionName));
            this.RaisePropertyChanged(nameof(ShowSelectedDataSourceConnectionString));
            this.RaisePropertyChanged(nameof(ShowSelectedDataSourceBaseAddress));
            this.RaisePropertyChanged(nameof(ShowSelectedDataSourceProviderInvariant));
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source credential mode.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedDataSourceCredentialModeOption
    {
        get => _selectedDataSourceCredentialModeOption;
        set
        {
            if (ReferenceEquals(_selectedDataSourceCredentialModeOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceCredentialModeOption, value);
            if (_suppressDataWorkspaceEditorUpdates || value is null || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            dataSource.CredentialMode = Enum.Parse<ReportCredentialMode>(value.Value, ignoreCase: true);
            OnModelChanged("Updated data-source credential mode.");
        }
    }

    /// <summary>
    /// Gets or sets the selected suggested connection option.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedDataSourceConnectionOption
    {
        get => _selectedDataSourceConnectionOption;
        set
        {
            if (ReferenceEquals(_selectedDataSourceConnectionOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceConnectionOption, value);
            if (!_suppressDataWorkspaceEditorUpdates && value is not null)
            {
                SelectedDataSourceConnectionName = value.Value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected suggested source-key option.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedDataSourceSourceKeyOption
    {
        get => _selectedDataSourceSourceKeyOption;
        set
        {
            if (ReferenceEquals(_selectedDataSourceSourceKeyOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceSourceKeyOption, value);
            if (!_suppressDataWorkspaceEditorUpdates && value is not null)
            {
                SelectedDataSourceSourceKey = value.Value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source connection name.
    /// </summary>
    public string SelectedDataSourceConnectionName
    {
        get => _selectedDataSourceConnectionName;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceConnectionName, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceConnectionName, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            dataSource.ConnectionName = NormalizeOptional(normalized);
            SetOrRemoveOption(dataSource.Options, "connectionName", normalized);
            OnModelChanged("Updated data-source connection name.");
            RefreshDataWorkspaceNodes();
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source source key.
    /// </summary>
    public string SelectedDataSourceSourceKey
    {
        get => _selectedDataSourceSourceKey;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceSourceKey, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceSourceKey, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            SetOrRemoveOption(dataSource.Options, "sourceKey", normalized);
            OnModelChanged("Updated data-source source key.");
            RefreshDataWorkspaceNodes();
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source inline connection string.
    /// </summary>
    public string SelectedDataSourceConnectionString
    {
        get => _selectedDataSourceConnectionString;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceConnectionString, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceConnectionString, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            SetOrRemoveOption(dataSource.Options, "connectionString", normalized);
            OnModelChanged("Updated inline connection string.");
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source base address.
    /// </summary>
    public string SelectedDataSourceBaseAddress
    {
        get => _selectedDataSourceBaseAddress;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceBaseAddress, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceBaseAddress, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            SetOrRemoveOption(dataSource.Options, "baseAddress", normalized);
            OnModelChanged("Updated data-source base address.");
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source provider invariant.
    /// </summary>
    public string SelectedDataSourceProviderInvariantName
    {
        get => _selectedDataSourceProviderInvariantName;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceProviderInvariantName, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceProviderInvariantName, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            SetOrRemoveOption(dataSource.Options, "providerInvariantName", normalized);
            OnModelChanged("Updated provider invariant.");
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source timeout text.
    /// </summary>
    public string SelectedDataSourceTimeoutText
    {
        get => _selectedDataSourceTimeoutText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceTimeoutText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceTimeoutText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                dataSource.TimeoutSeconds = null;
                OnModelChanged("Cleared data-source timeout.");
                return;
            }

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
            {
                dataSource.TimeoutSeconds = parsed;
                OnModelChanged("Updated data-source timeout.");
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected data-source extra options.
    /// </summary>
    public string SelectedDataSourceOptionsText
    {
        get => _selectedDataSourceOptionsText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSourceOptionsText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSourceOptionsText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSourceForEditing() is not { } dataSource)
            {
                return;
            }

            if (!TryParseDictionary(normalized, out var options, out _))
            {
                return;
            }

            ReplaceAdditionalDataSourceOptions(dataSource, options);
            OnModelChanged("Updated additional data-source options.");
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset data-source option.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedDataSetSourceOption
    {
        get => _selectedDataSetSourceOption;
        set
        {
            if (ReferenceEquals(_selectedDataSetSourceOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetSourceOption, value);
            if (_suppressDataWorkspaceEditorUpdates || value is null || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            dataSet.DataSourceId = value.Value;
            OnModelChanged("Updated dataset source.");
            RefreshDataWorkspaceNodes();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset query text.
    /// </summary>
    public string SelectedDataSetQueryText
    {
        get => _selectedDataSetQueryText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetQueryText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetQueryText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            dataSet.Query = normalized;
            EnsureReportParametersForQueryTokens(dataSet);
            OnModelChanged("Updated dataset query.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset expected-fields text.
    /// </summary>
    public string SelectedDataSetFieldsText
    {
        get => _selectedDataSetFieldsText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetFieldsText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetFieldsText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            if (!TryParseExpectedFields(normalized, out var fields, out _))
            {
                return;
            }

            dataSet.ExpectedFields = fields;
            OnModelChanged("Updated dataset fields.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset calculated-fields text.
    /// </summary>
    public string SelectedDataSetCalculatedFieldsText
    {
        get => _selectedDataSetCalculatedFieldsText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetCalculatedFieldsText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetCalculatedFieldsText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            if (!TryParseCalculatedFields(normalized, out var fields))
            {
                return;
            }

            dataSet.CalculatedFields = fields;
            OnModelChanged("Updated calculated fields.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset parameter text.
    /// </summary>
    public string SelectedDataSetParametersText
    {
        get => _selectedDataSetParametersText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetParametersText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetParametersText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            if (!TryParseDataSetParameters(normalized, out var parameters))
            {
                return;
            }

            dataSet.Parameters = parameters;
            OnModelChanged("Updated dataset parameters.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset filter text.
    /// </summary>
    public string SelectedDataSetFiltersText
    {
        get => _selectedDataSetFiltersText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetFiltersText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetFiltersText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            if (!TryParseDataSetFilters(normalized, out var filters))
            {
                return;
            }

            dataSet.Filters = filters;
            OnModelChanged("Updated dataset filters.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets or sets the selected dataset sort text.
    /// </summary>
    public string SelectedDataSetSortsText
    {
        get => _selectedDataSetSortsText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedDataSetSortsText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDataSetSortsText, normalized);
            if (_suppressDataWorkspaceEditorUpdates || GetSelectedDataSetForEditing() is not { } dataSet)
            {
                return;
            }

            if (!TryParseDataSetSorts(normalized, out var sorts))
            {
                return;
            }

            dataSet.Sorts = sorts;
            OnModelChanged("Updated dataset sorts.");
            RebuildDataWorkspace();
        }
    }

    /// <summary>
    /// Gets the command that previews the selected dataset.
    /// </summary>
    public ReactiveCommand<Unit, Unit> PreviewSelectedDataSetCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that refreshes the selected dataset fields from live data.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshSelectedDataSetFieldsCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that inserts the selected data field into the current design selection.
    /// </summary>
    public ReactiveCommand<Unit, Unit> InsertSelectedDataFieldCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that binds the selected dataset to the current design selection.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BindSelectedDataSetCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that adds one calculated field to the selected dataset.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddCalculatedFieldCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that adds one query parameter to the selected dataset.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddDataSetParameterCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that adds one dataset filter.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddDataFilterCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that adds one dataset sort.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddDataSortCommand { get; private set; } = null!;

    private void InitializeDataWorkspace()
    {
        ReportDataNodes = new ReadOnlyObservableCollection<ReportDesignerDataNodeViewModel>(_reportDataNodes);
        DataPreviewColumns = new ReadOnlyObservableCollection<ReportDesignerDataPreviewColumnViewModel>(_dataPreviewColumns);
        DataPreviewRows = new ReadOnlyObservableCollection<ReportDesignerDataPreviewRowViewModel>(_dataPreviewRows);
        SelectedDataSourceConnectionOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_selectedDataSourceConnectionOptions);
        SelectedDataSourceSourceKeyOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_selectedDataSourceSourceKeyOptions);
        SelectedDataSetSourceOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_selectedDataSetSourceOptions);

        PreviewSelectedDataSetCommand = DesignerCommandFactory.CreateFromTask(PreviewSelectedDataSetAsync);
        RefreshSelectedDataSetFieldsCommand = DesignerCommandFactory.CreateFromTask(RefreshSelectedDataSetFieldsAsync);
        InsertSelectedDataFieldCommand = DesignerCommandFactory.Create(InsertSelectedDataField);
        BindSelectedDataSetCommand = DesignerCommandFactory.Create(BindSelectedDataSet);
        AddCalculatedFieldCommand = DesignerCommandFactory.Create(AddCalculatedField);
        AddDataSetParameterCommand = DesignerCommandFactory.Create(AddDataSetParameter);
        AddDataFilterCommand = DesignerCommandFactory.Create(AddDataFilter);
        AddDataSortCommand = DesignerCommandFactory.Create(AddDataSort);
    }

    private void RebuildDataWorkspace()
    {
        HydrateMissingDataSetFieldsFromLocalSources();

        var selectionState = CaptureSelectedDataNodeState();

        _dataTargetNodeMap.Clear();
        _reportDataNodes.Clear();

        var parameterGroup = CreateDataGroupNode("Parameters", $"{ReportDefinition.Parameters.Count} item(s)");
        foreach (var parameter in ReportDefinition.Parameters)
        {
            var node = CreateDataTargetNode(
                ReportDesignerDataNodeKind.Parameter,
                string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName,
                DescribeParameterEntry(parameter),
                parameter,
                parameter);
            parameterGroup.AddChild(node);
            RegisterPrimaryDataNode(parameter, node);
        }

        var dataSourceGroup = CreateDataGroupNode("Data Sources", $"{ReportDefinition.DataSources.Count} source(s)");
        foreach (var dataSource in ReportDefinition.DataSources)
        {
            var node = CreateDataTargetNode(
                ReportDesignerDataNodeKind.DataSource,
                dataSource.Id,
                DescribeDataSourceEntry(dataSource),
                dataSource,
                dataSource);
            dataSourceGroup.AddChild(node);
            RegisterPrimaryDataNode(dataSource, node);
        }

        var dataSetGroup = CreateDataGroupNode("Datasets", $"{ReportDefinition.DataSets.Count} dataset(s)");
        foreach (var dataSet in ReportDefinition.DataSets)
        {
            var node = CreateDataTargetNode(
                ReportDesignerDataNodeKind.DataSet,
                dataSet.Id,
                DescribeWorkspaceDataSetEntry(dataSet),
                dataSet,
                dataSet);
            RegisterPrimaryDataNode(dataSet, node);
            dataSetGroup.AddChild(node);
            BuildDataSetChildNodes(node, dataSet);
        }

        var builtInFields = CreateBuiltInFieldDefinitions();
        var builtInFieldGroup = CreateDataGroupNode("Built-in Fields", $"{builtInFields.Count} field(s)");
        foreach (var builtInField in builtInFields)
        {
            builtInFieldGroup.AddChild(CreateDataTargetNode(
                ReportDesignerDataNodeKind.BuiltInField,
                builtInField.Label,
                builtInField.Description,
                builtInField,
                ReportDefinition));
        }

        var imageResources = EnumerateImageResourceDefinitions().ToList();
        var imagesGroup = CreateDataGroupNode("Images", imageResources.Count == 0 ? "No report images." : $"{imageResources.Count} image resource(s)");
        foreach (var imageResource in imageResources)
        {
            imagesGroup.AddChild(CreateDataTargetNode(
                ReportDesignerDataNodeKind.ImageResource,
                imageResource.Label,
                imageResource.Description,
                imageResource,
                ReportDefinition));
        }

        _reportDataNodes.Add(parameterGroup);
        _reportDataNodes.Add(dataSourceGroup);
        _reportDataNodes.Add(dataSetGroup);
        _reportDataNodes.Add(builtInFieldGroup);
        _reportDataNodes.Add(imagesGroup);

        RestoreSelectedDataNode(selectionState);
        this.RaisePropertyChanged(nameof(HasSelectedDataSource));
        this.RaisePropertyChanged(nameof(HasSelectedDataSet));
        this.RaisePropertyChanged(nameof(HasSelectedDataField));
        this.RaisePropertyChanged(nameof(ShowDataWorkspacePlaceholder));
    }

    private void HydrateMissingDataSetFieldsFromLocalSources()
    {
        for (var index = 0; index < ReportDefinition.DataSets.Count; index++)
        {
            var dataSet = ReportDefinition.DataSets[index];
            if (dataSet.ExpectedFields.Count > 0)
            {
                continue;
            }

            if (!_dataRuntimeService.TryHydrateLocalDataSetFields(Source, dataSet, out var fields)
                || fields.Count == 0)
            {
                continue;
            }

            dataSet.ExpectedFields = fields
                .Select(static field => new ReportFieldDefinition
                {
                    Name = field.Name,
                    DataType = field.DataType
                })
                .ToList();
        }
    }

    private void RefreshDataWorkspaceNodes()
    {
        RebuildDataWorkspace();
        RefreshDataWorkspaceEditors();
    }

    private void SyncDataWorkspaceSelectionFromTarget()
    {
        if (_suppressDataWorkspaceSelectionFromTarget)
        {
            return;
        }

        var target = _selectedTarget;
        var matchingNode = target is not null && _dataTargetNodeMap.TryGetValue(target, out var mappedNode)
            ? mappedNode
            : null;

        _suppressSelectionSynchronization = true;
        try
        {
            SelectedDataNode = matchingNode;
        }
        finally
        {
            _suppressSelectionSynchronization = false;
        }
    }

    private void RefreshDataWorkspaceEditors()
    {
        _suppressDataWorkspaceEditorUpdates = true;
        try
        {
            LoadSelectedDataSourceEditor();
            LoadSelectedDataSetEditor();
        }
        finally
        {
            _suppressDataWorkspaceEditorUpdates = false;
        }

        this.RaisePropertyChanged(nameof(SelectedDataWorkspaceTitle));
        this.RaisePropertyChanged(nameof(SelectedDataWorkspaceSubtitle));
        this.RaisePropertyChanged(nameof(SelectedDataFieldName));
        this.RaisePropertyChanged(nameof(SelectedDataFieldType));
        this.RaisePropertyChanged(nameof(SelectedDataFieldExpression));
        this.RaisePropertyChanged(nameof(CanPreviewSelectedDataSet));
        this.RaisePropertyChanged(nameof(CanRefreshSelectedDataSetFields));
        this.RaisePropertyChanged(nameof(CanInsertSelectedField));
        this.RaisePropertyChanged(nameof(CanBindSelectedDataSet));
        this.RaisePropertyChanged(nameof(HasSelectedDataSource));
        this.RaisePropertyChanged(nameof(HasSelectedDataSet));
        this.RaisePropertyChanged(nameof(HasSelectedDataField));
        this.RaisePropertyChanged(nameof(ShowDataWorkspacePlaceholder));
        this.RaisePropertyChanged(nameof(ShowSelectedDataSourceSourceKey));
        this.RaisePropertyChanged(nameof(ShowSelectedDataSourceConnectionName));
        this.RaisePropertyChanged(nameof(ShowSelectedDataSourceConnectionString));
        this.RaisePropertyChanged(nameof(ShowSelectedDataSourceBaseAddress));
        this.RaisePropertyChanged(nameof(ShowSelectedDataSourceProviderInvariant));

        var selectedDataSet = GetSelectedDataSetForEditing();
        if (selectedDataSet is null)
        {
            ClearDataPreview();
        }
        else if (!string.Equals(_previewDataSetId, selectedDataSet.Id, StringComparison.OrdinalIgnoreCase))
        {
            _ = PreviewSelectedDataSetAsync();
        }
    }

    private async Task PreviewSelectedDataSetAsync()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null || IsDataPreviewBusy)
        {
            return;
        }

        IsDataPreviewBusy = true;
        DataWorkspaceStatusMessage = $"Previewing dataset '{dataSet.Id}'...";
        this.RaisePropertyChanged(nameof(CanPreviewSelectedDataSet));
        this.RaisePropertyChanged(nameof(CanRefreshSelectedDataSetFields));

        try
        {
            var result = await _dataRuntimeService.PreviewDataSetAsync(BuildPreviewSource(), dataSet.Id);
            ApplyPreviewResult(dataSet, result, refreshExpectedFields: false);
        }
        finally
        {
            IsDataPreviewBusy = false;
            this.RaisePropertyChanged(nameof(CanPreviewSelectedDataSet));
            this.RaisePropertyChanged(nameof(CanRefreshSelectedDataSetFields));
        }
    }

    private async Task RefreshSelectedDataSetFieldsAsync()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null || IsDataPreviewBusy)
        {
            return;
        }

        IsDataPreviewBusy = true;
        DataWorkspaceStatusMessage = $"Refreshing fields for dataset '{dataSet.Id}'...";
        this.RaisePropertyChanged(nameof(CanPreviewSelectedDataSet));
        this.RaisePropertyChanged(nameof(CanRefreshSelectedDataSetFields));

        try
        {
            var result = await _dataRuntimeService.PreviewDataSetAsync(BuildPreviewSource(), dataSet.Id);
            ApplyPreviewResult(dataSet, result, refreshExpectedFields: true);
        }
        finally
        {
            IsDataPreviewBusy = false;
            this.RaisePropertyChanged(nameof(CanPreviewSelectedDataSet));
            this.RaisePropertyChanged(nameof(CanRefreshSelectedDataSetFields));
        }
    }

    private void InsertSelectedDataField()
    {
        if (TryGetSelectedBuiltInField(out var builtInField) is not null)
        {
            InsertBuiltInField(builtInField!);
            return;
        }

        if (TryGetSelectedImageResource(out var imageResource) is not null)
        {
            InsertImageResource(imageResource!);
            return;
        }

        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out var dataType) is null)
        {
            return;
        }

        switch (_selectedTarget)
        {
            case TextItem textItem:
                textItem.StaticText = null;
                textItem.ValueExpression = CreateReportFieldExpression(dataSet!.Id, fieldName!);
                SelectTarget(textItem);
                OnModelChanged($"Inserted field '{fieldName}' into text item.");
                break;
            case TablixItem tablixItem:
                tablixItem.DataSetId = dataSet!.Id;
                AppendFieldColumnToTablix(tablixItem, fieldName!);
                SelectTarget(tablixItem);
                OnModelChanged($"Inserted field '{fieldName}' into tablix.");
                break;
            case ChartItem chartItem:
                chartItem.DataSetId = dataSet!.Id;
                InsertFieldIntoChart(chartItem, dataSet, fieldName!, dataType);
                SelectTarget(chartItem);
                OnModelChanged($"Inserted field '{fieldName}' into chart.");
                break;
            case DocumentTemplateItem templateItem:
                ApplyTemplateBindingAndPlaceholder(templateItem, fieldName!, CreateReportFieldExpression(dataSet!.Id, fieldName!));
                SelectTarget(templateItem);
                OnModelChanged($"Bound field '{fieldName}' into template.");
                break;
            case ReportParameterDefinition parameter:
                parameter.AvailableValuesDataSetId = dataSet!.Id;
                parameter.ValueField = fieldName;
                parameter.LabelField ??= fieldName;
                SelectTarget(parameter);
                OnModelChanged($"Bound field '{fieldName}' to parameter values.");
                break;
            default:
                CreateTextItemForField(dataSet!, fieldName!);
                break;
        }
    }

    private void InsertBuiltInField(ReportDesignerBuiltInFieldDefinition builtInField)
    {
        switch (_selectedTarget)
        {
            case TextItem textItem:
                textItem.StaticText = null;
                textItem.ValueExpression = builtInField.Expression;
                SelectTarget(textItem);
                OnModelChanged($"Inserted built-in field '{builtInField.Label}' into text item.");
                break;
            case DocumentTemplateItem templateItem:
                ApplyTemplateBindingAndPlaceholder(templateItem, builtInField.Id, builtInField.Expression);
                SelectTarget(templateItem);
                OnModelChanged($"Bound built-in field '{builtInField.Label}' into template.");
                break;
            default:
                CreateTextItemForBuiltInField(builtInField);
                break;
        }
    }

    private void InsertImageResource(ReportDesignerImageResourceDefinition imageResource)
    {
        switch (_selectedTarget)
        {
            case ImageItem imageItem:
                ApplyImageResourceDefinition(imageItem, imageResource);
                SelectTarget(imageItem);
                OnModelChanged($"Updated image item from '{imageResource.Label}'.");
                break;
            default:
                CreateImageItemForResource(imageResource);
                break;
        }
    }

    private void BindSelectedDataSet()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null)
        {
            return;
        }

        switch (_selectedTarget)
        {
            case TablixItem tablixItem:
                tablixItem.DataSetId = dataSet.Id;
                ConfigureTablixFromDataSet(tablixItem, dataSet);
                SelectTarget(tablixItem);
                OnModelChanged($"Bound dataset '{dataSet.Id}' to tablix.");
                break;
            case ChartItem chartItem:
                chartItem.DataSetId = dataSet.Id;
                ConfigureChartFromDataSet(chartItem, dataSet);
                SelectTarget(chartItem);
                OnModelChanged($"Bound dataset '{dataSet.Id}' to chart.");
                break;
            case ReportParameterDefinition parameter:
                parameter.AvailableValuesDataSetId = dataSet.Id;
                parameter.ValueField = dataSet.ExpectedFields.FirstOrDefault()?.Name;
                parameter.LabelField = dataSet.ExpectedFields.FirstOrDefault()?.Name;
                SelectTarget(parameter);
                OnModelChanged($"Bound dataset '{dataSet.Id}' to parameter values.");
                break;
            default:
                var section = EnsureSelectedSection();
                var tablix = CreateDefaultTablix(
                    CreateUniqueId("tablix", EnumerateItemIds()),
                    dataSet.Id,
                    section);
                ConfigureTablixFromDataSet(tablix, dataSet);
                section.BodyItems.Add(tablix);
                RebuildDesignerState(tablix);
                SelectedCenterTabIndex = 0;
                MarkDirty($"Added tablix bound to dataset '{dataSet.Id}'.");
                break;
        }
    }

    private void AddCalculatedField()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null)
        {
            return;
        }

        var field = new ReportCalculatedFieldDefinition
        {
            Name = CreateUniqueId("calculated", dataSet.CalculatedFields.Select(static item => item.Name)),
            Expression = dataSet.ExpectedFields.Count > 0
                ? $"Fields.{dataSet.ExpectedFields[0].Name}"
                : "Fields.Value",
            DataType = dataSet.ExpectedFields.FirstOrDefault()?.DataType ?? ReportParameterDataType.String
        };
        dataSet.CalculatedFields.Add(field);
        RebuildDataWorkspace();
        RefreshDataWorkspaceEditors();
        OnModelChanged("Added calculated field.");
    }

    private void AddDataSetParameter()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null)
        {
            return;
        }

        var parameterId = CreateUniqueId("parameter", dataSet.Parameters.Select(static item => item.Name));
        dataSet.Parameters.Add(new ReportDataSetParameterDefinition
        {
            Name = parameterId,
            ValueExpression = $"Parameters.{parameterId}"
        });

        EnsureReportParameter(parameterId);
        RebuildDataWorkspace();
        RefreshDataWorkspaceEditors();
        OnModelChanged("Added dataset parameter.");
    }

    private void AddDataFilter()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null)
        {
            return;
        }

        var firstField = dataSet.ExpectedFields.FirstOrDefault()?.Name ?? "Value";
        var parameterId = CreateUniqueId("parameter", ReportDefinition.Parameters.Select(static item => item.Id));
        dataSet.Filters.Add(new ReportFilterDefinition
        {
            Expression = $"Fields.{firstField}",
            Operator = ReportFilterOperator.Equal,
            ValueExpression = $"Parameters.{parameterId}"
        });
        EnsureReportParameter(parameterId);
        RebuildDataWorkspace();
        RefreshDataWorkspaceEditors();
        OnModelChanged("Added dataset filter.");
    }

    private void AddDataSort()
    {
        var dataSet = GetSelectedDataSetForEditing();
        if (dataSet is null)
        {
            return;
        }

        var firstField = dataSet.ExpectedFields.FirstOrDefault()?.Name ?? "Value";
        dataSet.Sorts.Add(new ReportSortDefinition
        {
            Expression = $"Fields.{firstField}",
            Direction = ReportSortDirection.Ascending
        });
        RebuildDataWorkspace();
        RefreshDataWorkspaceEditors();
        OnModelChanged("Added dataset sort.");
    }

    private void LoadSelectedDataSourceEditor()
    {
        var dataSource = GetSelectedDataSourceForEditing();
        if (dataSource is null)
        {
            _selectedDataSourceProviderOption = null;
            _selectedDataSourceCredentialModeOption = null;
            _selectedDataSourceConnectionName = string.Empty;
            _selectedDataSourceSourceKey = string.Empty;
            _selectedDataSourceConnectionString = string.Empty;
            _selectedDataSourceBaseAddress = string.Empty;
            _selectedDataSourceProviderInvariantName = string.Empty;
            _selectedDataSourceOptionsText = string.Empty;
            _selectedDataSourceTimeoutText = string.Empty;
            _selectedDataSourceConnectionOption = null;
            _selectedDataSourceSourceKeyOption = null;
            _selectedDataSourceConnectionOptions.Clear();
            _selectedDataSourceSourceKeyOptions.Clear();
            return;
        }

        _selectedDataSourceProviderOption = _providerOptions.FirstOrDefault(option =>
            string.Equals(option.Value, dataSource.ProviderId, StringComparison.OrdinalIgnoreCase));
        _selectedDataSourceCredentialModeOption = _credentialModeOptions.FirstOrDefault(option =>
            string.Equals(option.Value, dataSource.CredentialMode.ToString(), StringComparison.OrdinalIgnoreCase));
        _selectedDataSourceConnectionName = dataSource.ConnectionName ?? GetDataSourceOption(dataSource, "connectionName") ?? string.Empty;
        _selectedDataSourceSourceKey = GetDataSourceOption(dataSource, "sourceKey") ?? string.Empty;
        _selectedDataSourceConnectionString = GetDataSourceOption(dataSource, "connectionString") ?? string.Empty;
        _selectedDataSourceBaseAddress = GetDataSourceOption(dataSource, "baseAddress") ?? string.Empty;
        _selectedDataSourceProviderInvariantName = GetDataSourceOption(dataSource, "providerInvariantName") ?? string.Empty;
        _selectedDataSourceOptionsText = SerializeAdditionalDataSourceOptions(dataSource);
        _selectedDataSourceTimeoutText = dataSource.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        UpdateDataSourceEditorOptions(dataSource);
        this.RaisePropertyChanged(nameof(SelectedDataSourceProviderOption));
        this.RaisePropertyChanged(nameof(SelectedDataSourceCredentialModeOption));
        this.RaisePropertyChanged(nameof(SelectedDataSourceConnectionName));
        this.RaisePropertyChanged(nameof(SelectedDataSourceSourceKey));
        this.RaisePropertyChanged(nameof(SelectedDataSourceConnectionString));
        this.RaisePropertyChanged(nameof(SelectedDataSourceBaseAddress));
        this.RaisePropertyChanged(nameof(SelectedDataSourceProviderInvariantName));
        this.RaisePropertyChanged(nameof(SelectedDataSourceOptionsText));
        this.RaisePropertyChanged(nameof(SelectedDataSourceTimeoutText));
        this.RaisePropertyChanged(nameof(SelectedDataSourceConnectionOption));
        this.RaisePropertyChanged(nameof(SelectedDataSourceSourceKeyOption));
    }

    private void LoadSelectedDataSetEditor()
    {
        var dataSet = GetSelectedDataSetForEditing();
        _selectedDataSetSourceOptions.Clear();
        foreach (var dataSource in ReportDefinition.DataSources)
        {
            _selectedDataSetSourceOptions.Add(new ReportDesignerChoiceOptionViewModel(dataSource.Id, dataSource.Id));
        }

        if (dataSet is null)
        {
            _selectedDataSetSourceOption = null;
            _selectedDataSetQueryText = string.Empty;
            _selectedDataSetFieldsText = string.Empty;
            _selectedDataSetCalculatedFieldsText = string.Empty;
            _selectedDataSetParametersText = string.Empty;
            _selectedDataSetFiltersText = string.Empty;
            _selectedDataSetSortsText = string.Empty;
            this.RaisePropertyChanged(nameof(SelectedDataSetSourceOption));
            this.RaisePropertyChanged(nameof(SelectedDataSetQueryText));
            this.RaisePropertyChanged(nameof(SelectedDataSetFieldsText));
            this.RaisePropertyChanged(nameof(SelectedDataSetCalculatedFieldsText));
            this.RaisePropertyChanged(nameof(SelectedDataSetParametersText));
            this.RaisePropertyChanged(nameof(SelectedDataSetFiltersText));
            this.RaisePropertyChanged(nameof(SelectedDataSetSortsText));
            return;
        }

        _selectedDataSetSourceOption = _selectedDataSetSourceOptions.FirstOrDefault(option =>
            string.Equals(option.Value, dataSet.DataSourceId, StringComparison.OrdinalIgnoreCase));
        _selectedDataSetQueryText = dataSet.Query ?? string.Empty;
        _selectedDataSetFieldsText = SerializeExpectedFields(dataSet.ExpectedFields);
        _selectedDataSetCalculatedFieldsText = SerializeCalculatedFields(dataSet.CalculatedFields);
        _selectedDataSetParametersText = SerializeDataSetParameters(dataSet.Parameters);
        _selectedDataSetFiltersText = SerializeDataSetFilters(dataSet.Filters);
        _selectedDataSetSortsText = SerializeDataSetSorts(dataSet.Sorts);

        this.RaisePropertyChanged(nameof(SelectedDataSetSourceOption));
        this.RaisePropertyChanged(nameof(SelectedDataSetQueryText));
        this.RaisePropertyChanged(nameof(SelectedDataSetFieldsText));
        this.RaisePropertyChanged(nameof(SelectedDataSetCalculatedFieldsText));
        this.RaisePropertyChanged(nameof(SelectedDataSetParametersText));
        this.RaisePropertyChanged(nameof(SelectedDataSetFiltersText));
        this.RaisePropertyChanged(nameof(SelectedDataSetSortsText));
    }

    private void ApplyPreviewResult(
        ReportDataSetDefinition dataSet,
        ReportDesignerDataPreviewResult result,
        bool refreshExpectedFields)
    {
        _dataPreviewColumns.Clear();
        _dataPreviewRows.Clear();

        if (refreshExpectedFields && result.DataSet is not null)
        {
            var calculatedFieldNames = new HashSet<string>(
                dataSet.CalculatedFields.Select(static field => field.Name),
                StringComparer.OrdinalIgnoreCase);
            dataSet.ExpectedFields = result.DataSet.Fields
                .Where(field => !calculatedFieldNames.Contains(field.Name))
                .Select(field => new ReportFieldDefinition
                {
                    Name = field.Name,
                    DataType = field.DataType
                })
                .ToList();
            RebuildDataWorkspace();
            LoadSelectedDataSetEditor();
        }

        if (result.DataSet is not null)
        {
            _previewDataSetId = dataSet.Id;
            foreach (var field in result.DataSet.Fields)
            {
                _dataPreviewColumns.Add(new ReportDesignerDataPreviewColumnViewModel(field.Name, field.DataType.ToString()));
            }

            var culture = Source.Culture ?? CultureInfo.InvariantCulture;
            for (var rowIndex = 0; rowIndex < Math.Min(12, result.DataSet.Rows.Count); rowIndex++)
            {
                var row = result.DataSet.Rows[rowIndex];
                _dataPreviewRows.Add(new ReportDesignerDataPreviewRowViewModel(
                    result.DataSet.Fields.Select(field =>
                        new ReportDesignerDataPreviewCellViewModel(FormatPreviewValue(row.Values.GetValueOrDefault(field.Name), culture)))));
            }

            var diagnosticText = result.Diagnostics.Count == 0
                ? $"Previewed dataset '{dataSet.Id}' successfully."
                : result.Diagnostics[0].Message;
            DataWorkspaceStatusMessage = diagnosticText;
        }
        else
        {
            _previewDataSetId = null;
            DataWorkspaceStatusMessage = result.Diagnostics.FirstOrDefault()?.Message
                ?? $"Preview for dataset '{dataSet.Id}' returned no rows.";
        }

        this.RaisePropertyChanged(nameof(DataPreviewSummary));
    }

    private void ClearDataPreview()
    {
        _previewDataSetId = null;
        _dataPreviewColumns.Clear();
        _dataPreviewRows.Clear();
        this.RaisePropertyChanged(nameof(DataPreviewSummary));
    }

    private void UpdateDataSourceEditorOptions(ReportDataSourceDefinition dataSource)
    {
        _selectedDataSourceConnectionOptions.Clear();
        foreach (var option in BuildConnectionOptions(dataSource))
        {
            _selectedDataSourceConnectionOptions.Add(option);
        }

        _selectedDataSourceSourceKeyOptions.Clear();
        foreach (var option in BuildSourceKeyOptions(dataSource))
        {
            _selectedDataSourceSourceKeyOptions.Add(option);
        }

        _selectedDataSourceConnectionOption = _selectedDataSourceConnectionOptions.FirstOrDefault(option =>
            string.Equals(option.Value, _selectedDataSourceConnectionName, StringComparison.OrdinalIgnoreCase));
        _selectedDataSourceSourceKeyOption = _selectedDataSourceSourceKeyOptions.FirstOrDefault(option =>
            string.Equals(option.Value, _selectedDataSourceSourceKey, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ReportDesignerChoiceOptionViewModel> BuildConnectionOptions(ReportDataSourceDefinition dataSource)
    {
        var category = GetConnectorCategory(dataSource.ProviderId);
        if (category is not ReportDataConnectorCategory.Database and not ReportDataConnectorCategory.Api)
        {
            return Array.Empty<ReportDesignerChoiceOptionViewModel>();
        }

        return Source.HostDataRegistry.ListConnections()
            .Where(connection => string.IsNullOrWhiteSpace(connection.ProviderId)
                || string.Equals(connection.ProviderId, dataSource.ProviderId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(dataSource.ProviderId, ReportProviderIds.Sql, StringComparison.OrdinalIgnoreCase))
            .Select(connection => new ReportDesignerChoiceOptionViewModel(
                connection.Name,
                string.IsNullOrWhiteSpace(connection.DisplayName)
                    ? connection.Name
                    : $"{connection.DisplayName} ({connection.Name})"))
            .ToArray();
    }

    private IReadOnlyList<ReportDesignerChoiceOptionViewModel> BuildSourceKeyOptions(ReportDataSourceDefinition dataSource)
    {
        IReadOnlyList<string> keys = dataSource.ProviderId switch
        {
            ReportProviderIds.InMemory => Source.HostDataRegistry.ListInMemorySourceKeys(),
            ReportProviderIds.Json => Source.HostDataRegistry.ListJsonSourceKeys(),
            ReportProviderIds.Csv => Source.HostDataRegistry.ListCsvSourceKeys(),
            ReportProviderIds.Sql => Source.HostDataRegistry.ListSqlConnectorKeys(),
            _ => Array.Empty<string>()
        };

        return keys
            .Select(static key => new ReportDesignerChoiceOptionViewModel(key, key))
            .ToArray();
    }

    private void BuildDataSetChildNodes(ReportDesignerDataNodeViewModel parentNode, ReportDataSetDefinition dataSet)
    {
        if (dataSet.ExpectedFields.Count > 0)
        {
            var fieldsGroup = CreateDataGroupNode("Fields", $"{dataSet.ExpectedFields.Count} query field(s)");
            foreach (var field in dataSet.ExpectedFields)
            {
                fieldsGroup.AddChild(CreateDataTargetNode(
                    ReportDesignerDataNodeKind.QueryField,
                    field.Name,
                    $"{field.DataType} · Query field",
                    field,
                    dataSet));
            }

            parentNode.AddChild(fieldsGroup);
        }

        if (dataSet.CalculatedFields.Count > 0)
        {
            var calculatedGroup = CreateDataGroupNode("Calculated Fields", $"{dataSet.CalculatedFields.Count} calculated field(s)");
            foreach (var field in dataSet.CalculatedFields)
            {
                calculatedGroup.AddChild(CreateDataTargetNode(
                    ReportDesignerDataNodeKind.CalculatedField,
                    field.Name,
                    $"{field.DataType} · {TrimForSubtitle(field.Expression)}",
                    field,
                    dataSet));
            }

            parentNode.AddChild(calculatedGroup);
        }

        if (dataSet.Parameters.Count > 0)
        {
            var parameterGroup = CreateDataGroupNode("Query Parameters", $"{dataSet.Parameters.Count} parameter(s)");
            foreach (var parameter in dataSet.Parameters)
            {
                parameterGroup.AddChild(CreateDataTargetNode(
                    ReportDesignerDataNodeKind.DataSetParameter,
                    parameter.Name,
                    TrimForSubtitle(parameter.ValueExpression),
                    parameter,
                    dataSet));
            }

            parentNode.AddChild(parameterGroup);
        }

        if (dataSet.Filters.Count > 0)
        {
            var filterGroup = CreateDataGroupNode("Filters", $"{dataSet.Filters.Count} filter(s)");
            for (var index = 0; index < dataSet.Filters.Count; index++)
            {
                var filter = dataSet.Filters[index];
                filterGroup.AddChild(CreateDataTargetNode(
                    ReportDesignerDataNodeKind.Filter,
                    $"Filter {index + 1}",
                    $"{TrimForSubtitle(filter.Expression)} {DescribeOperator(filter.Operator)} {TrimForSubtitle(filter.ValueExpression)}",
                    filter,
                    dataSet));
            }

            parentNode.AddChild(filterGroup);
        }

        if (dataSet.Sorts.Count > 0)
        {
            var sortGroup = CreateDataGroupNode("Sorts", $"{dataSet.Sorts.Count} sort(s)");
            for (var index = 0; index < dataSet.Sorts.Count; index++)
            {
                var sort = dataSet.Sorts[index];
                sortGroup.AddChild(CreateDataTargetNode(
                    ReportDesignerDataNodeKind.Sort,
                    $"Sort {index + 1}",
                    $"{TrimForSubtitle(sort.Expression)} · {sort.Direction}",
                    sort,
                    dataSet));
            }

            parentNode.AddChild(sortGroup);
        }
    }

    private IReadOnlyList<ReportDesignerBuiltInFieldDefinition> CreateBuiltInFieldDefinitions()
    {
        return
        [
            new ReportDesignerBuiltInFieldDefinition
            {
                Id = "ReportName",
                Label = "Report Name",
                Expression = "Globals.ReportName",
                Description = "Current report name."
            },
            new ReportDesignerBuiltInFieldDefinition
            {
                Id = "ExecutionTime",
                Label = "Execution Time",
                Expression = "Globals.ExecutionTime",
                Description = "Time when the report execution started."
            },
            new ReportDesignerBuiltInFieldDefinition
            {
                Id = "PageNumber",
                Label = "Page Number",
                Expression = "Globals.PageNumber",
                Description = "Current rendered page number."
            },
            new ReportDesignerBuiltInFieldDefinition
            {
                Id = "TotalPages",
                Label = "Total Pages",
                Expression = "Globals.TotalPages",
                Description = "Total number of rendered pages."
            }
        ];
    }

    private IEnumerable<ReportDesignerImageResourceDefinition> EnumerateImageResourceDefinitions()
    {
        foreach (var imageItem in EnumerateItems().OfType<ImageItem>())
        {
            var label = string.IsNullOrWhiteSpace(imageItem.Name) ? imageItem.Id : imageItem.Name;
            yield return new ReportDesignerImageResourceDefinition
            {
                Id = imageItem.Id,
                Label = label,
                SourceKind = imageItem.SourceKind,
                ValueExpression = imageItem.ValueExpression,
                MimeType = imageItem.MimeType,
                EmbeddedData = imageItem.EmbeddedData,
                Description = DescribeImageResource(imageItem)
            };
        }
    }

    private static string DescribeImageResource(ImageItem imageItem)
    {
        return imageItem.SourceKind switch
        {
            ReportImageSourceKind.Embedded => string.IsNullOrWhiteSpace(imageItem.MimeType)
                ? "Embedded report image."
                : $"Embedded {imageItem.MimeType} image.",
            ReportImageSourceKind.Uri => string.IsNullOrWhiteSpace(imageItem.ValueExpression)
                ? "URI-backed report image."
                : $"URI image · {TrimForSubtitle(imageItem.ValueExpression)}",
            _ => string.IsNullOrWhiteSpace(imageItem.ValueExpression)
                ? "Expression-backed report image."
                : $"Expression image · {TrimForSubtitle(imageItem.ValueExpression)}"
        };
    }

    private static ReportDesignerDataNodeViewModel CreateDataGroupNode(string title, string subtitle)
    {
        return new ReportDesignerDataNodeViewModel(
            ReportDesignerDataNodeKind.Group,
            title,
            subtitle,
            target: null,
            selectionTarget: null)
        {
            IsExpanded = true
        };
    }

    private static ReportDesignerDataNodeViewModel CreateDataTargetNode(
        ReportDesignerDataNodeKind kind,
        string title,
        string subtitle,
        object? target,
        object? selectionTarget)
    {
        return new ReportDesignerDataNodeViewModel(kind, title, subtitle, target, selectionTarget);
    }

    private void RegisterPrimaryDataNode(object target, ReportDesignerDataNodeViewModel node)
    {
        _dataTargetNodeMap[target] = node;
    }

    private DataNodeSelectionState CaptureSelectedDataNodeState()
    {
        if (SelectedDataNode is null)
        {
            return new DataNodeSelectionState(ReportDesignerDataNodeKind.Group, null, null);
        }

        return new DataNodeSelectionState(
            SelectedDataNode.Kind,
            SelectedDataNode.SelectionTarget,
            DescribeDataNodeIdentity(SelectedDataNode));
    }

    private void RestoreSelectedDataNode(DataNodeSelectionState selectionState)
    {
        ReportDesignerDataNodeViewModel? matchedNode = null;
        foreach (var node in EnumerateDataNodes(_reportDataNodes))
        {
            if (node.Kind != selectionState.Kind)
            {
                continue;
            }

            if (!ReferenceEquals(node.SelectionTarget, selectionState.SelectionTarget))
            {
                continue;
            }

            if (string.Equals(DescribeDataNodeIdentity(node), selectionState.TargetIdentity, StringComparison.OrdinalIgnoreCase))
            {
                matchedNode = node;
                break;
            }
        }

        if (matchedNode is null
            && selectionState.SelectionTarget is not null
            && _dataTargetNodeMap.TryGetValue(selectionState.SelectionTarget, out var primaryNode))
        {
            matchedNode = primaryNode;
        }

        _suppressSelectionSynchronization = true;
        try
        {
            SelectedDataNode = matchedNode;
        }
        finally
        {
            _suppressSelectionSynchronization = false;
        }
    }

    private static IEnumerable<ReportDesignerDataNodeViewModel> EnumerateDataNodes(IEnumerable<ReportDesignerDataNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateDataNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string? DescribeDataNodeIdentity(ReportDesignerDataNodeViewModel node)
    {
        return node.Target switch
        {
            ReportFieldDefinition field => field.Name,
            ReportCalculatedFieldDefinition field => field.Name,
            ReportDesignerBuiltInFieldDefinition builtInField => builtInField.Id,
            ReportDesignerImageResourceDefinition imageResource => imageResource.Id,
            ReportDataSetParameterDefinition parameter => parameter.Name,
            ReportFilterDefinition filter => filter.Expression,
            ReportSortDefinition sort => sort.Expression,
            ReportParameterDefinition parameter => parameter.Id,
            ReportDataSourceDefinition source => source.Id,
            ReportDataSetDefinition dataSet => dataSet.Id,
            _ => node.Title
        };
    }

    private ReportDataSourceDefinition? GetSelectedDataSourceForEditing()
    {
        return SelectedDataNode?.Target as ReportDataSourceDefinition
            ?? SelectedDataNode?.SelectionTarget as ReportDataSourceDefinition
            ?? _selectedTarget as ReportDataSourceDefinition;
    }

    private ReportDataSetDefinition? GetSelectedDataSetForEditing()
    {
        return SelectedDataNode?.Target as ReportDataSetDefinition
            ?? SelectedDataNode?.SelectionTarget as ReportDataSetDefinition
            ?? _selectedTarget as ReportDataSetDefinition;
    }

    private ReportDesignerDataNodeViewModel? TryGetSelectedDataField(
        out ReportDataSetDefinition? dataSet,
        out string? fieldName,
        out ReportParameterDataType dataType)
    {
        dataSet = SelectedDataNode?.SelectionTarget as ReportDataSetDefinition;
        fieldName = null;
        dataType = ReportParameterDataType.String;

        if (SelectedDataNode?.Kind == ReportDesignerDataNodeKind.QueryField
            && SelectedDataNode.Target is ReportFieldDefinition queryField)
        {
            fieldName = queryField.Name;
            dataType = queryField.DataType;
            return SelectedDataNode;
        }

        if (SelectedDataNode?.Kind == ReportDesignerDataNodeKind.CalculatedField
            && SelectedDataNode.Target is ReportCalculatedFieldDefinition calculatedField)
        {
            fieldName = calculatedField.Name;
            dataType = calculatedField.DataType;
            return SelectedDataNode;
        }

        dataSet = null;
        return null;
    }

    internal ReportDesignerBuiltInFieldDefinition? TryResolveBuiltInFieldDefinition(string id)
    {
        return CreateBuiltInFieldDefinitions().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    internal ReportDesignerImageResourceDefinition? TryResolveImageResourceDefinition(string id)
    {
        return EnumerateImageResourceDefinitions().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private ReportDesignerBuiltInFieldDefinition? TryGetSelectedBuiltInField(
        out ReportDesignerBuiltInFieldDefinition? definition)
    {
        definition = SelectedDataNode?.Kind == ReportDesignerDataNodeKind.BuiltInField
            ? SelectedDataNode.Target as ReportDesignerBuiltInFieldDefinition
            : null;
        return definition;
    }

    private ReportDesignerImageResourceDefinition? TryGetSelectedImageResource(
        out ReportDesignerImageResourceDefinition? definition)
    {
        definition = SelectedDataNode?.Kind == ReportDesignerDataNodeKind.ImageResource
            ? SelectedDataNode.Target as ReportDesignerImageResourceDefinition
            : null;
        return definition;
    }

    private static string DescribeImageResourceExpression(ReportDesignerImageResourceDefinition imageResource)
    {
        return imageResource.SourceKind switch
        {
            ReportImageSourceKind.Embedded => string.IsNullOrWhiteSpace(imageResource.MimeType)
                ? "Embedded image"
                : $"Embedded {imageResource.MimeType}",
            _ => imageResource.ValueExpression ?? string.Empty
        };
    }

    private ReportDataConnectorCategory? GetSelectedDataSourceConnectorCategory()
    {
        var providerId = GetSelectedDataSourceForEditing()?.ProviderId;
        return providerId is null ? null : GetConnectorCategory(providerId);
    }

    private ReportDataConnectorCategory GetConnectorCategory(string providerId)
    {
        return AvailableConnectors.FirstOrDefault(connector =>
            string.Equals(connector.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))?.Category
            ?? ReportDataConnectorCategory.Database;
    }

    private void ReplaceAdditionalDataSourceOptions(
        ReportDataSourceDefinition dataSource,
        IReadOnlyDictionary<string, string> additionalOptions)
    {
        foreach (var key in dataSource.Options.Keys.ToArray())
        {
            if (IsKnownDataSourceOptionKey(key))
            {
                continue;
            }

            dataSource.Options.Remove(key);
        }

        foreach (var pair in additionalOptions)
        {
            dataSource.Options[pair.Key] = pair.Value;
        }
    }

    private static bool IsKnownDataSourceOptionKey(string key)
    {
        return key.Equals("connectionName", StringComparison.OrdinalIgnoreCase)
            || key.Equals("connectorKey", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sourceKey", StringComparison.OrdinalIgnoreCase)
            || key.Equals("connectionString", StringComparison.OrdinalIgnoreCase)
            || key.Equals("baseAddress", StringComparison.OrdinalIgnoreCase)
            || key.Equals("providerInvariantName", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDataSourceOption(ReportDataSourceDefinition dataSource, string key)
    {
        return dataSource.Options.TryGetValue(key, out var value) ? value : null;
    }

    private static void SetOrRemoveOption(Dictionary<string, string> options, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            options.Remove(key);
            return;
        }

        options[key] = value.Trim();
    }

    private static string SerializeAdditionalDataSourceOptions(ReportDataSourceDefinition dataSource)
    {
        var additionalOptions = dataSource.Options
            .Where(pair => !IsKnownDataSourceOptionKey(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return SerializeDictionary(additionalOptions);
    }

    private void EnsureReportParametersForQueryTokens(ReportDataSetDefinition dataSet)
    {
        foreach (var parameterName in DetectQueryParameterNames(dataSet.Query))
        {
            if (!dataSet.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase)))
            {
                dataSet.Parameters.Add(new ReportDataSetParameterDefinition
                {
                    Name = parameterName,
                    ValueExpression = $"Parameters.{parameterName}"
                });
            }

            EnsureReportParameter(parameterName);
        }
    }

    private void EnsureReportParameter(string parameterId)
    {
        if (ReportDefinition.Parameters.Any(parameter =>
                string.Equals(parameter.Id, parameterId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ReportDefinition.Parameters.Add(new ReportParameterDefinition
        {
            Id = parameterId,
            DisplayName = parameterId,
            Prompt = parameterId,
            DataType = ReportParameterDataType.String,
            Visibility = ReportParameterVisibility.Visible
        });
    }

    private static IReadOnlyList<string> DetectQueryParameterNames(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = QueryParameterTokenRegex.Matches(query);
        foreach (Match match in matches)
        {
            if (match.Groups["name"].Success)
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names.ToArray();
    }

    private void CreateTextItemForField(ReportDataSetDefinition dataSet, string fieldName)
    {
        var section = EnsureSelectedSection();
        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = fieldName,
            ValueExpression = CreateReportFieldExpression(dataSet.Id, fieldName),
            Bounds = CreateNextBounds(section, 220f, 40f)
        };
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty($"Added text item for field '{fieldName}'.");
    }

    private void CreateTextItemForBuiltInField(ReportDesignerBuiltInFieldDefinition builtInField)
    {
        var section = EnsureSelectedSection();
        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = builtInField.Label,
            ValueExpression = builtInField.Expression,
            Bounds = CreateNextBounds(section, 220f, 40f)
        };
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty($"Added text item for built-in field '{builtInField.Label}'.");
    }

    private void CreateImageItemForResource(ReportDesignerImageResourceDefinition imageResource)
    {
        var section = EnsureSelectedSection();
        var item = new ImageItem
        {
            Id = CreateUniqueId("image", EnumerateItemIds()),
            Name = imageResource.Label,
            Bounds = CreateNextBounds(section, 220f, 140f)
        };
        ApplyImageResourceDefinition(item, imageResource);
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty($"Added image item from '{imageResource.Label}'.");
    }

    private static void ApplyImageResourceDefinition(ImageItem imageItem, ReportDesignerImageResourceDefinition imageResource)
    {
        imageItem.SourceKind = imageResource.SourceKind;
        imageItem.ValueExpression = imageResource.ValueExpression;
        imageItem.MimeType = imageResource.MimeType;
        imageItem.EmbeddedData = imageResource.EmbeddedData is null ? null : [.. imageResource.EmbeddedData];
    }

    private static string CreateReportFieldExpression(string dataSetId, string fieldName)
    {
        return $"First(Fields.{fieldName}, '{dataSetId}')";
    }

    private static void AppendFieldColumnToTablix(TablixItem tablixItem, string fieldName)
    {
        var nextIndex = tablixItem.Columns.Count + 1;
        tablixItem.Columns.Add(new ReportTablixColumnDefinition
        {
            Id = "col" + nextIndex.ToString(CultureInfo.InvariantCulture),
            Width = 120f
        });

        if (tablixItem.Rows.Count == 0)
        {
            tablixItem.Rows.Add(new ReportTablixRowDefinition
            {
                Id = "header",
                IsHeader = true
            });
            tablixItem.Rows.Add(new ReportTablixRowDefinition
            {
                Id = "detail"
            });
        }

        tablixItem.Rows[0].Cells.Add(new ReportTablixCellDefinition
        {
            Text = fieldName
        });

        if (tablixItem.Rows.Count == 1)
        {
            tablixItem.Rows.Add(new ReportTablixRowDefinition
            {
                Id = "detail"
            });
        }

        tablixItem.Rows[1].Cells.Add(new ReportTablixCellDefinition
        {
            ValueExpression = $"Fields.{fieldName}"
        });
    }

    private static void ConfigureTablixFromDataSet(TablixItem tablixItem, ReportDataSetDefinition dataSet)
    {
        tablixItem.Columns.Clear();
        tablixItem.Rows.Clear();

        var fields = dataSet.ExpectedFields.Count == 0
            ? new[]
            {
                new ReportFieldDefinition { Name = "Field1", DataType = ReportParameterDataType.String }
            }
            : dataSet.ExpectedFields.Take(6).ToArray();

        for (var index = 0; index < fields.Length; index++)
        {
            tablixItem.Columns.Add(new ReportTablixColumnDefinition
            {
                Id = "col" + (index + 1).ToString(CultureInfo.InvariantCulture),
                Width = 120f
            });
        }

        var headerRow = new ReportTablixRowDefinition
        {
            Id = "header",
            IsHeader = true
        };
        var detailRow = new ReportTablixRowDefinition
        {
            Id = "detail"
        };

        foreach (var field in fields)
        {
            headerRow.Cells.Add(new ReportTablixCellDefinition
            {
                Text = field.Name
            });
            detailRow.Cells.Add(new ReportTablixCellDefinition
            {
                ValueExpression = $"Fields.{field.Name}"
            });
        }

        tablixItem.Rows.Add(headerRow);
        tablixItem.Rows.Add(detailRow);
    }

    private static void InsertFieldIntoChart(
        ChartItem chartItem,
        ReportDataSetDefinition dataSet,
        string fieldName,
        ReportParameterDataType dataType)
    {
        var categoryField = dataSet.ExpectedFields.FirstOrDefault(field => !IsNumericField(field.DataType))
            ?? dataSet.ExpectedFields.FirstOrDefault();
        if (categoryField is not null)
        {
            chartItem.CategoryExpression = $"Fields.{categoryField.Name}";
        }

        if (IsNumericField(dataType))
        {
            chartItem.Series.Add(new ReportChartSeriesDefinition
            {
                NameExpression = $"'{fieldName}'",
                ValueExpression = $"Fields.{fieldName}"
            });
        }
        else if (chartItem.Series.Count == 0 && dataSet.ExpectedFields.FirstOrDefault(field => IsNumericField(field.DataType)) is { } numericField)
        {
            chartItem.Series.Add(new ReportChartSeriesDefinition
            {
                NameExpression = $"'{numericField.Name}'",
                ValueExpression = $"Fields.{numericField.Name}"
            });
        }

        if (string.IsNullOrWhiteSpace(chartItem.TitleExpression))
        {
            chartItem.TitleExpression = $"'{chartItem.Name}'";
        }
    }

    private static void ConfigureChartFromDataSet(ChartItem chartItem, ReportDataSetDefinition dataSet)
    {
        chartItem.Series.Clear();
        var categoryField = dataSet.ExpectedFields.FirstOrDefault(field => !IsNumericField(field.DataType))
            ?? dataSet.ExpectedFields.FirstOrDefault();
        if (categoryField is not null)
        {
            chartItem.CategoryExpression = $"Fields.{categoryField.Name}";
        }

        foreach (var field in dataSet.ExpectedFields.Where(field => IsNumericField(field.DataType)).Take(3))
        {
            chartItem.Series.Add(new ReportChartSeriesDefinition
            {
                NameExpression = $"'{field.Name}'",
                ValueExpression = $"Fields.{field.Name}"
            });
        }

        if (chartItem.Series.Count == 0 && categoryField is not null)
        {
            chartItem.Series.Add(new ReportChartSeriesDefinition
            {
                NameExpression = $"'{categoryField.Name}'",
                ValueExpression = $"Fields.{categoryField.Name}"
            });
        }

        chartItem.TitleExpression = string.IsNullOrWhiteSpace(chartItem.TitleExpression)
            ? $"'{chartItem.Name}'"
            : chartItem.TitleExpression;
    }

    private static bool IsNumericField(ReportParameterDataType dataType)
    {
        return dataType is ReportParameterDataType.Decimal
            or ReportParameterDataType.Integer
            or ReportParameterDataType.Number;
    }

    private static string DescribeWorkspaceDataSetEntry(ReportDataSetDefinition dataSet)
    {
        var source = string.IsNullOrWhiteSpace(dataSet.DataSourceId) ? "No source" : dataSet.DataSourceId;
        var fieldCount = dataSet.ExpectedFields.Count;
        var parameterCount = dataSet.Parameters.Count;
        return $"{source} · {fieldCount} field(s) · {parameterCount} query parameter(s)";
    }

    private static string DescribeOperator(ReportFilterOperator filterOperator)
    {
        return filterOperator switch
        {
            ReportFilterOperator.Equal => "=",
            ReportFilterOperator.NotEqual => "!=",
            ReportFilterOperator.GreaterThan => ">",
            ReportFilterOperator.GreaterThanOrEqual => ">=",
            ReportFilterOperator.LessThan => "<",
            ReportFilterOperator.LessThanOrEqual => "<=",
            ReportFilterOperator.Contains => "contains",
            _ => filterOperator.ToString()
        };
    }

    private static string TrimForSubtitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Not configured";
        }

        var trimmed = text.Trim();
        return trimmed.Length <= 42 ? trimmed : trimmed[..39] + "...";
    }

    private static string FormatPreviewValue(object? value, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString(culture),
            IFormattable formattable => formattable.ToString(null, culture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string SerializeCalculatedFields(IReadOnlyList<ReportCalculatedFieldDefinition> fields)
    {
        return string.Join(Environment.NewLine, fields.Select(static field =>
            $"{field.Name}:{field.DataType}={field.Expression}"));
    }

    private static bool TryParseCalculatedFields(
        string text,
        out List<ReportCalculatedFieldDefinition> fields)
    {
        fields = new List<ReportCalculatedFieldDefinition>();
        foreach (var line in SplitDesignerLines(text))
        {
            var equalsIndex = line.IndexOf('=');
            var colonIndex = line.IndexOf(':');
            if (equalsIndex <= 0 || colonIndex <= 0 || colonIndex > equalsIndex)
            {
                return false;
            }

            var name = line[..colonIndex].Trim();
            var dataTypeText = line[(colonIndex + 1)..equalsIndex].Trim();
            var expression = line[(equalsIndex + 1)..].Trim();
            if (!Enum.TryParse<ReportParameterDataType>(dataTypeText, ignoreCase: true, out var dataType))
            {
                return false;
            }

            fields.Add(new ReportCalculatedFieldDefinition
            {
                Name = name,
                DataType = dataType,
                Expression = expression
            });
        }

        return true;
    }

    private static string SerializeDataSetParameters(IReadOnlyList<ReportDataSetParameterDefinition> parameters)
    {
        return string.Join(Environment.NewLine, parameters.Select(static parameter =>
            $"{parameter.Name}={parameter.ValueExpression}"));
    }

    private static bool TryParseDataSetParameters(
        string text,
        out List<ReportDataSetParameterDefinition> parameters)
    {
        parameters = new List<ReportDataSetParameterDefinition>();
        foreach (var line in SplitDesignerLines(text))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return false;
            }

            parameters.Add(new ReportDataSetParameterDefinition
            {
                Name = line[..separatorIndex].Trim(),
                ValueExpression = line[(separatorIndex + 1)..].Trim()
            });
        }

        return true;
    }

    private static string SerializeDataSetFilters(IReadOnlyList<ReportFilterDefinition> filters)
    {
        return string.Join(Environment.NewLine, filters.Select(static filter =>
            $"{filter.Expression}|{filter.Operator}|{filter.ValueExpression}"));
    }

    private static bool TryParseDataSetFilters(
        string text,
        out List<ReportFilterDefinition> filters)
    {
        filters = new List<ReportFilterDefinition>();
        foreach (var line in SplitDesignerLines(text))
        {
            var parts = line.Split('|');
            if (parts.Length != 3
                || !Enum.TryParse<ReportFilterOperator>(parts[1].Trim(), ignoreCase: true, out var filterOperator))
            {
                return false;
            }

            filters.Add(new ReportFilterDefinition
            {
                Expression = parts[0].Trim(),
                Operator = filterOperator,
                ValueExpression = parts[2].Trim()
            });
        }

        return true;
    }

    private static string SerializeDataSetSorts(IReadOnlyList<ReportSortDefinition> sorts)
    {
        return string.Join(Environment.NewLine, sorts.Select(static sort =>
            $"{sort.Expression}|{sort.Direction}"));
    }

    private static bool TryParseDataSetSorts(
        string text,
        out List<ReportSortDefinition> sorts)
    {
        sorts = new List<ReportSortDefinition>();
        foreach (var line in SplitDesignerLines(text))
        {
            var parts = line.Split('|');
            if (parts.Length != 2
                || !Enum.TryParse<ReportSortDirection>(parts[1].Trim(), ignoreCase: true, out var direction))
            {
                return false;
            }

            sorts.Add(new ReportSortDefinition
            {
                Expression = parts[0].Trim(),
                Direction = direction
            });
        }

        return true;
    }

    private static IEnumerable<string> SplitDesignerLines(string text)
    {
        return (text ?? string.Empty)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
