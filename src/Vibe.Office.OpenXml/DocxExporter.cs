using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using OpenXmlTableCellBorders = DocumentFormat.OpenXml.Wordprocessing.TableCellBorders;
using OpenXmlParagraphBorders = DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders;
using OpenXmlPageBorders = DocumentFormat.OpenXml.Wordprocessing.PageBorders;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;
using VibeDocument = Vibe.Office.Documents.Document;

namespace Vibe.Office.OpenXml;

public sealed class DocxExporter
{
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string ObfuscatedFontContentType = "application/vnd.openxmlformats-officedocument.obfuscatedFont";

    public void Save(VibeDocument document, string filePath)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        using var wordDocument = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = wordDocument.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
        var body = mainPart.Document.Body!;

        var numberingContext = EnsureNumbering(mainPart, document);
        EnsureStyles(mainPart, document);
        EnsureFontTable(mainPart, document);
        EnsureNotesAndComments(mainPart, document, numberingContext);
        var imageWriter = new ImageWriter(mainPart);
        var chartWriter = new ChartWriter(mainPart);
        var hyperlinkWriter = new HyperlinkWriter(mainPart);
        var sectionParts = new Dictionary<int, SectionPartInfo>();
        var includeEvenHeaders = document.EvenAndOddHeaders
            || document.Sections.Any(section => section.EvenHeader.Blocks.Count > 0 || section.EvenFooter.Blocks.Count > 0);
        EnsureDocumentSettings(mainPart, document, includeEvenHeaders);
        ApplyDocumentBackground(mainPart.Document, document);

        SectionPartInfo EnsureSectionParts(int sectionIndex)
        {
            if (sectionParts.TryGetValue(sectionIndex, out var cached))
            {
                return cached;
            }

            var section = document.GetSection(sectionIndex);
            var useFirstHeaderFooter = section.Properties.DifferentFirstPageHeaderFooter == true
                || section.FirstHeader.Blocks.Count > 0
                || section.FirstFooter.Blocks.Count > 0;

            string? headerId = null;
            if (section.Header.Blocks.Count > 0)
            {
                var headerPart = mainPart.AddNewPart<HeaderPart>();
                var headerWriter = new ImageWriter(headerPart);
                var headerChartWriter = new ChartWriter(headerPart);
                var headerLinkWriter = new HyperlinkWriter(headerPart);
                headerPart.Header = CreateHeader(section.Header, numberingContext, headerWriter, headerChartWriter, headerLinkWriter, document.Fonts);
                headerId = mainPart.GetIdOfPart(headerPart);
            }

            string? footerId = null;
            if (section.Footer.Blocks.Count > 0)
            {
                var footerPart = mainPart.AddNewPart<FooterPart>();
                var footerWriter = new ImageWriter(footerPart);
                var footerChartWriter = new ChartWriter(footerPart);
                var footerLinkWriter = new HyperlinkWriter(footerPart);
                footerPart.Footer = CreateFooter(section.Footer, numberingContext, footerWriter, footerChartWriter, footerLinkWriter, document.Fonts);
                footerId = mainPart.GetIdOfPart(footerPart);
            }

            string? firstHeaderId = null;
            string? firstFooterId = null;
            if (useFirstHeaderFooter)
            {
                var firstHeaderPart = mainPart.AddNewPart<HeaderPart>();
                var firstHeaderWriter = new ImageWriter(firstHeaderPart);
                var firstHeaderChartWriter = new ChartWriter(firstHeaderPart);
                var firstHeaderLinkWriter = new HyperlinkWriter(firstHeaderPart);
                firstHeaderPart.Header = CreateHeader(section.FirstHeader, numberingContext, firstHeaderWriter, firstHeaderChartWriter, firstHeaderLinkWriter, document.Fonts);
                firstHeaderId = mainPart.GetIdOfPart(firstHeaderPart);

                var firstFooterPart = mainPart.AddNewPart<FooterPart>();
                var firstFooterWriter = new ImageWriter(firstFooterPart);
                var firstFooterChartWriter = new ChartWriter(firstFooterPart);
                var firstFooterLinkWriter = new HyperlinkWriter(firstFooterPart);
                firstFooterPart.Footer = CreateFooter(section.FirstFooter, numberingContext, firstFooterWriter, firstFooterChartWriter, firstFooterLinkWriter, document.Fonts);
                firstFooterId = mainPart.GetIdOfPart(firstFooterPart);
            }

            string? evenHeaderId = null;
            string? evenFooterId = null;
            if (includeEvenHeaders)
            {
                var evenHeaderPart = mainPart.AddNewPart<HeaderPart>();
                var evenHeaderWriter = new ImageWriter(evenHeaderPart);
                var evenHeaderChartWriter = new ChartWriter(evenHeaderPart);
                var evenHeaderLinkWriter = new HyperlinkWriter(evenHeaderPart);
                evenHeaderPart.Header = CreateHeader(section.EvenHeader, numberingContext, evenHeaderWriter, evenHeaderChartWriter, evenHeaderLinkWriter, document.Fonts);
                evenHeaderId = mainPart.GetIdOfPart(evenHeaderPart);

                var evenFooterPart = mainPart.AddNewPart<FooterPart>();
                var evenFooterWriter = new ImageWriter(evenFooterPart);
                var evenFooterChartWriter = new ChartWriter(evenFooterPart);
                var evenFooterLinkWriter = new HyperlinkWriter(evenFooterPart);
                evenFooterPart.Footer = CreateFooter(section.EvenFooter, numberingContext, evenFooterWriter, evenFooterChartWriter, evenFooterLinkWriter, document.Fonts);
                evenFooterId = mainPart.GetIdOfPart(evenFooterPart);
            }

            var info = new SectionPartInfo(headerId, footerId, firstHeaderId, firstFooterId, evenHeaderId, evenFooterId);
            sectionParts[sectionIndex] = info;
            return info;
        }

