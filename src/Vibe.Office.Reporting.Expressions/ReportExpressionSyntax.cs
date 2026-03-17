using System.Globalization;
using System.Text;

namespace Vibe.Office.Reporting.Expressions;

internal enum ReportExpressionTokenKind
{
    End,
    Identifier,
    Number,
    String,
    LeftParen,
    RightParen,
    Comma,
    Dot,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    And,
    Or,
    Not
}

internal readonly record struct ReportExpressionToken(
    ReportExpressionTokenKind Kind,
    string Text,
    int Position);

internal abstract record ReportExpressionNode;

internal sealed record ReportLiteralExpressionNode(object? Value) : ReportExpressionNode;

internal sealed record ReportIdentifierExpressionNode(string Name) : ReportExpressionNode;

internal sealed record ReportMemberAccessExpressionNode(ReportExpressionNode Target, string MemberName) : ReportExpressionNode;

internal sealed record ReportUnaryExpressionNode(
    ReportExpressionTokenKind OperatorKind,
    ReportExpressionNode Operand) : ReportExpressionNode;

internal sealed record ReportBinaryExpressionNode(
    ReportExpressionNode Left,
    ReportExpressionTokenKind OperatorKind,
    ReportExpressionNode Right) : ReportExpressionNode;

internal sealed record ReportFunctionCallExpressionNode(
    ReportExpressionNode Target,
    IReadOnlyList<ReportExpressionNode> Arguments) : ReportExpressionNode;

internal sealed class ReportExpressionTokenizer
{
    private readonly List<ReportDiagnostic> _diagnostics;
    private readonly string _text;

    public ReportExpressionTokenizer(string text, List<ReportDiagnostic> diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<ReportExpressionToken> Tokenize()
    {
        var tokens = new List<ReportExpressionToken>();
        var position = 0;
        while (position < _text.Length)
        {
            var current = _text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = position;
                position++;
                while (position < _text.Length && (char.IsLetterOrDigit(_text[position]) || _text[position] == '_'))
                {
                    position++;
                }

                var identifier = _text[start..position];
                var kind = identifier.ToLowerInvariant() switch
                {
                    "and" => ReportExpressionTokenKind.And,
                    "or" => ReportExpressionTokenKind.Or,
                    "not" => ReportExpressionTokenKind.Not,
                    _ => ReportExpressionTokenKind.Identifier
                };

                tokens.Add(new ReportExpressionToken(kind, identifier, start));
                continue;
            }

            if (char.IsDigit(current))
            {
                var start = position;
                position++;
                while (position < _text.Length && (char.IsDigit(_text[position]) || _text[position] == '.'))
                {
                    position++;
                }

                tokens.Add(new ReportExpressionToken(
                    ReportExpressionTokenKind.Number,
                    _text[start..position],
                    start));
                continue;
            }

            if (current == '\'' || current == '"')
            {
                var start = position;
                if (!TryReadStringLiteral(current, ref position, out var literal))
                {
                    _diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ExpressionParseFailed,
                        $"Unterminated string literal at position {start}.",
                        $"${start}"));
                    break;
                }

                tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.String, literal, start));
                continue;
            }

            switch (current)
            {
                case '(':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.LeftParen, "(", position));
                    position++;
                    break;
                case ')':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.RightParen, ")", position));
                    position++;
                    break;
                case ',':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Comma, ",", position));
                    position++;
                    break;
                case '.':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Dot, ".", position));
                    position++;
                    break;
                case '+':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Plus, "+", position));
                    position++;
                    break;
                case '-':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Minus, "-", position));
                    position++;
                    break;
                case '*':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Star, "*", position));
                    position++;
                    break;
                case '/':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Slash, "/", position));
                    position++;
                    break;
                case '%':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Percent, "%", position));
                    position++;
                    break;
                case '=':
                    tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Equal, "=", position));
                    position++;
                    break;
                case '!':
                    if (TryMatch('=', ref position))
                    {
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.NotEqual, "!=", position - 2));
                        break;
                    }

                    _diagnostics.Add(UnexpectedCharacterDiagnostic(current, position));
                    position++;
                    break;
                case '&':
                    if (TryMatch('&', ref position))
                    {
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.And, "&&", position - 2));
                        break;
                    }

                    _diagnostics.Add(UnexpectedCharacterDiagnostic(current, position));
                    position++;
                    break;
                case '|':
                    if (TryMatch('|', ref position))
                    {
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Or, "||", position - 2));
                        break;
                    }

                    _diagnostics.Add(UnexpectedCharacterDiagnostic(current, position));
                    position++;
                    break;
                case '<':
                    position++;
                    if (position < _text.Length && _text[position] == '=')
                    {
                        position++;
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.LessOrEqual, "<=", position - 2));
                    }
                    else if (position < _text.Length && _text[position] == '>')
                    {
                        position++;
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.NotEqual, "<>", position - 2));
                    }
                    else
                    {
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Less, "<", position - 1));
                    }

                    break;
                case '>':
                    position++;
                    if (position < _text.Length && _text[position] == '=')
                    {
                        position++;
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.GreaterOrEqual, ">=", position - 2));
                    }
                    else
                    {
                        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.Greater, ">", position - 1));
                    }

                    break;
                default:
                    _diagnostics.Add(UnexpectedCharacterDiagnostic(current, position));
                    position++;
                    break;
            }
        }

        tokens.Add(new ReportExpressionToken(ReportExpressionTokenKind.End, string.Empty, _text.Length));
        return tokens;
    }

    private static ReportDiagnostic UnexpectedCharacterDiagnostic(char current, int position)
    {
        return new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExpressionParseFailed,
            $"Unexpected character '{current}' at position {position}.",
            $"${position}");
    }

    private bool TryReadStringLiteral(char quote, ref int position, out string literal)
    {
        var builder = new StringBuilder();
        position++;
        while (position < _text.Length)
        {
            var current = _text[position];
            if (current == quote)
            {
                if (position + 1 < _text.Length && _text[position + 1] == quote)
                {
                    builder.Append(quote);
                    position += 2;
                    continue;
                }

                position++;
                literal = builder.ToString();
                return true;
            }

            builder.Append(current);
            position++;
        }

        literal = string.Empty;
        return false;
    }

    private bool TryMatch(char expected, ref int position)
    {
        if (position + 1 < _text.Length && _text[position + 1] == expected)
        {
            position += 2;
            return true;
        }

        return false;
    }
}

