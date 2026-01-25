using System.Globalization;
using Vibe.Office.Vba;

namespace Vibe.Office.Vba.Runtime;

public sealed class VbaRuntime : IVbaDebugRuntime
{
    private readonly IVbaHost _host;
    private readonly Dictionary<string, BuiltinFunction> _builtins;

    public VbaRuntime(IVbaHost? host = null)
    {
        _host = host ?? new NullVbaHost();
        _builtins = CreateBuiltins();
    }

    public ValueTask<VbaRunResult> ExecuteAsync(
        string source,
        string? entryPoint,
        IReadOnlyList<VbaValue>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(source, entryPoint, arguments, null, cancellationToken);
    }

    public ValueTask<VbaRunResult> ExecuteDebugAsync(
        string source,
        string? entryPoint,
        IReadOnlyList<VbaValue>? arguments,
        VbaDebugSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ExecuteInternalAsync(source, entryPoint, arguments, session, cancellationToken);
    }

    private ValueTask<VbaRunResult> ExecuteInternalAsync(
        string source,
        string? entryPoint,
        IReadOnlyList<VbaValue>? arguments,
        VbaDebugSession? debugSession,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ValueTask.FromResult(new VbaRunResult(false, "Macro source is empty."));
        }

        VbaCompilation compilation;
        try
        {
            compilation = VbaCompiler.Compile(source);
        }
        catch (VbaParseException ex)
        {
            var span = new VbaSourceSpan(ex.Token.Line, ex.Token.Column);
            var diagnostic = new VbaDiagnostic(ex.Message, span, Array.Empty<VbaStackFrame>());
            debugSession?.NotifyStopped();
            return ValueTask.FromResult(new VbaRunResult(false, $"Parse error: {ex.Message}", diagnostic));
        }
        catch (Exception ex)
        {
            debugSession?.NotifyStopped();
            return ValueTask.FromResult(new VbaRunResult(false, $"Parse error: {ex.Message}"));
        }

        var procedures = BuildProcedures(compilation.Module);
        if (procedures.Count == 0)
        {
            debugSession?.NotifyStopped();
            return ValueTask.FromResult(new VbaRunResult(false, "No runnable procedures found."));
        }

        var target = ResolveEntryPoint(procedures, entryPoint);
        if (target is null)
        {
            debugSession?.NotifyStopped();
            return ValueTask.FromResult(new VbaRunResult(false, $"Entry point '{entryPoint}' not found."));
        }

