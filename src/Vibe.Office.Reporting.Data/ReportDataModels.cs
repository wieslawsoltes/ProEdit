using System.Data.Common;
using System.Globalization;
using System.Net.Http;

namespace Vibe.Office.Reporting.Data;

/// <summary>
/// Well-known built-in report data provider identifiers.
/// </summary>
public static class ReportProviderIds
{
    /// <summary>
    /// Provider identifier for in-memory data.
    /// </summary>
    public const string InMemory = "in-memory";

    /// <summary>
    /// Provider identifier for JSON data.
    /// </summary>
    public const string Json = "json";

    /// <summary>
    /// Provider identifier for CSV data.
    /// </summary>
    public const string Csv = "csv";

    /// <summary>
    /// Provider identifier for host-registered SQL connectors.
    /// </summary>
    public const string Sql = "sql";

    /// <summary>
    /// Provider identifier for Microsoft SQL Server.
    /// </summary>
    public const string SqlServer = "sqlserver";

    /// <summary>
    /// Provider identifier for PostgreSQL.
    /// </summary>
    public const string PostgreSql = "postgresql";

    /// <summary>
    /// Provider identifier for MySQL.
    /// </summary>
    public const string MySql = "mysql";

    /// <summary>
    /// Provider identifier for MariaDB.
    /// </summary>
    public const string MariaDb = "mariadb";

    /// <summary>
    /// Provider identifier for Oracle Database.
    /// </summary>
    public const string Oracle = "oracle";

    /// <summary>
    /// Provider identifier for SQLite.
    /// </summary>
    public const string Sqlite = "sqlite";

    /// <summary>
    /// Provider identifier for Snowflake.
    /// </summary>
    public const string Snowflake = "snowflake";

    /// <summary>
    /// Provider identifier for ODBC-backed data sources.
    /// </summary>
    public const string Odbc = "odbc";

    /// <summary>
    /// Provider identifier for HTTP REST JSON endpoints.
    /// </summary>
    public const string RestJson = "rest-json";

    /// <summary>
    /// Provider identifier for OData JSON endpoints.
    /// </summary>
    public const string OData = "odata";

    /// <summary>
    /// Provider identifier for GraphQL endpoints.
    /// </summary>
    public const string GraphQl = "graphql";
}

/// <summary>
/// Represents one normalized data record.
/// </summary>
public sealed class ReportDataRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDataRecord" /> class.
    /// </summary>
    public ReportDataRecord()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDataRecord" /> class.
    /// </summary>
    /// <param name="values">The initial record values.</param>
    public ReportDataRecord(IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var pair in values)
        {
            Values[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Gets the record values.
    /// </summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to read a field value.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="value">Receives the field value.</param>
    /// <returns><see langword="true" /> when the field exists; otherwise <see langword="false" />.</returns>
    public bool TryGetValue(string fieldName, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return Values.TryGetValue(fieldName, out value);
    }
}

/// <summary>
/// Represents one normalized dataset.
/// </summary>
public sealed class ReportDataTable
{
    /// <summary>
    /// Gets or sets the dataset identifier.
    /// </summary>
    public string DataSetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the normalized field definitions.
    /// </summary>
    public List<ReportFieldDefinition> Fields { get; } = new();

    /// <summary>
    /// Gets the normalized data records.
    /// </summary>
    public List<ReportDataRecord> Rows { get; } = new();
}

/// <summary>
/// Reads in-memory tabular data for reporting.
/// </summary>
public interface IReportInMemoryDataSource
{
    /// <summary>
    /// Reads the normalized data table.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized data table.</returns>
    ValueTask<ReportDataTable> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory data source backed by already-normalized dictionaries.
/// </summary>
public sealed class ReportDictionaryDataSource : IReportInMemoryDataSource
{
    private readonly IReadOnlyList<ReportFieldDefinition> _fields;
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDictionaryDataSource" /> class.
    /// </summary>
    /// <param name="rows">The source rows.</param>
    /// <param name="fields">Optional explicit field definitions.</param>
    public ReportDictionaryDataSource(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<ReportFieldDefinition>? fields = null)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _fields = fields ?? Array.Empty<ReportFieldDefinition>();
    }

    /// <inheritdoc />
    public ValueTask<ReportDataTable> ReadAsync(CancellationToken cancellationToken = default)
    {
        var table = new ReportDataTable();
        for (var index = 0; index < _fields.Count; index++)
        {
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = _fields[index].Name,
                DataType = _fields[index].DataType
            });
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            table.Rows.Add(new ReportDataRecord(_rows[index]));
        }

        return ValueTask.FromResult(table);
    }
}

