using Vibe.Office.Documents;

namespace Vibe.Office.Reporting;

/// <summary>
/// Defines a report template and its static metadata.
/// </summary>
public sealed class ReportDefinition
{
    /// <summary>
    /// Current schema version for native VibeOffice report templates.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets or sets the template schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets or sets the template identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the report-level default font family imported from RDL extensions such as <c>df:DefaultFontFamily</c>.
    /// </summary>
    public string? DefaultFontFamily { get; set; }

    /// <summary>
    /// Gets the report sections.
    /// </summary>
    public List<ReportSection> Sections { get; set; } = new();

    /// <summary>
    /// Gets the parameter definitions.
    /// </summary>
    public List<ReportParameterDefinition> Parameters { get; set; } = new();

    /// <summary>
    /// Gets the parameter-pane layout metadata used by authoring tools.
    /// </summary>
    public ReportParameterLayoutDefinition ParameterLayout { get; set; } = new();

    /// <summary>
    /// Gets the data source definitions.
    /// </summary>
    public List<ReportDataSourceDefinition> DataSources { get; set; } = new();

    /// <summary>
    /// Gets the dataset definitions.
    /// </summary>
    public List<ReportDataSetDefinition> DataSets { get; set; } = new();

    /// <summary>
    /// Gets the named style definitions.
    /// </summary>
    public List<ReportStyleDefinition> Styles { get; set; } = new();

    /// <summary>
    /// Gets the shared narrative template definitions.
    /// </summary>
    public List<ReportSharedTemplateDefinition> SharedTemplates { get; set; } = new();

    /// <summary>
    /// Gets arbitrary template metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether whitespace in containers is consumed when nested content grows.
    /// </summary>
    public bool ConsumeContainerWhitespace { get; set; }
}

/// <summary>
/// Defines authoring-time layout metadata for the report-parameter pane.
/// </summary>
public sealed class ReportParameterLayoutDefinition
{
    /// <summary>
    /// Gets or sets a value indicating whether the parameter pane is shown on the design surface.
    /// </summary>
    public bool ShowOnDesignSurface { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of layout columns.
    /// </summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of layout rows.
    /// </summary>
    public int RowCount { get; set; } = 1;

    /// <summary>
    /// Gets the occupied parameter layout cells.
    /// </summary>
    public List<ReportParameterLayoutCellDefinition> Cells { get; set; } = new();
}

/// <summary>
/// Defines one positioned parameter cell within the parameter-pane layout grid.
/// </summary>
public sealed class ReportParameterLayoutCellDefinition
{
    /// <summary>
    /// Gets or sets the parameter identifier.
    /// </summary>
    public string ParameterId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the row index.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// Gets or sets the column index.
    /// </summary>
    public int ColumnIndex { get; set; }
}

/// <summary>
/// Defines a paginated section within a report.
/// </summary>
public sealed class ReportSection
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
    /// Gets the page settings for the section.
    /// </summary>
    public ReportPageSettings PageSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets an optional visibility expression.
    /// </summary>
    public string? VisibilityExpression { get; set; }

    /// <summary>
    /// Gets or sets an optional bookmark expression.
    /// </summary>
    public string? BookmarkExpression { get; set; }

    /// <summary>
    /// Gets the header items.
    /// </summary>
    public List<ReportItem> HeaderItems { get; set; } = new();

    /// <summary>
    /// Gets the footer items.
    /// </summary>
    public List<ReportItem> FooterItems { get; set; } = new();

    /// <summary>
    /// Gets the body items.
    /// </summary>
    public List<ReportItem> BodyItems { get; set; } = new();
}

/// <summary>
/// Defines section page settings.
/// </summary>
public sealed class ReportPageSettings
{
    /// <summary>
    /// Gets or sets the page width.
    /// </summary>
    public float Width { get; set; } = 816f;

    /// <summary>
    /// Gets or sets the page height.
    /// </summary>
    public float Height { get; set; } = 1056f;

    /// <summary>
    /// Gets or sets the page orientation.
    /// </summary>
    public ReportPageOrientation Orientation { get; set; } = ReportPageOrientation.Portrait;

    /// <summary>
    /// Gets or sets the left margin.
    /// </summary>
    public float MarginLeft { get; set; } = 72f;

    /// <summary>
    /// Gets or sets the top margin.
    /// </summary>
    public float MarginTop { get; set; } = 72f;

    /// <summary>
    /// Gets or sets the right margin.
    /// </summary>
    public float MarginRight { get; set; } = 72f;

    /// <summary>
    /// Gets or sets the bottom margin.
    /// </summary>
    public float MarginBottom { get; set; } = 72f;

    /// <summary>
    /// Gets or sets the page header band height.
    /// </summary>
    public float HeaderHeight { get; set; }

    /// <summary>
    /// Gets or sets the page footer band height.
    /// </summary>
    public float FooterHeight { get; set; }

