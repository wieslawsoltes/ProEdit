using System.Collections;
using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Expressions;

namespace Vibe.Office.Reporting.Materialization;

/// <summary>
/// Default implementation of <see cref="IReportMaterializer" />.
/// </summary>
public sealed class ReportMaterializer : IReportMaterializer
{
    private readonly IReportExpressionCompiler _expressionCompiler;
    private readonly ReportParameterResolver _parameterResolver;
    private readonly ReportDataSetExecutor _dataSetExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportMaterializer" /> class.
    /// </summary>
    /// <param name="expressionCompiler">The expression compiler.</param>
    public ReportMaterializer(IReportExpressionCompiler expressionCompiler)
    {
        _expressionCompiler = expressionCompiler ?? throw new ArgumentNullException(nameof(expressionCompiler));
        _parameterResolver = new ReportParameterResolver(expressionCompiler);
        _dataSetExecutor = new ReportDataSetExecutor(expressionCompiler);
    }

    /// <inheritdoc />
    public ValueTask<ReportMaterializationResult> MaterializeAsync(
        ReportMaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runtime = new MaterializationRuntime(
            request.ProviderRegistry,
            request.HostDataRegistry,
            request.ReferencedReports,
            request.Culture ?? CultureInfo.InvariantCulture,
            request.UiCulture ?? request.Culture ?? CultureInfo.InvariantCulture,
            request.TimeZone ?? TimeZoneInfo.Utc);

        var activeReports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return MaterializeDefinitionAsync(
            request.ReportDefinition,
            request.ParameterValues,
            request.Globals,
            runtime,
            activeReports,
            cancellationToken);
    }

