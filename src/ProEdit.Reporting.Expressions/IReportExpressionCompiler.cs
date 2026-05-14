namespace ProEdit.Reporting.Expressions;

/// <summary>
/// Compiles report expressions into reusable evaluators.
/// </summary>
public interface IReportExpressionCompiler
{
    /// <summary>
    /// Compiles the supplied expression text.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <returns>The compilation result.</returns>
    ReportExpressionCompilationResult Compile(string expression);
}

/// <summary>
/// Represents one compiled report expression.
/// </summary>
public interface ICompiledReportExpression
{
    /// <summary>
    /// Gets the original expression text.
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Evaluates the expression against the supplied runtime context.
    /// </summary>
    /// <param name="context">The runtime context.</param>
    /// <returns>The evaluated value.</returns>
    object? Evaluate(ReportExpressionContext context);

    /// <summary>
    /// Attempts to evaluate the expression against the supplied runtime context.
    /// </summary>
    /// <param name="context">The runtime context.</param>
    /// <param name="value">Receives the evaluated value.</param>
    /// <param name="diagnostic">Receives the emitted diagnostic when evaluation fails.</param>
    /// <returns><see langword="true" /> when evaluation succeeds; otherwise <see langword="false" />.</returns>
    bool TryEvaluate(
        ReportExpressionContext context,
        out object? value,
        out ReportDiagnostic? diagnostic);
}

/// <summary>
/// Represents the result of expression compilation.
/// </summary>
public sealed class ReportExpressionCompilationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExpressionCompilationResult" /> class.
    /// </summary>
    /// <param name="expression">The compiled expression.</param>
    /// <param name="diagnostics">The emitted diagnostics.</param>
    public ReportExpressionCompilationResult(
        ICompiledReportExpression? expression,
        IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        Expression = expression;
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Gets the compiled expression.
    /// </summary>
    public ICompiledReportExpression? Expression { get; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether the compilation emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}
