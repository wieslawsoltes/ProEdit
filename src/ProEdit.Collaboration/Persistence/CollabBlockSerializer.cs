using ProEdit.Documents;
using ProEdit.OpenXml;

namespace ProEdit.Collaboration.Persistence;

public sealed record CollabBlockPayload(Block Block, Document Resources);

public sealed class CollabBlockSerializer
{
    public byte[] Serialize(Block block, Document context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer,
            document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));

        CopyDefaults(context, document);
        CopyStyles(context, document);
        CopyFonts(context, document);
        CopyThemeColors(context, document);
        CopyListDefinitions(context, document);

        document.Blocks.Add(DocumentClone.CloneBlock(block));
        CollabNodeIdMap.TryAttach(document);

        using var stream = new MemoryStream();
        var exporter = new DocxExporter();
        exporter.Save(document, stream);
        return stream.ToArray();
    }

    public byte[] SerializeForDiff(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer,
            document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));
        document.Blocks.Add(DocumentClone.CloneBlock(block));
        CollabNodeIdMap.TryAttach(document);

        using var stream = new MemoryStream();
        var exporter = new DocxExporter();
        exporter.Save(document, stream);
        return stream.ToArray();
    }

    public CollabBlockPayload Deserialize(ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray());
        var importer = new DocxImporter();
        var document = importer.Load(stream);

        if (CollabNodeIdMap.TryExtract(document, out var map) && map is not null)
        {
            CollabNodeIdMap.TryApply(document, map);
            CollabNodeIdMap.Remove(document);
        }

        var block = document.Blocks.FirstOrDefault() ?? new ParagraphBlock();
        return new CollabBlockPayload(block, document);
    }

    private static void CopyDefaults(Document source, Document target)
    {
        CopyTextStyle(source.DefaultTextStyle, target.DefaultTextStyle);
        CopyParagraphStyleProperties(source.DefaultParagraphStyleProperties, target.DefaultParagraphStyleProperties);
    }

    private static void CopyStyles(Document source, Document target)
    {
        var cloned = DocumentClone.CloneStyles(source.Styles);
        target.Styles.ParagraphStyles.Clear();
        foreach (var pair in cloned.ParagraphStyles)
        {
            target.Styles.ParagraphStyles[pair.Key] = pair.Value;
        }

        target.Styles.CharacterStyles.Clear();
        foreach (var pair in cloned.CharacterStyles)
        {
            target.Styles.CharacterStyles[pair.Key] = pair.Value;
        }

        target.Styles.TableStyles.Clear();
        foreach (var pair in cloned.TableStyles)
        {
            target.Styles.TableStyles[pair.Key] = pair.Value;
        }

        target.Styles.DefaultParagraphStyleId = cloned.DefaultParagraphStyleId;
        target.Styles.DefaultCharacterStyleId = cloned.DefaultCharacterStyleId;
        target.Styles.DefaultTableStyleId = cloned.DefaultTableStyleId;
    }

    private static void CopyFonts(Document source, Document target)
    {
        var cloned = DocumentClone.CloneFonts(source.Fonts);
        target.Fonts.FontTable.Clear();
        foreach (var pair in cloned.FontTable)
        {
            target.Fonts.FontTable[pair.Key] = pair.Value;
        }

        target.Fonts.Theme.Clear();
        foreach (var pair in cloned.Theme.Entries)
        {
            target.Fonts.Theme.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyThemeColors(Document source, Document target)
    {
        var cloned = DocumentClone.CloneThemeColors(source.ThemeColors);
        target.ThemeColors.Clear();
        foreach (var pair in cloned.Overrides)
        {
            target.ThemeColors.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyListDefinitions(Document source, Document target)
    {
        target.ListDefinitions.Clear();
        foreach (var pair in source.ListDefinitions)
        {
            target.ListDefinitions[pair.Key] = pair.Value.Clone();
        }
    }

    private static void CopyTextStyle(TextStyle source, TextStyle target)
    {
        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private static void CopyParagraphStyleProperties(ParagraphStyleProperties source, ParagraphStyleProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }
}