        var currentSectionIndex = 0;
        var containerStack = new Stack<OpenXmlCompositeElement>();
        OpenXmlCompositeElement currentContainer = body;
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    currentContainer.AppendChild(CreateParagraph(paragraph, numberingContext, imageWriter, chartWriter, hyperlinkWriter, document.Fonts));
                    break;
                case TableBlock table:
                    currentContainer.AppendChild(CreateTable(table, numberingContext, imageWriter, chartWriter, hyperlinkWriter, document.Fonts));
                    break;
                case PageBreakBlock:
                    currentContainer.AppendChild(CreatePageBreakParagraph());
                    break;
                case ColumnBreakBlock:
                    currentContainer.AppendChild(CreateColumnBreakParagraph());
                    break;
                case RevisionStartBlock revisionStart:
                {
                    var revisionElement = BuildBlockRevisionElement(revisionStart.Revision);
                    currentContainer.AppendChild(revisionElement);
                    containerStack.Push(currentContainer);
                    currentContainer = revisionElement;
                    break;
                }
                case RevisionEndBlock:
                    if (containerStack.Count > 0)
                    {
                        currentContainer = containerStack.Pop();
                    }
                    break;
                case SectionBreakBlock sectionBreak:
                {
                    var section = document.GetSection(currentSectionIndex);
                    var parts = EnsureSectionParts(currentSectionIndex);
                    currentContainer.AppendChild(CreateSectionBreakParagraph(sectionBreak, section, parts));
                    var nextIndex = sectionBreak.SectionIndex ?? Math.Min(currentSectionIndex + 1, document.SectionCount - 1);
                    currentSectionIndex = Math.Max(0, nextIndex);
                    break;
                }
            }
        }

        if (!document.Blocks.Any())
        {
            body.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
        }

        var finalSection = document.GetSection(currentSectionIndex);
        var shouldWriteFinalSection = document.SectionCount > 1
            || finalSection.Properties.HasValues
            || finalSection.Header.Blocks.Count > 0
            || finalSection.Footer.Blocks.Count > 0
            || finalSection.FirstHeader.Blocks.Count > 0
            || finalSection.FirstFooter.Blocks.Count > 0
            || finalSection.EvenHeader.Blocks.Count > 0
            || finalSection.EvenFooter.Blocks.Count > 0
            || finalSection.Properties.DifferentFirstPageHeaderFooter == true;

        if (shouldWriteFinalSection)
        {
            var parts = EnsureSectionParts(currentSectionIndex);
            body.Append(BuildSectionProperties(finalSection, parts, null));
        }

        mainPart.Document.Save();
    }

    private static NumberingContext EnsureNumbering(MainDocumentPart mainPart, VibeDocument document)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();
        var listMap = new Dictionary<int, int>();
        var kindMap = new Dictionary<ListKind, int>();
        var usedIds = new HashSet<int>();
        var nextId = 1;

        int AllocateId()
        {
            while (usedIds.Contains(nextId))
            {
                nextId++;
            }

            var id = nextId;
            usedIds.Add(id);
            nextId++;
            return id;
        }

        int ReserveId(int preferred)
        {
            if (preferred > 0 && usedIds.Add(preferred))
            {
                nextId = Math.Max(nextId, preferred + 1);
                return preferred;
            }

            return AllocateId();
        }

        foreach (var listDefinition in document.ListDefinitions.Values.OrderBy(definition => definition.Id))
        {
            var numId = ReserveId(listDefinition.Id);
            var abstractId = numId;
            numbering.Append(CreateAbstractNumbering(listDefinition, abstractId));
            numbering.Append(CreateNumberingInstance(numId, abstractId));
            listMap[listDefinition.Id] = numId;
        }

        var bulletId = AllocateId();
        var decimalId = AllocateId();
        numbering.Append(CreateAbstractNumbering(bulletId, NumberFormatValues.Bullet, "•"));
        numbering.Append(CreateAbstractNumbering(decimalId, NumberFormatValues.Decimal, "%1."));
        numbering.Append(CreateNumberingInstance(bulletId, bulletId));
        numbering.Append(CreateNumberingInstance(decimalId, decimalId));
        kindMap[ListKind.Bullet] = bulletId;
        kindMap[ListKind.Numbered] = decimalId;

        numberingPart.Numbering = numbering;
        return new NumberingContext(listMap, kindMap);
    }

    private sealed class NumberingContext
    {
        private readonly Dictionary<int, int> _listMap;
        private readonly Dictionary<ListKind, int> _kindMap;

        public NumberingContext(Dictionary<int, int> listMap, Dictionary<ListKind, int> kindMap)
        {
            _listMap = listMap;
            _kindMap = kindMap;
        }

        public bool TryResolve(ListInfo info, out int numId)
        {
            if (info.ListId.HasValue && _listMap.TryGetValue(info.ListId.Value, out numId))
            {
                return true;
            }

            return _kindMap.TryGetValue(info.Kind, out numId);
        }
    }

    private static void EnsureStyles(MainDocumentPart mainPart, VibeDocument document)
    {
        if (document.Styles.ParagraphStyles.Count == 0
            && document.Styles.CharacterStyles.Count == 0
            && document.Styles.TableStyles.Count == 0
            && string.IsNullOrWhiteSpace(document.Styles.DefaultParagraphStyleId)
            && string.IsNullOrWhiteSpace(document.Styles.DefaultCharacterStyleId)
            && string.IsNullOrWhiteSpace(document.Styles.DefaultTableStyleId))
        {
            return;
        }

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        var docDefaults = BuildDocDefaults(document, document.Fonts);
        if (docDefaults is not null)
        {
            styles.DocDefaults = docDefaults;
        }

        foreach (var paragraphStyle in document.Styles.ParagraphStyles.Values)
        {
            styles.AppendChild(BuildParagraphStyle(paragraphStyle, document.Styles.DefaultParagraphStyleId, document.Fonts));
        }

        foreach (var characterStyle in document.Styles.CharacterStyles.Values)
        {
            styles.AppendChild(BuildCharacterStyle(characterStyle, document.Styles.DefaultCharacterStyleId, document.Fonts));
        }

        foreach (var tableStyle in document.Styles.TableStyles.Values)
        {
            styles.AppendChild(BuildTableStyle(tableStyle, document.Styles.DefaultTableStyleId));
        }

        stylesPart.Styles = styles;
    }

    private static void EnsureFontTable(MainDocumentPart mainPart, VibeDocument document)
    {
        if (document.Fonts.FontTable.Count == 0)
        {
            return;
        }

        var fontTablePart = mainPart.AddNewPart<FontTablePart>();
        var fonts = new Fonts();

        foreach (var entry in document.Fonts.FontTable.Values)
        {
            var font = new Font { Name = entry.Name };

            if (!string.IsNullOrWhiteSpace(entry.AltName))
            {
                font.AppendChild(new AltName { Val = entry.AltName });
            }

            AppendFontMetadataElement(font, "charset", entry.Charset);
            AppendFontMetadataElement(font, "family", entry.Family);
            AppendFontMetadataElement(font, "pitch", entry.Pitch);
            AppendFontMetadataElement(font, "panose1", entry.Panose1);

            AppendEmbeddedFont(fontTablePart, font, entry.Regular, "embedRegular");
            AppendEmbeddedFont(fontTablePart, font, entry.Bold, "embedBold");
            AppendEmbeddedFont(fontTablePart, font, entry.Italic, "embedItalic");
            AppendEmbeddedFont(fontTablePart, font, entry.BoldItalic, "embedBoldItalic");

            fonts.AppendChild(font);
        }

        fontTablePart.Fonts = fonts;
    }

    private static void EnsureNotesAndComments(MainDocumentPart mainPart, VibeDocument document, NumberingContext numberingContext)
    {
        if (document.Footnotes.Count > 0)
        {
            var footnotesPart = mainPart.AddNewPart<FootnotesPart>();
            PopulateFootnotes(footnotesPart, document, numberingContext, document.Fonts);
        }

        if (document.Endnotes.Count > 0)
        {
            var endnotesPart = mainPart.AddNewPart<EndnotesPart>();
            PopulateEndnotes(endnotesPart, document, numberingContext, document.Fonts);
        }

        if (document.Comments.Count > 0)
        {
            var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
            PopulateComments(commentsPart, document, numberingContext, document.Fonts);
        }
    }

    private static void EnsureDocumentSettings(MainDocumentPart mainPart, VibeDocument document, bool includeEvenHeaders)
    {
        if (!document.MirrorMargins && !document.GutterAtTop && !includeEvenHeaders)
        {
            return;
        }

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        var settings = new Settings();

        if (document.MirrorMargins)
        {
            settings.AppendChild(new MirrorMargins());
        }

        if (document.GutterAtTop)
        {
            settings.AppendChild(new GutterAtTop());
        }

        if (includeEvenHeaders)
        {
            settings.AppendChild(new EvenAndOddHeaders());
        }

        settingsPart.Settings = settings;
    }

    private static void ApplyDocumentBackground(DocumentFormat.OpenXml.Wordprocessing.Document documentXml, VibeDocument document)
    {
        var color = document.SectionProperties.PageBackgroundColor;
        if (!color.HasValue)
        {
            return;
        }

        var background = documentXml.GetFirstChild<DocumentBackground>();
        if (background is null)
        {
            background = new DocumentBackground { Color = ColorToHex(color.Value) };
            if (documentXml.Body is not null)
            {
                documentXml.InsertBefore(background, documentXml.Body);
            }
            else
            {
                documentXml.AppendChild(background);
            }

            return;
        }

        background.Color = ColorToHex(color.Value);
    }

    private static AbstractNum CreateAbstractNumbering(int abstractId, NumberFormatValues format, string levelText)
    {
        var abstractNum = new AbstractNum { AbstractNumberId = abstractId };
        for (var levelIndex = 0; levelIndex < 9; levelIndex++)
        {
            var level = new Level { LevelIndex = levelIndex };
            level.Append(new StartNumberingValue { Val = 1 });
            level.Append(new NumberingFormat { Val = format });
            level.Append(new LevelText { Val = levelText });
            abstractNum.Append(level);
        }

        return abstractNum;
    }

    private static AbstractNum CreateAbstractNumbering(ListDefinition definition, int abstractId)
    {
        var abstractNum = new AbstractNum { AbstractNumberId = abstractId };
        if (definition.Levels.Count == 0)
        {
            abstractNum.Append(CreateLevelDefinition(0, ListNumberFormat.Decimal, "%1.", 1, null, null, null));
            return abstractNum;
        }

        foreach (var level in definition.Levels.Values.OrderBy(item => item.Level))
        {
            var levelText = string.IsNullOrWhiteSpace(level.LevelText)
                ? $"%{level.Level + 1}."
                : level.LevelText;
            if (level.Format == ListNumberFormat.Bullet)
            {
                levelText = level.BulletSymbol ?? level.LevelText ?? "•";
            }

            abstractNum.Append(CreateLevelDefinition(
                level.Level,
                level.Format,
                levelText,
                Math.Max(1, level.StartAt),
                level.LeftIndent,
                level.HangingIndent,
                level.TabStop));
        }

        return abstractNum;
    }

    private static Level CreateLevelDefinition(
        int levelIndex,
        ListNumberFormat format,
        string levelText,
        int startAt,
        float? leftIndent,
        float? hangingIndent,
        float? tabStop)
    {
        var level = new Level { LevelIndex = levelIndex };
        level.Append(new StartNumberingValue { Val = startAt });
        level.Append(new NumberingFormat { Val = MapNumberFormat(format) });
        level.Append(new LevelText { Val = levelText });

        var paragraphProperties = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties();
        if (leftIndent.HasValue || hangingIndent.HasValue)
        {
            paragraphProperties.Indentation = new Indentation
            {
                Left = leftIndent.HasValue ? DipToTwips(leftIndent.Value) : null,
                Hanging = hangingIndent.HasValue ? DipToTwips(hangingIndent.Value) : null
            };
        }

        if (tabStop.HasValue)
        {
            paragraphProperties.Tabs = new Tabs(
                new TabStop
                {
                    Position = DipToTwipsValue(tabStop.Value),
                    Val = TabStopValues.Left
                });
        }

        if (paragraphProperties.ChildElements.Count > 0)
        {
            level.Append(paragraphProperties);
        }

        return level;
    }

    private static NumberFormatValues MapNumberFormat(ListNumberFormat format)
    {
        return format switch
        {
            ListNumberFormat.Bullet => NumberFormatValues.Bullet,
            ListNumberFormat.LowerLetter => NumberFormatValues.LowerLetter,
            ListNumberFormat.UpperLetter => NumberFormatValues.UpperLetter,
            ListNumberFormat.LowerRoman => NumberFormatValues.LowerRoman,
            ListNumberFormat.UpperRoman => NumberFormatValues.UpperRoman,
            _ => NumberFormatValues.Decimal
        };
    }

    private static NumberingInstance CreateNumberingInstance(int numId, int abstractId)
    {
        return new NumberingInstance(new AbstractNumId { Val = abstractId })
        {
            NumberID = numId
        };
    }

    private static DocDefaults? BuildDocDefaults(VibeDocument document, DocumentFonts fonts)
    {
        var runProperties = BuildRunPropertiesBaseStyle(document.DefaultTextStyle, fonts);
        var paragraphProperties = BuildParagraphPropertiesBaseStyle(document.DefaultParagraphStyleProperties);

        if (runProperties is null && paragraphProperties is null)
        {
            return null;
        }

        var docDefaults = new DocDefaults();
        if (runProperties is not null)
        {
            docDefaults.AppendChild(new RunPropertiesDefault { RunPropertiesBaseStyle = runProperties });
        }

        if (paragraphProperties is not null)
        {
            docDefaults.AppendChild(new ParagraphPropertiesDefault { ParagraphPropertiesBaseStyle = paragraphProperties });
        }

        return docDefaults;
    }

    private static Style BuildParagraphStyle(ParagraphStyleDefinition style, string? defaultStyleId, DocumentFonts fonts)
    {
        var element = new Style { Type = StyleValues.Paragraph, StyleId = style.Id };

        if (!string.IsNullOrWhiteSpace(style.Name))
        {
            element.StyleName = new StyleName { Val = style.Name };
        }

        if (!string.IsNullOrWhiteSpace(style.BasedOnId))
        {
            element.BasedOn = new BasedOn { Val = style.BasedOnId };
        }

        if (!string.IsNullOrWhiteSpace(defaultStyleId)
            && string.Equals(style.Id, defaultStyleId, StringComparison.OrdinalIgnoreCase))
        {
            element.Default = true;
        }

        var paragraphProperties = BuildStyleParagraphProperties(style.ParagraphProperties);
        if (paragraphProperties is not null)
        {
            element.AppendChild(paragraphProperties);
        }

        var runProperties = BuildStyleRunProperties(style.RunProperties, fonts);
        if (runProperties is not null)
        {
            element.AppendChild(runProperties);
        }

        return element;
    }

    private static Style BuildCharacterStyle(CharacterStyleDefinition style, string? defaultStyleId, DocumentFonts fonts)
    {
        var element = new Style { Type = StyleValues.Character, StyleId = style.Id };

        if (!string.IsNullOrWhiteSpace(style.Name))
        {
            element.StyleName = new StyleName { Val = style.Name };
        }

        if (!string.IsNullOrWhiteSpace(style.BasedOnId))
        {
            element.BasedOn = new BasedOn { Val = style.BasedOnId };
        }

        if (!string.IsNullOrWhiteSpace(defaultStyleId)
            && string.Equals(style.Id, defaultStyleId, StringComparison.OrdinalIgnoreCase))
        {
            element.Default = true;
        }

        var runProperties = BuildStyleRunProperties(style.RunProperties, fonts);
        if (runProperties is not null)
        {
            element.AppendChild(runProperties);
        }

        return element;
    }

    private static Style BuildTableStyle(TableStyleDefinition style, string? defaultStyleId)
    {
        var element = new Style { Type = StyleValues.Table, StyleId = style.Id };

        if (!string.IsNullOrWhiteSpace(style.Name))
        {
            element.StyleName = new StyleName { Val = style.Name };
        }

        if (!string.IsNullOrWhiteSpace(style.BasedOnId))
        {
            element.BasedOn = new BasedOn { Val = style.BasedOnId };
        }

        if (!string.IsNullOrWhiteSpace(defaultStyleId)
            && string.Equals(style.Id, defaultStyleId, StringComparison.OrdinalIgnoreCase))
        {
            element.Default = true;
        }

        var tableProperties = BuildStyleTableProperties(style.TableProperties);
        if (tableProperties is not null)
        {
            element.AppendChild(tableProperties);
        }

        var cellProperties = BuildStyleTableCellProperties(style.CellProperties);
        if (cellProperties is not null)
        {
            element.AppendChild(cellProperties);
        }

        foreach (var condition in style.Conditions)
        {
            var conditionProperties = BuildTableStyleCondition(condition.Key, condition.Value);
            if (conditionProperties is not null)
            {
                element.AppendChild(conditionProperties);
            }
        }

        return element;
    }

    private static StyleTableProperties? BuildStyleTableProperties(Vibe.Office.Documents.TableProperties properties)
    {
        if (!HasTableProperties(properties))
        {
            return null;
        }

        var props = new StyleTableProperties();
        var borders = BuildTableBorders(properties.Borders);
        if (borders is not null)
        {
            props.TableBorders = borders;
        }

        var defaultMargin = BuildTableCellMarginDefault(properties.CellPadding);
        if (defaultMargin is not null)
        {
            props.TableCellMarginDefault = defaultMargin;
        }

        if (properties.ShadingColor.HasValue)
        {
            props.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static StyleTableCellProperties? BuildStyleTableCellProperties(Vibe.Office.Documents.TableCellProperties properties)
    {
        if (!HasTableCellProperties(properties))
        {
            return null;
        }

        var props = new StyleTableCellProperties();
        var cellMargin = BuildTableCellMargin(properties.Padding);
        if (cellMargin is not null)
        {
            props.TableCellMargin = cellMargin;
        }

        if (properties.VerticalAlignment.HasValue && properties.VerticalAlignment != Vibe.Office.Documents.TableCellVerticalAlignment.Top)
        {
            props.TableCellVerticalAlignment = new DocumentFormat.OpenXml.Wordprocessing.TableCellVerticalAlignment
            {
                Val = properties.VerticalAlignment switch
                {
                    Vibe.Office.Documents.TableCellVerticalAlignment.Center => TableVerticalAlignmentValues.Center,
                    Vibe.Office.Documents.TableCellVerticalAlignment.Bottom => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Top
                }
            };
        }

        if (properties.ShadingColor.HasValue)
        {
            props.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static TableStyleProperties? BuildTableStyleCondition(TableStyleCondition condition, TableStyleConditionProperties properties)
    {
        if (!HasTableProperties(properties.TableProperties) && !HasTableCellProperties(properties.CellProperties))
        {
            return null;
        }

        var props = new TableStyleProperties { Type = MapTableStyleCondition(condition) };
        var tableProps = BuildConditionalTableProperties(properties.TableProperties);
        if (tableProps is not null)
        {
            props.TableStyleConditionalFormattingTableProperties = tableProps;
        }

        var cellProps = BuildConditionalTableCellProperties(properties.CellProperties);
        if (cellProps is not null)
        {
            props.TableStyleConditionalFormattingTableCellProperties = cellProps;
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static TableStyleConditionalFormattingTableProperties? BuildConditionalTableProperties(Vibe.Office.Documents.TableProperties properties)
    {
        if (!HasTableProperties(properties))
        {
            return null;
        }

        var props = new TableStyleConditionalFormattingTableProperties();
        var borders = BuildTableBorders(properties.Borders);
        if (borders is not null)
        {
            props.TableBorders = borders;
        }

        var defaultMargin = BuildTableCellMarginDefault(properties.CellPadding);
        if (defaultMargin is not null)
        {
            props.TableCellMarginDefault = defaultMargin;
        }

        if (properties.ShadingColor.HasValue)
        {
            props.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static TableStyleConditionalFormattingTableCellProperties? BuildConditionalTableCellProperties(Vibe.Office.Documents.TableCellProperties properties)
    {
        if (!HasTableCellProperties(properties))
        {
            return null;
        }

        var props = new TableStyleConditionalFormattingTableCellProperties();
        var borders = BuildTableCellBorders(properties.Borders);
        if (borders is not null)
        {
            props.TableCellBorders = borders;
        }

        var cellMargin = BuildTableCellMargin(properties.Padding);
        if (cellMargin is not null)
        {
            props.TableCellMargin = cellMargin;
        }

        if (properties.VerticalAlignment.HasValue && properties.VerticalAlignment != Vibe.Office.Documents.TableCellVerticalAlignment.Top)
        {
            props.TableCellVerticalAlignment = new DocumentFormat.OpenXml.Wordprocessing.TableCellVerticalAlignment
            {
                Val = properties.VerticalAlignment switch
                {
                    Vibe.Office.Documents.TableCellVerticalAlignment.Center => TableVerticalAlignmentValues.Center,
                    Vibe.Office.Documents.TableCellVerticalAlignment.Bottom => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Top
                }
            };
        }

        if (properties.ShadingColor.HasValue)
        {
            props.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static Paragraph CreateParagraph(
        ParagraphBlock paragraphBlock,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var paragraph = new Paragraph();

        var paragraphProperties = BuildParagraphProperties(paragraphBlock);
        if (paragraphProperties is not null)
        {
            paragraph.AppendChild(paragraphProperties);
        }

        if (paragraphBlock.ListInfo is not null && paragraphBlock.ListInfo.Kind != ListKind.None
            && numberingContext.TryResolve(paragraphBlock.ListInfo, out var numId))
        {
            var numbering = new NumberingProperties(
                new NumberingLevelReference { Val = paragraphBlock.ListInfo.Level },
                new NumberingId { Val = numId });

            var props = paragraph.ParagraphProperties ?? new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties();
            props.AppendChild(numbering);
            paragraph.ParagraphProperties = props;
        }

        AppendRuns(paragraph, paragraphBlock, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        AppendFloatingObjects(paragraph, paragraphBlock, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        return paragraph;
    }

    private static Table CreateTable(
        TableBlock tableBlock,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var table = new Table();
        var tableProperties = BuildTableProperties(tableBlock.Properties, tableBlock.StyleId);
        if (tableProperties is not null)
        {
            table.AppendChild(tableProperties);
        }

        var columnWidths = tableBlock.Properties.ColumnWidths;
        if (columnWidths.Count > 0)
        {
            var grid = new TableGrid();
            foreach (var width in columnWidths)
            {
                grid.AppendChild(new GridColumn { Width = DipToTwips(width) });
            }

            table.AppendChild(grid);
        }

        foreach (var row in tableBlock.Rows)
        {
            var tableRow = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
            var rowProperties = BuildTableRowProperties(row);
            if (rowProperties is not null)
            {
                tableRow.AppendChild(rowProperties);
            }

            foreach (var cell in row.Cells)
            {
                var tableCell = new DocumentFormat.OpenXml.Wordprocessing.TableCell();
                ApplyTableCellProperties(tableCell, cell.Properties);
                ApplyTableCellStructure(tableCell, cell);
                foreach (var paragraph in cell.Paragraphs)
                {
                    tableCell.AppendChild(CreateParagraph(paragraph, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                }

                if (cell.Paragraphs.Count == 0)
                {
                    tableCell.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
                }

                OpenXmlElement cellElement = tableCell;
                if (cell.ContentControl is not null)
                {
                    cellElement = BuildSdtCell(cell.ContentControl, tableCell);
                }

                cellElement = WrapWithMetadata(cellElement, cell.Metadata);
                tableRow.AppendChild(cellElement);
            }

            OpenXmlElement rowElement = tableRow;
            if (row.ContentControl is not null)
            {
                rowElement = BuildSdtRow(row.ContentControl, tableRow);
            }

            rowElement = WrapWithMetadata(rowElement, row.Metadata);
            table.AppendChild(rowElement);
        }

        return table;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.TableRowProperties? BuildTableRowProperties(Vibe.Office.Documents.TableRow row)
    {
        var properties = row.Properties;
        if (!properties.HasValues)
        {
            return null;
        }

        var rowProperties = new DocumentFormat.OpenXml.Wordprocessing.TableRowProperties();
        if (properties.CantSplit == true)
        {
            rowProperties.AppendChild(new CantSplit());
        }

        if (properties.RepeatOnEachPage == true)
        {
            rowProperties.AppendChild(new TableHeader());
        }

        if (properties.Height.HasValue || properties.HeightRule.HasValue)
        {
            var height = new TableRowHeight();
            if (properties.Height.HasValue)
            {
                height.Val = DipToTwipsUInt32(properties.Height.Value);
            }

            if (properties.HeightRule.HasValue)
            {
                height.HeightType = MapRowHeightRule(properties.HeightRule.Value);
            }

            rowProperties.AppendChild(height);
        }

        return rowProperties;
    }

    private static Paragraph CreatePageBreakParagraph()
    {
        var run = new Run(new Break { Type = BreakValues.Page });
        return new Paragraph(run);
    }

    private static Paragraph CreateColumnBreakParagraph()
    {
        var run = new Run(new Break { Type = BreakValues.Column });
        return new Paragraph(run);
    }

    private static DocumentFormat.OpenXml.Wordprocessing.SectionProperties BuildSectionProperties(
        DocumentSection section,
        SectionPartInfo parts,
        SectionMarkValues? sectionType,
        Vibe.Office.Documents.SectionProperties? overrides = null)
    {
        var sectionProperties = new DocumentFormat.OpenXml.Wordprocessing.SectionProperties();
        if (sectionType.HasValue)
        {
            sectionProperties.AppendChild(new SectionType { Val = sectionType.Value });
        }

        if (section.Properties.HasValues)
        {
            ApplySectionProperties(sectionProperties, section.Properties);
        }

        if (overrides is not null && overrides.HasValues)
        {
            ApplySectionProperties(sectionProperties, overrides);
        }

        if (!string.IsNullOrWhiteSpace(parts.HeaderId))
        {
            sectionProperties.AppendChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = parts.HeaderId });
        }

        if (!string.IsNullOrWhiteSpace(parts.FooterId))
        {
            sectionProperties.AppendChild(new FooterReference { Type = HeaderFooterValues.Default, Id = parts.FooterId });
        }

        if (!string.IsNullOrWhiteSpace(parts.FirstHeaderId))
        {
            sectionProperties.AppendChild(new HeaderReference { Type = HeaderFooterValues.First, Id = parts.FirstHeaderId });
        }

        if (!string.IsNullOrWhiteSpace(parts.FirstFooterId))
        {
            sectionProperties.AppendChild(new FooterReference { Type = HeaderFooterValues.First, Id = parts.FirstFooterId });
        }

        if (!string.IsNullOrWhiteSpace(parts.EvenHeaderId))
        {
            sectionProperties.AppendChild(new HeaderReference { Type = HeaderFooterValues.Even, Id = parts.EvenHeaderId });
        }

        if (!string.IsNullOrWhiteSpace(parts.EvenFooterId))
        {
            sectionProperties.AppendChild(new FooterReference { Type = HeaderFooterValues.Even, Id = parts.EvenFooterId });
        }

        if (!string.IsNullOrWhiteSpace(parts.FirstHeaderId)
            || !string.IsNullOrWhiteSpace(parts.FirstFooterId)
            || section.Properties.DifferentFirstPageHeaderFooter == true
            || overrides?.DifferentFirstPageHeaderFooter == true)
        {
            sectionProperties.AppendChild(new TitlePage());
        }

        return sectionProperties;
    }

    private static Paragraph CreateSectionBreakParagraph(SectionBreakBlock sectionBreak, DocumentSection section, SectionPartInfo parts)
    {
        var sectionType = sectionBreak.BreakType switch
        {
            SectionBreakType.Continuous => SectionMarkValues.Continuous,
            SectionBreakType.EvenPage => SectionMarkValues.EvenPage,
            SectionBreakType.OddPage => SectionMarkValues.OddPage,
            SectionBreakType.NextColumn => SectionMarkValues.NextColumn,
            _ => SectionMarkValues.NextPage
        };

        var overrides = sectionBreak.SectionIndex is null && sectionBreak.Properties.HasValues
            ? sectionBreak.Properties
            : null;
        var sectionProperties = BuildSectionProperties(section, parts, sectionType, overrides);
        var paragraphProperties = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties
        {
            SectionProperties = sectionProperties
        };

        return new Paragraph { ParagraphProperties = paragraphProperties };
    }

    private static Header CreateHeader(
        HeaderFooter headerFooter,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var header = new Header();
        AppendBlocks(header, headerFooter.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!header.ChildElements.Any())
        {
            header.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
        }

        return header;
    }

    private static Footer CreateFooter(
        HeaderFooter headerFooter,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var footer = new Footer();
        AppendBlocks(footer, headerFooter.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!footer.ChildElements.Any())
        {
            footer.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
        }

        return footer;
    }

    private static void AppendBlocks(
        OpenXmlCompositeElement container,
        IReadOnlyList<Block> blocks,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var index = 0;
        var containerStack = new Stack<OpenXmlCompositeElement>();
        var currentContainer = container;
        while (index < blocks.Count)
        {
            var block = blocks[index];
            switch (block)
            {
                case RevisionStartBlock revisionStart:
                {
                    var revisionElement = BuildBlockRevisionElement(revisionStart.Revision);
                    currentContainer.AppendChild(revisionElement);
                    containerStack.Push(currentContainer);
                    currentContainer = revisionElement;
                    index++;
                    continue;
                }
                case RevisionEndBlock:
                    if (containerStack.Count > 0)
                    {
                        currentContainer = containerStack.Pop();
                    }
                    index++;
                    continue;
                case MetadataStartBlock metadataStart:
                {
                    var metadataElement = BuildMetadataWrapperElement(metadataStart.Metadata);
                    currentContainer.AppendChild(metadataElement);
                    containerStack.Push(currentContainer);
                    currentContainer = metadataElement;
                    index++;
                    continue;
                }
                case MetadataEndBlock:
                    if (containerStack.Count > 0)
                    {
                        currentContainer = containerStack.Pop();
                    }
                    index++;
                    continue;
                case ContentControlStartBlock startBlock:
                {
                    var contentBlocks = new List<Block>();
                    index++;
                    while (index < blocks.Count)
                    {
                        if (blocks[index] is ContentControlEndBlock endBlock
                            && (!startBlock.Properties.Id.HasValue || endBlock.Id == startBlock.Properties.Id))
                        {
                            index++;
                            break;
                        }

                        contentBlocks.Add(blocks[index]);
                        index++;
                    }

                    currentContainer.AppendChild(BuildSdtBlock(startBlock.Properties, contentBlocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                    continue;
                }
                case ContentControlEndBlock:
                    index++;
                    continue;
                case ParagraphBlock paragraph:
                    currentContainer.AppendChild(CreateParagraph(paragraph, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                    index++;
                    break;
                case TableBlock table:
                    currentContainer.AppendChild(CreateTable(table, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                    index++;
                    break;
                case PageBreakBlock:
                    currentContainer.AppendChild(CreatePageBreakParagraph());
                    index++;
                    break;
                case ColumnBreakBlock:
                    currentContainer.AppendChild(CreateColumnBreakParagraph());
                    index++;
                    break;
                default:
                    index++;
                    break;
            }
        }
    }

    private static SdtBlock BuildSdtBlock(
        ContentControlProperties properties,
        IReadOnlyList<Block> blocks,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var sdt = new SdtBlock();
        sdt.AppendChild(BuildContentControlProperties(properties));
        var content = new SdtContentBlock();
        AppendBlocks(content, blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!content.ChildElements.Any())
        {
            content.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
        }

        sdt.AppendChild(content);
        return sdt;
    }

    private static SdtRow BuildSdtRow(ContentControlProperties properties, DocumentFormat.OpenXml.Wordprocessing.TableRow row)
    {
        var sdt = new SdtRow();
        sdt.AppendChild(BuildContentControlProperties(properties));
        var content = new SdtContentRow();
        content.AppendChild(row);
        sdt.AppendChild(content);
        return sdt;
    }

    private static SdtCell BuildSdtCell(ContentControlProperties properties, DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
    {
        var sdt = new SdtCell();
        sdt.AppendChild(BuildContentControlProperties(properties));
        var content = new SdtContentCell();
        content.AppendChild(cell);
        sdt.AppendChild(content);
        return sdt;
    }

    private static SdtRun BuildSdtRun(
        ContentControlProperties properties,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var sdt = new SdtRun();
        sdt.AppendChild(BuildContentControlProperties(properties));
        var content = new SdtContentRun();
        AppendInlineSequence(content, inlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!content.ChildElements.Any())
        {
            content.AppendChild(new Run(new Text(string.Empty)));
        }

        sdt.AppendChild(content);
        return sdt;
    }

    private static SdtProperties BuildContentControlProperties(ContentControlProperties properties)
    {
        var props = new SdtProperties();
        if (properties.Id.HasValue)
        {
            props.AppendChild(new SdtId { Val = properties.Id.Value });
        }

        if (!string.IsNullOrWhiteSpace(properties.Tag))
        {
            props.AppendChild(new Tag { Val = properties.Tag });
        }

        if (!string.IsNullOrWhiteSpace(properties.Alias))
        {
            props.AppendChild(new SdtAlias { Val = properties.Alias });
        }

        if (!string.IsNullOrWhiteSpace(properties.Lock)
            && TryParseLockingValue(properties.Lock, out var lockValue))
        {
            props.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Lock { Val = lockValue });
        }

        if (!string.IsNullOrWhiteSpace(properties.Placeholder))
        {
            var placeholder = new SdtPlaceholder(new DocPartReference { Val = properties.Placeholder });
            props.AppendChild(placeholder);
        }

        if (properties.ShowingPlaceholder.HasValue)
        {
            props.AppendChild(new ShowingPlaceholder { Val = properties.ShowingPlaceholder.Value });
        }

        if (properties.DataBinding is not null
            && (!string.IsNullOrWhiteSpace(properties.DataBinding.XPath)
                || !string.IsNullOrWhiteSpace(properties.DataBinding.StoreItemId)
                || !string.IsNullOrWhiteSpace(properties.DataBinding.PrefixMappings)))
        {
            var binding = new DataBinding();
            if (!string.IsNullOrWhiteSpace(properties.DataBinding.XPath))
            {
                binding.XPath = properties.DataBinding.XPath;
            }

            if (!string.IsNullOrWhiteSpace(properties.DataBinding.StoreItemId))
            {
                binding.StoreItemId = properties.DataBinding.StoreItemId;
            }

            if (!string.IsNullOrWhiteSpace(properties.DataBinding.PrefixMappings))
            {
                binding.PrefixMappings = properties.DataBinding.PrefixMappings;
            }

            props.AppendChild(binding);
        }

        return props;
    }

    private static readonly Dictionary<string, LockingValues> LockingValueMap = BuildLockingValueMap();

    private static Dictionary<string, LockingValues> BuildLockingValueMap()
    {
        var map = new Dictionary<string, LockingValues>(StringComparer.OrdinalIgnoreCase);
        AddLockValue(map, LockingValues.SdtLocked, nameof(LockingValues.SdtLocked));
        AddLockValue(map, LockingValues.ContentLocked, nameof(LockingValues.ContentLocked));
        AddLockValue(map, LockingValues.Unlocked, nameof(LockingValues.Unlocked));
        AddLockValue(map, LockingValues.SdtContentLocked, nameof(LockingValues.SdtContentLocked));
        return map;
    }

    private static void AddLockValue(Dictionary<string, LockingValues> map, LockingValues value, string name)
    {
        var key = ((IEnumValue)value).Value;
        if (!string.IsNullOrWhiteSpace(key))
        {
            map[key] = value;
        }

        map[name] = value;
    }

    private static bool TryParseLockingValue(string value, out LockingValues result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return LockingValueMap.TryGetValue(value.Trim(), out result);
    }

    private static void PopulateFootnotes(FootnotesPart footnotesPart, VibeDocument document, NumberingContext numberingContext, DocumentFonts fonts)
    {
        var footnotes = new Footnotes();
        footnotes.AppendChild(CreateSeparatorFootnote(-1, false));
        footnotes.AppendChild(CreateSeparatorFootnote(0, true));

        var imageWriter = new ImageWriter(footnotesPart);
        var chartWriter = new ChartWriter(footnotesPart);
        var hyperlinkWriter = new HyperlinkWriter(footnotesPart);
        foreach (var definition in document.Footnotes.Values.OrderBy(item => item.Id))
        {
            var footnote = new Footnote { Id = definition.Id };
            AppendBlocks(footnote, definition.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
            if (!footnote.ChildElements.Any())
            {
                footnote.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
            }

            footnotes.AppendChild(footnote);
        }

        footnotesPart.Footnotes = footnotes;
    }

    private static void PopulateEndnotes(EndnotesPart endnotesPart, VibeDocument document, NumberingContext numberingContext, DocumentFonts fonts)
    {
        var endnotes = new Endnotes();
        endnotes.AppendChild(CreateEndnoteSeparator(-1));
        endnotes.AppendChild(CreateEndnoteSeparator(0));

        var imageWriter = new ImageWriter(endnotesPart);
        var chartWriter = new ChartWriter(endnotesPart);
        var hyperlinkWriter = new HyperlinkWriter(endnotesPart);
        foreach (var definition in document.Endnotes.Values.OrderBy(item => item.Id))
        {
            var endnote = new Endnote { Id = definition.Id };
            AppendBlocks(endnote, definition.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
            if (!endnote.ChildElements.Any())
            {
                endnote.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
            }

            endnotes.AppendChild(endnote);
        }

        endnotesPart.Endnotes = endnotes;
    }

    private static void PopulateComments(WordprocessingCommentsPart commentsPart, VibeDocument document, NumberingContext numberingContext, DocumentFonts fonts)
    {
        var comments = new Comments();
        var imageWriter = new ImageWriter(commentsPart);
        var chartWriter = new ChartWriter(commentsPart);
        var hyperlinkWriter = new HyperlinkWriter(commentsPart);

        foreach (var definition in document.Comments.Values.OrderBy(item => item.Id))
        {
            var comment = new Comment
            {
                Id = definition.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Author = definition.Author,
                Initials = definition.Initials,
                Date = definition.Date
            };

            AppendBlocks(comment, definition.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
            if (!comment.ChildElements.Any())
            {
                comment.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
            }

            comments.AppendChild(comment);
        }

        commentsPart.Comments = comments;
    }

    private static Footnote CreateSeparatorFootnote(int id, bool continuation)
    {
        var footnote = new Footnote { Id = id };
        var paragraph = new Paragraph();
        var run = new Run();
        if (continuation)
        {
            run.AppendChild(new ContinuationSeparatorMark());
        }
        else
        {
            run.AppendChild(new SeparatorMark());
        }

        paragraph.AppendChild(run);
        footnote.AppendChild(paragraph);
        return footnote;
    }

    private static Endnote CreateEndnoteSeparator(int id)
    {
        var endnote = new Endnote { Id = id };
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new SeparatorMark()));
        endnote.AppendChild(paragraph);
        return endnote;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties? BuildParagraphProperties(ParagraphBlock paragraphBlock)
    {
        var properties = paragraphBlock.Properties;
        if (properties is null)
        {
            return null;
        }

        var paragraphProperties = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties();
        if (!string.IsNullOrWhiteSpace(paragraphBlock.StyleId))
        {
            paragraphProperties.ParagraphStyleId = new ParagraphStyleId { Val = paragraphBlock.StyleId };
        }

        if (properties.Alignment.HasValue)
        {
            paragraphProperties.Justification = new Justification
            {
                Val = properties.Alignment switch
                {
                    ParagraphAlignment.Center => JustificationValues.Center,
                    ParagraphAlignment.Right => JustificationValues.Right,
                    ParagraphAlignment.Justify => JustificationValues.Both,
                    _ => JustificationValues.Left
                }
            };
        }

        if (properties.SpacingBefore.HasValue || properties.SpacingAfter.HasValue
            || properties.LineSpacing.HasValue || properties.LineSpacingRule.HasValue)
        {
            var spacing = new SpacingBetweenLines
            {
                Before = properties.SpacingBefore.HasValue ? DipToTwips(properties.SpacingBefore.Value) : null,
                After = properties.SpacingAfter.HasValue ? DipToTwips(properties.SpacingAfter.Value) : null
            };
            if (properties.LineSpacing.HasValue)
            {
                spacing.Line = properties.LineSpacing.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (properties.LineSpacingRule.HasValue)
            {
                spacing.LineRule = MapLineSpacingRule(properties.LineSpacingRule.Value);
            }

            paragraphProperties.SpacingBetweenLines = spacing;
        }

        if (properties.IndentLeft.HasValue || properties.IndentRight.HasValue || properties.FirstLineIndent.HasValue)
        {
            paragraphProperties.Indentation = new Indentation
            {
                Left = properties.IndentLeft.HasValue ? DipToTwips(properties.IndentLeft.Value) : null,
                Right = properties.IndentRight.HasValue ? DipToTwips(properties.IndentRight.Value) : null,
                FirstLine = properties.FirstLineIndent.HasValue ? DipToTwips(properties.FirstLineIndent.Value) : null
            };
        }

        if (properties.TabStops.Count > 0)
        {
            var tabs = new Tabs();
            foreach (var tabStop in properties.TabStops)
            {
                var tab = new TabStop
                {
                    Position = DipToTwipsValue(tabStop.Position),
                    Val = MapTabAlignment(tabStop.Alignment)
                };
                var leader = MapTabLeader(tabStop.Leader);
                if (leader.HasValue)
                {
                    tab.Leader = leader;
                }

                tabs.AppendChild(tab);
            }

            paragraphProperties.Tabs = tabs;
        }

        if (properties.KeepWithNext == true)
        {
            paragraphProperties.KeepNext = new KeepNext();
        }

        if (properties.KeepLinesTogether == true)
        {
            paragraphProperties.KeepLines = new KeepLines();
        }

        if (properties.WidowControl == false)
        {
            paragraphProperties.WidowControl = new WidowControl { Val = false };
        }

        if (properties.PageBreakBefore == true)
        {
            paragraphProperties.PageBreakBefore = new PageBreakBefore();
        }

        if (properties.ContextualSpacing.HasValue)
        {
            paragraphProperties.ContextualSpacing = properties.ContextualSpacing.Value
                ? new ContextualSpacing()
                : new ContextualSpacing { Val = false };
        }

        if (properties.Bidi.HasValue)
        {
            paragraphProperties.BiDi = new BiDi { Val = properties.Bidi.Value };
        }

        if (properties.TextDirection.HasValue)
        {
            paragraphProperties.TextDirection = new TextDirection { Val = MapTextDirection(properties.TextDirection.Value) };
        }

        if (properties.ShadingColor.HasValue)
        {
            paragraphProperties.Shading = new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = ColorToHex(properties.ShadingColor.Value)
            };
        }

        if (properties.SuppressLineNumbers == true)
        {
            paragraphProperties.SuppressLineNumbers = new SuppressLineNumbers();
        }

        var frameProperties = BuildFrameProperties(properties.Frame, properties.DropCap);
        if (frameProperties is not null)
        {
            paragraphProperties.FrameProperties = frameProperties;
        }

        var borders = BuildParagraphBorders(properties.Borders);
        if (borders is not null)
        {
            paragraphProperties.ParagraphBorders = borders;
        }

        return paragraphProperties;
    }

    private static void AppendRuns(
        Paragraph paragraph,
        ParagraphBlock block,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        if (block.Inlines.Count == 0)
        {
            AppendTextRuns(paragraph, block.Text ?? string.Empty, null, null, fonts);
            return;
        }

        AppendInlineSequence(paragraph, block.Inlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
    }

    private static void AppendFloatingObjects(
        Paragraph paragraph,
        ParagraphBlock block,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        if (block.FloatingObjects.Count == 0)
        {
            return;
        }

        foreach (var floating in block.FloatingObjects)
        {
            Drawing? drawing = floating.Content switch
            {
                ImageInline image when image.EmbeddedObject is not null
                    && !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => null,
                ImageInline image => CreateImageDrawing(imageWriter, image, floating.Anchor),
                ShapeInline shape => CreateShapeDrawing(shape, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, floating.Anchor),
                ChartInline chart => CreateChartDrawing(chartWriter, chart, floating.Anchor),
                _ => null
            };

            if (drawing is null)
            {
                continue;
            }

            var run = new Run(drawing);
            var link = floating.Content.Hyperlink;
            if (link is not null && !link.IsEmpty)
            {
                var hyperlink = BuildHyperlinkElement(link, hyperlinkWriter);
                hyperlink.AppendChild(run);
                paragraph.AppendChild(hyperlink);
            }
            else
            {
                paragraph.AppendChild(run);
            }
        }
    }

    private static void AppendInlineSequence(
        OpenXmlCompositeElement container,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        AppendInlineSequence(container, inlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, null);
    }

    private static void AppendInlineSequence(
        OpenXmlCompositeElement container,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts,
        RevisionKind? revisionKind)
    {
        var index = 0;
        while (index < inlines.Count)
        {
            var inline = inlines[index];
            switch (inline)
            {
                case BookmarkStartInline bookmarkStart:
                    container.AppendChild(new BookmarkStart
                    {
                        Id = bookmarkStart.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Name = bookmarkStart.Name
                    });
                    index++;
                    continue;
                case BookmarkEndInline bookmarkEnd:
                    container.AppendChild(new BookmarkEnd
                    {
                        Id = bookmarkEnd.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                    index++;
                    continue;
                case CommentRangeStartInline commentStart:
                    container.AppendChild(new CommentRangeStart { Id = commentStart.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    index++;
                    continue;
                case CommentRangeEndInline commentEnd:
                    container.AppendChild(new CommentRangeEnd { Id = commentEnd.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    index++;
                    continue;
                case RevisionRangeStartInline rangeStart:
                    container.AppendChild(BuildMoveRangeStart(rangeStart.Revision));
                    index++;
                    continue;
                case RevisionRangeEndInline rangeEnd:
                    container.AppendChild(BuildMoveRangeEnd(rangeEnd.Kind, rangeEnd.Id));
                    index++;
                    continue;
                case FieldStartInline fieldStart:
                {
                    var fieldInlines = new List<Inline>();
                    index++;
                    while (index < inlines.Count)
                    {
                        var item = inlines[index];
                        if (item is FieldSeparatorInline)
                        {
                            index++;
                            continue;
                        }

                        if (item is FieldEndInline)
                        {
                            index++;
                            break;
                        }

                        fieldInlines.Add(item);
                        index++;
                    }

                    AppendSimpleField(container, fieldStart.Instruction, fieldInlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
                    continue;
                }
                case ContentControlStartInline controlStart:
                {
                    var contentInlines = new List<Inline>();
                    index++;
                    while (index < inlines.Count)
                    {
                        if (inlines[index] is ContentControlEndInline controlEnd
                            && (!controlStart.Properties.Id.HasValue || controlEnd.Id == controlStart.Properties.Id))
                        {
                            index++;
                            break;
                        }

                        contentInlines.Add(inlines[index]);
                        index++;
                    }

                    container.AppendChild(BuildSdtRun(controlStart.Properties, contentInlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                    continue;
                }
                case MetadataStartInline metadataStart:
                {
                    var metadataInlines = new List<Inline>();
                    index++;
                    var depth = 0;
                    while (index < inlines.Count)
                    {
                        var candidate = inlines[index];
                        if (candidate is MetadataStartInline)
                        {
                            depth++;
                        }

                        if (candidate is MetadataEndInline metadataEnd)
                        {
                            if (depth == 0 && ReferenceEquals(metadataStart.Metadata, metadataEnd.Metadata))
                            {
                                index++;
                                break;
                            }

                            depth = Math.Max(0, depth - 1);
                        }

                        metadataInlines.Add(candidate);
                        index++;
                    }

                    var wrapper = BuildMetadataWrapperElement(metadataStart.Metadata);
                    AppendInlineSequence(wrapper, metadataInlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, revisionKind);
                    container.AppendChild(wrapper);
                    continue;
                }
                case RevisionStartInline revisionStart:
                {
                    var revisionInlines = new List<Inline>();
                    index++;
                    var depth = 0;
                    while (index < inlines.Count)
                    {
                        if (inlines[index] is RevisionStartInline nestedStart
                            && RevisionMatches(revisionStart.Revision, nestedStart.Revision))
                        {
                            depth++;
                        }

                        if (inlines[index] is RevisionEndInline revisionEnd
                            && RevisionMatches(revisionStart.Revision, revisionEnd))
                        {
                            if (depth == 0)
                            {
                                index++;
                                break;
                            }

                            depth--;
                        }

                        revisionInlines.Add(inlines[index]);
                        index++;
                    }

                    var revisionElement = BuildRunRevisionElement(revisionStart.Revision);
                    AppendInlineSequence(revisionElement, revisionInlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, revisionStart.Revision.Kind);
                    container.AppendChild(revisionElement);
                    continue;
                }
                case FieldSeparatorInline:
                case FieldEndInline:
                case ContentControlEndInline:
                case MetadataEndInline:
                case RevisionEndInline:
                    index++;
                    continue;
            }

            var link = inline.Hyperlink;
            var group = new List<Inline> { inline };
            index++;
            while (index < inlines.Count)
            {
                var candidate = inlines[index];
                if (candidate is BookmarkStartInline
                    or BookmarkEndInline
                    or CommentRangeStartInline
                    or CommentRangeEndInline
                    or ContentControlStartInline
                    or ContentControlEndInline
                    or MetadataStartInline
                    or MetadataEndInline
                    or RevisionStartInline
                    or RevisionEndInline
                    or RevisionRangeStartInline
                    or RevisionRangeEndInline
                    or FieldStartInline
                    or FieldSeparatorInline
                    or FieldEndInline)
                {
                    break;
                }

                if (!Equals(candidate.Hyperlink, link))
                {
                    break;
                }

                group.Add(candidate);
                index++;
            }

            if (link is not null && !link.IsEmpty)
            {
                var hyperlink = BuildHyperlinkElement(link, hyperlinkWriter);
                AppendInlineRuns(hyperlink, group, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, revisionKind);
                container.AppendChild(hyperlink);
            }
            else
            {
                AppendInlineRuns(container, group, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, revisionKind);
            }
        }
    }

    private static void AppendInlineRuns(
        OpenXmlCompositeElement container,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        AppendInlineRuns(container, inlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts, null);
    }

    private static void AppendInlineRuns(
        OpenXmlCompositeElement container,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts,
        RevisionKind? revisionKind)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case RunInline runInline:
                    AppendTextRuns(container, runInline.GetText(), runInline.Style, runInline.StyleId, fonts, revisionKind);
                    break;
                case ImageInline imageInline:
                {
                    if (imageInline.EmbeddedObject is not null
                        && !imageInline.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var label = string.IsNullOrWhiteSpace(imageInline.EmbeddedObject.ProgId)
                            ? "[Embedded Object]"
                            : $"[Embedded Object: {imageInline.EmbeddedObject.ProgId}]";
                        container.AppendChild(new Run(new Text(label)));
                        break;
                    }

                    var run = new Run(CreateImageDrawing(imageWriter, imageInline));
                    container.AppendChild(run);
                    break;
                }
                case ChartInline chartInline:
                {
                    var run = new Run(CreateChartDrawing(chartWriter, chartInline));
                    container.AppendChild(run);
                    break;
                }
                case ShapeInline shapeInline:
                {
                    var run = new Run(CreateShapeDrawing(shapeInline, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts));
                    container.AppendChild(run);
                    break;
                }
                case EquationInline equationInline:
                {
                    var math = BuildOfficeMath(equationInline);
                    if (math is not null)
                    {
                        container.AppendChild(math);
                    }
                    break;
                }
                case RubyInline rubyInline:
                {
                    container.AppendChild(BuildRubyElement(rubyInline, fonts));
                    break;
                }
                case PageNumberInline pageNumberInline:
                    container.AppendChild(CreatePageNumberField(pageNumberInline.Style, fonts));
                    break;
                case TotalPagesInline totalPagesInline:
                    container.AppendChild(CreateTotalPagesField(totalPagesInline.Style, fonts));
                    break;
                case FootnoteReferenceInline footnoteReference:
                {
                    var run = new Run();
                    var props = BuildRunProperties(footnoteReference.Style, footnoteReference.StyleId, fonts);
                    if (props is not null)
                    {
                        run.RunProperties = props;
                    }

                    run.AppendChild(new FootnoteReference { Id = footnoteReference.Id });
                    container.AppendChild(run);
                    break;
                }
                case EndnoteReferenceInline endnoteReference:
                {
                    var run = new Run();
                    var props = BuildRunProperties(endnoteReference.Style, endnoteReference.StyleId, fonts);
                    if (props is not null)
                    {
                        run.RunProperties = props;
                    }

                    run.AppendChild(new EndnoteReference { Id = endnoteReference.Id });
                    container.AppendChild(run);
                    break;
                }
                case CommentReferenceInline commentReference:
                {
                    var run = new Run();
                    var props = BuildRunProperties(commentReference.Style, commentReference.StyleId, fonts);
                    if (props is not null)
                    {
                        run.RunProperties = props;
                    }

                    run.AppendChild(new CommentReference { Id = commentReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    container.AppendChild(run);
                    break;
                }
            }
        }
    }

    private static Ruby BuildRubyElement(RubyInline rubyInline, DocumentFonts fonts)
    {
        var ruby = new Ruby();
        var rubyScale = rubyInline.RubyScale > 0f ? rubyInline.RubyScale : 0.5f;

        var baseSize = rubyInline.BaseStyle?.FontSize;
        if ((!baseSize.HasValue || baseSize.Value <= 0f)
            && rubyInline.RubyStyle?.FontSize is float rubyStyleSize
            && rubyStyleSize > 0f)
        {
            baseSize = rubyScale > 0f ? rubyStyleSize / rubyScale : rubyStyleSize;
        }

        if (baseSize.HasValue && baseSize.Value > 0f)
        {
            var rubySize = baseSize.Value * rubyScale;
            var rubyProps = new RubyProperties
            {
                PhoneticGuideBaseTextSize = new PhoneticGuideBaseTextSize { Val = DipToHalfPoints(baseSize.Value) },
                PhoneticGuideTextFontSize = new PhoneticGuideTextFontSize { Val = DipToHalfPoints(rubySize) }
            };
            ruby.RubyProperties = rubyProps;
        }

        var rubyContent = new RubyContent();
        AppendTextRuns(rubyContent, rubyInline.RubyText ?? string.Empty, rubyInline.RubyStyle, rubyInline.RubyStyleId, fonts);
        ruby.RubyContent = rubyContent;

        var rubyBase = new RubyBase();
        AppendTextRuns(rubyBase, rubyInline.BaseText ?? string.Empty, rubyInline.BaseStyle, rubyInline.BaseStyleId, fonts);
        ruby.RubyBase = rubyBase;

        return ruby;
    }

    private static void AppendSimpleField(
        OpenXmlCompositeElement container,
        string instruction,
        IReadOnlyList<Inline> inlines,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        var field = new SimpleField { Instruction = instruction ?? string.Empty };
        AppendInlineSequence(field, inlines, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!field.ChildElements.Any())
        {
            field.AppendChild(new Run(new Text(string.Empty)));
        }

        container.AppendChild(field);
    }

    private static Hyperlink BuildHyperlinkElement(HyperlinkInfo link, HyperlinkWriter hyperlinkWriter)
    {
        var hyperlink = new Hyperlink();
        if (!string.IsNullOrWhiteSpace(link.Uri))
        {
            hyperlink.Id = hyperlinkWriter.AddHyperlink(link.Uri);
        }

        if (!string.IsNullOrWhiteSpace(link.Anchor))
        {
            hyperlink.Anchor = link.Anchor;
        }

        if (!string.IsNullOrWhiteSpace(link.Tooltip))
        {
            hyperlink.Tooltip = link.Tooltip;
        }

        return hyperlink;
    }

    private static void AppendTextRuns(OpenXmlCompositeElement container, string text, TextStyleProperties? style, string? styleId, DocumentFonts fonts)
    {
        AppendTextRuns(container, text, style, styleId, fonts, null);
    }

    private static void AppendTextRuns(
        OpenXmlCompositeElement container,
        string text,
        TextStyleProperties? style,
        string? styleId,
        DocumentFonts fonts,
        RevisionKind? revisionKind)
    {
        var run = new Run();
        var props = BuildRunProperties(style, styleId, fonts);
        if (props is not null)
        {
            run.RunProperties = props;
        }

        var useDeletedText = ShouldUseDeletedText(revisionKind);
        var span = text.AsSpan();
        var lineStart = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i < span.Length && span[i] != '\n')
            {
                continue;
            }

            var lineSpan = span.Slice(lineStart, i - lineStart);
            var segmentStart = 0;
            for (var j = 0; j <= lineSpan.Length; j++)
            {
                if (j < lineSpan.Length && lineSpan[j] != '\t')
                {
                    continue;
                }

                var segmentLength = j - segmentStart;
                if (segmentLength > 0)
                {
                    var segmentText = new string(lineSpan.Slice(segmentStart, segmentLength));
                    OpenXmlElement textElement = useDeletedText ? new DeletedText(segmentText) : new Text(segmentText);
                    if (textElement is Text textNode)
                    {
                        textNode.Space = SpaceProcessingModeValues.Preserve;
                    }
                    else if (textElement is DeletedText deletedNode)
                    {
                        deletedNode.Space = SpaceProcessingModeValues.Preserve;
                    }
                    run.AppendChild(textElement);
                }

                if (j < lineSpan.Length)
                {
                    run.AppendChild(new TabChar());
                }

                segmentStart = j + 1;
            }

            if (i < span.Length)
            {
                run.AppendChild(new Break());
            }

            lineStart = i + 1;
        }

        container.AppendChild(run);
    }

    private static bool ShouldUseDeletedText(RevisionKind? revisionKind)
    {
        return revisionKind is RevisionKind.Delete or RevisionKind.MoveFrom;
    }

    private static bool RevisionMatches(RevisionInfo start, RevisionInfo candidate)
    {
        if (start.Kind != candidate.Kind)
        {
            return false;
        }

        if (start.Id.HasValue && candidate.Id.HasValue)
        {
            return start.Id.Value == candidate.Id.Value;
        }

        return true;
    }

    private static bool RevisionMatches(RevisionInfo start, RevisionEndInline end)
    {
        if (start.Kind != end.Kind)
        {
            return false;
        }

        if (start.Id.HasValue && end.Id.HasValue)
        {
            return start.Id.Value == end.Id.Value;
        }

        return true;
    }

    private static RunTrackChangeType BuildRunRevisionElement(RevisionInfo revision)
    {
        RunTrackChangeType element = revision.Kind switch
        {
            RevisionKind.Insert => new InsertedRun(),
            RevisionKind.Delete => new DeletedRun(),
            RevisionKind.MoveFrom => new MoveFromRun(),
            RevisionKind.MoveTo => new MoveToRun(),
            _ => new InsertedRun()
        };

        ApplyRevisionMetadata(element, revision);
        return element;
    }

    private static OpenXmlCompositeElement BuildBlockRevisionElement(RevisionInfo revision)
    {
        var localName = revision.Kind switch
        {
            RevisionKind.Insert => "ins",
            RevisionKind.Delete => "del",
            RevisionKind.MoveFrom => "moveFrom",
            RevisionKind.MoveTo => "moveTo",
            _ => "ins"
        };

        var element = new OpenXmlUnknownElement("w", localName, WordprocessingNamespace);
        ApplyRevisionAttributes(element, revision, revision.Kind is RevisionKind.MoveFrom or RevisionKind.MoveTo);
        return element;
    }

    private static OpenXmlElement BuildMoveRangeStart(RevisionInfo revision)
    {
        MoveBookmarkType element = revision.Kind == RevisionKind.MoveTo
            ? new MoveToRangeStart()
            : new MoveFromRangeStart();

        ApplyRevisionMetadata(element, revision);
        return element;
    }

    private static OpenXmlElement BuildMoveRangeEnd(RevisionKind kind, int? id)
    {
        MarkupRangeType element = kind == RevisionKind.MoveTo
            ? new MoveToRangeEnd()
            : new MoveFromRangeEnd();

        if (id.HasValue)
        {
            element.Id = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return element;
    }

    private static void ApplyRevisionMetadata(TrackChangeType element, RevisionInfo revision)
    {
        if (!string.IsNullOrWhiteSpace(revision.Author))
        {
            element.Author = revision.Author;
        }

        if (revision.Date.HasValue)
        {
            element.Date = revision.Date.Value.UtcDateTime;
        }

        if (revision.Id.HasValue)
        {
            element.Id = revision.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyRevisionAttributes(OpenXmlElement element, RevisionInfo revision, bool includeName)
    {
        if (!string.IsNullOrWhiteSpace(revision.Author))
        {
            element.SetAttribute(new OpenXmlAttribute("w", "author", WordprocessingNamespace, revision.Author));
        }

        if (revision.Date.HasValue)
        {
            var dateValue = System.Xml.XmlConvert.ToString(revision.Date.Value.UtcDateTime, System.Xml.XmlDateTimeSerializationMode.Utc);
            element.SetAttribute(new OpenXmlAttribute("w", "date", WordprocessingNamespace, dateValue));
        }

        if (revision.Id.HasValue)
        {
            var idValue = revision.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            element.SetAttribute(new OpenXmlAttribute("w", "id", WordprocessingNamespace, idValue));
        }

        if (includeName && !string.IsNullOrWhiteSpace(revision.Name))
        {
            element.SetAttribute(new OpenXmlAttribute("w", "name", WordprocessingNamespace, revision.Name));
        }
    }

    private static void ApplyRevisionMetadata(RunTrackChangeType element, RevisionInfo revision)
    {
        if (!string.IsNullOrWhiteSpace(revision.Author))
        {
            element.Author = revision.Author;
        }

        if (revision.Date.HasValue)
        {
            element.Date = revision.Date.Value.UtcDateTime;
        }

        if (revision.Id.HasValue)
        {
            element.Id = revision.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyRevisionMetadata(MoveBookmarkType element, RevisionInfo revision)
    {
        if (!string.IsNullOrWhiteSpace(revision.Author))
        {
            element.Author = revision.Author;
        }

        if (revision.Date.HasValue)
        {
            element.Date = revision.Date.Value.UtcDateTime;
        }

        if (!string.IsNullOrWhiteSpace(revision.Name))
        {
            element.Name = revision.Name;
        }

        if (revision.Id.HasValue)
        {
            element.Id = revision.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static OpenXmlUnknownElement BuildMetadataWrapperElement(MetadataContainer metadata)
    {
        var element = BuildMetadataElement(metadata.Element);
        foreach (var property in metadata.PropertyElements)
        {
            element.AppendChild(BuildMetadataElement(property));
        }

        return element;
    }

    private static OpenXmlElement WrapWithMetadata(OpenXmlElement element, IReadOnlyList<MetadataContainer> metadata)
    {
        if (metadata.Count == 0)
        {
            return element;
        }

        var current = element;
        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            var wrapper = BuildMetadataWrapperElement(metadata[i]);
            wrapper.AppendChild(current);
            current = wrapper;
        }

        return current;
    }

    private static OpenXmlUnknownElement BuildMetadataElement(MetadataElement metadata)
    {
        var element = new OpenXmlUnknownElement(metadata.Prefix, metadata.LocalName, metadata.NamespaceUri);
        ApplyMetadataAttributes(element, metadata.Attributes);

        if (metadata.Children.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(metadata.Text))
            {
                var escaped = System.Security.SecurityElement.Escape(metadata.Text);
                if (!string.IsNullOrWhiteSpace(escaped))
                {
                    element.InnerXml = escaped;
                }
            }

            return element;
        }

        foreach (var child in metadata.Children)
        {
            element.AppendChild(BuildMetadataElement(child));
        }

        return element;
    }

    private static void ApplyMetadataAttributes(OpenXmlElement element, IReadOnlyList<MetadataAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            element.SetAttribute(new OpenXmlAttribute(
                attribute.Prefix,
                attribute.LocalName,
                attribute.NamespaceUri,
                attribute.Value));
        }
    }

    private static M.OfficeMath? BuildOfficeMath(EquationInline equation)
    {
        if (equation.Root is null)
        {
            return null;
        }

        var math = new M.OfficeMath();
        AppendMathElements(math, equation.Root);
        return math.ChildElements.Count == 0 ? null : math;
    }

    private static void AppendMathElements(OpenXmlCompositeElement parent, MathElement element)
    {
        switch (element)
        {
            case MathRow row:
                foreach (var child in row.Elements)
                {
                    AppendMathElements(parent, child);
                }
                break;
            case MathRun run:
                parent.AppendChild(BuildMathRun(run));
                break;
            case MathFraction fraction:
                parent.AppendChild(BuildMathFraction(fraction));
                break;
            case MathAccent accent:
                parent.AppendChild(BuildMathAccent(accent));
                break;
            case MathDelimiter delimiter:
                parent.AppendChild(BuildMathDelimiter(delimiter));
                break;
            case MathNary nary:
                parent.AppendChild(BuildMathNary(nary));
                break;
            case MathMatrix matrix:
                parent.AppendChild(BuildMathMatrix(matrix));
                break;
            case MathScript script:
                AppendMathScript(parent, script);
                break;
            case MathRadical radical:
                parent.AppendChild(BuildMathRadical(radical));
                break;
        }
    }

    private static M.Run BuildMathRun(MathRun run)
    {
        var mathRun = new M.Run();
        var props = BuildMathRunProperties(run.Style);
        if (props is not null)
        {
            mathRun.MathRunProperties = props;
        }

        var text = run.Text ?? string.Empty;
        if (text.Length == 0)
        {
            mathRun.AppendChild(new M.Text(string.Empty));
            return mathRun;
        }

        var mathText = new M.Text(text) { Space = SpaceProcessingModeValues.Preserve };
        mathRun.AppendChild(mathText);
        return mathRun;
    }

    private static M.Fraction BuildMathFraction(MathFraction fraction)
    {
        var mathFraction = new M.Fraction();
        if (!fraction.HasBar)
        {
            mathFraction.FractionProperties = new M.FractionProperties
            {
                FractionType = new M.FractionType { Val = M.FractionTypeValues.NoBar }
            };
        }

        var numerator = new M.Numerator();
        AppendMathElements(numerator, fraction.Numerator);
        var denominator = new M.Denominator();
        AppendMathElements(denominator, fraction.Denominator);
        mathFraction.Numerator = numerator;
        mathFraction.Denominator = denominator;
        return mathFraction;
    }

    private static M.Accent BuildMathAccent(MathAccent accent)
    {
        var node = new M.Accent
        {
            Base = BuildMathBase(accent.Base)
        };

        if (!string.IsNullOrWhiteSpace(accent.AccentChar))
        {
            node.AccentProperties = new M.AccentProperties
            {
                AccentChar = new M.AccentChar { Val = accent.AccentChar }
            };
        }

        return node;
    }

    private static M.Delimiter BuildMathDelimiter(MathDelimiter delimiter)
    {
        var node = new M.Delimiter();
        node.AppendChild(BuildMathBase(delimiter.Body));

        if (!string.IsNullOrWhiteSpace(delimiter.BeginChar)
            || !string.IsNullOrWhiteSpace(delimiter.EndChar)
            || !string.IsNullOrWhiteSpace(delimiter.SeparatorChar))
        {
            node.DelimiterProperties = new M.DelimiterProperties();
            if (!string.IsNullOrWhiteSpace(delimiter.BeginChar))
            {
                node.DelimiterProperties.BeginChar = new M.BeginChar { Val = delimiter.BeginChar };
            }

            if (!string.IsNullOrWhiteSpace(delimiter.EndChar))
            {
                node.DelimiterProperties.EndChar = new M.EndChar { Val = delimiter.EndChar };
            }

            if (!string.IsNullOrWhiteSpace(delimiter.SeparatorChar))
            {
                node.DelimiterProperties.SeparatorChar = new M.SeparatorChar { Val = delimiter.SeparatorChar };
            }
        }

        return node;
    }

    private static M.Nary BuildMathNary(MathNary nary)
    {
        var node = new M.Nary
        {
            Base = BuildMathBase(nary.Base)
        };

        if (nary.Subscript is not null)
        {
            node.SubArgument = BuildMathSubArgument(nary.Subscript);
        }

        if (nary.Superscript is not null)
        {
            node.SuperArgument = BuildMathSuperArgument(nary.Superscript);
        }

        if (!string.IsNullOrWhiteSpace(nary.OperatorChar)
            || nary.HideSub
            || nary.HideSup)
        {
            node.NaryProperties = new M.NaryProperties();
            if (!string.IsNullOrWhiteSpace(nary.OperatorChar))
            {
                node.NaryProperties.AccentChar = new M.AccentChar { Val = nary.OperatorChar };
            }

            if (nary.HideSub)
            {
                node.NaryProperties.HideSubArgument = new M.HideSubArgument();
            }

            if (nary.HideSup)
            {
                node.NaryProperties.HideSuperArgument = new M.HideSuperArgument();
            }
        }

        return node;
    }

    private static M.Matrix BuildMathMatrix(MathMatrix matrix)
    {
        var node = new M.Matrix();
        foreach (var row in matrix.Rows)
        {
            var rowNode = new M.MatrixRow();
            foreach (var cell in row)
            {
                rowNode.AppendChild(BuildMathBase(cell));
            }

            node.AppendChild(rowNode);
        }

        return node;
    }

    private static void AppendMathScript(OpenXmlCompositeElement parent, MathScript script)
    {
        var hasSub = script.Subscript is not null;
        var hasSup = script.Superscript is not null;
        if (hasSub && hasSup)
        {
            var node = new M.SubSuperscript
            {
                Base = BuildMathBase(script.Base),
                SubArgument = BuildMathSubArgument(script.Subscript!),
                SuperArgument = BuildMathSuperArgument(script.Superscript!)
            };
            parent.AppendChild(node);
            return;
        }

        if (hasSub)
        {
            var node = new M.Subscript
            {
                Base = BuildMathBase(script.Base),
                SubArgument = BuildMathSubArgument(script.Subscript!)
            };
            parent.AppendChild(node);
            return;
        }

        if (hasSup)
        {
            var node = new M.Superscript
            {
                Base = BuildMathBase(script.Base),
                SuperArgument = BuildMathSuperArgument(script.Superscript!)
            };
            parent.AppendChild(node);
            return;
        }

        AppendMathElements(parent, script.Base);
    }

    private static M.Radical BuildMathRadical(MathRadical radical)
    {
        var node = new M.Radical
        {
            Base = BuildMathBase(radical.Radicand)
        };

        if (radical.Degree is not null)
        {
            node.Degree = BuildMathDegree(radical.Degree);
        }

        return node;
    }

    private static M.Base BuildMathBase(MathElement element)
    {
        var container = new M.Base();
        AppendMathElements(container, element);
        return container;
    }

    private static M.SubArgument BuildMathSubArgument(MathElement element)
    {
        var container = new M.SubArgument();
        AppendMathElements(container, element);
        return container;
    }

    private static M.SuperArgument BuildMathSuperArgument(MathElement element)
    {
        var container = new M.SuperArgument();
        AppendMathElements(container, element);
        return container;
    }

    private static M.Degree BuildMathDegree(MathElement element)
    {
        var container = new M.Degree();
        AppendMathElements(container, element);
        return container;
    }

    private static M.RunProperties? BuildMathRunProperties(TextStyleProperties? style)
    {
        if (style is null)
        {
            return null;
        }

        var hasStyle = style.FontWeight.HasValue || style.FontStyle.HasValue;
        if (!hasStyle)
        {
            return null;
        }

        var bold = style.FontWeight == DocFontWeight.Bold;
        var italic = style.FontStyle == DocFontStyle.Italic;
        var value = bold && italic
            ? M.StyleValues.BoldItalic
            : bold
                ? M.StyleValues.Bold
                : italic
                    ? M.StyleValues.Italic
                    : M.StyleValues.Plain;

        var props = new M.RunProperties();
        props.AppendChild(new M.Style { Val = value });
        return props;
    }

    private static SimpleField CreatePageNumberField(TextStyle? style, DocumentFonts fonts)
    {
        var field = new SimpleField { Instruction = "PAGE" };
        var run = new Run();
        var props = BuildRunProperties(style, null, fonts);
        if (props is not null)
        {
            run.RunProperties = props;
        }

        run.AppendChild(new Text("1"));
        field.AppendChild(run);
        return field;
    }

    private static SimpleField CreateTotalPagesField(TextStyle? style, DocumentFonts fonts)
    {
        var field = new SimpleField { Instruction = "NUMPAGES" };
        var run = new Run();
        var props = BuildRunProperties(style, null, fonts);
        if (props is not null)
        {
            run.RunProperties = props;
        }

        run.AppendChild(new Text("1"));
        field.AppendChild(run);
        return field;
    }

    private static RunProperties? BuildRunProperties(TextStyleProperties? style, string? styleId, DocumentFonts fonts)
    {
        if (style is null && string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        var props = new RunProperties();
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            props.RunStyle = new RunStyle { Val = styleId };
        }

        if (style is null)
        {
            return props;
        }

        if (style.FontWeight == DocFontWeight.Bold)
        {
            props.Bold = new Bold();
        }

        if (style.FontStyle == DocFontStyle.Italic)
        {
            props.Italic = new Italic();
        }

        if (style.UnderlineStyle.HasValue || style.Underline == true)
        {
            var underlineValue = style.UnderlineStyle.HasValue
                ? MapUnderlineValue(style.UnderlineStyle.Value)
                : UnderlineValues.Single;
            var underline = new Underline { Val = underlineValue };
            if (style.UnderlineColor.HasValue)
            {
                underline.Color = ColorToHex(style.UnderlineColor.Value);
            }

            props.Underline = underline;
        }

        if (style.Strikethrough == true)
        {
            props.Strike = new Strike();
        }

        if (style.FontSize.HasValue && style.FontSize.Value > 0)
        {
            props.FontSize = new FontSize { Val = DipToHalfPoints(style.FontSize.Value) };
        }

        if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0)
        {
            props.FontSizeComplexScript = new FontSizeComplexScript { Val = DipToHalfPoints(style.FontSizeComplexScript.Value) };
        }

        if (style.VerticalPosition.HasValue && style.VerticalPosition.Value != DocVerticalPosition.Normal)
        {
            props.VerticalTextAlignment = new VerticalTextAlignment
            {
                Val = MapVerticalPositionValue(style.VerticalPosition.Value)
            };
        }

        if (style.SmallCaps.HasValue)
        {
            props.SmallCaps = new SmallCaps { Val = style.SmallCaps.Value };
        }

        var runFonts = BuildRunFonts(
            style.FontFamily,
            style.FontFamilyAscii,
            style.FontFamilyHighAnsi,
            style.FontFamilyEastAsia,
            style.FontFamilyComplexScript,
            style.ThemeFontAscii,
            style.ThemeFontHighAnsi,
            style.ThemeFontEastAsia,
            style.ThemeFontComplexScript,
            fonts);
        if (runFonts is not null)
        {
            props.RunFonts = runFonts;
        }

        if (style.Color.HasValue && !style.Color.Value.Equals(Vibe.Office.Primitives.DocColor.Black))
        {
            props.Color = new Color { Val = ColorToHex(style.Color.Value) };
        }

        if (style.HighlightColor.HasValue)
        {
            AppendHighlightOrShading(props, style.HighlightColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(style.Language)
            || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
            || !string.IsNullOrWhiteSpace(style.LanguageBidi))
        {
            props.Languages = new Languages
            {
                Val = string.IsNullOrWhiteSpace(style.Language) ? null : style.Language,
                EastAsia = string.IsNullOrWhiteSpace(style.LanguageEastAsia) ? null : style.LanguageEastAsia,
                Bidi = string.IsNullOrWhiteSpace(style.LanguageBidi) ? null : style.LanguageBidi
            };
        }

        var eastAsianLayout = BuildEastAsianLayout(style.EastAsianLayout);
        if (eastAsianLayout is not null)
        {
            props.EastAsianLayout = eastAsianLayout;
        }

        AppendTextEffects(props, style.Effects);

        return props;
    }

    private static RunProperties? BuildRunProperties(TextStyle? style, string? styleId, DocumentFonts fonts)
    {
        if (style is null && string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        var props = new RunProperties();
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            props.RunStyle = new RunStyle { Val = styleId };
        }

        if (style is null)
        {
            return props;
        }

        if (style.FontWeight == DocFontWeight.Bold)
        {
            props.Bold = new Bold();
        }

        if (style.FontStyle == DocFontStyle.Italic)
        {
            props.Italic = new Italic();
        }

        if (style.UnderlineStyle != DocUnderlineStyle.None || style.Underline)
        {
            var underlineValue = style.UnderlineStyle != DocUnderlineStyle.None
                ? MapUnderlineValue(style.UnderlineStyle)
                : UnderlineValues.Single;
            var underline = new Underline { Val = underlineValue };
            if (style.UnderlineColor.HasValue)
            {
                underline.Color = ColorToHex(style.UnderlineColor.Value);
            }

            props.Underline = underline;
        }

        if (style.Strikethrough)
        {
            props.Strike = new Strike();
        }

        if (style.FontSize > 0)
        {
            props.FontSize = new FontSize { Val = DipToHalfPoints(style.FontSize) };
        }

        if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0)
        {
            props.FontSizeComplexScript = new FontSizeComplexScript { Val = DipToHalfPoints(style.FontSizeComplexScript.Value) };
        }

        if (style.VerticalPosition != DocVerticalPosition.Normal)
        {
            props.VerticalTextAlignment = new VerticalTextAlignment
            {
                Val = MapVerticalPositionValue(style.VerticalPosition)
            };
        }

        if (style.SmallCaps)
        {
            props.SmallCaps = new SmallCaps { Val = true };
        }

        var runFonts = BuildRunFonts(
            style.FontFamily,
            style.FontFamilyAscii,
            style.FontFamilyHighAnsi,
            style.FontFamilyEastAsia,
            style.FontFamilyComplexScript,
            style.ThemeFontAscii,
            style.ThemeFontHighAnsi,
            style.ThemeFontEastAsia,
            style.ThemeFontComplexScript,
            fonts);
        if (runFonts is not null)
        {
            props.RunFonts = runFonts;
        }

        if (!style.Color.Equals(Vibe.Office.Primitives.DocColor.Black))
        {
            props.Color = new Color { Val = ColorToHex(style.Color) };
        }

        if (style.HighlightColor.HasValue)
        {
            AppendHighlightOrShading(props, style.HighlightColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(style.Language)
            || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
            || !string.IsNullOrWhiteSpace(style.LanguageBidi))
        {
            props.Languages = new Languages
            {
                Val = string.IsNullOrWhiteSpace(style.Language) ? null : style.Language,
                EastAsia = string.IsNullOrWhiteSpace(style.LanguageEastAsia) ? null : style.LanguageEastAsia,
                Bidi = string.IsNullOrWhiteSpace(style.LanguageBidi) ? null : style.LanguageBidi
            };
        }

        var eastAsianLayout = BuildEastAsianLayout(style.EastAsianLayout);
        if (eastAsianLayout is not null)
        {
            props.EastAsianLayout = eastAsianLayout;
        }

        AppendTextEffects(props, style.Effects);

        return props;
    }

    private static StyleRunProperties? BuildStyleRunProperties(TextStyleProperties style, DocumentFonts fonts)
    {
        if (!HasTextStyleProperties(style))
        {
            return null;
        }

        var props = new StyleRunProperties();

        if (style.FontWeight == DocFontWeight.Bold)
        {
            props.Bold = new Bold();
        }

        if (style.FontStyle == DocFontStyle.Italic)
        {
            props.Italic = new Italic();
        }

        if (style.UnderlineStyle.HasValue || style.Underline == true)
        {
            var underlineValue = style.UnderlineStyle.HasValue
                ? MapUnderlineValue(style.UnderlineStyle.Value)
                : UnderlineValues.Single;
            var underline = new Underline { Val = underlineValue };
            if (style.UnderlineColor.HasValue)
            {
                underline.Color = ColorToHex(style.UnderlineColor.Value);
            }

            props.Underline = underline;
        }

        if (style.Strikethrough == true)
        {
            props.Strike = new Strike();
        }

        if (style.FontSize.HasValue && style.FontSize.Value > 0)
        {
            props.FontSize = new FontSize { Val = DipToHalfPoints(style.FontSize.Value) };
        }

        if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0)
        {
            props.FontSizeComplexScript = new FontSizeComplexScript { Val = DipToHalfPoints(style.FontSizeComplexScript.Value) };
        }

        if (style.VerticalPosition.HasValue && style.VerticalPosition.Value != DocVerticalPosition.Normal)
        {
            props.VerticalTextAlignment = new VerticalTextAlignment
            {
                Val = MapVerticalPositionValue(style.VerticalPosition.Value)
            };
        }

        if (style.SmallCaps.HasValue)
        {
            props.SmallCaps = new SmallCaps { Val = style.SmallCaps.Value };
        }

        var runFonts = BuildRunFonts(
            style.FontFamily,
            style.FontFamilyAscii,
            style.FontFamilyHighAnsi,
            style.FontFamilyEastAsia,
            style.FontFamilyComplexScript,
            style.ThemeFontAscii,
            style.ThemeFontHighAnsi,
            style.ThemeFontEastAsia,
            style.ThemeFontComplexScript,
            fonts);
        if (runFonts is not null)
        {
            props.RunFonts = runFonts;
        }

        if (style.Color.HasValue && !style.Color.Value.Equals(Vibe.Office.Primitives.DocColor.Black))
        {
            props.Color = new Color { Val = ColorToHex(style.Color.Value) };
        }

        if (style.HighlightColor.HasValue)
        {
            AppendHighlightOrShading(props, style.HighlightColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(style.Language)
            || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
            || !string.IsNullOrWhiteSpace(style.LanguageBidi))
        {
            props.Languages = new Languages
            {
                Val = string.IsNullOrWhiteSpace(style.Language) ? null : style.Language,
                EastAsia = string.IsNullOrWhiteSpace(style.LanguageEastAsia) ? null : style.LanguageEastAsia,
                Bidi = string.IsNullOrWhiteSpace(style.LanguageBidi) ? null : style.LanguageBidi
            };
        }

        var eastAsianLayout = BuildEastAsianLayout(style.EastAsianLayout);
        if (eastAsianLayout is not null)
        {
            props.EastAsianLayout = eastAsianLayout;
        }

        AppendTextEffects(props, style.Effects);

        return props;
    }

    private static RunPropertiesBaseStyle? BuildRunPropertiesBaseStyle(TextStyle style, DocumentFonts fonts)
    {
        var props = new RunPropertiesBaseStyle();

        if (style.FontWeight == DocFontWeight.Bold)
        {
            props.Bold = new Bold();
        }

        if (style.FontStyle == DocFontStyle.Italic)
        {
            props.Italic = new Italic();
        }

        if (style.UnderlineStyle != DocUnderlineStyle.None || style.Underline)
        {
            var underlineValue = style.UnderlineStyle != DocUnderlineStyle.None
                ? MapUnderlineValue(style.UnderlineStyle)
                : UnderlineValues.Single;
            var underline = new Underline { Val = underlineValue };
            if (style.UnderlineColor.HasValue)
            {
                underline.Color = ColorToHex(style.UnderlineColor.Value);
            }

            props.Underline = underline;
        }

        if (style.Strikethrough)
        {
            props.Strike = new Strike();
        }

        if (style.FontSize > 0)
        {
            props.FontSize = new FontSize { Val = DipToHalfPoints(style.FontSize) };
        }

        if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0)
        {
            props.FontSizeComplexScript = new FontSizeComplexScript { Val = DipToHalfPoints(style.FontSizeComplexScript.Value) };
        }

        if (style.VerticalPosition != DocVerticalPosition.Normal)
        {
            props.VerticalTextAlignment = new VerticalTextAlignment
            {
                Val = MapVerticalPositionValue(style.VerticalPosition)
            };
        }

        if (style.SmallCaps)
        {
            props.SmallCaps = new SmallCaps { Val = true };
        }

        var runFonts = BuildRunFonts(
            style.FontFamily,
            style.FontFamilyAscii,
            style.FontFamilyHighAnsi,
            style.FontFamilyEastAsia,
            style.FontFamilyComplexScript,
            style.ThemeFontAscii,
            style.ThemeFontHighAnsi,
            style.ThemeFontEastAsia,
            style.ThemeFontComplexScript,
            fonts);
        if (runFonts is not null)
        {
            props.RunFonts = runFonts;
        }

        if (!style.Color.Equals(Vibe.Office.Primitives.DocColor.Black))
        {
            props.Color = new Color { Val = ColorToHex(style.Color) };
        }

        if (style.HighlightColor.HasValue)
        {
            AppendHighlightOrShading(props, style.HighlightColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(style.Language)
            || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
            || !string.IsNullOrWhiteSpace(style.LanguageBidi))
        {
            props.Languages = new Languages
            {
                Val = string.IsNullOrWhiteSpace(style.Language) ? null : style.Language,
                EastAsia = string.IsNullOrWhiteSpace(style.LanguageEastAsia) ? null : style.LanguageEastAsia,
                Bidi = string.IsNullOrWhiteSpace(style.LanguageBidi) ? null : style.LanguageBidi
            };
        }

        var eastAsianLayout = BuildEastAsianLayout(style.EastAsianLayout);
        if (eastAsianLayout is not null)
        {
            props.EastAsianLayout = eastAsianLayout;
        }

        AppendTextEffects(props, style.Effects);

        return props;
    }

    private static StyleParagraphProperties? BuildStyleParagraphProperties(ParagraphStyleProperties properties)
    {
        if (!HasParagraphStyleProperties(properties))
        {
            return null;
        }

        var paragraphProperties = new StyleParagraphProperties();

        if (properties.Alignment.HasValue)
        {
            paragraphProperties.Justification = new Justification
            {
                Val = properties.Alignment switch
                {
                    ParagraphAlignment.Center => JustificationValues.Center,
                    ParagraphAlignment.Right => JustificationValues.Right,
                    ParagraphAlignment.Justify => JustificationValues.Both,
                    _ => JustificationValues.Left
                }
            };
        }

        if (properties.SpacingBefore.HasValue || properties.SpacingAfter.HasValue
            || properties.LineSpacing.HasValue || properties.LineSpacingRule.HasValue)
        {
            var spacing = new SpacingBetweenLines
            {
                Before = properties.SpacingBefore.HasValue ? DipToTwips(properties.SpacingBefore.Value) : null,
                After = properties.SpacingAfter.HasValue ? DipToTwips(properties.SpacingAfter.Value) : null
            };
            if (properties.LineSpacing.HasValue)
            {
                spacing.Line = properties.LineSpacing.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (properties.LineSpacingRule.HasValue)
            {
                spacing.LineRule = MapLineSpacingRule(properties.LineSpacingRule.Value);
            }

            paragraphProperties.SpacingBetweenLines = spacing;
        }

        if (properties.IndentLeft.HasValue || properties.IndentRight.HasValue || properties.FirstLineIndent.HasValue)
        {
            paragraphProperties.Indentation = new Indentation
            {
                Left = properties.IndentLeft.HasValue ? DipToTwips(properties.IndentLeft.Value) : null,
                Right = properties.IndentRight.HasValue ? DipToTwips(properties.IndentRight.Value) : null,
                FirstLine = properties.FirstLineIndent.HasValue ? DipToTwips(properties.FirstLineIndent.Value) : null
            };
        }

        if (properties.TabStops.Count > 0)
        {
            var tabs = new Tabs();
            foreach (var tabStop in properties.TabStops)
            {
                var tab = new TabStop
                {
                    Position = DipToTwipsValue(tabStop.Position),
                    Val = MapTabAlignment(tabStop.Alignment)
                };
                var leader = MapTabLeader(tabStop.Leader);
                if (leader.HasValue)
                {
                    tab.Leader = leader;
                }

                tabs.AppendChild(tab);
            }

            paragraphProperties.Tabs = tabs;
        }

        if (properties.KeepWithNext == true)
        {
            paragraphProperties.KeepNext = new KeepNext();
        }

        if (properties.KeepLinesTogether == true)
        {
            paragraphProperties.KeepLines = new KeepLines();
        }

        if (properties.WidowControl == false)
        {
            paragraphProperties.WidowControl = new WidowControl { Val = false };
        }

        if (properties.PageBreakBefore == true)
        {
            paragraphProperties.PageBreakBefore = new PageBreakBefore();
        }

        if (properties.ContextualSpacing.HasValue)
        {
            paragraphProperties.ContextualSpacing = properties.ContextualSpacing.Value
                ? new ContextualSpacing()
                : new ContextualSpacing { Val = false };
        }

        if (properties.Bidi.HasValue)
        {
            paragraphProperties.BiDi = new BiDi { Val = properties.Bidi.Value };
        }

        if (properties.TextDirection.HasValue)
        {
            paragraphProperties.TextDirection = new TextDirection { Val = MapTextDirection(properties.TextDirection.Value) };
        }

        if (properties.ShadingColor.HasValue)
        {
            paragraphProperties.Shading = new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = ColorToHex(properties.ShadingColor.Value)
            };
        }

        if (properties.SuppressLineNumbers == true)
        {
            paragraphProperties.SuppressLineNumbers = new SuppressLineNumbers();
        }

        var frameProperties = BuildFrameProperties(properties.Frame, properties.DropCap);
        if (frameProperties is not null)
        {
            paragraphProperties.FrameProperties = frameProperties;
        }

        var borders = BuildParagraphBorders(properties.Borders);
        if (borders is not null)
        {
            paragraphProperties.ParagraphBorders = borders;
        }

        return paragraphProperties;
    }

    private static ParagraphPropertiesBaseStyle? BuildParagraphPropertiesBaseStyle(ParagraphStyleProperties properties)
    {
        if (!properties.HasValues)
        {
            return null;
        }

        var paragraphProperties = new ParagraphPropertiesBaseStyle();

        if (properties.Alignment.HasValue)
        {
            paragraphProperties.Justification = new Justification
            {
                Val = properties.Alignment switch
                {
                    ParagraphAlignment.Center => JustificationValues.Center,
                    ParagraphAlignment.Right => JustificationValues.Right,
                    ParagraphAlignment.Justify => JustificationValues.Both,
                    _ => JustificationValues.Left
                }
            };
        }

        if (properties.SpacingBefore.HasValue || properties.SpacingAfter.HasValue
            || properties.LineSpacing.HasValue || properties.LineSpacingRule.HasValue)
        {
            var spacing = new SpacingBetweenLines
            {
                Before = properties.SpacingBefore.HasValue ? DipToTwips(properties.SpacingBefore.Value) : null,
                After = properties.SpacingAfter.HasValue ? DipToTwips(properties.SpacingAfter.Value) : null
            };
            if (properties.LineSpacing.HasValue)
            {
                spacing.Line = properties.LineSpacing.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (properties.LineSpacingRule.HasValue)
            {
                spacing.LineRule = MapLineSpacingRule(properties.LineSpacingRule.Value);
            }

            paragraphProperties.SpacingBetweenLines = spacing;
        }

        if (properties.IndentLeft.HasValue || properties.IndentRight.HasValue || properties.FirstLineIndent.HasValue)
        {
            paragraphProperties.Indentation = new Indentation
            {
                Left = properties.IndentLeft.HasValue ? DipToTwips(properties.IndentLeft.Value) : null,
                Right = properties.IndentRight.HasValue ? DipToTwips(properties.IndentRight.Value) : null,
                FirstLine = properties.FirstLineIndent.HasValue ? DipToTwips(properties.FirstLineIndent.Value) : null
            };
        }

        if (properties.TabStops.Count > 0)
        {
            var tabs = new Tabs();
            foreach (var tabStop in properties.TabStops)
            {
                var tab = new TabStop
                {
                    Position = DipToTwipsValue(tabStop.Position),
                    Val = MapTabAlignment(tabStop.Alignment)
                };
                var leader = MapTabLeader(tabStop.Leader);
                if (leader.HasValue)
                {
                    tab.Leader = leader;
                }

                tabs.AppendChild(tab);
            }

            paragraphProperties.Tabs = tabs;
        }

        if (properties.KeepWithNext == true)
        {
            paragraphProperties.KeepNext = new KeepNext();
        }

        if (properties.KeepLinesTogether == true)
        {
            paragraphProperties.KeepLines = new KeepLines();
        }

        if (properties.WidowControl == false)
        {
            paragraphProperties.WidowControl = new WidowControl { Val = false };
        }

        if (properties.PageBreakBefore == true)
        {
            paragraphProperties.PageBreakBefore = new PageBreakBefore();
        }

        if (properties.ContextualSpacing.HasValue)
        {
            paragraphProperties.ContextualSpacing = properties.ContextualSpacing.Value
                ? new ContextualSpacing()
                : new ContextualSpacing { Val = false };
        }

        if (properties.Bidi.HasValue)
        {
            paragraphProperties.BiDi = new BiDi { Val = properties.Bidi.Value };
        }

        if (properties.ShadingColor.HasValue)
        {
            paragraphProperties.Shading = new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = ColorToHex(properties.ShadingColor.Value)
            };
        }

        if (properties.SuppressLineNumbers == true)
        {
            paragraphProperties.SuppressLineNumbers = new SuppressLineNumbers();
        }

        var frameProperties = BuildFrameProperties(properties.Frame, properties.DropCap);
        if (frameProperties is not null)
        {
            paragraphProperties.FrameProperties = frameProperties;
        }

        var borders = BuildParagraphBorders(properties.Borders);
        if (borders is not null)
        {
            paragraphProperties.ParagraphBorders = borders;
        }

        return paragraphProperties;
    }

    private static bool HasTextStyleProperties(TextStyleProperties style)
    {
        return !string.IsNullOrWhiteSpace(style.FontFamily)
               || !string.IsNullOrWhiteSpace(style.FontFamilyAscii)
               || !string.IsNullOrWhiteSpace(style.FontFamilyHighAnsi)
               || !string.IsNullOrWhiteSpace(style.FontFamilyEastAsia)
               || !string.IsNullOrWhiteSpace(style.FontFamilyComplexScript)
               || style.FontSize.HasValue
               || style.FontSizeComplexScript.HasValue
               || style.FontWeight.HasValue
               || style.FontStyle.HasValue
               || style.Color.HasValue
               || style.VerticalPosition.HasValue
               || style.SmallCaps.HasValue
               || style.Underline.HasValue
               || style.UnderlineStyle.HasValue
               || style.UnderlineColor.HasValue
               || style.Strikethrough.HasValue
               || style.HighlightColor.HasValue
               || style.ThemeFontAscii.HasValue
               || style.ThemeFontHighAnsi.HasValue
               || style.ThemeFontEastAsia.HasValue
               || style.ThemeFontComplexScript.HasValue
               || !string.IsNullOrWhiteSpace(style.Language)
               || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
               || !string.IsNullOrWhiteSpace(style.LanguageBidi)
               || (style.EastAsianLayout?.HasValues ?? false)
               || (style.Effects?.HasValues ?? false);
    }

    private static bool TryMapHighlightColor(Vibe.Office.Primitives.DocColor color, out HighlightColorValues highlight)
    {
        highlight = color switch
        {
            { R: 0, G: 0, B: 0 } => HighlightColorValues.Black,
            { R: 0, G: 0, B: 255 } => HighlightColorValues.Blue,
            { R: 0, G: 255, B: 255 } => HighlightColorValues.Cyan,
            { R: 0, G: 255, B: 0 } => HighlightColorValues.Green,
            { R: 255, G: 0, B: 255 } => HighlightColorValues.Magenta,
            { R: 255, G: 0, B: 0 } => HighlightColorValues.Red,
            { R: 255, G: 255, B: 0 } => HighlightColorValues.Yellow,
            { R: 255, G: 255, B: 255 } => HighlightColorValues.White,
            { R: 0, G: 0, B: 128 } => HighlightColorValues.DarkBlue,
            { R: 0, G: 128, B: 128 } => HighlightColorValues.DarkCyan,
            { R: 0, G: 128, B: 0 } => HighlightColorValues.DarkGreen,
            { R: 128, G: 0, B: 128 } => HighlightColorValues.DarkMagenta,
            { R: 128, G: 0, B: 0 } => HighlightColorValues.DarkRed,
            { R: 128, G: 128, B: 0 } => HighlightColorValues.DarkYellow,
            { R: 211, G: 211, B: 211 } => HighlightColorValues.LightGray,
            { R: 169, G: 169, B: 169 } => HighlightColorValues.DarkGray,
            _ => HighlightColorValues.None
        };

        return highlight != HighlightColorValues.None;
    }

    private static void AppendHighlightOrShading(OpenXmlCompositeElement props, Vibe.Office.Primitives.DocColor color)
    {
        if (TryMapHighlightColor(color, out var highlight))
        {
            props.AppendChild(new Highlight { Val = highlight });
            return;
        }

        props.AppendChild(new Shading
        {
            Val = ShadingPatternValues.Clear,
            Color = "auto",
            Fill = ColorToHex(color)
        });
    }

    private static void AppendTextEffects(OpenXmlCompositeElement props, TextEffects? effects)
    {
        if (effects is null || !effects.HasValues)
        {
            return;
        }

        if (effects.Outline is not null)
        {
            props.AppendChild(new Outline { Val = effects.Outline.Enabled });
        }

        if (effects.Shadow is not null)
        {
            props.AppendChild(new Shadow { Val = effects.Shadow.Enabled });
        }

        if (effects.Emboss.HasValue)
        {
            props.AppendChild(new Emboss { Val = effects.Emboss.Value });
        }

        if (effects.Imprint.HasValue)
        {
            props.AppendChild(new Imprint { Val = effects.Imprint.Value });
        }
    }

    private static bool HasParagraphStyleProperties(ParagraphStyleProperties properties)
    {
        return properties.Alignment.HasValue
               || properties.SpacingBefore.HasValue
               || properties.SpacingAfter.HasValue
               || properties.LineSpacing.HasValue
               || properties.LineSpacingRule.HasValue
               || properties.IndentLeft.HasValue
               || properties.IndentRight.HasValue
               || properties.FirstLineIndent.HasValue
               || properties.TabStops.Count > 0
               || properties.KeepWithNext.HasValue
               || properties.KeepLinesTogether.HasValue
               || properties.WidowControl.HasValue
               || properties.PageBreakBefore.HasValue
               || properties.ContextualSpacing.HasValue
               || properties.Bidi.HasValue
               || properties.TextDirection.HasValue
               || properties.ShadingColor.HasValue
               || properties.SuppressLineNumbers.HasValue
               || (properties.DropCap?.HasValues ?? false)
               || (properties.Frame?.HasValues ?? false)
               || properties.Borders.HasAny;
    }

    private static bool HasThemeFonts(TextStyleProperties style)
    {
        return style.ThemeFontAscii.HasValue
               || style.ThemeFontHighAnsi.HasValue
               || style.ThemeFontEastAsia.HasValue
               || style.ThemeFontComplexScript.HasValue;
    }

    private static bool HasThemeFonts(DocThemeFont? ascii, DocThemeFont? highAnsi, DocThemeFont? eastAsia, DocThemeFont? complexScript)
    {
        return ascii.HasValue
               || highAnsi.HasValue
               || eastAsia.HasValue
               || complexScript.HasValue;
    }

    private static RunFonts? BuildRunFonts(
        string? fontFamily,
        string? fontFamilyAscii,
        string? fontFamilyHighAnsi,
        string? fontFamilyEastAsia,
        string? fontFamilyComplexScript,
        DocThemeFont? ascii,
        DocThemeFont? highAnsi,
        DocThemeFont? eastAsia,
        DocThemeFont? complexScript,
        DocumentFonts? fonts)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)
            && string.IsNullOrWhiteSpace(fontFamilyAscii)
            && string.IsNullOrWhiteSpace(fontFamilyHighAnsi)
            && string.IsNullOrWhiteSpace(fontFamilyEastAsia)
            && string.IsNullOrWhiteSpace(fontFamilyComplexScript)
            && !HasThemeFonts(ascii, highAnsi, eastAsia, complexScript))
        {
            return null;
        }

        var runFonts = new RunFonts();
        var asciiFamily = !string.IsNullOrWhiteSpace(fontFamilyAscii) ? fontFamilyAscii : fontFamily;
        var highAnsiFamily = !string.IsNullOrWhiteSpace(fontFamilyHighAnsi) ? fontFamilyHighAnsi : fontFamily;
        var eastAsiaFamily = fontFamilyEastAsia;
        var complexFamily = fontFamilyComplexScript;

        if (!string.IsNullOrWhiteSpace(asciiFamily))
        {
            runFonts.Ascii = asciiFamily;
        }

        if (!string.IsNullOrWhiteSpace(highAnsiFamily))
        {
            runFonts.HighAnsi = highAnsiFamily;
        }

        if (!string.IsNullOrWhiteSpace(eastAsiaFamily))
        {
            runFonts.EastAsia = eastAsiaFamily;
        }

        if (!string.IsNullOrWhiteSpace(complexFamily))
        {
            runFonts.ComplexScript = complexFamily;
        }

        if (HasThemeFonts(ascii, highAnsi, eastAsia, complexScript))
        {
            var resolved = ResolveThemeFonts(fonts, ascii, highAnsi, eastAsia, complexScript);
            if (string.IsNullOrWhiteSpace(runFonts.Ascii) && !string.IsNullOrWhiteSpace(resolved.Ascii))
            {
                runFonts.Ascii = resolved.Ascii;
            }

            if (string.IsNullOrWhiteSpace(runFonts.HighAnsi) && !string.IsNullOrWhiteSpace(resolved.HighAnsi))
            {
                runFonts.HighAnsi = resolved.HighAnsi;
            }

            if (string.IsNullOrWhiteSpace(runFonts.EastAsia) && !string.IsNullOrWhiteSpace(resolved.EastAsia))
            {
                runFonts.EastAsia = resolved.EastAsia;
            }

            if (string.IsNullOrWhiteSpace(runFonts.ComplexScript) && !string.IsNullOrWhiteSpace(resolved.Complex))
            {
                runFonts.ComplexScript = resolved.Complex;
            }

            ApplyThemeFonts(runFonts, ascii, highAnsi, eastAsia, complexScript);
        }

        return runFonts;
    }

    private static ThemeFontFamilies ResolveThemeFonts(
        DocumentFonts? fonts,
        DocThemeFont? ascii,
        DocThemeFont? highAnsi,
        DocThemeFont? eastAsia,
        DocThemeFont? complexScript)
    {
        if (fonts is null || !fonts.Theme.HasValues)
        {
            return default;
        }

        var asciiFamily = ResolveThemeFont(fonts, ascii);
        var highAnsiFamily = ResolveThemeFont(fonts, highAnsi) ?? asciiFamily;
        var eastAsiaFamily = ResolveThemeFont(fonts, eastAsia);
        var complexFamily = ResolveThemeFont(fonts, complexScript);
        return new ThemeFontFamilies(asciiFamily, highAnsiFamily, eastAsiaFamily, complexFamily);
    }

    private static string? ResolveThemeFont(DocumentFonts fonts, DocThemeFont? theme)
    {
        return theme.HasValue && fonts.Theme.TryGet(theme.Value, out var family) ? family : null;
    }

    private static void ApplyThemeFonts(RunFonts runFonts, DocThemeFont? ascii, DocThemeFont? highAnsi, DocThemeFont? eastAsia, DocThemeFont? complexScript)
    {
        if (ascii.HasValue)
        {
            runFonts.AsciiTheme = MapThemeFontValue(ascii.Value);
        }

        if (highAnsi.HasValue)
        {
            runFonts.HighAnsiTheme = MapThemeFontValue(highAnsi.Value);
        }

        if (eastAsia.HasValue)
        {
            runFonts.EastAsiaTheme = MapThemeFontValue(eastAsia.Value);
        }

        if (complexScript.HasValue)
        {
            runFonts.ComplexScriptTheme = MapThemeFontValue(complexScript.Value);
        }
    }

    private static void AppendEmbeddedFont(FontTablePart fontTablePart, Font font, EmbeddedFontData? embedded, string localName)
    {
        if (embedded is null || embedded.Data.Length == 0)
        {
            return;
        }

        var fontKey = string.IsNullOrWhiteSpace(embedded.FontKey) ? Guid.NewGuid().ToString("D") : embedded.FontKey;
        var contentType = string.IsNullOrWhiteSpace(embedded.ContentType) ? ObfuscatedFontContentType : embedded.ContentType;
        var fontPart = fontTablePart.AddNewPart<FontPart>(contentType);
        var data = string.Equals(contentType, ObfuscatedFontContentType, StringComparison.OrdinalIgnoreCase)
            ? ObfuscateFontData(embedded.Data, fontKey)
            : embedded.Data;

        using (var stream = fontPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            stream.Write(data, 0, data.Length);
        }

        var relId = fontTablePart.GetIdOfPart(fontPart);
        var embed = new OpenXmlUnknownElement($"w:{localName}");
        embed.SetAttribute(new OpenXmlAttribute("r", "id", RelationshipNamespace, relId));
        embed.SetAttribute(new OpenXmlAttribute("w", "fontKey", WordprocessingNamespace, fontKey));
        font.AppendChild(embed);
    }

    private static void AppendFontMetadataElement(Font font, string localName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var element = new OpenXmlUnknownElement($"w:{localName}");
        element.SetAttribute(new OpenXmlAttribute("w", "val", WordprocessingNamespace, value));
        font.AppendChild(element);
    }

    private static byte[] ObfuscateFontData(byte[] data, string fontKey)
    {
        if (data.Length == 0 || !Guid.TryParse(fontKey, out var guid))
        {
            return data;
        }

        var keyBytes = guid.ToByteArray();
        var result = new byte[data.Length];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);

        var count = Math.Min(32, result.Length);
        for (var i = 0; i < count; i++)
        {
            result[i] ^= keyBytes[i % keyBytes.Length];
        }

        return result;
    }

    private readonly record struct ThemeFontFamilies(string? Ascii, string? HighAnsi, string? EastAsia, string? Complex);


    private static UnderlineValues MapUnderlineValue(DocUnderlineStyle style)
    {
        return style switch
        {
            DocUnderlineStyle.Words => UnderlineValues.Words,
            DocUnderlineStyle.Double => UnderlineValues.Double,
            DocUnderlineStyle.Thick => UnderlineValues.Thick,
            DocUnderlineStyle.Dotted => UnderlineValues.Dotted,
            DocUnderlineStyle.DottedHeavy => UnderlineValues.DottedHeavy,
            DocUnderlineStyle.Dash => UnderlineValues.Dash,
            DocUnderlineStyle.DashedHeavy => UnderlineValues.DashedHeavy,
            DocUnderlineStyle.DashLong => UnderlineValues.DashLong,
            DocUnderlineStyle.DashLongHeavy => UnderlineValues.DashLongHeavy,
            DocUnderlineStyle.DotDash => UnderlineValues.DotDash,
            DocUnderlineStyle.DashDotHeavy => UnderlineValues.DashDotHeavy,
            DocUnderlineStyle.DotDotDash => UnderlineValues.DotDotDash,
            DocUnderlineStyle.DashDotDotHeavy => UnderlineValues.DashDotDotHeavy,
            DocUnderlineStyle.Wave => UnderlineValues.Wave,
            DocUnderlineStyle.WavyHeavy => UnderlineValues.WavyHeavy,
            DocUnderlineStyle.WavyDouble => UnderlineValues.WavyDouble,
            DocUnderlineStyle.None => UnderlineValues.None,
            _ => UnderlineValues.Single
        };
    }

    private static VerticalPositionValues MapVerticalPositionValue(DocVerticalPosition position)
    {
        return position switch
        {
            DocVerticalPosition.Superscript => VerticalPositionValues.Superscript,
            DocVerticalPosition.Subscript => VerticalPositionValues.Subscript,
            _ => VerticalPositionValues.Baseline
        };
    }

    private static ThemeFontValues MapThemeFontValue(DocThemeFont font)
    {
        return font switch
        {
            DocThemeFont.MajorEastAsia => ThemeFontValues.MajorEastAsia,
            DocThemeFont.MajorBidi => ThemeFontValues.MajorBidi,
            DocThemeFont.MajorHighAnsi => ThemeFontValues.MajorHighAnsi,
            DocThemeFont.MinorEastAsia => ThemeFontValues.MinorEastAsia,
            DocThemeFont.MinorBidi => ThemeFontValues.MinorBidi,
            DocThemeFont.MinorHighAnsi => ThemeFontValues.MinorHighAnsi,
            DocThemeFont.MinorAscii => ThemeFontValues.MinorAscii,
            _ => ThemeFontValues.MajorAscii
        };
    }

    private static HeightRuleValues MapRowHeightRule(TableRowHeightRule rule)
    {
        return rule switch
        {
            TableRowHeightRule.AtLeast => HeightRuleValues.AtLeast,
            TableRowHeightRule.Exact => HeightRuleValues.Exact,
            _ => HeightRuleValues.Auto
        };
    }

    private static LineSpacingRuleValues MapLineSpacingRule(DocLineSpacingRule rule)
    {
        return rule switch
        {
            DocLineSpacingRule.AtLeast => LineSpacingRuleValues.AtLeast,
            DocLineSpacingRule.Exactly => LineSpacingRuleValues.Exact,
            _ => LineSpacingRuleValues.Auto
        };
    }

    private static TabStopValues MapTabAlignment(TabAlignment alignment)
    {
        return alignment switch
        {
            TabAlignment.Center => TabStopValues.Center,
            TabAlignment.Right => TabStopValues.Right,
            TabAlignment.Decimal => TabStopValues.Decimal,
            _ => TabStopValues.Left
        };
    }

    private static TabStopLeaderCharValues? MapTabLeader(TabLeader leader)
    {
        return leader switch
        {
            TabLeader.Dot => TabStopLeaderCharValues.Dot,
            TabLeader.Hyphen => TabStopLeaderCharValues.Hyphen,
            TabLeader.Underscore => TabStopLeaderCharValues.Underscore,
            _ => null
        };
    }

    private static TextDirectionValues MapTextDirection(DocTextDirection direction)
    {
        return direction switch
        {
            DocTextDirection.TopToBottomRightToLeft => TextDirectionValues.TopToBottomRightToLeft,
            DocTextDirection.BottomToTopLeftToRight => TextDirectionValues.BottomToTopLeftToRight,
            DocTextDirection.LeftToRightTopToBottomRotated => TextDirectionValues.LefttoRightTopToBottomRotated,
            DocTextDirection.TopToBottomRightToLeftRotated => TextDirectionValues.TopToBottomRightToLeftRotated,
            DocTextDirection.TopToBottomLeftToRightRotated => TextDirectionValues.TopToBottomLeftToRightRotated,
            _ => TextDirectionValues.LefToRightTopToBottom
        };
    }

    private static DocGridValues MapDocGridType(DocGridType type)
    {
        return type switch
        {
            DocGridType.Lines => DocGridValues.Lines,
            DocGridType.LinesAndChars => DocGridValues.LinesAndChars,
            DocGridType.SnapToChars => DocGridValues.SnapToChars,
            _ => DocGridValues.Default
        };
    }

    private static LineNumberRestartValues MapLineNumberRestart(LineNumberRestart restart)
    {
        return restart switch
        {
            LineNumberRestart.NewPage => LineNumberRestartValues.NewPage,
            LineNumberRestart.NewSection => LineNumberRestartValues.NewSection,
            _ => LineNumberRestartValues.Continuous
        };
    }

    private static NumberFormatValues MapPageNumberFormat(PageNumberFormat format)
    {
        return format switch
        {
            PageNumberFormat.UpperRoman => NumberFormatValues.UpperRoman,
            PageNumberFormat.LowerRoman => NumberFormatValues.LowerRoman,
            PageNumberFormat.UpperLetter => NumberFormatValues.UpperLetter,
            PageNumberFormat.LowerLetter => NumberFormatValues.LowerLetter,
            _ => NumberFormatValues.Decimal
        };
    }

    private static HorizontalAnchorValues MapFrameHorizontalReference(FloatingHorizontalReference reference)
    {
        return reference switch
        {
            FloatingHorizontalReference.Page => HorizontalAnchorValues.Page,
            FloatingHorizontalReference.Margin => HorizontalAnchorValues.Margin,
            _ => HorizontalAnchorValues.Text
        };
    }

    private static VerticalAnchorValues MapFrameVerticalReference(FloatingVerticalReference reference)
    {
        return reference switch
        {
            FloatingVerticalReference.Page => VerticalAnchorValues.Page,
            FloatingVerticalReference.Margin => VerticalAnchorValues.Margin,
            _ => VerticalAnchorValues.Text
        };
    }

    private static HorizontalAlignmentValues MapFrameHorizontalAlignment(FloatingHorizontalAlignment alignment)
    {
        return alignment switch
        {
            FloatingHorizontalAlignment.Center => HorizontalAlignmentValues.Center,
            FloatingHorizontalAlignment.Right => HorizontalAlignmentValues.Right,
            FloatingHorizontalAlignment.Outside => HorizontalAlignmentValues.Outside,
            FloatingHorizontalAlignment.Inside => HorizontalAlignmentValues.Inside,
            _ => HorizontalAlignmentValues.Left
        };
    }

    private static VerticalAlignmentValues MapFrameVerticalAlignment(FloatingVerticalAlignment alignment)
    {
        return alignment switch
        {
            FloatingVerticalAlignment.Center => VerticalAlignmentValues.Center,
            FloatingVerticalAlignment.Bottom => VerticalAlignmentValues.Bottom,
            FloatingVerticalAlignment.Outside => VerticalAlignmentValues.Outside,
            FloatingVerticalAlignment.Inside => VerticalAlignmentValues.Inside,
            _ => VerticalAlignmentValues.Top
        };
    }

    private static TextWrappingValues MapFrameWrapStyle(FloatingWrapStyle wrapStyle)
    {
        return wrapStyle switch
        {
            FloatingWrapStyle.Square => TextWrappingValues.Around,
            FloatingWrapStyle.Tight => TextWrappingValues.Tight,
            FloatingWrapStyle.Through => TextWrappingValues.Through,
            FloatingWrapStyle.TopBottom => TextWrappingValues.NotBeside,
            _ => TextWrappingValues.None
        };
    }

    private static PageBorderDisplayValues MapPageBorderDisplay(PageBorderDisplay display)
    {
        return display switch
        {
            PageBorderDisplay.FirstPage => PageBorderDisplayValues.FirstPage,
            PageBorderDisplay.ExceptFirstPage => PageBorderDisplayValues.NotFirstPage,
            _ => PageBorderDisplayValues.AllPages
        };
    }

    private static PageBorderOffsetValues MapPageBorderOffset(PageBorderOffset offset)
    {
        return offset == PageBorderOffset.Text
            ? PageBorderOffsetValues.Text
            : PageBorderOffsetValues.Page;
    }

    private static PageBorderZOrderValues MapPageBorderZOrder(PageBorderZOrder zOrder)
    {
        return zOrder == PageBorderZOrder.Front
            ? PageBorderZOrderValues.Front
            : PageBorderZOrderValues.Back;
    }

    private static EastAsianLayout? BuildEastAsianLayout(EastAsianLayoutProperties? properties)
    {
        if (properties is null || !properties.HasValues)
        {
            return null;
        }

        var layout = new EastAsianLayout();
        if (properties.Id.HasValue)
        {
            layout.Id = properties.Id.Value;
        }

        if (properties.Combine.HasValue)
        {
            layout.Combine = properties.Combine.Value;
        }

        if (!string.IsNullOrWhiteSpace(properties.CombineBrackets)
            && Enum.TryParse(properties.CombineBrackets, true, out CombineBracketValues combineBrackets))
        {
            layout.CombineBrackets = combineBrackets;
        }

        if (properties.Vertical.HasValue)
        {
            layout.Vertical = properties.Vertical.Value;
        }

        if (properties.VerticalCompress.HasValue)
        {
            layout.VerticalCompress = properties.VerticalCompress.Value;
        }

        return layout;
    }

    private static StringValue DipToTwips(float value)
    {
        var twips = value / (96f / 72f) * 20f;
        return ((int)Math.Round(twips)).ToString();
    }

    private static Int32Value DipToTwipsValue(float value)
    {
        var twips = value / (96f / 72f) * 20f;
        return (int)Math.Round(twips);
    }

    private static UInt32Value DipToTwipsUInt32(float value)
    {
        var twips = value / (96f / 72f) * 20f;
        var rounded = Math.Max(0, Math.Round(twips));
        return (uint)rounded;
    }

    private static Int16Value DipToTwipsInt16(float value)
    {
        var twips = value / (96f / 72f) * 20f;
        var rounded = (int)Math.Round(twips);
        var clamped = (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
        return clamped;
    }

    private static Int32Value DipToTwipsInt32(float value)
    {
        var twips = value / (96f / 72f) * 20f;
        var rounded = (int)Math.Round(twips);
        var clamped = Math.Clamp(rounded, int.MinValue, int.MaxValue);
        return new Int32Value(clamped);
    }

    private static StringValue DipToHalfPoints(float value)
    {
        var points = value / (96f / 72f);
        var halfPoints = points * 2f;
        return ((int)Math.Round(halfPoints)).ToString();
    }

    private static string ColorToHex(Vibe.Office.Primitives.DocColor color)
    {
        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void ApplySectionProperties(DocumentFormat.OpenXml.Wordprocessing.SectionProperties target, Vibe.Office.Documents.SectionProperties properties)
    {
        if (properties.PageWidth.HasValue || properties.PageHeight.HasValue || properties.Orientation.HasValue)
        {
            var pageSize = target.GetFirstChild<PageSize>() ?? target.AppendChild(new PageSize());
            if (properties.PageWidth.HasValue)
            {
                pageSize.Width = DipToTwipsUInt32(properties.PageWidth.Value);
            }

            if (properties.PageHeight.HasValue)
            {
                pageSize.Height = DipToTwipsUInt32(properties.PageHeight.Value);
            }

            if (properties.Orientation.HasValue)
            {
                pageSize.Orient = properties.Orientation == Vibe.Office.Documents.PageOrientation.Landscape
                    ? PageOrientationValues.Landscape
                    : PageOrientationValues.Portrait;
            }
        }

        if (properties.MarginLeft.HasValue
            || properties.MarginRight.HasValue
            || properties.MarginTop.HasValue
            || properties.MarginBottom.HasValue
            || properties.HeaderOffset.HasValue
            || properties.FooterOffset.HasValue
            || properties.Gutter.HasValue)
        {
            var margins = target.GetFirstChild<PageMargin>() ?? target.AppendChild(new PageMargin());
            if (properties.MarginLeft.HasValue)
            {
                margins.Left = DipToTwipsUInt32(properties.MarginLeft.Value);
            }

            if (properties.MarginRight.HasValue)
            {
                margins.Right = DipToTwipsUInt32(properties.MarginRight.Value);
            }

            if (properties.MarginTop.HasValue)
            {
                margins.Top = DipToTwipsInt32(properties.MarginTop.Value);
            }

            if (properties.MarginBottom.HasValue)
            {
                margins.Bottom = DipToTwipsInt32(properties.MarginBottom.Value);
            }

            if (properties.HeaderOffset.HasValue)
            {
                margins.Header = DipToTwipsUInt32(properties.HeaderOffset.Value);
            }

            if (properties.FooterOffset.HasValue)
            {
                margins.Footer = DipToTwipsUInt32(properties.FooterOffset.Value);
            }

            if (properties.Gutter.HasValue)
            {
                margins.Gutter = DipToTwipsUInt32(properties.Gutter.Value);
            }
        }

        if (properties.ColumnCount.HasValue
            || properties.ColumnGap.HasValue
            || properties.ColumnEqualWidth.HasValue
            || properties.ColumnSeparator.HasValue
            || properties.ColumnWidths.Count > 0)
        {
            var columns = target.GetFirstChild<Columns>() ?? target.AppendChild(new Columns());
            if (properties.ColumnCount.HasValue)
            {
                var count = (short)Math.Clamp(properties.ColumnCount.Value, short.MinValue, short.MaxValue);
                columns.ColumnCount = new Int16Value(count);
            }

            if (properties.ColumnGap.HasValue)
            {
                columns.Space = DipToTwips(properties.ColumnGap.Value);
            }

            if (properties.ColumnEqualWidth.HasValue)
            {
                columns.EqualWidth = properties.ColumnEqualWidth.Value;
            }

            if (properties.ColumnSeparator.HasValue)
            {
                columns.Separator = properties.ColumnSeparator.Value;
            }

            if (properties.ColumnWidths.Count > 0)
            {
                columns.RemoveAllChildren<Column>();
                foreach (var width in properties.ColumnWidths)
                {
                    columns.AppendChild(new Column { Width = DipToTwips(width) });
                }
            }
        }

        if (properties.DocGrid?.HasValues == true)
        {
            var docGrid = target.GetFirstChild<DocGrid>() ?? target.AppendChild(new DocGrid());
            if (properties.DocGrid.Type.HasValue)
            {
                docGrid.Type = MapDocGridType(properties.DocGrid.Type.Value);
            }

            if (properties.DocGrid.LinePitch.HasValue)
            {
                docGrid.LinePitch = DipToTwipsInt32(properties.DocGrid.LinePitch.Value);
            }

            if (properties.DocGrid.CharacterSpace.HasValue)
            {
                docGrid.CharacterSpace = DipToTwipsInt32(properties.DocGrid.CharacterSpace.Value);
            }
        }

        if (properties.PageBorders?.HasAny == true)
        {
            var pageBorders = target.GetFirstChild<OpenXmlPageBorders>() ?? target.AppendChild(new OpenXmlPageBorders());
            pageBorders.TopBorder = BuildBorder<TopBorder>(properties.PageBorders.Top);
            pageBorders.BottomBorder = BuildBorder<BottomBorder>(properties.PageBorders.Bottom);
            pageBorders.LeftBorder = BuildBorder<LeftBorder>(properties.PageBorders.Left);
            pageBorders.RightBorder = BuildBorder<RightBorder>(properties.PageBorders.Right);
            pageBorders.Display = MapPageBorderDisplay(properties.PageBorders.Display);
            pageBorders.OffsetFrom = MapPageBorderOffset(properties.PageBorders.OffsetFrom);
            pageBorders.ZOrder = MapPageBorderZOrder(properties.PageBorders.ZOrder);
        }

        if (properties.LineNumbering is not null)
        {
            var lineNumbers = target.GetFirstChild<LineNumberType>() ?? target.AppendChild(new LineNumberType());
            if (properties.LineNumbering.Start.HasValue)
            {
                lineNumbers.Start = (short)properties.LineNumbering.Start.Value;
            }

            if (properties.LineNumbering.CountBy.HasValue)
            {
                lineNumbers.CountBy = (short)properties.LineNumbering.CountBy.Value;
            }

            if (properties.LineNumbering.Distance.HasValue)
            {
                lineNumbers.Distance = DipToTwips(properties.LineNumbering.Distance.Value);
            }

            lineNumbers.Restart = MapLineNumberRestart(properties.LineNumbering.Restart);
        }

        if (properties.PageNumbering is not null)
        {
            var pageNumbers = target.GetFirstChild<PageNumberType>() ?? target.AppendChild(new PageNumberType());
            if (properties.PageNumbering.Start.HasValue)
            {
                pageNumbers.Start = properties.PageNumbering.Start.Value;
            }

            if (properties.PageNumbering.Format.HasValue)
            {
                pageNumbers.Format = MapPageNumberFormat(properties.PageNumbering.Format.Value);
            }
        }
    }

    private static DocumentFormat.OpenXml.Wordprocessing.TableProperties? BuildTableProperties(Vibe.Office.Documents.TableProperties properties, string? styleId)
    {
        var props = new DocumentFormat.OpenXml.Wordprocessing.TableProperties();

        if (!string.IsNullOrWhiteSpace(styleId))
        {
            props.TableStyle = new TableStyle { Val = styleId };
        }

        var borders = BuildTableBorders(properties.Borders);
        if (borders is not null)
        {
            props.TableBorders = borders;
        }

        var defaultMargin = BuildTableCellMarginDefault(properties.CellPadding);
        if (defaultMargin is not null)
        {
            props.TableCellMarginDefault = defaultMargin;
        }

        if (properties.ShadingColor.HasValue)
        {
            props.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        if (properties.Look is not null)
        {
            props.TableLook = new DocumentFormat.OpenXml.Wordprocessing.TableLook
            {
                FirstRow = properties.Look.FirstRow,
                LastRow = properties.Look.LastRow,
                FirstColumn = properties.Look.FirstColumn,
                LastColumn = properties.Look.LastColumn,
                NoHorizontalBand = !properties.Look.BandedRows,
                NoVerticalBand = !properties.Look.BandedColumns
            };
        }

        return props.ChildElements.Count > 0 ? props : null;
    }

    private static void ApplyTableCellProperties(DocumentFormat.OpenXml.Wordprocessing.TableCell cell, Vibe.Office.Documents.TableCellProperties properties)
    {
        var cellProps = new DocumentFormat.OpenXml.Wordprocessing.TableCellProperties();

        var borders = BuildTableCellBorders(properties.Borders);
        if (borders is not null)
        {
            cellProps.TableCellBorders = borders;
        }

        var cellMargin = BuildTableCellMargin(properties.Padding);
        if (cellMargin is not null)
        {
            cellProps.TableCellMargin = cellMargin;
        }

        if (properties.VerticalAlignment.HasValue && properties.VerticalAlignment != Vibe.Office.Documents.TableCellVerticalAlignment.Top)
        {
            cellProps.TableCellVerticalAlignment = new DocumentFormat.OpenXml.Wordprocessing.TableCellVerticalAlignment
            {
                Val = properties.VerticalAlignment switch
                {
                    Vibe.Office.Documents.TableCellVerticalAlignment.Center => TableVerticalAlignmentValues.Center,
                    Vibe.Office.Documents.TableCellVerticalAlignment.Bottom => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Top
                }
            };
        }

        if (properties.TextDirection.HasValue)
        {
            cellProps.TextDirection = new TextDirection { Val = MapTextDirection(properties.TextDirection.Value) };
        }

        if (properties.ShadingColor.HasValue)
        {
            cellProps.Shading = new Shading { Fill = ColorToHex(properties.ShadingColor.Value) };
        }

        if (cellProps.ChildElements.Count > 0)
        {
            cell.TableCellProperties = cellProps;
        }
    }

    private static void ApplyTableCellStructure(DocumentFormat.OpenXml.Wordprocessing.TableCell cell, Vibe.Office.Documents.TableCell source)
    {
        if (source.ColumnSpan <= 1 && source.VerticalMerge == TableCellVerticalMerge.None)
        {
            return;
        }

        var cellProps = cell.TableCellProperties ?? new DocumentFormat.OpenXml.Wordprocessing.TableCellProperties();

        if (source.ColumnSpan > 1)
        {
            cellProps.GridSpan = new GridSpan { Val = source.ColumnSpan };
        }

        if (source.VerticalMerge != TableCellVerticalMerge.None)
        {
            cellProps.VerticalMerge = new VerticalMerge
            {
                Val = source.VerticalMerge == TableCellVerticalMerge.Restart
                    ? MergedCellValues.Restart
                    : MergedCellValues.Continue
            };
        }

        if (cellProps.ChildElements.Count > 0)
        {
            cell.TableCellProperties = cellProps;
        }
    }

    private static TableCellMarginDefault? BuildTableCellMarginDefault(DocThickness? padding)
    {
        if (!HasPaddingValues(padding))
        {
            return null;
        }

        var value = padding!.Value;
        var margin = new TableCellMarginDefault();
        if (!float.IsNaN(value.Left))
        {
            margin.TableCellLeftMargin = new TableCellLeftMargin { Width = DipToTwipsInt16(value.Left), Type = TableWidthValues.Dxa };
        }

        if (!float.IsNaN(value.Right))
        {
            margin.TableCellRightMargin = new TableCellRightMargin { Width = DipToTwipsInt16(value.Right), Type = TableWidthValues.Dxa };
        }

        if (!float.IsNaN(value.Top))
        {
            margin.TopMargin = new TopMargin { Width = DipToTwips(value.Top), Type = TableWidthUnitValues.Dxa };
        }

        if (!float.IsNaN(value.Bottom))
        {
            margin.BottomMargin = new BottomMargin { Width = DipToTwips(value.Bottom), Type = TableWidthUnitValues.Dxa };
        }

        return margin.ChildElements.Count > 0 ? margin : null;
    }

    private static TableCellMargin? BuildTableCellMargin(DocThickness? padding)
    {
        if (!HasPaddingValues(padding))
        {
            return null;
        }

        var value = padding!.Value;
        var margin = new TableCellMargin();
        if (!float.IsNaN(value.Left))
        {
            margin.LeftMargin = new LeftMargin { Width = DipToTwips(value.Left), Type = TableWidthUnitValues.Dxa };
        }

        if (!float.IsNaN(value.Right))
        {
            margin.RightMargin = new RightMargin { Width = DipToTwips(value.Right), Type = TableWidthUnitValues.Dxa };
        }

        if (!float.IsNaN(value.Top))
        {
            margin.TopMargin = new TopMargin { Width = DipToTwips(value.Top), Type = TableWidthUnitValues.Dxa };
        }

        if (!float.IsNaN(value.Bottom))
        {
            margin.BottomMargin = new BottomMargin { Width = DipToTwips(value.Bottom), Type = TableWidthUnitValues.Dxa };
        }

        return margin.ChildElements.Count > 0 ? margin : null;
    }

    private static bool HasTableProperties(Vibe.Office.Documents.TableProperties properties)
    {
        return HasPaddingValues(properties.CellPadding)
               || properties.ShadingColor.HasValue
               || HasTableBorders(properties.Borders);
    }

    private static bool HasTableCellProperties(Vibe.Office.Documents.TableCellProperties properties)
    {
        return HasPaddingValues(properties.Padding)
               || properties.ShadingColor.HasValue
               || properties.VerticalAlignment.HasValue
               || properties.TextDirection.HasValue
               || HasTableCellBorders(properties.Borders);
    }

    private static bool HasPaddingValues(DocThickness? padding)
    {
        if (!padding.HasValue)
        {
            return false;
        }

        var value = padding.Value;
        return !float.IsNaN(value.Left)
               || !float.IsNaN(value.Top)
               || !float.IsNaN(value.Right)
               || !float.IsNaN(value.Bottom);
    }

    private static bool HasTableBorders(Vibe.Office.Documents.TableBorders borders)
    {
        return borders.Top is not null
               || borders.Bottom is not null
               || borders.Left is not null
               || borders.Right is not null
               || borders.InsideHorizontal is not null
               || borders.InsideVertical is not null;
    }

    private static bool HasTableCellBorders(Vibe.Office.Documents.TableCellBorders borders)
    {
        return borders.Top is not null
               || borders.Bottom is not null
               || borders.Left is not null
               || borders.Right is not null;
    }

    private static bool HasParagraphBorders(Vibe.Office.Documents.ParagraphBorders borders)
    {
        return borders.Top is not null
               || borders.Bottom is not null
               || borders.Left is not null
               || borders.Right is not null;
    }

    private static OpenXmlParagraphBorders? BuildParagraphBorders(Vibe.Office.Documents.ParagraphBorders borders)
    {
        if (!HasParagraphBorders(borders))
        {
            return null;
        }

        return new OpenXmlParagraphBorders
        {
            TopBorder = BuildBorder<TopBorder>(borders.Top),
            BottomBorder = BuildBorder<BottomBorder>(borders.Bottom),
            LeftBorder = BuildBorder<LeftBorder>(borders.Left),
            RightBorder = BuildBorder<RightBorder>(borders.Right)
        };
    }

    private static FrameProperties? BuildFrameProperties(ParagraphFrameProperties? frame, DropCapSettings? dropCap)
    {
        if ((frame?.HasValues ?? false) == false && (dropCap?.HasValues ?? false) == false)
        {
            return null;
        }

        var properties = new FrameProperties();

        if (frame?.Width.HasValue == true)
        {
            properties.Width = DipToTwips(frame.Width.Value);
        }

        if (frame?.Height.HasValue == true)
        {
            properties.Height = DipToTwipsUInt32(frame.Height.Value);
        }

        if (frame?.X.HasValue == true)
        {
            properties.X = DipToTwips(frame.X.Value);
        }

        if (frame?.Y.HasValue == true)
        {
            properties.Y = DipToTwips(frame.Y.Value);
        }

        var hSpace = frame?.HorizontalSpace;
        var vSpace = frame?.VerticalSpace;
        if (hSpace.HasValue)
        {
            properties.HorizontalSpace = DipToTwips(hSpace.Value);
        }

        if (vSpace.HasValue)
        {
            properties.VerticalSpace = DipToTwips(vSpace.Value);
        }

        if (frame?.HorizontalReference.HasValue == true)
        {
            properties.HorizontalPosition = MapFrameHorizontalReference(frame.HorizontalReference.Value);
        }

        if (frame?.VerticalReference.HasValue == true)
        {
            properties.VerticalPosition = MapFrameVerticalReference(frame.VerticalReference.Value);
        }

        if (frame?.HorizontalAlignment.HasValue == true)
        {
            properties.XAlign = MapFrameHorizontalAlignment(frame.HorizontalAlignment.Value);
        }

        if (frame?.VerticalAlignment.HasValue == true)
        {
            properties.YAlign = MapFrameVerticalAlignment(frame.VerticalAlignment.Value);
        }

        if (frame?.WrapStyle.HasValue == true)
        {
            properties.Wrap = MapFrameWrapStyle(frame.WrapStyle.Value);
        }

        if (frame?.AnchorLock.HasValue == true)
        {
            properties.AnchorLock = frame.AnchorLock.Value;
        }

        if (dropCap is not null)
        {
            properties.DropCap = dropCap.Kind == DropCapKind.Margin
                ? DropCapLocationValues.Margin
                : DropCapLocationValues.Drop;

            if (dropCap.Lines > 0)
            {
                properties.Lines = dropCap.Lines;
            }

            if (dropCap.Distance.HasValue && !hSpace.HasValue)
            {
                properties.HorizontalSpace = DipToTwips(dropCap.Distance.Value);
            }
        }

        return properties;
    }

    private static OpenXmlTableBorders? BuildTableBorders(Vibe.Office.Documents.TableBorders borders)
    {
        if (!HasTableBorders(borders))
        {
            return null;
        }

        return new OpenXmlTableBorders
        {
            TopBorder = BuildBorder<TopBorder>(borders.Top),
            BottomBorder = BuildBorder<BottomBorder>(borders.Bottom),
            LeftBorder = BuildBorder<LeftBorder>(borders.Left),
            RightBorder = BuildBorder<RightBorder>(borders.Right),
            InsideHorizontalBorder = BuildBorder<InsideHorizontalBorder>(borders.InsideHorizontal),
            InsideVerticalBorder = BuildBorder<InsideVerticalBorder>(borders.InsideVertical)
        };
    }

    private static OpenXmlTableCellBorders? BuildTableCellBorders(Vibe.Office.Documents.TableCellBorders borders)
    {
        if (!HasTableCellBorders(borders))
        {
            return null;
        }

        return new OpenXmlTableCellBorders
        {
            TopBorder = BuildBorder<TopBorder>(borders.Top),
            BottomBorder = BuildBorder<BottomBorder>(borders.Bottom),
            LeftBorder = BuildBorder<LeftBorder>(borders.Left),
            RightBorder = BuildBorder<RightBorder>(borders.Right)
        };
    }

    private static TBorder? BuildBorder<TBorder>(BorderLine? border)
        where TBorder : BorderType, new()
    {
        if (border is null)
        {
            return null;
        }

        var size = border.Style == DocBorderStyle.None ? 0u : BorderDipToEighthPoints(border.Thickness);
        var element = new TBorder
        {
            Val = MapBorderStyle(border.Style),
            Size = size,
            Color = ColorToHex(border.Color)
        };

        if (border.Spacing.HasValue)
        {
            element.Space = DipToBorderSpace(border.Spacing.Value);
        }

        return element;
    }

    private static UInt32Value DipToBorderSpace(float value)
    {
        var points = value / (96f / 72f);
        var rounded = (uint)Math.Max(0, Math.Round(points));
        return new UInt32Value(rounded);
    }

    private static BorderValues MapBorderStyle(DocBorderStyle style)
    {
        return style switch
        {
            DocBorderStyle.None => BorderValues.None,
            DocBorderStyle.Double => BorderValues.Double,
            DocBorderStyle.Dotted => BorderValues.Dotted,
            DocBorderStyle.Dashed => BorderValues.Dashed,
            DocBorderStyle.DotDash => BorderValues.DotDash,
            DocBorderStyle.DotDotDash => BorderValues.DotDotDash,
            DocBorderStyle.Thick => BorderValues.Thick,
            DocBorderStyle.Hairline => BorderValues.Single,
            _ => BorderValues.Single
        };
    }

    private static TableStyleOverrideValues MapTableStyleCondition(TableStyleCondition condition)
    {
        return condition switch
        {
            TableStyleCondition.FirstRow => TableStyleOverrideValues.FirstRow,
            TableStyleCondition.LastRow => TableStyleOverrideValues.LastRow,
            TableStyleCondition.FirstColumn => TableStyleOverrideValues.FirstColumn,
            TableStyleCondition.LastColumn => TableStyleOverrideValues.LastColumn,
            TableStyleCondition.Band1Horizontal => TableStyleOverrideValues.Band1Horizontal,
            TableStyleCondition.Band2Horizontal => TableStyleOverrideValues.Band2Horizontal,
            TableStyleCondition.Band1Vertical => TableStyleOverrideValues.Band1Vertical,
            TableStyleCondition.Band2Vertical => TableStyleOverrideValues.Band2Vertical,
            _ => TableStyleOverrideValues.WholeTable
        };
    }

    private static Drawing CreateShapeDrawing(
        ShapeInline shapeInline,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts,
        FloatingAnchor? anchor = null)
    {
        var widthEmu = DipToEmu(shapeInline.Width);
        var heightEmu = DipToEmu(shapeInline.Height);
        var name = string.IsNullOrWhiteSpace(shapeInline.Name) ? "Shape" : shapeInline.Name;
        var docProperties = new DW.DocProperties { Id = 1U, Name = name };

        var shapeProperties = BuildShapeProperties(shapeInline, widthEmu, heightEmu);
        var bodyProperties = BuildShapeBodyProperties(shapeInline.TextBox);
        var textBox = BuildShapeTextBox(shapeInline.TextBox, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);

        var wpsShape = new Wps.WordprocessingShape();
        wpsShape.AppendChild(new Wps.NonVisualDrawingProperties { Id = 0U, Name = name });
        wpsShape.AppendChild(new Wps.NonVisualDrawingShapeProperties());
        wpsShape.AppendChild(shapeProperties);
        if (bodyProperties is not null)
        {
            wpsShape.AppendChild(bodyProperties);
        }

        if (textBox is not null)
        {
            wpsShape.AppendChild(textBox);
        }

        var graphic = new A.Graphic(
            new A.GraphicData(wpsShape)
            { Uri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape" });

        return CreateDrawingContainer(docProperties, graphic, widthEmu, heightEmu, anchor);
    }

    private static Wps.ShapeProperties BuildShapeProperties(ShapeInline shapeInline, long widthEmu, long heightEmu)
    {
        var props = new Wps.ShapeProperties();
        var transform = new A.Transform2D(
            new A.Offset { X = 0L, Y = 0L },
            new A.Extents { Cx = widthEmu, Cy = heightEmu });

        if (MathF.Abs(shapeInline.Properties.Rotation) >= 0.01f)
        {
            transform.Rotation = (int)MathF.Round(shapeInline.Properties.Rotation * 60000f);
        }

        if (shapeInline.Properties.FlipHorizontal)
        {
            transform.HorizontalFlip = true;
        }

        if (shapeInline.Properties.FlipVertical)
        {
            transform.VerticalFlip = true;
        }

        props.AppendChild(transform);
        props.AppendChild(new A.PresetGeometry(new A.AdjustValueList())
        {
            Preset = MapShapePreset(shapeInline.Properties.PresetGeometry)
        });

        if (shapeInline.Properties.FillColor.HasValue)
        {
            props.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = ColorToHex(shapeInline.Properties.FillColor.Value) }));
        }
        else
        {
            props.AppendChild(new A.NoFill());
        }

        var outline = BuildShapeOutline(shapeInline.Properties.Outline);
        if (outline is not null)
        {
            props.AppendChild(outline);
        }

        var effects = BuildDrawingEffects(shapeInline.Properties.Effects);
        if (effects is not null)
        {
            props.AppendChild(effects);
        }

        return props;
    }

    private static Wps.TextBodyProperties? BuildShapeBodyProperties(ShapeTextBox? textBox)
    {
        if (textBox is null)
        {
            return null;
        }

        var body = new Wps.TextBodyProperties();
        var padding = textBox.Properties.Padding;
        body.LeftInset = (Int32Value)DipToEmu(padding.Left);
        body.TopInset = (Int32Value)DipToEmu(padding.Top);
        body.RightInset = (Int32Value)DipToEmu(padding.Right);
        body.BottomInset = (Int32Value)DipToEmu(padding.Bottom);

        body.Anchor = textBox.Properties.VerticalAlignment switch
        {
            ShapeTextVerticalAlignment.Center => A.TextAnchoringTypeValues.Center,
            ShapeTextVerticalAlignment.Bottom => A.TextAnchoringTypeValues.Bottom,
            _ => A.TextAnchoringTypeValues.Top
        };

        return body;
    }

    private static Wps.TextBoxInfo2? BuildShapeTextBox(
        ShapeTextBox? textBox,
        NumberingContext numberingContext,
        ImageWriter imageWriter,
        ChartWriter chartWriter,
        HyperlinkWriter hyperlinkWriter,
        DocumentFonts fonts)
    {
        if (textBox is null)
        {
            return null;
        }

        var info = new Wps.TextBoxInfo2();
        var content = new TextBoxContent();
        AppendBlocks(content, textBox.Blocks, numberingContext, imageWriter, chartWriter, hyperlinkWriter, fonts);
        if (!content.ChildElements.Any())
        {
            content.AppendChild(new Paragraph(new Run(new Text(string.Empty))));
        }

        info.TextBoxContent = content;
        return info;
    }

    private static A.ShapeTypeValues MapShapePreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return A.ShapeTypeValues.Rectangle;
        }

        var span = preset.AsSpan().Trim();
        if (span.Equals("rect", StringComparison.OrdinalIgnoreCase)
            || span.Equals("rectangle", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Rectangle;
        }

        if (span.Equals("roundrect", StringComparison.OrdinalIgnoreCase)
            || span.Equals("roundrectangle", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.RoundRectangle;
        }

        if (span.Equals("ellipse", StringComparison.OrdinalIgnoreCase)
            || span.Equals("oval", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Ellipse;
        }

        if (span.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Line;
        }

        if (span.Equals("triangle", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Triangle;
        }

        if (span.Equals("diamond", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Diamond;
        }

        if (span.Equals("parallelogram", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Parallelogram;
        }

        if (span.Equals("trapezoid", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Trapezoid;
        }

        if (span.Equals("hexagon", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Hexagon;
        }

        if (span.Equals("octagon", StringComparison.OrdinalIgnoreCase))
        {
            return A.ShapeTypeValues.Octagon;
        }

        return A.ShapeTypeValues.Rectangle;
    }

    private static A.Outline? BuildShapeOutline(BorderLine? border)
    {
        if (border is null)
        {
            return null;
        }

        if (!border.IsVisible)
        {
            return new A.Outline(new A.NoFill());
        }

        var outline = new A.Outline
        {
            Width = (Int32Value)DipToEmu(border.Thickness)
        };

        outline.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = ColorToHex(border.Color) }));
        var dash = MapShapeDash(border.Style);
        if (dash.HasValue)
        {
            outline.AppendChild(new A.PresetDash { Val = dash.Value });
        }

        return outline;
    }

    private static A.EffectList? BuildDrawingEffects(DrawingEffects? effects)
    {
        if (effects is null || !effects.HasValues)
        {
            return null;
        }

        var effectList = new A.EffectList();

        if (effects.Shadow is not null)
        {
            var shadow = effects.Shadow;
            var outerShadow = new A.OuterShadow();
            if (shadow.BlurRadius > 0f)
            {
                outerShadow.BlurRadius = DipToEmu(shadow.BlurRadius);
            }

            if (shadow.Distance > 0f)
            {
                outerShadow.Distance = DipToEmu(shadow.Distance);
            }

            if (MathF.Abs(shadow.Direction) > 0.01f)
            {
                outerShadow.Direction = (Int32Value)MathF.Round(shadow.Direction * 60000f);
            }

            outerShadow.AppendChild(BuildDrawingColor(shadow.Color));
            effectList.AppendChild(outerShadow);
        }

        if (effects.Glow is not null)
        {
            var glow = effects.Glow;
            var glowElement = new A.Glow();
            if (glow.Radius > 0f)
            {
                glowElement.Radius = DipToEmu(glow.Radius);
            }

            glowElement.AppendChild(BuildDrawingColor(glow.Color));
            effectList.AppendChild(glowElement);
        }

        if (effects.Reflection is not null)
        {
            var reflection = effects.Reflection;
            var reflectionElement = new A.Reflection();
            if (reflection.BlurRadius > 0f)
            {
                reflectionElement.BlurRadius = DipToEmu(reflection.BlurRadius);
            }

            if (reflection.Distance > 0f)
            {
                reflectionElement.Distance = DipToEmu(reflection.Distance);
            }

            if (reflection.StartOpacity > 0f)
            {
                reflectionElement.StartOpacity = ToDrawingPercentageValue(reflection.StartOpacity);
            }

            if (reflection.EndOpacity > 0f)
            {
                reflectionElement.EndAlpha = ToDrawingPercentageValue(reflection.EndOpacity);
            }

            if (reflection.ScaleX > 0f && MathF.Abs(reflection.ScaleX - 1f) > 0.001f)
            {
                reflectionElement.HorizontalRatio = ToDrawingPercentageValue(reflection.ScaleX);
            }

            if (reflection.ScaleY > 0f && MathF.Abs(reflection.ScaleY - 1f) > 0.001f)
            {
                reflectionElement.VerticalRatio = ToDrawingPercentageValue(reflection.ScaleY);
            }

            effectList.AppendChild(reflectionElement);
        }

        if (effects.SoftEdge is not null)
        {
            var softEdge = effects.SoftEdge;
            var softEdgeElement = new A.SoftEdge();
            if (softEdge.Radius > 0f)
            {
                softEdgeElement.Radius = DipToEmu(softEdge.Radius);
            }

            effectList.AppendChild(softEdgeElement);
        }

        return effectList.ChildElements.Count > 0 ? effectList : null;
    }

    private static A.RgbColorModelHex BuildDrawingColor(DocColor color)
    {
        var rgb = new A.RgbColorModelHex { Val = ColorToHex(color) };
        if (color.A < byte.MaxValue)
        {
            rgb.AppendChild(new A.Alpha { Val = ToDrawingPercentageValue(color.A / 255f) });
        }

        return rgb;
    }

    private static Int32Value ToDrawingPercentageValue(float value)
    {
        if (value <= 0f)
        {
            return 0;
        }

        if (value >= 1f)
        {
            return 100000;
        }

        return (Int32Value)MathF.Round(value * 100000f);
    }

    private static A.PresetLineDashValues? MapShapeDash(DocBorderStyle style)
    {
        return style switch
        {
            DocBorderStyle.Dotted => A.PresetLineDashValues.Dot,
            DocBorderStyle.Dashed => A.PresetLineDashValues.Dash,
            DocBorderStyle.DotDash => A.PresetLineDashValues.DashDot,
            DocBorderStyle.DotDotDash => A.PresetLineDashValues.SystemDashDotDot,
            _ => null
        };
    }

    private static Drawing CreateImageDrawing(ImageWriter writer, ImageInline imageInline, FloatingAnchor? anchor = null)
    {
        var relationshipId = writer.AddImage(imageInline);
        var widthEmu = DipToEmu(imageInline.Width);
        var heightEmu = DipToEmu(imageInline.Height);
        var docProperties = new DW.DocProperties { Id = 1U, Name = "Picture" };
        var shapeProperties = new PIC.ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = 0L, Y = 0L },
                new A.Extents { Cx = widthEmu, Cy = heightEmu }),
            new A.PresetGeometry(new A.AdjustValueList())
            {
                Preset = A.ShapeTypeValues.Rectangle
            });

        var effects = BuildDrawingEffects(imageInline.Effects);
        if (effects is not null)
        {
            shapeProperties.AppendChild(effects);
        }

        var graphic = new A.Graphic(
            new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Picture" },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(
                        new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                        new A.Stretch(new A.FillRectangle())),
                    shapeProperties)
            )
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });

        return CreateDrawingContainer(docProperties, graphic, widthEmu, heightEmu, anchor);
    }

    private static Drawing CreateChartDrawing(ChartWriter writer, ChartInline chartInline, FloatingAnchor? anchor = null)
    {
        var relationshipId = writer.AddChart(chartInline);
        var widthEmu = DipToEmu(chartInline.Width);
        var heightEmu = DipToEmu(chartInline.Height);
        var name = string.IsNullOrWhiteSpace(chartInline.Name) ? "Chart" : chartInline.Name;
        var docProperties = new DW.DocProperties { Id = 1U, Name = name };

        var graphic = new A.Graphic(
            new A.GraphicData(
                new C.ChartReference { Id = relationshipId })
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" });

        return CreateDrawingContainer(docProperties, graphic, widthEmu, heightEmu, anchor);
    }

    private static Drawing CreateDrawingContainer(DW.DocProperties docProperties, A.Graphic graphic, long widthEmu, long heightEmu, FloatingAnchor? anchor)
    {
        if (anchor is null)
        {
            return new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                    new DW.EffectExtent
                    {
                        LeftEdge = 0L,
                        TopEdge = 0L,
                        RightEdge = 0L,
                        BottomEdge = 0L
                    },
                    docProperties,
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    graphic)
                {
                    DistanceFromTop = 0U,
                    DistanceFromBottom = 0U,
                    DistanceFromLeft = 0U,
                    DistanceFromRight = 0U
                });
        }

        var anchorElement = BuildAnchorElement(anchor, docProperties, graphic, widthEmu, heightEmu);
        return new Drawing(anchorElement);
    }

    private static DW.Anchor BuildAnchorElement(FloatingAnchor anchor, DW.DocProperties docProperties, A.Graphic graphic, long widthEmu, long heightEmu)
    {
        var horizontal = new DW.HorizontalPosition
        {
            RelativeFrom = MapHorizontalReference(anchor.HorizontalReference)
        };
        if (anchor.HorizontalAlignment != FloatingHorizontalAlignment.None)
        {
            horizontal.HorizontalAlignment = new DW.HorizontalAlignment { Text = MapHorizontalAlignment(anchor.HorizontalAlignment) };
        }

        horizontal.PositionOffset = new DW.PositionOffset
        {
            Text = DipToEmu(anchor.OffsetX).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var vertical = new DW.VerticalPosition
        {
            RelativeFrom = MapVerticalReference(anchor.VerticalReference)
        };
        if (anchor.VerticalAlignment != FloatingVerticalAlignment.None)
        {
            vertical.VerticalAlignment = new DW.VerticalAlignment { Text = MapVerticalAlignment(anchor.VerticalAlignment) };
        }

        vertical.PositionOffset = new DW.PositionOffset
        {
            Text = DipToEmu(anchor.OffsetY).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var wrap = BuildWrapElement(anchor);
        var effectExtent = new DW.EffectExtent
        {
            LeftEdge = 0L,
            TopEdge = 0L,
            RightEdge = 0L,
            BottomEdge = 0L
        };

        return new DW.Anchor(
            new DW.SimplePosition { X = 0L, Y = 0L },
            horizontal,
            vertical,
            new DW.Extent { Cx = widthEmu, Cy = heightEmu },
            effectExtent,
            wrap,
            docProperties,
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = DipToEmuUInt32(anchor.Distance.Top),
            DistanceFromBottom = DipToEmuUInt32(anchor.Distance.Bottom),
            DistanceFromLeft = DipToEmuUInt32(anchor.Distance.Left),
            DistanceFromRight = DipToEmuUInt32(anchor.Distance.Right),
            BehindDoc = anchor.BehindText,
            LayoutInCell = true,
            AllowOverlap = true,
            SimplePos = false,
            RelativeHeight = 0U
        };
    }

    private static OpenXmlElement BuildWrapElement(FloatingAnchor anchor)
    {
        switch (anchor.WrapStyle)
        {
            case FloatingWrapStyle.Square:
            {
                return new DW.WrapSquare
                {
                    WrapText = MapWrapSide(anchor.WrapSide),
                    DistanceFromLeft = DipToEmuUInt32(anchor.Distance.Left),
                    DistanceFromRight = DipToEmuUInt32(anchor.Distance.Right),
                    DistanceFromTop = DipToEmuUInt32(anchor.Distance.Top),
                    DistanceFromBottom = DipToEmuUInt32(anchor.Distance.Bottom)
                };
            }
            case FloatingWrapStyle.Tight:
            {
                var wrap = new DW.WrapTight
                {
                    WrapText = MapWrapSide(anchor.WrapSide),
                    DistanceFromLeft = DipToEmuUInt32(anchor.Distance.Left),
                    DistanceFromRight = DipToEmuUInt32(anchor.Distance.Right)
                };
                wrap.WrapPolygon = BuildWrapPolygon(anchor.WrapPolygon);
                return wrap;
            }
            case FloatingWrapStyle.TopBottom:
                return new DW.WrapTopBottom();
            case FloatingWrapStyle.Through:
            {
                var wrap = new DW.WrapThrough
                {
                    WrapText = MapWrapSide(anchor.WrapSide),
                    DistanceFromLeft = DipToEmuUInt32(anchor.Distance.Left),
                    DistanceFromRight = DipToEmuUInt32(anchor.Distance.Right)
                };
                wrap.WrapPolygon = BuildWrapPolygon(anchor.WrapPolygon);
                return wrap;
            }
            default:
                return new DW.WrapNone();
        }
    }

    private static DW.WrapPolygon? BuildWrapPolygon(FloatingWrapPolygon? polygon)
    {
        if (polygon is null)
        {
            return null;
        }

        var points = polygon.Points;
        if (points.Length < 3)
        {
            return null;
        }

        var wrapPolygon = new DW.WrapPolygon
        {
            StartPoint = new DW.StartPoint
            {
                X = DipToEmu(points[0].X),
                Y = DipToEmu(points[0].Y)
            }
        };

        for (var i = 1; i < points.Length; i++)
        {
            var point = points[i];
            wrapPolygon.AppendChild(new DW.LineTo
            {
                X = DipToEmu(point.X),
                Y = DipToEmu(point.Y)
            });
        }

        return wrapPolygon;
    }

    private static DW.WrapTextValues MapWrapSide(FloatingWrapSide side)
    {
        return side switch
        {
            FloatingWrapSide.Left => DW.WrapTextValues.Left,
            FloatingWrapSide.Right => DW.WrapTextValues.Right,
            FloatingWrapSide.Largest => DW.WrapTextValues.Largest,
            _ => DW.WrapTextValues.BothSides
        };
    }

    private static DW.HorizontalRelativePositionValues MapHorizontalReference(FloatingHorizontalReference reference)
    {
        return reference switch
        {
            FloatingHorizontalReference.Page => DW.HorizontalRelativePositionValues.Page,
            FloatingHorizontalReference.Column => DW.HorizontalRelativePositionValues.Column,
            FloatingHorizontalReference.Character => DW.HorizontalRelativePositionValues.Character,
            _ => DW.HorizontalRelativePositionValues.Margin
        };
    }

    private static DW.VerticalRelativePositionValues MapVerticalReference(FloatingVerticalReference reference)
    {
        return reference switch
        {
            FloatingVerticalReference.Page => DW.VerticalRelativePositionValues.Page,
            FloatingVerticalReference.Paragraph => DW.VerticalRelativePositionValues.Paragraph,
            FloatingVerticalReference.Line => DW.VerticalRelativePositionValues.Line,
            _ => DW.VerticalRelativePositionValues.Margin
        };
    }

    private static string MapHorizontalAlignment(FloatingHorizontalAlignment alignment)
    {
        return alignment switch
        {
            FloatingHorizontalAlignment.Center => "center",
            FloatingHorizontalAlignment.Right => "right",
            FloatingHorizontalAlignment.Inside => "inside",
            FloatingHorizontalAlignment.Outside => "outside",
            _ => "left"
        };
    }

    private static string MapVerticalAlignment(FloatingVerticalAlignment alignment)
    {
        return alignment switch
        {
            FloatingVerticalAlignment.Center => "center",
            FloatingVerticalAlignment.Bottom => "bottom",
            FloatingVerticalAlignment.Inside => "inside",
            FloatingVerticalAlignment.Outside => "outside",
            _ => "top"
        };
    }

    private static long DipToEmu(float value)
    {
        return (long)Math.Round(value / 96f * 914400f);
    }

    private static UInt32Value DipToEmuUInt32(float value)
    {
        var emu = Math.Max(0, DipToEmu(value));
        var clamped = (uint)Math.Min(uint.MaxValue, emu);
        return new UInt32Value(clamped);
    }

    private static uint BorderDipToEighthPoints(float value)
    {
        var points = value / (96f / 72f);
        var eighths = points * 8f;
        return (uint)Math.Max(0, Math.Round(eighths));
    }

    private sealed record SectionPartInfo(
        string? HeaderId,
        string? FooterId,
        string? FirstHeaderId,
        string? FirstFooterId,
        string? EvenHeaderId,
        string? EvenFooterId);

    private sealed class HyperlinkWriter
    {
        private readonly OpenXmlPartContainer _container;

        public HyperlinkWriter(OpenXmlPartContainer container)
        {
            _container = container;
        }

        public string AddHyperlink(string uri)
        {
            var relationship = _container.AddHyperlinkRelationship(new Uri(uri), true);
            return relationship.Id;
        }
    }

    private sealed class ImageWriter
    {
        private readonly OpenXmlPartContainer _container;

        public ImageWriter(OpenXmlPartContainer container)
        {
            _container = container;
        }

        public string AddImage(ImageInline image)
        {
            var imagePart = _container switch
            {
                MainDocumentPart mainPart => mainPart.AddImagePart(image.ContentType),
                HeaderPart headerPart => headerPart.AddImagePart(image.ContentType),
                FooterPart footerPart => footerPart.AddImagePart(image.ContentType),
                _ => throw new NotSupportedException("Unsupported OpenXML part for images.")
            };
            using var stream = new MemoryStream(image.Data);
            imagePart.FeedData(stream);
            return _container.GetIdOfPart(imagePart);
        }
    }

    private sealed class ChartWriter
    {
        private readonly OpenXmlPartContainer _container;

        public ChartWriter(OpenXmlPartContainer container)
        {
            _container = container;
        }

        public string AddChart(ChartInline chart)
        {
            var chartPart = _container switch
            {
                MainDocumentPart mainPart => mainPart.AddNewPart<ChartPart>(),
                HeaderPart headerPart => headerPart.AddNewPart<ChartPart>(),
                FooterPart footerPart => footerPart.AddNewPart<ChartPart>(),
                _ => throw new NotSupportedException("Unsupported OpenXML part for charts.")
            };

            if (chart.PartData is { Length: > 0 })
            {
                using var stream = new MemoryStream(chart.PartData);
                chartPart.FeedData(stream);
            }
            else
            {
                chartPart.ChartSpace = BuildChartSpace(chart);
            }

            return _container.GetIdOfPart(chartPart);
        }
    }

    private static C.ChartSpace BuildChartSpace(ChartInline chart)
    {
        var chartSpace = new C.ChartSpace();
        var chartElement = chartSpace.AppendChild(new C.Chart());
        if (!string.IsNullOrWhiteSpace(chart.Model?.Title))
        {
            chartElement.AppendChild(BuildChartTitle(chart.Model.Title!));
        }

        var plotArea = chartElement.AppendChild(new C.PlotArea());

        if (chart.Model is null || chart.Model.Series.Count == 0)
        {
            plotArea.AppendChild(new C.Layout());
            return chartSpace;
        }

        switch (chart.Model.Type)
        {
            case ChartType.Line:
            {
                var lineChart = new C.LineChart();
                var grouping = MapChartGrouping(chart.Model.Stacking);
                if (grouping.HasValue)
                {
                    lineChart.AppendChild(new C.Grouping { Val = grouping.Value });
                }
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    lineChart.AppendChild(BuildLineSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(lineChart);
                break;
            }
            case ChartType.Doughnut:
            {
                var doughnutChart = new C.DoughnutChart();
                var holeSize = (byte)Math.Clamp((int)MathF.Round(chart.Model.DoughnutHoleSize * 100f), 10, 90);
                doughnutChart.AppendChild(new C.HoleSize { Val = holeSize });
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    doughnutChart.AppendChild(BuildPieSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(doughnutChart);
                break;
            }
            case ChartType.Pie:
            {
                var pieChart = new C.PieChart();
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    pieChart.AppendChild(BuildPieSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(pieChart);
                break;
            }
            case ChartType.Scatter:
            {
                var scatterChart = new C.ScatterChart(new C.ScatterStyle { Val = C.ScatterStyleValues.LineMarker });
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    scatterChart.AppendChild(BuildScatterSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(scatterChart);
                break;
            }
            case ChartType.Bubble:
            {
                var bubbleChart = new C.BubbleChart();
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    bubbleChart.AppendChild(BuildBubbleSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(bubbleChart);
                break;
            }
            case ChartType.Area:
            {
                var areaChart = new C.AreaChart();
                var grouping = MapChartGrouping(chart.Model.Stacking);
                if (grouping.HasValue)
                {
                    areaChart.AppendChild(new C.Grouping { Val = grouping.Value });
                }
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    areaChart.AppendChild(BuildAreaSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(areaChart);
                break;
            }
            case ChartType.Radar:
            {
                var radarChart = new C.RadarChart();
                radarChart.AppendChild(new C.RadarStyle { Val = MapRadarStyle(chart.Model.RadarStyle) });
                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    radarChart.AppendChild(BuildRadarSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(radarChart);
                break;
            }
            case ChartType.Bar:
            default:
            {
                var direction = chart.Model.BarDirection == ChartBarDirection.Bar
                    ? C.BarDirectionValues.Bar
                    : C.BarDirectionValues.Column;
                var grouping = MapBarGrouping(chart.Model.Stacking);
                var barChart = new C.BarChart(
                    new C.BarDirection { Val = direction },
                    new C.BarGrouping { Val = grouping });

                for (var seriesIndex = 0; seriesIndex < chart.Model.Series.Count; seriesIndex++)
                {
                    barChart.AppendChild(BuildBarSeries(chart.Model.Series[seriesIndex], seriesIndex));
                }

                plotArea.AppendChild(barChart);
                break;
            }
        }

        plotArea.AppendChild(new C.Layout());
        return chartSpace;
    }

    private static C.BarChartSeries BuildBarSeries(ChartSeries series, int seriesIndex)
    {
        var barSeries = new C.BarChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        barSeries.AppendChild(BuildCategoryAxisData(series));
        barSeries.AppendChild(BuildValues(series));
        return barSeries;
    }

    private static C.LineChartSeries BuildLineSeries(ChartSeries series, int seriesIndex)
    {
        var lineSeries = new C.LineChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        lineSeries.AppendChild(BuildCategoryAxisData(series));
        lineSeries.AppendChild(BuildValues(series));
        return lineSeries;
    }

    private static C.PieChartSeries BuildPieSeries(ChartSeries series, int seriesIndex)
    {
        var pieSeries = new C.PieChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        pieSeries.AppendChild(BuildCategoryAxisData(series));
        pieSeries.AppendChild(BuildValues(series));
        return pieSeries;
    }

    private static C.ScatterChartSeries BuildScatterSeries(ChartSeries series, int seriesIndex)
    {
        var scatterSeries = new C.ScatterChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        scatterSeries.AppendChild(BuildXValues(series));
        scatterSeries.AppendChild(BuildYValues(series));
        return scatterSeries;
    }

    private static C.BubbleChartSeries BuildBubbleSeries(ChartSeries series, int seriesIndex)
    {
        var bubbleSeries = new C.BubbleChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        bubbleSeries.AppendChild(BuildXValues(series));
        bubbleSeries.AppendChild(BuildYValues(series));
        bubbleSeries.AppendChild(BuildBubbleSize(series));
        return bubbleSeries;
    }

    private static C.AreaChartSeries BuildAreaSeries(ChartSeries series, int seriesIndex)
    {
        var areaSeries = new C.AreaChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        areaSeries.AppendChild(BuildCategoryAxisData(series));
        areaSeries.AppendChild(BuildValues(series));
        return areaSeries;
    }

    private static C.RadarChartSeries BuildRadarSeries(ChartSeries series, int seriesIndex)
    {
        var radarSeries = new C.RadarChartSeries(
            new C.Index { Val = (uint)seriesIndex },
            new C.Order { Val = (uint)seriesIndex },
            new C.SeriesText(new C.NumericValue { Text = ResolveSeriesName(series, seriesIndex) }));

        radarSeries.AppendChild(BuildCategoryAxisData(series));
        radarSeries.AppendChild(BuildValues(series));
        return radarSeries;
    }

    private static string ResolveSeriesName(ChartSeries series, int seriesIndex)
    {
        return string.IsNullOrWhiteSpace(series.Name)
            ? $"Series {seriesIndex + 1}"
            : series.Name!;
    }

    private static C.CategoryAxisData BuildCategoryAxisData(ChartSeries series)
    {
        var categories = new C.CategoryAxisData();
        var stringLiteral = new C.StringLiteral();
        stringLiteral.AppendChild(new C.PointCount { Val = (uint)series.Points.Count });
        for (var i = 0; i < series.Points.Count; i++)
        {
            var category = series.Points[i].Category ?? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            stringLiteral.AppendChild(new C.StringPoint
            {
                Index = (uint)i,
                NumericValue = new C.NumericValue(category)
            });
        }

        categories.AppendChild(stringLiteral);
        return categories;
    }

    private static C.Values BuildValues(ChartSeries series)
    {
        var values = new C.Values();
        var numberLiteral = new C.NumberLiteral();
        numberLiteral.AppendChild(new C.PointCount { Val = (uint)series.Points.Count });
        for (var i = 0; i < series.Points.Count; i++)
        {
            var valueText = series.Points[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            numberLiteral.AppendChild(new C.NumericPoint
            {
                Index = (uint)i,
                NumericValue = new C.NumericValue(valueText)
            });
        }

        values.AppendChild(numberLiteral);
        return values;
    }

    private static C.XValues BuildXValues(ChartSeries series)
    {
        var values = new C.XValues();
        var numberLiteral = new C.NumberLiteral();
        numberLiteral.AppendChild(new C.PointCount { Val = (uint)series.Points.Count });
        for (var i = 0; i < series.Points.Count; i++)
        {
            var point = series.Points[i];
            var numeric = point.XValue ?? (double)i;
            var category = point.Category;
            if (!point.XValue.HasValue && !string.IsNullOrWhiteSpace(category)
                && double.TryParse(category, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                numeric = parsed;
            }

            var valueText = numeric.ToString(System.Globalization.CultureInfo.InvariantCulture);
            numberLiteral.AppendChild(new C.NumericPoint
            {
                Index = (uint)i,
                NumericValue = new C.NumericValue(valueText)
            });
        }

        values.AppendChild(numberLiteral);
        return values;
    }

    private static C.YValues BuildYValues(ChartSeries series)
    {
        var values = new C.YValues();
        var numberLiteral = new C.NumberLiteral();
        numberLiteral.AppendChild(new C.PointCount { Val = (uint)series.Points.Count });
        for (var i = 0; i < series.Points.Count; i++)
        {
            var valueText = series.Points[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            numberLiteral.AppendChild(new C.NumericPoint
            {
                Index = (uint)i,
                NumericValue = new C.NumericValue(valueText)
            });
        }

        values.AppendChild(numberLiteral);
        return values;
    }

    private static C.BubbleSize BuildBubbleSize(ChartSeries series)
    {
        var values = new C.BubbleSize();
        var numberLiteral = new C.NumberLiteral();
        numberLiteral.AppendChild(new C.PointCount { Val = (uint)series.Points.Count });
        for (var i = 0; i < series.Points.Count; i++)
        {
            var size = series.Points[i].Size ?? 1d;
            if (size <= 0)
            {
                size = 1d;
            }

            var valueText = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            numberLiteral.AppendChild(new C.NumericPoint
            {
                Index = (uint)i,
                NumericValue = new C.NumericValue(valueText)
            });
        }

        values.AppendChild(numberLiteral);
        return values;
    }

    private static C.Title BuildChartTitle(string titleText)
    {
        var richText = new C.RichText(
            new A.BodyProperties(),
            new A.ListStyle(),
            new A.Paragraph(
                new A.Run(
                    new A.RunProperties(),
                    new A.Text(titleText))));

        return new C.Title(
            new C.ChartText(richText),
            new C.Layout(),
            new C.Overlay { Val = false });
    }

    private static C.BarGroupingValues MapBarGrouping(ChartStacking stacking)
    {
        return stacking switch
        {
            ChartStacking.Stacked => C.BarGroupingValues.Stacked,
            ChartStacking.Percent => C.BarGroupingValues.PercentStacked,
            _ => C.BarGroupingValues.Clustered
        };
    }

    private static C.GroupingValues? MapChartGrouping(ChartStacking stacking)
    {
        return stacking switch
        {
            ChartStacking.Stacked => C.GroupingValues.Stacked,
            ChartStacking.Percent => C.GroupingValues.PercentStacked,
            _ => null
        };
    }

    private static C.RadarStyleValues MapRadarStyle(ChartRadarStyle style)
    {
        return style switch
        {
            ChartRadarStyle.Marker => C.RadarStyleValues.Marker,
            ChartRadarStyle.Filled => C.RadarStyleValues.Filled,
            _ => C.RadarStyleValues.Standard
        };
    }
}
