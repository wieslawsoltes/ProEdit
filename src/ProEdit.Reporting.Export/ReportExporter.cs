using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProEdit.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Html;
using ProEdit.Layout;
using ProEdit.Markdown;
using ProEdit.OpenXml;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;
using ProEdit.Reporting.DocumentComposition;

namespace ProEdit.Reporting.Export;

/// <summary>
/// Default implementation of <see cref="IReportExporter" />.
/// </summary>
public sealed class ReportExporter : IReportExporter
{
    private readonly IReportDocumentComposer _documentComposer;
    private readonly IXpsDocumentConversionService _xpsDocumentConversionService;
    private readonly IPostScriptDocumentConversionService _postScriptDocumentConversionService;
    private readonly IReportPdfDocumentExporter _pdfDocumentExporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExporter"/> class.
    /// </summary>
    public ReportExporter()
        : this(
            new ReportDocumentComposer(),
            new XpsDocumentConversionService(),
            new PostScriptDocumentConversionService(),
            new ReportPdfDocumentExporter())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExporter"/> class.
    /// </summary>
    /// <param name="documentComposer">The document composer.</param>
    /// <param name="xpsDocumentConversionService">The XPS conversion service.</param>
    /// <param name="postScriptDocumentConversionService">The PostScript conversion service.</param>
    public ReportExporter(
        IReportDocumentComposer documentComposer,
        IXpsDocumentConversionService xpsDocumentConversionService,
        IPostScriptDocumentConversionService postScriptDocumentConversionService)
        : this(
            documentComposer,
            xpsDocumentConversionService,
            postScriptDocumentConversionService,
            new ReportPdfDocumentExporter())
    {
    }

    internal ReportExporter(
        IReportDocumentComposer documentComposer,
        IXpsDocumentConversionService xpsDocumentConversionService,
        IPostScriptDocumentConversionService postScriptDocumentConversionService,
        IReportPdfDocumentExporter pdfDocumentExporter)
    {
        _documentComposer = documentComposer ?? throw new ArgumentNullException(nameof(documentComposer));
        _xpsDocumentConversionService = xpsDocumentConversionService ?? throw new ArgumentNullException(nameof(xpsDocumentConversionService));
        _postScriptDocumentConversionService = postScriptDocumentConversionService ?? throw new ArgumentNullException(nameof(postScriptDocumentConversionService));
        _pdfDocumentExporter = pdfDocumentExporter ?? throw new ArgumentNullException(nameof(pdfDocumentExporter));
    }