internal sealed class ReportExpressionParser
{
    private readonly List<ReportDiagnostic> _diagnostics;
    private readonly string _expression;
    private readonly IReadOnlyList<ReportExpressionToken> _tokens;
    private int _position;

    public ReportExpressionParser(
        IReadOnlyList<ReportExpressionToken> tokens,
        string expression,
        List<ReportDiagnostic> diagnostics)
    {
        _tokens = tokens;
        _expression = expression;
        _diagnostics = diagnostics;
    }

    public ReportExpressionNode? Parse()
    {
        var expression = ParseExpression();
        if (expression is null)
        {
            return null;
        }

        if (Current.Kind != ReportExpressionTokenKind.End)
        {
            _diagnostics.Add(ParseError($"Unexpected token '{Current.Text}' at position {Current.Position}.", Current.Position));
            return null;
        }

        return expression;
    }

    private ReportExpressionNode? ParseExpression(int minimumPrecedence = 1)
    {
        var left = ParseUnary();
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            var precedence = GetBinaryPrecedence(Current.Kind);
            if (precedence < minimumPrecedence)
            {
                break;
            }

            var operatorToken = Read();
            var right = ParseExpression(precedence + 1);
            if (right is null)
            {
                return null;
            }

            left = new ReportBinaryExpressionNode(left, operatorToken.Kind, right);
        }

