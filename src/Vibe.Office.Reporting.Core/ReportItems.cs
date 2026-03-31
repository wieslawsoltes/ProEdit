using System.Text.Json.Serialization;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting;

/// <summary>
/// Represents rectangular bounds for a report item.
/// </summary>
/// <param name="X">The x-coordinate.</param>
/// <param name="Y">The y-coordinate.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct ReportItemBounds(float X, float Y, float Width, float Height);

/// <summary>
/// Base type for all report items.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "itemType")]
[JsonDerivedType(typeof(TextItem), "TextItem")]
[JsonDerivedType(typeof(ImageItem), "ImageItem")]
[JsonDerivedType(typeof(LineItem), "LineItem")]
[JsonDerivedType(typeof(ShapeItem), "ShapeItem")]
[JsonDerivedType(typeof(ContainerItem), "ContainerItem")]
[JsonDerivedType(typeof(ChartItem), "ChartItem")]
[JsonDerivedType(typeof(GaugeItem), "GaugeItem")]
[JsonDerivedType(typeof(TablixItem), "TablixItem")]
[JsonDerivedType(typeof(SubreportItem), "SubreportItem")]
[JsonDerivedType(typeof(DocumentTemplateItem), "DocumentTemplateItem")]
public abstract class ReportItem
{
    /// <summary>
    /// Gets or sets the item identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item bounds.
    /// </summary>
    public ReportItemBounds Bounds { get; set; }

    /// <summary>
    /// Gets or sets the z-order.
    /// </summary>
    public int ZIndex { get; set; }

    /// <summary>
    /// Gets or sets the optional visibility expression.
    /// </summary>
    public string? VisibilityExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional bookmark expression.
    /// </summary>
    public string? BookmarkExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional tooltip expression.
    /// </summary>
    public string? TooltipExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional drillthrough action.
    /// </summary>
    public ReportDrillthroughAction? DrillthroughAction { get; set; }

    /// <summary>
    /// Gets or sets the optional page break definition.
    /// </summary>
    public ReportPageBreakDefinition? PageBreak { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item should stay together on one page when possible.
    /// </summary>
    public bool KeepTogether { get; set; }

    /// <summary>
    /// Gets or sets the optional style name.
    /// </summary>
    public string? StyleName { get; set; }
}

/// <summary>
/// Defines a drillthrough action.
/// </summary>
public sealed class ReportDrillthroughAction
{
    /// <summary>
    /// Gets or sets the target report reference.
    /// </summary>
    public string ReportReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the parameter bindings passed to the target report.
    /// </summary>
    public List<ReportParameterBinding> Parameters { get; set; } = new();
}

/// <summary>
/// Defines a parameter binding.
/// </summary>
public sealed class ReportParameterBinding
{
    /// <summary>
    /// Gets or sets the target parameter identifier.
    /// </summary>
    public string ParameterId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bound value expression.
    /// </summary>
    public string ValueExpression { get; set; } = string.Empty;
}

/// <summary>
/// Text report item.
/// </summary>
public sealed class TextItem : ReportItem
{
    /// <summary>
    /// Gets or sets the static text content.
    /// </summary>
    public string? StaticText { get; set; }

    /// <summary>
    /// Gets or sets the value expression.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional format string.
    /// </summary>
    public string? FormatString { get; set; }

    /// <summary>
    /// Gets or sets whether the text can grow.
    /// </summary>
    public bool CanGrow { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the text can shrink.
    /// </summary>
    public bool CanShrink { get; set; }

    /// <summary>
    /// Gets the optional paragraph and run structure preserved for rich RDL textboxes.
    /// </summary>
    public List<ReportTextParagraph> Paragraphs { get; set; } = new();
}

/// <summary>
/// Defines one logical paragraph within a text report item.
/// </summary>
public sealed class ReportTextParagraph
{
    /// <summary>
    /// Gets or sets the optional paragraph alignment override.
    /// </summary>
    public ParagraphAlignment? TextAlign { get; set; }

    /// <summary>
    /// Gets the text runs in the paragraph.
    /// </summary>
    public List<ReportTextRun> Runs { get; set; } = new();
}

/// <summary>
/// Defines one text run within a paragraph.
/// </summary>
public sealed class ReportTextRun
{
    /// <summary>
    /// Gets or sets the static text content.
    /// </summary>
    public string? StaticText { get; set; }

    /// <summary>
    /// Gets or sets the optional value expression.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional style name applied to the run.
    /// </summary>
    public string? StyleName { get; set; }
}

/// <summary>
/// Image report item.
/// </summary>
public sealed class ImageItem : ReportItem
{
    /// <summary>
    /// Gets or sets the image source kind.
    /// </summary>
    public ReportImageSourceKind SourceKind { get; set; } = ReportImageSourceKind.Expression;