/// <summary>
/// Defines one explicit field accessor for typed in-memory objects.
/// </summary>
/// <typeparam name="T">The object type.</typeparam>
/// <param name="Name">The field name.</param>
/// <param name="Getter">The field getter.</param>
/// <param name="DataType">The field data type.</param>
public readonly record struct ReportObjectFieldAccessor<T>(
    string Name,
    Func<T, object?> Getter,
    ReportParameterDataType DataType = ReportParameterDataType.String);

/// <summary>
/// In-memory data source backed by typed objects and explicit field accessors.
/// </summary>
/// <typeparam name="T">The object type.</typeparam>
public sealed class ReportObjectDataSource<T> : IReportInMemoryDataSource
{
    private readonly IReadOnlyList<ReportObjectFieldAccessor<T>> _fields;
    private readonly IReadOnlyList<T> _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportObjectDataSource{T}" /> class.
    /// </summary>
    /// <param name="items">The source items.</param>
    /// <param name="fields">The explicit field accessors.</param>
    public ReportObjectDataSource(
        IReadOnlyList<T> items,
        IReadOnlyList<ReportObjectFieldAccessor<T>> fields)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <inheritdoc />
    public ValueTask<ReportDataTable> ReadAsync(CancellationToken cancellationToken = default)
    {
        var table = new ReportDataTable();
        for (var fieldIndex = 0; fieldIndex < _fields.Count; fieldIndex++)
        {
            var field = _fields[fieldIndex];
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = field.Name,
                DataType = field.DataType
            });
        }

        for (var itemIndex = 0; itemIndex < _items.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = _items[itemIndex];
            var record = new ReportDataRecord();
            for (var fieldIndex = 0; fieldIndex < _fields.Count; fieldIndex++)
            {
                var accessor = _fields[fieldIndex];
                record.Values[accessor.Name] = accessor.Getter(item);
            }

            table.Rows.Add(record);
        }

        return ValueTask.FromResult(table);
    }
}

/// <summary>
/// Represents a host-registered SQL connector.
/// </summary>
public interface IReportSqlConnector
{
    /// <summary>
    /// Executes the supplied query request.
    /// </summary>
    /// <param name="request">The query request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized data table.</returns>
    ValueTask<ReportDataTable> ExecuteAsync(
        ReportSqlQueryRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents one SQL query request emitted by the reporting runtime.
/// </summary>
public sealed class ReportSqlQueryRequest
{
    /// <summary>
    /// Gets or sets the logical connection name.
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Gets or sets the query text.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets the resolved query parameters.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>
/// Provides runtime data context to one provider execution.
/// </summary>
public sealed class ReportDataProviderContext
{
    private static readonly IReadOnlyDictionary<string, ReportParameterValue> EmptyParameters =
        new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the resolved report parameters.
    /// </summary>
    public IReadOnlyDictionary<string, ReportParameterValue> Parameters { get; set; } = EmptyParameters;

    /// <summary>
    /// Gets or sets the resolved dataset parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object?> DataSetParameters { get; set; } = EmptyValues;

    /// <summary>
    /// Gets or sets the host data registry.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

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
}

/// <summary>
/// Reads one dataset from a specific provider type.
/// </summary>
public interface IReportDataProvider
{
    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Executes the dataset query.
    /// </summary>
    /// <param name="dataSource">The report data source definition.</param>
    /// <param name="dataSet">The report dataset definition.</param>
    /// <param name="context">The runtime provider context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized data table.</returns>
    ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores registered data providers.
/// </summary>
public sealed class ReportDataProviderRegistry
{
    private readonly Dictionary<string, IReportDataProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers one provider instance.
    /// </summary>
    /// <param name="provider">The provider instance.</param>
    public void Register(IReportDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.ProviderId] = provider;
    }

    /// <summary>
    /// Attempts to resolve a provider by identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="provider">Receives the resolved provider.</param>
    /// <returns><see langword="true" /> when the provider exists; otherwise <see langword="false" />.</returns>
    public bool TryGetProvider(string providerId, out IReportDataProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out provider!);
    }
}

/// <summary>
/// Stores host-registered data sources and connectors consumed by built-in providers.
/// </summary>
public sealed class ReportHostDataRegistry
{
    private readonly Dictionary<string, IReportInMemoryDataSource> _inMemorySources =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _jsonSources =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _csvSources =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IReportSqlConnector> _sqlConnectors =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ReportConnectionDefinition> _connections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DbProviderFactory> _dbProviderFactories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HttpClient> _httpClients =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an in-memory source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="source">The source instance.</param>
    public void RegisterInMemorySource(string key, IReportInMemoryDataSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(source);
        _inMemorySources[key] = source;
    }

    /// <summary>
    /// Registers a JSON payload source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="json">The JSON payload.</param>
    public void RegisterJsonSource(string key, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(json);
        _jsonSources[key] = json;
    }

    /// <summary>
    /// Registers a CSV payload source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="csv">The CSV payload.</param>
    public void RegisterCsvSource(string key, string csv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(csv);
        _csvSources[key] = csv;
    }

    /// <summary>
    /// Registers a SQL connector.
    /// </summary>
    /// <param name="key">The connector key.</param>
    /// <param name="connector">The connector instance.</param>
    public void RegisterSqlConnector(string key, IReportSqlConnector connector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(connector);
        _sqlConnectors[key] = connector;
    }

    /// <summary>
    /// Registers one named connection definition.
    /// </summary>
    /// <param name="connection">The connection definition.</param>
    public void RegisterConnection(ReportConnectionDefinition connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Name);
        _connections[connection.Name] = connection.Clone();
    }

    /// <summary>
    /// Registers one provider factory used for ADO.NET-backed connectors.
    /// </summary>
    /// <param name="providerInvariantName">The provider invariant name.</param>
    /// <param name="factory">The provider factory.</param>
    public void RegisterDbProviderFactory(string providerInvariantName, DbProviderFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerInvariantName);
        ArgumentNullException.ThrowIfNull(factory);
        _dbProviderFactories[providerInvariantName] = factory;
    }

