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
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        if (_cache.TryGetValue(styleId, out var cached))
        {
            return cached?.Clone();
        }

        var resolved = ResolveCore(styleId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _cache[styleId] = resolved?.Clone();
        return resolved;
    }

    private MaterializedReportStyle? ResolveCore(
        string styleId,
        HashSet<string> stack)
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
            resolved = ResolveCore(style.ParentStyleId, stack) ?? new MaterializedReportStyle();
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

        if (!string.IsNullOrWhiteSpace(style.Background))
        {
            resolved.Background = style.Background;
        }

        if (style.Bold.HasValue)
        {
            resolved.Bold = style.Bold.Value;
        }

        if (style.Italic.HasValue)
        {
            resolved.Italic = style.Italic.Value;
        }

        return resolved;
    }
}
