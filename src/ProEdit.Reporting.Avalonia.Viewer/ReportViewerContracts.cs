using System.Globalization;
using ProEdit.Layout;
using ProEdit.Printing;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Export;

namespace ProEdit.Reporting.Avalonia.Viewer;

/// <summary>
/// Identifies the currently selected viewer pane.
/// </summary>
public enum ReportViewerPane
{
    /// <summary>
    /// Parameter input pane.
    /// </summary>
    Parameters,

    /// <summary>
    /// Document map pane.
    /// </summary>
    Outline,

    /// <summary>
    /// Search pane.
    /// </summary>
    Search,

    /// <summary>
    /// Diagnostics pane.
    /// </summary>
    Diagnostics,

    /// <summary>
    /// Drillthrough pane.
    /// </summary>
    Drillthrough
}

/// <summary>
/// Defines the report source and runtime context used by the Avalonia viewer.
/// </summary>
public sealed class ReportViewerSource
{
    /// <summary>
    /// Gets or sets the report definition to preview.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the provider registry used by dataset execution.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; set; } = ReportDataProviders.CreateDefaultRegistry();

    /// <summary>
    /// Gets or sets the host data registry used by built-in providers.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

    /// <summary>
    /// Gets the referenced subreport definitions.
    /// </summary>
    public Dictionary<string, ReportDefinition> ReferencedReports { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the host-supplied global values.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the layout settings used for preview pagination.
    /// </summary>
    public LayoutSettings LayoutSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets the preview DPI.
    /// </summary>
    public float PreviewDpi { get; set; } = 120f;

    /// <summary>
    /// Gets or sets default print settings applied when no host dialog is supplied.
    /// </summary>
    public PrintSettings? DefaultPrintSettings { get; set; }

    /// <summary>
    /// Gets or sets the execution culture.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the UI culture.
    /// </summary>
    public CultureInfo? UiCulture { get; set; }

    /// <summary>
    /// Gets or sets the execution time zone.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }
}

/// <summary>
/// Captures the resolved viewer state for one parameter.
/// </summary>
public sealed class ReportViewerParameterState
{
    /// <summary>
    /// Gets or sets the parameter definition.
    /// </summary>
    public ReportParameterDefinition Definition { get; set; } = new();

    /// <summary>
    /// Gets or sets the resolved value.
    /// </summary>
    public ReportParameterValue? ResolvedValue { get; set; }

    /// <summary>
    /// Gets the available values resolved for the parameter.
    /// </summary>
    public List<ReportParameterAvailableValue> AvailableValues { get; } = new();
}

/// <summary>
/// Represents the outcome of parameter resolution for the viewer.
/// </summary>
public sealed class ReportViewerParameterResolutionResult
{
    /// <summary>
    /// Gets the resolved parameter states.
    /// </summary>
    public List<ReportViewerParameterState> Parameters { get; } = new();

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether any errors were emitted.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Represents one document map entry exposed by the viewer.
/// </summary>
public sealed class ReportViewerDocumentMapEntry
{
    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bookmark name.
    /// </summary>
    public string Bookmark { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 0-based page index.
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Gets or sets the document-map indentation level.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the source item identifier when the entry originates from one report item.
    /// </summary>
    public string? SourceItemId { get; set; }
}

/// <summary>
/// Represents one viewer search result.
/// </summary>
public sealed class ReportViewerSearchEntry
{
    /// <summary>
    /// Gets or sets the matching paragraph text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 0-based page index.
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Gets or sets the 0-based paragraph index.
    /// </summary>
    public int ParagraphIndex { get; set; }
}

/// <summary>
/// Represents one drillthrough entry exposed by the viewer.
/// </summary>
public sealed class ReportViewerDrillthroughEntry
{
    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional tooltip text.
    /// </summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// Gets or sets the 0-based page index.
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Gets or sets the source item identifier.
    /// </summary>
    public string SourceItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved drillthrough action.
    /// </summary>
    public MaterializedReportDrillthroughAction Action { get; set; } = new();
}

/// <summary>
/// Captures one fully materialized viewer execution snapshot.
/// </summary>
public sealed class ReportViewerExecutionSnapshot
{
    /// <summary>
    /// Gets or sets the report execution result.
    /// </summary>
    public ReportExecutionResult ExecutionResult { get; set; } = new();

    /// <summary>
    /// Gets or sets the layout used for pagination and preview.
    /// </summary>
    public DocumentLayout? Layout { get; set; }

