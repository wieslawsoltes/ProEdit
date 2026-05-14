using System.Globalization;
using ProEdit.Reporting.Expressions;

namespace ProEdit.Reporting.Data;

/// <summary>
/// Represents one available parameter choice.
/// </summary>
public sealed class ReportParameterAvailableValue
{
    /// <summary>
    /// Gets or sets the underlying value.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Represents one parameter resolution request.
/// </summary>
public sealed class ReportParameterResolutionRequest
{
    /// <summary>
    /// Gets or sets the report definition.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

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
    public Dictionary<string, ReportParameterValue> SuppliedValues { get; } = new(StringComparer.OrdinalIgnoreCase);

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
/// Represents one parameter resolution result.
/// </summary>
public sealed class ReportParameterResolutionResult
{
    /// <summary>
    /// Gets the resolved parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ResolvedValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the available values per parameter identifier.
    /// </summary>
    public Dictionary<string, List<ReportParameterAvailableValue>> AvailableValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether resolution emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Resolves parameter defaults and cascading available values.
/// </summary>
public sealed class ReportParameterResolver
{
    private readonly ReportDataSetExecutor _dataSetExecutor;
    private readonly IReportExpressionCompiler _expressionCompiler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportParameterResolver" /> class.
    /// </summary>
    /// <param name="expressionCompiler">The expression compiler.</param>
    public ReportParameterResolver(IReportExpressionCompiler expressionCompiler)
    {
        _expressionCompiler = expressionCompiler ?? throw new ArgumentNullException(nameof(expressionCompiler));
        _dataSetExecutor = new ReportDataSetExecutor(expressionCompiler);
    }

    /// <summary>
    /// Resolves the report parameters.
    /// </summary>
    /// <param name="request">The resolution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolution result.</returns>
    public async ValueTask<ReportParameterResolutionResult> ResolveAsync(
        ReportParameterResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ReportParameterResolutionResult();
        var culture = request.Culture ?? CultureInfo.InvariantCulture;
        var uiCulture = request.UiCulture ?? culture;
        var timeZone = request.TimeZone ?? TimeZoneInfo.Utc;
        var globals = ReportDataRuntimeHelpers.CreateGlobals(request.Globals, timeZone);
        var orderedParameters = OrderParameters(request.ReportDefinition.Parameters, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        for (var parameterIndex = 0; parameterIndex < orderedParameters.Count; parameterIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameter = orderedParameters[parameterIndex];
            ReportParameterValue resolvedValue;
            var usedSuppliedValue = request.SuppliedValues.TryGetValue(parameter.Id, out var suppliedValue);

            if (usedSuppliedValue)
            {
                try
                {
                    resolvedValue = ReportDataRuntimeHelpers.CoerceParameterValue(parameter, suppliedValue!, culture);
                }
                catch (Exception ex)
                {
                    result.Diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ValueCoercionFailed,
                        ex.Message,
                        $"$.parameters[{parameter.Id}]"));
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(parameter.DefaultValueExpression))
            {
                if (!TryResolveDefaultValue(parameter, result.ResolvedValues, globals, culture, uiCulture, timeZone, result.Diagnostics, out resolvedValue))
                {
                    continue;
                }
            }
            else
            {
                if (!parameter.AllowNull && parameter.Visibility != ReportParameterVisibility.Visible)
                {
                    result.Diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ParameterResolutionFailed,
                        $"Parameter '{parameter.Id}' requires a value.",
                        $"$.parameters[{parameter.Id}]"));
                    continue;
                }

                resolvedValue = new ReportParameterValue
                {
                    IsNull = true
                };
            }

            result.ResolvedValues[parameter.Id] = resolvedValue;
            EnsureParameterLabels(resolvedValue, culture);

            if (!string.IsNullOrWhiteSpace(parameter.AvailableValuesDataSetId))
            {
                var availableValues = await ResolveAvailableValuesAsync(
                    parameter,
                    request,
                    result.ResolvedValues,
                    culture,
                    uiCulture,
                    timeZone,
                    cancellationToken);
                for (var diagnosticIndex = 0; diagnosticIndex < availableValues.Diagnostics.Count; diagnosticIndex++)
                {
                    result.Diagnostics.Add(availableValues.Diagnostics[diagnosticIndex]);
                }

                if (availableValues.Values is not null)
                {
                    result.AvailableValues[parameter.Id] = availableValues.Values;
                    resolvedValue = ValidateResolvedValueAgainstAvailableValues(
                        parameter,
                        resolvedValue,
                        usedSuppliedValue,
                        availableValues.Values,
                        culture,
                        result.Diagnostics);
                    result.ResolvedValues[parameter.Id] = resolvedValue;
                    ApplyAvailableValueLabels(resolvedValue, availableValues.Values, culture);
                }
            }
        }

