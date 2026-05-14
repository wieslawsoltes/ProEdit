using System.Globalization;
using System.Text;
using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;
using ProEdit.Primitives;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.DocumentComposition;
using ProEdit.Reporting.Export;
using ProEdit.Reporting.Expressions;
using ProEdit.Reporting.Materialization;

namespace ProEdit.Reporting.Avalonia.Viewer;

/// <summary>
/// Default implementation of <see cref="IReportViewerSessionService" />.
/// </summary>
public sealed class ReportViewerSessionService : IReportViewerSessionService
{
    private const float DipsPerInch = 96f;

    private readonly IReportDocumentComposer _documentComposer;
    private readonly IReportExporter _exporter;
    private readonly IReportMaterializer _materializer;
    private readonly ReportParameterResolver _parameterResolver;
    private readonly IPrintService _printService;
    private readonly DocumentLayouter _layouter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerSessionService" /> class.
    /// </summary>
    /// <param name="expressionCompiler">The expression compiler.</param>
    /// <param name="materializer">The materializer.</param>
    /// <param name="documentComposer">The document composer.</param>
    /// <param name="exporter">The exporter.</param>
    /// <param name="printService">The print service.</param>
    public ReportViewerSessionService(
        IReportExpressionCompiler? expressionCompiler = null,
        IReportMaterializer? materializer = null,
        IReportDocumentComposer? documentComposer = null,
        IReportExporter? exporter = null,
        IPrintService? printService = null)
    {
        var compiler = expressionCompiler ?? new ReportExpressionCompiler();
        _parameterResolver = new ReportParameterResolver(compiler);
        _materializer = materializer ?? new ReportMaterializer(compiler);
        _documentComposer = documentComposer ?? new ReportDocumentComposer();
        _exporter = exporter ?? new ReportExporter();
        if (printService is null)
        {
            var systemPrintService = new SystemPrintService();
            _printService = new SkiaPrintService(systemPrintService, systemPrintService);
        }
        else
        {
            _printService = printService;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ReportViewerParameterResolutionResult> ResolveParametersAsync(
        ReportViewerSource source,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(suppliedParameters);

        var request = new ReportParameterResolutionRequest
        {
            ReportDefinition = source.ReportDefinition,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = source.Culture,
            UiCulture = source.UiCulture,
            TimeZone = source.TimeZone
        };

        CopyParameters(suppliedParameters, request.SuppliedValues);
        CopyValues(source.Globals, request.Globals);

        var resolution = await _parameterResolver.ResolveAsync(request, cancellationToken);
        var result = new ReportViewerParameterResolutionResult();
        result.Diagnostics.AddRange(CloneDiagnostics(resolution.Diagnostics));

        for (var index = 0; index < source.ReportDefinition.Parameters.Count; index++)
        {
            var definition = source.ReportDefinition.Parameters[index];
            var state = new ReportViewerParameterState
            {
                Definition = definition,
                ResolvedValue = resolution.ResolvedValues.TryGetValue(definition.Id, out var resolvedValue)
                    ? CloneParameterValue(resolvedValue)
                    : null
            };

            if (resolution.AvailableValues.TryGetValue(definition.Id, out var availableValues))
            {
                for (var valueIndex = 0; valueIndex < availableValues.Count; valueIndex++)
                {
                    state.AvailableValues.Add(CloneAvailableValue(availableValues[valueIndex]));
                }
            }

            result.Parameters.Add(state);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<ReportViewerExecutionSnapshot> ExecuteAsync(
        ReportViewerSource source,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(suppliedParameters);

        var snapshot = new ReportViewerExecutionSnapshot
        {
            LayoutSettings = NormalizeLayoutSettings(source.LayoutSettings)
        };

        var materializationRequest = new ReportMaterializationRequest
        {
            ReportDefinition = source.ReportDefinition,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = source.Culture,
            UiCulture = source.UiCulture,
            TimeZone = source.TimeZone
        };

        CopyParameters(suppliedParameters, materializationRequest.ParameterValues);
        CopyValues(source.Globals, materializationRequest.Globals);
        CopyReports(source.ReferencedReports, materializationRequest.ReferencedReports);

        var materialization = await _materializer.MaterializeAsync(materializationRequest, cancellationToken);
        var executionResult = new ReportExecutionResult
        {
            MaterializedReport = materialization.MaterializedReport
        };

        executionResult.Diagnostics.AddRange(CloneDiagnostics(materialization.Diagnostics));
        CopyParameters(materialization.ResolvedParameters, executionResult.ResolvedParameters);

        if (materialization.MaterializedReport is null)
        {
            snapshot.ExecutionResult = executionResult;
            return snapshot;
        }

        var composition = await _documentComposer.ComposeAsync(
            new ReportDocumentCompositionRequest
            {
                MaterializedReport = materialization.MaterializedReport
            },
            cancellationToken);
        executionResult.Diagnostics.AddRange(CloneDiagnostics(composition.Diagnostics));
        executionResult.Document = composition.Document;
        snapshot.ExecutionResult = executionResult;

        if (composition.Document is null)
        {
            return snapshot;
        }

        using var fontResolver = new SkiaDocumentFontResolver(composition.Document.Fonts);
        var textMeasurer = new SkiaTextMeasurer
        {
            TypefaceResolver = fontResolver
        };

        var layout = _layouter.Layout(composition.Document, snapshot.LayoutSettings, textMeasurer);
        snapshot.Layout = layout;
        executionResult.Metrics.PageCount = layout.Pages.Count;
        executionResult.Metrics.DataRowCount = CountDataRows(materialization.MaterializedReport);

        var previewPages = RenderPreviewPages(
            composition.Document,
            layout,
            fontResolver,
            source.PreviewDpi,
            cancellationToken);
        snapshot.PreviewPages.AddRange(previewPages);

        var paragraphPageMap = BuildParagraphPageMap(layout);
        var floatingPageMap = BuildFloatingObjectPageMap(layout);
        var bookmarkPageMap = BuildBookmarkPageMap(composition.Document, paragraphPageMap, floatingPageMap);

        snapshot.DocumentMapEntries.AddRange(BuildDocumentMapEntries(materialization.MaterializedReport, bookmarkPageMap));
        snapshot.SearchEntries.AddRange(BuildSearchEntries(composition.Document, paragraphPageMap, floatingPageMap));
        snapshot.DrillthroughEntries.AddRange(BuildDrillthroughEntries(materialization.MaterializedReport, bookmarkPageMap));
        return snapshot;
    }

    /// <inheritdoc />
    public ValueTask<ReportExportResult> ExportAsync(
        ReportViewerExecutionSnapshot snapshot,
        ReportExportRequest request,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stream);

        request.ExecutionResult = snapshot.ExecutionResult;
        return _exporter.ExportAsync(request, stream, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PrintJobResult> PrintAsync(
        ReportViewerExecutionSnapshot snapshot,
        PrintSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);

        if (snapshot.ExecutionResult.Document is null)
        {
            return ValueTask.FromResult(PrintJobResult.Failed("No composed document is available for printing."));
        }

        var context = new DocumentPrintContext(snapshot.ExecutionResult.Document, snapshot.LayoutSettings.Clone());
        return _printService.PrintAsync(context, settings, cancellationToken);
    }

    private static LayoutSettings NormalizeLayoutSettings(LayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Clone();
        normalized.UsePagination = true;
        normalized.PageFlow = PageFlowDirection.Vertical;
        normalized.ViewportWidth = normalized.PageWidth;
        normalized.ViewportHeight = normalized.PageHeight;
        return normalized;
    }

    private static int CountDataRows(MaterializedReport materializedReport)
    {
        var rowCount = 0;
        for (var index = 0; index < materializedReport.DataSets.Count; index++)
        {
            rowCount += materializedReport.DataSets[index].Rows.Count;
        }

        return rowCount;
    }

    private static IReadOnlyList<PrintPreviewPage> RenderPreviewPages(
        Document document,
        DocumentLayout layout,
        SkiaDocumentFontResolver fontResolver,
        float dpi,
        CancellationToken cancellationToken)
    {
        var renderOptions = CreateRenderOptions();
        var renderer = new SkiaDocumentRenderer
        {
            TypefaceResolver = fontResolver
        };

        var previewPages = new List<PrintPreviewPage>(layout.Pages.Count);
        var effectiveDpi = dpi > 0f ? dpi : 120f;
        var scale = effectiveDpi / DipsPerInch;

        for (var pageIndex = 0; pageIndex < layout.Pages.Count; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = layout.Pages[pageIndex];
            var widthPx = Math.Max(1, (int)MathF.Ceiling(page.Bounds.Width * effectiveDpi / DipsPerInch));
            var heightPx = Math.Max(1, (int)MathF.Ceiling(page.Bounds.Height * effectiveDpi / DipsPerInch));
            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface is null)
            {
                continue;
            }

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale(scale);
            canvas.Translate(-page.Bounds.X, -page.Bounds.Y);

            renderOptions.VisibleBounds = page.Bounds;
            renderer.Render(canvas, document, layout, renderOptions);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            if (data is null)
            {
                continue;
            }

            previewPages.Add(new PrintPreviewPage(pageIndex + 1, data.ToArray(), widthPx, heightPx));
        }

        return previewPages;
    }

    private static RenderOptions CreateRenderOptions()
    {
        return new RenderOptions
        {
            BackgroundColor = DocColor.White,
            PageColor = DocColor.White,
            PageBorderColor = new DocColor(220, 220, 220),
            PageBorderThickness = 1f,
            SvgRenderMode = SvgRenderMode.Rasterize,
            SvgRasterizationScale = 2f,
            ShowCaret = false,
            ShowInvisibles = false,
            ShowLayout = false,
            ShowGridlines = false,
            UsePictureCache = false,
            Selection = null,
            SelectionRanges = null,
            HeaderFooterMode = HeaderFooterEditMode.None
        };
    }

    private static Dictionary<int, int> BuildParagraphPageMap(DocumentLayout layout)
    {
        var map = new Dictionary<int, int>();
        foreach (var pair in layout.ParagraphLineRanges)
        {
            if (pair.Value.Count > 0)
            {
                map[pair.Key] = Math.Max(0, layout.LineIndex.GetPageForLine(pair.Value.Start));
            }
        }

        if (map.Count == 0)
        {
            return map;
        }

        var orderedParagraphs = map.Keys.OrderBy(static value => value).ToArray();
        var lastPage = 0;
        for (var index = 0; index < orderedParagraphs.Length; index++)
        {
            var paragraphIndex = orderedParagraphs[index];
            if (map.TryGetValue(paragraphIndex, out var pageIndex))
            {
                lastPage = pageIndex;
            }
            else
            {
                map[paragraphIndex] = lastPage;
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildBookmarkPageMap(
        Document document,
        IReadOnlyDictionary<int, int> paragraphPageMap,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var paragraphs = new List<ParagraphBlock>();
        CollectParagraphs(document.Blocks, paragraphs);

        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            var paragraph = paragraphs[paragraphIndex];
            var pageIndex = paragraphPageMap.TryGetValue(paragraphIndex, out var resolvedPageIndex)
                ? resolvedPageIndex
                : 0;
            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is BookmarkStartInline bookmarkStart
                    && !string.IsNullOrWhiteSpace(bookmarkStart.Name)
                    && !map.ContainsKey(bookmarkStart.Name))
                {
                    map[bookmarkStart.Name] = pageIndex;
                }
            }

            AddFloatingBookmarks(paragraph.FloatingObjects, pageIndex, map, floatingPageMap);
        }

        return map;
    }

    private static IReadOnlyList<ReportViewerDocumentMapEntry> BuildDocumentMapEntries(
        MaterializedReport report,
        IReadOnlyDictionary<string, int> bookmarkPageMap)
    {
        var entries = new List<ReportViewerDocumentMapEntry>();
        for (var sectionIndex = 0; sectionIndex < report.Sections.Count; sectionIndex++)
        {
            var section = report.Sections[sectionIndex];
            if (!string.IsNullOrWhiteSpace(section.Bookmark))
            {
                entries.Add(new ReportViewerDocumentMapEntry
                {
                    Label = string.IsNullOrWhiteSpace(section.Name) ? section.Bookmark : section.Name,
                    Bookmark = section.Bookmark,
                    PageIndex = ResolveBookmarkPageIndex(section.Bookmark, bookmarkPageMap),
                    Level = 0
                });
            }

            AddItemDocumentMapEntries(section.BodyItems, entries, bookmarkPageMap, level: 1);
        }

        return entries;
    }

    private static void AddItemDocumentMapEntries(
        IReadOnlyList<MaterializedReportItem> items,
        List<ReportViewerDocumentMapEntry> entries,
        IReadOnlyDictionary<string, int> bookmarkPageMap,
        int level)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!string.IsNullOrWhiteSpace(item.Bookmark))
            {
                entries.Add(new ReportViewerDocumentMapEntry
                {
                    Label = string.IsNullOrWhiteSpace(item.Name) ? item.Bookmark : item.Name,
                    Bookmark = item.Bookmark,
                    PageIndex = ResolveBookmarkPageIndex(item.Bookmark, bookmarkPageMap),
                    Level = level,
                    SourceItemId = item.SourceItemId
                });
            }

            if (item is MaterializedSubreportReportItem subreportItem && subreportItem.Report is not null)
            {
                for (var sectionIndex = 0; sectionIndex < subreportItem.Report.Sections.Count; sectionIndex++)
                {
                    AddItemDocumentMapEntries(subreportItem.Report.Sections[sectionIndex].BodyItems, entries, bookmarkPageMap, level + 1);
                }
            }
            else if (item is MaterializedContainerReportItem containerItem)
            {
                AddItemDocumentMapEntries(containerItem.Items, entries, bookmarkPageMap, level + 1);
            }
            else if (item is MaterializedTablixReportItem tablixItem)
            {
                for (var rowIndex = 0; rowIndex < tablixItem.Rows.Count; rowIndex++)
                {
                    var row = tablixItem.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        if (row.Cells[cellIndex].Content is not null)
                        {
                            AddItemDocumentMapEntries([row.Cells[cellIndex].Content!], entries, bookmarkPageMap, level + 1);
                        }
                    }
                }
            }
        }
    }

    private static IReadOnlyList<ReportViewerSearchEntry> BuildSearchEntries(
        Document document,
        IReadOnlyDictionary<int, int> paragraphPageMap,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap)
    {
        var paragraphs = new List<ParagraphBlock>();
        CollectParagraphs(document.Blocks, paragraphs);

        var entries = new List<ReportViewerSearchEntry>(paragraphs.Count);
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            var paragraph = paragraphs[paragraphIndex];
            var anchorPageIndex = paragraphPageMap.TryGetValue(paragraphIndex, out var resolvedAnchorPageIndex) ? resolvedAnchorPageIndex : 0;
            AddFloatingSearchEntries(
                paragraph.FloatingObjects,
                anchorPageIndex,
                floatingPageMap,
                entries);

            var text = ExtractParagraphText(paragraph);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            entries.Add(new ReportViewerSearchEntry
            {
                Text = text,
                ParagraphIndex = paragraphIndex,
                PageIndex = anchorPageIndex
            });
        }

        return entries;
    }

    private static IReadOnlyList<ReportViewerDrillthroughEntry> BuildDrillthroughEntries(
        MaterializedReport report,
        IReadOnlyDictionary<string, int> bookmarkPageMap)
    {
        var entries = new List<ReportViewerDrillthroughEntry>();
        for (var sectionIndex = 0; sectionIndex < report.Sections.Count; sectionIndex++)
        {
            AddDrillthroughEntries(report.Sections[sectionIndex].BodyItems, entries, bookmarkPageMap);
        }

        return entries;
    }

    private static void AddDrillthroughEntries(
        IReadOnlyList<MaterializedReportItem> items,
        List<ReportViewerDrillthroughEntry> entries,
        IReadOnlyDictionary<string, int> bookmarkPageMap)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.DrillthroughAction is not null)
            {
                entries.Add(new ReportViewerDrillthroughEntry
                {
                    Label = string.IsNullOrWhiteSpace(item.Name) ? item.DrillthroughAction.ReportReferenceId : item.Name,
                    Tooltip = item.Tooltip,
                    PageIndex = string.IsNullOrWhiteSpace(item.Bookmark) ? 0 : ResolveBookmarkPageIndex(item.Bookmark, bookmarkPageMap),
                    SourceItemId = item.SourceItemId,
                    Action = CloneDrillthroughAction(item.DrillthroughAction)
                });
            }

            if (item is MaterializedSubreportReportItem subreportItem && subreportItem.Report is not null)
            {
                for (var sectionIndex = 0; sectionIndex < subreportItem.Report.Sections.Count; sectionIndex++)
                {
                    AddDrillthroughEntries(subreportItem.Report.Sections[sectionIndex].BodyItems, entries, bookmarkPageMap);
                }
            }
            else if (item is MaterializedContainerReportItem containerItem)
            {
                AddDrillthroughEntries(containerItem.Items, entries, bookmarkPageMap);
            }
            else if (item is MaterializedTablixReportItem tablixItem)
            {
                for (var rowIndex = 0; rowIndex < tablixItem.Rows.Count; rowIndex++)
                {
                    var row = tablixItem.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        if (row.Cells[cellIndex].Content is not null)
                        {
                            AddDrillthroughEntries([row.Cells[cellIndex].Content!], entries, bookmarkPageMap);
                        }
                    }
                }
            }
        }
    }

    private static int ResolveBookmarkPageIndex(string bookmark, IReadOnlyDictionary<string, int> bookmarkPageMap)
    {
        return bookmarkPageMap.TryGetValue(bookmark, out var pageIndex)
            ? Math.Max(0, pageIndex)
            : 0;
    }

    private static string ExtractParagraphText(ParagraphBlock paragraph)
    {
        if (!string.IsNullOrWhiteSpace(paragraph.Text))
        {
            return paragraph.Text;
        }

        var builder = new StringBuilder();
        for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
        {
            switch (paragraph.Inlines[inlineIndex])
            {
                case RunInline run:
                    builder.Append(run.GetText());
                    break;
                case PageNumberInline:
                    builder.Append("Page");
                    break;
                case TotalPagesInline:
                    builder.Append("Total Pages");
                    break;
            }
        }

        return builder.ToString();
    }

    private static Dictionary<FloatingObject, int> BuildFloatingObjectPageMap(DocumentLayout layout)
    {
        var map = new Dictionary<FloatingObject, int>(ReferenceEqualityComparer.Instance);
        AddFloatingObjectPageMap(layout.FloatingObjects, map);
        AddFloatingObjectPageMap(layout.ExtraFloatingObjects, map);
        return map;
    }

    private static void AddFloatingObjectPageMap(
        IReadOnlyList<FloatingLayoutObject> floatingObjects,
        Dictionary<FloatingObject, int> map)
    {
        for (var index = 0; index < floatingObjects.Count; index++)
        {
            map[floatingObjects[index].Object] = Math.Max(0, floatingObjects[index].PageIndex);
        }
    }

    private static void AddFloatingBookmarks(
        IReadOnlyList<FloatingObject> floatingObjects,
        int fallbackPageIndex,
        Dictionary<string, int> bookmarkPageMap,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap)
    {
        for (var floatingIndex = 0; floatingIndex < floatingObjects.Count; floatingIndex++)
        {
            var floatingObject = floatingObjects[floatingIndex];
            var pageIndex = floatingPageMap.TryGetValue(floatingObject, out var resolvedPageIndex)
                ? resolvedPageIndex
                : fallbackPageIndex;
            if (floatingObject.Content is not ShapeInline shapeInline || shapeInline.TextBox is null)
            {
                continue;
            }

            AddBookmarksFromBlocks(shapeInline.TextBox.Blocks, pageIndex, bookmarkPageMap, floatingPageMap);
        }
    }

    private static void AddBookmarksFromBlocks(
        IReadOnlyList<Block> blocks,
        int pageIndex,
        Dictionary<string, int> bookmarkPageMap,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case ParagraphBlock paragraph:
                    for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
                    {
                        if (paragraph.Inlines[inlineIndex] is BookmarkStartInline bookmarkStart
                            && !string.IsNullOrWhiteSpace(bookmarkStart.Name)
                            && !bookmarkPageMap.ContainsKey(bookmarkStart.Name))
                        {
                            bookmarkPageMap[bookmarkStart.Name] = pageIndex;
                        }
                    }

                    AddFloatingBookmarks(paragraph.FloatingObjects, pageIndex, bookmarkPageMap, floatingPageMap);
                    break;
                case TableBlock table:
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        var row = table.Rows[rowIndex];
                        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        {
                            AddBookmarksFromBlocks(row.Cells[cellIndex].Blocks, pageIndex, bookmarkPageMap, floatingPageMap);
                        }
                    }

                    break;
            }
        }
    }

    private static void AddFloatingSearchEntries(
        IReadOnlyList<FloatingObject> floatingObjects,
        int fallbackPageIndex,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap,
        List<ReportViewerSearchEntry> entries)
    {
        for (var floatingIndex = 0; floatingIndex < floatingObjects.Count; floatingIndex++)
        {
            var floatingObject = floatingObjects[floatingIndex];
            var pageIndex = floatingPageMap.TryGetValue(floatingObject, out var resolvedPageIndex)
                ? resolvedPageIndex
                : fallbackPageIndex;
            if (floatingObject.Content is not ShapeInline shapeInline || shapeInline.TextBox is null)
            {
                continue;
            }

            AddSearchEntriesFromBlocks(shapeInline.TextBox.Blocks, pageIndex, floatingPageMap, entries);
        }
    }

    private static void AddSearchEntriesFromBlocks(
        IReadOnlyList<Block> blocks,
        int pageIndex,
        IReadOnlyDictionary<FloatingObject, int> floatingPageMap,
        List<ReportViewerSearchEntry> entries)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case ParagraphBlock paragraph:
                {
                    var text = ExtractParagraphText(paragraph);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        entries.Add(new ReportViewerSearchEntry
                        {
                            Text = text,
                            ParagraphIndex = entries.Count,
                            PageIndex = pageIndex
                        });
                    }

                    AddFloatingSearchEntries(paragraph.FloatingObjects, pageIndex, floatingPageMap, entries);
                    break;
                }

                case TableBlock table:
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        var row = table.Rows[rowIndex];
                        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        {
                            AddSearchEntriesFromBlocks(row.Cells[cellIndex].Blocks, pageIndex, floatingPageMap, entries);
                        }
                    }

                    break;
            }
        }
    }

    private static void CollectParagraphs(IReadOnlyList<Block> blocks, List<ParagraphBlock> paragraphs)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case ParagraphBlock paragraph:
                    paragraphs.Add(paragraph);
                    break;
                case TableBlock table:
                    CollectParagraphs(table, paragraphs);
                    break;
            }
        }
    }

    private static void CollectParagraphs(TableBlock table, List<ParagraphBlock> paragraphs)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                CollectParagraphs(row.Cells[cellIndex].Blocks, paragraphs);
            }
        }
    }

    private static ReportParameterAvailableValue CloneAvailableValue(ReportParameterAvailableValue availableValue)
    {
        return new ReportParameterAvailableValue
        {
            Label = availableValue.Label,
            Value = availableValue.Value
        };
    }

    private static MaterializedReportDrillthroughAction CloneDrillthroughAction(MaterializedReportDrillthroughAction action)
    {
        var clone = new MaterializedReportDrillthroughAction
        {
            ReportReferenceId = action.ReportReferenceId
        };
        CopyParameters(action.Parameters, clone.Parameters);
        return clone;
    }

    private static ReportParameterValue CloneParameterValue(ReportParameterValue value)
    {
        var clone = new ReportParameterValue
        {
            IsNull = value.IsNull
        };
        for (var index = 0; index < value.Values.Count; index++)
        {
            clone.Values.Add(value.Values[index]);
        }

        for (var index = 0; index < value.Labels.Count; index++)
        {
            clone.Labels.Add(value.Labels[index]);
        }

        return clone;
    }

    private static List<ReportDiagnostic> CloneDiagnostics(IEnumerable<ReportDiagnostic> diagnostics)
    {
        return diagnostics.Select(static diagnostic => new ReportDiagnostic(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Path)).ToList();
    }

    private static void CopyParameters(
        IReadOnlyDictionary<string, ReportParameterValue> source,
        IDictionary<string, ReportParameterValue> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = CloneParameterValue(pair.Value);
        }
    }

    private static void CopyValues(
        IReadOnlyDictionary<string, object?> source,
        IDictionary<string, object?> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyReports(
        IReadOnlyDictionary<string, ReportDefinition> source,
        IDictionary<string, ReportDefinition> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }
}
