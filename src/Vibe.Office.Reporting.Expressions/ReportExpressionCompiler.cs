using System.Collections.Concurrent;

namespace Vibe.Office.Reporting.Expressions;

/// <summary>
/// Default implementation of <see cref="IReportExpressionCompiler" />.
/// </summary>
public sealed class ReportExpressionCompiler : IReportExpressionCompiler
{
    private readonly ConcurrentDictionary<string, ReportExpressionCompilationResult> _cache =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ReportExpressionCompilationResult Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new ReportExpressionCompilationResult(
                null,
                new[]
                {
                    new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ExpressionParseFailed,
                        "Expression text is empty.",
                        "$")
                });
        }

        return _cache.GetOrAdd(expression, static text => CompileCore(text));
    }

    private static ReportExpressionCompilationResult CompileCore(string expression)
    {
        var diagnostics = new List<ReportDiagnostic>();
        var tokenizer = new ReportExpressionTokenizer(expression, diagnostics);
        var tokens = tokenizer.Tokenize();
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error))
        {
            return new ReportExpressionCompilationResult(null, diagnostics);
        }

        var parser = new ReportExpressionParser(tokens, expression, diagnostics);
        var root = parser.Parse();
        if (root is null || diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error))
        {
            return new ReportExpressionCompilationResult(null, diagnostics);
        }

        return new ReportExpressionCompilationResult(
            new CompiledReportExpression(expression, root),
            diagnostics);
    }

    private sealed class CompiledReportExpression : ICompiledReportExpression
    {
        private readonly ReportExpressionNode _root;

        public CompiledReportExpression(string text, ReportExpressionNode root)
        {
            Text = text;
            _root = root;
        }

        public string Text { get; }

        public object? Evaluate(ReportExpressionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return ReportExpressionRuntime.Evaluate(_root, context);
        }

        public bool TryEvaluate(
            ReportExpressionContext context,
            out object? value,
            out ReportDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                value = Evaluate(context);
                diagnostic = null;
                return true;
            }
            catch (ReportExpressionEvaluationException ex)
            {
                value = null;
                diagnostic = new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ExpressionEvaluationFailed,
                    ex.Message,
                    Text);
                return false;
            }
        }
    }
}