        return left;
    }

    private ReportExpressionNode? ParseUnary()
    {
        if (Current.Kind is ReportExpressionTokenKind.Not or ReportExpressionTokenKind.Plus or ReportExpressionTokenKind.Minus)
        {
            var operatorToken = Read();
            var operand = ParseUnary();
            if (operand is null)
            {
                return null;
            }

            return new ReportUnaryExpressionNode(operatorToken.Kind, operand);
        }

        return ParsePostfix();
    }

    private ReportExpressionNode? ParsePostfix()
    {
        var expression = ParsePrimary();
        if (expression is null)
        {
            return null;
        }

        while (true)
        {
            if (Match(ReportExpressionTokenKind.Dot))
            {
                if (Current.Kind != ReportExpressionTokenKind.Identifier)
                {
                    _diagnostics.Add(ParseError("Expected an identifier after '.'.", Current.Position));
                    return null;
                }

                expression = new ReportMemberAccessExpressionNode(expression, Read().Text);
                continue;
            }

            if (Match(ReportExpressionTokenKind.LeftParen))
            {
                var arguments = new List<ReportExpressionNode>();
                if (!Match(ReportExpressionTokenKind.RightParen))
                {
                    while (true)
                    {
                        var argument = ParseExpression();
                        if (argument is null)
                        {
                            return null;
                        }

                        arguments.Add(argument);
                        if (Match(ReportExpressionTokenKind.RightParen))
                        {
                            break;
                        }

                        if (!Match(ReportExpressionTokenKind.Comma))
                        {
                            _diagnostics.Add(ParseError("Expected ',' or ')' in function call.", Current.Position));
                            return null;
                        }
                    }
                }

                expression = new ReportFunctionCallExpressionNode(expression, arguments);
                continue;
            }

            break;
        }

        return expression;
    }

    private ReportExpressionNode? ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case ReportExpressionTokenKind.Number:
                Read();
                if (!decimal.TryParse(token.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    _diagnostics.Add(ParseError($"Number literal '{token.Text}' is invalid.", token.Position));
                    return null;
                }

                return new ReportLiteralExpressionNode(number);

            case ReportExpressionTokenKind.String:
                Read();
                return new ReportLiteralExpressionNode(token.Text);

            case ReportExpressionTokenKind.Identifier:
                Read();
                return token.Text.ToLowerInvariant() switch
                {
                    "true" => new ReportLiteralExpressionNode(true),
                    "false" => new ReportLiteralExpressionNode(false),
                    "null" => new ReportLiteralExpressionNode(null),
                    _ => new ReportIdentifierExpressionNode(token.Text)
                };

            case ReportExpressionTokenKind.LeftParen:
                Read();
                var expression = ParseExpression();
                if (expression is null)
                {
                    return null;
                }

                if (!Match(ReportExpressionTokenKind.RightParen))
                {
                    _diagnostics.Add(ParseError("Expected ')' to close the expression.", Current.Position));
                    return null;
                }

                return expression;

            default:
                _diagnostics.Add(ParseError($"Unexpected token '{token.Text}' at position {token.Position}.", token.Position));
                return null;
        }
    }

    private ReportDiagnostic ParseError(string message, int position)
    {
        return new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.ExpressionParseFailed,
            $"{message} Expression: {_expression}",
            $"${position}");
    }

    private int GetBinaryPrecedence(ReportExpressionTokenKind kind)
    {
        return kind switch
        {
            ReportExpressionTokenKind.Or => 1,
            ReportExpressionTokenKind.And => 2,
            ReportExpressionTokenKind.Equal or ReportExpressionTokenKind.NotEqual => 3,
            ReportExpressionTokenKind.Less or ReportExpressionTokenKind.LessOrEqual
                or ReportExpressionTokenKind.Greater or ReportExpressionTokenKind.GreaterOrEqual => 4,
            ReportExpressionTokenKind.Plus or ReportExpressionTokenKind.Minus => 5,
            ReportExpressionTokenKind.Star or ReportExpressionTokenKind.Slash or ReportExpressionTokenKind.Percent => 6,
            _ => 0
        };
    }

    private ReportExpressionToken Current => _tokens[_position];

    private ReportExpressionToken Read()
    {
        var token = Current;
        _position++;
        return token;
    }

    private bool Match(ReportExpressionTokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        _position++;
        return true;
    }
}

internal static class ReportExpressionRuntime
{
    public static object? Evaluate(ReportExpressionNode node, ReportExpressionContext context)
    {
        return node switch
        {
            ReportLiteralExpressionNode literal => literal.Value,
            ReportIdentifierExpressionNode identifier => EvaluateIdentifier(identifier.Name, context),
            ReportMemberAccessExpressionNode memberAccess => EvaluateMemberAccess(memberAccess, context),
            ReportUnaryExpressionNode unary => EvaluateUnary(unary, context),
            ReportBinaryExpressionNode binary => EvaluateBinary(binary, context),
            ReportFunctionCallExpressionNode functionCall => EvaluateFunctionCall(functionCall, context),
            _ => throw new ReportExpressionEvaluationException($"Unsupported expression node '{node.GetType().Name}'.")
        };
    }