    private async ValueTask<ReportMaterializationResult> MaterializeDefinitionAsync(
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
        IReadOnlyDictionary<string, object?> suppliedGlobals,
        MaterializationRuntime runtime,
        HashSet<string> activeReports,
        CancellationToken cancellationToken)
    {
        var result = new ReportMaterializationResult();
        var reportKey = string.IsNullOrWhiteSpace(reportDefinition.Id) ? Guid.NewGuid().ToString("N") : reportDefinition.Id;
        if (!activeReports.Add(reportKey))
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.SubreportCycleDetected,
                $"Recursive subreport reference detected for '{reportDefinition.Id}'.",
                "$.sections"));
            return result;
        }

        try
        {
            var globals = CreateGlobals(reportDefinition, suppliedGlobals, runtime.TimeZone);
            var parameterResolutionRequest = new ReportParameterResolutionRequest
            {
                ReportDefinition = reportDefinition,
                ProviderRegistry = runtime.ProviderRegistry,
                HostDataRegistry = runtime.HostDataRegistry,
                Culture = runtime.Culture,
                UiCulture = runtime.UiCulture,
                TimeZone = runtime.TimeZone
            };

            CopyParameters(suppliedParameters, parameterResolutionRequest.SuppliedValues);
            CopyValues(globals, parameterResolutionRequest.Globals);

            var parameterResolution = await _parameterResolver.ResolveAsync(parameterResolutionRequest, cancellationToken);
            CopyParameters(parameterResolution.ResolvedValues, result.ResolvedParameters);
            AppendDiagnostics(result.Diagnostics, parameterResolution.Diagnostics);
            if (parameterResolution.HasErrors)
            {
                return result;
            }

            var materializedReport = new MaterializedReport
            {
                Id = reportDefinition.Id,
                Name = reportDefinition.Name,
                GeneratedAt = ResolveGeneratedAt(globals)
            };

            CopyParameters(parameterResolution.ResolvedValues, materializedReport.ResolvedParameters);
            AppendDiagnostics(materializedReport.Diagnostics, parameterResolution.Diagnostics);

            var materializedDataSets = await ExecuteDataSetsAsync(
                reportDefinition,
                parameterResolution.ResolvedValues,
                globals,
                runtime,
                materializedReport.Diagnostics,
                cancellationToken);

            for (var dataSetIndex = 0; dataSetIndex < materializedDataSets.Count; dataSetIndex++)
            {
                materializedReport.DataSets.Add(materializedDataSets[dataSetIndex]);
            }

            var styleResolver = new ReportMaterializationStyleResolver(reportDefinition.Styles);
            var reportContext = CreateReportContext(
                parameterResolution.ResolvedValues,
                EmptyFields,
                EmptyScopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Report,
                reportDefinition.Id);

            for (var sectionIndex = 0; sectionIndex < reportDefinition.Sections.Count; sectionIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var section = reportDefinition.Sections[sectionIndex];
                var sectionPath = $"$.sections[{sectionIndex}]";
                if (!EvaluateVisibility(
                        section.VisibilityExpression,
                        reportContext,
                        sectionPath,
                        materializedReport.Diagnostics))
                {
                    continue;
                }

                var sectionContext = CreateReportContext(
                    parameterResolution.ResolvedValues,
                    EmptyFields,
                    EmptyScopeRows,
                    globals,
                    runtime,
                    ReportExpressionScopeKind.Section,
                    section.Id);

                var materializedSection = new MaterializedReportSection
                {
                    Id = section.Id,
                    Name = section.Name,
                    PageSettings = ClonePageSettings(section.PageSettings),
                    Bookmark = EvaluateStringExpression(section.BookmarkExpression, sectionContext, $"{sectionPath}.bookmarkExpression", materializedReport.Diagnostics)
                };

                await MaterializeItemsAsync(
                    section.HeaderItems,
                    materializedSection.HeaderItems,
                    reportDefinition,
                    parameterResolution.ResolvedValues,
                    materializedDataSets,
                    globals,
                    styleResolver,
                    sectionContext,
                    sectionPath + ".headerItems",
                    runtime,
                    activeReports,
                    materializedReport.Diagnostics,
                    cancellationToken);

                await MaterializeItemsAsync(
                    section.FooterItems,
                    materializedSection.FooterItems,
                    reportDefinition,
                    parameterResolution.ResolvedValues,
                    materializedDataSets,
                    globals,
                    styleResolver,
                    sectionContext,
                    sectionPath + ".footerItems",
                    runtime,
                    activeReports,
                    materializedReport.Diagnostics,
                    cancellationToken);

                await MaterializeItemsAsync(
                    section.BodyItems,
                    materializedSection.BodyItems,
                    reportDefinition,
                    parameterResolution.ResolvedValues,
                    materializedDataSets,
                    globals,
                    styleResolver,
                    sectionContext,
                    sectionPath + ".bodyItems",
                    runtime,
                    activeReports,
                    materializedReport.Diagnostics,
                    cancellationToken);

                materializedReport.Sections.Add(materializedSection);
            }

            result.MaterializedReport = materializedReport;
            AppendDiagnostics(result.Diagnostics, materializedReport.Diagnostics);
            return result;
        }
        finally
        {
            activeReports.Remove(reportKey);
        }
    }

    private async ValueTask<List<MaterializedDataSet>> ExecuteDataSetsAsync(
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var dataSets = new List<MaterializedDataSet>(reportDefinition.DataSets.Count);
        for (var dataSetIndex = 0; dataSetIndex < reportDefinition.DataSets.Count; dataSetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dataSetDefinition = reportDefinition.DataSets[dataSetIndex];
            var executionRequest = new ReportDataSetExecutionRequest
            {
                ReportDefinition = reportDefinition,
                DataSetId = dataSetDefinition.Id,
                ProviderRegistry = runtime.ProviderRegistry,
                HostDataRegistry = runtime.HostDataRegistry,
                Culture = runtime.Culture,
                UiCulture = runtime.UiCulture,
                TimeZone = runtime.TimeZone
            };

            CopyParameters(resolvedParameters, executionRequest.ParameterValues);
            CopyValues(globals, executionRequest.Globals);

            var executionResult = await _dataSetExecutor.ExecuteAsync(executionRequest, cancellationToken);
            AppendDiagnostics(diagnostics, executionResult.Diagnostics);
            if (executionResult.DataSet is null)
            {
                continue;
            }

            dataSets.Add(ToMaterializedDataSet(executionResult.DataSet));
        }

        return dataSets;
    }

    private async ValueTask MaterializeItemsAsync(
        IReadOnlyList<ReportItem> sourceItems,
        List<MaterializedReportItem> targetItems,
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        ReportMaterializationStyleResolver styleResolver,
        ReportExpressionContext baseContext,
        string path,
        MaterializationRuntime runtime,
        HashSet<string> activeReports,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var orderedItems = sourceItems
            .OrderBy(static item => item.Bounds.Y)
            .ThenBy(static item => item.Bounds.X)
            .ToList();

        for (var itemIndex = 0; itemIndex < orderedItems.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = orderedItems[itemIndex];
            var itemPath = $"{path}[{itemIndex}]";
            if (!EvaluateVisibility(item.VisibilityExpression, baseContext, itemPath, diagnostics))
            {
                continue;
            }

            var materializedItem = await MaterializeItemAsync(
                item,
                reportDefinition,
                resolvedParameters,
                materializedDataSets,
                globals,
                styleResolver,
                baseContext,
                itemPath,
                runtime,
                activeReports,
                diagnostics,
                cancellationToken);
            if (materializedItem is not null)
            {
                targetItems.Add(materializedItem);
            }
        }
    }

    private async ValueTask<MaterializedReportItem?> MaterializeItemAsync(
        ReportItem item,
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        ReportMaterializationStyleResolver styleResolver,
        ReportExpressionContext context,
        string path,
        MaterializationRuntime runtime,
        HashSet<string> activeReports,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var bookmark = EvaluateStringExpression(item.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics);
        var tooltip = EvaluateStringExpression(item.TooltipExpression, context, path + ".tooltipExpression", diagnostics);
        var style = styleResolver.Resolve(item.StyleName);
        var drillthrough = MaterializeDrillthrough(item.DrillthroughAction, context, path + ".drillthroughAction", diagnostics);

        return item switch
        {
            TextItem textItem => MaterializeTextItem(textItem, style, bookmark, tooltip, drillthrough, context, path, diagnostics),
            ImageItem imageItem => MaterializeImageItem(imageItem, style, bookmark, tooltip, drillthrough, context, path, diagnostics),
            LineItem lineItem => CreateBaseItem(
                new MaterializedLineReportItem
                {
                    X2 = lineItem.X2,
                    Y2 = lineItem.Y2
                },
                lineItem,
                style,
                bookmark,
                tooltip,
                drillthrough),
            ShapeItem shapeItem => CreateBaseItem(
                new MaterializedShapeReportItem
                {
                    Shape = shapeItem.Shape
                },
                shapeItem,
                style,
                bookmark,
                tooltip,
                drillthrough),
            ChartItem chartItem => MaterializeChartItem(chartItem, style, bookmark, tooltip, drillthrough, materializedDataSets, resolvedParameters, globals, runtime, context, path, diagnostics),
            TablixItem tablixItem => MaterializeTablixItem(tablixItem, style, bookmark, tooltip, drillthrough, materializedDataSets, resolvedParameters, globals, runtime, context, path, styleResolver, diagnostics),
            SubreportItem subreportItem => await MaterializeSubreportItemAsync(subreportItem, style, bookmark, tooltip, drillthrough, globals, runtime, context, path, activeReports, diagnostics, cancellationToken),
            DocumentTemplateItem templateItem => MaterializeDocumentTemplateItem(templateItem, reportDefinition, style, bookmark, tooltip, drillthrough, context, path, diagnostics),
            _ => null
        };
    }

    private MaterializedTextReportItem MaterializeTextItem(
        TextItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var materialized = CreateBaseItem(
            new MaterializedTextReportItem(),
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        if (string.IsNullOrWhiteSpace(item.ValueExpression))
        {
            materialized.Text = item.StaticText ?? string.Empty;
            materialized.ValueKind = MaterializedTextValueKind.Static;
            return materialized;
        }

        if (IsPageNumberExpression(item.ValueExpression))
        {
            materialized.ValueKind = MaterializedTextValueKind.PageNumber;
            return materialized;
        }

        if (IsTotalPagesExpression(item.ValueExpression))
        {
            materialized.ValueKind = MaterializedTextValueKind.TotalPages;
            return materialized;
        }

        var value = EvaluateExpression(item.ValueExpression, context, path + ".valueExpression", diagnostics);
        materialized.Text = FormatValue(value, item.FormatString, context.Culture);
        materialized.ValueKind = MaterializedTextValueKind.Expression;
        return materialized;
    }

    private MaterializedImageReportItem MaterializeImageItem(
        ImageItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var materialized = CreateBaseItem(
            new MaterializedImageReportItem
            {
                SizingMode = item.SizingMode,
                ContentType = string.IsNullOrWhiteSpace(item.MimeType) ? "image/png" : item.MimeType
            },
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        switch (item.SourceKind)
        {
            case ReportImageSourceKind.Embedded:
                materialized.Data = item.EmbeddedData;
                break;
            case ReportImageSourceKind.Uri:
            {
                var resolved = item.ValueExpression ?? string.Empty;
                if (TryLoadImageBytes(resolved, materialized.ContentType, out var uriBytes, out var uriContentType))
                {
                    materialized.Data = uriBytes;
                    materialized.ContentType = uriContentType;
                }
                else
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                        $"Image URI '{resolved}' could not be loaded.",
                        path + ".valueExpression"));
                }

                break;
            }
            default:
            {
                var value = EvaluateExpression(item.ValueExpression, context, path + ".valueExpression", diagnostics);
                if (!TryResolveImageValue(value, materialized.ContentType, out var data, out var contentType))
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                        $"Image expression for item '{item.Id}' did not produce a supported payload.",
                        path + ".valueExpression"));
                }
                else
                {
                    materialized.Data = data;
                    materialized.ContentType = contentType;
                }

                break;
            }
        }

        return materialized;
    }

    private MaterializedChartReportItem MaterializeChartItem(
        ChartItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var materialized = CreateBaseItem(
            new MaterializedChartReportItem(),
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        var dataSet = FindDataSet(materializedDataSets, item.DataSetId);
        if (dataSet is null)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DataSetNotFound,
                $"Dataset '{item.DataSetId}' was not found for chart '{item.Id}'.",
                path + ".dataSetId"));
            return materialized;
        }

        var rows = ToScopeRows(dataSet.Rows);
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            BarDirection = ChartBarDirection.Column,
            Title = EvaluateStringExpression(item.TitleExpression, context, path + ".titleExpression", diagnostics)
        };

        var seriesMap = new Dictionary<string, ChartSeries>(StringComparer.OrdinalIgnoreCase);
        for (var rowIndex = 0; rowIndex < dataSet.Rows.Count; rowIndex++)
        {
            var row = dataSet.Rows[rowIndex];
            var rowContext = CreateReportContext(
                resolvedParameters,
                row.Values,
                rows,
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                item.DataSetId,
                rowIndex);

            var category = EvaluateStringExpression(item.CategoryExpression, rowContext, path + ".categoryExpression", diagnostics);
            for (var seriesIndex = 0; seriesIndex < item.Series.Count; seriesIndex++)
            {
                var seriesDefinition = item.Series[seriesIndex];
                var seriesPath = $"{path}.series[{seriesIndex}]";
                var seriesName = EvaluateStringExpression(seriesDefinition.NameExpression, rowContext, seriesPath + ".nameExpression", diagnostics);
                if (string.IsNullOrWhiteSpace(seriesName))
                {
                    seriesName = $"Series {seriesIndex + 1}";
                }

                var rawValue = EvaluateExpression(seriesDefinition.ValueExpression, rowContext, seriesPath + ".valueExpression", diagnostics);
                if (!TryConvertToDouble(rawValue, runtime.Culture, out var numericValue))
                {
                    continue;
                }

                if (!seriesMap.TryGetValue(seriesName, out var chartSeries))
                {
                    chartSeries = new ChartSeries
                    {
                        Name = seriesName
                    };
                    seriesMap[seriesName] = chartSeries;
                    chart.Series.Add(chartSeries);
                }

                var point = new ChartPoint
                {
                    Category = category,
                    Value = numericValue
                };

                var colorText = EvaluateStringExpression(seriesDefinition.ColorExpression, rowContext, seriesPath + ".colorExpression", diagnostics);
                if (TryParseColor(colorText, out var color))
                {
                    point.Style = new ChartStyle
                    {
                        Fill = new ChartFillStyle
                        {
                            Color = color
                        }
                    };
                }

                chartSeries.Points.Add(point);
            }
        }

        materialized.Model = chart;
        return materialized;
    }

    private MaterializedTablixReportItem MaterializeTablixItem(
        TablixItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext context,
        string path,
        ReportMaterializationStyleResolver styleResolver,
        List<ReportDiagnostic> diagnostics)
    {
        var materialized = CreateBaseItem(
            new MaterializedTablixReportItem
            {
                RepeatHeaderRows = item.RepeatHeaderRows
            },
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        for (var columnIndex = 0; columnIndex < item.Columns.Count; columnIndex++)
        {
            var column = item.Columns[columnIndex];
            materialized.Columns.Add(new MaterializedTablixColumn
            {
                Id = column.Id,
                Width = column.Width
            });
        }

        var dataSet = FindDataSet(materializedDataSets, item.DataSetId);
        IReadOnlyList<MaterializedDataRow> dataRows = dataSet is null
            ? Array.Empty<MaterializedDataRow>()
            : dataSet.Rows;
        var scopeRows = ToScopeRows(dataRows);

        for (var rowIndex = 0; rowIndex < item.Rows.Count; rowIndex++)
        {
            var rowDefinition = item.Rows[rowIndex];
            if (rowDefinition.IsHeader)
            {
                materialized.Rows.Add(MaterializeTablixRow(
                    rowDefinition,
                    context,
                    path + $".rows[{rowIndex}]",
                    styleResolver,
                    diagnostics));
                continue;
            }

            if (dataRows.Count == 0)
            {
                materialized.Rows.Add(MaterializeTablixRow(
                    rowDefinition,
                    context,
                    path + $".rows[{rowIndex}]",
                    styleResolver,
                    diagnostics));
                continue;
            }

            for (var detailIndex = 0; detailIndex < dataRows.Count; detailIndex++)
            {
                var row = dataRows[detailIndex];
                var rowContext = CreateReportContext(
                    resolvedParameters,
                    row.Values,
                    scopeRows,
                    globals,
                    runtime,
                    ReportExpressionScopeKind.Row,
                    item.DataSetId,
                    detailIndex);
                materialized.Rows.Add(MaterializeTablixRow(
                    rowDefinition,
                    rowContext,
                    path + $".rows[{rowIndex}]",
                    styleResolver,
                    diagnostics));
            }
        }

        return materialized;
    }

    private MaterializedTablixRow MaterializeTablixRow(
        ReportTablixRowDefinition rowDefinition,
        ReportExpressionContext context,
        string path,
        ReportMaterializationStyleResolver styleResolver,
        List<ReportDiagnostic> diagnostics)
    {
        var row = new MaterializedTablixRow
        {
            IsHeader = rowDefinition.IsHeader
        };

        for (var cellIndex = 0; cellIndex < rowDefinition.Cells.Count; cellIndex++)
        {
            var cellDefinition = rowDefinition.Cells[cellIndex];
            var value = cellDefinition.ValueExpression is null
                ? cellDefinition.Text
                : FormatValue(
                    EvaluateExpression(cellDefinition.ValueExpression, context, $"{path}.cells[{cellIndex}].valueExpression", diagnostics),
                    cellDefinition.FormatString,
                    context.Culture);

            row.Cells.Add(new MaterializedTablixCell
            {
                Text = value ?? string.Empty,
                StyleName = cellDefinition.StyleName,
                Style = styleResolver.Resolve(cellDefinition.StyleName),
                RowSpan = Math.Max(1, cellDefinition.RowSpan),
                ColumnSpan = Math.Max(1, cellDefinition.ColumnSpan)
            });
        }

        return row;
    }

    private async ValueTask<MaterializedSubreportReportItem> MaterializeSubreportItemAsync(
        SubreportItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext context,
        string path,
        HashSet<string> activeReports,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var materialized = CreateBaseItem(
            new MaterializedSubreportReportItem(),
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        if (!runtime.ReferencedReports.TryGetValue(item.ReportReferenceId, out var referencedReport))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.SubreportNotFound,
                $"Subreport '{item.ReportReferenceId}' was not found.",
                path + ".reportReferenceId"));
            return materialized;
        }

        var parameters = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);
        for (var parameterIndex = 0; parameterIndex < item.Parameters.Count; parameterIndex++)
        {
            var parameter = item.Parameters[parameterIndex];
            var value = EvaluateExpression(
                parameter.ValueExpression,
                context,
                $"{path}.parameters[{parameterIndex}].valueExpression",
                diagnostics);
            parameters[parameter.ParameterId] = ToParameterValue(value);
        }

        var nestedResult = await MaterializeDefinitionAsync(
            referencedReport,
            parameters,
            globals,
            runtime,
            activeReports,
            cancellationToken);
        AppendDiagnostics(diagnostics, nestedResult.Diagnostics);
        materialized.Report = nestedResult.MaterializedReport;
        return materialized;
    }

    private MaterializedDocumentTemplateReportItem MaterializeDocumentTemplateItem(
        DocumentTemplateItem item,
        ReportDefinition reportDefinition,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var materialized = CreateBaseItem(
            new MaterializedDocumentTemplateReportItem(),
            item,
            style,
            bookmark,
            tooltip,
            drillthrough);

        if (!TryResolveTemplateContent(item, reportDefinition, path, diagnostics, out var format, out var content))
        {
            return materialized;
        }

        materialized.TemplateFormat = format;
        materialized.Content = content;
        foreach (var pair in item.Bindings)
        {
            var value = EvaluateExpression(pair.Value, context, $"{path}.bindings[{pair.Key}]", diagnostics);
            materialized.Bindings[pair.Key] = FormatValue(value, formatString: null, context.Culture);
        }

        return materialized;
    }

    private bool TryResolveTemplateContent(
        DocumentTemplateItem item,
        ReportDefinition reportDefinition,
        string path,
        List<ReportDiagnostic> diagnostics,
        out ReportDocumentTemplateFormat format,
        out string? content)
    {
        format = item.TemplateFormat;
        content = item.EmbeddedContent;

        if (!string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(item.TemplateId))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentTemplateNotFound,
                $"Template item '{item.Id}' does not define embedded content or a shared template reference.",
                path));
            return false;
        }

        var sharedTemplate = reportDefinition.SharedTemplates.FirstOrDefault(template =>
            template.Id.Equals(item.TemplateId, StringComparison.OrdinalIgnoreCase));
        if (sharedTemplate is null)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentTemplateNotFound,
                $"Shared template '{item.TemplateId}' was not found.",
                path + ".templateId"));
            return false;
        }

        format = sharedTemplate.Format;
        if (sharedTemplate.IsEmbedded)
        {
            content = sharedTemplate.Content;
            if (!string.IsNullOrWhiteSpace(content))
            {
                return true;
            }

            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentTemplateNotFound,
                $"Shared template '{sharedTemplate.Id}' does not contain embedded content.",
                path + ".templateId"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(sharedTemplate.Source))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentTemplateNotFound,
                $"Shared template '{sharedTemplate.Id}' does not define a source path.",
                path + ".templateId"));
            return false;
        }

        try
        {
            var sourcePath = ResolveTemplatePath(sharedTemplate.Source);
            if (format == ReportDocumentTemplateFormat.Docx)
            {
                content = Convert.ToBase64String(File.ReadAllBytes(sourcePath));
            }
            else
            {
                content = File.ReadAllText(sourcePath, Encoding.UTF8);
            }

            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                ex.Message,
                path + ".templateId"));
            return false;
        }
    }

    private MaterializedReportDrillthroughAction? MaterializeDrillthrough(
        ReportDrillthroughAction? action,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (action is null)
        {
            return null;
        }

        var resolved = new MaterializedReportDrillthroughAction
        {
            ReportReferenceId = action.ReportReferenceId
        };

        for (var parameterIndex = 0; parameterIndex < action.Parameters.Count; parameterIndex++)
        {
            var parameter = action.Parameters[parameterIndex];
            var value = EvaluateExpression(
                parameter.ValueExpression,
                context,
                $"{path}.parameters[{parameterIndex}].valueExpression",
                diagnostics);
            resolved.Parameters[parameter.ParameterId] = ToParameterValue(value);
        }

        return resolved;
    }

    private static T CreateBaseItem<T>(
        T target,
        ReportItem source,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportDrillthroughAction? drillthrough)
        where T : MaterializedReportItem
    {
        target.SourceItemId = source.Id;
        target.Name = source.Name;
        target.Bounds = source.Bounds;
        target.StyleName = source.StyleName;
        target.Style = style?.Clone();
        target.Bookmark = bookmark;
        target.Tooltip = tooltip;
        target.DrillthroughAction = drillthrough;
        return target;
    }

    private static MaterializedDataSet ToMaterializedDataSet(ReportDataTable table)
    {
        var materialized = new MaterializedDataSet
        {
            Id = table.DataSetId
        };

        for (var fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
        {
            var field = table.Fields[fieldIndex];
            materialized.Fields.Add(new ReportFieldDefinition
            {
                Name = field.Name,
                DataType = field.DataType
            });
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var materializedRow = new MaterializedDataRow();
            foreach (var pair in row.Values)
            {
                materializedRow.Values[pair.Key] = pair.Value;
            }

            materialized.Rows.Add(materializedRow);
        }

        return materialized;
    }

    private static MaterializedDataSet? FindDataSet(
        IReadOnlyList<MaterializedDataSet> dataSets,
        string? dataSetId)
    {
        if (string.IsNullOrWhiteSpace(dataSetId))
        {
            return null;
        }

        for (var index = 0; index < dataSets.Count; index++)
        {
            if (dataSets[index].Id.Equals(dataSetId, StringComparison.OrdinalIgnoreCase))
            {
                return dataSets[index];
            }
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToScopeRows(
        IReadOnlyList<MaterializedDataRow> rows)
    {
        if (rows.Count == 0)
        {
            return EmptyScopeRows;
        }

        var scopeRows = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            scopeRows.Add(rows[index].Values);
        }

        return scopeRows;
    }

    private static ReportExpressionContext CreateReportContext(
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopeRows,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionScopeKind scopeKind,
        string? scopeName,
        int rowIndex = 0)
    {
        return new ReportExpressionContext
        {
            Parameters = parameters,
            Fields = fields,
            ScopeRows = scopeRows,
            ParentScopeRows = EmptyScopeRows,
            Globals = globals,
            Culture = runtime.Culture,
            UiCulture = runtime.UiCulture,
            TimeZone = runtime.TimeZone,
            ScopeKind = scopeKind,
            ScopeName = scopeName,
            RowIndex = rowIndex
        };
    }

    private object? EvaluateExpression(
        string? expression,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var compilation = _expressionCompiler.Compile(expression);
        AppendExpressionDiagnostics(compilation.Diagnostics, path, diagnostics);
        if (compilation.Expression is null || compilation.HasErrors)
        {
            return null;
        }

        if (!compilation.Expression.TryEvaluate(context, out var value, out var diagnostic))
        {
            diagnostics.Add(CloneDiagnostic(diagnostic!, path));
            return null;
        }

        return value;
    }

    private string? EvaluateStringExpression(
        string? expression,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var value = EvaluateExpression(expression, context, path, diagnostics);
        return value is null ? null : FormatValue(value, formatString: null, context.Culture);
    }

    private bool EvaluateVisibility(
        string? visibilityExpression,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(visibilityExpression))
        {
            return true;
        }

        var value = EvaluateExpression(visibilityExpression, context, path + ".visibilityExpression", diagnostics);
        return TryConvertToBoolean(value, context.Culture, defaultValue: false);
    }

    private static ReportParameterValue ToParameterValue(object? value)
    {
        if (value is null)
        {
            return new ReportParameterValue
            {
                IsNull = true
            };
        }

        if (value is IEnumerable enumerable && value is not string && value is not byte[])
        {
            var parameterValue = new ReportParameterValue();
            foreach (var item in enumerable)
            {
                parameterValue.Values.Add(item);
            }

            parameterValue.IsNull = parameterValue.Values.Count == 0 || parameterValue.Values.All(static item => item is null);
            return parameterValue;
        }

        return ReportParameterValue.FromScalar(value);
    }

    private static Dictionary<string, object?> CreateGlobals(
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, object?> suppliedGlobals,
        TimeZoneInfo timeZone)
    {
        var globals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in suppliedGlobals)
        {
            globals[pair.Key] = pair.Value;
        }

        if (!globals.ContainsKey("ExecutionTime"))
        {
            globals["ExecutionTime"] = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        }

        globals["ReportName"] = reportDefinition.Name;
        globals["ReportId"] = reportDefinition.Id;
        globals.TryAdd("PageNumber", 1);
        globals.TryAdd("TotalPages", 1);
        return globals;
    }

    private static DateTimeOffset ResolveGeneratedAt(IReadOnlyDictionary<string, object?> globals)
    {
        if (globals.TryGetValue("ExecutionTime", out var executionTime))
        {
            return executionTime switch
            {
                DateTimeOffset offset => offset,
                DateTime dateTime => new DateTimeOffset(dateTime),
                _ => DateTimeOffset.UtcNow
            };
        }

        return DateTimeOffset.UtcNow;
    }

    private static ReportPageSettings ClonePageSettings(ReportPageSettings settings)
    {
        return new ReportPageSettings
        {
            Width = settings.Width,
            Height = settings.Height,
            Orientation = settings.Orientation,
            MarginLeft = settings.MarginLeft,
            MarginTop = settings.MarginTop,
            MarginRight = settings.MarginRight,
            MarginBottom = settings.MarginBottom,
            ColumnCount = settings.ColumnCount,
            ColumnGap = settings.ColumnGap
        };
    }

    private static string ResolveTemplatePath(string source)
    {
        if (Path.IsPathRooted(source))
        {
            return source;
        }

        return Path.GetFullPath(source, Environment.CurrentDirectory);
    }

    private static void CopyParameters(
        IReadOnlyDictionary<string, ReportParameterValue> source,
        Dictionary<string, ReportParameterValue> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = CloneParameterValue(pair.Value);
        }
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

        return clone;
    }

    private static void CopyValues(
        IReadOnlyDictionary<string, object?> source,
        Dictionary<string, object?> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void AppendDiagnostics(
        List<ReportDiagnostic> target,
        IReadOnlyList<ReportDiagnostic> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static void AppendExpressionDiagnostics(
        IReadOnlyList<ReportDiagnostic> diagnostics,
        string path,
        List<ReportDiagnostic> target)
    {
        for (var index = 0; index < diagnostics.Count; index++)
        {
            target.Add(CloneDiagnostic(diagnostics[index], path));
        }
    }

    private static ReportDiagnostic CloneDiagnostic(
        ReportDiagnostic diagnostic,
        string path)
    {
        return new ReportDiagnostic(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            path);
    }

    private static string FormatValue(
        object? value,
        string? formatString,
        CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(formatString) && value is IFormattable formattable)
        {
            return formattable.ToString(formatString, culture);
        }

        return Convert.ToString(value, culture) ?? string.Empty;
    }

    private static bool IsPageNumberExpression(string expression)
    {
        return string.Equals(NormalizeExpression(expression), "globals.pagenumber", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTotalPagesExpression(string expression)
    {
        return string.Equals(NormalizeExpression(expression), "globals.totalpages", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExpression(string expression)
    {
        return string.Concat(expression.Where(static ch => !char.IsWhiteSpace(ch)));
    }

    private static bool TryResolveImageValue(
        object? value,
        string defaultContentType,
        out byte[]? data,
        out string contentType)
    {
        data = null;
        contentType = defaultContentType;
        if (value is null)
        {
            return false;
        }

        if (value is byte[] byteArray)
        {
            data = byteArray;
            return true;
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            data = memory.ToArray();
            return true;
        }

        if (value is string text)
        {
            return TryLoadImageBytes(text, defaultContentType, out data, out contentType);
        }

        return false;
    }

    private static bool TryLoadImageBytes(
        string text,
        string defaultContentType,
        out byte[]? data,
        out string contentType)
    {
        data = null;
        contentType = defaultContentType;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = text.IndexOf(',');
            if (commaIndex <= 5 || commaIndex + 1 >= text.Length)
            {
                return false;
            }

            var header = text.Substring(5, commaIndex - 5);
            contentType = header.Split(';', 2)[0];
            data = Convert.FromBase64String(text[(commaIndex + 1)..]);
            return true;
        }

        var filePath = Path.IsPathRooted(text)
            ? text
            : Path.GetFullPath(text, Environment.CurrentDirectory);
        if (!File.Exists(filePath))
        {
            return false;
        }

        data = File.ReadAllBytes(filePath);
        contentType = ResolveContentTypeFromPath(filePath, defaultContentType);
        return true;
    }

    private static string ResolveContentTypeFromPath(
        string path,
        string fallback)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".png" => "image/png",
            _ => fallback
        };
    }

    private static bool TryConvertToDouble(
        object? value,
        CultureInfo culture,
        out double result)
    {
        result = 0d;
        if (value is null)
        {
            return false;
        }

        try
        {
            result = Convert.ToDouble(value, culture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertToBoolean(
        object? value,
        CultureInfo culture,
        bool defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value is bool booleanValue)
        {
            return booleanValue;
        }

        if (value is string text)
        {
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (double.TryParse(text, NumberStyles.Number, culture, out var numeric))
            {
                return Math.Abs(numeric) > double.Epsilon;
            }

            return !string.IsNullOrWhiteSpace(text);
        }

        try
        {
            return Convert.ToBoolean(value, culture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static bool TryParseColor(
        string? value,
        out Vibe.Office.Primitives.DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim().AsSpan();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length == 6
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = new Vibe.Office.Primitives.DocColor(r, g, b);
            return true;
        }

        if (span.Length == 8
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            color = Vibe.Office.Primitives.DocColor.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyFields =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> EmptyScopeRows =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    private sealed record MaterializationRuntime(
        ReportDataProviderRegistry ProviderRegistry,
        ReportHostDataRegistry HostDataRegistry,
        IReadOnlyDictionary<string, ReportDefinition> ReferencedReports,
        CultureInfo Culture,
        CultureInfo UiCulture,
        TimeZoneInfo TimeZone);
}
