using System.Globalization;
using Vibe.Office.Reporting.Expressions;

namespace Vibe.Office.Reporting.Data;

/// <summary>
/// Represents one dataset execution request.
/// </summary>
public sealed class ReportDataSetExecutionRequest
{
    /// <summary>
    /// Gets or sets the owning report definition.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the dataset identifier to execute.
    /// </summary>
    public string DataSetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider registry.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; set; } = new();

    /// <summary>
    /// Gets or sets the host data registry.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

    /// <summary>
    /// Gets the supplied parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the supplied global values.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } = new(StringComparer.OrdinalIgnoreCase);

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
/// Represents one dataset execution result.
/// </summary>
public sealed class ReportDataSetExecutionResult
{
    /// <summary>
    /// Gets or sets the normalized dataset.
    /// </summary>
    public ReportDataTable? DataSet { get; set; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether execution emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Executes one report dataset through providers, calculated fields, filters, and sorts.
/// </summary>
public sealed class ReportDataSetExecutor
{
    private readonly IReportExpressionCompiler _expressionCompiler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDataSetExecutor" /> class.
    /// </summary>
    /// <param name="expressionCompiler">The expression compiler.</param>
    public ReportDataSetExecutor(IReportExpressionCompiler expressionCompiler)
    {
        _expressionCompiler = expressionCompiler ?? throw new ArgumentNullException(nameof(expressionCompiler));
    }

    /// <summary>
    /// Executes one dataset.
    /// </summary>
    /// <param name="request">The execution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The dataset execution result.</returns>
    public async ValueTask<ReportDataSetExecutionResult> ExecuteAsync(
        ReportDataSetExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ReportDataSetExecutionResult();
        var reportDefinition = request.ReportDefinition;
        var dataSet = reportDefinition.DataSets.FirstOrDefault(item =>
            item.Id.Equals(request.DataSetId, StringComparison.OrdinalIgnoreCase));
        if (dataSet is null)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DataSetNotFound,
                $"Dataset '{request.DataSetId}' was not found.",
                "$.dataSets"));
            return result;
        }