    /// <summary>
    /// Registers one named HTTP client.
    /// </summary>
    /// <param name="key">The client key.</param>
    /// <param name="httpClient">The HTTP client.</param>
    public void RegisterHttpClient(string key, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClients[key] = httpClient;
    }

    /// <summary>
    /// Attempts to resolve an in-memory source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="source">Receives the source instance.</param>
    /// <returns><see langword="true" /> when the source exists; otherwise <see langword="false" />.</returns>
    public bool TryGetInMemorySource(string key, out IReportInMemoryDataSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _inMemorySources.TryGetValue(key, out source!);
    }

    /// <summary>
    /// Attempts to resolve a JSON payload source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="json">Receives the JSON payload.</param>
    /// <returns><see langword="true" /> when the source exists; otherwise <see langword="false" />.</returns>
    public bool TryGetJsonSource(string key, out string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _jsonSources.TryGetValue(key, out json!);
    }

    /// <summary>
    /// Attempts to resolve a CSV payload source.
    /// </summary>
    /// <param name="key">The source key.</param>
    /// <param name="csv">Receives the CSV payload.</param>
    /// <returns><see langword="true" /> when the source exists; otherwise <see langword="false" />.</returns>
    public bool TryGetCsvSource(string key, out string csv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _csvSources.TryGetValue(key, out csv!);
    }

    /// <summary>
    /// Attempts to resolve a SQL connector.
    /// </summary>
    /// <param name="key">The connector key.</param>
    /// <param name="connector">Receives the connector instance.</param>
    /// <returns><see langword="true" /> when the connector exists; otherwise <see langword="false" />.</returns>
    public bool TryGetSqlConnector(string key, out IReportSqlConnector connector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _sqlConnectors.TryGetValue(key, out connector!);
    }

    /// <summary>
    /// Attempts to resolve one named connection definition.
    /// </summary>
    /// <param name="name">The connection name.</param>
    /// <param name="connection">Receives the connection definition.</param>
    /// <returns><see langword="true" /> when the connection exists; otherwise <see langword="false" />.</returns>
    public bool TryGetConnection(string name, out ReportConnectionDefinition connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_connections.TryGetValue(name, out var stored))
        {
            connection = stored.Clone();
            return true;
        }

        connection = null!;
        return false;
    }

    /// <summary>
    /// Attempts to resolve one registered ADO.NET provider factory.
    /// </summary>
    /// <param name="providerInvariantName">The provider invariant name.</param>
    /// <param name="factory">Receives the factory.</param>
    /// <returns><see langword="true" /> when the factory exists; otherwise <see langword="false" />.</returns>
    public bool TryGetDbProviderFactory(string providerInvariantName, out DbProviderFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerInvariantName);
        return _dbProviderFactories.TryGetValue(providerInvariantName, out factory!);
    }

    /// <summary>
    /// Attempts to resolve one named HTTP client.
    /// </summary>
    /// <param name="key">The client key.</param>
    /// <param name="httpClient">Receives the HTTP client.</param>
    /// <returns><see langword="true" /> when the client exists; otherwise <see langword="false" />.</returns>
    public bool TryGetHttpClient(string key, out HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _httpClients.TryGetValue(key, out httpClient!);
    }
}
