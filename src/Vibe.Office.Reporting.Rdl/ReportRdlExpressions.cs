using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vibe.Office.Reporting.Rdl;

internal static partial class ReportRdlExpressions
{
    [GeneratedRegex(@"(?<![A-Za-z0-9_])Fields!(?<name>[A-Za-z_][A-Za-z0-9_]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FieldsReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Parameters!(?<name>[A-Za-z_][A-Za-z0-9_]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParametersReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Globals!(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GlobalsReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Fields\.(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeFieldsReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Parameters\.(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeParametersReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Globals\.(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeGlobalsReferenceRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])ParameterLabel\(\s*'(?<name>[A-Za-z_][A-Za-z0-9_]*)'\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeParameterLabelRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])ParameterIsMultiValue\(\s*'(?<name>[A-Za-z_][A-Za-z0-9_]*)'\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeParameterIsMultiValueRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])CurrentValue\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeCurrentValueRegex();

    [GeneratedRegex(@"\bnull\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeNullRegex();

    [GeneratedRegex(@"\bNothing\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlNothingRegex();

    [GeneratedRegex(@"\bAndAlso\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlAndAlsoRegex();

    [GeneratedRegex(@"\bOrElse\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlOrElseRegex();

    [GeneratedRegex(@"\bAnd\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlAndRegex();

    [GeneratedRegex(@"\bOr\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlOrRegex();

    [GeneratedRegex(@"\bNot\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlNotRegex();

    [GeneratedRegex(@"\bMod\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RdlModRegex();

    public static string? ToRdlExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var normalized = expression.Trim();
        if (normalized.StartsWith('='))
        {
            return normalized;
        }

        normalized = EncodeSpecialNativeFunctions(normalized, out var specialReplacements);
        normalized = TransformExpression(
            normalized,
            TransformNativeCodeToRdl,
            static literal => EncodeRdlStringLiteral(literal));
        normalized = DecodeSpecialNativeFunctions(normalized, specialReplacements);

        return "=" + normalized;
    }

    public static string? ToNativeExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var normalized = expression.Trim();
        if (normalized.StartsWith('='))
        {
            normalized = normalized[1..].Trim();
        }

        normalized = TransformExpression(
            normalized,
            TransformRdlCodeToNative,
            QuoteNativeString);

        return normalized;
    }

    public static void SplitTextboxValue(
        string? rdlValue,
        out string? staticText,
        out string? expression)
    {
        staticText = null;
        expression = null;
        if (rdlValue is null)
        {
            return;
        }

        if (rdlValue.Length == 0)
        {
            staticText = string.Empty;
            return;
        }

        var trimmedStart = rdlValue.TrimStart();
        if (!trimmedStart.StartsWith('='))
        {
            staticText = rdlValue;
            return;
        }

        var body = trimmedStart[1..].Trim();
        if (TryParseRdlStringLiteral(body, out var literal))
        {
            staticText = literal;
            return;
        }

        expression = ToNativeExpression(trimmedStart);
    }

    public static string ToTextboxValue(string? staticText, string? expression)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            return ToRdlExpression(expression) ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(staticText) && staticText[0] == '=')
        {
            return "=" + EncodeRdlStringLiteral(staticText);
        }

        return staticText ?? string.Empty;
    }

    public static string? ToRdlScalarValue(string? expressionOrLiteral)
    {
        if (string.IsNullOrWhiteSpace(expressionOrLiteral))
        {
            return null;
        }

        if (TryExtractNativeStringLiteral(expressionOrLiteral, out var literal))
        {
            return literal;
        }

        return ToRdlExpression(expressionOrLiteral);
    }

    public static string? ToNativeValueExpression(string? rdlValue)
    {
        if (rdlValue is null)
        {
            return null;
        }

        if (rdlValue.Length == 0)
        {
            return QuoteNativeString(string.Empty);
        }

        var trimmedStart = rdlValue.TrimStart();
        if (trimmedStart.StartsWith('='))
        {
            return ToNativeExpression(trimmedStart);
        }

        var trimmed = rdlValue.Trim();
        if (bool.TryParse(trimmed, out var boolean))
        {
            return boolean ? "true" : "false";
        }

        if (trimmed.Equals("Nothing", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Null", StringComparison.OrdinalIgnoreCase))
        {
            return "null";
        }

        if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
        }

        return QuoteNativeString(rdlValue);
    }

    public static string? ToNativeScalarExpression(string? rdlValue)
    {
        return ToNativeValueExpression(rdlValue);
    }

    public static string QuoteNativeString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static bool TryExtractNativeStringLiteral(string expression, out string literal)
    {
        literal = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
        if (trimmed.Length < 2)
        {
            return false;
        }

        if (trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            literal = trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
            return true;
        }

        if (trimmed[0] == '"' && trimmed[^1] == '"')
        {
            literal = trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            return true;
        }

        return false;
    }

    private static bool TryParseRdlStringLiteral(string expression, out string literal)
    {
        literal = string.Empty;
        if (expression.Length < 2 || expression[0] != '"' || expression[^1] != '"')
        {
            return false;
        }

        var builder = new StringBuilder(expression.Length - 2);
        for (var index = 1; index < expression.Length - 1; index++)
        {
            var current = expression[index];
            if (current == '"')
            {
                if (index + 1 >= expression.Length - 1 || expression[index + 1] != '"')
                {
                    return false;
                }

                builder.Append('"');
                index++;
                continue;
            }

            builder.Append(current);
        }

        literal = builder.ToString();
        return true;
    }

    private static string TransformNativeCodeToRdl(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        var normalized = NativeFieldsReferenceRegex().Replace(code, "Fields!${name}.Value");
        normalized = NativeParametersReferenceRegex().Replace(normalized, "Parameters!${name}.Value");
        normalized = NativeGlobalsReferenceRegex().Replace(normalized, "Globals!${name}");
        normalized = NativeParameterLabelRegex().Replace(normalized, "Parameters!${name}.Label");
        normalized = NativeParameterIsMultiValueRegex().Replace(normalized, "Parameters!${name}.IsMultiValue");
        normalized = NativeCurrentValueRegex().Replace(normalized, "Me.Value");
        normalized = NativeNullRegex().Replace(normalized, "Nothing");
        normalized = normalized.Replace("!=", "<>", StringComparison.Ordinal);
        normalized = normalized.Replace("&&", " AndAlso ", StringComparison.Ordinal);
        normalized = normalized.Replace("||", " OrElse ", StringComparison.Ordinal);
        normalized = normalized.Replace("%", " Mod ", StringComparison.Ordinal);
        return normalized;
    }

    private static string TransformRdlCodeToNative(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        var normalized = FieldsReferenceRegex().Replace(code, static match =>
        {
            var name = match.Groups["name"].Value;
            var member = match.Groups["member"].Value;
            return member.Equals("Value", StringComparison.OrdinalIgnoreCase)
                ? $"Fields.{name}"
                : $"Fields.{name}.{member}";
        });
        normalized = ParametersReferenceRegex().Replace(normalized, static match =>
        {
            var name = match.Groups["name"].Value;
            var member = match.Groups["member"].Value;
            if (member.Equals("Value", StringComparison.OrdinalIgnoreCase))
            {
                return $"Parameters.{name}";
            }

            if (member.Equals("Label", StringComparison.OrdinalIgnoreCase))
            {
                return $"ParameterLabel('{name}')";
            }

            if (member.Equals("IsMultiValue", StringComparison.OrdinalIgnoreCase))
            {
                return $"ParameterIsMultiValue('{name}')";
            }

            return $"Parameters.{name}.{member}";
        });
        normalized = GlobalsReferenceRegex().Replace(normalized, "Globals.${name}");
        normalized = normalized.Replace("Me.Value", "CurrentValue()", StringComparison.OrdinalIgnoreCase);
        normalized = RdlNothingRegex().Replace(normalized, "null");
        normalized = RdlAndAlsoRegex().Replace(normalized, "and");
        normalized = RdlOrElseRegex().Replace(normalized, "or");
        normalized = RdlAndRegex().Replace(normalized, "and");
        normalized = RdlOrRegex().Replace(normalized, "or");
        normalized = RdlNotRegex().Replace(normalized, "not");
        normalized = RdlModRegex().Replace(normalized, "%");
        normalized = normalized.Replace("&", "+", StringComparison.Ordinal);
        return normalized;
    }

    private static string TransformExpression(
        string expression,
        Func<string, string> codeTransform,
        Func<string, string> stringTransform)
    {
        var result = new StringBuilder(expression.Length + 8);
        var codeBuilder = new StringBuilder(expression.Length);
        var index = 0;
        while (index < expression.Length)
        {
            var current = expression[index];
            if (current is '\'' or '"')
            {
                if (codeBuilder.Length > 0)
                {
                    result.Append(codeTransform(codeBuilder.ToString()));
                    codeBuilder.Clear();
                }

                if (!TryReadLiteral(expression, ref index, current, out var literal))
                {
                    codeBuilder.Append(current);
                    index++;
                    continue;
                }

                result.Append(stringTransform(literal));
                continue;
            }

            codeBuilder.Append(current);
            index++;
        }

        if (codeBuilder.Length > 0)
        {
            result.Append(codeTransform(codeBuilder.ToString()));
        }

        return result.ToString();
    }

    private static string EncodeSpecialNativeFunctions(
        string expression,
        out Dictionary<string, string> replacements)
    {
        var localReplacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        var normalized = NativeParameterLabelRegex().Replace(expression, match =>
        {
            var key = $"__vibe_rdl_parameter_label_{index++}__";
            localReplacements[key] = $"Parameters!{match.Groups["name"].Value}.Label";
            return key;
        });

        normalized = NativeParameterIsMultiValueRegex().Replace(normalized, match =>
        {
            var key = $"__vibe_rdl_parameter_multi_{index++}__";
            localReplacements[key] = $"Parameters!{match.Groups["name"].Value}.IsMultiValue";
            return key;
        });

        replacements = localReplacements;
        return normalized;
    }

    private static string DecodeSpecialNativeFunctions(
        string expression,
        IReadOnlyDictionary<string, string> replacements)
    {
        var normalized = expression;
        foreach (var pair in replacements)
        {
            normalized = normalized.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return normalized;
    }

    private static bool TryReadLiteral(
        string text,
        ref int index,
        char delimiter,
        out string literal)
    {
        var builder = new StringBuilder();
        var position = index + 1;
        while (position < text.Length)
        {
            var current = text[position];
            if (current == delimiter)
            {
                if (position + 1 < text.Length && text[position + 1] == delimiter)
                {
                    builder.Append(delimiter);
                    position += 2;
                    continue;
                }

                index = position + 1;
                literal = builder.ToString();
                return true;
            }

            builder.Append(current);
            position++;
        }

        literal = string.Empty;
        return false;
    }

    private static string EncodeRdlStringLiteral(string literal)
    {
        return "\"" + literal.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
