using System.Collections;
using System.Globalization;
using System.Linq;
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
                DefaultFontFamily = reportDefinition.DefaultFontFamily,
                GeneratedAt = ResolveGeneratedAt(globals),
                ConsumeContainerWhitespace = reportDefinition.ConsumeContainerWhitespace
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
            var baseNamedScopes = CreateNamedScopes(materializedDataSets);
            var defaultFields = ResolveDefaultFields(materializedDataSets, out var defaultScopeRows, out var defaultDataSetId);
            var reportContext = CreateReportContext(
                parameterResolution.ResolvedValues,
                defaultFields,
                defaultScopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Report,
                defaultDataSetId ?? reportDefinition.Id,
                namedScopes: baseNamedScopes);

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
                    defaultFields,
                    defaultScopeRows,
                    globals,
                    runtime,
                    ReportExpressionScopeKind.Section,
                    defaultDataSetId ?? section.Id,
                    namedScopes: baseNamedScopes);

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
        var style = styleResolver.Resolve(item.StyleName, context, path + ".styleName", diagnostics);
        var pageBreak = MaterializePageBreak(item.PageBreak, context, path + ".pageBreak", diagnostics);
        var drillthrough = MaterializeDrillthrough(item.DrillthroughAction, context, path + ".drillthroughAction", diagnostics);

        return item switch
        {
            TextItem textItem => MaterializeTextItem(textItem, styleResolver, style, bookmark, tooltip, pageBreak, drillthrough, context, path, diagnostics),
            ImageItem imageItem => MaterializeImageItem(imageItem, style, bookmark, tooltip, pageBreak, drillthrough, context, path, diagnostics),
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
                pageBreak,
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
                pageBreak,
                drillthrough),
            ContainerItem containerItem => await MaterializeContainerItemAsync(containerItem, style, bookmark, tooltip, pageBreak, drillthrough, reportDefinition, resolvedParameters, materializedDataSets, globals, styleResolver, context, path, runtime, activeReports, diagnostics, cancellationToken),
            ChartItem chartItem => MaterializeChartItem(chartItem, style, bookmark, tooltip, pageBreak, drillthrough, materializedDataSets, resolvedParameters, globals, runtime, context, path, diagnostics),
            GaugeItem gaugeItem => MaterializeGaugeItem(gaugeItem, style, bookmark, tooltip, pageBreak, drillthrough, materializedDataSets, resolvedParameters, globals, runtime, context, path, diagnostics),
            TablixItem tablixItem => MaterializeTablixItem(tablixItem, style, bookmark, tooltip, pageBreak, drillthrough, reportDefinition, materializedDataSets, resolvedParameters, globals, runtime, context, path, styleResolver, diagnostics),
            SubreportItem subreportItem => await MaterializeSubreportItemAsync(subreportItem, style, bookmark, tooltip, pageBreak, drillthrough, globals, runtime, context, path, activeReports, diagnostics, cancellationToken),
            DocumentTemplateItem templateItem => MaterializeDocumentTemplateItem(templateItem, reportDefinition, style, bookmark, tooltip, pageBreak, drillthrough, context, path, diagnostics),
            _ => null
        };
    }

    private MaterializedTextReportItem MaterializeTextItem(
        TextItem item,
        ReportMaterializationStyleResolver styleResolver,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
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
            pageBreak,
            drillthrough);

        if (item.Paragraphs.Count > 0)
        {
            MaterializeTextParagraphs(materialized, item, styleResolver, style, context, path, diagnostics);
            materialized.Text = string.Join(
                Environment.NewLine,
                materialized.Paragraphs.Select(static paragraph => string.Concat(paragraph.Runs.Select(static run => run.Text))));
            materialized.ValueKind = materialized.Paragraphs.Count == 1
                                     && materialized.Paragraphs[0].Runs.Count == 1
                ? materialized.Paragraphs[0].Runs[0].ValueKind
                : MaterializedTextValueKind.Static;
            materialized.CanGrow = item.CanGrow;
            materialized.CanShrink = item.CanShrink;
            return materialized;
        }

        if (string.IsNullOrWhiteSpace(item.ValueExpression))
        {
            materialized.Text = item.StaticText ?? string.Empty;
            materialized.ValueKind = MaterializedTextValueKind.Static;
            materialized.CanGrow = item.CanGrow;
            materialized.CanShrink = item.CanShrink;
            return materialized;
        }

        if (IsPageNumberExpression(item.ValueExpression))
        {
            materialized.ValueKind = MaterializedTextValueKind.PageNumber;
            materialized.CanGrow = item.CanGrow;
            materialized.CanShrink = item.CanShrink;
            return materialized;
        }

        if (IsTotalPagesExpression(item.ValueExpression))
        {
            materialized.ValueKind = MaterializedTextValueKind.TotalPages;
            materialized.CanGrow = item.CanGrow;
            materialized.CanShrink = item.CanShrink;
            return materialized;
        }

        var value = EvaluateExpression(item.ValueExpression, context, path + ".valueExpression", diagnostics);
        materialized.Text = FormatValue(value, item.FormatString, context.Culture);
        materialized.Style = styleResolver.Resolve(item.StyleName, context.CreateWithSelfValue(value), path + ".styleName", diagnostics);
        materialized.ValueKind = MaterializedTextValueKind.Expression;
        materialized.CanGrow = item.CanGrow;
        materialized.CanShrink = item.CanShrink;
        return materialized;
    }

    private void MaterializeTextParagraphs(
        MaterializedTextReportItem materialized,
        TextItem item,
        ReportMaterializationStyleResolver styleResolver,
        MaterializedReportStyle? baseStyle,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        for (var paragraphIndex = 0; paragraphIndex < item.Paragraphs.Count; paragraphIndex++)
        {
            var sourceParagraph = item.Paragraphs[paragraphIndex];
            var materializedParagraph = new MaterializedTextParagraph
            {
                TextAlign = sourceParagraph.TextAlign
            };

            for (var runIndex = 0; runIndex < sourceParagraph.Runs.Count; runIndex++)
            {
                var sourceRun = sourceParagraph.Runs[runIndex];
                var valueKind = MaterializedTextValueKind.Static;
                var resolvedText = sourceRun.StaticText ?? string.Empty;
                object? resolvedValue = null;

                if (!string.IsNullOrWhiteSpace(sourceRun.ValueExpression))
                {
                    if (IsPageNumberExpression(sourceRun.ValueExpression))
                    {
                        valueKind = MaterializedTextValueKind.PageNumber;
                        resolvedText = string.Empty;
                    }
                    else if (IsTotalPagesExpression(sourceRun.ValueExpression))
                    {
                        valueKind = MaterializedTextValueKind.TotalPages;
                        resolvedText = string.Empty;
                    }
                    else
                    {
                        resolvedValue = EvaluateExpression(
                            sourceRun.ValueExpression,
                            context,
                            $"{path}.paragraphs[{paragraphIndex}].runs[{runIndex}].valueExpression",
                            diagnostics);
                        resolvedText = FormatValue(resolvedValue, item.FormatString, context.Culture);
                        valueKind = MaterializedTextValueKind.Expression;
                    }
                }

                var runContext = resolvedValue is not null ? context.CreateWithSelfValue(resolvedValue) : context;
                var runStyle = string.IsNullOrWhiteSpace(sourceRun.StyleName)
                    ? null
                    : styleResolver.Resolve(
                        sourceRun.StyleName,
                        runContext,
                        $"{path}.paragraphs[{paragraphIndex}].runs[{runIndex}].styleName",
                        diagnostics);

                materializedParagraph.Runs.Add(new MaterializedTextRun
                {
                    Text = resolvedText,
                    ValueKind = valueKind,
                    Style = MergeStyles(baseStyle, runStyle)
                });
            }

            if (materializedParagraph.Runs.Count == 0)
            {
                materializedParagraph.Runs.Add(new MaterializedTextRun
                {
                    Style = baseStyle?.Clone()
                });
            }

            materialized.Paragraphs.Add(materializedParagraph);
        }
    }

    private static MaterializedReportStyle? MergeStyles(
        MaterializedReportStyle? baseStyle,
        MaterializedReportStyle? overrideStyle)
    {
        if (baseStyle is null)
        {
            return overrideStyle?.Clone();
        }

        if (overrideStyle is null)
        {
            return baseStyle.Clone();
        }

        var merged = baseStyle.Clone();
        merged.FontFamily = overrideStyle.FontFamily ?? merged.FontFamily;
        merged.FontSize = overrideStyle.FontSize ?? merged.FontSize;
        merged.Foreground = overrideStyle.Foreground ?? merged.Foreground;
        merged.Background = overrideStyle.Background ?? merged.Background;
        merged.BackgroundGradientType = overrideStyle.BackgroundGradientType ?? merged.BackgroundGradientType;
        merged.BackgroundGradientEndColor = overrideStyle.BackgroundGradientEndColor ?? merged.BackgroundGradientEndColor;
        merged.Bold = overrideStyle.Bold ?? merged.Bold;
        merged.Italic = overrideStyle.Italic ?? merged.Italic;
        merged.Border = overrideStyle.Border?.Clone() ?? merged.Border;
        merged.TopBorder = overrideStyle.TopBorder?.Clone() ?? merged.TopBorder;
        merged.BottomBorder = overrideStyle.BottomBorder?.Clone() ?? merged.BottomBorder;
        merged.LeftBorder = overrideStyle.LeftBorder?.Clone() ?? merged.LeftBorder;
        merged.RightBorder = overrideStyle.RightBorder?.Clone() ?? merged.RightBorder;
        merged.PaddingLeft = overrideStyle.PaddingLeft ?? merged.PaddingLeft;
        merged.PaddingRight = overrideStyle.PaddingRight ?? merged.PaddingRight;
        merged.PaddingTop = overrideStyle.PaddingTop ?? merged.PaddingTop;
        merged.PaddingBottom = overrideStyle.PaddingBottom ?? merged.PaddingBottom;
        merged.TextAlign = overrideStyle.TextAlign ?? merged.TextAlign;
        merged.VerticalAlign = overrideStyle.VerticalAlign ?? merged.VerticalAlign;
        merged.TextDecoration = overrideStyle.TextDecoration ?? merged.TextDecoration;
        return merged;
    }

    private MaterializedImageReportItem MaterializeImageItem(
        ImageItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
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
            pageBreak,
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
        MaterializedReportPageBreak? pageBreak,
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
            pageBreak,
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

        var chartScopeRows = ResolveScopedDataRows(dataSet.Rows, context.ScopeRows);
        var chart = new ChartModel
        {
            Type = item.Type,
            BarDirection = item.BarDirection,
            Title = EvaluateStringExpression(item.TitleExpression, context, path + ".titleExpression", diagnostics),
            TitleTextStyle = CloneChartTextStyle(item.TitleTextStyle),
            TitlePosition = item.TitlePosition,
            PaletteName = item.PaletteName,
            ChartAreaStyle = CloneChartStyle(item.ChartAreaStyle),
            PlotAreaStyle = CloneChartStyle(item.PlotAreaStyle),
            Legend = CloneChartLegend(item.Legend)
        };
        CloneChartAxes(item.Axes, chart.Axes);

        if (item.Type is ChartType.Treemap or ChartType.Sunburst)
        {
            var hierarchyRoots = BuildShapeChartHierarchy(
                item,
                chartScopeRows,
                resolvedParameters,
                globals,
                runtime,
                context,
                path,
                diagnostics);
            foreach (var root in hierarchyRoots)
            {
                chart.HierarchyRoots.Add(root);
            }

            materialized.Model = chart;
            return materialized;
        }

        var categoryBuckets = BuildChartCategoryBuckets(
            item,
            chartScopeRows,
            resolvedParameters,
            globals,
            runtime,
            context,
            path,
            diagnostics);
        if (categoryBuckets.Count == 0)
        {
            materialized.Model = chart;
            return materialized;
        }

        for (var seriesIndex = 0; seriesIndex < item.Series.Count; seriesIndex++)
        {
            var seriesDefinition = item.Series[seriesIndex];
            var seriesPath = $"{path}.series[{seriesIndex}]";
            var representativeContext = categoryBuckets[0].Context;
            var seriesName = EvaluateStringExpression(seriesDefinition.NameExpression, representativeContext, seriesPath + ".nameExpression", diagnostics);
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                seriesName = $"Series {seriesIndex + 1}";
            }

            var chartSeries = new ChartSeries
            {
                Name = seriesName,
                Style = BuildChartSeriesStyle(item.Type, seriesDefinition, representativeContext, seriesPath, diagnostics),
                DataLabels = CloneChartDataLabels(seriesDefinition.DataLabels),
                UseSmoothedLine = seriesDefinition.UseSmoothedLine
            };

            for (var categoryIndex = 0; categoryIndex < categoryBuckets.Count; categoryIndex++)
            {
                var categoryBucket = categoryBuckets[categoryIndex];
                var rawValue = EvaluateExpression(seriesDefinition.ValueExpression, categoryBucket.Context, seriesPath + ".valueExpression", diagnostics);
                if (!TryConvertToDouble(rawValue, runtime.Culture, out var numericValue))
                {
                    continue;
                }

                var point = new ChartPoint
                {
                    Category = categoryBucket.Label,
                    Value = numericValue
                };

                if (item.Type != ChartType.Line
                    && item.Type != ChartType.Scatter
                    && item.Type != ChartType.Radar
                    && item.Type != ChartType.Area)
                {
                    var colorText = EvaluateStringExpression(
                        seriesDefinition.ColorExpression,
                        categoryBucket.Context.CreateWithSelfValue(numericValue),
                        seriesPath + ".colorExpression",
                        diagnostics);
                    if (TryParseColor(colorText, out var color))
                    {
                        point.Style = CreateChartColorStyle(item.Type, color);
                    }
                }

                chartSeries.Points.Add(point);
            }

            chart.Series.Add(chartSeries);
        }

        materialized.Model = chart;
        return materialized;
    }

    private List<ChartHierarchyNode> BuildShapeChartHierarchy(
        ChartItem item,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sourceRows,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext fallbackContext,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var categoryLevels = item.CategoryLevels;
        if (categoryLevels.Count == 0)
        {
            var fallbackLevel = new ReportChartCategoryLevelDefinition
            {
                GroupExpression = item.CategoryExpression,
                LabelExpression = item.CategoryLabelExpression,
                SortExpression = item.CategorySortExpression,
                SortDirection = item.CategorySortDirection
            };

            if (!string.IsNullOrWhiteSpace(fallbackLevel.GroupExpression) || !string.IsNullOrWhiteSpace(fallbackLevel.LabelExpression))
            {
                categoryLevels = new List<ReportChartCategoryLevelDefinition> { fallbackLevel };
            }
        }

        if (categoryLevels.Count == 0 || item.Series.Count == 0 || sourceRows.Count == 0)
        {
            return new List<ChartHierarchyNode>();
        }

        var valueSeries = item.Series[0];
        return BuildChartHierarchyLevelNodes(
            item,
            valueSeries,
            categoryLevels,
            0,
            sourceRows,
            resolvedParameters,
            globals,
            runtime,
            fallbackContext,
            path,
            diagnostics);
    }

    private List<ChartHierarchyNode> BuildChartHierarchyLevelNodes(
        ChartItem item,
        ReportChartSeriesDefinition seriesDefinition,
        IReadOnlyList<ReportChartCategoryLevelDefinition> categoryLevels,
        int levelIndex,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sourceRows,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext fallbackContext,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var nodes = new List<ChartHierarchyNode>();
        if (levelIndex >= categoryLevels.Count || sourceRows.Count == 0)
        {
            return nodes;
        }

        var level = categoryLevels[levelIndex];
        var scopeRows = sourceRows;
        var buckets = new List<ChartHierarchyBucket>();
        var bucketsByKey = new Dictionary<string, ChartHierarchyBucket>(StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            var row = sourceRows[rowIndex];
            var rowContext = CreateReportContext(
                resolvedParameters,
                row,
                scopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                item.DataSetId,
                rowIndex,
                fallbackContext.NamedScopes);

            var groupExpression = !string.IsNullOrWhiteSpace(level.GroupExpression)
                ? level.GroupExpression
                : level.LabelExpression;
            var groupValue = EvaluateExpression(groupExpression, rowContext, $"{path}.categoryLevels[{levelIndex}].groupExpression", diagnostics);
            var bucketKey = CreateChartCategoryKey(groupValue, rowIndex, runtime.Culture);
            if (!bucketsByKey.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new ChartHierarchyBucket(bucketKey, row, new List<IReadOnlyDictionary<string, object?>>(), string.Empty, null, rowIndex);
                bucketsByKey[bucketKey] = bucket;
                buckets.Add(bucket);
            }

            bucket.Rows.Add(row);
            if (string.IsNullOrWhiteSpace(bucket.Label))
            {
                bucket.Label = EvaluateStringExpression(
                    !string.IsNullOrWhiteSpace(level.LabelExpression) ? level.LabelExpression : level.GroupExpression,
                    rowContext,
                    $"{path}.categoryLevels[{levelIndex}].labelExpression",
                    diagnostics)
                    ?? Convert.ToString(groupValue, runtime.Culture)
                    ?? (rowIndex + 1).ToString(runtime.Culture);
            }

            if (bucket.SortValue is null && !string.IsNullOrWhiteSpace(level.SortExpression))
            {
                bucket.SortValue = EvaluateExpression(level.SortExpression, rowContext, $"{path}.categoryLevels[{levelIndex}].sortExpression", diagnostics);
            }
        }

        if (!string.IsNullOrWhiteSpace(level.SortExpression))
        {
            buckets.Sort((left, right) =>
            {
                var comparison = CompareChartSortValues(left.SortValue, right.SortValue, runtime.Culture);
                return level.SortDirection == ReportSortDirection.Descending ? -comparison : comparison;
            });
        }

        for (var bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
        {
            var bucket = buckets[bucketIndex];
            var groupContext = CreateReportContext(
                resolvedParameters,
                bucket.RepresentativeFields,
                bucket.Rows,
                globals,
                runtime,
                ReportExpressionScopeKind.Group,
                item.DataSetId,
                bucketIndex,
                fallbackContext.NamedScopes);

            var node = new ChartHierarchyNode
            {
                Key = bucket.Key,
                Label = bucket.Label,
                DataLabel = CloneChartDataLabels(seriesDefinition.DataLabels)
            };

            var rawValue = EvaluateExpression(seriesDefinition.ValueExpression, groupContext, $"{path}.series[0].valueExpression", diagnostics);
            if (TryConvertToDouble(rawValue, runtime.Culture, out var numericValue))
            {
                node.Value = numericValue;
            }

            var colorText = EvaluateStringExpression(
                seriesDefinition.ColorExpression,
                groupContext.CreateWithSelfValue(node.Value),
                $"{path}.series[0].colorExpression",
                diagnostics);
            if (TryParseColor(colorText, out var color))
            {
                node.Style = CreateChartColorStyle(item.Type, color);
            }

            if (levelIndex + 1 < categoryLevels.Count)
            {
                var children = BuildChartHierarchyLevelNodes(
                    item,
                    seriesDefinition,
                    categoryLevels,
                    levelIndex + 1,
                    bucket.Rows,
                    resolvedParameters,
                    globals,
                    runtime,
                    fallbackContext,
                    path,
                    diagnostics);
                foreach (var child in children)
                {
                    node.Children.Add(child);
                }

                if (node.Children.Count > 0)
                {
                    var sum = 0d;
                    for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
                    {
                        sum += node.Children[childIndex].Value;
                    }

                    if (node.Value <= 0d)
                    {
                        node.Value = sum;
                    }
                }
            }

            nodes.Add(node);
        }

        return nodes;
    }

    private List<ChartCategoryBucket> BuildChartCategoryBuckets(
        ChartItem item,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sourceRows,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext fallbackContext,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var buckets = new List<ChartCategoryBucket>();
        if (sourceRows.Count == 0)
        {
            return buckets;
        }

        var scopeRows = sourceRows;
        var bucketsByKey = new Dictionary<string, ChartCategoryBucket>(StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            var row = sourceRows[rowIndex];
            var rowContext = CreateReportContext(
                resolvedParameters,
                row,
                scopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                item.DataSetId,
                rowIndex,
                fallbackContext.NamedScopes);

            var categoryValue = EvaluateExpression(
                !string.IsNullOrWhiteSpace(item.CategoryExpression) ? item.CategoryExpression : item.CategoryLabelExpression,
                rowContext,
                path + ".categoryExpression",
                diagnostics);
            var bucketKey = CreateChartCategoryKey(categoryValue, rowIndex, runtime.Culture);
            if (!bucketsByKey.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new ChartCategoryBucket(
                    bucketKey,
                    row,
                    new List<IReadOnlyDictionary<string, object?>>(),
                    string.Empty,
                    null,
                    rowIndex);
                bucketsByKey[bucketKey] = bucket;
                buckets.Add(bucket);
            }

            bucket.Rows.Add(row);
            if (string.IsNullOrWhiteSpace(bucket.Label))
            {
                bucket.Label = EvaluateStringExpression(
                    !string.IsNullOrWhiteSpace(item.CategoryLabelExpression) ? item.CategoryLabelExpression : item.CategoryExpression,
                    rowContext,
                    path + ".categoryLabelExpression",
                    diagnostics)
                    ?? Convert.ToString(categoryValue, runtime.Culture)
                    ?? (rowIndex + 1).ToString(runtime.Culture);
            }

            if (bucket.SortValue is null && !string.IsNullOrWhiteSpace(item.CategorySortExpression))
            {
                bucket.SortValue = EvaluateExpression(item.CategorySortExpression, rowContext, path + ".categorySortExpression", diagnostics);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.CategorySortExpression))
        {
            buckets.Sort((left, right) =>
            {
                var comparison = CompareChartSortValues(left.SortValue, right.SortValue, runtime.Culture);
                return item.CategorySortDirection == ReportSortDirection.Descending ? -comparison : comparison;
            });
        }

        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index];
            bucket.Context = CreateReportContext(
                resolvedParameters,
                bucket.RepresentativeFields,
                bucket.Rows,
                globals,
                runtime,
                ReportExpressionScopeKind.Group,
                item.DataSetId,
                index,
                fallbackContext.NamedScopes);
        }

        return buckets;
    }

    private static string CreateChartCategoryKey(object? categoryValue, int rowIndex, CultureInfo culture)
    {
        if (categoryValue is null)
        {
            return $"__row_{rowIndex}";
        }

        return categoryValue switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(categoryValue, culture) ?? $"__row_{rowIndex}"
        };
    }

    private static int CompareChartSortValues(object? left, object? right, CultureInfo culture)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryConvertToDouble(left, culture, out var leftNumber)
            && TryConvertToDouble(right, culture, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is DateTimeOffset leftOffset && right is DateTimeOffset rightOffset)
        {
            return leftOffset.CompareTo(rightOffset);
        }

        if (left is DateTime leftDate && right is DateTime rightDate)
        {
            return leftDate.CompareTo(rightDate);
        }

        return culture.CompareInfo.Compare(
            Convert.ToString(left, culture) ?? string.Empty,
            Convert.ToString(right, culture) ?? string.Empty,
            CompareOptions.IgnoreCase);
    }

    private ChartStyle? BuildChartSeriesStyle(
        ChartType chartType,
        ReportChartSeriesDefinition definition,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var style = CloneChartStyle(definition.Style) ?? new ChartStyle();
        var colorExpression = definition.ColorExpression;
        if (!string.IsNullOrWhiteSpace(colorExpression))
        {
            var colorText = EvaluateStringExpression(colorExpression, context, path + ".colorExpression", diagnostics);
            if (TryParseColor(colorText, out var color))
            {
                var colorStyle = CreateChartColorStyle(chartType, color);
                style = MergeChartStyles(style, colorStyle);
            }
        }

        return style.Fill is null && style.Line is null && style.Effects is null ? null : style;
    }

    private static ChartStyle CreateChartColorStyle(ChartType chartType, Vibe.Office.Primitives.DocColor color)
    {
        if (chartType is ChartType.Line or ChartType.Scatter or ChartType.Radar or ChartType.Area)
        {
            return new ChartStyle
            {
                Line = new ChartLineStyle
                {
                    Color = color
                }
            };
        }

        return new ChartStyle
        {
            Fill = new ChartFillStyle
            {
                Color = color
            },
            Line = new ChartLineStyle
            {
                Color = color
            }
        };
    }

    private static ChartStyle MergeChartStyles(ChartStyle primary, ChartStyle secondary)
    {
        primary.Fill ??= secondary.Fill is null ? null : CloneChartFillStyle(secondary.Fill);
        primary.Line ??= secondary.Line is null ? null : CloneChartLineStyle(secondary.Line);

        if (secondary.Fill?.Color.HasValue == true)
        {
            primary.Fill ??= new ChartFillStyle();
            primary.Fill.Color = secondary.Fill.Color;
            primary.Fill.IsNone = secondary.Fill.IsNone;
        }

        if (secondary.Line is not null)
        {
            primary.Line ??= new ChartLineStyle();
            primary.Line.Color = secondary.Line.Color ?? primary.Line.Color;
            primary.Line.Width = secondary.Line.Width ?? primary.Line.Width;
            primary.Line.Style = secondary.Line.Style ?? primary.Line.Style;
            primary.Line.IsNone = secondary.Line.IsNone;
        }

        return primary;
    }

    private static ChartStyle? CloneChartStyle(ChartStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ChartStyle
        {
            Fill = CloneChartFillStyle(style.Fill),
            Line = CloneChartLineStyle(style.Line),
            Effects = style.Effects is null
                ? null
                : new ChartEffectStyle
                {
                    Shadow = style.Effects.Shadow is null
                        ? null
                        : new ChartShadowEffect
                        {
                            BlurRadius = style.Effects.Shadow.BlurRadius,
                            Distance = style.Effects.Shadow.Distance,
                            Direction = style.Effects.Shadow.Direction,
                            Color = style.Effects.Shadow.Color
                        }
                }
        };
    }

    private static ChartFillStyle? CloneChartFillStyle(ChartFillStyle? fill)
    {
        if (fill is null)
        {
            return null;
        }

        return new ChartFillStyle
        {
            IsNone = fill.IsNone,
            Color = fill.Color
        };
    }

    private static ChartLineStyle? CloneChartLineStyle(ChartLineStyle? line)
    {
        if (line is null)
        {
            return null;
        }

        return new ChartLineStyle
        {
            IsNone = line.IsNone,
            Color = line.Color,
            Width = line.Width,
            Style = line.Style
        };
    }

    private static ChartLegend? CloneChartLegend(ChartLegend? legend)
    {
        if (legend is null)
        {
            return null;
        }

        return new ChartLegend
        {
            IsVisible = legend.IsVisible,
            Position = legend.Position,
            Overlay = legend.Overlay,
            TextStyle = CloneChartTextStyle(legend.TextStyle)
        };
    }

    private static ChartDataLabelSettings? CloneChartDataLabels(ChartDataLabelSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        return new ChartDataLabelSettings
        {
            IsHidden = settings.IsHidden,
            ShowValue = settings.ShowValue,
            ShowCategoryName = settings.ShowCategoryName,
            ShowSeriesName = settings.ShowSeriesName,
            ShowPercent = settings.ShowPercent,
            ShowBubbleSize = settings.ShowBubbleSize,
            ShowLegendKey = settings.ShowLegendKey,
            ShowLeaderLines = settings.ShowLeaderLines,
            Position = settings.Position,
            NumberFormat = settings.NumberFormat,
            TextStyle = CloneChartTextStyle(settings.TextStyle),
            ShapeStyle = CloneChartStyle(settings.ShapeStyle)
        };
    }

    private static void CloneChartAxes(IReadOnlyList<ChartAxis> source, List<ChartAxis> target)
    {
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(CloneChartAxis(source[index]));
        }
    }

    private static ChartAxis CloneChartAxis(ChartAxis axis)
    {
        return new ChartAxis
        {
            AxisId = axis.AxisId,
            CrossAxisId = axis.CrossAxisId,
            Kind = axis.Kind,
            Position = axis.Position,
            IsVisible = axis.IsVisible,
            Minimum = axis.Minimum,
            Maximum = axis.Maximum,
            MajorUnit = axis.MajorUnit,
            MinorUnit = axis.MinorUnit,
            MajorTickMark = axis.MajorTickMark,
            MinorTickMark = axis.MinorTickMark,
            TickLabelPosition = axis.TickLabelPosition,
            NumberFormat = axis.NumberFormat,
            Title = axis.Title,
            LineStyle = CloneChartLineStyle(axis.LineStyle),
            MajorGridlineStyle = CloneChartLineStyle(axis.MajorGridlineStyle),
            MinorGridlineStyle = CloneChartLineStyle(axis.MinorGridlineStyle),
            LabelTextStyle = CloneChartTextStyle(axis.LabelTextStyle),
            TitleTextStyle = CloneChartTextStyle(axis.TitleTextStyle)
        };
    }

    private static ChartTextStyle? CloneChartTextStyle(ChartTextStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ChartTextStyle
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Color = style.Color,
            Bold = style.Bold,
            Italic = style.Italic
        };
    }

    private async ValueTask<MaterializedContainerReportItem> MaterializeContainerItemAsync(
        ContainerItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
        MaterializedReportDrillthroughAction? drillthrough,
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
        var materialized = CreateBaseItem(
            new MaterializedContainerReportItem(),
            item,
            style,
            bookmark,
            tooltip,
            pageBreak,
            drillthrough);

        await MaterializeItemsAsync(
            item.Items,
            materialized.Items,
            reportDefinition,
            resolvedParameters,
            materializedDataSets,
            globals,
            styleResolver,
            context,
            path + ".items",
            runtime,
            activeReports,
            diagnostics,
            cancellationToken);

        for (var index = 0; index < materialized.Items.Count; index++)
        {
            OffsetMaterializedItem(materialized.Items[index], item.Bounds.X, item.Bounds.Y);
        }

        return materialized;
    }

    private MaterializedGaugeReportItem MaterializeGaugeItem(
        GaugeItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
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
            new MaterializedGaugeReportItem
            {
                GaugeKind = item.GaugeKind
            },
            item,
            style,
            bookmark,
            tooltip,
            pageBreak,
            drillthrough);

        var evaluationContext = context;
        var dataSet = FindDataSet(materializedDataSets, item.DataSetId);
        if (dataSet is not null)
        {
            var gaugeScopeRows = ResolveScopedDataRows(dataSet.Rows, context.ScopeRows);
            evaluationContext = CreateReportContext(
                resolvedParameters,
                gaugeScopeRows.Count > 0 ? gaugeScopeRows[0] : EmptyFields,
                gaugeScopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Group,
                item.DataSetId,
                namedScopes: context.NamedScopes);
        }

        materialized.Label = EvaluateStringExpression(item.LabelExpression, evaluationContext, path + ".labelExpression", diagnostics);
        materialized.Value = EvaluateNumericExpression(item.ValueExpression, evaluationContext, path + ".valueExpression", diagnostics);
        materialized.Minimum = EvaluateNumericExpression(item.MinimumExpression, evaluationContext, path + ".minimumExpression", diagnostics);
        materialized.Maximum = EvaluateNumericExpression(item.MaximumExpression, evaluationContext, path + ".maximumExpression", diagnostics);
        materialized.TargetValue = EvaluateNumericExpression(item.TargetValueExpression, evaluationContext, path + ".targetValueExpression", diagnostics);
        return materialized;
    }

    private MaterializedTablixReportItem MaterializeTablixItem(
        TablixItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
        MaterializedReportDrillthroughAction? drillthrough,
        ReportDefinition reportDefinition,
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
            pageBreak,
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
        dataRows = ApplyTablixFilters(
            item,
            dataRows,
            resolvedParameters,
            globals,
            runtime,
            context,
            path,
            diagnostics);
        var scopeRows = ToScopeRows(dataRows);

        if (item.RowMembers.Count > 0)
        {
            var tablixContext = dataRows.Count == 0
                ? context
                : CreateReportContext(
                    resolvedParameters,
                    dataRows[0].Values,
                    scopeRows,
                    globals,
                    runtime,
                    ReportExpressionScopeKind.Group,
                    item.DataSetId,
                    namedScopes: context.NamedScopes);
            MaterializeTablixMembers(
                item.RowMembers,
                item,
                materialized.Rows,
                dataRows,
                tablixContext,
                reportDefinition,
                resolvedParameters,
                materializedDataSets,
                globals,
                runtime,
                styleResolver,
                path + ".rowMembers",
                diagnostics);
            return materialized;
        }

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
                    reportDefinition,
                    resolvedParameters,
                    materializedDataSets,
                    globals,
                    runtime,
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
                    reportDefinition,
                    resolvedParameters,
                    materializedDataSets,
                    globals,
                    runtime,
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
                    detailIndex,
                    context.NamedScopes);
                materialized.Rows.Add(MaterializeTablixRow(
                    rowDefinition,
                    rowContext,
                    path + $".rows[{rowIndex}]",
                    styleResolver,
                    reportDefinition,
                    resolvedParameters,
                    materializedDataSets,
                    globals,
                    runtime,
                    diagnostics));
            }
        }

        return materialized;
    }

    private IReadOnlyList<MaterializedDataRow> ApplyTablixFilters(
        TablixItem item,
        IReadOnlyList<MaterializedDataRow> dataRows,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (item.Filters.Count == 0 || dataRows.Count == 0)
        {
            return dataRows;
        }

        var compiledFilters = new List<(ICompiledReportExpression Left, ICompiledReportExpression Right, ReportFilterOperator Operator)>();
        for (var filterIndex = 0; filterIndex < item.Filters.Count; filterIndex++)
        {
            var filter = item.Filters[filterIndex];
            var leftCompilation = _expressionCompiler.Compile(filter.Expression);
            AppendExpressionDiagnostics(leftCompilation.Diagnostics, $"{path}.filters[{filterIndex}].expression", diagnostics);
            var rightCompilation = _expressionCompiler.Compile(filter.ValueExpression);
            AppendExpressionDiagnostics(rightCompilation.Diagnostics, $"{path}.filters[{filterIndex}].valueExpression", diagnostics);
            if (leftCompilation.Expression is null || leftCompilation.HasErrors
                || rightCompilation.Expression is null || rightCompilation.HasErrors)
            {
                continue;
            }

            compiledFilters.Add((leftCompilation.Expression, rightCompilation.Expression, filter.Operator));
        }

        if (compiledFilters.Count == 0)
        {
            return dataRows;
        }

        var scopeRows = ToScopeRows(dataRows);
        var filteredRows = new List<MaterializedDataRow>(dataRows.Count);
        for (var rowIndex = 0; rowIndex < dataRows.Count; rowIndex++)
        {
            var row = dataRows[rowIndex];
            var rowContext = CreateReportContext(
                resolvedParameters,
                row.Values,
                scopeRows,
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                item.DataSetId,
                rowIndex,
                context.NamedScopes);

            var includeRow = true;
            for (var filterIndex = 0; filterIndex < compiledFilters.Count; filterIndex++)
            {
                var filter = compiledFilters[filterIndex];
                if (!filter.Left.TryEvaluate(rowContext, out var left, out var leftDiagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(leftDiagnostic!, $"{path}.filters[{filterIndex}]"));
                    includeRow = false;
                    break;
                }

                if (!filter.Right.TryEvaluate(rowContext, out var right, out var rightDiagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(rightDiagnostic!, $"{path}.filters[{filterIndex}]"));
                    includeRow = false;
                    break;
                }

                if (!EvaluateTablixFilter(left, filter.Operator, right, runtime.Culture))
                {
                    includeRow = false;
                    break;
                }
            }

            if (includeRow)
            {
                filteredRows.Add(row);
            }
        }

        return filteredRows;
    }

    private static bool EvaluateTablixFilter(
        object? left,
        ReportFilterOperator filterOperator,
        object? right,
        CultureInfo culture)
    {
        return filterOperator switch
        {
            ReportFilterOperator.Equal => CompareTablixValues(left, right, culture) == 0,
            ReportFilterOperator.NotEqual => CompareTablixValues(left, right, culture) != 0,
            ReportFilterOperator.GreaterThan => CompareTablixValues(left, right, culture) > 0,
            ReportFilterOperator.GreaterThanOrEqual => CompareTablixValues(left, right, culture) >= 0,
            ReportFilterOperator.LessThan => CompareTablixValues(left, right, culture) < 0,
            ReportFilterOperator.LessThanOrEqual => CompareTablixValues(left, right, culture) <= 0,
            ReportFilterOperator.Contains => culture.CompareInfo.IndexOf(
                Convert.ToString(left, culture) ?? string.Empty,
                Convert.ToString(right, culture) ?? string.Empty,
                CompareOptions.IgnoreCase) >= 0,
            _ => false
        };
    }

    private void MaterializeTablixMembers(
        IReadOnlyList<ReportTablixMemberDefinition> members,
        TablixItem tablix,
        List<MaterializedTablixRow> targetRows,
        IReadOnlyList<MaterializedDataRow> currentRows,
        ReportExpressionContext currentContext,
        ReportDefinition? reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportMaterializationStyleResolver styleResolver,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            var member = members[memberIndex];
            MaterializeTablixMember(
                member,
                tablix,
                targetRows,
                currentRows,
                currentContext,
                reportDefinition,
                resolvedParameters,
                materializedDataSets,
                globals,
                runtime,
                styleResolver,
                $"{path}[{memberIndex}]",
                diagnostics);
        }
    }

    private void MaterializeTablixMember(
        ReportTablixMemberDefinition member,
        TablixItem tablix,
        List<MaterializedTablixRow> targetRows,
        IReadOnlyList<MaterializedDataRow> currentRows,
        ReportExpressionContext currentContext,
        ReportDefinition? reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportMaterializationStyleResolver styleResolver,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        switch (member.Kind)
        {
            case ReportTablixMemberKind.Static:
            {
                var staticContext = CreateTablixScopeContext(
                    currentRows,
                    currentContext,
                    resolvedParameters,
                    globals,
                    runtime,
                    member.GroupName ?? tablix.DataSetId,
                    ReportExpressionScopeKind.Group);
                if (!EvaluateVisibility(member.VisibilityExpression, staticContext, path, diagnostics))
                {
                    return;
                }

                if (member.Members.Count == 0)
                {
                    var rowStartIndex = targetRows.Count;
                    AddMaterializedTablixRow(
                        member,
                        tablix,
                        targetRows,
                        staticContext,
                        reportDefinition,
                        resolvedParameters,
                        materializedDataSets,
                        globals,
                        runtime,
                        styleResolver,
                        path,
                        diagnostics);
                    ApplyTablixMemberPageBreak(member, 0, rowStartIndex, targetRows.Count, targetRows);
                    return;
                }

                var memberStartIndex = targetRows.Count;
                MaterializeTablixMembers(
                    member.Members,
                    tablix,
                    targetRows,
                    currentRows,
                    staticContext,
                    reportDefinition,
                    resolvedParameters,
                    materializedDataSets,
                    globals,
                    runtime,
                    styleResolver,
                    path + ".members",
                    diagnostics);
                ApplyTablixMemberPageBreak(member, 0, memberStartIndex, targetRows.Count, targetRows);
                return;
            }

            case ReportTablixMemberKind.Details:
            {
                if (currentRows.Count == 0)
                {
                    if (!EvaluateVisibility(member.VisibilityExpression, currentContext, path, diagnostics))
                    {
                        return;
                    }

                    if (member.Members.Count == 0)
                    {
                        var rowStartIndex = targetRows.Count;
                        AddMaterializedTablixRow(
                            member,
                            tablix,
                            targetRows,
                            currentContext,
                            reportDefinition,
                            resolvedParameters,
                            materializedDataSets,
                            globals,
                            runtime,
                            styleResolver,
                            path,
                            diagnostics);
                        ApplyTablixMemberPageBreak(member, 0, rowStartIndex, targetRows.Count, targetRows);
                        return;
                    }

                    var memberStartIndex = targetRows.Count;
                    MaterializeTablixMembers(
                        member.Members,
                        tablix,
                        targetRows,
                        currentRows,
                        currentContext,
                        reportDefinition,
                        resolvedParameters,
                        materializedDataSets,
                        globals,
                        runtime,
                        styleResolver,
                        path + ".members",
                        diagnostics);
                    ApplyTablixMemberPageBreak(member, 0, memberStartIndex, targetRows.Count, targetRows);
                    return;
                }

                var sortedRows = SortTablixRows(
                    currentRows,
                    member.SortExpression,
                    member.SortDirection,
                    tablix.DataSetId,
                    currentContext,
                    resolvedParameters,
                    globals,
                    runtime,
                    path + ".sortExpression",
                    diagnostics);
                for (var rowIndex = 0; rowIndex < sortedRows.Count; rowIndex++)
                {
                    var row = sortedRows[rowIndex];
                    var detailContext = CreateReportContext(
                        resolvedParameters,
                        row.Values,
                        ToScopeRows(sortedRows),
                        globals,
                        runtime,
                        ReportExpressionScopeKind.Row,
                        tablix.DataSetId,
                        rowIndex,
                        currentContext.NamedScopes);
                    if (!EvaluateVisibility(member.VisibilityExpression, detailContext, path, diagnostics))
                    {
                        continue;
                    }

                    if (member.Members.Count == 0)
                    {
                        var rowStartIndex = targetRows.Count;
                        AddMaterializedTablixRow(
                            member,
                            tablix,
                            targetRows,
                            detailContext,
                            reportDefinition,
                            resolvedParameters,
                            materializedDataSets,
                            globals,
                            runtime,
                            styleResolver,
                            path + $".detail[{rowIndex}]",
                            diagnostics);
                        ApplyTablixMemberPageBreak(member, rowIndex, rowStartIndex, targetRows.Count, targetRows);
                        continue;
                    }

                    var memberStartIndex = targetRows.Count;
                    MaterializeTablixMembers(
                        member.Members,
                        tablix,
                        targetRows,
                        [row],
                        detailContext,
                        reportDefinition,
                        resolvedParameters,
                        materializedDataSets,
                        globals,
                        runtime,
                        styleResolver,
                        path + $".detail[{rowIndex}].members",
                        diagnostics);
                    ApplyTablixMemberPageBreak(member, rowIndex, memberStartIndex, targetRows.Count, targetRows);
                }

                return;
            }

            case ReportTablixMemberKind.Group:
            {
                var groups = GroupTablixRows(
                    member,
                    currentRows,
                    tablix.DataSetId,
                    currentContext,
                    resolvedParameters,
                    globals,
                    runtime,
                    path,
                    diagnostics);
                if (groups.Count == 0 && currentRows.Count == 0)
                {
                    if (member.Members.Count == 0 && EvaluateVisibility(member.VisibilityExpression, currentContext, path, diagnostics))
                    {
                        AddMaterializedTablixRow(
                            member,
                            tablix,
                            targetRows,
                            currentContext,
                            reportDefinition,
                            resolvedParameters,
                            materializedDataSets,
                            globals,
                            runtime,
                            styleResolver,
                            path,
                            diagnostics);
                    }

                    return;
                }

                for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    if (!EvaluateVisibility(member.VisibilityExpression, group.Context, path + $".group[{groupIndex}]", diagnostics))
                    {
                        continue;
                    }

                    if (member.Members.Count == 0)
                    {
                        var rowStartIndex = targetRows.Count;
                        AddMaterializedTablixRow(
                            member,
                            tablix,
                            targetRows,
                            group.Context,
                            reportDefinition,
                            resolvedParameters,
                            materializedDataSets,
                            globals,
                            runtime,
                            styleResolver,
                            path + $".group[{groupIndex}]",
                            diagnostics);
                        ApplyTablixMemberPageBreak(member, groupIndex, rowStartIndex, targetRows.Count, targetRows);
                        continue;
                    }

                    var memberStartIndex = targetRows.Count;
                    MaterializeTablixMembers(
                        member.Members,
                        tablix,
                        targetRows,
                        group.Rows,
                        group.Context,
                        reportDefinition,
                        resolvedParameters,
                        materializedDataSets,
                        globals,
                        runtime,
                        styleResolver,
                        path + $".group[{groupIndex}].members",
                        diagnostics);
                    ApplyTablixMemberPageBreak(member, groupIndex, memberStartIndex, targetRows.Count, targetRows);
                }

                return;
            }
        }
    }

    private void AddMaterializedTablixRow(
        ReportTablixMemberDefinition member,
        TablixItem tablix,
        List<MaterializedTablixRow> targetRows,
        ReportExpressionContext context,
        ReportDefinition? reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportMaterializationStyleResolver styleResolver,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (!member.RowDefinitionIndex.HasValue
            || member.RowDefinitionIndex.Value < 0
            || member.RowDefinitionIndex.Value >= tablix.Rows.Count)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.InvalidTemplate,
                $"Tablix member '{member.Id}' does not map to a valid body row definition.",
                path));
            return;
        }

        targetRows.Add(MaterializeTablixRow(
            tablix.Rows[member.RowDefinitionIndex.Value],
            context,
            path + $".row[{member.RowDefinitionIndex.Value}]",
            styleResolver,
            reportDefinition,
            resolvedParameters,
            materializedDataSets,
            globals,
            runtime,
            diagnostics));
    }

    private static void ApplyTablixMemberPageBreak(
        ReportTablixMemberDefinition member,
        int instanceIndex,
        int rowStartIndex,
        int rowEndIndex,
        List<MaterializedTablixRow> targetRows)
    {
        if (member.PageBreak is null || rowStartIndex < 0 || rowEndIndex <= rowStartIndex || rowEndIndex > targetRows.Count)
        {
            return;
        }

        if (ShouldInsertPageBreakBefore(member.PageBreak.Location, instanceIndex))
        {
            targetRows[rowStartIndex].PageBreakBefore = true;
        }

        if (ShouldInsertPageBreakAfter(member.PageBreak.Location))
        {
            targetRows[rowEndIndex - 1].PageBreakAfter = true;
        }
    }

    private static bool ShouldInsertPageBreakBefore(ReportPageBreakLocation location, int instanceIndex)
    {
        return location switch
        {
            ReportPageBreakLocation.Start => true,
            ReportPageBreakLocation.StartAndEnd => true,
            ReportPageBreakLocation.Between => instanceIndex > 0,
            _ => false
        };
    }

    private static bool ShouldInsertPageBreakAfter(ReportPageBreakLocation location)
    {
        return location is ReportPageBreakLocation.End or ReportPageBreakLocation.StartAndEnd;
    }

    private List<TablixGroupInstance> GroupTablixRows(
        ReportTablixMemberDefinition member,
        IReadOnlyList<MaterializedDataRow> rows,
        string? dataSetId,
        ReportExpressionContext fallbackContext,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var groups = new List<TablixGroupInstance>();
        if (rows.Count == 0)
        {
            return groups;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowContext = CreateReportContext(
                resolvedParameters,
                row.Values,
                ToScopeRows(rows),
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                dataSetId,
                rowIndex,
                fallbackContext.NamedScopes);
            var key = string.IsNullOrWhiteSpace(member.GroupExpression)
                ? rowIndex
                : EvaluateExpression(member.GroupExpression, rowContext, path + ".groupExpression", diagnostics);
            var existingIndex = FindTablixGroupIndex(groups, key);
            if (existingIndex >= 0)
            {
                groups[existingIndex].Rows.Add(row);
                continue;
            }

            var groupRows = new List<MaterializedDataRow> { row };
            groups.Add(new TablixGroupInstance(
                key,
                groupRows,
                CreateTablixScopeContext(
                    groupRows,
                    fallbackContext,
                    resolvedParameters,
                    globals,
                    runtime,
                    member.GroupName ?? dataSetId,
                    ReportExpressionScopeKind.Group)));
        }

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            groups[groupIndex] = groups[groupIndex] with
            {
                Context = CreateTablixScopeContext(
                    groups[groupIndex].Rows,
                    fallbackContext,
                    resolvedParameters,
                    globals,
                    runtime,
                    member.GroupName ?? dataSetId,
                    ReportExpressionScopeKind.Group)
            };
        }

        if (!string.IsNullOrWhiteSpace(member.SortExpression))
        {
            groups.Sort((left, right) =>
            {
                var leftValue = EvaluateExpression(member.SortExpression, left.Context, path + ".sortExpression", diagnostics);
                var rightValue = EvaluateExpression(member.SortExpression, right.Context, path + ".sortExpression", diagnostics);
                var comparison = CompareTablixValues(leftValue, rightValue, left.Context.Culture);
                return member.SortDirection == ReportSortDirection.Descending
                    ? -comparison
                    : comparison;
            });
        }

        return groups;
    }

    private List<MaterializedDataRow> SortTablixRows(
        IReadOnlyList<MaterializedDataRow> rows,
        string? sortExpression,
        ReportSortDirection sortDirection,
        string? dataSetId,
        ReportExpressionContext fallbackContext,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        var result = rows.ToList();
        if (result.Count <= 1 || string.IsNullOrWhiteSpace(sortExpression))
        {
            return result;
        }

        result.Sort((left, right) =>
        {
            var leftContext = CreateReportContext(
                resolvedParameters,
                left.Values,
                ToScopeRows(rows),
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                dataSetId,
                namedScopes: fallbackContext.NamedScopes);
            var rightContext = CreateReportContext(
                resolvedParameters,
                right.Values,
                ToScopeRows(rows),
                globals,
                runtime,
                ReportExpressionScopeKind.Row,
                dataSetId,
                namedScopes: fallbackContext.NamedScopes);
            var leftValue = EvaluateExpression(sortExpression, leftContext, path, diagnostics);
            var rightValue = EvaluateExpression(sortExpression, rightContext, path, diagnostics);
            var comparison = CompareTablixValues(leftValue, rightValue, leftContext.Culture);
            return sortDirection == ReportSortDirection.Descending
                ? -comparison
                : comparison;
        });

        return result;
    }

    private static ReportExpressionContext CreateTablixScopeContext(
        IReadOnlyList<MaterializedDataRow> rows,
        ReportExpressionContext fallbackContext,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        string? scopeName,
        ReportExpressionScopeKind scopeKind)
    {
        if (rows.Count == 0)
        {
            return fallbackContext;
        }

        var scopeRows = ToScopeRows(rows);
        var context = CreateReportContext(
            resolvedParameters,
            rows[0].Values,
            scopeRows,
            globals,
            runtime,
            scopeKind,
            scopeName,
            namedScopes: AppendNamedScope(fallbackContext.NamedScopes, scopeName, scopeRows));
        context.RowIndex = fallbackContext.RowIndex;
        context.SelfValue = fallbackContext.SelfValue;
        context.PageNumber = fallbackContext.PageNumber;
        context.TotalPages = fallbackContext.TotalPages;
        return context;
    }

    private static int FindTablixGroupIndex(List<TablixGroupInstance> groups, object? key)
    {
        for (var index = 0; index < groups.Count; index++)
        {
            if (Equals(groups[index].Key, key))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CompareTablixValues(object? left, object? right, CultureInfo culture)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryConvertToDouble(left, culture, out var leftNumber)
            && TryConvertToDouble(right, culture, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is DateTime leftDate
            && TryConvertToDateTime(right, culture, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        if (left is IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(right);
            }
            catch
            {
            }
        }

        var leftText = Convert.ToString(left, culture) ?? string.Empty;
        var rightText = Convert.ToString(right, culture) ?? string.Empty;
        return string.Compare(leftText, rightText, StringComparison.CurrentCulture);
    }

    private MaterializedTablixRow MaterializeTablixRow(
        ReportTablixRowDefinition rowDefinition,
        ReportExpressionContext context,
        string path,
        ReportMaterializationStyleResolver styleResolver,
        ReportDefinition? reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        List<ReportDiagnostic> diagnostics)
    {
        var row = new MaterializedTablixRow
        {
            IsHeader = rowDefinition.IsHeader,
            Height = rowDefinition.Height
        };
        var consumeContainerWhitespace = reportDefinition?.ConsumeContainerWhitespace ?? false;

        for (var cellIndex = 0; cellIndex < rowDefinition.Cells.Count; cellIndex++)
        {
            var cellDefinition = rowDefinition.Cells[cellIndex];
            var rawValue = cellDefinition.ValueExpression is null
                ? cellDefinition.Text
                : EvaluateExpression(cellDefinition.ValueExpression, context, $"{path}.cells[{cellIndex}].valueExpression", diagnostics);
            var value = cellDefinition.ValueExpression is null
                ? cellDefinition.Text
                : FormatValue(rawValue, cellDefinition.FormatString, context.Culture);
            var styleContext = context.CreateWithSelfValue(rawValue);

            row.Cells.Add(new MaterializedTablixCell
            {
                Text = value ?? string.Empty,
                StyleName = cellDefinition.StyleName,
                Style = styleResolver.Resolve(cellDefinition.StyleName, styleContext, $"{path}.cells[{cellIndex}].styleName", diagnostics),
                Content = cellDefinition.ContentItem is null
                    ? null
                    : MaterializeEmbeddedTablixCellContent(
                        cellDefinition.ContentItem,
                        reportDefinition,
                        resolvedParameters,
                        materializedDataSets,
                        globals,
                        styleResolver,
                        context,
                        $"{path}.cells[{cellIndex}].contentItem",
                        runtime,
                        diagnostics),
                RowSpan = Math.Max(1, cellDefinition.RowSpan),
                ColumnSpan = Math.Max(1, cellDefinition.ColumnSpan)
            });
        }

        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
        {
            var content = row.Cells[cellIndex].Content;
            if (content is null)
            {
                continue;
            }

            var requiredHeight = EstimatePreferredItemBottom(content, consumeContainerWhitespace);
            if (requiredHeight > row.Height)
            {
                row.Height = requiredHeight;
            }
        }

        return row;
    }

    private static float EstimatePreferredItemBottom(
        MaterializedReportItem item,
        bool consumeContainerWhitespace)
    {
        return item.Bounds.Y + EstimatePreferredItemHeight(item, consumeContainerWhitespace);
    }

    private static float EstimatePreferredItemHeight(
        MaterializedReportItem item,
        bool consumeContainerWhitespace)
    {
        return item switch
        {
            MaterializedTextReportItem textItem => EstimateTextHeight(textItem),
            MaterializedContainerReportItem containerItem => EstimateContainerHeight(containerItem, consumeContainerWhitespace),
            MaterializedTablixReportItem tablixItem => EstimateTablixHeight(tablixItem),
            MaterializedSubreportReportItem subreportItem => EstimateSubreportHeight(subreportItem),
            _ => item.Bounds.Height
        };
    }

    private static float EstimateTextHeight(MaterializedTextReportItem item)
    {
        if (!item.CanGrow && !item.CanShrink)
        {
            return item.Bounds.Height;
        }

        var paddingLeft = item.Style?.PaddingLeft ?? 0f;
        var paddingRight = item.Style?.PaddingRight ?? 0f;
        var paddingTop = item.Style?.PaddingTop ?? 0f;
        var paddingBottom = item.Style?.PaddingBottom ?? 0f;
        var availableWidth = Math.Max(1f, item.Bounds.Width - paddingLeft - paddingRight);
        var fontSize = Math.Max(8f, item.Style?.FontSize ?? 10f);
        var lineHeight = Math.Max(fontSize * 1.2f, fontSize + 1f);
        var lineCount = EstimateWrappedLineCount(GetEstimatedTextContent(item), availableWidth, fontSize);
        var desiredHeight = paddingTop + paddingBottom + (lineCount * lineHeight);

        if (item.CanGrow && desiredHeight > item.Bounds.Height)
        {
            return desiredHeight;
        }

        if (item.CanShrink && desiredHeight < item.Bounds.Height)
        {
            return Math.Max(lineHeight + paddingTop + paddingBottom, desiredHeight);
        }

        return item.Bounds.Height;
    }

    private static float EstimateContainerHeight(
        MaterializedContainerReportItem item,
        bool consumeContainerWhitespace)
    {
        if (item.Items.Count == 0)
        {
            return item.Bounds.Height;
        }

        var childLayouts = NormalizePreferredLayouts(item.Items, consumeContainerWhitespace);
        var originalBottom = 0f;
        for (var index = 0; index < item.Items.Count; index++)
        {
            var child = item.Items[index];
            var bottom = child.Bounds.Y - item.Bounds.Y + child.Bounds.Height;
            if (bottom > originalBottom)
            {
                originalBottom = bottom;
            }
        }

        var adjustedBottom = 0f;
        for (var index = 0; index < childLayouts.Count; index++)
        {
            var bottom = childLayouts[index].Bounds.Y + childLayouts[index].Bounds.Height;
            if (bottom > adjustedBottom)
            {
                adjustedBottom = bottom;
            }
        }

        if (adjustedBottom <= originalBottom)
        {
            return item.Bounds.Height;
        }

        if (consumeContainerWhitespace)
        {
            return Math.Max(item.Bounds.Height, adjustedBottom);
        }

        return item.Bounds.Height + (adjustedBottom - originalBottom);
    }

    private static float EstimateTablixHeight(MaterializedTablixReportItem item)
    {
        if (item.Rows.Count == 0)
        {
            return item.Bounds.Height;
        }

        var height = 0f;
        for (var index = 0; index < item.Rows.Count; index++)
        {
            height += Math.Max(1f, item.Rows[index].Height);
        }

        return Math.Max(item.Bounds.Height, height);
    }

    private static float EstimateSubreportHeight(MaterializedSubreportReportItem item)
    {
        if (item.Report is null || item.Report.Sections.Count == 0)
        {
            return item.Bounds.Height;
        }

        var section = item.Report.Sections[0];
        if (section.BodyItems.Count == 0)
        {
            return item.Bounds.Height;
        }

        var layouts = NormalizePreferredLayouts(section.BodyItems, item.Report.ConsumeContainerWhitespace);
        var maxBottom = 0f;
        for (var index = 0; index < layouts.Count; index++)
        {
            var bottom = layouts[index].Bounds.Y + layouts[index].Bounds.Height;
            if (bottom > maxBottom)
            {
                maxBottom = bottom;
            }
        }

        return Math.Max(item.Bounds.Height, maxBottom);
    }

    private static List<(MaterializedReportItem Item, ReportItemBounds Bounds)> NormalizePreferredLayouts(
        IReadOnlyList<MaterializedReportItem> items,
        bool consumeContainerWhitespace)
    {
        var layouts = new List<(MaterializedReportItem Item, ReportItemBounds Bounds)>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var bounds = item.Bounds with
            {
                Height = EstimatePreferredItemHeight(item, consumeContainerWhitespace)
            };
            bounds = ApplyGrowthReflow(layouts, item, bounds);
            layouts.Add((item, bounds));
        }

        return layouts;
    }

    private static ReportItemBounds ApplyGrowthReflow(
        IReadOnlyList<(MaterializedReportItem Item, ReportItemBounds Bounds)> existingLayouts,
        MaterializedReportItem currentItem,
        ReportItemBounds currentBounds)
    {
        if (existingLayouts.Count == 0)
        {
            return currentBounds;
        }

        var adjustedBounds = currentBounds;
        var currentOriginal = currentItem.Bounds;
        for (var index = 0; index < existingLayouts.Count; index++)
        {
            var previousLayout = existingLayouts[index];
            var previousOriginal = previousLayout.Item.Bounds;
            if (currentOriginal.Y + 0.01f < previousOriginal.Y)
            {
                continue;
            }

            var overlapWidth = ComputeOverlapWidth(previousOriginal, currentOriginal);
            if (overlapWidth <= 0f)
            {
                continue;
            }

            var previousGrowth = (previousLayout.Bounds.Y + previousLayout.Bounds.Height)
                - (previousOriginal.Y + previousOriginal.Height);
            if (previousGrowth <= 0.01f)
            {
                continue;
            }

            var originalGap = Math.Max(0f, currentOriginal.Y - (previousOriginal.Y + previousOriginal.Height));
            var requiredY = previousLayout.Bounds.Y + previousLayout.Bounds.Height + originalGap;
            if (adjustedBounds.Y < requiredY)
            {
                adjustedBounds = adjustedBounds with { Y = requiredY };
            }
        }

        return adjustedBounds;
    }

    private static float ComputeOverlapWidth(ReportItemBounds left, ReportItemBounds right)
    {
        var start = Math.Max(left.X, right.X);
        var end = Math.Min(left.X + left.Width, right.X + right.Width);
        return Math.Max(0f, end - start);
    }

    private static string GetEstimatedTextContent(MaterializedTextReportItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return item.Text;
        }

        return item.ValueKind switch
        {
            MaterializedTextValueKind.PageNumber => "999",
            MaterializedTextValueKind.TotalPages => "999",
            _ => string.Empty
        };
    }

    private static int EstimateWrappedLineCount(string text, float availableWidth, float fontSize)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var averageCharacterWidth = Math.Max(1f, fontSize * 0.47f);
        var maxCharsPerLine = Math.Max(1, (int)MathF.Floor(availableWidth / averageCharacterWidth));
        var total = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            total += EstimateWrappedLineCount(lines[index].AsSpan(), maxCharsPerLine);
        }

        return Math.Max(1, total);
    }

    private static int EstimateWrappedLineCount(ReadOnlySpan<char> text, int maxCharsPerLine)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        var lineCount = 1;
        var current = 0;
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                if (current == 0)
                {
                    index++;
                    continue;
                }

                if (current + 1 > maxCharsPerLine)
                {
                    lineCount++;
                    current = 0;
                }
                else
                {
                    current++;
                }

                index++;
            }

            var wordStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var wordLength = index - wordStart;
            if (wordLength <= 0)
            {
                continue;
            }

            if (current > 0)
            {
                if (current + wordLength > maxCharsPerLine)
                {
                    lineCount++;
                    current = 0;
                }
            }

            while (wordLength > 0)
            {
                var remaining = maxCharsPerLine - current;
                if (remaining <= 0)
                {
                    lineCount++;
                    current = 0;
                    remaining = maxCharsPerLine;
                }

                if (wordLength <= remaining)
                {
                    current += wordLength;
                    wordLength = 0;
                }
                else
                {
                    current += remaining;
                    wordLength -= remaining;
                    if (wordLength > 0)
                    {
                        lineCount++;
                        current = 0;
                    }
                }
            }
        }

        return lineCount;
    }

    private MaterializedReportItem? MaterializeEmbeddedTablixCellContent(
        ReportItem item,
        ReportDefinition? reportDefinition,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedParameters,
        IReadOnlyList<MaterializedDataSet> materializedDataSets,
        IReadOnlyDictionary<string, object?> globals,
        ReportMaterializationStyleResolver styleResolver,
        ReportExpressionContext context,
        string path,
        MaterializationRuntime runtime,
        List<ReportDiagnostic> diagnostics)
    {
        return item switch
        {
            TextItem textItem => MaterializeTextItem(
                textItem,
                styleResolver,
                styleResolver.Resolve(textItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(textItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(textItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(textItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(textItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics),
                context,
                path,
                diagnostics),
            ImageItem imageItem => MaterializeImageItem(
                imageItem,
                styleResolver.Resolve(imageItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(imageItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(imageItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(imageItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(imageItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics),
                context,
                path,
                diagnostics),
            ShapeItem shapeItem => CreateBaseItem(
                new MaterializedShapeReportItem
                {
                    Shape = shapeItem.Shape
                },
                shapeItem,
                styleResolver.Resolve(shapeItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(shapeItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(shapeItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(shapeItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(shapeItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics)),
            ChartItem chartItem => MaterializeChartItem(
                chartItem,
                styleResolver.Resolve(chartItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(chartItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(chartItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(chartItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(chartItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics),
                materializedDataSets,
                resolvedParameters,
                globals,
                runtime,
                context,
                path,
                diagnostics),
            GaugeItem gaugeItem => MaterializeGaugeItem(
                gaugeItem,
                styleResolver.Resolve(gaugeItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(gaugeItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(gaugeItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(gaugeItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(gaugeItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics),
                materializedDataSets,
                resolvedParameters,
                globals,
                runtime,
                context,
                path,
                diagnostics),
            ContainerItem containerItem when reportDefinition is not null => MaterializeContainerItemAsync(
                containerItem,
                styleResolver.Resolve(containerItem.StyleName, context, path + ".styleName", diagnostics),
                EvaluateStringExpression(containerItem.BookmarkExpression, context, path + ".bookmarkExpression", diagnostics),
                EvaluateStringExpression(containerItem.TooltipExpression, context, path + ".tooltipExpression", diagnostics),
                MaterializePageBreak(containerItem.PageBreak, context, path + ".pageBreak", diagnostics),
                MaterializeDrillthrough(containerItem.DrillthroughAction, context, path + ".drillthroughAction", diagnostics),
                reportDefinition,
                resolvedParameters,
                materializedDataSets,
                globals,
                styleResolver,
                context,
                path,
                runtime,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                diagnostics,
                CancellationToken.None).AsTask().GetAwaiter().GetResult(),
            _ => null
        };
    }

    private async ValueTask<MaterializedSubreportReportItem> MaterializeSubreportItemAsync(
        SubreportItem item,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
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
            pageBreak,
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
        MaterializedReportPageBreak? pageBreak,
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
            pageBreak,
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

    private MaterializedReportPageBreak? MaterializePageBreak(
        ReportPageBreakDefinition? pageBreak,
        ReportExpressionContext context,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (pageBreak is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pageBreak.DisabledExpression))
        {
            var disabled = EvaluateExpression(pageBreak.DisabledExpression, context, path + ".disabledExpression", diagnostics);
            if (TryConvertToBoolean(disabled, context.Culture, defaultValue: false))
            {
                return null;
            }
        }

        return new MaterializedReportPageBreak
        {
            Location = pageBreak.Location,
            ResetPageNumber = pageBreak.ResetPageNumber
        };
    }

    private static T CreateBaseItem<T>(
        T target,
        ReportItem source,
        MaterializedReportStyle? style,
        string? bookmark,
        string? tooltip,
        MaterializedReportPageBreak? pageBreak,
        MaterializedReportDrillthroughAction? drillthrough)
        where T : MaterializedReportItem
    {
        target.SourceItemId = source.Id;
        target.Name = source.Name;
        target.Bounds = source.Bounds;
        target.ZIndex = source.ZIndex;
        target.StyleName = source.StyleName;
        target.Style = style?.Clone();
        target.Bookmark = bookmark;
        target.Tooltip = tooltip;
        target.PageBreak = pageBreak?.Clone();
        target.KeepTogether = source.KeepTogether;
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

    private static IReadOnlyDictionary<string, object?> ResolveDefaultFields(
        IReadOnlyList<MaterializedDataSet> dataSets,
        out IReadOnlyList<IReadOnlyDictionary<string, object?>> scopeRows,
        out string? dataSetId)
    {
        scopeRows = EmptyScopeRows;
        dataSetId = null;
        if (dataSets.Count != 1 || dataSets[0].Rows.Count == 0)
        {
            return EmptyFields;
        }

        dataSetId = dataSets[0].Id;
        scopeRows = ToScopeRows(dataSets[0].Rows);
        return dataSets[0].Rows[0].Values;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> CreateNamedScopes(
        IReadOnlyList<MaterializedDataSet> dataSets)
    {
        if (dataSets.Count == 0)
        {
            return EmptyNamedScopes;
        }

        var scopes = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < dataSets.Count; index++)
        {
            var scopeRows = ToScopeRows(dataSets[index].Rows);
            scopes[dataSets[index].Id] = scopeRows;
        }

        return scopes;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> AppendNamedScope(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> scopes,
        string? scopeName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopeRows)
    {
        if (string.IsNullOrWhiteSpace(scopeName))
        {
            return scopes;
        }

        var clone = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(scopes, StringComparer.OrdinalIgnoreCase)
        {
            [scopeName] = scopeRows
        };
        return clone;
    }

    private static ReportExpressionContext CreateReportContext(
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopeRows,
        IReadOnlyDictionary<string, object?> globals,
        MaterializationRuntime runtime,
        ReportExpressionScopeKind scopeKind,
        string? scopeName,
        int rowIndex = 0,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? namedScopes = null)
    {
        return new ReportExpressionContext
        {
            Parameters = parameters,
            Fields = fields,
            ScopeRows = scopeRows,
            ParentScopeRows = EmptyScopeRows,
            Globals = globals,
            NamedScopes = namedScopes ?? EmptyNamedScopes,
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

    private double? EvaluateNumericExpression(
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
        return TryConvertToDouble(value, context.Culture, out var numericValue)
            ? numericValue
            : null;
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
        // RDL stores a Hidden expression, so `true` means suppressed and `false` means visible.
        return !TryConvertToBoolean(value, context.Culture, defaultValue: false);
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
            HeaderHeight = settings.HeaderHeight,
            FooterHeight = settings.FooterHeight,
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

        for (var index = 0; index < value.Labels.Count; index++)
        {
            clone.Labels.Add(value.Labels[index]);
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

    private static void OffsetMaterializedItem(
        MaterializedReportItem item,
        float offsetX,
        float offsetY)
    {
        item.Bounds = item.Bounds with
        {
            X = item.Bounds.X + offsetX,
            Y = item.Bounds.Y + offsetY
        };

        switch (item)
        {
            case MaterializedLineReportItem lineItem:
                lineItem.X2 += offsetX;
                lineItem.Y2 += offsetY;
                break;
            case MaterializedContainerReportItem containerItem:
                for (var index = 0; index < containerItem.Items.Count; index++)
                {
                    OffsetMaterializedItem(containerItem.Items[index], offsetX, offsetY);
                }

                break;
        }
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

    private static bool TryConvertToDateTime(
        object? value,
        IFormatProvider formatProvider,
        out DateTime result)
    {
        result = default;
        if (value is null)
        {
            return false;
        }

        if (value is DateTime dateTime)
        {
            result = dateTime;
            return true;
        }

        try
        {
            result = Convert.ToDateTime(value, formatProvider);
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

        return ReportColorParser.TryParse(value.Trim(), out color);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ResolveScopedDataRows(
        IReadOnlyList<MaterializedDataRow> dataRows,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> activeScopeRows)
    {
        if (dataRows.Count == 0)
        {
            return EmptyScopeRows;
        }

        if (activeScopeRows.Count == 0)
        {
            return ToScopeRows(dataRows);
        }

        var availableRows = new HashSet<IReadOnlyDictionary<string, object?>>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < dataRows.Count; index++)
        {
            availableRows.Add(dataRows[index].Values);
        }

        var scopedRows = new List<IReadOnlyDictionary<string, object?>>(activeScopeRows.Count);
        for (var index = 0; index < activeScopeRows.Count; index++)
        {
            var row = activeScopeRows[index];
            if (availableRows.Contains(row))
            {
                scopedRows.Add(row);
            }
        }

        return scopedRows.Count > 0 ? scopedRows : ToScopeRows(dataRows);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyFields =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> EmptyScopeRows =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyNamedScopes =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

    private sealed class ReferenceEqualityComparer : IEqualityComparer<IReadOnlyDictionary<string, object?>>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IReadOnlyDictionary<string, object?>? x, IReadOnlyDictionary<string, object?>? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(IReadOnlyDictionary<string, object?> obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    private sealed record MaterializationRuntime(
        ReportDataProviderRegistry ProviderRegistry,
        ReportHostDataRegistry HostDataRegistry,
        IReadOnlyDictionary<string, ReportDefinition> ReferencedReports,
        CultureInfo Culture,
        CultureInfo UiCulture,
        TimeZoneInfo TimeZone);

    private sealed class ChartCategoryBucket
    {
        public ChartCategoryBucket(
            string key,
            IReadOnlyDictionary<string, object?> representativeFields,
            List<IReadOnlyDictionary<string, object?>> rows,
            string label,
            object? sortValue,
            int sourceIndex)
        {
            Key = key;
            RepresentativeFields = representativeFields;
            Rows = rows;
            Label = label;
            SortValue = sortValue;
            SourceIndex = sourceIndex;
        }

        public string Key { get; }

        public IReadOnlyDictionary<string, object?> RepresentativeFields { get; }

        public List<IReadOnlyDictionary<string, object?>> Rows { get; }

        public string Label { get; set; }

        public object? SortValue { get; set; }

        public int SourceIndex { get; }

        public ReportExpressionContext Context { get; set; } = null!;
    }

    private sealed class ChartHierarchyBucket
    {
        public ChartHierarchyBucket(
            string key,
            IReadOnlyDictionary<string, object?> representativeFields,
            List<IReadOnlyDictionary<string, object?>> rows,
            string label,
            object? sortValue,
            int sourceIndex)
        {
            Key = key;
            RepresentativeFields = representativeFields;
            Rows = rows;
            Label = label;
            SortValue = sortValue;
            SourceIndex = sourceIndex;
        }

        public string Key { get; }

        public IReadOnlyDictionary<string, object?> RepresentativeFields { get; }

        public List<IReadOnlyDictionary<string, object?>> Rows { get; }

        public string Label { get; set; }

        public object? SortValue { get; set; }

        public int SourceIndex { get; }
    }

    private sealed record TablixGroupInstance(
        object? Key,
        List<MaterializedDataRow> Rows,
        ReportExpressionContext Context);
}