        var dataSource = reportDefinition.DataSources.FirstOrDefault(item =>
            item.Id.Equals(dataSet.DataSourceId, StringComparison.OrdinalIgnoreCase));
        if (dataSource is null)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DataSourceNotFound,
                $"Data source '{dataSet.DataSourceId}' was not found for dataset '{dataSet.Id}'.",
                "$.dataSources"));
            return result;
        }

        if (!request.ProviderRegistry.TryGetProvider(dataSource.ProviderId, out var provider))
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DataProviderNotFound,
                $"Data provider '{dataSource.ProviderId}' was not registered.",
                "$.dataSources"));
            return result;
        }

        var culture = request.Culture ?? CultureInfo.InvariantCulture;
        var uiCulture = request.UiCulture ?? culture;
        var timeZone = request.TimeZone ?? TimeZoneInfo.Utc;
        var globals = ReportDataRuntimeHelpers.CreateGlobals(request.Globals, timeZone);
        var dataSetParameters = EvaluateDataSetParameters(
            dataSet,
            request.ParameterValues,
            globals,
            culture,
            uiCulture,
            timeZone,
            result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        ReportDataTable table;
        try
        {
            table = await provider.ExecuteAsync(
                dataSource,
                dataSet,
                new ReportDataProviderContext
                {
                    Parameters = request.ParameterValues,
                    DataSetParameters = dataSetParameters,
                    HostDataRegistry = request.HostDataRegistry,
                    Culture = culture,
                    UiCulture = uiCulture,
                    TimeZone = timeZone
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DataReadFailed,
                ex.Message,
                $"$.dataSets[{dataSet.Id}]"));
            return result;
        }

        table ??= new ReportDataTable();
        table.DataSetId = dataSet.Id;
        EnsureFieldDefinitions(table);
        ApplyExpectedFields(table, dataSet.ExpectedFields, culture, result.Diagnostics);
        ApplyCalculatedFields(table, dataSet.CalculatedFields, request.ParameterValues, globals, culture, uiCulture, timeZone, result.Diagnostics);
        ApplyFilters(table, dataSet.Filters, request.ParameterValues, globals, culture, uiCulture, timeZone, result.Diagnostics);
        ApplySorts(table, dataSet.Sorts, request.ParameterValues, globals, culture, uiCulture, timeZone, result.Diagnostics);

        result.DataSet = table;
        return result;
    }

    private Dictionary<string, object?> EvaluateDataSetParameters(
        ReportDataSetDefinition dataSet,
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> globals,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        List<ReportDiagnostic> diagnostics)
    {
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var parameterIndex = 0; parameterIndex < dataSet.Parameters.Count; parameterIndex++)
        {
            var definition = dataSet.Parameters[parameterIndex];
            if (!TryCompileExpression(
                    definition.ValueExpression,
                    $"$.dataSets[{dataSet.Id}].parameters[{parameterIndex}]",
                    diagnostics,
                    out var expression))
            {
                continue;
            }

            var context = new ReportExpressionContext
            {
                Parameters = parameters,
                Globals = globals,
                Culture = culture,
                UiCulture = uiCulture,
                TimeZone = timeZone,
                ScopeKind = ReportExpressionScopeKind.Report
            };

            if (!expression!.TryEvaluate(context, out var value, out var diagnostic))
            {
                diagnostics.Add(CloneDiagnostic(
                    diagnostic!,
                    $"$.dataSets[{dataSet.Id}].parameters[{parameterIndex}]"));
                continue;
            }

            resolved[definition.Name] = value;
        }

        return resolved;
    }

    private void EnsureFieldDefinitions(ReportDataTable table)
    {
        var fields = new Dictionary<string, ReportParameterDataType>(StringComparer.OrdinalIgnoreCase);
        for (var fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
        {
            var field = table.Fields[fieldIndex];
            fields[field.Name] = field.DataType;
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            foreach (var pair in table.Rows[rowIndex].Values)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                var dataType = ReportDataRuntimeHelpers.InferDataType(pair.Value);
                if (!fields.TryGetValue(pair.Key, out var existing))
                {
                    fields[pair.Key] = dataType;
                    continue;
                }

                fields[pair.Key] = ReportDataRuntimeHelpers.MergeDataType(existing, dataType);
            }
        }

        table.Fields.Clear();
        foreach (var pair in fields)
        {
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = pair.Key,
                DataType = pair.Value
            });
        }
    }

    private void ApplyExpectedFields(
        ReportDataTable table,
        IReadOnlyList<ReportFieldDefinition> expectedFields,
        CultureInfo culture,
        List<ReportDiagnostic> diagnostics)
    {
        for (var fieldIndex = 0; fieldIndex < expectedFields.Count; fieldIndex++)
        {
            var expectedField = expectedFields[fieldIndex];
            if (!table.Fields.Any(item => item.Name.Equals(expectedField.Name, StringComparison.OrdinalIgnoreCase)))
            {
                table.Fields.Add(new ReportFieldDefinition
                {
                    Name = expectedField.Name,
                    DataType = expectedField.DataType
                });
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                row.Values.TryGetValue(expectedField.Name, out var rawValue);
                try
                {
                    row.Values[expectedField.Name] = ReportDataRuntimeHelpers.CoerceValue(
                        rawValue,
                        expectedField.DataType,
                        culture);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ValueCoercionFailed,
                        ex.Message,
                        $"$.rows[{rowIndex}].{expectedField.Name}"));
                }
            }
        }
    }

    private void ApplyCalculatedFields(
        ReportDataTable table,
        IReadOnlyList<ReportCalculatedFieldDefinition> calculatedFields,
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> globals,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        List<ReportDiagnostic> diagnostics)
    {
        if (calculatedFields.Count == 0 || table.Rows.Count == 0)
        {
            return;
        }

        var scopeRows = BuildScopeRows(table.Rows);
        for (var fieldIndex = 0; fieldIndex < calculatedFields.Count; fieldIndex++)
        {
            var calculatedField = calculatedFields[fieldIndex];
            if (!TryCompileExpression(
                    calculatedField.Expression,
                    $"$.calculatedFields[{fieldIndex}]",
                    diagnostics,
                    out var expression))
            {
                continue;
            }

            if (!table.Fields.Any(item => item.Name.Equals(calculatedField.Name, StringComparison.OrdinalIgnoreCase)))
            {
                table.Fields.Add(new ReportFieldDefinition
                {
                    Name = calculatedField.Name,
                    DataType = calculatedField.DataType
                });
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                var context = new ReportExpressionContext
                {
                    Parameters = parameters,
                    Fields = row.Values,
                    Globals = globals,
                    ScopeRows = scopeRows,
                    Culture = culture,
                    UiCulture = uiCulture,
                    TimeZone = timeZone,
                    ScopeKind = ReportExpressionScopeKind.Row,
                    RowIndex = rowIndex
                };

                if (!expression!.TryEvaluate(context, out var rawValue, out var diagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(
                        diagnostic!,
                        $"$.rows[{rowIndex}].{calculatedField.Name}"));
                    continue;
                }

                try
                {
                    row.Values[calculatedField.Name] = ReportDataRuntimeHelpers.CoerceValue(
                        rawValue,
                        calculatedField.DataType,
                        culture);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ValueCoercionFailed,
                        ex.Message,
                        $"$.rows[{rowIndex}].{calculatedField.Name}"));
                }
            }
        }
    }

    private void ApplyFilters(
        ReportDataTable table,
        IReadOnlyList<ReportFilterDefinition> filters,
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> globals,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        List<ReportDiagnostic> diagnostics)
    {
        if (filters.Count == 0 || table.Rows.Count == 0)
        {
            return;
        }

        var compiledFilters = new List<(ICompiledReportExpression Left, ICompiledReportExpression Right, ReportFilterOperator Operator)>();
        for (var filterIndex = 0; filterIndex < filters.Count; filterIndex++)
        {
            var filter = filters[filterIndex];
            if (!TryCompileExpression(filter.Expression, $"$.filters[{filterIndex}].expression", diagnostics, out var leftExpression)
                || !TryCompileExpression(filter.ValueExpression, $"$.filters[{filterIndex}].valueExpression", diagnostics, out var rightExpression))
            {
                continue;
            }

            compiledFilters.Add((leftExpression!, rightExpression!, filter.Operator));
        }

        if (compiledFilters.Count == 0)
        {
            return;
        }

        var scopeRows = BuildScopeRows(table.Rows);
        var filteredRows = new List<ReportDataRecord>(table.Rows.Count);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var context = new ReportExpressionContext
            {
                Parameters = parameters,
                Fields = row.Values,
                Globals = globals,
                ScopeRows = scopeRows,
                Culture = culture,
                UiCulture = uiCulture,
                TimeZone = timeZone,
                ScopeKind = ReportExpressionScopeKind.Row,
                RowIndex = rowIndex
            };

            var includeRow = true;
            for (var filterIndex = 0; filterIndex < compiledFilters.Count; filterIndex++)
            {
                var filter = compiledFilters[filterIndex];
                if (!filter.Left.TryEvaluate(context, out var left, out var leftDiagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(leftDiagnostic!, $"$.rows[{rowIndex}].filter[{filterIndex}]"));
                    includeRow = false;
                    break;
                }

                if (!filter.Right.TryEvaluate(context, out var right, out var rightDiagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(rightDiagnostic!, $"$.rows[{rowIndex}].filter[{filterIndex}]"));
                    includeRow = false;
                    break;
                }

                if (!ReportDataRuntimeHelpers.EvaluateFilter(left, filter.Operator, right, culture))
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

        table.Rows.Clear();
        table.Rows.AddRange(filteredRows);
    }

    private void ApplySorts(
        ReportDataTable table,
        IReadOnlyList<ReportSortDefinition> sorts,
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        IReadOnlyDictionary<string, object?> globals,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        List<ReportDiagnostic> diagnostics)
    {
        if (sorts.Count == 0 || table.Rows.Count < 2)
        {
            return;
        }

        var compiledSorts = new List<(ICompiledReportExpression Expression, ReportSortDirection Direction)>();
        for (var sortIndex = 0; sortIndex < sorts.Count; sortIndex++)
        {
            if (!TryCompileExpression(sorts[sortIndex].Expression, $"$.sorts[{sortIndex}]", diagnostics, out var expression))
            {
                continue;
            }

            compiledSorts.Add((expression!, sorts[sortIndex].Direction));
        }

        if (compiledSorts.Count == 0)
        {
            return;
        }

        var scopeRows = BuildScopeRows(table.Rows);
        var sortableRows = new List<SortableRow>(table.Rows.Count);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var context = new ReportExpressionContext
            {
                Parameters = parameters,
                Fields = row.Values,
                Globals = globals,
                ScopeRows = scopeRows,
                Culture = culture,
                UiCulture = uiCulture,
                TimeZone = timeZone,
                ScopeKind = ReportExpressionScopeKind.Row,
                RowIndex = rowIndex
            };

            var keys = new object?[compiledSorts.Count];
            var hasError = false;
            for (var sortIndex = 0; sortIndex < compiledSorts.Count; sortIndex++)
            {
                if (!compiledSorts[sortIndex].Expression.TryEvaluate(context, out keys[sortIndex], out var diagnostic))
                {
                    diagnostics.Add(CloneDiagnostic(diagnostic!, $"$.rows[{rowIndex}].sort[{sortIndex}]"));
                    hasError = true;
                    break;
                }
            }

            if (!hasError)
            {
                sortableRows.Add(new SortableRow(row, keys));
            }
        }

        sortableRows.Sort((left, right) =>
        {
            for (var index = 0; index < compiledSorts.Count; index++)
            {
                var comparison = ReportDataRuntimeHelpers.Compare(left.Keys[index], right.Keys[index], culture);
                if (comparison == 0)
                {
                    continue;
                }

                return compiledSorts[index].Direction == ReportSortDirection.Descending
                    ? -comparison
                    : comparison;
            }

            return 0;
        });

        table.Rows.Clear();
        for (var index = 0; index < sortableRows.Count; index++)
        {
            table.Rows.Add(sortableRows[index].Row);
        }
    }

    private bool TryCompileExpression(
        string expressionText,
        string path,
        List<ReportDiagnostic> diagnostics,
        out ICompiledReportExpression? expression)
    {
        var compilationResult = _expressionCompiler.Compile(expressionText);
        for (var index = 0; index < compilationResult.Diagnostics.Count; index++)
        {
            diagnostics.Add(CloneDiagnostic(compilationResult.Diagnostics[index], path));
        }

        expression = compilationResult.Expression;
        return expression is not null && !compilationResult.HasErrors;
    }

    private static List<IReadOnlyDictionary<string, object?>> BuildScopeRows(IReadOnlyList<ReportDataRecord> rows)
    {
        var scopeRows = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            scopeRows.Add(rows[index].Values);
        }

        return scopeRows;
    }

    private static ReportDiagnostic CloneDiagnostic(ReportDiagnostic diagnostic, string path)
    {
        return new ReportDiagnostic(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            path);
    }

    private sealed record SortableRow(ReportDataRecord Row, object?[] Keys);
}
