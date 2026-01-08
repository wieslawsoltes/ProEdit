using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed class DocumentStyleResolver
{
    private readonly Document _document;
    private readonly Dictionary<string, ParagraphStyleProperties> _paragraphPropertiesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextStyleProperties> _paragraphRunPropertiesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextStyleProperties> _characterPropertiesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TableStyleDefinition> _tableStyleCache = new(StringComparer.OrdinalIgnoreCase);

    public DocumentStyleResolver(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public ParagraphProperties ResolveParagraphProperties(ParagraphBlock paragraph)
    {
        var resolved = new ParagraphProperties();
        if (_document.DefaultParagraphStyleProperties.HasValues)
        {
            ApplyParagraphStyleProperties(resolved, _document.DefaultParagraphStyleProperties);
        }

        var styleId = paragraph.StyleId ?? _document.Styles.DefaultParagraphStyleId;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            var styleProperties = ResolveParagraphStyleProperties(styleId);
            ApplyParagraphStyleProperties(resolved, styleProperties);
        }

        ApplyDirectParagraphProperties(resolved, paragraph.Properties);
        if (resolved.TabStops.Count > 1)
        {
            resolved.TabStops.Sort();
        }
        return resolved;
    }

    public TextStyle ResolveParagraphTextStyle(ParagraphBlock paragraph, TextStyle defaultStyle)
    {
        var resolved = defaultStyle.Clone();
        var hasExplicitFontFamily = false;
        var hasExplicitThemeFont = false;
        ApplyDefaultCharacterStyle(resolved, ref hasExplicitFontFamily, ref hasExplicitThemeFont);

        var styleId = paragraph.StyleId ?? _document.Styles.DefaultParagraphStyleId;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            var runProperties = ResolveParagraphRunProperties(styleId);
            UpdateFontFlags(runProperties, ref hasExplicitFontFamily, ref hasExplicitThemeFont);
            runProperties.ApplyTo(resolved);
        }

        ResolveThemeFont(resolved, hasExplicitFontFamily, hasExplicitThemeFont);
        return resolved;
    }

    public TextStyle ResolveRunStyle(ParagraphBlock paragraph, RunInline run, TextStyle paragraphStyle)
    {
        return ResolveRunStyle(run.StyleId, run.Style, paragraphStyle);
    }

    public TextStyle ResolveRunStyle(string? styleId, TextStyleProperties? runProperties, TextStyle paragraphStyle)
    {
        var resolved = paragraphStyle.Clone();
        var hasExplicitFontFamily = false;
        var hasExplicitThemeFont = false;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            var styleProperties = ResolveCharacterStyleProperties(styleId);
            UpdateFontFlags(styleProperties, ref hasExplicitFontFamily, ref hasExplicitThemeFont);
            styleProperties.ApplyTo(resolved);
        }

        if (runProperties is not null)
        {
            UpdateFontFlags(runProperties, ref hasExplicitFontFamily, ref hasExplicitThemeFont);
            runProperties.ApplyTo(resolved);
        }

        ResolveThemeFont(resolved, hasExplicitFontFamily, hasExplicitThemeFont);
        return resolved;
    }

    public TableStyleDefinition? ResolveTableStyle(TableBlock table)
    {
        var styleId = table.StyleId ?? _document.Styles.DefaultTableStyleId;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        return ResolveTableStyleDefinition(styleId);
    }

    private void ApplyDefaultCharacterStyle(TextStyle style, ref bool hasExplicitFontFamily, ref bool hasExplicitThemeFont)
    {
        var defaultCharacterStyleId = _document.Styles.DefaultCharacterStyleId;
        if (string.IsNullOrWhiteSpace(defaultCharacterStyleId))
        {
            return;
        }

        var runProperties = ResolveCharacterStyleProperties(defaultCharacterStyleId);
        UpdateFontFlags(runProperties, ref hasExplicitFontFamily, ref hasExplicitThemeFont);
        runProperties.ApplyTo(style);
    }

    private static void UpdateFontFlags(TextStyleProperties properties, ref bool hasExplicitFontFamily, ref bool hasExplicitThemeFont)
    {
        if (!string.IsNullOrWhiteSpace(properties.FontFamily)
            || !string.IsNullOrWhiteSpace(properties.FontFamilyAscii)
            || !string.IsNullOrWhiteSpace(properties.FontFamilyHighAnsi)
            || !string.IsNullOrWhiteSpace(properties.FontFamilyEastAsia)
            || !string.IsNullOrWhiteSpace(properties.FontFamilyComplexScript))
        {
            hasExplicitFontFamily = true;
        }

        if (properties.ThemeFontAscii.HasValue
            || properties.ThemeFontHighAnsi.HasValue
            || properties.ThemeFontEastAsia.HasValue
            || properties.ThemeFontComplexScript.HasValue)
        {
            hasExplicitThemeFont = true;
        }
    }

    private void ResolveThemeFont(TextStyle style, bool hasExplicitFontFamily, bool hasExplicitThemeFont)
    {
        if (!hasExplicitThemeFont)
        {
            return;
        }

        var themeFonts = _document.Fonts.Theme;
        if (style.ThemeFontAscii.HasValue && themeFonts.TryGet(style.ThemeFontAscii.Value, out var family)
            && string.IsNullOrWhiteSpace(style.FontFamilyAscii))
        {
            style.FontFamilyAscii = family;
        }

        if (style.ThemeFontHighAnsi.HasValue && themeFonts.TryGet(style.ThemeFontHighAnsi.Value, out family)
            && string.IsNullOrWhiteSpace(style.FontFamilyHighAnsi))
        {
            style.FontFamilyHighAnsi = family;
        }

        if (style.ThemeFontEastAsia.HasValue && themeFonts.TryGet(style.ThemeFontEastAsia.Value, out family)
            && string.IsNullOrWhiteSpace(style.FontFamilyEastAsia))
        {
            style.FontFamilyEastAsia = family;
        }

        if (style.ThemeFontComplexScript.HasValue && themeFonts.TryGet(style.ThemeFontComplexScript.Value, out family)
            && string.IsNullOrWhiteSpace(style.FontFamilyComplexScript))
        {
            style.FontFamilyComplexScript = family;
        }

        if (!hasExplicitFontFamily && string.IsNullOrWhiteSpace(style.FontFamily))
        {
            style.FontFamily = style.FontFamilyAscii
                               ?? style.FontFamilyHighAnsi
                               ?? style.FontFamilyEastAsia
                               ?? style.FontFamilyComplexScript
                               ?? style.FontFamily;
        }
    }

    private ParagraphStyleProperties ResolveParagraphStyleProperties(string styleId)
    {
        if (_paragraphPropertiesCache.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var resolved = new ParagraphStyleProperties();
        foreach (var style in EnumerateParagraphStyleChain(styleId))
        {
            ApplyParagraphStyleProperties(resolved, style.ParagraphProperties);
        }

        _paragraphPropertiesCache[styleId] = resolved;
        return resolved;
    }

    private TextStyleProperties ResolveParagraphRunProperties(string styleId)
    {
        if (_paragraphRunPropertiesCache.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var resolved = new TextStyleProperties();
        foreach (var style in EnumerateParagraphStyleChain(styleId))
        {
            ApplyTextStyleProperties(resolved, style.RunProperties);
        }

        _paragraphRunPropertiesCache[styleId] = resolved;
        return resolved;
    }

    private TextStyleProperties ResolveCharacterStyleProperties(string styleId)
    {
        if (_characterPropertiesCache.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var resolved = new TextStyleProperties();
        foreach (var style in EnumerateCharacterStyleChain(styleId))
        {
            ApplyTextStyleProperties(resolved, style.RunProperties);
        }

        _characterPropertiesCache[styleId] = resolved;
        return resolved;
    }

    private TableStyleDefinition ResolveTableStyleDefinition(string styleId)
    {
        if (_tableStyleCache.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var resolved = new TableStyleDefinition(styleId);
        foreach (var style in EnumerateTableStyleChain(styleId))
        {
            if (!string.IsNullOrWhiteSpace(style.Name))
            {
                resolved.Name = style.Name;
            }

            if (!string.IsNullOrWhiteSpace(style.BasedOnId))
            {
                resolved.BasedOnId = style.BasedOnId;
            }

            ApplyTableProperties(resolved.TableProperties, style.TableProperties);
            ApplyTableCellProperties(resolved.CellProperties, style.CellProperties);

            foreach (var (condition, conditionProperties) in style.Conditions)
            {
                if (!resolved.Conditions.TryGetValue(condition, out var mergedCondition))
                {
                    mergedCondition = new TableStyleConditionProperties();
                    resolved.Conditions[condition] = mergedCondition;
                }

                ApplyTableProperties(mergedCondition.TableProperties, conditionProperties.TableProperties);
                ApplyTableCellProperties(mergedCondition.CellProperties, conditionProperties.CellProperties);
            }
        }

        _tableStyleCache[styleId] = resolved;
        return resolved;
    }

    private IEnumerable<ParagraphStyleDefinition> EnumerateParagraphStyleChain(string styleId)
    {
        var stack = new Stack<ParagraphStyleDefinition>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = styleId;

        while (!string.IsNullOrWhiteSpace(current)
               && _document.Styles.ParagraphStyles.TryGetValue(current, out var style)
               && visited.Add(current))
        {
            stack.Push(style);
            current = style.BasedOnId;
        }

        while (stack.Count > 0)
        {
            yield return stack.Pop();
        }
    }

    private IEnumerable<CharacterStyleDefinition> EnumerateCharacterStyleChain(string styleId)
    {
        var stack = new Stack<CharacterStyleDefinition>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = styleId;

        while (!string.IsNullOrWhiteSpace(current)
               && _document.Styles.CharacterStyles.TryGetValue(current, out var style)
               && visited.Add(current))
        {
            stack.Push(style);
            current = style.BasedOnId;
        }

        while (stack.Count > 0)
        {
            yield return stack.Pop();
        }
    }

    private IEnumerable<TableStyleDefinition> EnumerateTableStyleChain(string styleId)
    {
        var stack = new Stack<TableStyleDefinition>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = styleId;

        while (!string.IsNullOrWhiteSpace(current)
               && _document.Styles.TableStyles.TryGetValue(current, out var style)
               && visited.Add(current))
        {
            stack.Push(style);
            current = style.BasedOnId;
        }

        while (stack.Count > 0)
        {
            yield return stack.Pop();
        }
    }

    private static void ApplyParagraphStyleProperties(ParagraphProperties target, ParagraphStyleProperties source)
    {
        if (source.Alignment.HasValue)
        {
            target.Alignment = source.Alignment;
        }

        if (source.SpacingBefore.HasValue)
        {
            target.SpacingBefore = source.SpacingBefore;
        }

        if (source.SpacingAfter.HasValue)
        {
            target.SpacingAfter = source.SpacingAfter;
        }

        if (source.LineSpacing.HasValue)
        {
            target.LineSpacing = source.LineSpacing;
        }

        if (source.LineSpacingRule.HasValue)
        {
            target.LineSpacingRule = source.LineSpacingRule;
        }

        if (source.IndentLeft.HasValue)
        {
            target.IndentLeft = source.IndentLeft;
        }

        if (source.IndentRight.HasValue)
        {
            target.IndentRight = source.IndentRight;
        }

        if (source.FirstLineIndent.HasValue)
        {
            target.FirstLineIndent = source.FirstLineIndent;
        }

        if (source.TabStops.Count > 0)
        {
            target.TabStops.Clear();
            foreach (var tabStop in source.TabStops)
            {
                target.TabStops.Add(tabStop.Clone());
            }
        }

        if (source.KeepWithNext.HasValue)
        {
            target.KeepWithNext = source.KeepWithNext;
        }

        if (source.KeepLinesTogether.HasValue)
        {
            target.KeepLinesTogether = source.KeepLinesTogether;
        }

        if (source.WidowControl.HasValue)
        {
            target.WidowControl = source.WidowControl;
        }

        if (source.PageBreakBefore.HasValue)
        {
            target.PageBreakBefore = source.PageBreakBefore;
        }

        if (source.ContextualSpacing.HasValue)
        {
            target.ContextualSpacing = source.ContextualSpacing;
        }

        if (source.Bidi.HasValue)
        {
            target.Bidi = source.Bidi;
        }

        if (source.TextDirection.HasValue)
        {
            target.TextDirection = source.TextDirection;
        }

        if (source.EastAsianLayout?.HasValues == true)
        {
            target.EastAsianLayout = source.EastAsianLayout.Clone();
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        ApplyParagraphBorders(target.Borders, source.Borders);
    }

    private static void ApplyParagraphStyleProperties(ParagraphStyleProperties target, ParagraphStyleProperties source)
    {
        if (source.Alignment.HasValue)
        {
            target.Alignment = source.Alignment;
        }

        if (source.SpacingBefore.HasValue)
        {
            target.SpacingBefore = source.SpacingBefore;
        }

        if (source.SpacingAfter.HasValue)
        {
            target.SpacingAfter = source.SpacingAfter;
        }

        if (source.LineSpacing.HasValue)
        {
            target.LineSpacing = source.LineSpacing;
        }

        if (source.LineSpacingRule.HasValue)
        {
            target.LineSpacingRule = source.LineSpacingRule;
        }

        if (source.IndentLeft.HasValue)
        {
            target.IndentLeft = source.IndentLeft;
        }

        if (source.IndentRight.HasValue)
        {
            target.IndentRight = source.IndentRight;
        }

        if (source.FirstLineIndent.HasValue)
        {
            target.FirstLineIndent = source.FirstLineIndent;
        }

        if (source.TabStops.Count > 0)
        {
            target.TabStops.Clear();
            foreach (var tabStop in source.TabStops)
            {
                target.TabStops.Add(tabStop.Clone());
            }
        }

        if (source.KeepWithNext.HasValue)
        {
            target.KeepWithNext = source.KeepWithNext;
        }

        if (source.KeepLinesTogether.HasValue)
        {
            target.KeepLinesTogether = source.KeepLinesTogether;
        }

        if (source.WidowControl.HasValue)
        {
            target.WidowControl = source.WidowControl;
        }

        if (source.PageBreakBefore.HasValue)
        {
            target.PageBreakBefore = source.PageBreakBefore;
        }

        if (source.ContextualSpacing.HasValue)
        {
            target.ContextualSpacing = source.ContextualSpacing;
        }

        if (source.Bidi.HasValue)
        {
            target.Bidi = source.Bidi;
        }

        if (source.TextDirection.HasValue)
        {
            target.TextDirection = source.TextDirection;
        }

        if (source.EastAsianLayout?.HasValues == true)
        {
            target.EastAsianLayout = source.EastAsianLayout.Clone();
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        ApplyParagraphBorders(target.Borders, source.Borders);
    }

    private static void ApplyDirectParagraphProperties(ParagraphProperties target, ParagraphProperties source)
    {
        if (source.Alignment.HasValue)
        {
            target.Alignment = source.Alignment;
        }

        if (source.SpacingBefore.HasValue)
        {
            target.SpacingBefore = source.SpacingBefore;
        }

        if (source.SpacingAfter.HasValue)
        {
            target.SpacingAfter = source.SpacingAfter;
        }

        if (source.LineSpacing.HasValue)
        {
            target.LineSpacing = source.LineSpacing;
        }

        if (source.LineSpacingRule.HasValue)
        {
            target.LineSpacingRule = source.LineSpacingRule;
        }

        if (source.IndentLeft.HasValue)
        {
            target.IndentLeft = source.IndentLeft;
        }

        if (source.IndentRight.HasValue)
        {
            target.IndentRight = source.IndentRight;
        }

        if (source.FirstLineIndent.HasValue)
        {
            target.FirstLineIndent = source.FirstLineIndent;
        }

        if (source.TabStops.Count > 0)
        {
            target.TabStops.Clear();
            foreach (var tabStop in source.TabStops)
            {
                target.TabStops.Add(tabStop.Clone());
            }
        }

        if (source.KeepWithNext.HasValue)
        {
            target.KeepWithNext = source.KeepWithNext;
        }

        if (source.KeepLinesTogether.HasValue)
        {
            target.KeepLinesTogether = source.KeepLinesTogether;
        }

        if (source.WidowControl.HasValue)
        {
            target.WidowControl = source.WidowControl;
        }

        if (source.PageBreakBefore.HasValue)
        {
            target.PageBreakBefore = source.PageBreakBefore;
        }

        if (source.ContextualSpacing.HasValue)
        {
            target.ContextualSpacing = source.ContextualSpacing;
        }

        if (source.Bidi.HasValue)
        {
            target.Bidi = source.Bidi;
        }

        if (source.TextDirection.HasValue)
        {
            target.TextDirection = source.TextDirection;
        }

        if (source.EastAsianLayout?.HasValues == true)
        {
            target.EastAsianLayout = source.EastAsianLayout.Clone();
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        ApplyParagraphBorders(target.Borders, source.Borders);
    }

    private static void ApplyTextStyleProperties(TextStyleProperties target, TextStyleProperties source)
    {
        if (!string.IsNullOrWhiteSpace(source.FontFamily))
        {
            target.FontFamily = source.FontFamily;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamilyAscii))
        {
            target.FontFamilyAscii = source.FontFamilyAscii;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamilyHighAnsi))
        {
            target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamilyEastAsia))
        {
            target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamilyComplexScript))
        {
            target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        }

        if (source.FontSize.HasValue)
        {
            target.FontSize = source.FontSize;
        }

        if (source.FontSizeComplexScript.HasValue)
        {
            target.FontSizeComplexScript = source.FontSizeComplexScript;
        }

        if (source.FontWeight.HasValue)
        {
            target.FontWeight = source.FontWeight;
        }

        if (source.FontStyle.HasValue)
        {
            target.FontStyle = source.FontStyle;
        }

        if (source.Color.HasValue)
        {
            target.Color = source.Color;
        }

        if (source.VerticalPosition.HasValue)
        {
            target.VerticalPosition = source.VerticalPosition;
        }

        if (source.SmallCaps.HasValue)
        {
            target.SmallCaps = source.SmallCaps;
        }

        if (source.Underline.HasValue)
        {
            target.Underline = source.Underline;
        }

        if (source.UnderlineStyle.HasValue)
        {
            target.UnderlineStyle = source.UnderlineStyle;
            target.Underline = source.UnderlineStyle.Value != DocUnderlineStyle.None;
        }

        if (source.UnderlineColor.HasValue)
        {
            target.UnderlineColor = source.UnderlineColor;
        }

        if (source.Strikethrough.HasValue)
        {
            target.Strikethrough = source.Strikethrough;
        }

        if (source.HighlightColor.HasValue)
        {
            target.HighlightColor = source.HighlightColor;
        }

        if (source.ThemeFontAscii.HasValue)
        {
            target.ThemeFontAscii = source.ThemeFontAscii;
        }

        if (source.ThemeFontHighAnsi.HasValue)
        {
            target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        }

        if (source.ThemeFontEastAsia.HasValue)
        {
            target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        }

        if (source.ThemeFontComplexScript.HasValue)
        {
            target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        }

        if (!string.IsNullOrWhiteSpace(source.Language))
        {
            target.Language = source.Language;
        }

        if (!string.IsNullOrWhiteSpace(source.LanguageEastAsia))
        {
            target.LanguageEastAsia = source.LanguageEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(source.LanguageBidi))
        {
            target.LanguageBidi = source.LanguageBidi;
        }
    }

    private static void ApplyParagraphBorders(ParagraphBorders target, ParagraphBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties source)
    {
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        if (source.CellPadding.HasValue)
        {
            target.CellPadding = MergePadding(target.CellPadding, source.CellPadding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        if (source.Look is not null)
        {
            target.Look = source.Look.Clone();
        }

        ApplyTableBorders(target.Borders, source.Borders);
    }

    private static void ApplyTableBorders(TableBorders target, TableBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }

        if (source.InsideHorizontal is not null)
        {
            target.InsideHorizontal = source.InsideHorizontal.Clone();
        }

        if (source.InsideVertical is not null)
        {
            target.InsideVertical = source.InsideVertical.Clone();
        }
    }

    private static void ApplyTableCellProperties(TableCellProperties target, TableCellProperties source)
    {
        if (source.Padding.HasValue)
        {
            target.Padding = MergePadding(target.Padding, source.Padding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        if (source.VerticalAlignment.HasValue)
        {
            target.VerticalAlignment = source.VerticalAlignment;
        }

        ApplyTableCellBorders(target.Borders, source.Borders);
    }

    private static void ApplyTableCellBorders(TableCellBorders target, TableCellBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }
    }

    private static DocThickness MergePadding(DocThickness? basePadding, DocThickness overridePadding)
    {
        if (!basePadding.HasValue)
        {
            return overridePadding;
        }

        var value = basePadding.Value;
        return new DocThickness(
            float.IsNaN(overridePadding.Left) ? value.Left : overridePadding.Left,
            float.IsNaN(overridePadding.Top) ? value.Top : overridePadding.Top,
            float.IsNaN(overridePadding.Right) ? value.Right : overridePadding.Right,
            float.IsNaN(overridePadding.Bottom) ? value.Bottom : overridePadding.Bottom);
    }
}
