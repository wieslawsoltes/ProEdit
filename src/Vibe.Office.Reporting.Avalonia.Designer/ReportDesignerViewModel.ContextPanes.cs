using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private readonly ObservableCollection<ReportDesignerParameterLayoutRowViewModel> _parameterLayoutRows = new();
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _chartDataSetOptions = new();
    private readonly ObservableCollection<ReportDesignerChartSeriesEntryViewModel> _chartSeriesEntries = new();

    private ReportDesignerParameterLayoutCellViewModel? _selectedParameterLayoutCell;
    private ReportDesignerChoiceOptionViewModel? _selectedChartDataSetOption;
    private ReportDesignerChartSeriesEntryViewModel? _selectedChartSeriesEntry;
    private string _chartCategoryExpressionText = string.Empty;
    private string _chartDataStatusMessage = "Select a chart to author categories, series groups, and values.";
    private string _parameterPaneStatusMessage = "Add visible parameters to show the parameter pane.";
    private bool _suppressChartWorkspaceUpdates;
    private bool _suppressParameterLayoutSelectionSync;

    internal ReadOnlyObservableCollection<ReportDesignerParameterLayoutRowViewModel> ParameterLayoutRows { get; private set; } = null!;

    internal ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> ChartDataSetOptions { get; private set; } = null!;

    internal ReadOnlyObservableCollection<ReportDesignerChartSeriesEntryViewModel> ChartSeriesEntries { get; private set; } = null!;

    public bool ShowParameterLayoutPane => ShowParameterPaneOnDesignSurface && GetDesignSurfaceParameters().Count > 0;

    public bool ShowGroupingPane => GetSelectedTablix() is not null;

    public bool ShowChartDataPane => GetSelectedChart() is not null;

    public bool ShowContextPanePlaceholder => !ShowGroupingPane && !ShowChartDataPane;

    public bool ShowParameterPaneOnDesignSurface
    {
        get => ReportDefinition.ParameterLayout.ShowOnDesignSurface;
        set
        {
            if (ReportDefinition.ParameterLayout.ShowOnDesignSurface == value)
            {
                return;
            }

            ReportDefinition.ParameterLayout.ShowOnDesignSurface = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(ShowParameterLayoutPane));
            MarkDirty(value
                ? "Displayed the parameter pane on the design surface."
                : "Hid the parameter pane on the design surface.");
        }
    }

    public string ContextPanePlaceholderTitle => "Context Pane";

    public string ContextPanePlaceholderText => "Select a tablix to author grouping or select a chart to author categories, series groups, and values.";

    public string ParameterPaneStatusMessage
    {
        get => _parameterPaneStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _parameterPaneStatusMessage, value ?? string.Empty);
    }

    public string ChartDataStatusMessage
    {
        get => _chartDataStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _chartDataStatusMessage, value ?? string.Empty);
    }

    internal string ChartCategoryExpressionText
    {
        get => _chartCategoryExpressionText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_chartCategoryExpressionText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _chartCategoryExpressionText, normalized);
            if (_suppressChartWorkspaceUpdates || GetSelectedChart() is not { } chart)
            {
                return;
            }

            chart.CategoryExpression = NormalizeOptional(normalized);
            OnChartWorkspaceChanged("Updated chart category group.");
        }
    }

    public bool CanRemoveParameterLayoutRow => ReportDefinition.ParameterLayout.RowCount > 1;

    public bool CanRemoveParameterLayoutColumn => ReportDefinition.ParameterLayout.ColumnCount > 1;

    public bool CanMoveSelectedParameterLeft => SelectedParameterLayoutCell is { HasParameter: true, ColumnIndex: > 0 };

    public bool CanMoveSelectedParameterRight => SelectedParameterLayoutCell is { HasParameter: true } cell
        && cell.ColumnIndex < ReportDefinition.ParameterLayout.ColumnCount - 1;

    public bool CanMoveSelectedParameterUp => SelectedParameterLayoutCell is { HasParameter: true, RowIndex: > 0 };

    public bool CanMoveSelectedParameterDown => SelectedParameterLayoutCell is { HasParameter: true } cell
        && cell.RowIndex < ReportDefinition.ParameterLayout.RowCount - 1;

    public bool HasSelectedChartSeries => SelectedChartSeriesEntry is not null;

    public bool CanApplySelectedFieldToChartCategory => GetSelectedChart() is not null && TryGetSelectedDataField(out _, out _, out _) is not null;

    public bool CanApplySelectedFieldToChartValue => GetSelectedChart() is not null && TryGetSelectedDataField(out _, out _, out _) is not null;

    public bool CanApplySelectedFieldToChartSeriesGroup => GetSelectedChart() is not null && TryGetSelectedDataField(out _, out _, out _) is not null;

    internal ReactiveCommand<Unit, Unit> AddParameterLayoutRowCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> AddParameterLayoutColumnCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> RemoveParameterLayoutRowCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> RemoveParameterLayoutColumnCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> MoveSelectedParameterLeftCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> MoveSelectedParameterRightCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> MoveSelectedParameterUpCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> MoveSelectedParameterDownCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> AddChartSeriesCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> RemoveSelectedChartSeriesCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> ApplySelectedFieldToChartCategoryCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> ApplySelectedFieldToChartValueCommand { get; private set; } = null!;

    internal ReactiveCommand<Unit, Unit> ApplySelectedFieldToChartSeriesGroupCommand { get; private set; } = null!;

    internal ReportDesignerParameterLayoutCellViewModel? SelectedParameterLayoutCell
    {
        get => _selectedParameterLayoutCell;
        set
        {
            if (ReferenceEquals(_selectedParameterLayoutCell, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedParameterLayoutCell, value);
            foreach (var row in _parameterLayoutRows)
            {
                foreach (var cell in row.Cells)
                {
                    cell.IsSelected = ReferenceEquals(cell, value);
                }
            }

            if (!_suppressSelectionSynchronization
                && !_suppressParameterLayoutSelectionSync
                && value?.Parameter is not null)
            {
                SelectedInspectorTabIndex = 1;
                SelectTarget(value.Parameter);
            }

            this.RaisePropertyChanged(nameof(CanMoveSelectedParameterLeft));
            this.RaisePropertyChanged(nameof(CanMoveSelectedParameterRight));
            this.RaisePropertyChanged(nameof(CanMoveSelectedParameterUp));
            this.RaisePropertyChanged(nameof(CanMoveSelectedParameterDown));
        }
    }

    internal ReportDesignerChoiceOptionViewModel? SelectedChartDataSetOption
    {
        get => _selectedChartDataSetOption;
        set
        {
            if (ReferenceEquals(_selectedChartDataSetOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedChartDataSetOption, value);
            if (_suppressChartWorkspaceUpdates || GetSelectedChart() is not { } chart)
            {
                return;
            }

            chart.DataSetId = string.IsNullOrWhiteSpace(value?.Value) ? null : value.Value;
            if (!string.IsNullOrWhiteSpace(chart.DataSetId)
                && FindDataSet(chart.DataSetId) is { } dataSet
                && (chart.Series.Count == 0 || string.IsNullOrWhiteSpace(chart.CategoryExpression)))
            {
                ConfigureChartFromDataSet(chart, dataSet);
            }

            RebuildDesignerState(chart);
            MarkDirty("Updated chart dataset.");
        }
    }

    internal ReportDesignerChartSeriesEntryViewModel? SelectedChartSeriesEntry
    {
        get => _selectedChartSeriesEntry;
        set
        {
            if (ReferenceEquals(_selectedChartSeriesEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedChartSeriesEntry, value);
            foreach (var entry in _chartSeriesEntries)
            {
                entry.IsSelected = ReferenceEquals(entry, value);
            }

            this.RaisePropertyChanged(nameof(HasSelectedChartSeries));
        }
    }

    private void InitializeContextPanes()
    {
        ParameterLayoutRows = new ReadOnlyObservableCollection<ReportDesignerParameterLayoutRowViewModel>(_parameterLayoutRows);
        ChartDataSetOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_chartDataSetOptions);
        ChartSeriesEntries = new ReadOnlyObservableCollection<ReportDesignerChartSeriesEntryViewModel>(_chartSeriesEntries);

        AddParameterLayoutRowCommand = ReactiveCommand.Create(AddParameterLayoutRow);
        AddParameterLayoutColumnCommand = ReactiveCommand.Create(AddParameterLayoutColumn);
        RemoveParameterLayoutRowCommand = ReactiveCommand.Create(RemoveParameterLayoutRow);
        RemoveParameterLayoutColumnCommand = ReactiveCommand.Create(RemoveParameterLayoutColumn);
        MoveSelectedParameterLeftCommand = ReactiveCommand.Create(() => MoveSelectedParameterByDelta(-1, 0));
        MoveSelectedParameterRightCommand = ReactiveCommand.Create(() => MoveSelectedParameterByDelta(1, 0));
        MoveSelectedParameterUpCommand = ReactiveCommand.Create(() => MoveSelectedParameterByDelta(0, -1));
        MoveSelectedParameterDownCommand = ReactiveCommand.Create(() => MoveSelectedParameterByDelta(0, 1));
        AddChartSeriesCommand = ReactiveCommand.Create(AddChartSeries);
        RemoveSelectedChartSeriesCommand = ReactiveCommand.Create(RemoveSelectedChartSeries);
        ApplySelectedFieldToChartCategoryCommand = ReactiveCommand.Create(ApplySelectedFieldToChartCategory);
        ApplySelectedFieldToChartValueCommand = ReactiveCommand.Create(ApplySelectedFieldToChartValue);
        ApplySelectedFieldToChartSeriesGroupCommand = ReactiveCommand.Create(ApplySelectedFieldToChartSeriesGroup);
    }

    private void RefreshContextPanes()
    {
        RebuildParameterLayoutPane();
        RebuildChartWorkspace();
        RaiseContextPanePropertiesChanged();
    }

    private void RaiseContextPanePropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(ShowParameterPaneOnDesignSurface));
        this.RaisePropertyChanged(nameof(ShowParameterLayoutPane));
        this.RaisePropertyChanged(nameof(ShowGroupingPane));
        this.RaisePropertyChanged(nameof(ShowChartDataPane));
        this.RaisePropertyChanged(nameof(ShowContextPanePlaceholder));
        this.RaisePropertyChanged(nameof(CanRemoveParameterLayoutRow));
        this.RaisePropertyChanged(nameof(CanRemoveParameterLayoutColumn));
        this.RaisePropertyChanged(nameof(CanMoveSelectedParameterLeft));
        this.RaisePropertyChanged(nameof(CanMoveSelectedParameterRight));
        this.RaisePropertyChanged(nameof(CanMoveSelectedParameterUp));
        this.RaisePropertyChanged(nameof(CanMoveSelectedParameterDown));
        this.RaisePropertyChanged(nameof(CanApplySelectedFieldToChartCategory));
        this.RaisePropertyChanged(nameof(CanApplySelectedFieldToChartValue));
        this.RaisePropertyChanged(nameof(CanApplySelectedFieldToChartSeriesGroup));
        this.RaisePropertyChanged(nameof(HasSelectedChartSeries));
    }

    private void RebuildParameterLayoutPane()
    {
        NormalizeParameterLayout();

        var selectedParameterId = SelectedParameterLayoutCell?.ParameterId;
        _parameterLayoutRows.Clear();

        var layout = ReportDefinition.ParameterLayout;
        var cellMap = layout.Cells.ToDictionary(
            static cell => (cell.RowIndex, cell.ColumnIndex),
            static cell => cell,
            EqualityComparer<(int RowIndex, int ColumnIndex)>.Default);
        var parameterMap = ReportDefinition.Parameters.ToDictionary(
            static parameter => parameter.Id,
            static parameter => parameter,
            StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = 0; rowIndex < layout.RowCount; rowIndex++)
        {
            var rowCells = new List<ReportDesignerParameterLayoutCellViewModel>(layout.ColumnCount);
            for (var columnIndex = 0; columnIndex < layout.ColumnCount; columnIndex++)
            {
                parameterMap.TryGetValue(
                    cellMap.TryGetValue((rowIndex, columnIndex), out var cellDefinition)
                        ? cellDefinition.ParameterId
                        : string.Empty,
                    out var parameter);
                rowCells.Add(new ReportDesignerParameterLayoutCellViewModel(rowIndex, columnIndex, parameter, SelectParameterLayoutCell));
            }

            _parameterLayoutRows.Add(new ReportDesignerParameterLayoutRowViewModel(rowIndex, rowCells));
        }

        _suppressParameterLayoutSelectionSync = true;
        try
        {
            SelectedParameterLayoutCell = _parameterLayoutRows
                .SelectMany(static row => row.Cells)
                .FirstOrDefault(cell => string.Equals(cell.ParameterId, selectedParameterId, StringComparison.OrdinalIgnoreCase))
                ?? _parameterLayoutRows.SelectMany(static row => row.Cells).FirstOrDefault(static cell => cell.HasParameter)
                ?? _parameterLayoutRows.SelectMany(static row => row.Cells).FirstOrDefault();
        }
        finally
        {
            _suppressParameterLayoutSelectionSync = false;
        }

        ParameterPaneStatusMessage = ShowParameterLayoutPane
            ? $"{layout.RowCount} row(s) · {layout.ColumnCount} column(s)"
            : "Use View > Parameters to show the parameter pane when visible parameters exist.";
    }

    private void RebuildChartWorkspace()
    {
        var chart = GetSelectedChart();
        var selectedDataSetId = chart?.DataSetId;
        var selectedSeries = SelectedChartSeriesEntry is null ? null : SelectedChartSeriesEntry;

        _chartDataSetOptions.Clear();
        foreach (var dataSet in ReportDefinition.DataSets)
        {
            _chartDataSetOptions.Add(new ReportDesignerChoiceOptionViewModel(dataSet.Id, dataSet.Id));
        }

        _suppressChartWorkspaceUpdates = true;
        try
        {
            SelectedChartDataSetOption = chart is null
                ? null
                : _chartDataSetOptions.FirstOrDefault(option => string.Equals(option.Value, selectedDataSetId, StringComparison.OrdinalIgnoreCase));

            ChartCategoryExpressionText = chart?.CategoryExpression ?? string.Empty;

            var selectedSeriesName = selectedSeries?.NameExpression;
            _chartSeriesEntries.Clear();
            if (chart is not null)
            {
                foreach (var series in chart.Series)
                {
                    _chartSeriesEntries.Add(new ReportDesignerChartSeriesEntryViewModel(series, OnChartSeriesEdited));
                }

                SelectedChartSeriesEntry = _chartSeriesEntries.FirstOrDefault(entry => string.Equals(entry.NameExpression, selectedSeriesName, StringComparison.Ordinal))
                    ?? _chartSeriesEntries.FirstOrDefault();
            }
            else
            {
                SelectedChartSeriesEntry = null;
            }
        }
        finally
        {
            _suppressChartWorkspaceUpdates = false;
        }

        ChartDataStatusMessage = chart is null
            ? "Select a chart to author categories, series groups, and values."
            : string.IsNullOrWhiteSpace(chart.DataSetId)
                ? "Select a dataset or drop Report Data fields into the chart-data buckets."
                : $"Editing chart data for dataset '{chart.DataSetId}'.";
    }

    private void NormalizeParameterLayout()
    {
        var layout = ReportDefinition.ParameterLayout;
        layout.ColumnCount = Math.Max(1, layout.ColumnCount);
        layout.RowCount = Math.Max(1, layout.RowCount);

        var visibleParameters = GetDesignSurfaceParameters();
        var visibleIds = new HashSet<string>(visibleParameters.Select(static parameter => parameter.Id), StringComparer.OrdinalIgnoreCase);
        var usedParameterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupiedCells = new HashSet<(int RowIndex, int ColumnIndex)>();
        var normalizedCells = new List<ReportParameterLayoutCellDefinition>(visibleParameters.Count);
        var rowCount = layout.RowCount;

        foreach (var cell in layout.Cells)
        {
            if (string.IsNullOrWhiteSpace(cell.ParameterId)
                || !visibleIds.Contains(cell.ParameterId)
                || !usedParameterIds.Add(cell.ParameterId))
            {
                continue;
            }

            var rowIndex = Math.Max(0, cell.RowIndex);
            var columnIndex = cell.ColumnIndex >= 0 && cell.ColumnIndex < layout.ColumnCount
                ? cell.ColumnIndex
                : 0;
            if (!occupiedCells.Add((rowIndex, columnIndex)))
            {
                var nextSlot = FindNextAvailableParameterCell(occupiedCells, layout.ColumnCount, ref rowCount);
                rowIndex = nextSlot.RowIndex;
                columnIndex = nextSlot.ColumnIndex;
            }

            rowCount = Math.Max(rowCount, rowIndex + 1);
            normalizedCells.Add(new ReportParameterLayoutCellDefinition
            {
                ParameterId = cell.ParameterId,
                RowIndex = rowIndex,
                ColumnIndex = columnIndex
            });
        }

        foreach (var parameter in visibleParameters)
        {
            if (usedParameterIds.Contains(parameter.Id))
            {
                continue;
            }

            var slot = FindNextAvailableParameterCell(occupiedCells, layout.ColumnCount, ref rowCount);
            normalizedCells.Add(new ReportParameterLayoutCellDefinition
            {
                ParameterId = parameter.Id,
                RowIndex = slot.RowIndex,
                ColumnIndex = slot.ColumnIndex
            });
            occupiedCells.Add(slot);
        }

        layout.RowCount = rowCount;
        layout.Cells = normalizedCells
            .OrderBy(static cell => cell.RowIndex)
            .ThenBy(static cell => cell.ColumnIndex)
            .ToList();
        SynchronizeParameterOrderFromLayout();
    }

    private List<ReportParameterDefinition> GetDesignSurfaceParameters()
    {
        return ReportDefinition.Parameters
            .Where(static parameter => parameter.Visibility == ReportParameterVisibility.Visible)
            .ToList();
    }

    private static (int RowIndex, int ColumnIndex) FindNextAvailableParameterCell(
        HashSet<(int RowIndex, int ColumnIndex)> occupiedCells,
        int columnCount,
        ref int rowCount)
    {
        var normalizedColumnCount = Math.Max(1, columnCount);
        var normalizedRowCount = Math.Max(1, rowCount);
        for (var rowIndex = 0; rowIndex < normalizedRowCount; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < normalizedColumnCount; columnIndex++)
            {
                if (!occupiedCells.Contains((rowIndex, columnIndex)))
                {
                    rowCount = normalizedRowCount;
                    return (rowIndex, columnIndex);
                }
            }
        }

        rowCount = normalizedRowCount + 1;
        return (normalizedRowCount, 0);
    }

    private void SynchronizeParameterOrderFromLayout()
    {
        var layout = ReportDefinition.ParameterLayout;
        var visibleById = ReportDefinition.Parameters
            .Where(static parameter => parameter.Visibility == ReportParameterVisibility.Visible)
            .ToDictionary(static parameter => parameter.Id, static parameter => parameter, StringComparer.OrdinalIgnoreCase);
        var orderedVisible = layout.Cells
            .OrderBy(static cell => cell.RowIndex)
            .ThenBy(static cell => cell.ColumnIndex)
            .Select(cell => visibleById.GetValueOrDefault(cell.ParameterId))
            .Where(static parameter => parameter is not null)
            .Cast<ReportParameterDefinition>()
            .ToList();
        var hiddenOrInternal = ReportDefinition.Parameters
            .Where(static parameter => parameter.Visibility != ReportParameterVisibility.Visible)
            .ToList();
        var unpositionedVisible = ReportDefinition.Parameters
            .Where(parameter => parameter.Visibility == ReportParameterVisibility.Visible
                && !orderedVisible.Contains(parameter))
            .ToList();

        ReportDefinition.Parameters.Clear();
        ReportDefinition.Parameters.AddRange(orderedVisible);
        ReportDefinition.Parameters.AddRange(unpositionedVisible);
        ReportDefinition.Parameters.AddRange(hiddenOrInternal);
    }

    private void SelectParameterLayoutCell(ReportDesignerParameterLayoutCellViewModel cell)
    {
        SelectedParameterLayoutCell = cell;
    }

    private void AddParameterLayoutRow()
    {
        ReportDefinition.ParameterLayout.RowCount = Math.Max(1, ReportDefinition.ParameterLayout.RowCount) + 1;
        RefreshContextPanes();
        MarkDirty("Added a parameter-pane row.");
    }

    private void AddParameterLayoutColumn()
    {
        ReportDefinition.ParameterLayout.ColumnCount = Math.Max(1, ReportDefinition.ParameterLayout.ColumnCount) + 1;
        RefreshContextPanes();
        MarkDirty("Added a parameter-pane column.");
    }

    private void RemoveParameterLayoutRow()
    {
        if (ReportDefinition.ParameterLayout.RowCount <= 1)
        {
            return;
        }

        var removedRowIndex = SelectedParameterLayoutCell?.RowIndex ?? (ReportDefinition.ParameterLayout.RowCount - 1);
        RemoveParametersInDeletedRow(removedRowIndex);
        ReportDefinition.ParameterLayout.Cells = ReportDefinition.ParameterLayout.Cells
            .Where(cell => cell.RowIndex != removedRowIndex)
            .Select(cell =>
            {
                if (cell.RowIndex > removedRowIndex)
                {
                    cell.RowIndex--;
                }

                return cell;
            })
            .ToList();
        ReportDefinition.ParameterLayout.RowCount--;
        RebuildDesignerState(ReportDefinition);
        MarkDirty("Removed a parameter-pane row.");
    }

    private void RemoveParameterLayoutColumn()
    {
        if (ReportDefinition.ParameterLayout.ColumnCount <= 1)
        {
            return;
        }

        var removedColumnIndex = SelectedParameterLayoutCell?.ColumnIndex ?? (ReportDefinition.ParameterLayout.ColumnCount - 1);
        RemoveParametersInDeletedColumn(removedColumnIndex);
        ReportDefinition.ParameterLayout.Cells = ReportDefinition.ParameterLayout.Cells
            .Where(cell => cell.ColumnIndex != removedColumnIndex)
            .Select(cell =>
            {
                if (cell.ColumnIndex > removedColumnIndex)
                {
                    cell.ColumnIndex--;
                }

                return cell;
            })
            .ToList();
        ReportDefinition.ParameterLayout.ColumnCount--;
        RebuildDesignerState(ReportDefinition);
        MarkDirty("Removed a parameter-pane column.");
    }

    private void RemoveParametersInDeletedRow(int rowIndex)
    {
        var parameterIds = ReportDefinition.ParameterLayout.Cells
            .Where(cell => cell.RowIndex == rowIndex)
            .Select(static cell => cell.ParameterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parameterIds.Count == 0)
        {
            return;
        }

        ReportDefinition.Parameters.RemoveAll(parameter => parameterIds.Contains(parameter.Id));
    }

    private void RemoveParametersInDeletedColumn(int columnIndex)
    {
        var parameterIds = ReportDefinition.ParameterLayout.Cells
            .Where(cell => cell.ColumnIndex == columnIndex)
            .Select(static cell => cell.ParameterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parameterIds.Count == 0)
        {
            return;
        }

        ReportDefinition.Parameters.RemoveAll(parameter => parameterIds.Contains(parameter.Id));
    }

    private void MoveSelectedParameterByDelta(int deltaColumn, int deltaRow)
    {
        if (SelectedParameterLayoutCell?.Parameter is not { } parameter)
        {
            return;
        }

        var targetColumn = Math.Clamp(SelectedParameterLayoutCell.ColumnIndex + deltaColumn, 0, ReportDefinition.ParameterLayout.ColumnCount - 1);
        var targetRow = Math.Clamp(SelectedParameterLayoutCell.RowIndex + deltaRow, 0, ReportDefinition.ParameterLayout.RowCount - 1);
        if (targetColumn == SelectedParameterLayoutCell.ColumnIndex
            && targetRow == SelectedParameterLayoutCell.RowIndex)
        {
            return;
        }

        if (MoveParameterToLayoutCell(parameter, targetRow, targetColumn))
        {
            MarkDirty("Moved parameter in the parameter pane.");
        }
    }

    internal bool TryApplyParameterLayoutDrop(ReportDesignerParameterDragPayload payload, int rowIndex, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(payload);
        payload.Parameter.Visibility = ReportParameterVisibility.Visible;
        if (MoveParameterToLayoutCell(payload.Parameter, rowIndex, columnIndex))
        {
            MarkDirty($"Moved parameter '{payload.Parameter.Id}' in the parameter pane.");
            return true;
        }

        return false;
    }

    internal bool CanAcceptParameterLayoutDrop(int rowIndex, int columnIndex)
    {
        return rowIndex >= 0
            && columnIndex >= 0
            && rowIndex < ReportDefinition.ParameterLayout.RowCount
            && columnIndex < ReportDefinition.ParameterLayout.ColumnCount;
    }

    private bool MoveParameterToLayoutCell(ReportParameterDefinition parameter, int rowIndex, int columnIndex)
    {
        NormalizeParameterLayout();
        var layout = ReportDefinition.ParameterLayout;
        var sourceCell = layout.Cells.FirstOrDefault(cell => string.Equals(cell.ParameterId, parameter.Id, StringComparison.OrdinalIgnoreCase));
        var targetCell = layout.Cells.FirstOrDefault(cell => cell.RowIndex == rowIndex && cell.ColumnIndex == columnIndex);
        var sourceWasPositioned = sourceCell is not null;
        if (sourceCell is not null && sourceCell.RowIndex == rowIndex && sourceCell.ColumnIndex == columnIndex)
        {
            return false;
        }

        if (sourceCell is null)
        {
            sourceCell = new ReportParameterLayoutCellDefinition
            {
                ParameterId = parameter.Id,
                RowIndex = -1,
                ColumnIndex = -1
            };
            layout.Cells.Add(sourceCell);
        }

        if (targetCell is not null && !ReferenceEquals(targetCell, sourceCell))
        {
            if (sourceWasPositioned)
            {
                var sourceRow = sourceCell.RowIndex;
                var sourceColumn = sourceCell.ColumnIndex;
                targetCell.RowIndex = sourceRow;
                targetCell.ColumnIndex = sourceColumn;
            }
            else
            {
                var occupiedCells = layout.Cells
                    .Where(cell => !ReferenceEquals(cell, sourceCell) && !ReferenceEquals(cell, targetCell))
                    .Select(static cell => (cell.RowIndex, cell.ColumnIndex))
                    .ToHashSet();
                var rowCount = layout.RowCount;
                var nextSlot = FindNextAvailableParameterCell(occupiedCells, layout.ColumnCount, ref rowCount);
                layout.RowCount = Math.Max(layout.RowCount, rowCount);
                targetCell.RowIndex = nextSlot.RowIndex;
                targetCell.ColumnIndex = nextSlot.ColumnIndex;
            }
        }

        sourceCell.RowIndex = rowIndex;
        sourceCell.ColumnIndex = columnIndex;
        layout.RowCount = Math.Max(layout.RowCount, rowIndex + 1);
        layout.ColumnCount = Math.Max(layout.ColumnCount, columnIndex + 1);

        SynchronizeParameterOrderFromLayout();
        RebuildDesignerState(parameter);
        return true;
    }

    private ChartItem? GetSelectedChart()
    {
        return _selectedTarget as ChartItem;
    }

    private void OnChartSeriesEdited()
    {
        OnChartWorkspaceChanged("Updated chart series.");
    }

    private void OnChartWorkspaceChanged(string message)
    {
        RefreshLightweightViews();
        RebuildChartWorkspace();
        MarkDirty(message);
    }

    private void AddChartSeries()
    {
        if (GetSelectedChart() is not { } chart)
        {
            return;
        }

        var series = new ReportChartSeriesDefinition
        {
            NameExpression = "'Series'",
            ValueExpression = ResolveDefaultChartValueExpression(chart)
        };
        chart.Series.Add(series);
        RebuildDesignerState(chart);
        SelectedChartSeriesEntry = _chartSeriesEntries.LastOrDefault();
        MarkDirty("Added chart value.");
    }

    private void RemoveSelectedChartSeries()
    {
        if (GetSelectedChart() is not { } chart || SelectedChartSeriesEntry is null)
        {
            return;
        }

        var series = chart.Series.FirstOrDefault(candidate =>
            string.Equals(candidate.NameExpression ?? string.Empty, SelectedChartSeriesEntry.NameExpression, StringComparison.Ordinal)
            && string.Equals(candidate.ValueExpression ?? string.Empty, SelectedChartSeriesEntry.ValueExpression, StringComparison.Ordinal));
        if (series is null)
        {
            return;
        }

        chart.Series.Remove(series);
        RebuildDesignerState(chart);
        MarkDirty("Removed chart value.");
    }

    private void ApplySelectedFieldToChartCategory()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null
            || dataSet is null
            || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryApplyChartDataDrop(new ReportDesignerDataFieldDragPayload(dataSet, fieldName!, ReportParameterDataType.String), ReportDesignerChartDropTarget.CategoryGroups);
    }

    private void ApplySelectedFieldToChartValue()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out var dataType) is null
            || dataSet is null
            || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryApplyChartDataDrop(new ReportDesignerDataFieldDragPayload(dataSet, fieldName!, dataType), ReportDesignerChartDropTarget.Values);
    }

    private void ApplySelectedFieldToChartSeriesGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out var dataType) is null
            || dataSet is null
            || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryApplyChartDataDrop(new ReportDesignerDataFieldDragPayload(dataSet, fieldName!, dataType), ReportDesignerChartDropTarget.SeriesGroups);
    }

    internal bool CanAcceptChartDataDrop(ReportDesignerDragPayload payload, ReportDesignerChartDropTarget dropTarget)
    {
        if (GetSelectedChart() is null)
        {
            return false;
        }

        return payload switch
        {
            ReportDesignerDataSetDragPayload => true,
            ReportDesignerDataFieldDragPayload fieldPayload => dropTarget switch
            {
                ReportDesignerChartDropTarget.CategoryGroups => true,
                ReportDesignerChartDropTarget.SeriesGroups => true,
                ReportDesignerChartDropTarget.Values => IsNumericField(fieldPayload.DataType)
                    || fieldPayload.DataSet.ExpectedFields.Any(field => IsNumericField(field.DataType)),
                _ => false
            },
            _ => false
        };
    }

    internal bool TryApplyChartDataDrop(ReportDesignerDragPayload payload, ReportDesignerChartDropTarget dropTarget)
    {
        if (GetSelectedChart() is not { } chart)
        {
            return false;
        }

        switch (payload)
        {
            case ReportDesignerDataSetDragPayload dataSetPayload:
                chart.DataSetId = dataSetPayload.DataSet.Id;
                ConfigureChartFromDataSet(chart, dataSetPayload.DataSet);
                RebuildDesignerState(chart);
                MarkDirty($"Bound dataset '{dataSetPayload.DataSet.Id}' to chart.");
                return true;

            case ReportDesignerDataFieldDragPayload fieldPayload:
                chart.DataSetId = fieldPayload.DataSet.Id;
                switch (dropTarget)
                {
                    case ReportDesignerChartDropTarget.CategoryGroups:
                        chart.CategoryExpression = $"Fields.{fieldPayload.FieldName}";
                        RebuildDesignerState(chart);
                        MarkDirty($"Bound field '{fieldPayload.FieldName}' as the chart category group.");
                        return true;

                    case ReportDesignerChartDropTarget.SeriesGroups:
                        ApplyFieldToChartSeriesGroup(chart, fieldPayload);
                        RebuildDesignerState(chart);
                        MarkDirty($"Bound field '{fieldPayload.FieldName}' as the chart series group.");
                        return true;

                    case ReportDesignerChartDropTarget.Values:
                        if (!TryAddChartValueFromField(chart, fieldPayload))
                        {
                            ChartDataStatusMessage = $"Field '{fieldPayload.FieldName}' does not provide a numeric chart value.";
                            return false;
                        }

                        RebuildDesignerState(chart);
                        MarkDirty($"Added field '{fieldPayload.FieldName}' to chart values.");
                        return true;
                }

                break;
        }

        return false;
    }

    private void ApplyFieldToChartSeriesGroup(ChartItem chart, ReportDesignerDataFieldDragPayload payload)
    {
        var targetSeries = ResolveChartSeriesTarget(chart, payload);
        targetSeries.NameExpression = $"Fields.{payload.FieldName}";
        if (string.IsNullOrWhiteSpace(targetSeries.ValueExpression))
        {
            targetSeries.ValueExpression = ResolveFallbackChartValueExpression(payload.DataSet, payload.FieldName, payload.DataType);
        }
    }

    private bool TryAddChartValueFromField(ChartItem chart, ReportDesignerDataFieldDragPayload payload)
    {
        var valueExpression = IsNumericField(payload.DataType)
            ? $"Fields.{payload.FieldName}"
            : ResolveFallbackChartValueExpression(payload.DataSet, payload.FieldName, payload.DataType);
        if (string.IsNullOrWhiteSpace(valueExpression))
        {
            return false;
        }

        chart.Series.Add(new ReportChartSeriesDefinition
        {
            NameExpression = $"'{payload.FieldName}'",
            ValueExpression = valueExpression
        });
        return true;
    }

    private ReportChartSeriesDefinition ResolveChartSeriesTarget(ChartItem chart, ReportDesignerDataFieldDragPayload payload)
    {
        if (SelectedChartSeriesEntry is not null)
        {
            var selectedSeries = chart.Series.FirstOrDefault(candidate =>
                string.Equals(candidate.NameExpression ?? string.Empty, SelectedChartSeriesEntry.NameExpression, StringComparison.Ordinal)
                && string.Equals(candidate.ValueExpression ?? string.Empty, SelectedChartSeriesEntry.ValueExpression, StringComparison.Ordinal));
            if (selectedSeries is not null)
            {
                return selectedSeries;
            }
        }

        if (chart.Series.Count > 0)
        {
            return chart.Series[0];
        }

        var createdSeries = new ReportChartSeriesDefinition
        {
            NameExpression = $"Fields.{payload.FieldName}",
            ValueExpression = ResolveFallbackChartValueExpression(payload.DataSet, payload.FieldName, payload.DataType)
        };
        chart.Series.Add(createdSeries);
        return createdSeries;
    }

    private string ResolveFallbackChartValueExpression(
        ReportDataSetDefinition dataSet,
        string fieldName,
        ReportParameterDataType dataType)
    {
        if (IsNumericField(dataType))
        {
            return $"Fields.{fieldName}";
        }

        var numericField = dataSet.ExpectedFields.FirstOrDefault(field => IsNumericField(field.DataType));
        return numericField is null ? string.Empty : $"Fields.{numericField.Name}";
    }

    private string ResolveDefaultChartValueExpression(ChartItem chart)
    {
        var dataSet = string.IsNullOrWhiteSpace(chart.DataSetId) ? null : FindDataSet(chart.DataSetId);
        if (dataSet is null)
        {
            return "Fields.Value";
        }

        var numericField = dataSet.ExpectedFields.FirstOrDefault(field => IsNumericField(field.DataType));
        return numericField is null ? "Fields.Value" : $"Fields.{numericField.Name}";
    }

    private ReportDataSetDefinition? FindDataSet(string? dataSetId)
    {
        return string.IsNullOrWhiteSpace(dataSetId)
            ? null
            : ReportDefinition.DataSets.FirstOrDefault(candidate => string.Equals(candidate.Id, dataSetId, StringComparison.OrdinalIgnoreCase));
    }
}
