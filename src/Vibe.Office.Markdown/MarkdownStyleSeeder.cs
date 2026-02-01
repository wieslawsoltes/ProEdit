using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Markdown;

internal static class MarkdownStyleSeeder
{
    private static readonly DocColor MarkdownTextColor = new DocColor(36, 41, 46);
    private static readonly DocColor MarkdownMutedTextColor = new DocColor(87, 96, 106);
    private static readonly DocColor MarkdownBorderColor = new DocColor(223, 226, 229);
    private static readonly DocColor MarkdownCodeBackground = new DocColor(246, 248, 250);
    private static readonly DocColor MarkdownLinkColor = new DocColor(9, 105, 218);

    internal static void Seed(Document document, MarkdownOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);

        ApplyDocumentDefaults(document);

        var styles = document.Styles;
        styles.ParagraphStyles.Clear();
        styles.CharacterStyles.Clear();
        styles.TableStyles.Clear();
        styles.DefaultParagraphStyleId = MarkdownStyleIds.Normal;
        styles.DefaultCharacterStyleId = null;
        styles.DefaultTableStyleId = null;

        AddParagraphStyle(styles, CreateNormalStyle());
        for (var level = 1; level <= 6; level++)
        {
            AddParagraphStyle(styles, CreateHeadingStyle(level));
        }

        AddParagraphStyle(styles, CreateBlockQuoteStyle());
        AddParagraphStyle(styles, CreateCodeBlockStyle());
        AddParagraphStyle(styles, CreateListParagraphStyle());
        AddParagraphStyle(styles, CreateTableCellStyle());
        AddParagraphStyle(styles, CreateTableHeaderStyle());

        AddCharacterStyle(styles, CreateCodeInlineStyle());
        AddCharacterStyle(styles, CreateHyperlinkStyle());