    /// <summary>
    /// Gets or sets the column count.
    /// </summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the gap between columns.
    /// </summary>
    public float ColumnGap { get; set; } = 18f;
}

/// <summary>
/// Supported page orientations.
/// </summary>
public enum ReportPageOrientation
{
    /// <summary>
    /// Portrait orientation.
    /// </summary>
    Portrait,

    /// <summary>
    /// Landscape orientation.
    /// </summary>
    Landscape
}

/// <summary>
/// Defines a report parameter.
/// </summary>
public sealed class ReportParameterDefinition
{
    /// <summary>
    /// Gets or sets the parameter identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parameter data type.
    /// </summary>
    public ReportParameterDataType DataType { get; set; } = ReportParameterDataType.String;

    /// <summary>
    /// Gets or sets a value indicating whether multiple values are allowed.
    /// </summary>
    public bool IsMultiValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether null is allowed.
    /// </summary>
    public bool AllowNull { get; set; }

    /// <summary>
    /// Gets or sets the default value expression.
    /// </summary>
    public string? DefaultValueExpression { get; set; }

    /// <summary>
    /// Gets or sets the prompt text.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Gets or sets the dataset used for available values.
    /// </summary>
    public string? AvailableValuesDataSetId { get; set; }

    /// <summary>
    /// Gets or sets the available values field.
    /// </summary>
    public string? ValueField { get; set; }

    /// <summary>
    /// Gets or sets the available values label field.
    /// </summary>
    public string? LabelField { get; set; }

    /// <summary>
    /// Gets or sets the parameter visibility mode.
    /// </summary>
    public ReportParameterVisibility Visibility { get; set; } = ReportParameterVisibility.Visible;

    /// <summary>
    /// Gets the parameter dependencies.
    /// </summary>
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>
/// Supported parameter data types.
/// </summary>
public enum ReportParameterDataType
{
    /// <summary>
    /// String parameter.
    /// </summary>
    String,

    /// <summary>
    /// Integer parameter.
    /// </summary>
    Integer,

    /// <summary>
    /// Floating point parameter.
    /// </summary>
    Number,

    /// <summary>
    /// Decimal parameter.
    /// </summary>
    Decimal,

    /// <summary>
    /// Boolean parameter.
    /// </summary>
    Boolean,

    /// <summary>
    /// Date parameter.
    /// </summary>
    Date,

    /// <summary>
    /// Date and time parameter.
    /// </summary>
    DateTime
}

/// <summary>
/// Visibility modes for parameters.
/// </summary>
public enum ReportParameterVisibility
{
    /// <summary>
    /// Visible to users.
    /// </summary>
    Visible,

    /// <summary>
    /// Hidden from the prompt UI.
    /// </summary>
    Hidden,

    /// <summary>
    /// Internal parameter.
    /// </summary>
    Internal
}

/// <summary>
/// Defines a named shared style.
/// </summary>
public sealed class ReportStyleDefinition
{
    /// <summary>
    /// Gets or sets the style identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional parent style identifier.
    /// </summary>
    public string? ParentStyleId { get; set; }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public float? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the foreground color.
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the foreground color expression.
    /// </summary>
    public string? ForegroundExpression { get; set; }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Gets or sets the background color expression.
    /// </summary>
    public string? BackgroundExpression { get; set; }

    /// <summary>
    /// Gets or sets the background gradient type.
    /// </summary>
    public ReportBackgroundGradientType? BackgroundGradientType { get; set; }

    /// <summary>
    /// Gets or sets the background gradient end color.
    /// </summary>
    public string? BackgroundGradientEndColor { get; set; }

    /// <summary>
    /// Gets or sets the background gradient end color expression.
    /// </summary>
    public string? BackgroundGradientEndColorExpression { get; set; }

    /// <summary>
    /// Gets or sets whether the style is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the style is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the default border definition.
    /// </summary>
    public ReportBorderDefinition? Border { get; set; }

    /// <summary>
    /// Gets or sets the top border override.
    /// </summary>
    public ReportBorderDefinition? TopBorder { get; set; }

    /// <summary>
    /// Gets or sets the bottom border override.
    /// </summary>
    public ReportBorderDefinition? BottomBorder { get; set; }

    /// <summary>
    /// Gets or sets the left border override.
    /// </summary>
    public ReportBorderDefinition? LeftBorder { get; set; }

    /// <summary>
    /// Gets or sets the right border override.
    /// </summary>
    public ReportBorderDefinition? RightBorder { get; set; }

    /// <summary>
    /// Gets or sets the left padding.
    /// </summary>
    public float? PaddingLeft { get; set; }

    /// <summary>
    /// Gets or sets the right padding.
    /// </summary>
    public float? PaddingRight { get; set; }

    /// <summary>
    /// Gets or sets the top padding.
    /// </summary>
    public float? PaddingTop { get; set; }

    /// <summary>
    /// Gets or sets the bottom padding.
    /// </summary>
    public float? PaddingBottom { get; set; }

