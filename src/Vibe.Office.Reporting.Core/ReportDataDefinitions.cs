namespace Vibe.Office.Reporting;

/// <summary>
/// Defines a report data source.
/// </summary>
public sealed class ReportDataSourceDefinition
{
    /// <summary>
    /// Gets or sets the data source identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the logical connection name.
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Gets the provider options.
    /// </summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the credential mode.
    /// </summary>
    public ReportCredentialMode CredentialMode { get; set; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>
/// Credential handling mode for a report data source.
/// </summary>
public enum ReportCredentialMode
{
    /// <summary>
    /// No explicit credential handling.
    /// </summary>
    None,

    /// <summary>
    /// Credentials are provided by the host.
    /// </summary>
    Host,

    /// <summary>
    /// Credentials are prompted from the user.
    /// </summary>
    Prompt
}

/// <summary>
/// Defines a report dataset.
/// </summary>
public sealed class ReportDataSetDefinition
{
    /// <summary>
    /// Gets or sets the dataset identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the backing data source identifier.
    /// </summary>
    public string DataSourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the query or logical command.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets the dataset parameters.
    /// </summary>
    public List<ReportDataSetParameterDefinition> Parameters { get; set; } = new();

    /// <summary>
    /// Gets the calculated fields.
    /// </summary>
    public List<ReportCalculatedFieldDefinition> CalculatedFields { get; set; } = new();

    /// <summary>
    /// Gets the dataset filters.
    /// </summary>
    public List<ReportFilterDefinition> Filters { get; set; } = new();

    /// <summary>
    /// Gets the dataset sorts.
    /// </summary>
    public List<ReportSortDefinition> Sorts { get; set; } = new();

    /// <summary>
    /// Gets the expected fields.
    /// </summary>
    public List<ReportFieldDefinition> ExpectedFields { get; set; } = new();
}

/// <summary>
/// Defines a dataset parameter.
/// </summary>
public sealed class ReportDataSetParameterDefinition
{
    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value expression.
    /// </summary>
    public string ValueExpression { get; set; } = string.Empty;
}

/// <summary>
/// Defines a calculated dataset field.
/// </summary>
public sealed class ReportCalculatedFieldDefinition
{
    /// <summary>
    /// Gets or sets the field name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field expression.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field data type.
    /// </summary>
    public ReportParameterDataType DataType { get; set; } = ReportParameterDataType.String;
}

/// <summary>
/// Defines a dataset filter.
/// </summary>
public sealed class ReportFilterDefinition
{
    /// <summary>
    /// Gets or sets the filter expression.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filter operator.
    /// </summary>
    public ReportFilterOperator Operator { get; set; } = ReportFilterOperator.Equal;

    /// <summary>
    /// Gets or sets the filter value expression.
    /// </summary>
    public string ValueExpression { get; set; } = string.Empty;
}

/// <summary>
/// Supported filter operators.
/// </summary>
public enum ReportFilterOperator
{
    /// <summary>
    /// Equality operator.
    /// </summary>
    Equal,

    /// <summary>
    /// Inequality operator.
    /// </summary>
    NotEqual,

    /// <summary>
    /// Greater-than operator.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater-than-or-equal operator.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less-than operator.
    /// </summary>
    LessThan,

    /// <summary>
    /// Less-than-or-equal operator.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Contains operator.
    /// </summary>
    Contains
}

/// <summary>
/// Defines a dataset sort.
/// </summary>
public sealed class ReportSortDefinition
{
    /// <summary>
    /// Gets or sets the sort expression.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort direction.
    /// </summary>
    public ReportSortDirection Direction { get; set; } = ReportSortDirection.Ascending;
}

/// <summary>
/// Supported sort directions.
/// </summary>
public enum ReportSortDirection
{
    /// <summary>
    /// Ascending order.
    /// </summary>
    Ascending,

    /// <summary>
    /// Descending order.
    /// </summary>
    Descending
}

/// <summary>
/// Defines an expected dataset field.
/// </summary>
public sealed class ReportFieldDefinition
{
    /// <summary>
    /// Gets or sets the field name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field data type.
    /// </summary>
    public ReportParameterDataType DataType { get; set; } = ReportParameterDataType.String;
}
