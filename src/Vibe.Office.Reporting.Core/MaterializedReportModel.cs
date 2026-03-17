using Vibe.Office.Documents;

namespace Vibe.Office.Reporting;

/// <summary>
/// Represents one fully-resolved report style.
/// </summary>
public sealed class MaterializedReportStyle
{
    /// <summary>
    /// Gets or sets the resolved font family.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the resolved font size.
    /// </summary>
    public float? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the resolved foreground color text.
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the resolved background color text.
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Creates a deep clone of the style.
    /// </summary>
    /// <returns>The cloned style.</returns>
    public MaterializedReportStyle Clone()
    {
        return new MaterializedReportStyle
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            Foreground = Foreground,
            Background = Background,
            Bold = Bold,
            Italic = Italic
        };
    }
}

/// <summary>
/// Represents one materialized report section.
/// </summary>
public sealed class MaterializedReportSection
{
    /// <summary>
    /// Gets or sets the section identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the section name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the resolved page settings.
    /// </summary>
    public ReportPageSettings PageSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets the resolved bookmark text.
    /// </summary>
    public string? Bookmark { get; set; }

    /// <summary>
    /// Gets the materialized header items.
    /// </summary>
    public List<MaterializedReportItem> HeaderItems { get; } = new();

    /// <summary>
    /// Gets the materialized footer items.
    /// </summary>
    public List<MaterializedReportItem> FooterItems { get; } = new();

    /// <summary>
    /// Gets the materialized body items.
    /// </summary>
    public List<MaterializedReportItem> BodyItems { get; } = new();
}

/// <summary>
/// Captures a resolved drillthrough target.
/// </summary>
public sealed class MaterializedReportDrillthroughAction
{
    /// <summary>
    /// Gets or sets the target report identifier.
    /// </summary>
    public string ReportReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the resolved target parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Base type for one semantic materialized item.
/// </summary>
public abstract class MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the source item identifier.
    /// </summary>
    public string SourceItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item bounds.
    /// </summary>
    public ReportItemBounds Bounds { get; set; }

    /// <summary>
    /// Gets or sets the resolved bookmark text.
    /// </summary>
    public string? Bookmark { get; set; }

    /// <summary>
    /// Gets or sets the resolved tooltip text.
    /// </summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// Gets or sets the source style name.
    /// </summary>
    public string? StyleName { get; set; }

    /// <summary>
    /// Gets or sets the resolved item style.
    /// </summary>
    public MaterializedReportStyle? Style { get; set; }

    /// <summary>
    /// Gets or sets the resolved drillthrough action.
    /// </summary>
    public MaterializedReportDrillthroughAction? DrillthroughAction { get; set; }
}

/// <summary>
/// Describes the semantic text kind.
/// </summary>
public enum MaterializedTextValueKind
{
    /// <summary>
    /// A static text value.
    /// </summary>
    Static,

    /// <summary>
    /// A value produced by expression evaluation.
    /// </summary>
    Expression,

    /// <summary>
    /// A dynamic page number placeholder.
    /// </summary>
    PageNumber,

    /// <summary>
    /// A dynamic total-page-count placeholder.
    /// </summary>
    TotalPages
}

/// <summary>
/// Materialized text item.
/// </summary>
public sealed class MaterializedTextReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the resolved text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the semantic text kind.
    /// </summary>
    public MaterializedTextValueKind ValueKind { get; set; } = MaterializedTextValueKind.Static;
}

/// <summary>
/// Materialized image item.
/// </summary>
public sealed class MaterializedImageReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the binary image payload.
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    /// Gets or sets the resolved content type.
    /// </summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>
    /// Gets or sets the sizing mode.
    /// </summary>
    public ReportSizingMode SizingMode { get; set; } = ReportSizingMode.FitProportional;
}

/// <summary>
/// Materialized line item.
/// </summary>
public sealed class MaterializedLineReportItem : MaterializedReportItem
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
/// Materialized shape item.
/// </summary>
public sealed class MaterializedShapeReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the shape kind.
    /// </summary>
    public ReportShapeKind Shape { get; set; } = ReportShapeKind.Rectangle;
}

/// <summary>
/// Materialized chart item.
/// </summary>
public sealed class MaterializedChartReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the resolved chart model.
    /// </summary>
    public ChartModel? Model { get; set; }
}

/// <summary>
/// Defines one materialized tablix column.
/// </summary>
public sealed class MaterializedTablixColumn
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
/// Defines one materialized tablix cell.
/// </summary>
public sealed class MaterializedTablixCell
{
    /// <summary>
    /// Gets or sets the resolved cell text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source style name.
    /// </summary>
    public string? StyleName { get; set; }

    /// <summary>
    /// Gets or sets the resolved cell style.
    /// </summary>
    public MaterializedReportStyle? Style { get; set; }

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
/// Defines one materialized tablix row.
/// </summary>
public sealed class MaterializedTablixRow
{
    /// <summary>
    /// Gets or sets a value indicating whether the row is a header row.
    /// </summary>
    public bool IsHeader { get; set; }

    /// <summary>
    /// Gets the materialized row cells.
    /// </summary>
    public List<MaterializedTablixCell> Cells { get; } = new();
}

/// <summary>
/// Materialized tablix item.
/// </summary>
public sealed class MaterializedTablixReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets a value indicating whether header rows repeat on each page.
    /// </summary>
    public bool RepeatHeaderRows { get; set; }

    /// <summary>
    /// Gets the materialized columns.
    /// </summary>
    public List<MaterializedTablixColumn> Columns { get; } = new();

    /// <summary>
    /// Gets the materialized rows.
    /// </summary>
    public List<MaterializedTablixRow> Rows { get; } = new();
}

/// <summary>
/// Materialized subreport item.
/// </summary>
public sealed class MaterializedSubreportReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the nested subreport.
    /// </summary>
    public MaterializedReport? Report { get; set; }
}

/// <summary>
/// Materialized narrative-template item.
/// </summary>
public sealed class MaterializedDocumentTemplateReportItem : MaterializedReportItem
{
    /// <summary>
    /// Gets or sets the resolved template format.
    /// </summary>
    public ReportDocumentTemplateFormat TemplateFormat { get; set; } = ReportDocumentTemplateFormat.Vibe;

    /// <summary>
    /// Gets or sets the resolved template content payload.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Gets the resolved string bindings.
    /// </summary>
    public Dictionary<string, string> Bindings { get; } = new(StringComparer.OrdinalIgnoreCase);
}
