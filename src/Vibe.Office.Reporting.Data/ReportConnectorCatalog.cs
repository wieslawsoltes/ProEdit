namespace Vibe.Office.Reporting.Data;

/// <summary>
/// High-level connector categories exposed to reporting, documents, and tooling.
/// </summary>
public enum ReportDataConnectorCategory
{
    /// <summary>
    /// In-memory object or host-provided data.
    /// </summary>
    InMemory,

    /// <summary>
    /// File or payload-based data.
    /// </summary>
    File,

    /// <summary>
    /// Relational or analytical databases.
    /// </summary>
    Database,

    /// <summary>
    /// HTTP-backed online services.
    /// </summary>
    Api
}

/// <summary>
/// Declares the capabilities of one connector type.
/// </summary>
[Flags]
public enum ReportDataConnectorCapabilities
{
    /// <summary>
    /// No declared capabilities.
    /// </summary>
    None = 0,

    /// <summary>
    /// Supports parameterized queries or commands.
    /// </summary>
    Parameters = 1 << 0,

    /// <summary>
    /// Supports explicit query/command execution.
    /// </summary>
    Query = 1 << 1,

    /// <summary>
    /// Supports schema discovery or typed field materialization.
    /// </summary>
    Schema = 1 << 2,

    /// <summary>
    /// Supports paging or server-side result slicing.
    /// </summary>
    Paging = 1 << 3,

    /// <summary>
    /// Supports host-managed authentication or connection secrets.
    /// </summary>
    Authentication = 1 << 4
}

/// <summary>
/// Describes one available connector type.
/// </summary>
public sealed class ReportDataConnectorDefinition
{
    /// <summary>
    /// Gets or sets the connector/provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the high-level category.
    /// </summary>
    public ReportDataConnectorCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the declared connector capabilities.
    /// </summary>
    public ReportDataConnectorCapabilities Capabilities { get; set; } =
        ReportDataConnectorCapabilities.Query | ReportDataConnectorCapabilities.Parameters;

    /// <summary>
    /// Gets or sets the default ADO.NET provider invariant name when applicable.
    /// </summary>
    public string? ProviderInvariantName { get; set; }

    /// <summary>
    /// Gets or sets the short description.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Represents one reusable named connection definition.
/// </summary>
public sealed class ReportConnectionDefinition
{
    /// <summary>
    /// Gets or sets the logical connection name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connector/provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ADO.NET provider invariant name when applicable.
    /// </summary>
    public string? ProviderInvariantName { get; set; }

    /// <summary>
    /// Gets or sets the database connection string when applicable.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the base address for HTTP-backed connectors when applicable.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the optional display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets the connection-scoped options.
    /// </summary>
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the default HTTP headers.
    /// </summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a deep clone of the connection definition.
    /// </summary>
    /// <returns>The cloned connection definition.</returns>
    public ReportConnectionDefinition Clone()
    {
        var clone = new ReportConnectionDefinition
        {
            Name = Name,
            ProviderId = ProviderId,
            ProviderInvariantName = ProviderInvariantName,
            ConnectionString = ConnectionString,
            BaseAddress = BaseAddress,
            DisplayName = DisplayName
        };

        foreach (var pair in Options)
        {
            clone.Options[pair.Key] = pair.Value;
        }

        foreach (var pair in Headers)
        {
            clone.Headers[pair.Key] = pair.Value;
        }

        return clone;
    }
}

/// <summary>
/// Stores connector definitions available to tooling and host composition.
/// </summary>
public sealed class ReportDataConnectorCatalog
{
    private readonly Dictionary<string, ReportDataConnectorDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers one connector definition.
    /// </summary>
    /// <param name="definition">The connector definition.</param>
    public void Register(ReportDataConnectorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ProviderId);
        _definitions[definition.ProviderId] = definition;
    }

    /// <summary>
    /// Attempts to resolve one connector definition.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="definition">Receives the connector definition.</param>
    /// <returns><see langword="true" /> when the connector exists; otherwise <see langword="false" />.</returns>
    public bool TryGetConnector(string providerId, out ReportDataConnectorDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _definitions.TryGetValue(providerId, out definition!);
    }

