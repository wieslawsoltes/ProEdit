using System.Globalization;
using System.IO;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Expressions;

namespace Vibe.Office.Reporting.Avalonia.Designer;

internal sealed class ReportDesignerDataPreviewResult
{
    public ReportDataTable? DataSet { get; set; }

    public Dictionary<string, ReportParameterValue> ResolvedParameters { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<ReportParameterAvailableValue>> AvailableValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<ReportDiagnostic> Diagnostics { get; } = new();

    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

internal sealed class ReportDesignerDataRuntimeService
{
    private readonly ReportDataSetExecutor _dataSetExecutor;
    private readonly IReportExpressionCompiler _expressionCompiler;
    private readonly ReportParameterResolver _parameterResolver;

    public ReportDesignerDataRuntimeService(IReportExpressionCompiler expressionCompiler)
    {
        ArgumentNullException.ThrowIfNull(expressionCompiler);
        _expressionCompiler = expressionCompiler;
        _dataSetExecutor = new ReportDataSetExecutor(expressionCompiler);
        _parameterResolver = new ReportParameterResolver(expressionCompiler);
    }

    public async ValueTask<ReportDesignerDataPreviewResult> PreviewDataSetAsync(
        ReportViewerSource source,
        string dataSetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetId);

        var result = new ReportDesignerDataPreviewResult();
        var parameterRequest = new ReportParameterResolutionRequest
        {
            ReportDefinition = source.ReportDefinition,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = ResolveCulture(source.Culture, source.UiCulture),
            UiCulture = source.UiCulture ?? source.Culture ?? CultureInfo.InvariantCulture,
            TimeZone = source.TimeZone ?? TimeZoneInfo.Utc
        };

        foreach (var pair in source.Globals)
        {
            parameterRequest.Globals[pair.Key] = pair.Value;
        }

        var parameterResolution = await _parameterResolver.ResolveAsync(parameterRequest, cancellationToken);

        foreach (var pair in parameterResolution.ResolvedValues)
        {
            result.ResolvedParameters[pair.Key] = pair.Value;
        }

        foreach (var pair in parameterResolution.AvailableValues)
        {
            result.AvailableValues[pair.Key] = pair.Value;
        }

        result.Diagnostics.AddRange(parameterResolution.Diagnostics);
        if (parameterResolution.HasErrors)
        {
            return result;
        }

        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = source.ReportDefinition,
            DataSetId = dataSetId,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = ResolveCulture(source.Culture, source.UiCulture),
            UiCulture = source.UiCulture ?? source.Culture ?? CultureInfo.InvariantCulture,
            TimeZone = source.TimeZone ?? TimeZoneInfo.Utc
        };

        foreach (var pair in source.Globals)
        {
            executionRequest.Globals[pair.Key] = pair.Value;
        }

        foreach (var pair in parameterResolution.ResolvedValues)
        {
            executionRequest.ParameterValues[pair.Key] = pair.Value;
        }

        var executionResult = await _dataSetExecutor.ExecuteAsync(executionRequest, cancellationToken);
        result.DataSet = executionResult.DataSet;
        result.Diagnostics.AddRange(executionResult.Diagnostics);
        return result;
    }

    public bool TryHydrateLocalDataSetFields(
        ReportViewerSource source,
        ReportDataSetDefinition dataSet,
        out IReadOnlyList<ReportFieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dataSet);

        fields = Array.Empty<ReportFieldDefinition>();
        if (!IsLocalPreviewSafe(source, dataSet))
        {
            return false;
        }

        try
        {
            var result = PreviewDataSetAsync(source, dataSet.Id, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (result.HasErrors || result.DataSet is null || result.DataSet.Fields.Count == 0)
            {
                return false;
            }

            fields = result.DataSet.Fields
                .Select(static field => new ReportFieldDefinition
                {
                    Name = field.Name,
                    DataType = field.DataType
                })
                .ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<ReportDesignerTemplatePreviewResult> PreviewTemplateAsync(
        ReportViewerSource source,
        string templateContent,
        IReadOnlyDictionary<string, string> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);

        var result = new ReportDesignerTemplatePreviewResult
        {
            RawContent = templateContent ?? string.Empty
        };

        var parameterResolution = await ResolveParametersAsync(source, cancellationToken);
        result.Diagnostics.AddRange(parameterResolution.Diagnostics);
        if (parameterResolution.HasErrors)
        {
            result.ResolvedContent = result.RawContent;
            return result;
        }

        var namedScopes = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> primaryRows = Array.Empty<IReadOnlyDictionary<string, object?>>();
        IReadOnlyDictionary<string, object?> primaryFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (var dataSetIndex = 0; dataSetIndex < source.ReportDefinition.DataSets.Count; dataSetIndex++)
        {
            var dataSet = source.ReportDefinition.DataSets[dataSetIndex];
            var dataSetResult = await ExecuteDataSetAsync(source, dataSet.Id, parameterResolution.ResolvedValues, cancellationToken);
            result.Diagnostics.AddRange(dataSetResult.Diagnostics);
            if (dataSetResult.HasErrors || dataSetResult.DataSet is null)
            {
                continue;
            }

            var rows = dataSetResult.DataSet.Rows
                .Select(static row => (IReadOnlyDictionary<string, object?>)row.Values)
                .ToList();
            namedScopes[dataSet.Id] = rows;
            if (primaryRows.Count == 0 && dataSetResult.DataSet.Rows.Count > 0)
            {
                primaryRows = rows;
                primaryFields = dataSetResult.DataSet.Rows[0].Values;
            }
        }

        var context = new ReportExpressionContext
        {
            Parameters = parameterResolution.ResolvedValues,
            Fields = primaryFields,
            Globals = source.Globals,
            ScopeRows = primaryRows,
            ParentScopeRows = primaryRows,
            NamedScopes = namedScopes,
            Culture = ResolveCulture(source.Culture, source.UiCulture),
            UiCulture = source.UiCulture ?? source.Culture ?? CultureInfo.InvariantCulture,
            TimeZone = source.TimeZone ?? TimeZoneInfo.Utc,
            ScopeKind = ReportExpressionScopeKind.Row,
            RowIndex = 0
        };

        foreach (var pair in bindings)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                result.BindingValues[pair.Key] = string.Empty;
                continue;
            }

            var compilation = _expressionCompiler.Compile(pair.Value);
            result.Diagnostics.AddRange(compilation.Diagnostics);
            if (compilation.HasErrors || compilation.Expression is null)
            {
                continue;
            }

            if (!compilation.Expression.TryEvaluate(context, out var value, out var diagnostic))
            {
                if (diagnostic is not null)
                {
                    result.Diagnostics.Add(diagnostic);
                }

                continue;
            }

            result.BindingValues[pair.Key] = FormatPreviewValue(value, context.Culture);
        }

