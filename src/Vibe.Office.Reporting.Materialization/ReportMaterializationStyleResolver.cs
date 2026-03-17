using Vibe.Office.Reporting.Expressions;

namespace Vibe.Office.Reporting.Materialization;

internal sealed class ReportMaterializationStyleResolver
{
    private readonly Dictionary<string, ReportStyleDefinition> _styles;
    private readonly Dictionary<string, MaterializedReportStyle?> _cache;

    public ReportMaterializationStyleResolver(IReadOnlyList<ReportStyleDefinition> styles)
    {
        _styles = new Dictionary<string, ReportStyleDefinition>(StringComparer.OrdinalIgnoreCase);
        _cache = new Dictionary<string, MaterializedReportStyle?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < styles.Count; index++)
        {
            var style = styles[index];
            if (string.IsNullOrWhiteSpace(style.Id))
            {
                continue;
            }

            _styles[style.Id] = style;
        }
    }

    public MaterializedReportStyle? Resolve(string? styleId)
    {
        return Resolve(styleId, context: null, path: null, diagnostics: null);
    }

    public MaterializedReportStyle? Resolve(
        string? styleId,
        ReportExpressionContext? context,
        string? path,
        List<ReportDiagnostic>? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        if (context is null && _cache.TryGetValue(styleId, out var cached))
        {
            return cached?.Clone();
        }

        var resolved = ResolveCore(styleId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), context, path, diagnostics);
        if (context is null)
        {
            _cache[styleId] = resolved?.Clone();
        }

        return resolved?.Clone();
    }

    private MaterializedReportStyle? ResolveCore(
        string styleId,
        HashSet<string> stack,
        ReportExpressionContext? context,
        string? path,
        List<ReportDiagnostic>? diagnostics)
    {
        if (!_styles.TryGetValue(styleId, out var style))
        {
            return null;
        }

        if (!stack.Add(styleId))
        {
            return null;
        }

        MaterializedReportStyle resolved;
        if (!string.IsNullOrWhiteSpace(style.ParentStyleId))
        {
            resolved = ResolveCore(style.ParentStyleId, stack, context, path, diagnostics) ?? new MaterializedReportStyle();
        }
        else
        {
            resolved = new MaterializedReportStyle();
        }

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            resolved.FontFamily = style.FontFamily;
        }

        if (style.FontSize.HasValue)
        {
            resolved.FontSize = style.FontSize.Value;
        }

        if (!string.IsNullOrWhiteSpace(style.Foreground))
        {
            resolved.Foreground = style.Foreground;
        }
        else if (!string.IsNullOrWhiteSpace(style.ForegroundExpression)
            && TryEvaluateColorExpression(style.ForegroundExpression, context, path, diagnostics, out var foreground))
        {
            resolved.Foreground = foreground;
        }

        if (!string.IsNullOrWhiteSpace(style.Background))
        {
            resolved.Background = style.Background;
        }
        else if (!string.IsNullOrWhiteSpace(style.BackgroundExpression)
            && TryEvaluateColorExpression(style.BackgroundExpression, context, path, diagnostics, out var background))
        {
            resolved.Background = background;
        }

        if (style.Bold.HasValue)
        {
            resolved.Bold = style.Bold.Value;
        }

        if (style.Italic.HasValue)
        {
            resolved.Italic = style.Italic.Value;
        }

        if (style.TextAlign.HasValue)
        {
            resolved.TextAlign = style.TextAlign.Value;
        }

        return resolved;
    }

    private static bool TryEvaluateColorExpression(
        string expression,
        ReportExpressionContext? context,
        string? path,
        List<ReportDiagnostic>? diagnostics,
        out string? value)
    {
        value = null;
        if (context is null)
        {
            return false;
        }

        var compiler = new ReportExpressionCompiler();
        var compilation = compiler.Compile(expression);
        if (compilation.Expression is null || compilation.HasErrors)
        {
            if (diagnostics is not null)
            {
                AppendDiagnostics(diagnostics, compilation.Diagnostics, path);
            }

            return false;
        }

        if (!compilation.Expression.TryEvaluate(context, out var rawValue, out var diagnostic))
        {
            if (diagnostics is not null && diagnostic is not null)
            {
                diagnostics.Add(path is null ? diagnostic : CloneDiagnostic(diagnostic, path));
            }

            return false;
        }

        value = rawValue switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, context.Culture),
            _ => Convert.ToString(rawValue, context.Culture)
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void AppendDiagnostics(
        List<ReportDiagnostic> diagnostics,
        IReadOnlyList<ReportDiagnostic> source,
        string? path)
    {
        for (var index = 0; index < source.Count; index++)
        {
            diagnostics.Add(path is null ? source[index] : CloneDiagnostic(source[index], path));
        }
    }

    private static ReportDiagnostic CloneDiagnostic(ReportDiagnostic diagnostic, string path)
    {
        return new ReportDiagnostic(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            path);
    }
}
