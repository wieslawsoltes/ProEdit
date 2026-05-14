using ReactiveUI;
using ProEdit.Reporting;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Supported design-surface insert tools.
/// </summary>
public enum ReportDesignerInsertTool
{
    None,
    TextBox,
    Tablix,
    Chart,
    Rectangle,
    Line,
    Image,
    Subreport,
    Template
}

/// <summary>
/// Supported grouping-pane drop targets.
/// </summary>
public enum ReportDesignerGroupingDropTarget
{
    None,
    RowGroups,
    ColumnGroups
}

/// <summary>
/// Identifies the tablix hierarchy axis currently being authored.
/// </summary>
public enum ReportDesignerTablixHierarchyAxis
{
    Row,
    Column
}

/// <summary>
/// Represents one selected tablix-member target in the grouping workbench.
/// </summary>
public sealed class ReportDesignerTablixMemberSelectionTarget
{
    /// <summary>
    /// Gets or sets the owning tablix item.
    /// </summary>
    public required TablixItem Tablix { get; init; }

    /// <summary>
    /// Gets or sets the represented member.
    /// </summary>
    public required ReportTablixMemberDefinition Member { get; init; }

    /// <summary>
    /// Gets or sets the hierarchy axis.
    /// </summary>
    public required ReportDesignerTablixHierarchyAxis Axis { get; init; }
}

/// <summary>
/// One transient snap guide rendered on the design surface.
/// </summary>
public sealed class ReportDesignerSnapGuideViewModel
{
    internal ReportDesignerSnapGuideViewModel(bool isHorizontal, double offset, double length)
    {
        IsHorizontal = isHorizontal;
        Offset = Math.Max(0d, offset);
        Length = Math.Max(0d, length);
    }

    /// <summary>
    /// Gets a value indicating whether the guide is horizontal.
    /// </summary>
    public bool IsHorizontal { get; }

    /// <summary>
    /// Gets a value indicating whether the guide is vertical.
    /// </summary>
    public bool IsVertical => !IsHorizontal;

    /// <summary>
    /// Gets the guide offset from the top or left edge.
    /// </summary>
    public double Offset { get; }

    /// <summary>
    /// Gets the visible guide length.
    /// </summary>
    public double Length { get; }
}

/// <summary>
/// One selectable insert tool entry exposed to the workbench chrome.
/// </summary>
public sealed class ReportDesignerInsertToolEntryViewModel : ReactiveObject
{
    private readonly Action<ReportDesignerInsertTool> _selectTool;
    private bool _isActive;

    internal ReportDesignerInsertToolEntryViewModel(
        ReportDesignerInsertTool tool,
        string label,
        string description,
        Action<ReportDesignerInsertTool> selectTool)
    {
        Tool = tool;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description ?? string.Empty;
        _selectTool = selectTool ?? throw new ArgumentNullException(nameof(selectTool));
        SelectCommand = DesignerCommandFactory.Create(() => _selectTool(Tool));
    }

    /// <summary>
    /// Gets the represented tool.
    /// </summary>
    public ReportDesignerInsertTool Tool { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the helper description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool is active.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>
    /// Gets the command that activates the tool.
    /// </summary>
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SelectCommand { get; }
}