    private static object? EvaluateIdentifier(string name, ReportExpressionContext context)
    {
        if (name.Equals("Fields", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Row", StringComparison.OrdinalIgnoreCase))
        {
            return ReportExpressionNamespace.Fields;
        }

        if (name.Equals("Parameters", StringComparison.OrdinalIgnoreCase))
        {
            return ReportExpressionNamespace.Parameters;
        }

        if (name.Equals("Globals", StringComparison.OrdinalIgnoreCase))
        {
            return ReportExpressionNamespace.Globals;
        }

        if (context.Fields.TryGetValue(name, out var fieldValue))
        {
            return fieldValue;
        }

        if (context.Globals.TryGetValue(name, out var globalValue))
        {
            return globalValue;
        }

        if (context.Parameters.TryGetValue(name, out var parameterValue))
        {
            return parameterValue.Values.Count > 1 ? parameterValue.Values.AsReadOnly() : parameterValue.GetScalarValue();
        }

        throw new ReportExpressionEvaluationException($"Identifier '{name}' was not found in the current expression scope.");
    }

    private static object? EvaluateMemberAccess(ReportMemberAccessExpressionNode node, ReportExpressionContext context)
    {
        var target = Evaluate(node.Target, context);
        return target switch
        {
            ReportExpressionNamespace.Fields => GetValue(context.Fields, node.MemberName, "field"),
            ReportExpressionNamespace.Parameters => GetParameterValue(context.Parameters, node.MemberName),
            ReportExpressionNamespace.Globals => GetValue(context.Globals, node.MemberName, "global"),
            IReadOnlyDictionary<string, object?> values => GetValue(values, node.MemberName, "member"),
            IDictionary<string, object?> mutableValues => GetValue(mutableValues, node.MemberName, "member"),
            _ => throw new ReportExpressionEvaluationException(
                $"Expression member '{node.MemberName}' cannot be read from '{target?.GetType().Name ?? "null"}'.")
        };
    }

    private static object? EvaluateUnary(ReportUnaryExpressionNode node, ReportExpressionContext context)
    {
        var operand = Evaluate(node.Operand, context);
        return node.OperatorKind switch
        {
            ReportExpressionTokenKind.Not => !ReportExpressionValueConverter.ToBoolean(operand, context.Culture),
            ReportExpressionTokenKind.Plus => ReportExpressionValueConverter.ToDecimal(operand, context.Culture),
            ReportExpressionTokenKind.Minus => -ReportExpressionValueConverter.ToDecimal(operand, context.Culture),
            _ => throw new ReportExpressionEvaluationException($"Unary operator '{node.OperatorKind}' is not supported.")
        };
    }

    private static object? EvaluateBinary(ReportBinaryExpressionNode node, ReportExpressionContext context)
    {
        if (node.OperatorKind == ReportExpressionTokenKind.And)
        {
            var left = ReportExpressionValueConverter.ToBoolean(Evaluate(node.Left, context), context.Culture);
            return left && ReportExpressionValueConverter.ToBoolean(Evaluate(node.Right, context), context.Culture);
        }

        if (node.OperatorKind == ReportExpressionTokenKind.Or)
        {
            var left = ReportExpressionValueConverter.ToBoolean(Evaluate(node.Left, context), context.Culture);
            return left || ReportExpressionValueConverter.ToBoolean(Evaluate(node.Right, context), context.Culture);
        }

        var leftValue = Evaluate(node.Left, context);
        var rightValue = Evaluate(node.Right, context);

        return node.OperatorKind switch
        {
            ReportExpressionTokenKind.Plus => ReportExpressionValueConverter.Add(leftValue, rightValue, context.Culture),
            ReportExpressionTokenKind.Minus => ReportExpressionValueConverter.ToDecimal(leftValue, context.Culture)
                - ReportExpressionValueConverter.ToDecimal(rightValue, context.Culture),
            ReportExpressionTokenKind.Star => ReportExpressionValueConverter.ToDecimal(leftValue, context.Culture)
                * ReportExpressionValueConverter.ToDecimal(rightValue, context.Culture),
            ReportExpressionTokenKind.Slash => Divide(leftValue, rightValue, context.Culture),
            ReportExpressionTokenKind.Percent => ReportExpressionValueConverter.ToDecimal(leftValue, context.Culture)
                % ReportExpressionValueConverter.ToDecimal(rightValue, context.Culture),
            ReportExpressionTokenKind.Equal => ReportExpressionValueConverter.AreEqual(leftValue, rightValue, context.Culture),
            ReportExpressionTokenKind.NotEqual => !ReportExpressionValueConverter.AreEqual(leftValue, rightValue, context.Culture),
            ReportExpressionTokenKind.Less => ReportExpressionValueConverter.Compare(leftValue, rightValue, context.Culture) < 0,
            ReportExpressionTokenKind.LessOrEqual => ReportExpressionValueConverter.Compare(leftValue, rightValue, context.Culture) <= 0,
            ReportExpressionTokenKind.Greater => ReportExpressionValueConverter.Compare(leftValue, rightValue, context.Culture) > 0,
            ReportExpressionTokenKind.GreaterOrEqual => ReportExpressionValueConverter.Compare(leftValue, rightValue, context.Culture) >= 0,
            _ => throw new ReportExpressionEvaluationException($"Binary operator '{node.OperatorKind}' is not supported.")
        };
    }

    private static object? Divide(object? leftValue, object? rightValue, CultureInfo culture)
    {
        var divisor = ReportExpressionValueConverter.ToDecimal(rightValue, culture);
        if (divisor == 0m)
        {
            throw new ReportExpressionEvaluationException("Division by zero is not allowed.");
        }

        return ReportExpressionValueConverter.ToDecimal(leftValue, culture) / divisor;
    }

    private static object? EvaluateFunctionCall(ReportFunctionCallExpressionNode node, ReportExpressionContext context)
    {
        var functionName = node.Target switch
        {
            ReportIdentifierExpressionNode identifier => identifier.Name,
            _ => throw new ReportExpressionEvaluationException("Only identifier-based function calls are supported.")
        };

        return functionName.ToLowerInvariant() switch
        {
            "iif" => EvaluateIif(node.Arguments, context),
            "coalesce" => EvaluateCoalesce(node.Arguments, context),
            "format" => EvaluateFormat(node.Arguments, context),
            "len" => EvaluateLength(node.Arguments, context),
            "upper" => EvaluateUpper(node.Arguments, context),
            "lower" => EvaluateLower(node.Arguments, context),
            "trim" => EvaluateTextTransform(node.Arguments, context, static text => text.Trim(), "Trim"),
            "contains" => EvaluateContains(node.Arguments, context),
            "startswith" => EvaluateStartsWith(node.Arguments, context),
            "endswith" => EvaluateEndsWith(node.Arguments, context),
            "now" => EvaluateNow(node.Arguments, context),
            "today" => EvaluateToday(node.Arguments, context),
            "sum" => EvaluateAggregate(node.Arguments, context, AggregateKind.Sum),
            "avg" => EvaluateAggregate(node.Arguments, context, AggregateKind.Average),
            "count" => EvaluateAggregate(node.Arguments, context, AggregateKind.Count),
            "min" => EvaluateAggregate(node.Arguments, context, AggregateKind.Min),
            "max" => EvaluateAggregate(node.Arguments, context, AggregateKind.Max),
            "first" => EvaluateAggregate(node.Arguments, context, AggregateKind.First),
            "last" => EvaluateAggregate(node.Arguments, context, AggregateKind.Last),
            _ => throw new ReportExpressionEvaluationException($"Function '{functionName}' is not supported.")
        };
    }

    private static object? EvaluateIif(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 3, "Iif");
        var condition = ReportExpressionValueConverter.ToBoolean(Evaluate(arguments[0], context), context.Culture);
        return Evaluate(condition ? arguments[1] : arguments[2], context);
    }