        if (SupportsTables(options))
        {
            AddTableStyle(styles, CreateTableStyle());
            styles.DefaultTableStyleId = MarkdownStyleIds.MarkdownTable;
        }
    }

    private static bool SupportsTables(MarkdownOptions? options)
    {
        var effective = options ?? new MarkdownOptions();
        return effective.Flavor == MarkdownFlavor.GitHub && effective.UseGfmTables;
    }

    private static ParagraphStyleDefinition CreateNormalStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.Normal)
        {
            Name = "Normal",
            NextStyleId = MarkdownStyleIds.Normal,
            PrimaryStyle = true,
            QuickStyle = true
        };
        style.RunProperties.FontSize = MarkdownStyleDefaults.PointsToDips(MarkdownStyleDefaults.NormalFontSizePoints);
        style.RunProperties.Color = MarkdownTextColor;
        style.ParagraphProperties.LineSpacing = MarkdownStyleDefaults.LineSpacingTwips;
        style.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        style.ParagraphProperties.SpacingBefore = 0f;
        style.ParagraphProperties.SpacingAfter = MarkdownStyleDefaults.ParagraphSpacingAfterDips;
        return style;
    }

    private static ParagraphStyleDefinition CreateHeadingStyle(int level)
    {
        var index = Math.Clamp(level, 1, 6) - 1;
        var sizePoints = MarkdownStyleDefaults.HeadingFontSizesPoints[index];
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.Heading(level))
        {
            Name = $"Heading {level}",
            BasedOnId = MarkdownStyleIds.Normal,
            NextStyleId = MarkdownStyleIds.Normal,
            QuickStyle = true
        };
        style.RunProperties.FontWeight = DocFontWeight.Bold;
        style.RunProperties.FontSize = MarkdownStyleDefaults.PointsToDips(sizePoints);
        style.ParagraphProperties.SpacingBefore = MarkdownStyleDefaults.HeadingSpacingBeforeDips;
        style.ParagraphProperties.SpacingAfter = MarkdownStyleDefaults.HeadingSpacingAfterDips;
        style.ParagraphProperties.KeepWithNext = true;
        if (level <= 2)
        {
            style.ParagraphProperties.Borders.Bottom = CreateParagraphBorder();
        }
        return style;
    }

    private static ParagraphStyleDefinition CreateBlockQuoteStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.BlockQuote)
        {
            Name = "Block Quote",
            BasedOnId = MarkdownStyleIds.Normal,
            NextStyleId = MarkdownStyleIds.Normal
        };
        style.RunProperties.Color = MarkdownMutedTextColor;
        style.ParagraphProperties.IndentLeft = MarkdownStyleDefaults.BlockQuoteIndentDips;
        style.ParagraphProperties.SpacingBefore = MarkdownStyleDefaults.BlockQuoteSpacingBeforeDips;
        style.ParagraphProperties.SpacingAfter = MarkdownStyleDefaults.BlockQuoteSpacingAfterDips;
        style.ParagraphProperties.Borders.Left = CreateParagraphBorder();
        return style;
    }

    private static ParagraphStyleDefinition CreateCodeBlockStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.CodeBlock)
        {
            Name = "Code Block",
            BasedOnId = MarkdownStyleIds.Normal,
            NextStyleId = MarkdownStyleIds.Normal
        };
        ApplyCodeFont(style.RunProperties);
        style.ParagraphProperties.ShadingColor = MarkdownCodeBackground;
        style.ParagraphProperties.IndentLeft = MarkdownStyleDefaults.CodeBlockIndentDips;
        style.ParagraphProperties.IndentRight = MarkdownStyleDefaults.CodeBlockIndentDips;
        style.ParagraphProperties.SpacingBefore = MarkdownStyleDefaults.CodeBlockSpacingBeforeDips;
        style.ParagraphProperties.SpacingAfter = MarkdownStyleDefaults.CodeBlockSpacingAfterDips;
        style.ParagraphProperties.LineSpacing = 240;
        style.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        return style;
    }

    private static ParagraphStyleDefinition CreateListParagraphStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.ListParagraph)
        {
            Name = "List Paragraph",
            BasedOnId = MarkdownStyleIds.Normal,
            NextStyleId = MarkdownStyleIds.ListParagraph
        };
        style.ParagraphProperties.LineSpacing = MarkdownStyleDefaults.LineSpacingTwips;
        style.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        style.ParagraphProperties.SpacingBefore = 0f;
        style.ParagraphProperties.SpacingAfter = 0f;
        return style;
    }

    private static ParagraphStyleDefinition CreateTableCellStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.TableCell)
        {
            Name = "Table Cell",
            BasedOnId = MarkdownStyleIds.Normal,
            NextStyleId = MarkdownStyleIds.TableCell
        };
        style.ParagraphProperties.LineSpacing = MarkdownStyleDefaults.LineSpacingTwips;
        style.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        style.ParagraphProperties.SpacingBefore = 0f;
        style.ParagraphProperties.SpacingAfter = 0f;
        return style;
    }

    private static ParagraphStyleDefinition CreateTableHeaderStyle()
    {
        var style = new ParagraphStyleDefinition(MarkdownStyleIds.TableHeader)
        {
            Name = "Table Header",
            BasedOnId = MarkdownStyleIds.TableCell,
            NextStyleId = MarkdownStyleIds.TableCell
        };
        style.RunProperties.FontWeight = DocFontWeight.Bold;
        style.ParagraphProperties.ShadingColor = MarkdownCodeBackground;
        return style;
    }

    private static CharacterStyleDefinition CreateCodeInlineStyle()
    {
        var style = new CharacterStyleDefinition(MarkdownStyleIds.CodeInline)
        {
            Name = "Code Inline"
        };
        ApplyCodeFont(style.RunProperties);
        style.RunProperties.HighlightColor = MarkdownCodeBackground;
        return style;
    }

    private static CharacterStyleDefinition CreateHyperlinkStyle()
    {
        var style = new CharacterStyleDefinition(MarkdownStyleIds.Hyperlink)
        {
            Name = "Hyperlink",
            UnhideWhenUsed = true
        };
        style.RunProperties.Underline = true;
        style.RunProperties.Color = MarkdownLinkColor;
        return style;
    }

    private static TableStyleDefinition CreateTableStyle()
    {
        var style = new TableStyleDefinition(MarkdownStyleIds.MarkdownTable)
        {
            Name = "Markdown Table",
            QuickStyle = true
        };
        var border = CreateTableBorder();
        style.TableProperties.Borders.Top = border;
        style.TableProperties.Borders.Bottom = CreateTableBorder();
        style.TableProperties.Borders.Left = CreateTableBorder();
        style.TableProperties.Borders.Right = CreateTableBorder();
        style.TableProperties.Borders.InsideHorizontal = CreateTableBorder();
        style.TableProperties.Borders.InsideVertical = CreateTableBorder();
        var padding = new DocThickness(
            MarkdownStyleDefaults.TableCellPaddingHorizontalDips,
            MarkdownStyleDefaults.TableCellPaddingVerticalDips,
            MarkdownStyleDefaults.TableCellPaddingHorizontalDips,
            MarkdownStyleDefaults.TableCellPaddingVerticalDips);
        style.TableProperties.CellPadding = padding;
        style.TableProperties.WidthUnit = TableWidthUnit.Pct;
        style.TableProperties.Width = 100f;
        style.TableProperties.LayoutMode = TableLayoutMode.Auto;
        style.TableProperties.Alignment = TableAlignment.Left;
        style.TableProperties.CellSpacing = 0f;
        style.CellProperties.Padding = padding;
        style.TableProperties.Look = new TableLook { FirstRow = true, BandedRows = false, BandedColumns = false };
        return style;
    }

    private static BorderLine CreateTableBorder()
    {
        return new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1f,
            Color = MarkdownBorderColor
        };
    }

    private static BorderLine CreateParagraphBorder()
    {
        return new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1f,
            Color = MarkdownBorderColor
        };
    }

    private static void AddParagraphStyle(DocumentStyles styles, ParagraphStyleDefinition style)
    {
        styles.ParagraphStyles[style.Id] = style;
    }

    private static void AddCharacterStyle(DocumentStyles styles, CharacterStyleDefinition style)
    {
        styles.CharacterStyles[style.Id] = style;
    }

    private static void AddTableStyle(DocumentStyles styles, TableStyleDefinition style)
    {
        styles.TableStyles[style.Id] = style;
    }

    private static void ApplyCodeFont(TextStyleProperties properties)
    {
        properties.FontFamily = MarkdownStyleDefaults.CodeFontFamily;
        properties.FontSize = MarkdownStyleDefaults.PointsToDips(MarkdownStyleDefaults.CodeFontSizePoints);
    }

    private static void ApplyDocumentDefaults(Document document)
    {
        var text = document.DefaultTextStyle;
        text.FontFamily = "Segoe UI";
        text.FontSize = MarkdownStyleDefaults.PointsToDips(MarkdownStyleDefaults.NormalFontSizePoints);
        text.FontWeight = DocFontWeight.Normal;
        text.FontStyle = DocFontStyle.Normal;
        text.Color = MarkdownTextColor;

        var paragraph = document.DefaultParagraphStyleProperties;
        paragraph.LineSpacing = MarkdownStyleDefaults.LineSpacingTwips;
        paragraph.LineSpacingRule = DocLineSpacingRule.Auto;
        paragraph.SpacingBefore = 0f;
        paragraph.SpacingAfter = MarkdownStyleDefaults.ParagraphSpacingAfterDips;
        paragraph.ContextualSpacing = true;
    }
}
