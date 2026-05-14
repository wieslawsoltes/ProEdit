using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ProEdit.Reporting;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Expressions;

namespace ProEdit.Documents.Data;

/// <summary>
/// Defines one document mail-merge binding.
/// </summary>
public sealed class DocumentMailMergeBindingDefinition
{
    /// <summary>
    /// Gets or sets the dataset identifier.
    /// </summary>
    public string DataSetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target mail-merge main document type.
    /// </summary>
    public string MainDocumentType { get; set; } = MailMergeData.DefaultMainDocumentType;
}

/// <summary>
/// Defines one document custom-XML binding.
/// </summary>
public sealed class DocumentCustomXmlBindingDefinition
{
    /// <summary>
    /// Gets or sets the dataset identifier.
    /// </summary>
    public string DataSetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target custom XML store item identifier.
    /// </summary>
    public string StoreItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root element name.
    /// </summary>
    public string RootElementName { get; set; } = "root";

    /// <summary>
    /// Gets or sets the row element name.
    /// </summary>
    public string RowElementName { get; set; } = "row";

    /// <summary>
    /// Gets or sets the optional namespace URI.
    /// </summary>
    public string? NamespaceUri { get; set; }
}

/// <summary>
/// Represents one document data-binding request.
/// </summary>
public sealed class DocumentDataBindingRequest
{
    /// <summary>
    /// Gets or sets the target document.
    /// </summary>
    public Document Document { get; set; } = new();

    /// <summary>
    /// Gets or sets the report definition that owns the datasource and dataset definitions.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the provider registry.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; set; } = ReportDataProviders.CreateDefaultRegistry();

    /// <summary>
    /// Gets or sets the host data registry.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

    /// <summary>
    /// Gets the supplied parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the supplied global values.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the configured mail-merge bindings.
    /// </summary>
    public List<DocumentMailMergeBindingDefinition> MailMergeBindings { get; } = new();

    /// <summary>
    /// Gets the configured custom XML bindings.
    /// </summary>
    public List<DocumentCustomXmlBindingDefinition> CustomXmlBindings { get; } = new();

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
/// Represents the outcome of one document data-binding operation.
/// </summary>
public sealed class DocumentDataBindingResult
{
    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the operation emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Binds connector-backed datasets into a document's custom XML parts and mail-merge state.
/// </summary>
public interface IDocumentDataBinder
{
    /// <summary>
    /// Executes the configured bindings.
    /// </summary>
    /// <param name="request">The binding request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The binding result.</returns>
    ValueTask<DocumentDataBindingResult> BindAsync(
        DocumentDataBindingRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IDocumentDataBinder" />.
/// </summary>
public sealed class DocumentDataBinder : IDocumentDataBinder
{
    private readonly ReportDataSetExecutor _dataSetExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentDataBinder" /> class.
    /// </summary>
    /// <param name="expressionCompiler">The optional expression compiler used by the dataset executor.</param>
    public DocumentDataBinder(IReportExpressionCompiler? expressionCompiler = null)
    {
        _dataSetExecutor = new ReportDataSetExecutor(expressionCompiler ?? new ReportExpressionCompiler());
    }

    /// <inheritdoc />
    public async ValueTask<DocumentDataBindingResult> BindAsync(
        DocumentDataBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.ReportDefinition);

        var result = new DocumentDataBindingResult();
        var cache = new Dictionary<string, ReportDataSetExecutionResult>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.CustomXmlBindings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = request.CustomXmlBindings[index];
            var normalizedStoreItemId = NormalizeStoreItemId(binding.StoreItemId);
            if (string.IsNullOrWhiteSpace(normalizedStoreItemId))
            {
                result.Diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.InvalidTemplate,
                    $"Custom XML binding '{binding.DataSetId}' requires a non-empty store item id.",
                    $"customXmlBindings[{index}].storeItemId"));
                continue;
            }

            var execution = await ExecuteDataSetAsync(binding.DataSetId, request, cache, cancellationToken);
            AppendDiagnostics(result.Diagnostics, execution.Diagnostics);
            if (execution.DataSet is null || execution.HasErrors)
            {
                continue;
            }

            request.Document.CustomXmlParts[normalizedStoreItemId] =
                BuildCustomXmlDocument(execution.DataSet, binding, request.Culture ?? CultureInfo.InvariantCulture);
        }

