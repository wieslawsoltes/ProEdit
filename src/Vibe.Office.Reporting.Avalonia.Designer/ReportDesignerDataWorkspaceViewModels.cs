using System.Collections.ObjectModel;
using ReactiveUI;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Identifies one node kind in the designer Report Data workspace.
/// </summary>
public enum ReportDesignerDataNodeKind
{
    Group,
    Parameter,
    DataSource,
    DataSet,
    QueryField,
    CalculatedField,
    BuiltInField,
    ImageResource,
    DataSetParameter,
    Filter,
    Sort
}

internal sealed class ReportDesignerBuiltInFieldDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Expression { get; init; }

    public string Description { get; init; } = string.Empty;
}

internal sealed class ReportDesignerImageResourceDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required ReportImageSourceKind SourceKind { get; init; }

    public string? ValueExpression { get; init; }

    public string? MimeType { get; init; }

    public byte[]? EmbeddedData { get; init; }

    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Represents one node in the hierarchical Report Data workspace.
/// </summary>
public sealed class ReportDesignerDataNodeViewModel : ReactiveObject
{
    private readonly ObservableCollection<ReportDesignerDataNodeViewModel> _children = new();
    private bool _isExpanded;
    private bool _isSelected;
    private string _subtitle;
    private string _title;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDataNodeViewModel" /> class.
    /// </summary>
    /// <param name="kind">The node kind.</param>
    /// <param name="title">The node title.</param>
    /// <param name="subtitle">The node subtitle.</param>
    /// <param name="target">The underlying data object represented by the node.</param>
    /// <param name="selectionTarget">The main designer selection target associated with the node.</param>
    public ReportDesignerDataNodeViewModel(
        ReportDesignerDataNodeKind kind,
        string title,
        string subtitle,
        object? target,
        object? selectionTarget)
    {
        Kind = kind;
        _title = title ?? string.Empty;
        _subtitle = subtitle ?? string.Empty;
        Target = target;
        SelectionTarget = selectionTarget;
        Children = new ReadOnlyObservableCollection<ReportDesignerDataNodeViewModel>(_children);
    }

    public ReportDesignerDataNodeKind Kind { get; }

    public object? Target { get; }

    public object? SelectionTarget { get; }

    public ReadOnlyObservableCollection<ReportDesignerDataNodeViewModel> Children { get; }

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value ?? string.Empty);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => this.RaiseAndSetIfChanged(ref _subtitle, value ?? string.Empty);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public string KindLabel => Kind switch
    {
        ReportDesignerDataNodeKind.Group => "Group",
        ReportDesignerDataNodeKind.Parameter => "Param",
        ReportDesignerDataNodeKind.DataSource => "Source",
        ReportDesignerDataNodeKind.DataSet => "DataSet",
        ReportDesignerDataNodeKind.QueryField => "Field",
        ReportDesignerDataNodeKind.CalculatedField => "Calc",
        ReportDesignerDataNodeKind.BuiltInField => "Built-in",
        ReportDesignerDataNodeKind.ImageResource => "Image",
        ReportDesignerDataNodeKind.DataSetParameter => "Query",
        ReportDesignerDataNodeKind.Filter => "Filter",
        ReportDesignerDataNodeKind.Sort => "Sort",
        _ => "Data"
    };

    public bool HasChildren => _children.Count > 0;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool IsFieldNode => Kind is ReportDesignerDataNodeKind.QueryField or ReportDesignerDataNodeKind.CalculatedField;

    public bool IsDataSetNode => Kind == ReportDesignerDataNodeKind.DataSet;

    public bool IsDataSourceNode => Kind == ReportDesignerDataNodeKind.DataSource;

    public bool IsParameterNode => Kind == ReportDesignerDataNodeKind.Parameter;

    public void ClearChildren()
    {
        _children.Clear();
        this.RaisePropertyChanged(nameof(HasChildren));
    }

    public void AddChild(ReportDesignerDataNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
        this.RaisePropertyChanged(nameof(HasChildren));
    }
}

/// <summary>
/// Represents one column in the live dataset preview grid.
/// </summary>
public sealed class ReportDesignerDataPreviewColumnViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDataPreviewColumnViewModel" /> class.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="dataType">The display data type.</param>
    public ReportDesignerDataPreviewColumnViewModel(string name, string dataType)
    {
        Name = name ?? string.Empty;
        DataType = dataType ?? string.Empty;
    }

    public string Name { get; }

    public string DataType { get; }
}

/// <summary>
/// Represents one cell in the live dataset preview grid.
/// </summary>
public sealed class ReportDesignerDataPreviewCellViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDataPreviewCellViewModel" /> class.
    /// </summary>
    /// <param name="text">The display text.</param>
    public ReportDesignerDataPreviewCellViewModel(string text)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; }
}

/// <summary>
/// Represents one row in the live dataset preview grid.
/// </summary>
public sealed class ReportDesignerDataPreviewRowViewModel
{
    private readonly ObservableCollection<ReportDesignerDataPreviewCellViewModel> _cells = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDataPreviewRowViewModel" /> class.
    /// </summary>
    /// <param name="cells">The row cells.</param>
    public ReportDesignerDataPreviewRowViewModel(IEnumerable<ReportDesignerDataPreviewCellViewModel> cells)
    {
        Cells = new ReadOnlyObservableCollection<ReportDesignerDataPreviewCellViewModel>(_cells);
        foreach (var cell in cells)
        {
            _cells.Add(cell);
        }
    }

    public ReadOnlyObservableCollection<ReportDesignerDataPreviewCellViewModel> Cells { get; }
}
