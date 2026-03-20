using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Vibe.Office.Reporting.Data;

/// <summary>
/// Registers the built-in report data providers.
/// </summary>
public static class ReportDataProviders
{
    /// <summary>
    /// Creates a provider registry pre-populated with the built-in providers.
    /// </summary>
    /// <returns>The populated provider registry.</returns>
    public static ReportDataProviderRegistry CreateDefaultRegistry()
    {
        var registry = new ReportDataProviderRegistry();
        AddBuiltInProviders(registry);
        return registry;
    }

    /// <summary>
    /// Adds the built-in providers to the supplied registry.
    /// </summary>
    /// <param name="registry">The target registry.</param>
    public static void AddBuiltInProviders(ReportDataProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(new InMemoryReportDataProvider());
        registry.Register(new JsonReportDataProvider());
        registry.Register(new CsvReportDataProvider());
        registry.Register(new EnterDataReportDataProvider());
        registry.Register(new SqlReportDataProvider());
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.SqlServer));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.PostgreSql));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.MySql));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.MariaDb));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.Oracle));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.Sqlite));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.Snowflake));
        registry.Register(new AdoNetReportDataProvider(ReportProviderIds.Odbc));
        registry.Register(new RestJsonReportDataProvider());
        registry.Register(new ODataReportDataProvider());
        registry.Register(new GraphQlReportDataProvider());
    }
}

internal sealed class InMemoryReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.InMemory;

    public async ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var sourceKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (!context.HostDataRegistry.TryGetInMemorySource(sourceKey, out var source))
        {
            throw new InvalidOperationException($"In-memory source '{sourceKey}' is not registered.");
        }

        var table = await source.ReadAsync(cancellationToken);
        table.DataSetId = dataSet.Id;
        return table;
    }
}

internal sealed class JsonReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.Json;

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var json = ResolveJson(dataSource, context);
        var path = string.IsNullOrWhiteSpace(dataSet.Query)
            ? ReportDataProviderSupport.GetOption(dataSource, "path")
            : dataSet.Query;
        return ValueTask.FromResult(ReportJsonDataHelpers.ParseTable(json, path, dataSet.Id));
    }

    private static string ResolveJson(ReportDataSourceDefinition dataSource, ReportDataProviderContext context)
    {
        if (dataSource.Options.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var sourceKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (!context.HostDataRegistry.TryGetJsonSource(sourceKey, out var json))
        {
            throw new InvalidOperationException($"JSON source '{sourceKey}' is not registered.");
        }

        return json;
    }
}