    /// <inheritdoc />
    public async ValueTask<ReportExportResult> ExportAsync(
        ReportExportRequest request,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(stream));
        }

        var exportResult = CreateExportResult(request.Format);
        var startPosition = stream.CanSeek ? stream.Position : 0L;

        try
        {
            switch (request.Format)
            {
                case ReportExportFormat.Docx:
                    if (!await TryExportDocxAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Html:
                    if (!await TryExportHtmlAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Rtf:
                    if (!await TryExportRtfAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Markdown:
                    if (!await TryExportMarkdownAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Pdf:
                    if (!await TryExportPdfAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Xps:
                    if (!await TryExportXpsAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Ps:
                    if (!await TryExportPostScriptAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Csv:
                    if (!await TryExportCsvAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                case ReportExportFormat.Xlsx:
                    if (!await TryExportXlsxAsync(request, stream, exportResult, cancellationToken))
                    {
                        return exportResult;
                    }

                    break;
                default:
                    exportResult.Diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        $"Export format '{request.Format}' is not supported.",
                        "$.format"));
                    return exportResult;
            }

            if (stream.CanSeek)
            {
                exportResult.BytesWritten = Math.Max(0L, stream.Position - startPosition);
            }

            return exportResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exportResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportFailed,
                ex.Message,
                "$"));
            return exportResult;
        }
    }

    private static ReportExportResult CreateExportResult(ReportExportFormat format)
    {
        return format switch
        {
            ReportExportFormat.Pdf => new ReportExportResult
            {
                MediaType = "application/pdf",
                FileExtension = ".pdf"
            },
            ReportExportFormat.Docx => new ReportExportResult
            {
                MediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileExtension = ".docx"
            },
            ReportExportFormat.Html => new ReportExportResult
            {
                MediaType = "text/html",
                FileExtension = ".html"
            },
            ReportExportFormat.Rtf => new ReportExportResult
            {
                MediaType = "application/rtf",
                FileExtension = ".rtf"
            },
            ReportExportFormat.Markdown => new ReportExportResult
            {
                MediaType = "text/markdown",
                FileExtension = ".md"
            },
            ReportExportFormat.Xps => new ReportExportResult
            {
                MediaType = "application/vnd.ms-xpsdocument",
                FileExtension = ".xps"
            },
            ReportExportFormat.Ps => new ReportExportResult
            {
                MediaType = "application/postscript",
                FileExtension = ".ps"
            },
            ReportExportFormat.Csv => new ReportExportResult
            {
                MediaType = "text/csv",
                FileExtension = ".csv"
            },
            ReportExportFormat.Xlsx => new ReportExportResult
            {
                MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileExtension = ".xlsx"
            },
            _ => new ReportExportResult()
        };
    }

    private async ValueTask<bool> TryExportDocxAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        new DocxExporter().Save(document, stream);
        return true;
    }

    private async ValueTask<bool> TryExportHtmlAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var html = HtmlDocumentConverter.ToHtml(document, new HtmlOptions
        {
            PrettyPrint = profile.PrettyPrintHtml
        });

        await WriteTextAsync(stream, html, cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportRtfAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var rtf = DocumentRtfSerializer.ToRtf(document);
        await WriteTextAsync(stream, rtf, cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportMarkdownAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var markdown = MarkdownDocumentConverter.ToMarkdown(document, new MarkdownOptions
        {
            Flavor = MarkdownFlavor.GitHub,
            UseGfmTables = true,
            UseTaskLists = true,
            UseStrikethrough = true
        });

        await WriteTextAsync(stream, markdown, cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportPdfAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var layoutSettings = BuildLayoutSettings(document, profile);
        await _pdfDocumentExporter.SaveAsync(document, layoutSettings, stream, cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportXpsAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var layoutSettings = BuildLayoutSettings(document, profile);
        await _xpsDocumentConversionService.SaveAsync(
            document,
            layoutSettings,
            stream,
            XpsFlavor.Xps,
            cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportPostScriptAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolvePaginatedProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var document = await ResolveDocumentAsync(request, exportResult, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var layoutSettings = BuildLayoutSettings(document, profile);
        await _postScriptDocumentConversionService.SaveAsync(
            document,
            layoutSettings,
            stream,
            PostScriptKind.Ps,
            cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportCsvAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolveCsvProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var materializedReport = ResolveMaterializedReport(request, exportResult);
        if (materializedReport is null)
        {
            return false;
        }

        var tablixItems = CollectTablixItems(materializedReport);
        var tablixItem = ResolveCsvTablixItem(tablixItems, profile, exportResult);
        if (tablixItem is null)
        {
            return false;
        }

        var grid = BuildTablixExportGrid(tablixItem.Item, profile.IncludeHeaderRows);
        await WriteCsvAsync(stream, grid, profile, cancellationToken);
        return true;
    }

    private async ValueTask<bool> TryExportXlsxAsync(
        ReportExportRequest request,
        Stream stream,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        var profile = ResolveXlsxProfile(request, exportResult);
        if (profile is null)
        {
            return false;
        }

        var materializedReport = ResolveMaterializedReport(request, exportResult);
        if (materializedReport is null)
        {
            return false;
        }

        var tablixItems = CollectTablixItems(materializedReport);
        if (tablixItems.Count == 0)
        {
            exportResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportTablixNotFound,
                "The materialized report does not contain any tablix regions to export.",
                "$.materializedReport"));
            return false;
        }

        IReadOnlyList<ReportTablixExportEntry> selectedEntries = tablixItems;
        if (!string.IsNullOrWhiteSpace(profile.TablixItemId))
        {
            var selectedEntry = FindTablixItem(tablixItems, profile.TablixItemId);
            if (selectedEntry is null)
            {
                exportResult.Diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ExportTablixNotFound,
                    $"Tablix '{profile.TablixItemId}' was not found.",
                    "$.profile.tablixItemId"));
                return false;
            }

            selectedEntries = new[] { selectedEntry };
        }

        cancellationToken.ThrowIfCancellationRequested();
        WriteXlsx(stream, materializedReport, selectedEntries, profile);
        return true;
    }

    private static PaginatedReportExportProfile? ResolvePaginatedProfile(
        ReportExportRequest request,
        ReportExportResult exportResult)
    {
        if (request.Profile is null)
        {
            return new PaginatedReportExportProfile();
        }

        if (request.Profile is PaginatedReportExportProfile paginatedProfile)
        {
            return paginatedProfile;
        }

        exportResult.Diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExportProfileInvalid,
            $"Export format '{request.Format}' requires a paginated export profile.",
            "$.profile"));
        return null;
    }

    private static CsvReportExportProfile? ResolveCsvProfile(
        ReportExportRequest request,
        ReportExportResult exportResult)
    {
        if (request.Profile is null)
        {
            return new CsvReportExportProfile();
        }

        if (request.Profile is CsvReportExportProfile csvProfile)
        {
            return csvProfile;
        }

        exportResult.Diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExportProfileInvalid,
            "CSV export requires a CSV export profile.",
            "$.profile"));
        return null;
    }

    private static XlsxReportExportProfile? ResolveXlsxProfile(
        ReportExportRequest request,
        ReportExportResult exportResult)
    {
        if (request.Profile is null)
        {
            return new XlsxReportExportProfile();
        }

        if (request.Profile is XlsxReportExportProfile xlsxProfile)
        {
            return xlsxProfile;
        }

        exportResult.Diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExportProfileInvalid,
            "XLSX export requires an XLSX export profile.",
            "$.profile"));
        return null;
    }

    private async ValueTask<Document?> ResolveDocumentAsync(
        ReportExportRequest request,
        ReportExportResult exportResult,
        CancellationToken cancellationToken)
    {
        if (request.ExecutionResult.Document is not null)
        {
            return request.ExecutionResult.Document;
        }

        if (request.ExecutionResult.MaterializedReport is null)
        {
            exportResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportDocumentRequired,
                "This export requires a rendered document or a materialized report that can be composed.",
                "$.executionResult"));
            return null;
        }

        var compositionResult = await _documentComposer.ComposeAsync(
            new ReportDocumentCompositionRequest
            {
                MaterializedReport = request.ExecutionResult.MaterializedReport
            },
            cancellationToken);

        if (compositionResult.Diagnostics.Count > 0)
        {
            exportResult.Diagnostics.AddRange(compositionResult.Diagnostics);
        }

        if (compositionResult.Document is null || compositionResult.HasErrors)
        {
            if (!compositionResult.HasErrors)
            {
                exportResult.Diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ExportDocumentRequired,
                    "Document composition did not produce a paginated document.",
                    "$.executionResult.materializedReport"));
            }

            return null;
        }

        request.ExecutionResult.Document = compositionResult.Document;
        return compositionResult.Document;
    }

    private static MaterializedReport? ResolveMaterializedReport(
        ReportExportRequest request,
        ReportExportResult exportResult)
    {
        if (request.ExecutionResult.MaterializedReport is not null)
        {
            return request.ExecutionResult.MaterializedReport;
        }

        exportResult.Diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExportMaterializedReportRequired,
            "This export requires a materialized report.",
            "$.executionResult"));
        return null;
    }

    private static LayoutSettings BuildLayoutSettings(Document document, PaginatedReportExportProfile profile)
    {
        var settings = profile.LayoutSettings?.Clone() ?? new LayoutSettings();
        settings.UsePagination = true;
        settings.PageFlow = PageFlowDirection.Vertical;

        var sectionProperties = document.GetSection(0).Properties;
        if (sectionProperties.PageWidth is > 0f)
        {
            settings.PageWidth = sectionProperties.PageWidth.Value;
        }

        if (sectionProperties.PageHeight is > 0f)
        {
            settings.PageHeight = sectionProperties.PageHeight.Value;
        }

        if (sectionProperties.MarginLeft.HasValue)
        {
            settings.MarginLeft = sectionProperties.MarginLeft.Value;
        }

        if (sectionProperties.MarginTop.HasValue)
        {
            settings.MarginTop = sectionProperties.MarginTop.Value;
        }

        if (sectionProperties.MarginRight.HasValue)
        {
            settings.MarginRight = sectionProperties.MarginRight.Value;
        }

        if (sectionProperties.MarginBottom.HasValue)
        {
            settings.MarginBottom = sectionProperties.MarginBottom.Value;
        }

        if (sectionProperties.HeaderOffset.HasValue)
        {
            settings.HeaderOffset = sectionProperties.HeaderOffset.Value;
        }

        if (sectionProperties.FooterOffset.HasValue)
        {
            settings.FooterOffset = sectionProperties.FooterOffset.Value;
        }

        if (sectionProperties.Gutter.HasValue)
        {
            settings.Gutter = sectionProperties.Gutter.Value;
        }

        if (sectionProperties.ColumnGap.HasValue)
        {
            settings.ColumnGap = sectionProperties.ColumnGap.Value;
        }

        settings.ViewportWidth = settings.PageWidth;
        settings.ViewportHeight = settings.PageHeight;
        return settings;
    }

    private static List<ReportTablixExportEntry> CollectTablixItems(MaterializedReport materializedReport)
    {
        var entries = new List<ReportTablixExportEntry>();
        CollectTablixItems(materializedReport, entries, materializedReport.Id);
        return entries;
    }

    private static void CollectTablixItems(
        MaterializedReport report,
        List<ReportTablixExportEntry> target,
        string reportPath)
    {
        for (var sectionIndex = 0; sectionIndex < report.Sections.Count; sectionIndex++)
        {
            var section = report.Sections[sectionIndex];
            CollectTablixItems(
                reportPath,
                section.Id,
                section.Name,
                section.BodyItems,
                target);
        }
    }

    private static void CollectTablixItems(
        string reportPath,
        string sectionId,
        string sectionName,
        IReadOnlyList<MaterializedReportItem> items,
        List<ReportTablixExportEntry> target)
    {
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            switch (items[itemIndex])
            {
                case MaterializedTablixReportItem tablixItem:
                    target.Add(new ReportTablixExportEntry(
                        reportPath,
                        sectionId,
                        sectionName,
                        tablixItem));
                    break;
                case MaterializedSubreportReportItem subreportItem when subreportItem.Report is not null:
                    CollectTablixItems(
                        subreportItem.Report,
                        target,
                        string.IsNullOrWhiteSpace(subreportItem.Report.Id)
                            ? reportPath
                            : $"{reportPath}/{subreportItem.Report.Id}");
                    break;
            }
        }
    }

    private static ReportTablixExportEntry? ResolveCsvTablixItem(
        IReadOnlyList<ReportTablixExportEntry> entries,
        CsvReportExportProfile profile,
        ReportExportResult exportResult)
    {
        if (entries.Count == 0)
        {
            exportResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportTablixNotFound,
                "The materialized report does not contain any tablix regions to export.",
                "$.materializedReport"));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.TablixItemId))
        {
            var match = FindTablixItem(entries, profile.TablixItemId);
            if (match is not null)
            {
                return match;
            }

            exportResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportTablixNotFound,
                $"Tablix '{profile.TablixItemId}' was not found.",
                "$.profile.tablixItemId"));
            return null;
        }

        if (entries.Count == 1)
        {
            return entries[0];
        }

        exportResult.Diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExportTablixSelectionRequired,
            "CSV export requires 'TablixItemId' when the materialized report contains more than one tablix region.",
            "$.profile.tablixItemId"));
        return null;
    }

    private static ReportTablixExportEntry? FindTablixItem(
        IReadOnlyList<ReportTablixExportEntry> entries,
        string tablixItemId)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (string.Equals(entry.Item.SourceItemId, tablixItemId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static async ValueTask WriteTextAsync(
        Stream stream,
        string text,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async ValueTask WriteCsvAsync(
        Stream stream,
        ReportTablixExportGrid grid,
        CsvReportExportProfile profile,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);

        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = grid.Rows[rowIndex];

            for (var cellIndex = 0; cellIndex < grid.ColumnCount; cellIndex++)
            {
                if (cellIndex > 0)
                {
                    await writer.WriteAsync(profile.Delimiter);
                }

                await writer.WriteAsync(EscapeCsvValue(row[cellIndex], profile.Delimiter));
            }

            await writer.WriteLineAsync();
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string EscapeCsvValue(string? value, char delimiter)
    {
        value ??= string.Empty;
        if (value.IndexOfAny([delimiter, '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void WriteXlsx(
        Stream stream,
        MaterializedReport materializedReport,
        IReadOnlyList<ReportTablixExportEntry> entries,
        XlsxReportExportProfile profile)
    {
        using var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        spreadsheet.PackageProperties.Creator = profile.WorkbookAuthor;
        spreadsheet.PackageProperties.Title = string.IsNullOrWhiteSpace(materializedReport.Name)
            ? materializedReport.Id
            : materializedReport.Name;

        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var worksheet = new Worksheet();
            var columns = CreateColumns(entry.Item);
            if (columns is not null)
            {
                worksheet.Append(columns);
            }

            var grid = BuildTablixExportGrid(entry.Item, profile.IncludeHeaderRows);
            var sheetData = new SheetData();
            PopulateSheetData(sheetData, grid);
            worksheet.Append(sheetData);
            AppendMergeCells(worksheet, grid);
            worksheetPart.Worksheet = worksheet;

            var sheetName = CreateUniqueWorksheetName(entry, usedSheetNames, index + 1);
            var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
            sheets.Append(new Sheet
            {
                Id = relationshipId,
                SheetId = (uint)(index + 1),
                Name = sheetName
            });
        }

        workbookPart.Workbook.Save();
    }

    private static Columns? CreateColumns(MaterializedTablixReportItem tablixItem)
    {
        if (tablixItem.Columns.Count == 0)
        {
            return null;
        }

        var columns = new Columns();
        for (var index = 0; index < tablixItem.Columns.Count; index++)
        {
            var width = tablixItem.Columns[index].Width;
            if (width <= 0f)
            {
                continue;
            }

            columns.Append(new Column
            {
                Min = (uint)(index + 1),
                Max = (uint)(index + 1),
                Width = Math.Max(1d, Math.Round(width / 7d, 2, MidpointRounding.AwayFromZero)),
                CustomWidth = true
            });
        }

        return columns.ChildElements.Count == 0 ? null : columns;
    }

    private static ReportTablixExportGrid BuildTablixExportGrid(
        MaterializedTablixReportItem tablixItem,
        bool includeHeaderRows)
    {
        var rows = new List<List<string>>();
        var merges = new List<ReportTablixCellMerge>();
        var activeRowSpans = new Dictionary<int, int>();
        var maxColumnIndex = Math.Max(0, tablixItem.Columns.Count);

        for (var rowIndex = 0; rowIndex < tablixItem.Rows.Count; rowIndex++)
        {
            var tablixRow = tablixItem.Rows[rowIndex];
            if (!includeHeaderRows && tablixRow.IsHeader)
            {
                continue;
            }

            var row = new List<string>();
            var newRowSpans = new Dictionary<int, int>();
            var columnIndex = 0;

            for (var cellIndex = 0; cellIndex < tablixRow.Cells.Count; cellIndex++)
            {
                while (activeRowSpans.TryGetValue(columnIndex, out var spanRowsRemaining) && spanRowsRemaining > 0)
                {
                    EnsureCell(row, columnIndex, string.Empty);
                    columnIndex++;
                }

                var cell = tablixRow.Cells[cellIndex];
                var columnSpan = Math.Max(1, cell.ColumnSpan);
                var rowSpan = Math.Max(1, cell.RowSpan);
                EnsureCell(row, columnIndex, cell.Text ?? string.Empty);

                for (var columnOffset = 1; columnOffset < columnSpan; columnOffset++)
                {
                    EnsureCell(row, columnIndex + columnOffset, string.Empty);
                }

                if (columnSpan > 1 || rowSpan > 1)
                {
                    var exportRowIndex = (uint)(rows.Count + 1);
                    merges.Add(new ReportTablixCellMerge(
                        exportRowIndex,
                        columnIndex + 1,
                        exportRowIndex + (uint)(rowSpan - 1),
                        columnIndex + columnSpan));
                }

                if (rowSpan > 1)
                {
                    for (var columnOffset = 0; columnOffset < columnSpan; columnOffset++)
                    {
                        newRowSpans[columnIndex + columnOffset] = rowSpan - 1;
                    }
                }

                columnIndex += columnSpan;
            }

            rows.Add(row);
            if (row.Count > maxColumnIndex)
            {
                maxColumnIndex = row.Count;
            }

            DecrementActiveRowSpans(activeRowSpans);
            foreach (var pair in newRowSpans)
            {
                activeRowSpans[pair.Key] = pair.Value;
                var occupiedColumnIndex = pair.Key + 1;
                if (occupiedColumnIndex > maxColumnIndex)
                {
                    maxColumnIndex = occupiedColumnIndex;
                }
            }
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            while (rows[rowIndex].Count < maxColumnIndex)
            {
                rows[rowIndex].Add(string.Empty);
            }
        }

        return new ReportTablixExportGrid(
            maxColumnIndex,
            rows,
            merges);
    }

    private static void PopulateSheetData(
        SheetData sheetData,
        ReportTablixExportGrid grid)
    {
        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            var rowNumber = (uint)(rowIndex + 1);
            var row = new Row
            {
                RowIndex = rowNumber
            };

            for (var cellIndex = 0; cellIndex < grid.ColumnCount; cellIndex++)
            {
                var cellText = grid.Rows[rowIndex][cellIndex];
                var reference = GetCellReference(cellIndex + 1, rowNumber);
                row.Append(new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(
                        new DocumentFormat.OpenXml.Spreadsheet.Text(cellText)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        })
                });
            }

            sheetData.Append(row);
        }
    }

    private static void AppendMergeCells(Worksheet worksheet, ReportTablixExportGrid grid)
    {
        if (grid.Merges.Count == 0)
        {
            return;
        }

        var mergeCells = new MergeCells();
        for (var index = 0; index < grid.Merges.Count; index++)
        {
            var merge = grid.Merges[index];
            if (merge.StartRow == merge.EndRow && merge.StartColumn == merge.EndColumn)
            {
                continue;
            }

            mergeCells.Append(new MergeCell
            {
                Reference = $"{GetCellReference(merge.StartColumn, merge.StartRow)}:{GetCellReference(merge.EndColumn, merge.EndRow)}"
            });
        }

        if (mergeCells.ChildElements.Count > 0)
        {
            worksheet.Append(mergeCells);
        }
    }

    private static void EnsureCell(List<string> row, int columnIndex, string value)
    {
        while (row.Count <= columnIndex)
        {
            row.Add(string.Empty);
        }

        row[columnIndex] = value;
    }

    private static void DecrementActiveRowSpans(Dictionary<int, int> activeRowSpans)
    {
        if (activeRowSpans.Count == 0)
        {
            return;
        }

        var columns = activeRowSpans.Keys.ToList();
        List<int>? columnsToClear = null;
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var next = activeRowSpans[column] - 1;
            activeRowSpans[column] = next;
            if (next <= 0)
            {
                columnsToClear ??= new List<int>();
                columnsToClear.Add(column);
            }
        }

        if (columnsToClear is null)
        {
            return;
        }

        for (var index = 0; index < columnsToClear.Count; index++)
        {
            activeRowSpans.Remove(columnsToClear[index]);
        }
    }

    private static string CreateUniqueWorksheetName(
        ReportTablixExportEntry entry,
        HashSet<string> usedSheetNames,
        int sheetNumber)
    {
        var baseName = entry.Item.Name;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = entry.Item.SourceItemId;
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = entry.SectionName;
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"Sheet{sheetNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        var normalized = NormalizeWorksheetName(baseName);
        if (usedSheetNames.Add(normalized))
        {
            return normalized;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = AppendWorksheetSuffix(normalized, suffix);
            if (usedSheetNames.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string NormalizeWorksheetName(string value)
    {
        Span<char> invalid = ['\\', '/', '?', '*', '[', ']', ':'];
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var normalized = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Sheet";
        }

        if (normalized.Length > 31)
        {
            normalized = normalized[..31];
        }

        return normalized;
    }

    private static string AppendWorksheetSuffix(string baseName, int suffix)
    {
        var suffixText = $" {suffix.ToString(CultureInfo.InvariantCulture)}";
        var maxBaseLength = Math.Max(1, 31 - suffixText.Length);
        var trimmedBaseName = baseName.Length > maxBaseLength
            ? baseName[..maxBaseLength]
            : baseName;
        return trimmedBaseName + suffixText;
    }

    private static string GetCellReference(int columnNumber, uint rowNumber)
    {
        var buffer = new StringBuilder();
        var value = columnNumber;
        while (value > 0)
        {
            var modulo = (value - 1) % 26;
            buffer.Insert(0, (char)('A' + modulo));
            value = (value - modulo - 1) / 26;
        }

        buffer.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
        return buffer.ToString();
    }

    private sealed record ReportTablixExportEntry(
        string ReportPath,
        string SectionId,
        string SectionName,
        MaterializedTablixReportItem Item);

    private sealed record ReportTablixCellMerge(
        uint StartRow,
        int StartColumn,
        uint EndRow,
        int EndColumn);

    private sealed record ReportTablixExportGrid(
        int ColumnCount,
        IReadOnlyList<List<string>> Rows,
        IReadOnlyList<ReportTablixCellMerge> Merges);
}

internal interface IReportPdfDocumentExporter
{
    ValueTask SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream stream,
        CancellationToken cancellationToken = default);
}

internal sealed class ReportPdfDocumentExporter : IReportPdfDocumentExporter
{
    public async ValueTask SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layoutSettings);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(stream));
        }

        var tempPdfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            var effectiveLayoutSettings = layoutSettings.Clone();
            effectiveLayoutSettings.UsePagination = true;
            effectiveLayoutSettings.PageFlow = PageFlowDirection.Vertical;
            effectiveLayoutSettings.ViewportWidth = effectiveLayoutSettings.PageWidth;
            effectiveLayoutSettings.ViewportHeight = effectiveLayoutSettings.PageHeight;

            var context = new DocumentPrintContext(document, effectiveLayoutSettings);
            var settings = new PrintSettings
            {
                OutputKind = PrintOutputKind.Pdf,
                OutputPath = tempPdfPath,
                RangeKind = PrintRangeKind.All,
                Copies = 1,
                Collate = true
            };

            var systemPrintService = new SystemPrintService();
            var printService = new SkiaPrintService(systemPrintService, systemPrintService);
            var result = await printService.PrintAsync(context, settings, cancellationToken);
            if (!result.Succeeded)
            {
                throw new IOException($"PDF export failed: {result.Message}");
            }

            await using var input = File.OpenRead(tempPdfPath);
            await input.CopyToAsync(stream, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
            catch
            {
                // best effort cleanup.
            }
        }
    }
}
