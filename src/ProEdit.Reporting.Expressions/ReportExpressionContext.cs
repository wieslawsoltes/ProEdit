using System.Globalization;

namespace ProEdit.Reporting.Expressions;

/// <summary>
/// Supported evaluation scope kinds.
/// </summary>
public enum ReportExpressionScopeKind
{
    /// <summary>
    /// Report scope.
    /// </summary>
    Report,

    /// <summary>
    /// Section scope.
    /// </summary>
    Section,

    /// <summary>
    /// Group scope.
    /// </summary>
    Group,

    /// <summary>
    /// Row scope.
    /// </summary>
    Row,

    /// <summary>
    /// Page scope.
    /// </summary>
    Page
}

/// <summary>
/// Provides runtime values for report expression evaluation.
/// </summary>
public sealed class ReportExpressionContext
{
    private static readonly IReadOnlyDictionary<string, ReportParameterValue> EmptyParameters =
        new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyNamedScopes =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the resolved report parameters.
    /// </summary>
    public IReadOnlyDictionary<string, ReportParameterValue> Parameters { get; set; } = EmptyParameters;

    /// <summary>
    /// Gets or sets the current row fields.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields { get; set; } = EmptyValues;

    /// <summary>
    /// Gets or sets the report/global values.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Globals { get; set; } = EmptyValues;

    /// <summary>
    /// Gets or sets the rows visible to aggregate functions in the current scope.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ScopeRows { get; set; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    /// <summary>
    /// Gets or sets the rows visible to the parent aggregate scope.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ParentScopeRows { get; set; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    /// <summary>
    /// Gets or sets the named aggregate scopes available to the current expression.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> NamedScopes { get; set; } = EmptyNamedScopes;

    /// <summary>
    /// Gets or sets the execution culture.
    /// </summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets or sets the UI culture.
    /// </summary>
    public CultureInfo UiCulture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets or sets the execution time zone.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Gets or sets the active scope kind.
    /// </summary>
    public ReportExpressionScopeKind ScopeKind { get; set; } = ReportExpressionScopeKind.Row;

    /// <summary>
    /// Gets or sets the active scope name.
    /// </summary>
    public string? ScopeName { get; set; }

    /// <summary>
    /// Gets or sets the current row index within the scope.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// Gets or sets the current item value for Me/CurrentValue expressions.
    /// </summary>
    public object? SelfValue { get; set; }

    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the total page count.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets a scalar parameter value by identifier.
    /// </summary>
    /// <param name="parameterId">The parameter identifier.</param>
    /// <returns>The scalar parameter value or <see langword="null" />.</returns>
    public object? GetParameterValue(string parameterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterId);

        return Parameters.TryGetValue(parameterId, out var parameterValue)
            ? parameterValue.GetScalarValue()
            : null;
    }

    /// <summary>
    /// Creates a child context for another row in the same scope.
    /// </summary>
    /// <param name="fields">The row fields.</param>
    /// <param name="rowIndex">The row index.</param>
    /// <param name="scopeRows">The scope rows.</param>
    /// <returns>The child context.</returns>
    public ReportExpressionContext CreateChild(
        IReadOnlyDictionary<string, object?> fields,
        int rowIndex,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? scopeRows = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return new ReportExpressionContext
        {
            Parameters = Parameters,
            Fields = fields,
            Globals = Globals,
            ScopeRows = scopeRows ?? ScopeRows,
            ParentScopeRows = ParentScopeRows,
            NamedScopes = NamedScopes,
            Culture = Culture,
            UiCulture = UiCulture,
            TimeZone = TimeZone,
            ScopeKind = ScopeKind,
            ScopeName = ScopeName,
            RowIndex = rowIndex,
            SelfValue = SelfValue,
            PageNumber = PageNumber,
            TotalPages = TotalPages
        };
    }

    /// <summary>
    /// Creates a copy of the current context with an updated current-item value.
    /// </summary>
    /// <param name="selfValue">The current item value.</param>
    /// <returns>The cloned context.</returns>
    public ReportExpressionContext CreateWithSelfValue(object? selfValue)
    {
        return new ReportExpressionContext
        {
            Parameters = Parameters,
            Fields = Fields,
            Globals = Globals,
            ScopeRows = ScopeRows,
            ParentScopeRows = ParentScopeRows,
            NamedScopes = NamedScopes,
            Culture = Culture,
            UiCulture = UiCulture,
            TimeZone = TimeZone,
            ScopeKind = ScopeKind,
            ScopeName = ScopeName,
            RowIndex = RowIndex,
            SelfValue = selfValue,
            PageNumber = PageNumber,
            TotalPages = TotalPages
        };
    }
}