    /// <summary>
    /// Gets or sets the layout settings used to build the preview layout.
    /// </summary>
    public LayoutSettings LayoutSettings { get; set; } = new();

    /// <summary>
    /// Gets the rendered preview pages.
    /// </summary>
    public List<PrintPreviewPage> PreviewPages { get; } = new();

    /// <summary>
    /// Gets the document map entries.
    /// </summary>
    public List<ReportViewerDocumentMapEntry> DocumentMapEntries { get; } = new();

    /// <summary>
    /// Gets the search corpus entries.
    /// </summary>
    public List<ReportViewerSearchEntry> SearchEntries { get; } = new();

    /// <summary>
    /// Gets the drillthrough entries.
    /// </summary>
    public List<ReportViewerDrillthroughEntry> DrillthroughEntries { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the execution emitted any errors.
    /// </summary>
    public bool HasErrors => ExecutionResult.Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Captures persisted UI state for the viewer.
/// </summary>
public sealed class ReportViewerState
{
    /// <summary>
    /// Gets or sets the selected pane.
    /// </summary>
    public ReportViewerPane ActivePane { get; set; } = ReportViewerPane.Parameters;

    /// <summary>
    /// Gets or sets the selected page index.
    /// </summary>
    public int SelectedPageIndex { get; set; }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public float ZoomFactor { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the current search query.
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected bookmark name.
    /// </summary>
    public string? SelectedBookmark { get; set; }

    /// <summary>
    /// Gets or sets the viewer drawer visibility state.
    /// </summary>
    public PaneVisibilityState LeftDrawerState { get; set; } = PaneVisibilityState.Closed;

    /// <summary>
    /// Gets or sets a value indicating whether the thumbnail filmstrip is expanded.
    /// </summary>
    public bool IsThumbnailTrayOpen { get; set; }
}

/// <summary>
/// Defines one host export request issued by the viewer.
/// </summary>
public sealed class ReportViewerExportDialogRequest
{
    /// <summary>
    /// Gets or sets the requested export format.
    /// </summary>
    public ReportExportFormat Format { get; set; }

    /// <summary>
    /// Gets or sets the suggested file name.
    /// </summary>
    public string SuggestedFileName { get; set; } = string.Empty;
}

/// <summary>
/// Defines one host-selected export target.
/// </summary>
public sealed class ReportViewerExportTarget
{
    /// <summary>
    /// Gets or sets the target file path.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional format-specific export profile settings.
    /// </summary>
    public ReportExportProfile? Profile { get; set; }
}

/// <summary>
/// Defines one host print request issued by the viewer.
/// </summary>
public sealed class ReportViewerPrintDialogRequest
{
    /// <summary>
    /// Gets or sets the suggested report name.
    /// </summary>
    public string ReportName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default print settings.
    /// </summary>
    public PrintSettings Settings { get; set; } = new();
}

/// <summary>
/// Provides report execution, preview, export, and print services for the viewer.
/// </summary>
public interface IReportViewerSessionService
{
    /// <summary>
    /// Resolves promptable parameters and available values for one viewer source.
    /// </summary>
    /// <param name="source">The report source.</param>
    /// <param name="suppliedParameters">The currently supplied parameters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parameter-resolution result.</returns>
    ValueTask<ReportViewerParameterResolutionResult> ResolveParametersAsync(
        ReportViewerSource source,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the report source and builds the preview snapshot used by the viewer.
    /// </summary>
    /// <param name="source">The report source.</param>
    /// <param name="suppliedParameters">The supplied parameters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The viewer execution snapshot.</returns>
    ValueTask<ReportViewerExecutionSnapshot> ExecuteAsync(
        ReportViewerSource source,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the current viewer snapshot to the supplied stream.
    /// </summary>
    /// <param name="snapshot">The current viewer snapshot.</param>
    /// <param name="request">The export request.</param>
    /// <param name="stream">The output stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result.</returns>
    ValueTask<ReportExportResult> ExportAsync(
        ReportViewerExecutionSnapshot snapshot,
        ReportExportRequest request,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prints the current viewer snapshot with the supplied settings.
    /// </summary>
    /// <param name="snapshot">The current viewer snapshot.</param>
    /// <param name="settings">The print settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The print job result.</returns>
    ValueTask<PrintJobResult> PrintAsync(
        ReportViewerExecutionSnapshot snapshot,
        PrintSettings settings,
        CancellationToken cancellationToken = default);
}