    /// <summary>
    /// Gets or sets the source expression or URI.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the MIME type.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the optional embedded image bytes.
    /// </summary>
    public byte[]? EmbeddedData { get; set; }

    /// <summary>
    /// Gets or sets the sizing mode.
    /// </summary>
    public ReportSizingMode SizingMode { get; set; } = ReportSizingMode.FitProportional;
}

/// <summary>
/// Supported image source kinds.
/// </summary>
public enum ReportImageSourceKind
{
    /// <summary>
    /// Source is provided by an expression.
    /// </summary>
    Expression,

    /// <summary>
    /// Source is embedded in the template.
    /// </summary>
    Embedded,

    /// <summary>
    /// Source is a URI.
    /// </summary>
    Uri
}

/// <summary>
/// Supported sizing modes.
/// </summary>
public enum ReportSizingMode
{
    /// <summary>
    /// Use the original size.
    /// </summary>
    OriginalSize,

    /// <summary>
    /// Stretch to fit the bounds.
    /// </summary>
    Stretch,

    /// <summary>
    /// Fit while preserving aspect ratio.
    /// </summary>
    FitProportional
}

/// <summary>
/// Line report item.
/// </summary>
public sealed class LineItem : ReportItem
{
    /// <summary>
    /// Gets or sets the end x-coordinate.
    /// </summary>
    public float X2 { get; set; }

    /// <summary>
    /// Gets or sets the end y-coordinate.
    /// </summary>
    public float Y2 { get; set; }
}

/// <summary>
/// Shape report item.
/// </summary>
public sealed class ShapeItem : ReportItem
{
    /// <summary>
    /// Gets or sets the shape kind.
    /// </summary>
    public ReportShapeKind Shape { get; set; } = ReportShapeKind.Rectangle;
}

/// <summary>
/// Container report item that preserves nested layout items.
/// </summary>
public sealed class ContainerItem : ReportItem
{
    /// <summary>
    /// Gets the nested child items.
    /// </summary>
    public List<ReportItem> Items { get; set; } = new();
}

/// <summary>
/// Supported shape kinds.
/// </summary>
public enum ReportShapeKind
{
    /// <summary>
    /// Rectangle shape.
    /// </summary>
    Rectangle,

    /// <summary>
    /// Rounded rectangle shape.
    /// </summary>
    RoundedRectangle,

    /// <summary>
    /// Ellipse shape.
    /// </summary>
    Ellipse
}

/// <summary>
/// Supported gauge kinds.
/// </summary>
public enum ReportGaugeKind
{
    /// <summary>
    /// State-indicator style gauge.
    /// </summary>
    StateIndicator,

    /// <summary>
    /// Linear gauge.
    /// </summary>
    Linear,

    /// <summary>
    /// Radial gauge.
    /// </summary>
    Radial
}

/// <summary>
/// Chart report item.
/// </summary>
public sealed class ChartItem : ReportItem
{
    /// <summary>
    /// Gets or sets the chart type.
    /// </summary>
    public ChartType Type { get; set; } = ChartType.Bar;

    /// <summary>
    /// Gets or sets the bar direction when the chart is a bar chart.
    /// </summary>
    public ChartBarDirection BarDirection { get; set; } = ChartBarDirection.Column;

    /// <summary>
    /// Gets or sets the backing dataset identifier.
    /// </summary>
    public string? DataSetId { get; set; }

    /// <summary>
    /// Gets or sets the category expression.
    /// </summary>
    public string? CategoryExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional category label expression.
    /// </summary>
    public string? CategoryLabelExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional category sort expression.
    /// </summary>
    public string? CategorySortExpression { get; set; }

    /// <summary>
    /// Gets or sets the category sort direction.
    /// </summary>
    public ReportSortDirection CategorySortDirection { get; set; } = ReportSortDirection.Ascending;

    /// <summary>
    /// Gets or sets the title expression.
    /// </summary>
    public string? TitleExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional chart title text style.
    /// </summary>
    public ChartTextStyle? TitleTextStyle { get; set; }

    /// <summary>
    /// Gets or sets the chart title position.
    /// </summary>
    public ChartTitlePosition TitlePosition { get; set; } = ChartTitlePosition.Center;

    /// <summary>
    /// Gets or sets the imported RDL palette name.
    /// </summary>
    public string? PaletteName { get; set; }

    /// <summary>
    /// Gets or sets the optional chart area style.
    /// </summary>
    public ChartStyle? ChartAreaStyle { get; set; }

