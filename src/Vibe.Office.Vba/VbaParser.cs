namespace Vibe.Office.Vba;

public sealed class VbaParser
{
    private readonly IReadOnlyList<VbaToken> _tokens;
    private int _position;

    public VbaParser(IReadOnlyList<VbaToken> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public static VbaModuleSyntax ParseModule(string source)
    {
        var tokens = VbaTokenizer.Tokenize(source);
        var parser = new VbaParser(tokens);
        return parser.ParseModule();
    }

    public static VbaExpressionSyntax ParseExpression(string source)
    {
        var tokens = VbaTokenizer.Tokenize(source);
        var parser = new VbaParser(tokens);
        parser.SkipNewLines();
        var expression = parser.ParseExpression();
        parser.SkipNewLines();
        if (!parser.IsAtEnd())
        {
            throw new VbaParseException("Unexpected token.", parser.CurrentToken);
        }

        return expression;
    }

    public VbaModuleSyntax ParseModule()
    {
        var members = new List<VbaModuleMemberSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (IsAtEnd())
            {
                break;
            }

            SkipAccessModifiers();
            if (Match(VbaTokenKind.KeywordSub))
            {
                members.Add(ParseSubroutine());
                continue;
            }

            if (Match(VbaTokenKind.KeywordFunction))
            {
                members.Add(ParseFunction());
                continue;
            }

            Advance();
        }

        return new VbaModuleSyntax(members);
    }

    private void SkipAccessModifiers()
    {
        while (Match(VbaTokenKind.KeywordPublic)
               || Match(VbaTokenKind.KeywordPrivate)
               || Match(VbaTokenKind.KeywordFriend))
        {
        }
    }

    private VbaSubroutineSyntax ParseSubroutine()
    {
        var name = Expect(VbaTokenKind.Identifier, "Expected subroutine name.").Text;
        var parameters = ParseParameters();
        SkipNewLines();
        var statements = ParseStatementsUntil(VbaTokenKind.KeywordEnd, VbaTokenKind.KeywordSub);
        return new VbaSubroutineSyntax(name, parameters, statements);
    }

    private VbaFunctionSyntax ParseFunction()
    {
        var name = Expect(VbaTokenKind.Identifier, "Expected function name.").Text;
        var parameters = ParseParameters();
        SkipNewLines();
        var statements = ParseStatementsUntil(VbaTokenKind.KeywordEnd, VbaTokenKind.KeywordFunction);
        return new VbaFunctionSyntax(name, parameters, statements);
    }

    private IReadOnlyList<VbaParameterSyntax> ParseParameters()
    {
        var parameters = new List<VbaParameterSyntax>();
        if (!Match(VbaTokenKind.LParen))
        {
            return parameters;
        }

        while (!IsAtEnd() && !Check(VbaTokenKind.RParen))
        {
            var name = ParseParameterName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                parameters.Add(new VbaParameterSyntax(name));
            }

            if (!Match(VbaTokenKind.Comma))
            {
                break;
            }
        }

