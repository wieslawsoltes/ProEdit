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

        if (style.BackgroundGradientType.HasValue)
        {
            resolved.BackgroundGradientType = style.BackgroundGradientType.Value;
        }

        if (!string.IsNullOrWhiteSpace(style.BackgroundGradientEndColor))
        {
            resolved.BackgroundGradientEndColor = style.BackgroundGradientEndColor;
        }
        else if (!string.IsNullOrWhiteSpace(style.BackgroundGradientEndColorExpression)
            && TryEvaluateColorExpression(style.BackgroundGradientEndColorExpression, context, path, diagnostics, out var gradientEndColor))
        {
            resolved.BackgroundGradientEndColor = gradientEndColor;
        }

        if (style.Bold.HasValue)
        {
            resolved.Bold = style.Bold.Value;
        }

        if (style.Italic.HasValue)
        {
            resolved.Italic = style.Italic.Value;
        }

        resolved.Border = ResolveBorder(
            resolved.Border,
            style.Border,
            context,
            path,
            diagnostics);
        resolved.TopBorder = ResolveBorder(
            resolved.TopBorder,
            style.TopBorder,
            context,
            path,
            diagnostics);
        resolved.BottomBorder = ResolveBorder(
            resolved.BottomBorder,
            style.BottomBorder,
            context,
            path,
            diagnostics);
        resolved.LeftBorder = ResolveBorder(
            resolved.LeftBorder,
            style.LeftBorder,
            context,
            path,
            diagnostics);
        resolved.RightBorder = ResolveBorder(
            resolved.RightBorder,
            style.RightBorder,
            context,
            path,
            diagnostics);

        if (style.PaddingLeft.HasValue)
        {
            resolved.PaddingLeft = style.PaddingLeft.Value;
        }

        if (style.PaddingRight.HasValue)
        {
            resolved.PaddingRight = style.PaddingRight.Value;
        }

        if (style.PaddingTop.HasValue)
        {
            resolved.PaddingTop = style.PaddingTop.Value;
        }

        if (style.PaddingBottom.HasValue)
        {
            resolved.PaddingBottom = style.PaddingBottom.Value;
        }

        if (style.TextAlign.HasValue)
        {
            resolved.TextAlign = style.TextAlign.Value;
        }

        if (style.VerticalAlign.HasValue)
        {
            resolved.VerticalAlign = style.VerticalAlign.Value;
        }

        if (style.TextDecoration.HasValue)
        {
            resolved.TextDecoration = style.TextDecoration.Value;
        }

        resolved.Border = NormalizeBorder(resolved.Border);
        resolved.TopBorder = ResolveSideBorder(resolved.Border, resolved.TopBorder);
        resolved.BottomBorder = ResolveSideBorder(resolved.Border, resolved.BottomBorder);
        resolved.LeftBorder = ResolveSideBorder(resolved.Border, resolved.LeftBorder);
        resolved.RightBorder = ResolveSideBorder(resolved.Border, resolved.RightBorder);

        return resolved;
    }

    private static MaterializedReportBorder? ResolveBorder(
        MaterializedReportBorder? current,
        ReportBorderDefinition? definition,
        ReportExpressionContext? context,
        string? path,
        List<ReportDiagnostic>? diagnostics)
    {
        if (definition is null)
        {
            return current?.Clone();
        }

        var resolved = current?.Clone() ?? new MaterializedReportBorder();
        if (!string.IsNullOrWhiteSpace(definition.Color))
        {
            resolved.Color = definition.Color;
        }
        else if (!string.IsNullOrWhiteSpace(definition.ColorExpression)
            && TryEvaluateColorExpression(definition.ColorExpression, context, path, diagnostics, out var color))
        {
            resolved.Color = color;
        }

        if (definition.Style.HasValue)
        {
            resolved.Style = definition.Style.Value;
        }

        if (definition.Width.HasValue)
        {
            resolved.Width = definition.Width.Value;
        }

        return resolved;
    }

    private static MaterializedReportBorder? ResolveSideBorder(
        MaterializedReportBorder? border,
        MaterializedReportBorder? sideBorder)
    {
        if (border is null)
        {
            return NormalizeBorder(sideBorder);
        }

        if (sideBorder is null)
        {
            return NormalizeBorder(border);
        }

        var resolved = border.Clone();
        if (!string.IsNullOrWhiteSpace(sideBorder.Color))
        {
            resolved.Color = sideBorder.Color;
        }

        if (sideBorder.Style.HasValue)
        {
            resolved.Style = sideBorder.Style.Value;
        }

        if (sideBorder.Width.HasValue)
        {
            resolved.Width = sideBorder.Width.Value;
        }

        if (!sideBorder.Style.HasValue
            && sideBorder.Width.HasValue
            && resolved.Style == ReportBorderLineStyle.None)
        {
            resolved.Style = ReportBorderLineStyle.Solid;
        }

        return NormalizeBorder(resolved);
    }

    private static MaterializedReportBorder? NormalizeBorder(MaterializedReportBorder? border)
    {
        if (border is null)
        {
            return null;
        }

        if (!border.Style.HasValue
            && (!string.IsNullOrWhiteSpace(border.Color) || border.Width.HasValue))
        {
            border.Style = ReportBorderLineStyle.Solid;
        }

        if (border.Style == ReportBorderLineStyle.None)
        {
            return border;
        }

        if (!border.Width.HasValue)
        {
            border.Width = 1f;
        }

        return border;
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