        result.ResolvedContent = ReplaceTemplateTokens(result.RawContent, result.BindingValues);
        return result;
    }

    public bool TryLoadTemplateSourceText(
        ReportDocumentTemplateFormat format,
        string? sourcePath,
        out string content,
        out string? errorMessage)
    {
        content = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            errorMessage = "Template source path is empty.";
            return false;
        }

        if (format == ReportDocumentTemplateFormat.Docx)
        {
            if (!File.Exists(sourcePath))
            {
                errorMessage = $"Template source '{sourcePath}' was not found.";
                return false;
            }

            content = Convert.ToBase64String(File.ReadAllBytes(sourcePath));
            return true;
        }

        if (!File.Exists(sourcePath))
        {
            errorMessage = $"Template source '{sourcePath}' was not found.";
            return false;
        }

        content = File.ReadAllText(sourcePath);
        return true;
    }

    private async ValueTask<ReportParameterResolutionResult> ResolveParametersAsync(
        ReportViewerSource source,
        CancellationToken cancellationToken)
    {
        var parameterRequest = new ReportParameterResolutionRequest
        {
            ReportDefinition = source.ReportDefinition,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = ResolveCulture(source.Culture, source.UiCulture),
            UiCulture = source.UiCulture ?? source.Culture ?? CultureInfo.InvariantCulture,
            TimeZone = source.TimeZone ?? TimeZoneInfo.Utc
        };

        foreach (var pair in source.Globals)
        {
            parameterRequest.Globals[pair.Key] = pair.Value;
        }

        return await _parameterResolver.ResolveAsync(parameterRequest, cancellationToken);
    }

    private async ValueTask<ReportDataSetExecutionResult> ExecuteDataSetAsync(
        ReportViewerSource source,
        string dataSetId,
        IReadOnlyDictionary<string, ReportParameterValue> parameterValues,
        CancellationToken cancellationToken)
    {
        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = source.ReportDefinition,
            DataSetId = dataSetId,
            ProviderRegistry = source.ProviderRegistry,
            HostDataRegistry = source.HostDataRegistry,
            Culture = ResolveCulture(source.Culture, source.UiCulture),
            UiCulture = source.UiCulture ?? source.Culture ?? CultureInfo.InvariantCulture,
            TimeZone = source.TimeZone ?? TimeZoneInfo.Utc
        };

        foreach (var pair in source.Globals)
        {
            executionRequest.Globals[pair.Key] = pair.Value;
        }

        foreach (var pair in parameterValues)
        {
            executionRequest.ParameterValues[pair.Key] = pair.Value;
        }

        return await _dataSetExecutor.ExecuteAsync(executionRequest, cancellationToken);
    }

    private static string FormatPreviewValue(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            DateTime dateTime => dateTime.ToString(culture),
            IFormattable formattable => formattable.ToString(format: null, culture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ReplaceTemplateTokens(
        string content,
        IReadOnlyDictionary<string, string> values)
    {
        var resolved = content ?? string.Empty;
        foreach (var pair in values)
        {
            resolved = resolved.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static CultureInfo ResolveCulture(CultureInfo? culture, CultureInfo? uiCulture)
    {
        return culture ?? uiCulture ?? CultureInfo.InvariantCulture;
    }

    private static bool IsLocalPreviewSafe(ReportViewerSource source, ReportDataSetDefinition dataSet)
    {
        var dataSource = source.ReportDefinition.DataSources.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, dataSet.DataSourceId, StringComparison.OrdinalIgnoreCase));
        if (dataSource is null)
        {
            return false;
        }

        return dataSource.ProviderId switch
        {
            ReportProviderIds.InMemory => true,
            ReportProviderIds.Json => true,
            ReportProviderIds.Csv => true,
            ReportProviderIds.EnterData => true,
            _ => false
        };
    }
}

internal sealed class ReportDesignerTemplatePreviewResult
{
    public string RawContent { get; set; } = string.Empty;

    public string ResolvedContent { get; set; } = string.Empty;

    public Dictionary<string, string> BindingValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<ReportDiagnostic> Diagnostics { get; } = new();

    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}
