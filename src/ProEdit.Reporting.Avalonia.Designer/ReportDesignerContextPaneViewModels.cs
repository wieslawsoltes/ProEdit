using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;
using ProEdit.Reporting;

namespace ProEdit.Reporting.Avalonia.Designer;

internal enum ReportDesignerChartDropTarget
{
    None,
    CategoryGroups,
    SeriesGroups,
    Values
}

internal sealed class ReportDesignerParameterLayoutCellViewModel : ReactiveObject
{
    private bool _isSelected;

    internal ReportDesignerParameterLayoutCellViewModel(
        int rowIndex,
        int columnIndex,
        ReportParameterDefinition? parameter,
        Action<ReportDesignerParameterLayoutCellViewModel> selectAction)
    {
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        Parameter = parameter;
        ArgumentNullException.ThrowIfNull(selectAction);
        SelectCommand = DesignerCommandFactory.Create(() => selectAction(this));
    }

    public int RowIndex { get; }

    public int ColumnIndex { get; }

    public ReportParameterDefinition? Parameter { get; }

    public string Title => Parameter is null
        ? "Empty"
        : string.IsNullOrWhiteSpace(Parameter.DisplayName) ? Parameter.Id : Parameter.DisplayName;

    public string Subtitle => Parameter is null
        ? $"Row {RowIndex + 1}, Column {ColumnIndex + 1}"
        : string.IsNullOrWhiteSpace(Parameter.Prompt) ? Parameter.Id : Parameter.Prompt!;

    public string ParameterId => Parameter?.Id ?? string.Empty;

    public bool HasParameter => Parameter is not null;

    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public ReactiveCommand<Unit, Unit> SelectCommand { get; }
}

internal sealed class ReportDesignerParameterLayoutRowViewModel
{
    private readonly ObservableCollection<ReportDesignerParameterLayoutCellViewModel> _cells = new();

    internal ReportDesignerParameterLayoutRowViewModel(int rowIndex, IEnumerable<ReportDesignerParameterLayoutCellViewModel> cells)
    {
        RowIndex = rowIndex;
        Cells = new ReadOnlyObservableCollection<ReportDesignerParameterLayoutCellViewModel>(_cells);
        foreach (var cell in cells)
        {
            _cells.Add(cell);
        }
    }

    public int RowIndex { get; }

    public ReadOnlyObservableCollection<ReportDesignerParameterLayoutCellViewModel> Cells { get; }
}

internal sealed class ReportDesignerChartSeriesEntryViewModel : ReactiveObject
{
    private readonly ReportChartSeriesDefinition _series;
    private readonly Action _notifyChanged;
    private bool _isSelected;

    internal ReportDesignerChartSeriesEntryViewModel(
        ReportChartSeriesDefinition series,
        Action notifyChanged)
    {
        _series = series ?? throw new ArgumentNullException(nameof(series));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
    }

    public string Label => string.IsNullOrWhiteSpace(_series.NameExpression)
        ? "Series"
        : _series.NameExpression!;

    public string NameExpression
    {
        get => _series.NameExpression ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_series.NameExpression ?? string.Empty, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _series.NameExpression = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(Label));
            _notifyChanged();
        }
    }

    public string ValueExpression
    {
        get => _series.ValueExpression ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_series.ValueExpression ?? string.Empty, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _series.ValueExpression = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            this.RaisePropertyChanged();
            _notifyChanged();
        }
    }

    public string ColorExpression
    {
        get => _series.ColorExpression ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_series.ColorExpression ?? string.Empty, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _series.ColorExpression = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            this.RaisePropertyChanged();
            _notifyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