    /// <summary>
    /// Gets or sets the optional plot area style.
    /// </summary>
    public ChartStyle? PlotAreaStyle { get; set; }

    /// <summary>
    /// Gets or sets the optional chart legend definition.
    /// </summary>
    public ChartLegend? Legend { get; set; }

    /// <summary>
    /// Gets the cartesian/radar axis definitions.
    /// </summary>
    public List<ChartAxis> Axes { get; set; } = new();

    /// <summary>
    /// Gets the chart category hierarchy levels.
    /// </summary>
    public List<ReportChartCategoryLevelDefinition> CategoryLevels { get; set; } = new();

    /// <summary>
    /// Gets the series definitions.
    /// </summary>
    public List<ReportChartSeriesDefinition> Series { get; set; } = new();
}

/// <summary>
/// Gauge report item.
/// </summary>
public sealed class GaugeItem : ReportItem
{
    /// <summary>
    /// Gets or sets the backing dataset identifier.
    /// </summary>
    public string? DataSetId { get; set; }

    /// <summary>
    /// Gets or sets the gauge kind.
    /// </summary>
    public ReportGaugeKind GaugeKind { get; set; } = ReportGaugeKind.Radial;

    /// <summary>
    /// Gets or sets the main value expression.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the minimum value expression.
    /// </summary>
    public string? MinimumExpression { get; set; }

    /// <summary>
    /// Gets or sets the maximum value expression.
    /// </summary>
    public string? MaximumExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional target value expression.
    /// </summary>
    public string? TargetValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional label expression.
    /// </summary>
    public string? LabelExpression { get; set; }

    /// <summary>
    /// Gets or sets the original imported RDL payload when the gauge contains unsupported styling details.
    /// </summary>
    public string? RawRdlXml { get; set; }
}

/// <summary>
/// Defines a chart series binding.
/// </summary>
public sealed class ReportChartSeriesDefinition
{
    /// <summary>
    /// Gets or sets the chart type for the series.
    /// </summary>
    public ChartType? Type { get; set; }

    /// <summary>
    /// Gets or sets the bar direction when the series uses a bar chart.
    /// </summary>
    public ChartBarDirection? BarDirection { get; set; }

    /// <summary>
    /// Gets or sets the series name expression.
    /// </summary>
    public string? NameExpression { get; set; }

    /// <summary>
    /// Gets or sets the series value expression.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional color expression.
    /// </summary>
    public string? ColorExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional static chart style.
    /// </summary>
    public ChartStyle? Style { get; set; }

    /// <summary>
    /// Gets or sets the series-level data label settings.
    /// </summary>
    public ChartDataLabelSettings? DataLabels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether line-style series should render with a smoothed spline.
    /// </summary>
    public bool UseSmoothedLine { get; set; }
}

/// <summary>
/// Defines one chart category hierarchy level.
/// </summary>
public sealed class ReportChartCategoryLevelDefinition
{
    /// <summary>
    /// Gets or sets the group expression for the level.
    /// </summary>
    public string? GroupExpression { get; set; }

    /// <summary>
    /// Gets or sets the label expression for the level.
    /// </summary>
    public string? LabelExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional sort expression for the level.
    /// </summary>
    public string? SortExpression { get; set; }

    /// <summary>
    /// Gets or sets the sort direction for the level.
    /// </summary>
    public ReportSortDirection SortDirection { get; set; } = ReportSortDirection.Ascending;
}

/// <summary>
/// Tablix report item.
/// </summary>
public sealed class TablixItem : ReportItem
{
    /// <summary>
    /// Gets or sets the backing dataset identifier.
    /// </summary>
    public string? DataSetId { get; set; }

    /// <summary>
    /// Gets or sets whether header rows repeat on each page.
    /// </summary>
    public bool RepeatHeaderRows { get; set; } = true;

    /// <summary>
    /// Gets the tablix-level filters applied after dataset execution and before grouping/materialization.
    /// </summary>
    public List<ReportFilterDefinition> Filters { get; set; } = new();

    /// <summary>
    /// Gets the column definitions.
    /// </summary>
    public List<ReportTablixColumnDefinition> Columns { get; set; } = new();

    /// <summary>
    /// Gets the row definitions.
    /// </summary>
    public List<ReportTablixRowDefinition> Rows { get; set; } = new();

    /// <summary>
    /// Gets the row hierarchy member definitions in depth-first leaf order.
    /// </summary>
    public List<ReportTablixMemberDefinition> RowMembers { get; set; } = new();