    /// <summary>
    /// Lists all registered connector definitions.
    /// </summary>
    /// <returns>The registered connector definitions.</returns>
    public IReadOnlyList<ReportDataConnectorDefinition> ListConnectors()
    {
        return _definitions.Values
            .OrderBy(static definition => definition.Category)
            .ThenBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Creates a catalog populated with the built-in connector definitions.
    /// </summary>
    /// <returns>The populated catalog.</returns>
    public static ReportDataConnectorCatalog CreateDefault()
    {
        var catalog = new ReportDataConnectorCatalog();
        foreach (var definition in ReportConnectorDefaults.GetBuiltInConnectors())
        {
            catalog.Register(definition);
        }

        return catalog;
    }
}

/// <summary>
/// Provides the built-in connector definitions and provider invariant mappings.
/// </summary>
public static class ReportConnectorDefaults
{
    /// <summary>
    /// Returns the built-in connector definitions.
    /// </summary>
    /// <returns>The connector definitions.</returns>
    public static IReadOnlyList<ReportDataConnectorDefinition> GetBuiltInConnectors()
    {
        return
        [
            Create(ReportProviderIds.InMemory, "In-Memory Objects", ReportDataConnectorCategory.InMemory, null, "Host-registered object graphs and normalized rows."),
            Create(ReportProviderIds.Json, "JSON Payload", ReportDataConnectorCategory.File, null, "Embedded or host-registered JSON payloads."),
            Create(ReportProviderIds.Csv, "CSV Payload", ReportDataConnectorCategory.File, null, "Embedded or host-registered CSV payloads."),
            Create(ReportProviderIds.Sql, "Host SQL Connector", ReportDataConnectorCategory.Database, null, "Host-supplied custom SQL execution adapters."),
            Create(ReportProviderIds.SqlServer, "SQL Server", ReportDataConnectorCategory.Database, "Microsoft.Data.SqlClient", "Parameterized ADO.NET execution for Microsoft SQL Server."),
            Create(ReportProviderIds.PostgreSql, "PostgreSQL", ReportDataConnectorCategory.Database, "Npgsql", "Parameterized ADO.NET execution for PostgreSQL."),
            Create(ReportProviderIds.MySql, "MySQL", ReportDataConnectorCategory.Database, "MySqlConnector", "Parameterized ADO.NET execution for MySQL."),
            Create(ReportProviderIds.MariaDb, "MariaDB", ReportDataConnectorCategory.Database, "MySqlConnector", "Parameterized ADO.NET execution for MariaDB."),
            Create(ReportProviderIds.Oracle, "Oracle Database", ReportDataConnectorCategory.Database, "Oracle.ManagedDataAccess.Client", "Parameterized ADO.NET execution for Oracle Database."),
            Create(ReportProviderIds.Sqlite, "SQLite", ReportDataConnectorCategory.Database, "Microsoft.Data.Sqlite", "Parameterized ADO.NET execution for SQLite."),
            Create(ReportProviderIds.Snowflake, "Snowflake", ReportDataConnectorCategory.Database, "Snowflake.Data.Client", "Parameterized ADO.NET execution for Snowflake."),
            Create(ReportProviderIds.Odbc, "ODBC", ReportDataConnectorCategory.Database, "System.Data.Odbc", "Parameterized ADO.NET execution through ODBC."),
            Create(ReportProviderIds.RestJson, "REST JSON", ReportDataConnectorCategory.Api, null, "HTTP JSON endpoints with host-managed authentication and query-string parameters."),
            Create(ReportProviderIds.OData, "OData", ReportDataConnectorCategory.Api, null, "HTTP OData endpoints returning JSON value collections."),
            Create(ReportProviderIds.GraphQl, "GraphQL", ReportDataConnectorCategory.Api, null, "HTTP GraphQL endpoints with query text and variables.")
        ];
    }

    /// <summary>
    /// Attempts to resolve the default provider invariant name for one connector/provider identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="providerInvariantName">Receives the invariant name.</param>
    /// <returns><see langword="true" /> when a default invariant name exists; otherwise <see langword="false" />.</returns>
    public static bool TryGetProviderInvariantName(string providerId, out string providerInvariantName)
    {
        switch (providerId)
        {
            case ReportProviderIds.SqlServer:
                providerInvariantName = "Microsoft.Data.SqlClient";
                return true;
            case ReportProviderIds.PostgreSql:
                providerInvariantName = "Npgsql";
                return true;
            case ReportProviderIds.MySql:
            case ReportProviderIds.MariaDb:
                providerInvariantName = "MySqlConnector";
                return true;
            case ReportProviderIds.Oracle:
                providerInvariantName = "Oracle.ManagedDataAccess.Client";
                return true;
            case ReportProviderIds.Sqlite:
                providerInvariantName = "Microsoft.Data.Sqlite";
                return true;
            case ReportProviderIds.Snowflake:
                providerInvariantName = "Snowflake.Data.Client";
                return true;
            case ReportProviderIds.Odbc:
                providerInvariantName = "System.Data.Odbc";
                return true;
            default:
                providerInvariantName = string.Empty;
                return false;
        }
    }

    private static ReportDataConnectorDefinition Create(
        string providerId,
        string displayName,
        ReportDataConnectorCategory category,
        string? providerInvariantName,
        string description)
    {
        return new ReportDataConnectorDefinition
        {
            ProviderId = providerId,
            DisplayName = displayName,
            Category = category,
            ProviderInvariantName = providerInvariantName,
            Description = description,
            Capabilities = category switch
            {
                ReportDataConnectorCategory.Api => ReportDataConnectorCapabilities.Query
                    | ReportDataConnectorCapabilities.Parameters
                    | ReportDataConnectorCapabilities.Schema
                    | ReportDataConnectorCapabilities.Authentication,
                ReportDataConnectorCategory.Database => ReportDataConnectorCapabilities.Query
                    | ReportDataConnectorCapabilities.Parameters
                    | ReportDataConnectorCapabilities.Schema
                    | ReportDataConnectorCapabilities.Authentication,
                _ => ReportDataConnectorCapabilities.Query
                    | ReportDataConnectorCapabilities.Schema
            }
        };
    }
}