        return result;
    }

    private bool TryResolveDefaultValue(
        ReportParameterDefinition parameter,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedValues,
        IReadOnlyDictionary<string, object?> globals,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        List<ReportDiagnostic> diagnostics,
        out ReportParameterValue parameterValue)
    {
        parameterValue = new ReportParameterValue();
        var compilationResult = _expressionCompiler.Compile(parameter.DefaultValueExpression!);
        for (var diagnosticIndex = 0; diagnosticIndex < compilationResult.Diagnostics.Count; diagnosticIndex++)
        {
            diagnostics.Add(CloneDiagnostic(compilationResult.Diagnostics[diagnosticIndex], $"$.parameters[{parameter.Id}].defaultValueExpression"));
        }

        if (compilationResult.Expression is null || compilationResult.HasErrors)
        {
            return false;
        }

        var context = new ReportExpressionContext
        {
            Parameters = resolvedValues,
            Globals = globals,
            Culture = culture,
            UiCulture = uiCulture,
            TimeZone = timeZone,
            ScopeKind = ReportExpressionScopeKind.Report
        };

        if (!compilationResult.Expression.TryEvaluate(context, out var rawValue, out var diagnostic))
        {
            diagnostics.Add(CloneDiagnostic(diagnostic!, $"$.parameters[{parameter.Id}].defaultValueExpression"));
            return false;
        }

        try
        {
            parameterValue = ReportDataRuntimeHelpers.CoerceParameterValue(parameter, rawValue, culture);
            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ValueCoercionFailed,
                ex.Message,
                $"$.parameters[{parameter.Id}]"));
            return false;
        }
    }

    private async ValueTask<(List<ReportParameterAvailableValue>? Values, List<ReportDiagnostic> Diagnostics)> ResolveAvailableValuesAsync(
        ReportParameterDefinition parameter,
        ReportParameterResolutionRequest request,
        IReadOnlyDictionary<string, ReportParameterValue> resolvedValues,
        CultureInfo culture,
        CultureInfo uiCulture,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = request.ReportDefinition,
            DataSetId = parameter.AvailableValuesDataSetId!,
            ProviderRegistry = request.ProviderRegistry,
            HostDataRegistry = request.HostDataRegistry,
            Culture = culture,
            UiCulture = uiCulture,
            TimeZone = timeZone
        };

        foreach (var pair in resolvedValues)
        {
            executionRequest.ParameterValues[pair.Key] = pair.Value;
        }

        foreach (var pair in request.Globals)
        {
            executionRequest.Globals[pair.Key] = pair.Value;
        }

        var executionResult = await _dataSetExecutor.ExecuteAsync(executionRequest, cancellationToken);
        if (executionResult.DataSet is null)
        {
            return (null, executionResult.Diagnostics);
        }

        var values = new List<ReportParameterAvailableValue>();
        for (var rowIndex = 0; rowIndex < executionResult.DataSet.Rows.Count; rowIndex++)
        {
            var row = executionResult.DataSet.Rows[rowIndex];
            row.TryGetValue(parameter.ValueField ?? "Value", out var value);
            row.TryGetValue(parameter.LabelField ?? parameter.ValueField ?? "Label", out var labelValue);
            var label = ReportDataRuntimeHelpers.ToDisplayText(labelValue ?? value, culture) ?? string.Empty;
            if (values.Any(existing => ReportDataRuntimeHelpers.AreEqual(existing.Value, value, culture)))
            {
                continue;
            }

            values.Add(new ReportParameterAvailableValue
            {
                Value = value,
                Label = label
            });
        }

        return (values, executionResult.Diagnostics);
    }

    private static void EnsureParameterLabels(
        ReportParameterValue parameterValue,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(parameterValue);
        ArgumentNullException.ThrowIfNull(culture);

        if (parameterValue.IsNull)
        {
            parameterValue.Labels.Clear();
            return;
        }

        if (parameterValue.Labels.Count > parameterValue.Values.Count)
        {
            parameterValue.Labels.RemoveRange(parameterValue.Values.Count, parameterValue.Labels.Count - parameterValue.Values.Count);
        }

        while (parameterValue.Labels.Count < parameterValue.Values.Count)
        {
            var valueIndex = parameterValue.Labels.Count;
            parameterValue.Labels.Add(ReportDataRuntimeHelpers.ToDisplayText(parameterValue.Values[valueIndex], culture) ?? string.Empty);
        }
    }

    private static void ApplyAvailableValueLabels(
        ReportParameterValue parameterValue,
        IReadOnlyList<ReportParameterAvailableValue> availableValues,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(parameterValue);
        ArgumentNullException.ThrowIfNull(availableValues);
        ArgumentNullException.ThrowIfNull(culture);

        EnsureParameterLabels(parameterValue, culture);
        if (parameterValue.IsNull || parameterValue.Values.Count == 0 || availableValues.Count == 0)
        {
            return;
        }

        for (var valueIndex = 0; valueIndex < parameterValue.Values.Count; valueIndex++)
        {
            var resolvedValue = parameterValue.Values[valueIndex];
            for (var availableIndex = 0; availableIndex < availableValues.Count; availableIndex++)
            {
                var availableValue = availableValues[availableIndex];
                if (!ReportDataRuntimeHelpers.AreEqual(availableValue.Value, resolvedValue, culture))
                {
                    continue;
                }

                parameterValue.Labels[valueIndex] = availableValue.Label;
                break;
            }
        }
    }

    private static ReportParameterValue ValidateResolvedValueAgainstAvailableValues(
        ReportParameterDefinition parameter,
        ReportParameterValue resolvedValue,
        bool usedSuppliedValue,
        IReadOnlyList<ReportParameterAvailableValue> availableValues,
        CultureInfo culture,
        List<ReportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(resolvedValue);
        ArgumentNullException.ThrowIfNull(availableValues);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (resolvedValue.IsNull || resolvedValue.Values.Count == 0 || availableValues.Count == 0)
        {
            return resolvedValue;
        }

        for (var valueIndex = 0; valueIndex < resolvedValue.Values.Count; valueIndex++)
        {
            var value = resolvedValue.Values[valueIndex];
            var isMatched = false;
            for (var availableIndex = 0; availableIndex < availableValues.Count; availableIndex++)
            {
                if (!ReportDataRuntimeHelpers.AreEqual(availableValues[availableIndex].Value, value, culture))
                {
                    continue;
                }

                isMatched = true;
                break;
            }

            if (isMatched)
            {
                continue;
            }

            if (usedSuppliedValue || parameter.Visibility != ReportParameterVisibility.Visible)
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ParameterResolutionFailed,
                    $"Parameter '{parameter.Id}' resolved to '{ReportDataRuntimeHelpers.ToDisplayText(value, culture) ?? string.Empty}', which is not one of the available values.",
                    $"$.parameters[{parameter.Id}]"));
                return resolvedValue;
            }

            return new ReportParameterValue
            {
                IsNull = true
            };
        }

        return resolvedValue;
    }

    private static List<ReportParameterDefinition> OrderParameters(
        IReadOnlyList<ReportParameterDefinition> parameters,
        List<ReportDiagnostic> diagnostics)
    {
        var parametersById = new Dictionary<string, ReportParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            parametersById[parameters[index].Id] = parameters[index];
        }

        var ordered = new List<ReportParameterDefinition>(parameters.Count);
        var state = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            Visit(parameters[index], parametersById, state, ordered, diagnostics);
        }

        return ordered;
    }

    private static void Visit(
        ReportParameterDefinition parameter,
        IReadOnlyDictionary<string, ReportParameterDefinition> parametersById,
        IDictionary<string, VisitState> state,
        ICollection<ReportParameterDefinition> ordered,
        ICollection<ReportDiagnostic> diagnostics)
    {
        if (state.TryGetValue(parameter.Id, out var visitState))
        {
            if (visitState == VisitState.Visited)
            {
                return;
            }

            if (visitState == VisitState.Visiting)
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ParameterResolutionFailed,
                    $"Parameter dependency cycle detected at '{parameter.Id}'.",
                    $"$.parameters[{parameter.Id}]"));
            }

            return;
        }

        state[parameter.Id] = VisitState.Visiting;
        for (var dependencyIndex = 0; dependencyIndex < parameter.Dependencies.Count; dependencyIndex++)
        {
            var dependencyId = parameter.Dependencies[dependencyIndex];
            if (!parametersById.TryGetValue(dependencyId, out var dependency))
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ParameterResolutionFailed,
                    $"Parameter '{parameter.Id}' depends on unknown parameter '{dependencyId}'.",
                    $"$.parameters[{parameter.Id}]"));
                continue;
            }

            Visit(dependency, parametersById, state, ordered, diagnostics);
        }

        state[parameter.Id] = VisitState.Visited;
        ordered.Add(parameter);
    }

    private static ReportDiagnostic CloneDiagnostic(ReportDiagnostic diagnostic, string path)
    {
        return new ReportDiagnostic(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            path);
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }
}