internal sealed class CsvReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.Csv;

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var csv = ResolveCsv(dataSource, context);
        var delimiter = ResolveOptionChar(dataSource, "delimiter", ',');
        var quote = ResolveOptionChar(dataSource, "quote", '"');
        var hasHeaders = ResolveOptionBoolean(dataSource, "hasHeaders", true);
        var rows = ParseCsv(csv, delimiter, quote);

        var table = new ReportDataTable
        {
            DataSetId = dataSet.Id
        };

        if (rows.Count == 0)
        {
            return ValueTask.FromResult(table);
        }

        var columnNames = BuildColumnNames(rows, hasHeaders);
        for (var columnIndex = 0; columnIndex < columnNames.Count; columnIndex++)
        {
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = columnNames[columnIndex],
                DataType = ReportParameterDataType.String
            });
        }

        var startRowIndex = hasHeaders ? 1 : 0;
        for (var rowIndex = startRowIndex; rowIndex < rows.Count; rowIndex++)
        {
            var parsedRow = rows[rowIndex];
            var record = new ReportDataRecord();
            for (var columnIndex = 0; columnIndex < columnNames.Count; columnIndex++)
            {
                var rawValue = columnIndex < parsedRow.Count ? parsedRow[columnIndex] : null;
                record.Values[columnNames[columnIndex]] = ReportDataRuntimeHelpers.ParseScalarValue(rawValue, context.Culture);
            }

            table.Rows.Add(record);
        }

        for (var fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
        {
            var field = table.Fields[fieldIndex];
            field.DataType = ReportDataRuntimeHelpers.InferFieldType(table.Rows, field.Name);
        }

        return ValueTask.FromResult(table);
    }

    private static string ResolveCsv(ReportDataSourceDefinition dataSource, ReportDataProviderContext context)
    {
        if (dataSource.Options.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var sourceKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (!context.HostDataRegistry.TryGetCsvSource(sourceKey, out var csv))
        {
            throw new InvalidOperationException($"CSV source '{sourceKey}' is not registered.");
        }

        return csv;
    }

    private static char ResolveOptionChar(ReportDataSourceDefinition dataSource, string name, char fallback)
    {
        if (!dataSource.Options.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        return value[0];
    }

    private static bool ResolveOptionBoolean(ReportDataSourceDefinition dataSource, string name, bool fallback)
    {
        if (!dataSource.Options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static List<List<string?>> ParseCsv(string csv, char delimiter, char quote)
    {
        var rows = new List<List<string?>>();
        var row = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var current = csv[index];
            if (inQuotes)
            {
                if (current == quote)
                {
                    if (index + 1 < csv.Length && csv[index + 1] == quote)
                    {
                        field.Append(quote);
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            if (current == quote)
            {
                inQuotes = true;
                continue;
            }

            if (current == delimiter)
            {
                row.Add(field.Length == 0 ? null : field.ToString());
                field.Clear();
                continue;
            }

            if (current == '\r')
            {
                continue;
            }

            if (current == '\n')
            {
                row.Add(field.Length == 0 ? null : field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string?>();
                continue;
            }

            field.Append(current);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.Length == 0 ? null : field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static List<string> BuildColumnNames(IReadOnlyList<List<string?>> rows, bool hasHeaders)
    {
        var firstRow = rows[0];
        var columnNames = new List<string>(firstRow.Count);
        for (var columnIndex = 0; columnIndex < firstRow.Count; columnIndex++)
        {
            if (hasHeaders)
            {
                columnNames.Add(string.IsNullOrWhiteSpace(firstRow[columnIndex])
                    ? $"Column{columnIndex + 1}"
                    : firstRow[columnIndex]!);
            }
            else
            {
                columnNames.Add($"Column{columnIndex + 1}");
            }
        }

        return columnNames;
    }
}

internal sealed class EnterDataReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.EnterData;

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(dataSet.Query))
        {
            return ValueTask.FromResult(new ReportDataTable
            {
                DataSetId = dataSet.Id
            });
        }

        try
        {
            var table = ParseEnterDataQuery(dataSet, context.Culture);
            return ValueTask.FromResult(table);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"ENTERDATA query for dataset '{dataSet.Id}' is not valid XML: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"ENTERDATA query for dataset '{dataSet.Id}' could not be parsed: {ex.Message}", ex);
        }
    }

    private static ReportDataTable ParseEnterDataQuery(
        ReportDataSetDefinition dataSet,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        var queryText = dataSet.Query;
        var dataSetId = dataSet.Id;
        var document = XDocument.Parse(queryText, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            throw new InvalidOperationException("ENTERDATA query does not contain a root element.");
        }

        var queryElement = root.Name.LocalName.Equals("Query", StringComparison.OrdinalIgnoreCase)
            ? root
            : root.Element(root.GetDefaultNamespace() + "Query") ?? root.Descendants().FirstOrDefault(static element =>
                element.Name.LocalName.Equals("Query", StringComparison.OrdinalIgnoreCase));
        if (queryElement is null)
        {
            throw new InvalidOperationException("ENTERDATA query does not contain a Query element.");
        }

        var xmlDataElement = queryElement.Elements().FirstOrDefault(static element =>
            element.Name.LocalName.Equals("XmlData", StringComparison.OrdinalIgnoreCase));
        if (xmlDataElement is null)
        {
            throw new InvalidOperationException("ENTERDATA query does not contain an XmlData element.");
        }

        var dataElement = xmlDataElement.Elements().FirstOrDefault(static element =>
            element.Name.LocalName.Equals("Data", StringComparison.OrdinalIgnoreCase));
        if (dataElement is null)
        {
            throw new InvalidOperationException("ENTERDATA query does not contain a Data element.");
        }

        var table = new ReportDataTable
        {
            DataSetId = dataSetId
        };
        var expectedFieldTypes = new Dictionary<string, ReportParameterDataType>(StringComparer.OrdinalIgnoreCase);
        for (var fieldIndex = 0; fieldIndex < dataSet.ExpectedFields.Count; fieldIndex++)
        {
            var expectedField = dataSet.ExpectedFields[fieldIndex];
            expectedFieldTypes[expectedField.Name] = expectedField.DataType;
        }

        foreach (var rowElement in dataElement.Elements().Where(static element =>
                     element.Name.LocalName.Equals("Row", StringComparison.OrdinalIgnoreCase)))
        {
            var record = new ReportDataRecord();
            foreach (var fieldElement in rowElement.Elements())
            {
                var fieldName = fieldElement.Name.LocalName;
                var rawValue = string.Concat(fieldElement.Nodes().OfType<XText>().Select(static node => node.Value));
                if (expectedFieldTypes.TryGetValue(fieldName, out var expectedDataType))
                {
                    record.Values[fieldName] = ParseExpectedEnterDataValue(rawValue, expectedDataType, culture);
                }
                else
                {
                    record.Values[fieldName] = ReportDataRuntimeHelpers.ParseScalarValue(rawValue, culture);
                }

                if (!table.Fields.Any(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    table.Fields.Add(new ReportFieldDefinition
                    {
                        Name = fieldName,
                        DataType = expectedFieldTypes.TryGetValue(fieldName, out var fieldDataType)
                            ? fieldDataType
                            : ReportParameterDataType.String
                    });
                }
            }

            table.Rows.Add(record);
        }

        for (var fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
        {
            var field = table.Fields[fieldIndex];
            if (expectedFieldTypes.TryGetValue(field.Name, out var expectedFieldType))
            {
                field.DataType = expectedFieldType;
                continue;
            }

            field.DataType = ReportDataRuntimeHelpers.InferFieldType(table.Rows, field.Name);
        }

        return table;
    }
    private static object? ParseExpectedEnterDataValue(
        string rawValue,
        ReportParameterDataType dataType,
        CultureInfo culture)
    {
        if (dataType == ReportParameterDataType.String)
        {
            return rawValue;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return ReportDataRuntimeHelpers.CoerceValue(rawValue, dataType, culture);
    }
}
