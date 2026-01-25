using System;
using System.Collections.Generic;
using System.Text;

namespace Vibe.Office.Documents;

public enum FieldKind
{
    Unknown,
    Page,
    NumPages,
    Date,
    Time,
    Hyperlink,
    Ref,
    DocProperty,
    Citation,
    Bibliography
}

public readonly record struct FieldArgument(string Value);

public readonly record struct FieldSwitch(string Name, string? Value);

public sealed class FieldDefinition
{
    public string RawInstruction { get; }
    public string Name { get; }
    public FieldKind Kind { get; }
    public IReadOnlyList<FieldArgument> Arguments { get; }
    public IReadOnlyList<FieldSwitch> Switches { get; }

    public FieldDefinition(
        string rawInstruction,
        string name,
        FieldKind kind,
        IReadOnlyList<FieldArgument> arguments,
        IReadOnlyList<FieldSwitch> switches)
    {
        RawInstruction = rawInstruction;
        Name = name;
        Kind = kind;
        Arguments = arguments;
        Switches = switches;
    }
}

public enum FieldEvaluationMode
{
    PreserveResult,
    Evaluate
}

public static class FieldEvaluationPolicy
{
    public static FieldEvaluationMode GetMode(FieldDefinition? definition)
    {
        if (definition is null)
        {
            return FieldEvaluationMode.PreserveResult;
        }

        return definition.Kind switch
        {
            FieldKind.Page => FieldEvaluationMode.Evaluate,
            FieldKind.NumPages => FieldEvaluationMode.Evaluate,
            _ => FieldEvaluationMode.PreserveResult
        };
    }
}

public static class FieldInstructionParser
{
    public static FieldDefinition? Parse(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return null;
        }

        var trimmed = instruction.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var tokens = Tokenize(trimmed.AsSpan());
        if (tokens.Count == 0)
        {
            return null;
        }

        var name = tokens[0];
        var kind = ParseKind(name);
        var arguments = new List<FieldArgument>();
        var switches = new List<FieldSwitch>();

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IsSwitchToken(token))
            {
                var value = i + 1 < tokens.Count && !IsSwitchToken(tokens[i + 1]) ? tokens[i + 1] : null;
                if (value is not null)
                {
                    i++;
                }

                switches.Add(new FieldSwitch(token, value));
            }
            else
            {
                arguments.Add(new FieldArgument(token));
            }
        }

        return new FieldDefinition(trimmed, name, kind, arguments, switches);
    }

    private static FieldKind ParseKind(string name)
    {
        if (name.Length == 0)
        {
            return FieldKind.Unknown;
        }

        return name.ToUpperInvariant() switch
        {
            "PAGE" => FieldKind.Page,
            "NUMPAGES" => FieldKind.NumPages,
            "DATE" => FieldKind.Date,
            "TIME" => FieldKind.Time,
            "HYPERLINK" => FieldKind.Hyperlink,
            "REF" => FieldKind.Ref,
            "PAGEREF" => FieldKind.Ref,
            "DOCPROPERTY" => FieldKind.DocProperty,
            "CITATION" => FieldKind.Citation,
            "BIBLIOGRAPHY" => FieldKind.Bibliography,
            _ => FieldKind.Unknown
        };
    }

    private static bool IsSwitchToken(string token)
    {
        return token.Length > 0 && token[0] == '\\';
    }

    private static List<string> Tokenize(ReadOnlySpan<char> span)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < span.Length)
        {
            SkipWhiteSpace(span, ref index);
            if (index >= span.Length)
            {
                break;
            }

            if (span[index] == '"')
            {
                tokens.Add(ReadQuoted(span, ref index));
                continue;
            }

            var start = index;
            while (index < span.Length && !char.IsWhiteSpace(span[index]))
            {
                index++;
            }

            if (index > start)
            {
                tokens.Add(span.Slice(start, index - start).ToString());
            }
        }

        return tokens;
    }

    private static void SkipWhiteSpace(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }
    }

    private static string ReadQuoted(ReadOnlySpan<char> span, ref int index)
    {
        index++; // skip opening quote
        var start = index;
        StringBuilder? builder = null;

        while (index < span.Length)
        {
            var ch = span[index];
            if (ch == '"')
            {
                if (index + 1 < span.Length && span[index + 1] == '"')
                {
                    builder ??= new StringBuilder();
                    builder.Append(span.Slice(start, index - start));
                    builder.Append('"');
                    index += 2;
                    start = index;
                    continue;
                }

                break;
            }

            if (ch == '\\' && index + 1 < span.Length && span[index + 1] == '"')
            {
                builder ??= new StringBuilder();
                builder.Append(span.Slice(start, index - start));
                builder.Append('"');
                index += 2;
                start = index;
                continue;
            }

            index++;
        }

        var segment = span.Slice(start, index - start);
        string value;
        if (builder is not null)
        {
            builder.Append(segment);
            value = builder.ToString();
        }
        else
        {
            value = segment.ToString();
        }

        if (index < span.Length && span[index] == '"')
        {
            index++;
        }

        return value;
    }
}