    private static object? EvaluateCoalesce(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        if (arguments.Count == 0)
        {
            throw new ReportExpressionEvaluationException("Coalesce requires at least one argument.");
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var value = Evaluate(arguments[index], context);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static object? EvaluateFormat(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 2, "Format");
        var value = Evaluate(arguments[0], context);
        var format = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[1], context), context.Culture);
        if (value is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            return ReportExpressionValueConverter.ToStringValue(value, context.Culture);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format, context.Culture);
        }

        return ReportExpressionValueConverter.ToStringValue(value, context.Culture);
    }

    private static object? EvaluateLength(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 1, "Len");
        var text = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        return text.Length;
    }

    private static object? EvaluateTextTransform(
        IReadOnlyList<ReportExpressionNode> arguments,
        ReportExpressionContext context,
        Func<string, string> transform,
        string functionName)
    {
        RequireArgumentCount(arguments, 1, functionName);
        var text = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        return transform(text);
    }

    private static object? EvaluateUpper(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 1, "Upper");
        var text = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        return text.ToUpper(context.Culture);
    }

    private static object? EvaluateLower(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 1, "Lower");
        var text = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        return text.ToLower(context.Culture);
    }

    private static object? EvaluateContains(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 2, "Contains");
        var source = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        var value = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[1], context), context.Culture) ?? string.Empty;
        return context.Culture.CompareInfo.IndexOf(source, value, CompareOptions.IgnoreCase) >= 0;
    }

    private static object? EvaluateStartsWith(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 2, "StartsWith");
        var source = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        var value = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[1], context), context.Culture) ?? string.Empty;
        return source.StartsWith(value, true, context.Culture);
    }

    private static object? EvaluateEndsWith(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 2, "EndsWith");
        var source = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[0], context), context.Culture) ?? string.Empty;
        var value = ReportExpressionValueConverter.ToStringValue(Evaluate(arguments[1], context), context.Culture) ?? string.Empty;
        return source.EndsWith(value, true, context.Culture);
    }

    private static object? EvaluateNow(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 0, "Now");
        return ResolveExecutionTime(context);
    }

    private static object? EvaluateToday(IReadOnlyList<ReportExpressionNode> arguments, ReportExpressionContext context)
    {
        RequireArgumentCount(arguments, 0, "Today");
        return TimeZoneInfo.ConvertTime(ResolveExecutionTime(context), context.TimeZone).Date;
    }

    private static DateTimeOffset ResolveExecutionTime(ReportExpressionContext context)
    {
        if (context.Globals.TryGetValue("ExecutionTime", out var executionTimeValue) && executionTimeValue is not null)
        {
            return ReportExpressionValueConverter.ToDateTimeOffset(executionTimeValue, context.Culture);
        }

        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, context.TimeZone);
    }

    private static object? EvaluateAggregate(
        IReadOnlyList<ReportExpressionNode> arguments,
        ReportExpressionContext context,
        AggregateKind aggregateKind)
    {
        if (aggregateKind == AggregateKind.Count && arguments.Count == 0)
        {
            return context.ScopeRows.Count;
        }

        RequireArgumentCount(arguments, 1, aggregateKind.ToString());
        var scopeRows = context.ScopeRows.Count > 0
            ? context.ScopeRows
            : new[] { context.Fields };

        decimal decimalAccumulator = 0m;
        object? extremum = null;
        object? firstValue = null;
        object? lastValue = null;
        var numericCount = 0;
        var valueCount = 0;

        for (var index = 0; index < scopeRows.Count; index++)
        {
            var childContext = context.CreateChild(scopeRows[index], index, scopeRows);
            var value = Evaluate(arguments[0], childContext);
            if (value is null)
            {
                continue;
            }

            valueCount++;
            firstValue ??= value;
            lastValue = value;

            switch (aggregateKind)
            {
                case AggregateKind.Sum:
                case AggregateKind.Average:
                    decimalAccumulator += ReportExpressionValueConverter.ToDecimal(value, context.Culture);
                    numericCount++;
                    break;
                case AggregateKind.Min:
                    if (extremum is null
                        || ReportExpressionValueConverter.Compare(value, extremum, context.Culture) < 0)
                    {
                        extremum = value;
                    }

                    break;
                case AggregateKind.Max:
                    if (extremum is null
                        || ReportExpressionValueConverter.Compare(value, extremum, context.Culture) > 0)
                    {
                        extremum = value;
                    }

                    break;
            }
        }

        return aggregateKind switch
        {
            AggregateKind.Sum => decimalAccumulator,
            AggregateKind.Average => numericCount == 0 ? null : decimalAccumulator / numericCount,
            AggregateKind.Count => valueCount,
            AggregateKind.Min => extremum,
            AggregateKind.Max => extremum,
            AggregateKind.First => firstValue,
            AggregateKind.Last => lastValue,
            _ => throw new ReportExpressionEvaluationException($"Aggregate '{aggregateKind}' is not supported.")
        };
    }

    private static void RequireArgumentCount(IReadOnlyList<ReportExpressionNode> arguments, int expectedCount, string functionName)
    {
        if (arguments.Count != expectedCount)
        {
            throw new ReportExpressionEvaluationException(
                $"Function '{functionName}' expects {expectedCount} argument(s), but {arguments.Count} were provided.");
        }
    }

    private static object? GetValue(
        IReadOnlyDictionary<string, object?> values,
        string key,
        string kind)
    {
        if (values.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new ReportExpressionEvaluationException($"The {kind} '{key}' was not found in the current expression scope.");
    }

    private static object? GetValue(
        IDictionary<string, object?> values,
        string key,
        string kind)
    {
        if (values.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new ReportExpressionEvaluationException($"The {kind} '{key}' was not found in the current expression scope.");
    }

    private static object? GetParameterValue(
        IReadOnlyDictionary<string, ReportParameterValue> parameters,
        string key)
    {
        if (!parameters.TryGetValue(key, out var parameterValue))
        {
            throw new ReportExpressionEvaluationException($"The parameter '{key}' was not found in the current expression scope.");
        }

        return parameterValue.Values.Count > 1
            ? parameterValue.Values.AsReadOnly()
            : parameterValue.GetScalarValue();
    }

    private enum AggregateKind
    {
        Sum,
        Average,
        Count,
        Min,
        Max,
        First,
        Last
    }

    private enum ReportExpressionNamespace
    {
        Fields,
        Parameters,
        Globals
    }
}

internal static class ReportExpressionValueConverter
{
    public static object? Add(object? left, object? right, CultureInfo culture)
    {
        if (left is null && right is null)
        {
            return null;
        }

        if (left is string || right is string)
        {
            return string.Concat(ToStringValue(left, culture), ToStringValue(right, culture));
        }

        return ToDecimal(left, culture) + ToDecimal(right, culture);
    }

    public static bool AreEqual(object? left, object? right, CultureInfo culture)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (TryToDecimal(left, culture, out var leftDecimal) && TryToDecimal(right, culture, out var rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        if (TryToDateTimeOffset(left, culture, out var leftDate) && TryToDateTimeOffset(right, culture, out var rightDate))
        {
            return leftDate == rightDate;
        }

        if (left is bool || right is bool)
        {
            return ToBoolean(left, culture) == ToBoolean(right, culture);
        }

        var leftText = ToStringValue(left, culture) ?? string.Empty;
        var rightText = ToStringValue(right, culture) ?? string.Empty;
        return culture.CompareInfo.Compare(leftText, rightText, CompareOptions.IgnoreCase) == 0;
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

        if (TryToDateTimeOffset(left, culture, out var leftDate) && TryToDateTimeOffset(right, culture, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        if (left is bool || right is bool)
        {
            return ToBoolean(left, culture).CompareTo(ToBoolean(right, culture));
        }

        var leftText = ToStringValue(left, culture) ?? string.Empty;
        var rightText = ToStringValue(right, culture) ?? string.Empty;
        return culture.CompareInfo.Compare(leftText, rightText, CompareOptions.IgnoreCase);
    }

    public static bool ToBoolean(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => false,
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
            _ => throw new ReportExpressionEvaluationException(
                $"Value '{value}' cannot be converted to Boolean.")
        };
    }

    public static decimal ToDecimal(object? value, CultureInfo culture)
    {
        if (TryToDecimal(value, culture, out var decimalValue))
        {
            return decimalValue;
        }

        throw new ReportExpressionEvaluationException($"Value '{value}' cannot be converted to Decimal.");
    }

    public static string? ToStringValue(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            string text => text,
            DateOnly dateOnly => dateOnly.ToString(culture),
            DateTime dateTime => dateTime.ToString(culture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(culture),
            IEnumerable<object?> values => string.Join(", ", values.Select(item => ToStringValue(item, culture))),
            IFormattable formattable => formattable.ToString(null, culture),
            _ => Convert.ToString(value, culture)
        };
    }

    public static DateTimeOffset ToDateTimeOffset(object? value, CultureInfo culture)
    {
        if (TryToDateTimeOffset(value, culture, out var dateTimeOffset))
        {
            return dateTimeOffset;
        }

        throw new ReportExpressionEvaluationException($"Value '{value}' cannot be converted to DateTimeOffset.");
    }

    public static bool TryToDecimal(object? value, CultureInfo culture, out decimal result)
    {
        switch (value)
        {
            case null:
                result = 0m;
                return false;
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

    private static bool TryToDateTimeOffset(object? value, CultureInfo culture, out DateTimeOffset result)
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

internal sealed class ReportExpressionEvaluationException : Exception
{
    public ReportExpressionEvaluationException(string message)
        : base(message)
    {
    }
}
