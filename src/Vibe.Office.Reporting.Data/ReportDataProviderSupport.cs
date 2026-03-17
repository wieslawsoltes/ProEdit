namespace Vibe.Office.Reporting.Data;

internal static class ReportDataProviderSupport
{
    public static string ResolveConnectionKey(ReportDataSourceDefinition dataSource)
    {
        if (dataSource.Options.TryGetValue("connectionName", out var connectionName)
            && !string.IsNullOrWhiteSpace(connectionName))
        {
            return connectionName;
        }

        if (dataSource.Options.TryGetValue("connectorKey", out var connectorKey)
            && !string.IsNullOrWhiteSpace(connectorKey))
        {
            return connectorKey;
        }

        if (dataSource.Options.TryGetValue("sourceKey", out var sourceKey)
            && !string.IsNullOrWhiteSpace(sourceKey))
        {
            return sourceKey;
        }

        if (!string.IsNullOrWhiteSpace(dataSource.ConnectionName))
        {
            return dataSource.ConnectionName;
        }

        if (!string.IsNullOrWhiteSpace(dataSource.Id))
        {
            return dataSource.Id;
        }

        throw new InvalidOperationException(
            "Data sources require a stable identifier, 'connectionName', 'connectorKey', or 'sourceKey'.");
    }

    public static string? GetOption(ReportDataSourceDefinition dataSource, string key)
    {
        return dataSource.Options.TryGetValue(key, out var value) ? value : null;
    }
}
