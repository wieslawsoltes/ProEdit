using System.Text.Json;

namespace Vibe.Office.Reporting.Data;

internal static class ReportJsonDataHelpers
{
    public static ReportDataTable ParseTable(
        string json,
        string? path,
        string dataSetId,
        bool unwrapSinglePropertyContainer = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = ResolvePath(document.RootElement, path);
        if (unwrapSinglePropertyContainer)
        {
            root = TryUnwrapSinglePropertyContainer(root);
        }

        return CreateTable(root, dataSetId);
    }

    public static ReportDataTable CreateTable(JsonElement root, string dataSetId)
    {
        var table = new ReportDataTable
        {
            DataSetId = dataSetId
        };

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                table.Rows.Add(CreateRecord(element));
            }
        }
        else
        {
            table.Rows.Add(CreateRecord(root));
        }

        PopulateFieldsFromRows(table);
        return table;
    }

    public static JsonElement ResolvePath(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            return root;
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        var current = root;
        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            current = ResolvePathSegment(current, segments[index]);
        }

        return current;
    }

    private static JsonElement ResolvePathSegment(JsonElement current, string segment)
    {
        var propertyName = segment;
        int? arrayIndex = null;

        var openBracket = segment.IndexOf('[');
        if (openBracket >= 0 && segment.EndsWith(']'))
        {
            propertyName = segment[..openBracket];
            var indexText = segment[(openBracket + 1)..^1];
            if (!int.TryParse(indexText, out var parsedIndex))
            {
                throw new InvalidOperationException($"JSON path segment '{segment}' contains an invalid array index.");
            }

            arrayIndex = parsedIndex;
        }

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(propertyName, out current))
            {
                throw new InvalidOperationException($"JSON path property '{propertyName}' was not found.");
            }
        }

        if (arrayIndex.HasValue)
        {
            if (current.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"JSON path segment '{segment}' does not point to an array.");
            }

            var index = 0;
            foreach (var item in current.EnumerateArray())
            {
                if (index == arrayIndex.Value)
                {
                    return item;
                }

                index++;
            }

            throw new InvalidOperationException($"JSON path array index {arrayIndex.Value} was out of range.");
        }

        return current;
    }

    private static JsonElement TryUnwrapSinglePropertyContainer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root;
        }

        var enumerator = root.EnumerateObject();
        if (!enumerator.MoveNext())
        {
            return root;
        }

        var first = enumerator.Current;
        if (enumerator.MoveNext())
        {
            return root;
        }

        return first.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? first.Value
            : root;
    }

    private static ReportDataRecord CreateRecord(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ReportDataRecord(new Dictionary<string, object?>
            {
                ["Value"] = ConvertJsonValue(element)
            });
        }

        var record = new ReportDataRecord();
        foreach (var property in element.EnumerateObject())
        {
            record.Values[property.Name] = ConvertJsonValue(property.Value);
        }

        return record;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String when element.TryGetDateTimeOffset(out var dateTimeOffset) => dateTimeOffset,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }

    private static void PopulateFieldsFromRows(ReportDataTable table)
    {
        var fields = new Dictionary<string, ReportParameterDataType>(StringComparer.OrdinalIgnoreCase);
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

        foreach (var pair in fields)
        {
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = pair.Key,
                DataType = pair.Value
            });
        }
    }
}
