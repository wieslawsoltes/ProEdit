using System.Globalization;

namespace ProEdit.Vba;

public static class VbaTokenizer
{
    public static List<VbaToken> Tokenize(string source)
    {
        var tokens = new List<VbaToken>();
        if (string.IsNullOrEmpty(source))
        {
            tokens.Add(new VbaToken(VbaTokenKind.EndOfFile, string.Empty, 0, 1, 1));
            return tokens;
        }

        var span = source.AsSpan();
        var index = 0;
        var line = 1;
        var column = 1;

        while (index < span.Length)
        {
            var ch = span[index];
            if (ch == '\r' || ch == '\n')
            {
                var start = index;
                if (ch == '\r' && index + 1 < span.Length && span[index + 1] == '\n')
                {
                    index += 2;
                }
                else
                {
                    index++;
                }

                tokens.Add(new VbaToken(VbaTokenKind.NewLine, "\n", start, line, column));
                line++;
                column = 1;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                index++;
                column++;
                continue;
            }

            if (ch == '\'')
            {
                index++;
                column++;
                while (index < span.Length && span[index] != '\r' && span[index] != '\n')
                {
                    index++;
                    column++;
                }
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = index;
                var startColumn = column;
                index++;
                column++;
                while (index < span.Length && (char.IsLetterOrDigit(span[index]) || span[index] == '_'))
                {
                    index++;
                    column++;
                }

                var text = span[start..index].ToString();
                var kind = ResolveKeyword(text);
                tokens.Add(new VbaToken(kind, text, start, line, startColumn));
                continue;
            }

            if (char.IsDigit(ch) || (ch == '.' && index + 1 < span.Length && char.IsDigit(span[index + 1])))
            {
                var start = index;
                var startColumn = column;
                index++;
                column++;
                while (index < span.Length && (char.IsDigit(span[index]) || span[index] == '.'))
                {
                    index++;
                    column++;
                }

                var text = span[start..index].ToString();
                tokens.Add(new VbaToken(VbaTokenKind.Number, text, start, line, startColumn));
                continue;
            }

            if (ch == '"')
            {
                var start = index;
                var startColumn = column;
                index++;
                column++;
                var builder = new System.Text.StringBuilder();
                while (index < span.Length)
                {
                    var current = span[index];
                    if (current == '"')
                    {
                        if (index + 1 < span.Length && span[index + 1] == '"')
                        {
                            builder.Append('"');
                            index += 2;
                            column += 2;
                            continue;
                        }

                        index++;
                        column++;
                        break;
                    }

                    builder.Append(current);
                    index++;
                    column++;
                }

                tokens.Add(new VbaToken(VbaTokenKind.String, builder.ToString(), start, line, startColumn));
                continue;
            }

            var tokenStart = index;
            var tokenColumn = column;
            index++;
            column++;

            var tokenKind = ch switch
            {
                '=' => VbaTokenKind.Equal,
                ',' => VbaTokenKind.Comma,
                '(' => VbaTokenKind.LParen,
                ')' => VbaTokenKind.RParen,
                '.' => VbaTokenKind.Dot,
                '+' => VbaTokenKind.Plus,
                '-' => VbaTokenKind.Minus,
                '*' => VbaTokenKind.Asterisk,
                '/' => VbaTokenKind.Slash,
                '&' => VbaTokenKind.Ampersand,
                '>' => ReadComparisonToken(span, ref index, ref column, VbaTokenKind.Greater, VbaTokenKind.GreaterEqual),
                '<' => ReadLessComparisonToken(span, ref index, ref column),
                _ => VbaTokenKind.EndOfFile
            };

            if (tokenKind != VbaTokenKind.EndOfFile)
            {
                tokens.Add(new VbaToken(tokenKind, ch.ToString(CultureInfo.InvariantCulture), tokenStart, line, tokenColumn));
            }
        }

        tokens.Add(new VbaToken(VbaTokenKind.EndOfFile, string.Empty, span.Length, line, column));
        return tokens;
    }

    private static VbaTokenKind ResolveKeyword(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "IF" => VbaTokenKind.KeywordIf,
            "THEN" => VbaTokenKind.KeywordThen,
            "ELSE" => VbaTokenKind.KeywordElse,
            "AND" => VbaTokenKind.KeywordAnd,
            "OR" => VbaTokenKind.KeywordOr,
            "NOT" => VbaTokenKind.KeywordNot,
            "FOR" => VbaTokenKind.KeywordFor,
            "TO" => VbaTokenKind.KeywordTo,
            "STEP" => VbaTokenKind.KeywordStep,
            "NEXT" => VbaTokenKind.KeywordNext,
            "DO" => VbaTokenKind.KeywordDo,
            "LOOP" => VbaTokenKind.KeywordLoop,
            "WHILE" => VbaTokenKind.KeywordWhile,
            "UNTIL" => VbaTokenKind.KeywordUntil,
            "ON" => VbaTokenKind.KeywordOn,
            "ERROR" => VbaTokenKind.KeywordError,
            "RESUME" => VbaTokenKind.KeywordResume,
            "GOTO" => VbaTokenKind.KeywordGoTo,
            "SELECT" => VbaTokenKind.KeywordSelect,
            "CASE" => VbaTokenKind.KeywordCase,
            "IS" => VbaTokenKind.KeywordIs,
            "WEND" => VbaTokenKind.KeywordWend,
            "WITH" => VbaTokenKind.KeywordWith,
            "REDIM" => VbaTokenKind.KeywordReDim,
            "PRESERVE" => VbaTokenKind.KeywordPreserve,
            "AS" => VbaTokenKind.KeywordAs,
            "DIM" => VbaTokenKind.KeywordDim,
            "SET" => VbaTokenKind.KeywordSet,
            "EXIT" => VbaTokenKind.KeywordExit,
            "FUNCTION" => VbaTokenKind.KeywordFunction,
            "SUB" => VbaTokenKind.KeywordSub,
            "END" => VbaTokenKind.KeywordEnd,
            "CALL" => VbaTokenKind.KeywordCall,
            "MOD" => VbaTokenKind.KeywordMod,
            "PUBLIC" => VbaTokenKind.KeywordPublic,
            "PRIVATE" => VbaTokenKind.KeywordPrivate,
            "FRIEND" => VbaTokenKind.KeywordFriend,
            "TRUE" => VbaTokenKind.KeywordTrue,
            "FALSE" => VbaTokenKind.KeywordFalse,
            _ => VbaTokenKind.Identifier
        };
    }

    private static VbaTokenKind ReadComparisonToken(
        ReadOnlySpan<char> span,
        ref int index,
        ref int column,
        VbaTokenKind single,
        VbaTokenKind paired)
    {
        if (index < span.Length && span[index] == '=')
        {
            index++;
            column++;
            return paired;
        }

        return single;
    }

    private static VbaTokenKind ReadLessComparisonToken(ReadOnlySpan<char> span, ref int index, ref int column)
    {
        if (index < span.Length)
        {
            var next = span[index];
            if (next == '=')
            {
                index++;
                column++;
                return VbaTokenKind.LessEqual;
            }

            if (next == '>')
            {
                index++;
                column++;
                return VbaTokenKind.NotEqual;
            }
        }

        return VbaTokenKind.Less;
    }
}
