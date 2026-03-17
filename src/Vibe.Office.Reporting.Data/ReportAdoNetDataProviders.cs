using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Vibe.Office.Reporting.Data;

internal sealed class SqlReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.Sql;

    public async ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var connectorKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (context.HostDataRegistry.TryGetSqlConnector(connectorKey, out var connector))
        {
            var request = new ReportSqlQueryRequest
            {
                ConnectionName = connectorKey,
                Query = dataSet.Query,
                TimeoutSeconds = dataSource.TimeoutSeconds
            };

            foreach (var parameter in context.DataSetParameters)
            {
                request.Parameters[parameter.Key] = parameter.Value;
            }

            var table = await connector.ExecuteAsync(request, cancellationToken);
            table.DataSetId = dataSet.Id;
            return table;
        }

        return await ReportAdoNetDataProviderSupport.ExecuteAsync(
            dataSource,
            dataSet,
            context,
            cancellationToken);
    }
}

internal sealed class AdoNetReportDataProvider : IReportDataProvider
{
    public AdoNetReportDataProvider(string providerId)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }

    public string ProviderId { get; }

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        return ReportAdoNetDataProviderSupport.ExecuteAsync(
            dataSource,
            dataSet,
            context,
            cancellationToken);
    }
}

internal static class ReportAdoNetDataProviderSupport
{
    public static async ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken)
    {
        var connection = ResolveConnectionDefinition(dataSource, context);
        var providerInvariantName = ResolveProviderInvariantName(dataSource, connection);
        var connectionString = ResolveConnectionString(dataSource, connection);

        var factory = ResolveDbProviderFactory(providerInvariantName, context.HostDataRegistry);
        await using var dbConnection = factory.CreateConnection()
            ?? throw new InvalidOperationException($"Provider factory '{providerInvariantName}' did not create a connection instance.");
        dbConnection.ConnectionString = connectionString;
        await dbConnection.OpenAsync(cancellationToken);

        await using var command = dbConnection.CreateCommand();
        command.CommandText = dataSet.Query;
        command.CommandType = CommandType.Text;
        if (dataSource.TimeoutSeconds.HasValue)
        {
            command.CommandTimeout = dataSource.TimeoutSeconds.Value;
        }

        AddParameters(command, dataSet.Query, context.DataSetParameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadTableAsync(dataSet.Id, reader, context.Culture, cancellationToken);
    }

    private static ReportConnectionDefinition ResolveConnectionDefinition(
        ReportDataSourceDefinition dataSource,
        ReportDataProviderContext context)
    {
        var connectionKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (context.HostDataRegistry.TryGetConnection(connectionKey, out var connection))
        {
            if (!string.IsNullOrWhiteSpace(connection.ProviderId)
                && !dataSource.ProviderId.Equals(ReportProviderIds.Sql, StringComparison.OrdinalIgnoreCase)
                && !connection.ProviderId.Equals(dataSource.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Connection '{connectionKey}' is registered for provider '{connection.ProviderId}' but data source '{dataSource.Id}' requested '{dataSource.ProviderId}'.");
            }

            return connection;
        }

        var inlineConnection = new ReportConnectionDefinition
        {
            Name = connectionKey,
            ProviderId = dataSource.ProviderId,
            ProviderInvariantName = ReportDataProviderSupport.GetOption(dataSource, "providerInvariantName"),
            ConnectionString = ReportDataProviderSupport.GetOption(dataSource, "connectionString")
        };
        return inlineConnection;
    }

    private static string ResolveProviderInvariantName(
        ReportDataSourceDefinition dataSource,
        ReportConnectionDefinition connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.ProviderInvariantName))
        {
            return connection.ProviderInvariantName;
        }

        if (ReportConnectorDefaults.TryGetProviderInvariantName(dataSource.ProviderId, out var providerInvariantName))
        {
            return providerInvariantName;
        }

        throw new InvalidOperationException(
            $"Data source '{dataSource.Id}' does not define 'providerInvariantName' and provider '{dataSource.ProviderId}' has no default ADO.NET mapping.");
    }

    private static string ResolveConnectionString(
        ReportDataSourceDefinition dataSource,
        ReportConnectionDefinition connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.ConnectionString))
        {
            return connection.ConnectionString;
        }

        throw new InvalidOperationException(
            $"Data source '{dataSource.Id}' does not resolve to a connection string. Register a named connection or set the 'connectionString' option.");
    }

    private static DbProviderFactory ResolveDbProviderFactory(
        string providerInvariantName,
        ReportHostDataRegistry hostDataRegistry)
    {
        if (hostDataRegistry.TryGetDbProviderFactory(providerInvariantName, out var factory))
        {
            return factory;
        }

        try
        {
            return DbProviderFactories.GetFactory(providerInvariantName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ADO.NET provider factory '{providerInvariantName}' is not registered. Register it in the host data registry or via DbProviderFactories.",
                ex);
        }
    }

    private static void AddParameters(
        DbCommand command,
        string query,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var parameterIndex = 0;
        foreach (var pair in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = ResolveParameterName(query, pair.Key, parameterIndex);
            parameter.Value = pair.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            parameterIndex++;
        }
    }

    private static string ResolveParameterName(string query, string parameterName, int parameterIndex)
    {
        if (query.Contains("@" + parameterName, StringComparison.Ordinal))
        {
            return "@" + parameterName;
        }

        if (query.Contains(":" + parameterName, StringComparison.Ordinal))
        {
            return ":" + parameterName;
        }

        if (query.Contains("?" + parameterName, StringComparison.Ordinal))
        {
            return "?" + parameterName;
        }

        if (query.IndexOf('?') >= 0)
        {
            return "p" + parameterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return parameterName;
    }

    private static async ValueTask<ReportDataTable> ReadTableAsync(
        string dataSetId,
        DbDataReader reader,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        var table = new ReportDataTable
        {
            DataSetId = dataSetId
        };

        for (var fieldIndex = 0; fieldIndex < reader.FieldCount; fieldIndex++)
        {
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = string.IsNullOrWhiteSpace(reader.GetName(fieldIndex))
                    ? $"Column{fieldIndex + 1}"
                    : reader.GetName(fieldIndex),
                DataType = InferFieldType(reader.GetFieldType(fieldIndex))
            });
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new ReportDataRecord();
            for (var fieldIndex = 0; fieldIndex < reader.FieldCount; fieldIndex++)
            {
                object? value = await reader.IsDBNullAsync(fieldIndex, cancellationToken)
                    ? null
                    : reader.GetValue(fieldIndex);
                record.Values[table.Fields[fieldIndex].Name] = NormalizeValue(value, culture);
            }

            table.Rows.Add(record);
        }

        for (var fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
        {
            table.Fields[fieldIndex].DataType = ReportDataRuntimeHelpers.InferFieldType(table.Rows, table.Fields[fieldIndex].Name);
        }

        return table;
    }

    private static object? NormalizeValue(object? value, CultureInfo culture)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        if (value is Guid guid)
        {
            return guid.ToString("D");
        }

        return value;
    }

    private static ReportParameterDataType InferFieldType(Type? fieldType)
    {
        if (fieldType is null)
        {
            return ReportParameterDataType.String;
        }

        var type = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        if (type == typeof(int) || type == typeof(short) || type == typeof(long) || type == typeof(byte))
        {
            return ReportParameterDataType.Integer;
        }

        if (type == typeof(float) || type == typeof(double))
        {
            return ReportParameterDataType.Number;
        }

        if (type == typeof(decimal))
        {
            return ReportParameterDataType.Decimal;
        }

        if (type == typeof(bool))
        {
            return ReportParameterDataType.Boolean;
        }

        if (type == typeof(DateTimeOffset))
        {
            return ReportParameterDataType.DateTime;
        }

        if (type == typeof(DateTime))
        {
            return ReportParameterDataType.DateTime;
        }

        return ReportParameterDataType.String;
    }
}
