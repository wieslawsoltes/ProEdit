using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Word.App;

internal static class DocumentTemplates
{
    private const float PointsToDipScale = 96f / 72f;
    private const int DefaultLineSpacingTwips = 276;
    private const int SingleLineSpacingTwips = 240;

    public static Document CreateDefaultDocument()
    {
        var document = new Document();
        ApplyWordDefaults(document);
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());
        return document;
    }

    private static void ApplyWordDefaults(Document document)
    {
        ApplyThemeFonts(document);
        ApplyDefaultTextStyle(document);
        ApplyDefaultParagraphDefaults(document);
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        ApplyStyles(document);
    }

    private static void ApplyThemeFonts(Document document)
    {
        var theme = document.Fonts.Theme;
        theme.Clear();
        theme.Set(DocThemeFont.MajorAscii, "Calibri Light");
        theme.Set(DocThemeFont.MajorHighAnsi, "Calibri Light");
        theme.Set(DocThemeFont.MinorAscii, "Calibri");
        theme.Set(DocThemeFont.MinorHighAnsi, "Calibri");
    }

    private static void ApplyDefaultTextStyle(Document document)
    {
        var text = document.DefaultTextStyle;
        text.FontFamily = "Calibri";
        text.FontSize = PointsToDips(11f);
        text.FontWeight = DocFontWeight.Normal;
        text.FontStyle = DocFontStyle.Normal;
        text.Color = DocColor.Black;
        text.ThemeFontAscii = DocThemeFont.MinorAscii;
        text.ThemeFontHighAnsi = DocThemeFont.MinorHighAnsi;
    }

    private static void ApplyDefaultParagraphDefaults(Document document)
    {
        var paragraph = document.DefaultParagraphStyleProperties;
        paragraph.LineSpacing = DefaultLineSpacingTwips;
        paragraph.LineSpacingRule = DocLineSpacingRule.Auto;
        paragraph.SpacingAfter = PointsToDips(8f);
        paragraph.SpacingBefore = 0f;
        paragraph.ContextualSpacing = true;
    }

    private static void ApplyStyles(Document document)
    {
        var styles = document.Styles;
        styles.ParagraphStyles.Clear();
        styles.CharacterStyles.Clear();
        styles.TableStyles.Clear();
        styles.DefaultParagraphStyleId = "Normal";
        styles.DefaultCharacterStyleId = "DefaultParagraphFont";
        styles.DefaultTableStyleId = "TableNormal";

        void AddParagraphStyle(ParagraphStyleDefinition style)
        {
            styles.ParagraphStyles[style.Id] = style;
        }

        void AddCharacterStyle(CharacterStyleDefinition style)
        {
            styles.CharacterStyles[style.Id] = style;
        }

        void AddTableStyle(TableStyleDefinition style)
        {
            styles.TableStyles[style.Id] = style;
        }

        var defaultParagraphFont = new CharacterStyleDefinition("DefaultParagraphFont")
        {
            Name = "Default Paragraph Font",
            PrimaryStyle = true
        };
        AddCharacterStyle(defaultParagraphFont);

        var hyperlink = new CharacterStyleDefinition("Hyperlink")
        {
            Name = "Hyperlink",
            UnhideWhenUsed = true
        };
        hyperlink.RunProperties.Underline = true;
        hyperlink.RunProperties.ThemeColor = DocThemeColor.Hyperlink;
        AddCharacterStyle(hyperlink);

        var normal = new ParagraphStyleDefinition("Normal")
        {
            Name = "Normal",
            NextStyleId = "Normal",
            PrimaryStyle = true,
            QuickStyle = true
        };
        ApplyMinorFont(normal.RunProperties, 11f);
        normal.ParagraphProperties.LineSpacing = DefaultLineSpacingTwips;
        normal.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        normal.ParagraphProperties.SpacingAfter = PointsToDips(8f);
        normal.ParagraphProperties.SpacingBefore = 0f;
        normal.ParagraphProperties.ContextualSpacing = true;
        AddParagraphStyle(normal);

        var noSpacing = new ParagraphStyleDefinition("NoSpacing")
        {
            Name = "No Spacing",
            BasedOnId = "Normal",
            NextStyleId = "Normal",
            QuickStyle = true
        };
        ApplyMinorFont(noSpacing.RunProperties, 11f);
        noSpacing.ParagraphProperties.LineSpacing = SingleLineSpacingTwips;
        noSpacing.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        noSpacing.ParagraphProperties.SpacingBefore = 0f;
        noSpacing.ParagraphProperties.SpacingAfter = 0f;
        noSpacing.ParagraphProperties.ContextualSpacing = true;
        AddParagraphStyle(noSpacing);

        var heading1 = new ParagraphStyleDefinition("Heading1")
        {
            Name = "Heading 1",
            BasedOnId = "Normal",
            NextStyleId = "Normal",
            QuickStyle = true
        };
        ApplyMajorFont(heading1.RunProperties, 16f);
        heading1.RunProperties.ThemeColor = DocThemeColor.Accent1;
        heading1.ParagraphProperties.SpacingBefore = PointsToDips(12f);
        heading1.ParagraphProperties.SpacingAfter = PointsToDips(4f);
        heading1.ParagraphProperties.KeepWithNext = true;
        AddParagraphStyle(heading1);

        var heading2 = new ParagraphStyleDefinition("Heading2")
        {
            Name = "Heading 2",
            BasedOnId = "Normal",
            NextStyleId = "Normal",
            QuickStyle = true
        };
        ApplyMajorFont(heading2.RunProperties, 13f);
        heading2.RunProperties.ThemeColor = DocThemeColor.Accent1;
        heading2.ParagraphProperties.SpacingBefore = PointsToDips(10f);
        heading2.ParagraphProperties.SpacingAfter = PointsToDips(2f);
        heading2.ParagraphProperties.KeepWithNext = true;
        AddParagraphStyle(heading2);

        var title = new ParagraphStyleDefinition("Title")
        {
            Name = "Title",
            BasedOnId = "Normal",
            NextStyleId = "Normal",
            QuickStyle = true
        };
        ApplyMajorFont(title.RunProperties, 26f);
        title.RunProperties.ThemeColor = DocThemeColor.Accent1;
        title.ParagraphProperties.SpacingBefore = PointsToDips(12f);
        title.ParagraphProperties.SpacingAfter = PointsToDips(8f);
        AddParagraphStyle(title);

        var tableNormal = new TableStyleDefinition("TableNormal")
        {
            Name = "Normal Table",
            QuickStyle = true
        };
        AddTableStyle(tableNormal);

        var tableGrid = new TableStyleDefinition("TableGrid")
        {
            Name = "Table Grid",
            QuickStyle = true
        };
        var gridBorder = new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1f,
            Color = DocColor.Black
        };
        tableGrid.TableProperties.Borders.Top = gridBorder.Clone();
        tableGrid.TableProperties.Borders.Bottom = gridBorder.Clone();
        tableGrid.TableProperties.Borders.Left = gridBorder.Clone();
        tableGrid.TableProperties.Borders.Right = gridBorder.Clone();
        tableGrid.TableProperties.Borders.InsideHorizontal = gridBorder.Clone();
        tableGrid.TableProperties.Borders.InsideVertical = gridBorder.Clone();
        AddTableStyle(tableGrid);

        var lightShading = new TableStyleDefinition("LightShading")
        {
            Name = "Light Shading",
            QuickStyle = true
        };
        lightShading.TableProperties.Borders.Top = gridBorder.Clone();
        lightShading.TableProperties.Borders.Bottom = gridBorder.Clone();
        lightShading.TableProperties.Borders.Left = gridBorder.Clone();
        lightShading.TableProperties.Borders.Right = gridBorder.Clone();
        lightShading.TableProperties.Borders.InsideHorizontal = gridBorder.Clone();
        lightShading.TableProperties.Borders.InsideVertical = gridBorder.Clone();
        var headerCondition = new TableStyleConditionProperties();
        headerCondition.CellProperties.ShadingColor = new DocColor(229, 229, 229);
        lightShading.Conditions[TableStyleCondition.FirstRow] = headerCondition;
        AddTableStyle(lightShading);
    }

    private static void ApplyMinorFont(TextStyleProperties properties, float fontSizePoints)
    {
        properties.FontFamily = "Calibri";
        properties.FontSize = PointsToDips(fontSizePoints);
        properties.ThemeFontAscii = DocThemeFont.MinorAscii;
        properties.ThemeFontHighAnsi = DocThemeFont.MinorHighAnsi;
    }

    private static void ApplyMajorFont(TextStyleProperties properties, float fontSizePoints)
    {
        properties.FontFamily = "Calibri Light";
        properties.FontSize = PointsToDips(fontSizePoints);
        properties.ThemeFontAscii = DocThemeFont.MajorAscii;
        properties.ThemeFontHighAnsi = DocThemeFont.MajorHighAnsi;
    }

    private static float PointsToDips(float points) => points * PointsToDipScale;
}
