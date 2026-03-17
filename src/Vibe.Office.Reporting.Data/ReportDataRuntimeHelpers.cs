using System.Globalization;

namespace Vibe.Office.Reporting.Data;

internal static class ReportDataRuntimeHelpers
{
    public static Dictionary<string, object?> CreateGlobals(
        IReadOnlyDictionary<string, object?> globals,
        TimeZoneInfo timeZone)
    {
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in globals)
        {
            resolved[pair.Key] = pair.Value;
        }

        if (!resolved.ContainsKey("ExecutionTime"))
        {
            resolved["ExecutionTime"] = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        }

        if (!resolved.ContainsKey("PageNumber"))
        {
            resolved["PageNumber"] = 1;
        }

        if (!resolved.ContainsKey("TotalPages"))
        {
            resolved["TotalPages"] = 1;
        }

        return resolved;
    }

    public static ReportParameterDataType InferDataType(object? value)
    {
        return value switch
        {
            null => ReportParameterDataType.String,
            bool => ReportParameterDataType.Boolean,
            sbyte or byte or short or ushort or int or uint => ReportParameterDataType.Integer,
            long or ulong or float or double => ReportParameterDataType.Number,
            decimal => ReportParameterDataType.Decimal,
            DateOnly => ReportParameterDataType.Date,
            DateTime or DateTimeOffset => ReportParameterDataType.DateTime,
            _ => ReportParameterDataType.String
        };
    }

    public static ReportParameterDataType MergeDataType(
        ReportParameterDataType left,
        ReportParameterDataType right)
    {
        if (left == right)
        {
            return left;
        }

        if ((left == ReportParameterDataType.Integer && right == ReportParameterDataType.Decimal)
            || (left == ReportParameterDataType.Decimal && right == ReportParameterDataType.Integer))
        {
            return ReportParameterDataType.Decimal;
        }

        if ((left == ReportParameterDataType.Integer && right == ReportParameterDataType.Number)
            || (left == ReportParameterDataType.Number && right == ReportParameterDataType.Integer))
        {
            return ReportParameterDataType.Number;
        }

        if ((left == ReportParameterDataType.Decimal && right == ReportParameterDataType.Number)
            || (left == ReportParameterDataType.Number && right == ReportParameterDataType.Decimal))
        {
            return ReportParameterDataType.Decimal;
        }

        return ReportParameterDataType.String;
    }

    public static ReportParameterDataType InferFieldType(
        IReadOnlyList<ReportDataRecord> rows,
        string fieldName)
    {
        ReportParameterDataType? dataType = null;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (!rows[rowIndex].TryGetValue(fieldName, out var value))
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            var inferredDataType = InferDataType(value);
            dataType = dataType is null
                ? inferredDataType
                : MergeDataType(dataType.Value, inferredDataType);
        }

        return dataType ?? ReportParameterDataType.String;
    }

    public static object? ParseScalarValue(string? rawValue, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (bool.TryParse(rawValue, out var booleanValue))
        {
            return booleanValue;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, culture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(rawValue, NumberStyles.Integer, culture, out var longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(rawValue, NumberStyles.Number, culture, out var decimalValue))
        {
            return decimalValue;
        }

        if (DateTimeOffset.TryParse(rawValue, culture, DateTimeStyles.None, out var dateTimeOffset))
        {
            return dateTimeOffset;
        }

        if (DateTime.TryParse(rawValue, culture, DateTimeStyles.None, out var dateTime))
        {
            return dateTime;
        }

        return rawValue;
    }

    public static object? CoerceValue(
        object? value,
        ReportParameterDataType dataType,
        CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        return dataType switch
        {
            ReportParameterDataType.String => ToDisplayText(value, culture),
            ReportParameterDataType.Integer => ToInt32(value, culture),
            ReportParameterDataType.Number => Convert.ToDouble(ToDecimal(value, culture), culture),
            ReportParameterDataType.Decimal => ToDecimal(value, culture),
            ReportParameterDataType.Boolean => ToBoolean(value, culture),
            ReportParameterDataType.Date => ToDate(value, culture),
            ReportParameterDataType.DateTime => ToDateTime(value, culture),
            _ => value
        };
    }

    public static ReportParameterValue CoerceParameterValue(
        ReportParameterDefinition definition,
        object? rawValue,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.IsMultiValue
            && rawValue is System.Collections.IEnumerable
            && rawValue is not string
            && rawValue is not byte[])
        {
            throw new InvalidOperationException($"Parameter '{definition.Id}' does not allow multiple values.");
        }

        var parameterValue = new ReportParameterValue();
        var values = ExtractValues(rawValue, definition.IsMultiValue);
        if (values.Count == 0)
        {
            if (!definition.AllowNull)
            {
                throw new InvalidOperationException($"Parameter '{definition.Id}' requires a value.");
            }

            parameterValue.IsNull = true;
            return parameterValue;
        }

        for (var index = 0; index < values.Count; index++)
        {
            parameterValue.Values.Add(CoerceValue(values[index], definition.DataType, culture));
        }

        parameterValue.IsNull = parameterValue.Values.All(static value => value is null);
        if (!definition.AllowNull && (parameterValue.IsNull || parameterValue.Values.Any(static value => value is null)))
        {
            throw new InvalidOperationException($"Parameter '{definition.Id}' does not allow null values.");
        }

        return parameterValue;
    }

    public static ReportParameterValue CoerceParameterValue(
        ReportParameterDefinition definition,
        ReportParameterValue rawValue,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        if (!definition.IsMultiValue && rawValue.Values.Count > 1)
        {
            throw new InvalidOperationException($"Parameter '{definition.Id}' does not allow multiple values.");
        }

        if (rawValue.IsNull)
        {
            if (!definition.AllowNull)
            {
                throw new InvalidOperationException($"Parameter '{definition.Id}' does not allow null values.");
            }

            return new ReportParameterValue
            {
                IsNull = true
            };
        }

        return CoerceParameterValue(
            definition,
            definition.IsMultiValue ? rawValue.Values : rawValue.GetScalarValue(),
            culture);
    }

    public static List<object?> ExtractValues(object? rawValue, bool isMultiValue)
    {
        if (rawValue is null)
        {
            return new List<object?>();
        }

        if (!isMultiValue)
        {
            return new List<object?> { rawValue };
        }

        if (rawValue is string)
        {
            return new List<object?> { rawValue };
        }

        if (rawValue is IEnumerable<object?> objectEnumerable)
        {
            return objectEnumerable.ToList();
        }

        if (rawValue is System.Collections.IEnumerable enumerable)
        {
            var values = new List<object?>();
            foreach (var item in enumerable)
            {
                values.Add(item);
            }

            return values;
        }

        return new List<object?> { rawValue };
    }

    public static bool EvaluateFilter(
        object? left,
        ReportFilterOperator filterOperator,
        object? right,
        CultureInfo culture)
    {
        return filterOperator switch
        {
            ReportFilterOperator.Equal => AreEqual(left, right, culture),
            ReportFilterOperator.NotEqual => !AreEqual(left, right, culture),
            ReportFilterOperator.GreaterThan => Compare(left, right, culture) > 0,
            ReportFilterOperator.GreaterThanOrEqual => Compare(left, right, culture) >= 0,
            ReportFilterOperator.LessThan => Compare(left, right, culture) < 0,
            ReportFilterOperator.LessThanOrEqual => Compare(left, right, culture) <= 0,
            ReportFilterOperator.Contains => culture.CompareInfo.IndexOf(
                ToDisplayText(left, culture) ?? string.Empty,
                ToDisplayText(right, culture) ?? string.Empty,
                CompareOptions.IgnoreCase) >= 0,
            _ => false
        };
    }

    public static int Compare(object? left, object? right, CultureInfo culture)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryToDecimal(left, culture, out var leftDecimal) && TryToDecimal(right, culture, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        if (TryToDateTime(left, culture, out var leftDate) && TryToDateTime(right, culture, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        if (left is bool || right is bool)
        {
            return ToBoolean(left, culture).CompareTo(ToBoolean(right, culture));
        }

        return culture.CompareInfo.Compare(
            ToDisplayText(left, culture) ?? string.Empty,
            ToDisplayText(right, culture) ?? string.Empty,
            CompareOptions.IgnoreCase);
    }

    public static bool AreEqual(object? left, object? right, CultureInfo culture)
    {
        return Compare(left, right, culture) == 0;
    }

    public static string? ToDisplayText(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            string text => text,
            DateOnly dateOnly => dateOnly.ToString(culture),
            DateTime dateTime => dateTime.ToString(culture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(culture),
            IEnumerable<object?> values => string.Join(", ", values.Select(item => ToDisplayText(item, culture))),
            IFormattable formattable => formattable.ToString(null, culture),
            _ => Convert.ToString(value, culture)
        };
    }

    private static bool ToBoolean(object value, CultureInfo culture)
    {
        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var boolean) => boolean,
            string text when decimal.TryParse(text, NumberStyles.Number, culture, out var number) => number != 0m,
            sbyte signedByte => signedByte != 0,
            byte unsignedByte => unsignedByte != 0,
            short shortValue => shortValue != 0,
            ushort unsignedShort => unsignedShort != 0,
            int intValue => intValue != 0,
            uint unsignedInt => unsignedInt != 0,
            long longValue => longValue != 0,
            ulong unsignedLong => unsignedLong != 0,
            float single => single != 0,
            double doubleValue => doubleValue != 0,
            decimal decimalValue => decimalValue != 0,
            _ => throw new InvalidOperationException($"Value '{value}' cannot be converted to Boolean.")
        };
    }

    private static DateTime ToDate(object value, CultureInfo culture)
    {
        var dateTime = ToDateTime(value, culture);
        return dateTime.Date;
    }

    private static int ToInt32(object value, CultureInfo culture)
    {
        var decimalValue = ToDecimal(value, culture);
        if (decimal.Truncate(decimalValue) != decimalValue)
        {
            throw new InvalidOperationException($"Value '{value}' is not a whole number and cannot be converted to Integer.");
        }

        return decimal.ToInt32(decimalValue);
    }

    private static DateTimeOffset ToDateTime(object value, CultureInfo culture)
    {
        if (TryToDateTime(value, culture, out var dateTime))
        {
            return dateTime;
        }

        throw new InvalidOperationException($"Value '{value}' cannot be converted to DateTime.");
    }

    private static decimal ToDecimal(object value, CultureInfo culture)
    {
        if (TryToDecimal(value, culture, out var decimalValue))
        {
            return decimalValue;
        }

        throw new InvalidOperationException($"Value '{value}' cannot be converted to Decimal.");
    }

    private static bool TryToDecimal(object value, CultureInfo culture, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case sbyte signedByte:
                result = signedByte;
                return true;
            case byte unsignedByte:
                result = unsignedByte;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort unsignedShort:
                result = unsignedShort;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint unsignedInt:
                result = unsignedInt;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong unsignedLong:
                result = unsignedLong;
                return true;
            case float single:
                result = (decimal)single;
                return true;
            case double doubleValue:
                result = (decimal)doubleValue;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, culture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static bool TryToDateTime(object value, CultureInfo culture, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset dateTimeOffset:
                result = dateTimeOffset;
                return true;
            case DateTime dateTime:
                result = new DateTimeOffset(dateTime);
                return true;
            case DateOnly dateOnly:
                result = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue));
                return true;
            case string text when DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out var parsedDateTimeOffset):
                result = parsedDateTimeOffset;
                return true;
            case string text when DateTime.TryParse(text, culture, DateTimeStyles.None, out var parsedDateTime):
                result = new DateTimeOffset(parsedDateTime);
                return true;
            default:
                result = default;
                return false;
        }
    }
}