        Expect(VbaTokenKind.RParen, "Expected ')' after parameters.");
        return parameters;
    }

    private string ParseParameterName()
    {
        string? candidate = null;
        while (!IsAtEnd() && !Check(VbaTokenKind.Comma) && !Check(VbaTokenKind.RParen))
        {
            if (Check(VbaTokenKind.Identifier))
            {
                candidate = Advance().Text;
                continue;
            }

            Advance();
        }

        return candidate ?? string.Empty;
    }

    private List<VbaStatementSyntax> ParseStatementsUntil(VbaTokenKind endKeyword, VbaTokenKind blockKeyword)
    {
        var statements = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(endKeyword) && CheckNext(blockKeyword))
            {
                Advance();
                Advance();
                break;
            }

            if (IsAtEnd())
            {
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return statements;
    }

    private VbaStatementSyntax? ParseStatement()
    {
        if (Match(VbaTokenKind.KeywordIf))
        {
            var span = CreateSpan(Previous());
            return ParseIfStatement(span);
        }

        if (Match(VbaTokenKind.KeywordFor))
        {
            var span = CreateSpan(Previous());
            return ParseForStatement(span);
        }

        if (Match(VbaTokenKind.KeywordDo))
        {
            var span = CreateSpan(Previous());
            return ParseDoStatement(span);
        }

        if (Match(VbaTokenKind.KeywordWhile))
        {
            var span = CreateSpan(Previous());
            return ParseWhileStatement(span);
        }

        if (Match(VbaTokenKind.KeywordSelect))
        {
            var span = CreateSpan(Previous());
            return ParseSelectStatement(span);
        }

        if (Match(VbaTokenKind.KeywordWith))
        {
            var span = CreateSpan(Previous());
            return ParseWithStatement(span);
        }

        if (Match(VbaTokenKind.KeywordDim))
        {
            var span = CreateSpan(Previous());
            return ParseDimStatement(span);
        }

        if (Match(VbaTokenKind.KeywordReDim))
        {
            var span = CreateSpan(Previous());
            return ParseRedimStatement(span);
        }

        if (Match(VbaTokenKind.KeywordOn))
        {
            var span = CreateSpan(Previous());
            return ParseOnErrorStatement(span);
        }

        if (Match(VbaTokenKind.KeywordExit))
        {
            var span = CreateSpan(Previous());
            return ParseExitStatement(span);
        }

        var isSet = Match(VbaTokenKind.KeywordSet);
        if (Check(VbaTokenKind.Identifier) || Check(VbaTokenKind.Dot))
        {
            var span = CreateSpan(CurrentToken);
            var target = ParseAssignableExpression();
            if (Match(VbaTokenKind.Equal))
            {
                var value = ParseExpression();
                return new VbaAssignmentStatementSyntax(target, value, isSet, span);
            }

            var arguments = ParseOptionalArguments();
            if (arguments.Count > 0)
            {
                return new VbaCallStatementSyntax(target, arguments, span);
            }

            return new VbaExpressionStatementSyntax(target, span);
        }

        if (Match(VbaTokenKind.KeywordCall))
        {
            var span = CreateSpan(Previous());
            var target = ParseAssignableExpression();
            var arguments = ParseOptionalArguments();
            return new VbaCallStatementSyntax(target, arguments, span);
        }

        return null;
    }

    private VbaStatementSyntax ParseIfStatement(VbaSourceSpan span)
    {
        var condition = ParseExpression();
        Expect(VbaTokenKind.KeywordThen, "Expected Then after If condition.");
        SkipNewLines();

        var thenStatements = new List<VbaStatementSyntax>();
        var elseStatements = new List<VbaStatementSyntax>();

        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordElse))
            {
                Advance();
                SkipNewLines();
                break;
            }

            if (Check(VbaTokenKind.KeywordEnd) && CheckNext(VbaTokenKind.KeywordIf))
            {
                Advance();
                Advance();
                return new VbaIfStatementSyntax(condition, thenStatements, elseStatements, span);
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                thenStatements.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordEnd) && CheckNext(VbaTokenKind.KeywordIf))
            {
                Advance();
                Advance();
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                elseStatements.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return new VbaIfStatementSyntax(condition, thenStatements, elseStatements, span);
    }

    private VbaStatementSyntax ParseForStatement(VbaSourceSpan span)
    {
        var iterator = Expect(VbaTokenKind.Identifier, "Expected iterator name.").Text;
        Expect(VbaTokenKind.Equal, "Expected '=' after iterator.");
        var start = ParseExpression();
        Expect(VbaTokenKind.KeywordTo, "Expected To in For statement.");
        var end = ParseExpression();
        VbaExpressionSyntax? step = null;
        if (Match(VbaTokenKind.KeywordStep))
        {
            step = ParseExpression();
        }

        SkipNewLines();
        var body = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordNext))
            {
                Advance();
                if (Check(VbaTokenKind.Identifier))
                {
                    Advance();
                }

                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return new VbaForStatementSyntax(iterator, start, end, step, body, span);
    }

    private VbaStatementSyntax ParseDoStatement(VbaSourceSpan span)
    {
        var isUntil = false;
        if (Match(VbaTokenKind.KeywordWhile))
        {
            isUntil = false;
        }
        else if (Match(VbaTokenKind.KeywordUntil))
        {
            isUntil = true;
        }
        else
        {
            throw new VbaParseException("Expected While or Until after Do.", CurrentToken);
        }

        var condition = ParseExpression();
        SkipNewLines();

        var body = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordLoop))
            {
                Advance();
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return new VbaDoWhileStatementSyntax(condition, body, isUntil, span);
    }

    private VbaStatementSyntax ParseWhileStatement(VbaSourceSpan span)
    {
        var condition = ParseExpression();
        SkipNewLines();

        var body = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordWend))
            {
                Advance();
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return new VbaWhileStatementSyntax(condition, body, span);
    }

    private VbaStatementSyntax ParseSelectStatement(VbaSourceSpan span)
    {
        Expect(VbaTokenKind.KeywordCase, "Expected Case after Select.");
        var selector = ParseExpression();
        SkipNewLines();

        var cases = new List<VbaSelectCaseSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordEnd) && CheckNext(VbaTokenKind.KeywordSelect))
            {
                Advance();
                Advance();
                break;
            }

            if (!Match(VbaTokenKind.KeywordCase))
            {
                Advance();
                continue;
            }

            if (Match(VbaTokenKind.KeywordElse))
            {
                SkipNewLines();
                var elseBody = ParseCaseBody();
                cases.Add(new VbaSelectCaseSyntax(Array.Empty<VbaSelectCaseClauseSyntax>(), elseBody, true));
                continue;
            }

            var clauses = ParseCaseClauses();
            SkipNewLines();
            var body = ParseCaseBody();
            cases.Add(new VbaSelectCaseSyntax(clauses, body, false));
        }

        return new VbaSelectStatementSyntax(selector, cases, span);
    }

    private IReadOnlyList<VbaSelectCaseClauseSyntax> ParseCaseClauses()
    {
        var clauses = new List<VbaSelectCaseClauseSyntax>();
        while (!IsAtEnd())
        {
            if (Match(VbaTokenKind.KeywordIs))
            {
                var comparison = ParseSelectComparisonOperator();
                var value = ParseExpression();
                clauses.Add(new VbaSelectCaseComparisonClauseSyntax(comparison, value));
            }
            else
            {
                var start = ParseExpression();
                if (Match(VbaTokenKind.KeywordTo))
                {
                    var end = ParseExpression();
                    clauses.Add(new VbaSelectCaseRangeClauseSyntax(start, end));
                }
                else
                {
                    clauses.Add(new VbaSelectCaseValueClauseSyntax(start));
                }
            }

            if (!Match(VbaTokenKind.Comma))
            {
                break;
            }
        }

        return clauses;
    }

    private VbaSelectComparisonOperator ParseSelectComparisonOperator()
    {
        if (Match(VbaTokenKind.Equal))
        {
            return VbaSelectComparisonOperator.Equal;
        }

        if (Match(VbaTokenKind.NotEqual))
        {
            return VbaSelectComparisonOperator.NotEqual;
        }

        if (Match(VbaTokenKind.Less))
        {
            return VbaSelectComparisonOperator.Less;
        }

        if (Match(VbaTokenKind.LessEqual))
        {
            return VbaSelectComparisonOperator.LessOrEqual;
        }

        if (Match(VbaTokenKind.Greater))
        {
            return VbaSelectComparisonOperator.Greater;
        }

        if (Match(VbaTokenKind.GreaterEqual))
        {
            return VbaSelectComparisonOperator.GreaterOrEqual;
        }

        throw new VbaParseException("Expected comparison operator after Is.", CurrentToken);
    }

    private List<VbaStatementSyntax> ParseCaseBody()
    {
        var statements = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordCase)
                || (Check(VbaTokenKind.KeywordEnd) && CheckNext(VbaTokenKind.KeywordSelect)))
            {
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return statements;
    }

    private VbaStatementSyntax ParseWithStatement(VbaSourceSpan span)
    {
        var target = ParseExpression();
        SkipNewLines();

        var body = new List<VbaStatementSyntax>();
        while (!IsAtEnd())
        {
            SkipNewLines();
            if (Check(VbaTokenKind.KeywordEnd) && CheckNext(VbaTokenKind.KeywordWith))
            {
                Advance();
                Advance();
                break;
            }

            var statement = ParseStatement();
            if (statement is not null)
            {
                body.Add(statement);
            }
            else
            {
                Advance();
            }
        }

        return new VbaWithStatementSyntax(target, body, span);
    }

    private VbaStatementSyntax ParseDimStatement(VbaSourceSpan span)
    {
        var declarators = ParseVariableDeclarators();
        return new VbaDimStatementSyntax(declarators, span);
    }

    private VbaStatementSyntax ParseRedimStatement(VbaSourceSpan span)
    {
        var preserve = Match(VbaTokenKind.KeywordPreserve);
        var declarators = ParseVariableDeclarators();
        return new VbaRedimStatementSyntax(declarators, preserve, span);
    }

    private IReadOnlyList<VbaVariableDeclaratorSyntax> ParseVariableDeclarators()
    {
        var declarators = new List<VbaVariableDeclaratorSyntax>();
        while (!IsAtEnd())
        {
            if (!Check(VbaTokenKind.Identifier))
            {
                break;
            }

            var name = Advance().Text;
            var bounds = ParseOptionalArrayBounds();
            SkipTypeAnnotation();
            declarators.Add(new VbaVariableDeclaratorSyntax(name, bounds));

            if (!Match(VbaTokenKind.Comma))
            {
                break;
            }
        }

        return declarators;
    }

    private IReadOnlyList<VbaArrayBoundSyntax> ParseOptionalArrayBounds()
    {
        if (!Match(VbaTokenKind.LParen))
        {
            return Array.Empty<VbaArrayBoundSyntax>();
        }

        var bounds = new List<VbaArrayBoundSyntax>();
        if (!Check(VbaTokenKind.RParen))
        {
            do
            {
                var start = ParseExpression();
                if (Match(VbaTokenKind.KeywordTo))
                {
                    var end = ParseExpression();
                    bounds.Add(new VbaArrayBoundSyntax(start, end));
                }
                else
                {
                    bounds.Add(new VbaArrayBoundSyntax(null, start));
                }
            } while (Match(VbaTokenKind.Comma));
        }

        Expect(VbaTokenKind.RParen, "Expected ')' after array bounds.");
        return bounds;
    }

    private void SkipTypeAnnotation()
    {
        if (!Match(VbaTokenKind.KeywordAs))
        {
            return;
        }

        if (Check(VbaTokenKind.Identifier))
        {
            Advance();
        }
    }

    private VbaStatementSyntax ParseExitStatement(VbaSourceSpan span)
    {
        if (Match(VbaTokenKind.KeywordSub))
        {
            return new VbaExitStatementSyntax(VbaExitKind.Sub, span);
        }

        if (Match(VbaTokenKind.KeywordFunction))
        {
            return new VbaExitStatementSyntax(VbaExitKind.Function, span);
        }

        if (Match(VbaTokenKind.KeywordFor))
        {
            return new VbaExitStatementSyntax(VbaExitKind.For, span);
        }

        if (Match(VbaTokenKind.KeywordDo))
        {
            return new VbaExitStatementSyntax(VbaExitKind.Do, span);
        }

        return new VbaExitStatementSyntax(VbaExitKind.Sub, span);
    }

    private VbaStatementSyntax ParseOnErrorStatement(VbaSourceSpan span)
    {
        Expect(VbaTokenKind.KeywordError, "Expected Error after On.");
        if (Match(VbaTokenKind.KeywordResume))
        {
            Expect(VbaTokenKind.KeywordNext, "Expected Next after Resume.");
            return new VbaOnErrorStatementSyntax(VbaOnErrorMode.ResumeNext, span);
        }

        if (Match(VbaTokenKind.KeywordGoTo))
        {
            if (Match(VbaTokenKind.Number, out var numberToken)
                && string.Equals(numberToken.Text, "0", StringComparison.Ordinal))
            {
                return new VbaOnErrorStatementSyntax(VbaOnErrorMode.GoTo0, span);
            }

            throw new VbaParseException("Expected '0' after On Error GoTo.", CurrentToken);
        }

        throw new VbaParseException("Expected Resume Next or GoTo 0 after On Error.", CurrentToken);
    }

    private VbaExpressionSyntax ParseExpression()
    {
        return ParseOr();
    }

    private VbaExpressionSyntax ParseOr()
    {
        var expr = ParseAnd();
        while (Match(VbaTokenKind.KeywordOr))
        {
            var right = ParseAnd();
            expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Or, right);
        }

        return expr;
    }

    private VbaExpressionSyntax ParseAnd()
    {
        var expr = ParseComparison();
        while (Match(VbaTokenKind.KeywordAnd))
        {
            var right = ParseComparison();
            expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.And, right);
        }

        return expr;
    }

    private VbaExpressionSyntax ParseComparison()
    {
        var expr = ParseConcat();
        while (true)
        {
            if (Match(VbaTokenKind.Equal))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Equal, right);
                continue;
            }

            if (Match(VbaTokenKind.NotEqual))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.NotEqual, right);
                continue;
            }

            if (Match(VbaTokenKind.Less))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Less, right);
                continue;
            }

            if (Match(VbaTokenKind.LessEqual))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.LessOrEqual, right);
                continue;
            }

            if (Match(VbaTokenKind.Greater))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Greater, right);
                continue;
            }

            if (Match(VbaTokenKind.GreaterEqual))
            {
                var right = ParseConcat();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.GreaterOrEqual, right);
                continue;
            }

            break;
        }

        return expr;
    }

    private VbaExpressionSyntax ParseConcat()
    {
        var expr = ParseAddition();
        while (Match(VbaTokenKind.Ampersand))
        {
            var right = ParseAddition();
            expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Concat, right);
        }

        return expr;
    }

    private VbaExpressionSyntax ParseAddition()
    {
        var expr = ParseMultiplication();
        while (true)
        {
            if (Match(VbaTokenKind.Plus))
            {
                var right = ParseMultiplication();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Add, right);
                continue;
            }

            if (Match(VbaTokenKind.Minus))
            {
                var right = ParseMultiplication();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Subtract, right);
                continue;
            }

            break;
        }

        return expr;
    }

    private VbaExpressionSyntax ParseMultiplication()
    {
        var expr = ParseUnary();
        while (true)
        {
            if (Match(VbaTokenKind.Asterisk))
            {
                var right = ParseUnary();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Multiply, right);
                continue;
            }

            if (Match(VbaTokenKind.Slash))
            {
                var right = ParseUnary();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Divide, right);
                continue;
            }

            if (Match(VbaTokenKind.KeywordMod))
            {
                var right = ParseUnary();
                expr = new VbaBinaryExpressionSyntax(expr, VbaBinaryOperator.Modulo, right);
                continue;
            }

            break;
        }

        return expr;
    }

    private VbaExpressionSyntax ParseUnary()
    {
        if (Match(VbaTokenKind.Minus))
        {
            var operand = ParseUnary();
            return new VbaUnaryExpressionSyntax(VbaUnaryOperator.Negate, operand);
        }

        if (Match(VbaTokenKind.KeywordNot))
        {
            var operand = ParseUnary();
            return new VbaUnaryExpressionSyntax(VbaUnaryOperator.Not, operand);
        }

        return ParsePostfix();
    }

    private VbaExpressionSyntax ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(VbaTokenKind.Dot))
            {
                var member = Expect(VbaTokenKind.Identifier, "Expected member name.").Text;
                expr = new VbaMemberAccessExpressionSyntax(expr, member);
                continue;
            }

            if (Match(VbaTokenKind.LParen))
            {
                var arguments = new List<VbaExpressionSyntax>();
                if (!Check(VbaTokenKind.RParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Match(VbaTokenKind.Comma));
                }

                Expect(VbaTokenKind.RParen, "Expected ')' after arguments.");
                expr = new VbaCallExpressionSyntax(expr, arguments);
                continue;
            }

            break;
        }

        return expr;
    }

    private VbaExpressionSyntax ParsePrimary()
    {
        if (Match(VbaTokenKind.Number, out var numberToken))
        {
            return new VbaLiteralExpressionSyntax(new VbaLiteral(VbaLiteralKind.Number, numberToken.Text));
        }

        if (Match(VbaTokenKind.String, out var stringToken))
        {
            return new VbaLiteralExpressionSyntax(new VbaLiteral(VbaLiteralKind.String, stringToken.Text));
        }

        if (Match(VbaTokenKind.KeywordTrue))
        {
            return new VbaLiteralExpressionSyntax(new VbaLiteral(VbaLiteralKind.Boolean, "True"));
        }

        if (Match(VbaTokenKind.KeywordFalse))
        {
            return new VbaLiteralExpressionSyntax(new VbaLiteral(VbaLiteralKind.Boolean, "False"));
        }

        if (Match(VbaTokenKind.Dot))
        {
            var member = Expect(VbaTokenKind.Identifier, "Expected member name.").Text;
            var target = new VbaWithReferenceExpressionSyntax();
            return new VbaMemberAccessExpressionSyntax(target, member);
        }

        if (Match(VbaTokenKind.Identifier, out var identifierToken))
        {
            return new VbaIdentifierExpressionSyntax(identifierToken.Text);
        }

        if (Match(VbaTokenKind.LParen))
        {
            var expr = ParseExpression();
            Expect(VbaTokenKind.RParen, "Expected ')' after expression.");
            return new VbaParenthesizedExpressionSyntax(expr);
        }

        return new VbaLiteralExpressionSyntax(new VbaLiteral(VbaLiteralKind.Empty, string.Empty));
    }

    private VbaExpressionSyntax ParseAssignableExpression()
    {
        VbaExpressionSyntax expr;
        if (Match(VbaTokenKind.Dot))
        {
            var member = Expect(VbaTokenKind.Identifier, "Expected member name.").Text;
            expr = new VbaMemberAccessExpressionSyntax(new VbaWithReferenceExpressionSyntax(), member);
        }
        else
        {
            expr = new VbaIdentifierExpressionSyntax(Expect(VbaTokenKind.Identifier, "Expected identifier.").Text);
        }

        while (true)
        {
            if (Match(VbaTokenKind.Dot))
            {
                var member = Expect(VbaTokenKind.Identifier, "Expected member name.").Text;
                expr = new VbaMemberAccessExpressionSyntax(expr, member);
                continue;
            }

            if (Match(VbaTokenKind.LParen))
            {
                var arguments = new List<VbaExpressionSyntax>();
                if (!Check(VbaTokenKind.RParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Match(VbaTokenKind.Comma));
                }

                Expect(VbaTokenKind.RParen, "Expected ')' after arguments.");
                expr = new VbaCallExpressionSyntax(expr, arguments);
                continue;
            }

            break;
        }

        return expr;
    }

    private IReadOnlyList<VbaExpressionSyntax> ParseOptionalArguments()
    {
        if (Match(VbaTokenKind.LParen))
        {
            var arguments = new List<VbaExpressionSyntax>();
            if (!Check(VbaTokenKind.RParen))
            {
                do
                {
                    arguments.Add(ParseExpression());
                } while (Match(VbaTokenKind.Comma));
            }

            Expect(VbaTokenKind.RParen, "Expected ')' after arguments.");
            return arguments;
        }

        if (Check(VbaTokenKind.NewLine)
            || Check(VbaTokenKind.KeywordElse)
            || Check(VbaTokenKind.KeywordEnd)
            || Check(VbaTokenKind.KeywordLoop)
            || Check(VbaTokenKind.KeywordNext)
            || Check(VbaTokenKind.EndOfFile))
        {
            return Array.Empty<VbaExpressionSyntax>();
        }

        var implicitArguments = new List<VbaExpressionSyntax>();
        do
        {
            implicitArguments.Add(ParseExpression());
        } while (Match(VbaTokenKind.Comma));

        return implicitArguments;
    }

    private void SkipNewLines()
    {
        while (Match(VbaTokenKind.NewLine))
        {
        }
    }

    private bool Match(VbaTokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }

        return false;
    }

    private bool Match(VbaTokenKind kind, out VbaToken token)
    {
        if (Check(kind))
        {
            token = Advance();
            return true;
        }

        token = default;
        return false;
    }

    private VbaToken Expect(VbaTokenKind kind, string message)
    {
        if (Check(kind))
        {
            return Advance();
        }

        throw new VbaParseException(message, CurrentToken);
    }

    private bool Check(VbaTokenKind kind)
    {
        if (IsAtEnd())
        {
            return false;
        }

        return Peek().Kind == kind;
    }

    private bool CheckNext(VbaTokenKind kind)
    {
        if (_position + 1 >= _tokens.Count)
        {
            return false;
        }

        return _tokens[_position + 1].Kind == kind;
    }

    private VbaToken Advance()
    {
        if (!IsAtEnd())
        {
            _position++;
        }

        return Previous();
    }

    private bool IsAtEnd() => Peek().Kind == VbaTokenKind.EndOfFile;

    private VbaToken Peek() => _tokens[_position];

    private VbaToken Previous() => _tokens[_position - 1];

    private VbaToken CurrentToken => _tokens[Math.Min(_position, _tokens.Count - 1)];

    private static VbaSourceSpan CreateSpan(VbaToken token) => new(token.Line, token.Column);
}

public sealed class VbaParseException : Exception
{
    public VbaToken Token { get; }

    public VbaParseException(string message, VbaToken token)
        : base(message)
    {
        Token = token;
    }
}