        if (request.MailMergeBindings.Count > 0)
        {
            var mergeData = new MailMergeData();
            var hasAppliedMailMergeBinding = false;
            for (var index = 0; index < request.MailMergeBindings.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var binding = request.MailMergeBindings[index];
                var execution = await ExecuteDataSetAsync(binding.DataSetId, request, cache, cancellationToken);
                AppendDiagnostics(result.Diagnostics, execution.Diagnostics);
                if (execution.DataSet is null || execution.HasErrors)
                {
                    continue;
                }

                mergeData.MainDocumentType = string.IsNullOrWhiteSpace(binding.MainDocumentType)
                    ? MailMergeData.DefaultMainDocumentType
                    : binding.MainDocumentType;
                PopulateMailMergeData(
                    mergeData,
                    execution.DataSet,
                    request.Culture ?? CultureInfo.InvariantCulture);
                hasAppliedMailMergeBinding = true;
            }

            if (hasAppliedMailMergeBinding)
            {
                request.Document.MailMergeData = mergeData;
            }
        }

        return result;
    }

    private async ValueTask<ReportDataSetExecutionResult> ExecuteDataSetAsync(
        string dataSetId,
        DocumentDataBindingRequest request,
        Dictionary<string, ReportDataSetExecutionResult> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(dataSetId, out var cached))
        {
            return cached;
        }

        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = request.ReportDefinition,
            DataSetId = dataSetId,
            ProviderRegistry = request.ProviderRegistry,
            HostDataRegistry = request.HostDataRegistry,
            Culture = request.Culture,
            UiCulture = request.UiCulture,
            TimeZone = request.TimeZone
        };

        foreach (var pair in request.ParameterValues)
        {
            executionRequest.ParameterValues[pair.Key] = CloneParameterValue(pair.Value);
        }

        foreach (var pair in request.Globals)
        {
            executionRequest.Globals[pair.Key] = pair.Value;
        }

        var execution = await _dataSetExecutor.ExecuteAsync(executionRequest, cancellationToken);
        cache[dataSetId] = execution;
        return execution;
    }

    private static void PopulateMailMergeData(
        MailMergeData target,
        ReportDataTable dataSet,
        CultureInfo culture)
    {
        for (var fieldIndex = 0; fieldIndex < dataSet.Fields.Count; fieldIndex++)
        {
            var fieldName = dataSet.Fields[fieldIndex].Name;
            if (!target.FieldNames.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
            {
                target.FieldNames.Add(fieldName);
            }
        }

        for (var rowIndex = 0; rowIndex < dataSet.Rows.Count; rowIndex++)
        {
            var record = new MailMergeRecord();
            foreach (var pair in dataSet.Rows[rowIndex].Values)
            {
                record.Fields[pair.Key] = ConvertToString(pair.Value, culture);
            }

            target.Records.Add(record);
        }
    }

    private static XDocument BuildCustomXmlDocument(
        ReportDataTable dataSet,
        DocumentCustomXmlBindingDefinition binding,
        CultureInfo culture)
    {
        var rootName = string.IsNullOrWhiteSpace(binding.RootElementName) ? "root" : binding.RootElementName;
        var rowName = string.IsNullOrWhiteSpace(binding.RowElementName) ? "row" : binding.RowElementName;
        var ns = string.IsNullOrWhiteSpace(binding.NamespaceUri)
            ? XNamespace.None
            : XNamespace.Get(binding.NamespaceUri);

        var root = new XElement(ns + XmlConvert.EncodeLocalName(rootName));
        for (var rowIndex = 0; rowIndex < dataSet.Rows.Count; rowIndex++)
        {
            var row = new XElement(ns + XmlConvert.EncodeLocalName(rowName));
            foreach (var pair in dataSet.Rows[rowIndex].Values)
            {
                row.Add(new XElement(
                    ns + XmlConvert.EncodeLocalName(pair.Key),
                    ConvertToString(pair.Value, culture)));
            }

            root.Add(row);
        }

        return new XDocument(root);
    }

    private static string ConvertToString(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", culture),
            DateTime dateTime => dateTime.ToString("O", culture),
            IFormattable formattable => formattable.ToString(null, culture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string NormalizeStoreItemId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
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

    private static void AppendDiagnostics(
        List<ReportDiagnostic> target,
        IReadOnlyList<ReportDiagnostic> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }
}
