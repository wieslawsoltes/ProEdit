using System.Globalization;
using Vibe.Office.Reporting.Expressions;
using Xunit;

namespace Vibe.Office.Reporting.Expressions.Tests;

public sealed class ReportExpressionCompilerTests
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    [Fact]
    public void Compile_EvaluatesArithmeticAcrossParametersAndFields()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Parameters.Discount + Fields.Amount * 2");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var context = new ReportExpressionContext
        {
            Parameters = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["Discount"] = ReportParameterValue.FromScalar(1.5m)
            },
            Fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Amount"] = 5m
            },
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal(11.5m, Assert.IsType<decimal>(value));
    }

    [Fact]
    public void Compile_EvaluatesConditionalsFormattingAndGlobals()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Iif(Fields.Active and Fields.Amount > 10, Format(Fields.Amount, '0.00') + ' / ' + Upper(Globals.ReportName), 'n/a')");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var context = new ReportExpressionContext
        {
            Fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Active"] = true,
                ["Amount"] = 12.5m
            },
            Globals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReportName"] = "sales"
            },
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal("12.50 / SALES", Assert.IsType<string>(value));
    }

    [Fact]
    public void Compile_EvaluatesAggregatesAgainstScopeRows()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Sum(Fields.Amount) + Count()");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var scopeRows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Amount"] = 2m },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Amount"] = 3m },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Amount"] = 5m }
        };

        var context = new ReportExpressionContext
        {
            Fields = scopeRows[0],
            ScopeRows = scopeRows,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal(13m, Assert.IsType<decimal>(value));
    }

    [Fact]
    public void Compile_ReportsParseErrors()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Fields.Amount + )");

        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Expression);
        Assert.Contains(
            compilation.Diagnostics,
            diagnostic => diagnostic.Code == ReportDiagnosticCodes.ExpressionParseFailed);
    }

    [Fact]
    public void Compile_UsesExecutionTimeForNowAndToday()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Iif(Now() = Globals.ExecutionTime and Format(Today(), 'yyyy-MM-dd') = '2026-03-17', 'match', 'miss')");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var executionTime = new DateTimeOffset(2026, 3, 17, 10, 30, 0, TimeSpan.FromHours(1));
        var context = new ReportExpressionContext
        {
            Globals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExecutionTime"] = executionTime
            },
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1")
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal("match", Assert.IsType<string>(value));
    }

    [Fact]
    public void Compile_UsesExecutionCultureForUpperAndLower()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("Upper('i') + '/' + Lower('I')");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var turkishCulture = new CultureInfo("tr-TR");
        var context = new ReportExpressionContext
        {
            Culture = turkishCulture,
            UiCulture = turkishCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal("İ/ı", Assert.IsType<string>(value));
    }

    [Fact]
    public void Compile_EvaluatesParameterLabelsAndCurrentValue()
    {
        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile("ParameterLabel('Region') + ':' + Iif(CurrentValue() < 0, 'neg', 'pos')");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Expression);

        var context = new ReportExpressionContext
        {
            Parameters = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["Region"] = new ReportParameterValue
                {
                    Values = { "NW" },
                    Labels = { "Northwest" }
                }
            },
            SelfValue = -1m,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var value = compilation.Expression!.Evaluate(context);
        Assert.Equal("Northwest:neg", Assert.IsType<string>(value));
    }
}