    /// <summary>
    /// Gets the column hierarchy member definitions in depth-first leaf order.
    /// </summary>
    public List<ReportTablixMemberDefinition> ColumnMembers { get; set; } = new();
}

/// <summary>
/// Defines one tablix column.
/// </summary>
public sealed class ReportTablixColumnDefinition
{
    /// <summary>
    /// Gets or sets the column identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column width.
    /// </summary>
    public float Width { get; set; }
}

/// <summary>
/// Defines one tablix row.
/// </summary>
public sealed class ReportTablixRowDefinition
{
    /// <summary>
    /// Gets or sets the row identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a header row.
    /// </summary>
    public bool IsHeader { get; set; }

    /// <summary>
    /// Gets or sets the explicit row height.
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// Gets the row cells.
    /// </summary>
    public List<ReportTablixCellDefinition> Cells { get; set; } = new();
}

/// <summary>
/// Defines the semantic tablix member kind.
/// </summary>
public enum ReportTablixMemberKind
{
    /// <summary>
    /// A static member rendered once for the current scope.
    /// </summary>
    Static,

    /// <summary>
    /// A dynamic grouping member rendered once per group instance.
    /// </summary>
    Group,

    /// <summary>
    /// A details member rendered once per row in the current scope.
    /// </summary>
    Details
}

/// <summary>
/// Defines one tablix row hierarchy member.
/// </summary>
public sealed class ReportTablixMemberDefinition
{
    /// <summary>
    /// Gets or sets the member identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the member kind.
    /// </summary>
    public ReportTablixMemberKind Kind { get; set; } = ReportTablixMemberKind.Static;

    /// <summary>
    /// Gets or sets the source group name.
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Gets or sets the grouping expression.
    /// </summary>
    public string? GroupExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional sort expression.
    /// </summary>
    public string? SortExpression { get; set; }

    /// <summary>
    /// Gets or sets the sort direction.
    /// </summary>
    public ReportSortDirection SortDirection { get; set; } = ReportSortDirection.Ascending;

    /// <summary>
    /// Gets or sets the visibility expression.
    /// </summary>
    public string? VisibilityExpression { get; set; }

    /// <summary>
    /// Gets or sets the optional toggle item identifier.
    /// </summary>
    public string? ToggleItemId { get; set; }

    /// <summary>
    /// Gets or sets whether the member repeats on new pages.
    /// </summary>
    public bool RepeatOnNewPage { get; set; }

    /// <summary>
    /// Gets or sets the keep-with-group mode text.
    /// </summary>
    public string? KeepWithGroup { get; set; }

    /// <summary>
    /// Gets or sets the optional page break.
    /// </summary>
    public ReportPageBreakDefinition? PageBreak { get; set; }

    /// <summary>
    /// Gets or sets the mapped body row index for leaf members.
    /// </summary>
    public int? RowDefinitionIndex { get; set; }

    /// <summary>
    /// Gets or sets the mapped body column index for leaf members.
    /// </summary>
    public int? ColumnDefinitionIndex { get; set; }

    /// <summary>
    /// Gets the nested child members.
    /// </summary>
    public List<ReportTablixMemberDefinition> Members { get; set; } = new();
}

/// <summary>
/// Defines one tablix cell.
/// </summary>
public sealed class ReportTablixCellDefinition
{
    /// <summary>
    /// Gets or sets static cell text.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the value expression.
    /// </summary>
    public string? ValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the format string.
    /// </summary>
    public string? FormatString { get; set; }

    /// <summary>
    /// Gets or sets the style name.
    /// </summary>
    public string? StyleName { get; set; }

    /// <summary>
    /// Gets or sets the optional nested cell content item.
    /// </summary>
    public ReportItem? ContentItem { get; set; }

    /// <summary>
    /// Gets or sets the row span.
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// Gets or sets the column span.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;
}

/// <summary>
/// Subreport report item.
/// </summary>
public sealed class SubreportItem : ReportItem
{
    /// <summary>
    /// Gets or sets the referenced report identifier.
    /// </summary>
    public string ReportReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the parameter bindings.
    /// </summary>
    public List<ReportParameterBinding> Parameters { get; set; } = new();
}

/// <summary>
/// Narrative template report item.
/// </summary>
public sealed class DocumentTemplateItem : ReportItem
{
    /// <summary>
    /// Gets or sets the shared template identifier.
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the embedded template format.
    /// </summary>
    public ReportDocumentTemplateFormat TemplateFormat { get; set; } = ReportDocumentTemplateFormat.Vibe;

    /// <summary>
    /// Gets or sets embedded template content.
    /// </summary>
    public string? EmbeddedContent { get; set; }

    /// <summary>
    /// Gets the binding expressions for the template.
    /// </summary>
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