    /// <summary>
    /// Gets or sets the paragraph alignment.
    /// </summary>
    public ParagraphAlignment? TextAlign { get; set; }

    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    public ReportVerticalAlignment? VerticalAlign { get; set; }

    /// <summary>
    /// Gets or sets the text decoration.
    /// </summary>
    public ReportTextDecoration? TextDecoration { get; set; }
}

/// <summary>
/// Defines one border segment.
/// </summary>
public sealed class ReportBorderDefinition
{
    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Gets or sets the border color expression.
    /// </summary>
    public string? ColorExpression { get; set; }

    /// <summary>
    /// Gets or sets the border line style.
    /// </summary>
    public ReportBorderLineStyle? Style { get; set; }

    /// <summary>
    /// Gets or sets the border width.
    /// </summary>
    public float? Width { get; set; }
}

/// <summary>
/// Supported background gradient types.
/// </summary>
public enum ReportBackgroundGradientType
{
    /// <summary>
    /// Default gradient behavior.
    /// </summary>
    Default,

    /// <summary>
    /// No gradient.
    /// </summary>
    None,

    /// <summary>
    /// Horizontal left-to-right gradient.
    /// </summary>
    LeftRight,

    /// <summary>
    /// Vertical top-to-bottom gradient.
    /// </summary>
    TopBottom,

    /// <summary>
    /// Radial gradient from the center.
    /// </summary>
    Center,

    /// <summary>
    /// Diagonal gradient from top-left to bottom-right.
    /// </summary>
    DiagonalLeft,

    /// <summary>
    /// Diagonal gradient from top-right to bottom-left.
    /// </summary>
    DiagonalRight,

    /// <summary>
    /// Horizontal gradient spreading from the center.
    /// </summary>
    HorizontalCenter,

    /// <summary>
    /// Vertical gradient spreading from the center.
    /// </summary>
    VerticalCenter
}

/// <summary>
/// Supported border styles.
/// </summary>
public enum ReportBorderLineStyle
{
    /// <summary>
    /// No border.
    /// </summary>
    None,

    /// <summary>
    /// Solid border.
    /// </summary>
    Solid,

    /// <summary>
    /// Dashed border.
    /// </summary>
    Dashed,

    /// <summary>
    /// Dotted border.
    /// </summary>
    Dotted,

    /// <summary>
    /// Double border.
    /// </summary>
    Double
}

/// <summary>
/// Supported vertical alignments.
/// </summary>
public enum ReportVerticalAlignment
{
    /// <summary>
    /// Top aligned.
    /// </summary>
    Top,

    /// <summary>
    /// Middle aligned.
    /// </summary>
    Middle,

    /// <summary>
    /// Bottom aligned.
    /// </summary>
    Bottom
}

/// <summary>
/// Supported text decorations.
/// </summary>
public enum ReportTextDecoration
{
    /// <summary>
    /// No decoration.
    /// </summary>
    None,

    /// <summary>
    /// Underline decoration.
    /// </summary>
    Underline,

    /// <summary>
    /// Strikethrough decoration.
    /// </summary>
    LineThrough
}

/// <summary>
/// Defines one page break.
/// </summary>
public sealed class ReportPageBreakDefinition
{
    /// <summary>
    /// Gets or sets the break location.
    /// </summary>
    public ReportPageBreakLocation Location { get; set; } = ReportPageBreakLocation.Start;

    /// <summary>
    /// Gets or sets the disabled expression.
    /// </summary>
    public string? DisabledExpression { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether page numbering resets after the break.
    /// </summary>
    public bool ResetPageNumber { get; set; }
}

/// <summary>
/// Supported page break locations.
/// </summary>
public enum ReportPageBreakLocation
{
    /// <summary>
    /// Break before the scope.
    /// </summary>
    Start,

    /// <summary>
    /// Break after the scope.
    /// </summary>
    End,

    /// <summary>
    /// Break before and after the scope.
    /// </summary>
    StartAndEnd,

    /// <summary>
    /// Break between repeated instances of the scope.
    /// </summary>
    Between
}

/// <summary>
/// Defines a reusable narrative template reference.
/// </summary>
public sealed class ReportSharedTemplateDefinition
{
    /// <summary>
    /// Gets or sets the template identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template format.
    /// </summary>
    public ReportDocumentTemplateFormat Format { get; set; } = ReportDocumentTemplateFormat.Vibe;

    /// <summary>
    /// Gets or sets a value indicating whether the template content is embedded.
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// Gets or sets the source path or URI.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the embedded content payload.
    /// </summary>
    public string? Content { get; set; }
}

/// <summary>
/// Supported document-template formats.
/// </summary>
public enum ReportDocumentTemplateFormat
{
    /// <summary>
    /// Native Vibe document fragment.
    /// </summary>
    Vibe,

    /// <summary>
    /// DOCX content.
    /// </summary>
    Docx,

    /// <summary>
    /// HTML content.
    /// </summary>
    Html,

    /// <summary>
    /// Markdown content.
    /// </summary>
    Markdown
}