        try
        {
            var context = new ExecutionContext(_host, _builtins, procedures, cancellationToken, debugSession);
            var callArgs = arguments is { Count: > 0 } ? arguments : Array.Empty<VbaValue>();
            var returnValue = context.ExecuteProcedure(target, callArgs);
            return ValueTask.FromResult(new VbaRunResult(true, ReturnValue: returnValue));
        }
        catch (VbaRuntimeException ex)
        {
            return ValueTask.FromResult(new VbaRunResult(false, ex.Message, ex.Diagnostic));
        }
        catch (OperationCanceledException)
        {
            return ValueTask.FromResult(new VbaRunResult(false, "Macro execution canceled."));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new VbaRunResult(false, $"Macro execution failed: {ex.Message}"));
        }
        finally
        {
            debugSession?.NotifyStopped();
        }
    }

    private static Dictionary<string, VbaProcedure> BuildProcedures(VbaModuleSyntax module)
    {
        var procedures = new Dictionary<string, VbaProcedure>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in module.Members)
        {
            switch (member)
            {
                case VbaSubroutineSyntax sub:
                    procedures[sub.Name] = new VbaProcedure(sub.Name, IsFunction: false, sub.Parameters, sub.Statements);
                    break;
                case VbaFunctionSyntax function:
                    procedures[function.Name] = new VbaProcedure(function.Name, IsFunction: true, function.Parameters, function.Statements);
                    break;
            }
        }

        return procedures;
    }

    private static VbaProcedure? ResolveEntryPoint(
        Dictionary<string, VbaProcedure> procedures,
        string? entryPoint)
    {
        if (!string.IsNullOrWhiteSpace(entryPoint) && procedures.TryGetValue(entryPoint, out var named))
        {
            return named;
        }

        foreach (var procedure in procedures.Values)
        {
            return procedure;
        }

        return null;
    }

    private static Dictionary<string, BuiltinFunction> CreateBuiltins()
    {
        return new Dictionary<string, BuiltinFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSGBOX"] = (_, args) =>
            {
                if (args.Count > 0)
                {
                    Console.WriteLine(args[0].AsString());
                }

                return VbaValue.FromDouble(0d);
            },
            ["DEBUG.PRINT"] = (_, args) =>
            {
                if (args.Count > 0)
                {
                    Console.WriteLine(args[0].AsString());
                }

                return VbaValue.Empty;
            },
            ["CSTR"] = (_, args) => VbaValue.FromString(args.Count > 0 ? args[0].AsString() : string.Empty),
            ["CDbl"] = (_, args) => VbaValue.FromDouble(args.Count > 0 ? args[0].AsDouble() : 0d),
            ["CLNG"] = (_, args) => VbaValue.FromDouble(args.Count > 0 ? Math.Round(args[0].AsDouble()) : 0d),
            ["CINT"] = (_, args) => VbaValue.FromDouble(args.Count > 0 ? Math.Round(args[0].AsDouble()) : 0d),
            ["CBOOL"] = (_, args) => VbaValue.FromBoolean(args.Count > 0 && args[0].AsBoolean()),
            ["LEN"] = (_, args) => VbaValue.FromDouble(args.Count > 0 ? args[0].AsString().Length : 0d),
            ["LCASE"] = (_, args) => VbaValue.FromString(args.Count > 0 ? args[0].AsString().ToLowerInvariant() : string.Empty),
            ["UCASE"] = (_, args) => VbaValue.FromString(args.Count > 0 ? args[0].AsString().ToUpperInvariant() : string.Empty),
            ["TRIM"] = (_, args) => VbaValue.FromString(args.Count > 0 ? args[0].AsString().Trim() : string.Empty),
            ["LEFT"] = (_, args) =>
            {
                var text = args.Count > 0 ? args[0].AsString() : string.Empty;
                var count = args.Count > 1 ? (int)Math.Max(0, args[1].AsDouble()) : text.Length;
                return VbaValue.FromString(count >= text.Length ? text : text.Substring(0, count));
            },
            ["RIGHT"] = (_, args) =>
            {
                var text = args.Count > 0 ? args[0].AsString() : string.Empty;
                var count = args.Count > 1 ? (int)Math.Max(0, args[1].AsDouble()) : text.Length;
                return VbaValue.FromString(count >= text.Length ? text : text.Substring(text.Length - count, count));
            },
            ["MID"] = (_, args) =>
            {
                var text = args.Count > 0 ? args[0].AsString() : string.Empty;
                var start = args.Count > 1 ? (int)Math.Max(1, args[1].AsDouble()) : 1;
                var length = args.Count > 2 ? (int)Math.Max(0, args[2].AsDouble()) : text.Length;
                var index = Math.Clamp(start - 1, 0, text.Length);
                var remaining = text.Length - index;
                var slice = Math.Clamp(length, 0, remaining);
                return VbaValue.FromString(slice == 0 ? string.Empty : text.Substring(index, slice));
            },
            ["ARRAY"] = (_, args) => CreateArrayFromArguments(args)
        };
    }

    private static VbaValue CreateArrayFromArguments(IReadOnlyList<VbaValue> args)
    {
        var upper = Math.Max(0, args.Count - 1);
        var array = new VbaArray(new[] { 0 }, new[] { upper });
        for (var i = 0; i < args.Count; i++)
        {
            array.Set(new[] { i }, args[i]);
        }

        return VbaValue.FromArray(array);
    }

    private sealed class ExecutionContext
    {
        private readonly IVbaHost _host;
        private readonly IReadOnlyDictionary<string, BuiltinFunction> _builtins;
        private readonly IReadOnlyDictionary<string, VbaProcedure> _procedures;
        private readonly CancellationToken _cancellationToken;
        private readonly VbaDebugSession? _debugSession;
        private readonly Stack<VbaStackFrame> _callStack = new();
        private readonly Stack<VbaWithContext> _withStack = new();
        private readonly VbaErrorState _errorState = new();

        public ExecutionContext(
            IVbaHost host,
            IReadOnlyDictionary<string, BuiltinFunction> builtins,
            IReadOnlyDictionary<string, VbaProcedure> procedures,
            CancellationToken cancellationToken,
            VbaDebugSession? debugSession)
        {
            _host = host;
            _builtins = builtins;
            _procedures = procedures;
            _cancellationToken = cancellationToken;
            _debugSession = debugSession;
        }

        private VbaRuntimeException CreateRuntimeException(string message, VbaFrame frame)
        {
            var diagnostic = new VbaDiagnostic(message, frame.CurrentSpan, BuildCallStack());
            return new VbaRuntimeException(message, diagnostic);
        }

        private void RaiseRuntimeError(VbaFrame frame, string message, int errorNumber = 5)
        {
            _errorState.SetError(errorNumber, message, frame.ProcedureName);
            throw CreateRuntimeException(message, frame);
        }

        private IReadOnlyList<VbaStackFrame> BuildCallStack()
        {
            if (_callStack.Count == 0)
            {
                return Array.Empty<VbaStackFrame>();
            }

            return _callStack.Reverse().ToArray();
        }

        private VbaDebugState BuildDebugState(VbaFrame frame, VbaSourceSpan span)
        {
            var location = new VbaDebugLocation(frame.ProcedureName, span);
            var callStack = BuildCallStack();
            var locals = frame.GetLocalsSnapshot();
            return new VbaDebugState(location, callStack, locals);
        }

        private VbaValue EvaluateImmediateExpression(VbaFrame frame, string expression)
        {
            var expr = VbaParser.ParseExpression(expression);
            return EvaluateExpression(frame, expr);
        }

        private bool TryGetWithPath(out string path)
        {
            if (_withStack.Count == 0)
            {
                path = string.Empty;
                return false;
            }

            var context = _withStack.Peek();
            if (context.Path is not null)
            {
                path = context.Path;
                return true;
            }

            path = string.Empty;
            return false;
        }

        public VbaValue ExecuteProcedure(
            VbaProcedure procedure,
            IReadOnlyList<VbaValue> arguments,
            VbaSourceSpan? callSite = null)
        {
            var frame = new VbaFrame(procedure.Name, procedure.IsFunction);
            BindParameters(frame, procedure.Parameters, arguments);

            _callStack.Push(new VbaStackFrame(procedure.Name, callSite));
            try
            {
                ExecuteStatements(frame, procedure.Statements);
            }
            catch (VbaExitException ex)
            {
                if (ex.Kind == VbaExitKind.For || ex.Kind == VbaExitKind.Do)
                {
                    throw;
                }
            }
            finally
            {
                _callStack.Pop();
            }

            return procedure.IsFunction ? frame.ReturnValue : VbaValue.Empty;
        }

        private void BindParameters(
            VbaFrame frame,
            IReadOnlyList<VbaParameterSyntax> parameters,
            IReadOnlyList<VbaValue> arguments)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                var value = i < arguments.Count ? arguments[i] : VbaValue.Empty;
                frame.SetVariable(parameter.Name, value);
            }
        }

        private void ExecuteStatements(VbaFrame frame, IReadOnlyList<VbaStatementSyntax> statements)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var span = statements[i].Span;
                    frame.CurrentSpan = span;
                    if (_debugSession is not null)
                    {
                        if (_debugSession.IsStopRequested)
                        {
                            throw new OperationCanceledException();
                        }

                        if (_debugSession.ShouldPause(frame.ProcedureName, span, _callStack.Count))
                        {
                            var state = BuildDebugState(frame, span);
                            _debugSession.Pause(state, _callStack.Count, expression => EvaluateImmediateExpression(frame, expression), _cancellationToken);
                            if (_debugSession.IsStopRequested)
                            {
                                throw new OperationCanceledException();
                            }
                        }
                    }

                    ExecuteStatement(frame, statements[i]);
                }
                catch (VbaRuntimeException ex)
                {
                    if (_errorState.Number == 0)
                    {
                        _errorState.SetError(5, ex.Message, frame.ProcedureName);
                    }

                    if (frame.ErrorHandling == VbaOnErrorMode.ResumeNext)
                    {
                        continue;
                    }

                    throw;
                }
            }
        }

        private void ExecuteStatement(VbaFrame frame, VbaStatementSyntax statement)
        {
            switch (statement)
            {
                case VbaAssignmentStatementSyntax assignment:
                    ExecuteAssignment(frame, assignment);
                    break;
                case VbaCallStatementSyntax call:
                    ExecuteCallStatement(frame, call);
                    break;
                case VbaExpressionStatementSyntax expr:
                    _ = EvaluateExpression(frame, expr.Expression);
                    break;
                case VbaIfStatementSyntax conditional:
                    ExecuteIf(frame, conditional);
                    break;
                case VbaForStatementSyntax loop:
                    ExecuteFor(frame, loop);
                    break;
                case VbaDoWhileStatementSyntax loop:
                    ExecuteDo(frame, loop);
                    break;
                case VbaWhileStatementSyntax loop:
                    ExecuteWhile(frame, loop);
                    break;
                case VbaSelectStatementSyntax selectStatement:
                    ExecuteSelect(frame, selectStatement);
                    break;
                case VbaWithStatementSyntax withStatement:
                    ExecuteWith(frame, withStatement);
                    break;
                case VbaDimStatementSyntax dim:
                    ExecuteDim(frame, dim);
                    break;
                case VbaRedimStatementSyntax redim:
                    ExecuteRedim(frame, redim);
                    break;
                case VbaExitStatementSyntax exitStatement:
                    throw new VbaExitException(exitStatement.Kind);
                case VbaOnErrorStatementSyntax onError:
                    frame.ErrorHandling = onError.Mode == VbaOnErrorMode.GoTo0
                        ? VbaOnErrorMode.None
                        : onError.Mode;
                    if (onError.Mode == VbaOnErrorMode.GoTo0)
                    {
                        _errorState.Clear();
                    }
                    break;
            }
        }

        private void ExecuteAssignment(VbaFrame frame, VbaAssignmentStatementSyntax assignment)
        {
            var value = EvaluateExpression(frame, assignment.Value);
            if (assignment.Target is VbaIdentifierExpressionSyntax identifier)
            {
                if (frame.IsFunction && string.Equals(identifier.Name, frame.ProcedureName, StringComparison.OrdinalIgnoreCase))
                {
                    frame.ReturnValue = value;
                    return;
                }

                frame.SetVariable(identifier.Name, value);
                return;
            }

            if (assignment.Target is VbaCallExpressionSyntax callTarget
                && TrySetArrayElement(frame, callTarget, value))
            {
                return;
            }

            if (TryResolveMemberPath(frame, assignment.Target, out var path))
            {
                if (TrySetIntrinsicMember(path, value))
                {
                    return;
                }

                if (!_host.TrySetMember(path, value))
                {
                    throw CreateRuntimeException($"Cannot set member '{path}'.", frame);
                }

                return;
            }

            throw CreateRuntimeException("Invalid assignment target.", frame);
        }

        private void ExecuteCallStatement(VbaFrame frame, VbaCallStatementSyntax call)
        {
            var arguments = EvaluateArguments(frame, call.Arguments);
            _ = InvokeTarget(frame, call.Target, arguments, out _);
        }

        private void ExecuteIf(VbaFrame frame, VbaIfStatementSyntax conditional)
        {
            var condition = EvaluateExpression(frame, conditional.Condition).AsBoolean();
            if (condition)
            {
                ExecuteStatements(frame, conditional.ThenStatements);
            }
            else if (conditional.ElseStatements.Count > 0)
            {
                ExecuteStatements(frame, conditional.ElseStatements);
            }
        }

        private void ExecuteFor(VbaFrame frame, VbaForStatementSyntax loop)
        {
            var start = EvaluateExpression(frame, loop.Start).AsDouble();
            var end = EvaluateExpression(frame, loop.End).AsDouble();
            var step = loop.Step is not null ? EvaluateExpression(frame, loop.Step).AsDouble() : 1d;
            if (Math.Abs(step) < double.Epsilon)
            {
                step = 1d;
            }

            var compare = step >= 0d
                ? (Func<double, bool>)(value => value <= end)
                : value => value >= end;

            for (var current = start; compare(current); current += step)
            {
                frame.SetVariable(loop.IteratorName, VbaValue.FromDouble(current));
                try
                {
                    ExecuteStatements(frame, loop.Body);
                }
                catch (VbaExitException ex)
                {
                    if (ex.Kind == VbaExitKind.For)
                    {
                        break;
                    }

                    throw;
                }
            }
        }

        private void ExecuteDo(VbaFrame frame, VbaDoWhileStatementSyntax loop)
        {
            while (true)
            {
                var condition = EvaluateExpression(frame, loop.Condition).AsBoolean();
                if (loop.IsUntil)
                {
                    condition = !condition;
                }

                if (!condition)
                {
                    break;
                }

                try
                {
                    ExecuteStatements(frame, loop.Body);
                }
                catch (VbaExitException ex)
                {
                    if (ex.Kind == VbaExitKind.Do)
                    {
                        break;
                    }

                    throw;
                }
            }
        }

        private void ExecuteWhile(VbaFrame frame, VbaWhileStatementSyntax loop)
        {
            while (EvaluateExpression(frame, loop.Condition).AsBoolean())
            {
                try
                {
                    ExecuteStatements(frame, loop.Body);
                }
                catch (VbaExitException ex)
                {
                    if (ex.Kind == VbaExitKind.Do)
                    {
                        break;
                    }

                    throw;
                }
            }
        }

        private void ExecuteSelect(VbaFrame frame, VbaSelectStatementSyntax selectStatement)
        {
            var selector = EvaluateExpression(frame, selectStatement.Selector);
            foreach (var selectCase in selectStatement.Cases)
            {
                if (selectCase.IsElse)
                {
                    ExecuteStatements(frame, selectCase.Body);
                    return;
                }

                if (MatchesSelectCase(frame, selector, selectCase.Clauses))
                {
                    ExecuteStatements(frame, selectCase.Body);
                    return;
                }
            }
        }

        private bool MatchesSelectCase(
            VbaFrame frame,
            VbaValue selector,
            IReadOnlyList<VbaSelectCaseClauseSyntax> clauses)
        {
            foreach (var clause in clauses)
            {
                switch (clause)
                {
                    case VbaSelectCaseValueClauseSyntax valueClause:
                    {
                        var value = EvaluateExpression(frame, valueClause.Value);
                        if (CompareValues(selector, value) == 0)
                        {
                            return true;
                        }

                        break;
                    }
                    case VbaSelectCaseRangeClauseSyntax rangeClause:
                    {
                        var start = EvaluateExpression(frame, rangeClause.Start);
                        var end = EvaluateExpression(frame, rangeClause.End);
                        if (CompareValues(selector, start) >= 0 && CompareValues(selector, end) <= 0)
                        {
                            return true;
                        }

                        break;
                    }
                    case VbaSelectCaseComparisonClauseSyntax comparisonClause:
                    {
                        var value = EvaluateExpression(frame, comparisonClause.Value);
                        if (MatchesComparison(selector, comparisonClause.Operator, value))
                        {
                            return true;
                        }

                        break;
                    }
                }
            }

            return false;
        }

        private static bool MatchesComparison(
            VbaValue left,
            VbaSelectComparisonOperator op,
            VbaValue right)
        {
            var comparison = CompareValues(left, right);
            return op switch
            {
                VbaSelectComparisonOperator.Equal => comparison == 0,
                VbaSelectComparisonOperator.NotEqual => comparison != 0,
                VbaSelectComparisonOperator.Less => comparison < 0,
                VbaSelectComparisonOperator.LessOrEqual => comparison <= 0,
                VbaSelectComparisonOperator.Greater => comparison > 0,
                VbaSelectComparisonOperator.GreaterOrEqual => comparison >= 0,
                _ => false
            };
        }

        private void ExecuteWith(VbaFrame frame, VbaWithStatementSyntax withStatement)
        {
            var target = EvaluateExpression(frame, withStatement.Target);
            if (target.Kind != VbaValueKind.Object || target.AsObjectPath() is null)
            {
                RaiseRuntimeError(frame, "With requires an object reference.");
            }

            _withStack.Push(new VbaWithContext(target, target.AsObjectPath()));
            try
            {
                ExecuteStatements(frame, withStatement.Body);
            }
            finally
            {
                _withStack.Pop();
            }
        }

        private void ExecuteDim(VbaFrame frame, VbaDimStatementSyntax dim)
        {
            foreach (var declarator in dim.Declarators)
            {
                if (string.IsNullOrWhiteSpace(declarator.Name))
                {
                    continue;
                }

                if (declarator.Bounds.Count > 0)
                {
                    var array = CreateArrayFromDeclarator(frame, declarator);
                    frame.SetVariable(declarator.Name, VbaValue.FromArray(array));
                }
                else
                {
                    frame.SetVariable(declarator.Name, VbaValue.Empty);
                }
            }
        }

        private void ExecuteRedim(VbaFrame frame, VbaRedimStatementSyntax redim)
        {
            foreach (var declarator in redim.Declarators)
            {
                if (string.IsNullOrWhiteSpace(declarator.Name))
                {
                    continue;
                }

                var array = CreateArrayFromDeclarator(frame, declarator);
                if (redim.Preserve
                    && frame.TryGetVariable(declarator.Name, out var existing)
                    && existing.Kind == VbaValueKind.Array
                    && existing.AsArray() is { } existingArray)
                {
                    CopyArrayPreserve(existingArray, array);
                }

                frame.SetVariable(declarator.Name, VbaValue.FromArray(array));
            }
        }

        private VbaValue EvaluateExpression(VbaFrame frame, VbaExpressionSyntax expression)
        {
            switch (expression)
            {
                case VbaLiteralExpressionSyntax literal:
                    return EvaluateLiteral(literal.Value);
                case VbaIdentifierExpressionSyntax identifier:
                    return ResolveIdentifier(frame, identifier.Name);
                case VbaMemberAccessExpressionSyntax member:
                    return ResolveMember(frame, member);
                case VbaCallExpressionSyntax call:
                    return EvaluateCallExpression(frame, call);
                case VbaUnaryExpressionSyntax unary:
                    return ApplyUnary(frame, unary);
                case VbaBinaryExpressionSyntax binary:
                    return ApplyBinary(frame, binary);
                case VbaParenthesizedExpressionSyntax parenthesized:
                    return EvaluateExpression(frame, parenthesized.Expression);
                case VbaWithReferenceExpressionSyntax:
                    return _withStack.Count > 0 ? _withStack.Peek().Value : VbaValue.Empty;
                default:
                    return VbaValue.Empty;
            }
        }

        private VbaValue EvaluateLiteral(VbaLiteral literal)
        {
            return literal.Kind switch
            {
                VbaLiteralKind.Number => VbaValue.FromDouble(ParseDouble(literal.Text)),
                VbaLiteralKind.String => VbaValue.FromString(literal.Text),
                VbaLiteralKind.Boolean => VbaValue.FromBoolean(
                    string.Equals(literal.Text, "TRUE", StringComparison.OrdinalIgnoreCase)),
                _ => VbaValue.Empty
            };
        }

        private VbaValue ResolveIdentifier(VbaFrame frame, string name)
        {
            if (string.Equals(name, "Err", StringComparison.OrdinalIgnoreCase))
            {
                return VbaValue.FromObjectPath("Err");
            }

            if (frame.TryGetVariable(name, out var value))
            {
                return value;
            }

            if (_host.TryGetMember(name, out var hostValue))
            {
                return hostValue;
            }

            frame.SetVariable(name, VbaValue.Empty);
            return VbaValue.Empty;
        }

        private VbaValue ResolveMember(VbaFrame frame, VbaMemberAccessExpressionSyntax member)
        {
            if (!TryResolveMemberPath(frame, member, out var path))
            {
                throw CreateRuntimeException($"Unknown member '{member.MemberName}'.", frame);
            }

            if (TryGetIntrinsicMember(path, out var intrinsic))
            {
                return intrinsic;
            }

            if (_host.TryGetMember(path, out var value))
            {
                return value;
            }

            throw CreateRuntimeException($"Unknown member '{path}'.", frame);
        }

        private VbaValue EvaluateCallExpression(VbaFrame frame, VbaCallExpressionSyntax call)
        {
            var arguments = EvaluateArguments(frame, call.Arguments);
            if (TryGetArrayElement(frame, call, arguments, out var arrayResult))
            {
                return arrayResult;
            }

            if (!InvokeTarget(frame, call.Target, arguments, out var result))
            {
                throw CreateRuntimeException("Call target not found.", frame);
            }

            return result;
        }

        private VbaValue ApplyUnary(VbaFrame frame, VbaUnaryExpressionSyntax unary)
        {
            var operand = EvaluateExpression(frame, unary.Operand);
            return unary.Operator switch
            {
                VbaUnaryOperator.Negate => VbaValue.FromDouble(-operand.AsDouble()),
                VbaUnaryOperator.Not => VbaValue.FromBoolean(!operand.AsBoolean()),
                _ => VbaValue.Empty
            };
        }

        private VbaValue ApplyBinary(VbaFrame frame, VbaBinaryExpressionSyntax binary)
        {
            var left = EvaluateExpression(frame, binary.Left);
            var right = EvaluateExpression(frame, binary.Right);
            return binary.Operator switch
            {
                VbaBinaryOperator.Add => ApplyAdd(left, right),
                VbaBinaryOperator.Subtract => VbaValue.FromDouble(left.AsDouble() - right.AsDouble()),
                VbaBinaryOperator.Multiply => VbaValue.FromDouble(left.AsDouble() * right.AsDouble()),
                VbaBinaryOperator.Divide => VbaValue.FromDouble(left.AsDouble() / right.AsDouble()),
                VbaBinaryOperator.Modulo => VbaValue.FromDouble(left.AsDouble() % right.AsDouble()),
                VbaBinaryOperator.Concat => VbaValue.FromString(left.AsString() + right.AsString()),
                VbaBinaryOperator.Equal => VbaValue.FromBoolean(CompareValues(left, right) == 0),
                VbaBinaryOperator.NotEqual => VbaValue.FromBoolean(CompareValues(left, right) != 0),
                VbaBinaryOperator.Less => VbaValue.FromBoolean(CompareValues(left, right) < 0),
                VbaBinaryOperator.LessOrEqual => VbaValue.FromBoolean(CompareValues(left, right) <= 0),
                VbaBinaryOperator.Greater => VbaValue.FromBoolean(CompareValues(left, right) > 0),
                VbaBinaryOperator.GreaterOrEqual => VbaValue.FromBoolean(CompareValues(left, right) >= 0),
                VbaBinaryOperator.And => VbaValue.FromBoolean(left.AsBoolean() && right.AsBoolean()),
                VbaBinaryOperator.Or => VbaValue.FromBoolean(left.AsBoolean() || right.AsBoolean()),
                _ => VbaValue.Empty
            };
        }

        private static VbaValue ApplyAdd(VbaValue left, VbaValue right)
        {
            if (left.Kind == VbaValueKind.String || right.Kind == VbaValueKind.String)
            {
                return VbaValue.FromString(left.AsString() + right.AsString());
            }

            return VbaValue.FromDouble(left.AsDouble() + right.AsDouble());
        }

        private static int CompareValues(VbaValue left, VbaValue right)
        {
            if (left.Kind == VbaValueKind.String || right.Kind == VbaValueKind.String)
            {
                return string.Compare(left.AsString(), right.AsString(), StringComparison.OrdinalIgnoreCase);
            }

            return left.AsDouble().CompareTo(right.AsDouble());
        }

        private IReadOnlyList<VbaValue> EvaluateArguments(VbaFrame frame, IReadOnlyList<VbaExpressionSyntax> arguments)
        {
            if (arguments.Count == 0)
            {
                return Array.Empty<VbaValue>();
            }

            var values = new VbaValue[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                values[i] = EvaluateExpression(frame, arguments[i]);
            }

            return values;
        }

        private bool TryGetArrayElement(
            VbaFrame frame,
            VbaCallExpressionSyntax call,
            IReadOnlyList<VbaValue> arguments,
            out VbaValue result)
        {
            result = VbaValue.Empty;
            if (!TryResolveArrayTarget(frame, call.Target, out var array))
            {
                return false;
            }

            var indices = BuildArrayIndices(arguments);
            result = array.Get(indices);
            return true;
        }

        private bool TrySetArrayElement(VbaFrame frame, VbaCallExpressionSyntax call, VbaValue value)
        {
            if (!TryResolveArrayTarget(frame, call.Target, out var array))
            {
                return false;
            }

            var indices = BuildArrayIndices(EvaluateArguments(frame, call.Arguments));
            array.Set(indices, value);
            return true;
        }

        private static int[] BuildArrayIndices(IReadOnlyList<VbaValue> arguments)
        {
            if (arguments.Count == 0)
            {
                return Array.Empty<int>();
            }

            var indices = new int[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                indices[i] = (int)Math.Round(arguments[i].AsDouble());
            }

            return indices;
        }

        private bool TryResolveArrayTarget(VbaFrame frame, VbaExpressionSyntax target, out VbaArray array)
        {
            array = null!;
            if (target is VbaIdentifierExpressionSyntax identifier
                && frame.TryGetVariable(identifier.Name, out var value)
                && value.Kind == VbaValueKind.Array
                && value.AsArray() is { } localArray)
            {
                array = localArray;
                return true;
            }

            if (target is VbaMemberAccessExpressionSyntax member
                && TryResolveMemberPath(frame, member, out var path)
                && _host.TryGetMember(path, out var hostValue)
                && hostValue.Kind == VbaValueKind.Array
                && hostValue.AsArray() is { } hostArray)
            {
                array = hostArray;
                return true;
            }

            return false;
        }

        private VbaArray CreateArrayFromDeclarator(VbaFrame frame, VbaVariableDeclaratorSyntax declarator)
        {
            var bounds = declarator.Bounds;
            if (bounds.Count == 0)
            {
                return new VbaArray(new[] { 0 }, new[] { -1 });
            }

            var lowerBounds = new int[bounds.Count];
            var upperBounds = new int[bounds.Count];
            for (var i = 0; i < bounds.Count; i++)
            {
                var bound = bounds[i];
                var upper = EvaluateExpression(frame, bound.Upper).AsDouble();
                var lower = bound.Lower is null ? 0d : EvaluateExpression(frame, bound.Lower).AsDouble();
                lowerBounds[i] = (int)Math.Round(lower);
                upperBounds[i] = (int)Math.Round(upper);
            }

            return new VbaArray(lowerBounds, upperBounds);
        }

        private static void CopyArrayPreserve(VbaArray source, VbaArray target)
        {
            if (source.Rank != target.Rank)
            {
                return;
            }

            var rank = source.Rank;
            var indices = new int[rank];
            CopyPreserveRecursive(source, target, 0, indices);
        }

        private static void CopyPreserveRecursive(
            VbaArray source,
            VbaArray target,
            int dimension,
            int[] indices)
        {
            if (dimension >= indices.Length)
            {
                var value = source.Get(indices);
                target.Set(indices, value);
                return;
            }

            var lower = Math.Max(source.LowerBounds[dimension], target.LowerBounds[dimension]);
            var upper = Math.Min(source.UpperBounds[dimension], target.UpperBounds[dimension]);
            for (var i = lower; i <= upper; i++)
            {
                indices[dimension] = i;
                CopyPreserveRecursive(source, target, dimension + 1, indices);
            }
        }

        private bool InvokeTarget(
            VbaFrame frame,
            VbaExpressionSyntax target,
            IReadOnlyList<VbaValue> arguments,
            out VbaValue result)
        {
            if (target is VbaIdentifierExpressionSyntax identifier)
            {
                if (_builtins.TryGetValue(identifier.Name, out var builtin))
                {
                    result = builtin(frame, arguments);
                    return true;
                }

                if (_procedures.TryGetValue(identifier.Name, out var procedure))
                {
                    result = ExecuteProcedure(procedure, arguments, frame.CurrentSpan);
                    return true;
                }

                if (_host.TryInvokeMember(identifier.Name, arguments, out var hostResult))
                {
                    result = hostResult;
                    return true;
                }
            }

            if (TryResolveMemberPath(frame, target, out var path)
                )
            {
                if (_builtins.TryGetValue(path, out var builtin))
                {
                    result = builtin(frame, arguments);
                    return true;
                }

                if (TryInvokeIntrinsic(path, frame, arguments, out var intrinsicResult))
                {
                    result = intrinsicResult;
                    return true;
                }

                if (_host.TryInvokeMember(path, arguments, out var invoked))
                {
                    result = invoked;
                    return true;
                }
            }

            result = VbaValue.Empty;
            return false;
        }

        private bool TryResolveMemberPath(VbaFrame frame, VbaExpressionSyntax expression, out string path)
        {
            if (expression is VbaWithReferenceExpressionSyntax)
            {
                if (TryGetWithPath(out path))
                {
                    return true;
                }

                path = string.Empty;
                return false;
            }

            if (expression is VbaIdentifierExpressionSyntax identifier)
            {
                if (frame.TryGetVariable(identifier.Name, out var value))
                {
                    if (value.Kind == VbaValueKind.Object && value.AsObjectPath() is { } objectPath)
                    {
                        path = objectPath;
                        return true;
                    }

                    path = string.Empty;
                    return false;
                }

                path = identifier.Name;
                return true;
            }

            if (expression is VbaMemberAccessExpressionSyntax member
                && TryResolveMemberPath(frame, member.Target, out var targetPath))
            {
                path = $"{targetPath}.{member.MemberName}";
                return true;
            }

            var resolvedValue = EvaluateExpression(frame, expression);
            if (resolvedValue.Kind == VbaValueKind.Object && resolvedValue.AsObjectPath() is { } resolvedPath)
            {
                path = resolvedPath;
                return true;
            }

            path = string.Empty;
            return false;
        }

        private bool TryGetIntrinsicMember(string path, out VbaValue value)
        {
            value = VbaValue.Empty;
            if (string.Equals(path, "Err", StringComparison.OrdinalIgnoreCase))
            {
                value = VbaValue.FromObjectPath("Err");
                return true;
            }

            if (!path.StartsWith("Err.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var member = path.Substring(4);
            if (string.Equals(member, "Number", StringComparison.OrdinalIgnoreCase))
            {
                value = VbaValue.FromDouble(_errorState.Number);
                return true;
            }

            if (string.Equals(member, "Description", StringComparison.OrdinalIgnoreCase))
            {
                value = VbaValue.FromString(_errorState.Description);
                return true;
            }

            if (string.Equals(member, "Source", StringComparison.OrdinalIgnoreCase))
            {
                value = VbaValue.FromString(_errorState.Source ?? string.Empty);
                return true;
            }

            return false;
        }

        private bool TrySetIntrinsicMember(string path, VbaValue value)
        {
            if (!path.StartsWith("Err.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var member = path.Substring(4);
            if (string.Equals(member, "Number", StringComparison.OrdinalIgnoreCase))
            {
                _errorState.SetError((int)Math.Round(value.AsDouble()), _errorState.Description, _errorState.Source);
                return true;
            }

            if (string.Equals(member, "Description", StringComparison.OrdinalIgnoreCase))
            {
                _errorState.SetError(_errorState.Number, value.AsString(), _errorState.Source);
                return true;
            }

            if (string.Equals(member, "Source", StringComparison.OrdinalIgnoreCase))
            {
                _errorState.SetError(_errorState.Number, _errorState.Description, value.AsString());
                return true;
            }

            return false;
        }

        private bool TryInvokeIntrinsic(
            string path,
            VbaFrame frame,
            IReadOnlyList<VbaValue> arguments,
            out VbaValue result)
        {
            result = VbaValue.Empty;
            if (string.Equals(path, "Err.Clear", StringComparison.OrdinalIgnoreCase))
            {
                _errorState.Clear();
                return true;
            }

            if (string.Equals(path, "Err.Raise", StringComparison.OrdinalIgnoreCase))
            {
                var number = arguments.Count > 0 ? (int)Math.Round(arguments[0].AsDouble()) : 1;
                var source = arguments.Count > 1 ? arguments[1].AsString() : frame.ProcedureName;
                var description = arguments.Count > 2 ? arguments[2].AsString() : "Error";
                _errorState.SetError(number, description, source);
                throw CreateRuntimeException(description, frame);
            }

            return false;
        }

        private static double ParseDouble(string text)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0d;
        }
    }

    private sealed class NullVbaHost : IVbaHost
    {
        public bool TryInvokeMember(string name, IReadOnlyList<VbaValue> arguments, out VbaValue result)
        {
            result = VbaValue.Empty;
            return false;
        }

        public bool TryGetMember(string name, out VbaValue result)
        {
            result = VbaValue.Empty;
            return false;
        }

        public bool TrySetMember(string name, VbaValue value) => false;
    }

    private sealed record VbaProcedure(
        string Name,
        bool IsFunction,
        IReadOnlyList<VbaParameterSyntax> Parameters,
        IReadOnlyList<VbaStatementSyntax> Statements);

    private delegate VbaValue BuiltinFunction(VbaFrame frame, IReadOnlyList<VbaValue> arguments);

    private sealed class VbaFrame
    {
        private readonly Dictionary<string, VbaValue> _locals = new(StringComparer.OrdinalIgnoreCase);

        public string ProcedureName { get; }
        public bool IsFunction { get; }
        public VbaValue ReturnValue { get; set; }
        public VbaOnErrorMode ErrorHandling { get; set; } = VbaOnErrorMode.None;
        public VbaSourceSpan? CurrentSpan { get; set; }

        public VbaFrame(string procedureName, bool isFunction)
        {
            ProcedureName = procedureName;
            IsFunction = isFunction;
            ReturnValue = VbaValue.Empty;
        }

        public bool TryGetVariable(string name, out VbaValue value)
        {
            return _locals.TryGetValue(name, out value);
        }

        public void SetVariable(string name, VbaValue value)
        {
            _locals[name] = value;
        }

        public IReadOnlyDictionary<string, VbaValue> GetLocalsSnapshot()
        {
            return new Dictionary<string, VbaValue>(_locals, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record VbaWithContext(VbaValue Value, string? Path);

    private sealed class VbaErrorState
    {
        public int Number { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public string? Source { get; private set; }

        public void SetError(int number, string description, string? source)
        {
            Number = number;
            Description = description ?? string.Empty;
            Source = source;
        }

        public void Clear()
        {
            Number = 0;
            Description = string.Empty;
            Source = null;
        }
    }

    private sealed class VbaExitException : Exception
    {
        public VbaExitKind Kind { get; }

        public VbaExitException(VbaExitKind kind)
        {
            Kind = kind;
        }
    }

    private sealed class VbaRuntimeException : Exception
    {
        public VbaDiagnostic Diagnostic { get; }

        public VbaRuntimeException(string message, VbaDiagnostic diagnostic)
            : base(message)
        {
            Diagnostic = diagnostic;
        }
    }
}
