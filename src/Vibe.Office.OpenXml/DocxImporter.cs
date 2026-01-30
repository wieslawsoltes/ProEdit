using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;
using W14 = DocumentFormat.OpenXml.Office2010.Word;
using W15 = DocumentFormat.OpenXml.Office2013.Word;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using OpenXmlTableCellBorders = DocumentFormat.OpenXml.Wordprocessing.TableCellBorders;
using OpenXmlParagraphBorders = DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders;
using OpenXmlPageBorders = DocumentFormat.OpenXml.Wordprocessing.PageBorders;
using Vibe.Office.Documents;
using Vibe.Office.Macros;
using Vibe.Office.Primitives;
using VibeDocument = Vibe.Office.Documents.Document;

namespace Vibe.Office.OpenXml;

public sealed class DocxImporter
{
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wordprocessing16DuNamespace = "http://schemas.microsoft.com/office/word/2023/wordml/word16du";
    private const string OleObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";
    private static readonly XNamespace BibliographyNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";

    public VibeDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        using var wordDocument = WordprocessingDocument.Open(filePath, false);
        return LoadDocument(wordDocument);
    }

    public VibeDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var wordDocument = WordprocessingDocument.Open(stream, false);
        return LoadDocument(wordDocument);
    }

    private VibeDocument LoadDocument(WordprocessingDocument wordDocument)
    {
        var mainPart = wordDocument.MainDocumentPart;
        var body = mainPart?.Document?.Body;

        var document = new VibeDocument();
        document.Blocks.Clear();
        document.Sections.Clear();
        LoadDocumentProperties(wordDocument, document);
        LoadFonts(mainPart, document);
        LoadDocumentSettings(mainPart, document);
        LoadDocumentBackground(mainPart, document);
        LoadCustomXmlParts(mainPart, document);
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer, document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));

        if (body is null)
        {
            document.Blocks.Add(new ParagraphBlock());
            LoadMacros(mainPart, document);
            LoadBibliography(mainPart, document);
            return document;
        }

        var styleResolver = new StyleResolver(mainPart, document);
        var listResolver = new ListResolver(mainPart, document, styleResolver);
        var imageResolver = new ImageResolver(mainPart, document.ThemeColors);
        var chartResolver = new ChartResolver(mainPart, document.ThemeColors);
        var hyperlinkResolver = new HyperlinkResolver(mainPart);
        var placeholderResolver = new ContentControlPlaceholderResolver(mainPart?.GlossaryDocumentPart);
        LoadNotesAndComments(mainPart, document, listResolver, styleResolver, placeholderResolver);
        var currentSectionIndex = 0;
        var currentSection = document.GetSection(currentSectionIndex);

        void ProcessParagraph(Paragraph paragraph)
        {
            foreach (var block in ParseParagraphBlocks(paragraph, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver, true))
            {
                document.Blocks.Add(block);
            }

            var sectionProps = paragraph.ParagraphProperties?.SectionProperties;
            if (sectionProps is null)
            {
                return;
            }

            ApplySectionProperties(currentSection.Properties, ParseSectionProperties(sectionProps));
            LoadSectionHeaderFooter(mainPart, sectionProps, currentSection, document, listResolver, styleResolver, placeholderResolver);

            var breakType = ParseSectionBreakType(sectionProps);
            var nextSection = new DocumentSection(
                document.SectionProperties.Clone(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter());
            document.Sections.Add(nextSection);
            currentSectionIndex = document.Sections.Count - 1;
            document.Blocks.Add(new SectionBreakBlock
            {
                BreakType = breakType,
                SectionIndex = currentSectionIndex
            });
            currentSection = nextSection;
        }

        void ProcessRevisionBlocks(OpenXmlElement element, RevisionKind kind)
        {
            var revision = ResolveRevision(element, kind, document.Revisions);
            document.Blocks.Add(new RevisionStartBlock(revision));
            foreach (var child in element.ChildElements)
            {
                ProcessBlockElement(child);
            }
            document.Blocks.Add(new RevisionEndBlock(kind, revision.Id));
        }

        void ProcessBlockElement(OpenXmlElement element)
        {
            if (TryGetRevisionKind(element, out var revisionKind))
            {
                ProcessRevisionBlocks(element, revisionKind);
                return;
            }

            switch (element)
            {
                case Paragraph paragraph:
                    ProcessParagraph(paragraph);
                    break;
                case AltChunk altChunk:
                    AppendAltChunkBlocks(document.Blocks, altChunk, imageResolver.Part);
                    break;
                case Table table:
                    document.Blocks.Add(ParseTable(table, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver));
                    break;
                case SdtBlock sdtBlock:
                    foreach (var block in ParseSdtBlock(sdtBlock, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                    {
                        document.Blocks.Add(block);
                    }
                    break;
            }
        }

        foreach (var element in body.Elements())
        {
            ProcessBlockElement(element);
        }

        var bodySectionProps = body.Elements<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>().LastOrDefault();
        if (bodySectionProps is not null)
        {
            ApplySectionProperties(currentSection.Properties, ParseSectionProperties(bodySectionProps));
            LoadSectionHeaderFooter(mainPart, bodySectionProps, currentSection, document, listResolver, styleResolver, placeholderResolver);
        }

        if (document.Blocks.Count == 0 || document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        LoadMacros(mainPart, document);
        LoadBibliography(mainPart, document);
        return document;
    
    }

    private static ParagraphBlock ParseParagraph(
        Paragraph paragraph,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var blocks = ParseParagraphBlocks(paragraph, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver, false);
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock paragraphBlock)
            {
                return paragraphBlock;
            }
        }

        return new ParagraphBlock();
    }

    private static TableBlock ParseTable(
        Table table,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var tableBlock = new TableBlock();
        var tableProps = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableProperties>().FirstOrDefault();
        var tableStyleId = tableProps?.TableStyle?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(tableStyleId))
        {
            tableBlock.StyleId = tableStyleId;
        }

        ApplyTableProperties(table, tableBlock.Properties, styleResolver.ThemeColors);

        Vibe.Office.Documents.TableCell ParseTableCell(DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
        {
            var tableCell = new Vibe.Office.Documents.TableCell();
            ApplyTableCellStructure(cell, tableCell);
            ApplyTableCellProperties(cell, tableCell.Properties, styleResolver.ThemeColors);
            foreach (var element in cell.Elements())
            {
                AppendBlockElement(element, tableCell.Blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            }

            if (tableCell.Blocks.Count == 0)
            {
                tableCell.Blocks.Add(new ParagraphBlock());
            }

            return tableCell;
        }

        Vibe.Office.Documents.TableCell? ParseTableCellElement(OpenXmlElement cellElement)
        {
            if (IsMetadataWrapper(cellElement))
            {
                var metadata = BuildMetadataContainer(cellElement);
                var innerCellElement = EnumerateMetadataContent(cellElement).FirstOrDefault();
                var parsed = innerCellElement is not null ? ParseTableCellElement(innerCellElement) : null;
                if (parsed is not null)
                {
                    parsed.Metadata.Insert(0, metadata);
                }

                return parsed;
            }

            if (cellElement is DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
            {
                return ParseTableCell(cell);
            }

            if (cellElement is SdtCell sdtCell)
            {
                var cellControl = ParseContentControlProperties(sdtCell.SdtProperties, ContentControlKind.Cell, placeholderResolver);
                var innerCell = sdtCell.SdtContentCell?.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().FirstOrDefault();
                if (innerCell is null)
                {
                    return null;
                }

                var tableCell = ParseTableCell(innerCell);
                tableCell.ContentControl = cellControl;
                return tableCell;
            }

            return null;
        }

        Vibe.Office.Documents.TableRow? ParseTableRowElement(OpenXmlElement rowElement)
        {
            if (IsMetadataWrapper(rowElement))
            {
                var metadata = BuildMetadataContainer(rowElement);
                var innerRowElement = EnumerateMetadataContent(rowElement).FirstOrDefault();
                var parsed = innerRowElement is not null ? ParseTableRowElement(innerRowElement) : null;
                if (parsed is not null)
                {
                    parsed.Metadata.Insert(0, metadata);
                }

                return parsed;
            }

            DocumentFormat.OpenXml.Wordprocessing.TableRow? row = null;
            ContentControlProperties? rowContentControl = null;
            if (rowElement is DocumentFormat.OpenXml.Wordprocessing.TableRow directRow)
            {
                row = directRow;
            }
            else if (rowElement is SdtRow sdtRow)
            {
                rowContentControl = ParseContentControlProperties(sdtRow.SdtProperties, ContentControlKind.Row, placeholderResolver);
                row = sdtRow.SdtContentRow?.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().FirstOrDefault();
            }

            if (row is null)
            {
                return null;
            }

            var tableRow = new Vibe.Office.Documents.TableRow { ContentControl = rowContentControl };
            ApplyTableRowProperties(row, tableRow.Properties, styleResolver.ThemeColors);
            foreach (var cellElement in row.Elements())
            {
                var parsedCell = ParseTableCellElement(cellElement);
                if (parsedCell is not null)
                {
                    tableRow.Cells.Add(parsedCell);
                }
            }

            return tableRow;
        }

        void AppendRow(Vibe.Office.Documents.TableRow tableRow)
        {
            tableBlock.Rows.Add(tableRow);
        }

        foreach (var rowElement in table.Elements())
        {
            var tableRow = ParseTableRowElement(rowElement);
            if (tableRow is null)
            {
                continue;
            }

            AppendRow(tableRow);
        }

        return tableBlock;
    }

    private static void LoadSectionHeaderFooter(
        MainDocumentPart? mainPart,
        DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps,
        DocumentSection section,
        VibeDocument document,
        ListResolver listResolver,
        StyleResolver styleResolver,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        if (mainPart is null)
        {
            return;
        }

        static void PopulateHeader(
            HeaderFooter target,
            HeaderPart? headerPart,
            bool isDefined,
            ListResolver listResolver,
            StyleResolver styleResolver,
            DocumentRevisions revisions,
            ContentControlPlaceholderResolver? placeholderResolver,
            DocumentThemeColorMap themeColors)
        {
            target.Blocks.Clear();
            target.IsDefined = isDefined;
            if (headerPart?.Header is null)
            {
                return;
            }

            var headerBlocks = ParseHeaderFooter(headerPart.Header, listResolver, new ImageResolver(headerPart, themeColors), new ChartResolver(headerPart, themeColors), new HyperlinkResolver(headerPart), styleResolver, revisions, placeholderResolver);
            foreach (var block in headerBlocks)
            {
                target.Blocks.Add(block);
            }
        }

        static void PopulateFooter(
            HeaderFooter target,
            FooterPart? footerPart,
            bool isDefined,
            ListResolver listResolver,
            StyleResolver styleResolver,
            DocumentRevisions revisions,
            ContentControlPlaceholderResolver? placeholderResolver,
            DocumentThemeColorMap themeColors)
        {
            target.Blocks.Clear();
            target.IsDefined = isDefined;
            if (footerPart?.Footer is null)
            {
                return;
            }

            var footerBlocks = ParseHeaderFooter(footerPart.Footer, listResolver, new ImageResolver(footerPart, themeColors), new ChartResolver(footerPart, themeColors), new HyperlinkResolver(footerPart), styleResolver, revisions, placeholderResolver);
            foreach (var block in footerBlocks)
            {
                target.Blocks.Add(block);
            }
        }

        static bool HasHeaderReference(DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps, HeaderFooterValues type)
        {
            return sectionProps.Elements<HeaderReference>()
                .Any(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
        }

        static bool HasFooterReference(DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps, HeaderFooterValues type)
        {
            return sectionProps.Elements<FooterReference>()
                .Any(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
        }

        static (HeaderPart? Part, bool IsDefined) ResolveHeaderPart(
            MainDocumentPart mainPart,
            DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps,
            HeaderFooterValues type)
        {
            var headerRef = sectionProps.Elements<HeaderReference>()
                .FirstOrDefault(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
            if (headerRef?.Id?.Value is string headerId)
            {
                return (mainPart.GetPartById(headerId) as HeaderPart, true);
            }

            return (null, false);
        }

        static (FooterPart? Part, bool IsDefined) ResolveFooterPart(
            MainDocumentPart mainPart,
            DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps,
            HeaderFooterValues type)
        {
            var footerRef = sectionProps.Elements<FooterReference>()
                .FirstOrDefault(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
            if (footerRef?.Id?.Value is string footerId)
            {
                return (mainPart.GetPartById(footerId) as FooterPart, true);
            }

            return (null, false);
        }

        var hasEvenHeader = HasHeaderReference(sectionProps, HeaderFooterValues.Even);
        var hasEvenFooter = HasFooterReference(sectionProps, HeaderFooterValues.Even);
        if (hasEvenHeader || hasEvenFooter)
        {
            document.EvenAndOddHeaders = true;
        }

        var (defaultHeaderPart, defaultHeaderDefined) = ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.Default);
        PopulateHeader(section.Header, defaultHeaderPart, defaultHeaderDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);

        var (defaultFooterPart, defaultFooterDefined) = ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.Default);
        PopulateFooter(section.Footer, defaultFooterPart, defaultFooterDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);

        var (firstHeaderPart, firstHeaderDefined) = ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.First);
        firstHeaderDefined |= HasHeaderReference(sectionProps, HeaderFooterValues.First);
        PopulateHeader(section.FirstHeader, firstHeaderPart, firstHeaderDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);

        var (firstFooterPart, firstFooterDefined) = ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.First);
        firstFooterDefined |= HasFooterReference(sectionProps, HeaderFooterValues.First);
        PopulateFooter(section.FirstFooter, firstFooterPart, firstFooterDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);

        var (evenHeaderPart, evenHeaderDefined) = ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.Even);
        evenHeaderDefined |= hasEvenHeader;
        PopulateHeader(section.EvenHeader, evenHeaderPart, evenHeaderDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);

        var (evenFooterPart, evenFooterDefined) = ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.Even);
        evenFooterDefined |= hasEvenFooter;
        PopulateFooter(section.EvenFooter, evenFooterPart, evenFooterDefined, listResolver, styleResolver, document.Revisions, placeholderResolver, document.ThemeColors);
    }

    private static void LoadDocumentSettings(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart?.DocumentSettingsPart?.Settings is not Settings settings)
        {
            return;
        }

        var mirrorMargins = ReadOnOff(settings.GetFirstChild<MirrorMargins>());
        if (mirrorMargins.HasValue)
        {
            document.MirrorMargins = mirrorMargins.Value;
        }

        var gutterAtTop = ReadOnOff(settings.GetFirstChild<GutterAtTop>());
        if (gutterAtTop.HasValue)
        {
            document.GutterAtTop = gutterAtTop.Value;
        }

        var evenAndOddHeaders = ReadOnOff(settings.GetFirstChild<EvenAndOddHeaders>());
        if (evenAndOddHeaders.HasValue)
        {
            document.EvenAndOddHeaders = evenAndOddHeaders.Value;
        }

        var trackRevisions = ReadOnOff(settings.GetFirstChild<TrackRevisions>());
        if (trackRevisions.HasValue)
        {
            document.TrackChangesEnabled = trackRevisions.Value;
        }

        LoadCompatibilitySettings(settings, document);
    }

    private static void LoadCompatibilitySettings(Settings settings, VibeDocument document)
    {
        var compat = settings.GetFirstChild<Compatibility>();
        if (compat is null)
        {
            return;
        }

        var target = document.Compatibility;
        target.SuppressSpacingAtTopOfPage = ReadOnOff(compat.GetFirstChild<SuppressSpacingAtTopOfPage>());
        target.SuppressSpacingBeforeAfterPageBreak = ReadOnOff(compat.GetFirstChild<SuppressSpacingBeforeAfterPageBreak>());
        target.UseWord97LineBreakRules = ReadOnOff(compat.GetFirstChild<UseWord97LineBreakRules>());
        target.WrapTrailSpaces = ReadOnOff(compat.GetFirstChild<WrapTrailSpaces>());
        target.DoNotUseEastAsianBreakRules = ReadOnOff(compat.GetFirstChild<DoNotUseEastAsianBreakRules>());
        target.UseAltKinsokuLineBreakRules = ReadOnOff(compat.GetFirstChild<UseAltKinsokuLineBreakRules>());
        target.DoNotWrapTextWithPunctuation = ReadOnOff(compat.GetFirstChild<DoNotWrapTextWithPunctuation>());
    }

    private static void LoadDocumentBackground(MainDocumentPart? mainPart, VibeDocument document)
    {
        var background = mainPart?.Document?.GetFirstChild<DocumentBackground>();
        if (background is null)
        {
            return;
        }

        var color = background.Color?.Value;
        if (!string.IsNullOrWhiteSpace(color) && TryParseHexColor(color, out var parsed))
        {
            document.SectionProperties.PageBackgroundColor = parsed;
        }
    }

    private static void LoadDocumentProperties(WordprocessingDocument wordDocument, VibeDocument document)
    {
        var properties = wordDocument.PackageProperties;
        if (properties is null)
        {
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        document.Properties.SetCoreProperty("Title", properties.Title);
        document.Properties.SetCoreProperty("Subject", properties.Subject);
        document.Properties.SetCoreProperty("Author", properties.Creator);
        document.Properties.SetCoreProperty("Keywords", properties.Keywords);
        document.Properties.SetCoreProperty("Comments", properties.Description);
        document.Properties.SetCoreProperty("LastSavedBy", properties.LastModifiedBy);
        document.Properties.SetCoreProperty("Last Saved By", properties.LastModifiedBy);
        document.Properties.SetCoreProperty("Revision", properties.Revision);
        document.Properties.SetCoreProperty("Revision Number", properties.Revision);
        document.Properties.SetCoreProperty("Category", properties.Category);
        document.Properties.SetCoreProperty("ContentStatus", properties.ContentStatus);
        document.Properties.SetCoreProperty("Status", properties.ContentStatus);
        document.Properties.SetCoreProperty("Identifier", properties.Identifier);
        document.Properties.SetCoreProperty("Language", properties.Language);
        document.Properties.SetCoreProperty("Version", properties.Version);

        if (properties.LastPrinted.HasValue)
        {
            var formatted = properties.LastPrinted.Value.ToString("G", culture);
            document.Properties.SetCoreProperty("LastPrinted", formatted);
            document.Properties.SetCoreProperty("Last Printed", formatted);
        }

        if (properties.Created.HasValue)
        {
            var formatted = properties.Created.Value.ToString("G", culture);
            document.Properties.SetCoreProperty("CreateDate", formatted);
            document.Properties.SetCoreProperty("Creation Date", formatted);
        }

        if (properties.Modified.HasValue)
        {
            var formatted = properties.Modified.Value.ToString("G", culture);
            document.Properties.SetCoreProperty("LastSaveTime", formatted);
            document.Properties.SetCoreProperty("Last Save Time", formatted);
        }

        var customPart = wordDocument.CustomFilePropertiesPart;
        if (customPart?.Properties is null)
        {
            return;
        }

        foreach (var property in customPart.Properties.Elements<CustomDocumentProperty>())
        {
            var name = property.Name?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var valueElement = property.ChildElements.FirstOrDefault();
            if (valueElement is null)
            {
                continue;
            }

            var value = valueElement.InnerText;
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            document.Properties.SetCustomProperty(name, value);
        }
    }

    private static void LoadCustomXmlParts(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart is null)
        {
            return;
        }

        document.CustomXmlParts.Clear();
        foreach (var part in mainPart.CustomXmlParts)
        {
            if (string.Equals(part.ContentType, DocxMacroSerializer.MacroCustomPartContentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var itemId = part.CustomXmlPropertiesPart?.DataStoreItem?.ItemId?.Value;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            try
            {
                using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
                var xml = XDocument.Load(stream);
                document.CustomXmlParts[NormalizeStoreItemId(itemId)] = xml;
            }
            catch
            {
                continue;
            }
        }
    }

    private static string NormalizeStoreItemId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static void LoadFonts(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart is null)
        {
            return;
        }

        LoadThemeFonts(mainPart.ThemePart?.Theme, document.Fonts);
        LoadThemeColors(mainPart.ThemePart?.Theme, document.ThemeColors);
        LoadFontTable(mainPart.FontTablePart, document.Fonts);
    }

    private static void LoadThemeFonts(A.Theme? theme, DocumentFonts fonts)
    {
        if (theme?.ThemeElements?.FontScheme is not A.FontScheme fontScheme)
        {
            return;
        }

        ApplyThemeFontCollection(fontScheme.MajorFont, true, fonts.Theme);
        ApplyThemeFontCollection(fontScheme.MinorFont, false, fonts.Theme);
    }

    private static void LoadThemeColors(A.Theme? theme, DocumentThemeColorMap colors)
    {
        var scheme = theme?.ThemeElements?.ColorScheme;
        if (scheme is null)
        {
            return;
        }

        foreach (var child in scheme.ChildElements)
        {
            if (child is null)
            {
                continue;
            }

            if (!TryParseThemeColor(child, out var color))
            {
                continue;
            }

            var name = child.LocalName;
            if (name.Equals("dk1", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Dark1, color);
            }
            else if (name.Equals("lt1", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Light1, color);
            }
            else if (name.Equals("dk2", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Dark2, color);
            }
            else if (name.Equals("lt2", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Light2, color);
            }
            else if (name.Equals("accent1", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent1, color);
            }
            else if (name.Equals("accent2", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent2, color);
            }
            else if (name.Equals("accent3", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent3, color);
            }
            else if (name.Equals("accent4", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent4, color);
            }
            else if (name.Equals("accent5", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent5, color);
            }
            else if (name.Equals("accent6", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Accent6, color);
            }
            else if (name.Equals("hlink", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.Hyperlink, color);
            }
            else if (name.Equals("folHlink", StringComparison.OrdinalIgnoreCase))
            {
                colors.Set(DocThemeColor.FollowedHyperlink, color);
            }
        }
    }

    private static bool TryParseThemeColor(OpenXmlElement element, out DocColor color)
    {
        color = DocColor.Black;

        var rgb = element.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) && TryParseHexColor(rgb, out color))
        {
            return true;
        }

        var systemColor = element.GetFirstChild<A.SystemColor>();
        var lastColor = systemColor?.LastColor?.Value;
        if (!string.IsNullOrWhiteSpace(lastColor) && TryParseHexColor(lastColor, out color))
        {
            return true;
        }

        return false;
    }

    private static void ApplyThemeFontCollection(OpenXmlElement? collection, bool isMajor, DocumentThemeFontMap themeFonts)
    {
        if (collection is null)
        {
            return;
        }

        var latin = collection.GetFirstChild<A.LatinFont>()?.Typeface?.Value;
        var eastAsia = collection.GetFirstChild<A.EastAsianFont>()?.Typeface?.Value;
        var complex = collection.GetFirstChild<A.ComplexScriptFont>()?.Typeface?.Value;

        if (isMajor)
        {
            themeFonts.Set(DocThemeFont.MajorAscii, latin);
            themeFonts.Set(DocThemeFont.MajorHighAnsi, latin);
            themeFonts.Set(DocThemeFont.MajorEastAsia, eastAsia);
            themeFonts.Set(DocThemeFont.MajorBidi, complex);
        }
        else
        {
            themeFonts.Set(DocThemeFont.MinorAscii, latin);
            themeFonts.Set(DocThemeFont.MinorHighAnsi, latin);
            themeFonts.Set(DocThemeFont.MinorEastAsia, eastAsia);
            themeFonts.Set(DocThemeFont.MinorBidi, complex);
        }
    }

    private static void LoadFontTable(FontTablePart? fontTablePart, DocumentFonts fonts)
    {
        if (fontTablePart?.Fonts is null)
        {
            return;
        }

        foreach (var font in fontTablePart.Fonts.Elements<Font>())
        {
            var name = font.Name?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var definition = new DocumentFontDefinition(name);
            definition.AltName = font.GetFirstChild<AltName>()?.Val?.Value;
            definition.Charset = GetFontMetadataValue(font, "charset");
            definition.Family = GetFontMetadataValue(font, "family");
            definition.Pitch = GetFontMetadataValue(font, "pitch");
            definition.Panose1 = GetFontMetadataValue(font, "panose1");

            definition.Regular = ReadEmbeddedFont(fontTablePart, font, "embedRegular");
            definition.Bold = ReadEmbeddedFont(fontTablePart, font, "embedBold");
            definition.Italic = ReadEmbeddedFont(fontTablePart, font, "embedItalic");
            definition.BoldItalic = ReadEmbeddedFont(fontTablePart, font, "embedBoldItalic");

            fonts.FontTable[name] = definition;
        }
    }

    private static string? GetFontMetadataValue(Font font, string localName)
    {
        var element = font.Elements().FirstOrDefault(child => string.Equals(child.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        if (element is null)
        {
            return null;
        }

        return GetAttributeValue(element, "val", element.NamespaceUri) ?? GetAttributeValue(element, "val");
    }

    private static EmbeddedFontData? ReadEmbeddedFont(FontTablePart fontTablePart, Font font, string localName)
    {
        var element = font.Elements().FirstOrDefault(child => string.Equals(child.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        if (element is null)
        {
            return null;
        }

        var relId = GetAttributeValue(element, "id", RelationshipNamespace);
        if (string.IsNullOrWhiteSpace(relId))
        {
            return null;
        }

        if (fontTablePart.GetPartById(relId) is not OpenXmlPart fontPart)
        {
            return null;
        }

        using var stream = fontPart.GetStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.ToArray();

        var fontKey = GetAttributeValue(element, "fontKey", element.NamespaceUri);
        if (!string.IsNullOrWhiteSpace(fontKey))
        {
            data = DeobfuscateFontData(data, fontKey);
        }

        return new EmbeddedFontData(data, fontPart.ContentType, fontKey);
    }

    private static byte[] DeobfuscateFontData(byte[] data, string fontKey)
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

    private static bool? ReadOnOff(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is OnOffType onOff)
        {
            return onOff.Val?.Value ?? true;
        }

        return true;
    }

    private static void LoadNotesAndComments(
        MainDocumentPart? mainPart,
        VibeDocument document,
        ListResolver listResolver,
        StyleResolver styleResolver,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        if (mainPart is null)
        {
            return;
        }

        document.Footnotes.Clear();
        document.Endnotes.Clear();
        document.Comments.Clear();
        document.FootnoteSeparators.SeparatorBlocks.Clear();
        document.FootnoteSeparators.ContinuationSeparatorBlocks.Clear();
        document.EndnoteSeparators.SeparatorBlocks.Clear();
        document.EndnoteSeparators.ContinuationSeparatorBlocks.Clear();

        if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
        {
            var imageResolver = new ImageResolver(mainPart.FootnotesPart, document.ThemeColors);
            var chartResolver = new ChartResolver(mainPart.FootnotesPart, document.ThemeColors);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.FootnotesPart);
            foreach (var footnote in footnotes.Elements<Footnote>())
            {
                var idValue = footnote.Id?.Value;
                var id = idValue.HasValue ? (int)idValue.Value : -1;
                if (id == -1 || id == 0)
                {
                    var separatorBlocks = id == -1
                        ? document.FootnoteSeparators.SeparatorBlocks
                        : document.FootnoteSeparators.ContinuationSeparatorBlocks;
                    foreach (var block in ParseHeaderFooter(footnote, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                    {
                        separatorBlocks.Add(block);
                    }

                    continue;
                }

                if (id < 0)
                {
                    continue;
                }

                var definition = new FootnoteDefinition(id);
                foreach (var block in ParseHeaderFooter(footnote, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Footnotes[id] = definition;
            }
        }

        if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
        {
            var imageResolver = new ImageResolver(mainPart.EndnotesPart, document.ThemeColors);
            var chartResolver = new ChartResolver(mainPart.EndnotesPart, document.ThemeColors);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.EndnotesPart);
            foreach (var endnote in endnotes.Elements<Endnote>())
            {
                var idValue = endnote.Id?.Value;
                var id = idValue.HasValue ? (int)idValue.Value : -1;
                if (id == -1 || id == 0)
                {
                    var separatorBlocks = id == -1
                        ? document.EndnoteSeparators.SeparatorBlocks
                        : document.EndnoteSeparators.ContinuationSeparatorBlocks;
                    foreach (var block in ParseHeaderFooter(endnote, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                    {
                        separatorBlocks.Add(block);
                    }

                    continue;
                }

                if (id < 0)
                {
                    continue;
                }

                var definition = new EndnoteDefinition(id);
                foreach (var block in ParseHeaderFooter(endnote, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Endnotes[id] = definition;
            }
        }

        if (mainPart.WordprocessingCommentsPart?.Comments is { } comments)
        {
            var imageResolver = new ImageResolver(mainPart.WordprocessingCommentsPart, document.ThemeColors);
            var chartResolver = new ChartResolver(mainPart.WordprocessingCommentsPart, document.ThemeColors);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.WordprocessingCommentsPart);
            var commentIdsByParaId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var comment in comments.Elements<Comment>())
            {
                var idText = comment.Id?.Value;
                var id = 0;
                if (idText is not null && !int.TryParse(idText, out id))
                {
                    id = 0;
                }

                var definition = new CommentDefinition(id)
                {
                    Author = comment.Author?.Value,
                    Initials = comment.Initials?.Value,
                    Date = comment.Date?.Value
                };

                foreach (var block in ParseHeaderFooter(comment, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, document.Revisions, placeholderResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Comments[id] = definition;

                var paraId = ResolveCommentParaId(comment);
                if (!string.IsNullOrWhiteSpace(paraId))
                {
                    commentIdsByParaId[paraId] = id;
                }
            }

            ApplyCommentThreading(mainPart, document, commentIdsByParaId);
        }
    }

    private static string? ResolveCommentParaId(Comment comment)
    {
        foreach (var paragraph in comment.Descendants<Paragraph>())
        {
            var paraId = paragraph.ParagraphId?.Value;
            if (!string.IsNullOrWhiteSpace(paraId))
            {
                return paraId;
            }
        }

        return null;
    }

    private static void ApplyCommentThreading(
        MainDocumentPart mainPart,
        VibeDocument document,
        IReadOnlyDictionary<string, int> commentIdsByParaId)
    {
        if (commentIdsByParaId.Count == 0)
        {
            return;
        }

        var commentsExPart = mainPart.WordprocessingCommentsExPart;
        if (commentsExPart?.CommentsEx is not { } commentsEx)
        {
            return;
        }

        var parentMap = new Dictionary<int, int>();
        var resolvedIds = new HashSet<int>();

        foreach (var commentEx in commentsEx.Elements<W15.CommentEx>())
        {
            var paraId = commentEx.ParaId?.Value;
            if (string.IsNullOrWhiteSpace(paraId))
            {
                continue;
            }

            if (!commentIdsByParaId.TryGetValue(paraId, out var commentId))
            {
                continue;
            }

            var parentParaId = commentEx.ParaIdParent?.Value;
            if (!string.IsNullOrWhiteSpace(parentParaId)
                && commentIdsByParaId.TryGetValue(parentParaId, out var parentId))
            {
                parentMap[commentId] = parentId;
            }

            if (commentEx.Done?.Value == true)
            {
                resolvedIds.Add(commentId);
            }
        }

        if (parentMap.Count == 0 && resolvedIds.Count == 0)
        {
            return;
        }

        foreach (var pair in parentMap)
        {
            if (document.Comments.TryGetValue(pair.Key, out var comment))
            {
                comment.ParentId = pair.Value;
            }
        }

        foreach (var pair in document.Comments)
        {
            pair.Value.ThreadId = CommentThreading.ResolveThreadId(pair.Value, document.Comments);
        }

        foreach (var commentId in resolvedIds)
        {
            if (!document.Comments.TryGetValue(commentId, out var comment))
            {
                continue;
            }

            var root = CommentThreading.ResolveRootComment(comment, document.Comments);
            root.IsResolved = true;
        }
    }

    private static List<Block> ParseHeaderFooter(
        OpenXmlCompositeElement root,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var blocks = new List<Block>();
        foreach (var element in root.Elements())
        {
            AppendBlockElement(element, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
        }

        return blocks;
    }

    private static List<Block> ParseSdtBlock(
        SdtBlock sdtBlock,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var blocks = new List<Block>();
        var content = sdtBlock.SdtContentBlock;
        var kind = ContentControlKind.Block;
        if (content is not null)
        {
            var elements = content.Elements().ToList();
            if (elements.Count == 1 && elements[0] is Table)
            {
                kind = ContentControlKind.Table;
            }
        }

        var properties = ParseContentControlProperties(sdtBlock.SdtProperties, kind, placeholderResolver);
        blocks.Add(new ContentControlStartBlock(properties));

        if (content is not null)
        {
            foreach (var element in content.Elements())
            {
                AppendBlockElement(element, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            }
        }

        blocks.Add(new ContentControlEndBlock(properties.Id));
        return blocks;
    }

    private static ContentControlProperties ParseContentControlProperties(
        SdtProperties? properties,
        ContentControlKind kind,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var result = new ContentControlProperties();
        result.Kind = kind;
        if (properties is null)
        {
            return result;
        }

        result.Tag = properties.GetFirstChild<Tag>()?.Val?.Value;
        result.Alias = properties.GetFirstChild<SdtAlias>()?.Val?.Value;
        if (properties.GetFirstChild<SdtId>()?.Val?.Value is int id)
        {
            result.Id = id;
        }

        var lockValue = properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Lock>()?.Val?.Value;
        if (lockValue is not null)
        {
            result.Lock = ((IEnumValue)lockValue.Value).Value;
        }

        var placeholder = properties.GetFirstChild<SdtPlaceholder>()?.DocPartReference?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            result.Placeholder = placeholder;
            result.PlaceholderText = placeholderResolver?.TryResolve(placeholder);
        }

        var showingPlaceholder = properties.GetFirstChild<ShowingPlaceholder>();
        if (showingPlaceholder is not null)
        {
            result.ShowingPlaceholder = showingPlaceholder.Val?.Value != false;
        }

        var dataBinding = properties.GetFirstChild<DataBinding>();
        if (dataBinding is not null)
        {
            result.DataBinding = new ContentControlDataBinding
            {
                XPath = dataBinding.XPath?.Value,
                StoreItemId = dataBinding.StoreItemId?.Value,
                PrefixMappings = dataBinding.PrefixMappings?.Value
            };
        }

        ParseStructuredContentControlProperties(properties, result);

        return result;
    }

    private static void ParseStructuredContentControlProperties(SdtProperties properties, ContentControlProperties result)
    {
        foreach (var child in properties.ChildElements)
        {
            if (child is null)
            {
                continue;
            }

            var localName = child.LocalName;
            if (localName.Equals("checkBox", StringComparison.OrdinalIgnoreCase)
                || localName.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
            {
                ParseCheckBoxContentControl(child, result);
            }
            else if (localName.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                ParseDateContentControl(child, result);
            }
            else if (localName.Equals("dropDownList", StringComparison.OrdinalIgnoreCase))
            {
                ParseListContentControl(child, result, ContentControlDataType.DropDownList);
            }
            else if (localName.Equals("comboBox", StringComparison.OrdinalIgnoreCase))
            {
                ParseListContentControl(child, result, ContentControlDataType.ComboBox);
            }
        }
    }

    private static void ParseCheckBoxContentControl(OpenXmlElement element, ContentControlProperties result)
    {
        result.DataType = ContentControlDataType.CheckBox;
        var checkedElement = element.ChildElements.FirstOrDefault(child => child.LocalName.Equals("checked", StringComparison.OrdinalIgnoreCase));
        var isChecked = ReadOnOffAttribute(checkedElement);
        if (isChecked.HasValue)
        {
            result.IsChecked = isChecked;
        }
    }

    private static void ParseDateContentControl(OpenXmlElement element, ContentControlProperties result)
    {
        result.DataType = ContentControlDataType.Date;
        result.FullDate = GetAttributeValue(element, "fullDate");
        var formatElement = element.ChildElements.FirstOrDefault(child => child.LocalName.Equals("dateFormat", StringComparison.OrdinalIgnoreCase));
        result.DateFormat = formatElement is not null ? GetAttributeValue(formatElement, "val") : null;
    }

    private static void ParseListContentControl(OpenXmlElement element, ContentControlProperties result, ContentControlDataType type)
    {
        result.DataType = type;
        var lastValue = GetAttributeValue(element, "lastValue");
        if (!string.IsNullOrWhiteSpace(lastValue))
        {
            result.SelectedValue = lastValue;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName.Equals("listItem", StringComparison.OrdinalIgnoreCase))
            {
                var value = GetAttributeValue(child, "value");
                var display = GetAttributeValue(child, "displayText") ?? value;
                result.Items.Add(new ContentControlListItem
                {
                    Value = value,
                    DisplayText = display
                });
            }
            else if (child.LocalName.Equals("lastValue", StringComparison.OrdinalIgnoreCase))
            {
                result.SelectedValue = GetAttributeValue(child, "val") ?? GetAttributeValue(child, "value");
            }
        }
    }

    private static bool? ReadOnOffAttribute(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName.Equals("val", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(attribute.Value, out var parsed))
                {
                    return parsed;
                }

                if (int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                {
                    return numeric != 0;
                }
            }
        }

        return null;
    }

    private static bool IsMetadataWrapper(OpenXmlElement element)
    {
        if (element is CustomXmlElement)
        {
            return true;
        }

        if (!string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(element.LocalName, "customXml", StringComparison.OrdinalIgnoreCase)
               || string.Equals(element.LocalName, "smartTag", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetadataPropertyElement(OpenXmlElement element)
    {
        if (element is CustomXmlProperties)
        {
            return true;
        }

        if (!string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(element.LocalName, "customXmlPr", StringComparison.OrdinalIgnoreCase)
               || string.Equals(element.LocalName, "smartTagPr", StringComparison.OrdinalIgnoreCase);
    }

    private static MetadataContainer BuildMetadataContainer(OpenXmlElement element)
    {
        var metadataElement = new MetadataElement(element.Prefix ?? string.Empty, element.LocalName, element.NamespaceUri ?? string.Empty);
        foreach (var attribute in element.GetAttributes())
        {
            metadataElement.Attributes.Add(new MetadataAttribute(
                attribute.Prefix ?? string.Empty,
                attribute.LocalName,
                attribute.NamespaceUri ?? string.Empty,
                attribute.Value ?? string.Empty));
        }

        var propertyElements = element.ChildElements
            .Where(IsMetadataPropertyElement)
            .Select(CreateMetadataElement)
            .ToList();

        return new MetadataContainer(metadataElement, propertyElements);
    }

    private static MetadataElement CreateMetadataElement(OpenXmlElement element)
    {
        var metadataElement = new MetadataElement(element.Prefix ?? string.Empty, element.LocalName, element.NamespaceUri ?? string.Empty);
        foreach (var attribute in element.GetAttributes())
        {
            metadataElement.Attributes.Add(new MetadataAttribute(
                attribute.Prefix ?? string.Empty,
                attribute.LocalName,
                attribute.NamespaceUri ?? string.Empty,
                attribute.Value ?? string.Empty));
        }

        if (element.ChildElements.Count == 0)
        {
            var text = element.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                metadataElement.Text = text;
            }

            return metadataElement;
        }

        foreach (var child in element.ChildElements)
        {
            metadataElement.Children.Add(CreateMetadataElement(child));
        }

        return metadataElement;
    }

    private static IEnumerable<OpenXmlElement> EnumerateMetadataContent(OpenXmlElement element)
    {
        foreach (var child in element.ChildElements)
        {
            if (IsMetadataPropertyElement(child))
            {
                continue;
            }

            yield return child;
        }
    }

    private sealed class ContentControlPlaceholderResolver
    {
        private readonly Dictionary<string, string> _placeholders = new(StringComparer.OrdinalIgnoreCase);

        public ContentControlPlaceholderResolver(GlossaryDocumentPart? glossaryPart)
        {
            var docParts = glossaryPart?.GlossaryDocument?.DocParts;
            if (docParts is null)
            {
                return;
            }

            foreach (var docPart in docParts.Elements<DocPart>())
            {
                var name = docPart.DocPartProperties?.DocPartName?.Val?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var text = ExtractDocPartText(docPart.DocPartBody);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                _placeholders[name] = text;
            }
        }

        public string? TryResolve(string? docPartName)
        {
            if (string.IsNullOrWhiteSpace(docPartName))
            {
                return null;
            }

            return _placeholders.TryGetValue(docPartName, out var text) ? text : null;
        }
    }

    private static string? ExtractDocPartText(DocPartBody? body)
    {
        if (body is null)
        {
            return null;
        }

        var paragraphs = body.Elements<Paragraph>()
            .Select(static paragraph => paragraph.InnerText?.Trim())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (paragraphs.Length > 0)
        {
            return string.Join("\n", paragraphs);
        }

        var fallback = body.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static AltChunkBlock CreateAltChunkBlock(AltChunk altChunk, OpenXmlPart? part)
    {
        var block = new AltChunkBlock();
        var relId = altChunk.Id?.Value ?? GetAttributeValue(altChunk, "id", RelationshipNamespace);
        block.RelationshipId = relId;
        if (part is null || string.IsNullOrWhiteSpace(relId))
        {
            return block;
        }

        if (part.GetPartById(relId) is AlternativeFormatImportPart altPart)
        {
            block.ContentType = altPart.ContentType;
            block.TargetUri = altPart.Uri?.ToString();
            block.Data = ReadPartData(altPart);
            return block;
        }

        var external = part.ExternalRelationships.FirstOrDefault(item => item.Id == relId);
        if (external is not null)
        {
            block.TargetUri = external.Uri?.ToString();
        }

        return block;
    }

    private static void AppendAltChunkBlocks(List<Block> blocks, AltChunk altChunk, OpenXmlPart? part)
    {
        var altChunkBlock = CreateAltChunkBlock(altChunk, part);
        if (AltChunkConverter.TryConvert(altChunkBlock, out var convertedBlocks))
        {
            blocks.AddRange(convertedBlocks);
            return;
        }

        blocks.Add(altChunkBlock);
    }

    private static void AppendBlockElement(
        OpenXmlElement element,
        List<Block> blocks,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        if (IsMetadataWrapper(element))
        {
            AppendMetadataBlocks(element, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            return;
        }

        if (TryGetRevisionKind(element, out var revisionKind))
        {
            AppendRevisionBlocks(element, revisionKind, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            return;
        }

        switch (element)
        {
            case Paragraph paragraph:
                blocks.AddRange(ParseParagraphBlocks(paragraph, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver, false));
                break;
            case AltChunk altChunk:
                AppendAltChunkBlocks(blocks, altChunk, imageResolver.Part);
                break;
            case Table table:
                blocks.Add(ParseTable(table, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver));
                break;
            case SdtBlock sdtBlock:
                blocks.AddRange(ParseSdtBlock(sdtBlock, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver));
                break;
        }
    }

    private static void AppendRevisionBlocks(
        OpenXmlElement element,
        RevisionKind kind,
        List<Block> blocks,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var revision = ResolveRevision(element, kind, revisions);
        blocks.Add(new RevisionStartBlock(revision));
        foreach (var child in element.ChildElements)
        {
            AppendBlockElement(child, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
        }
        blocks.Add(new RevisionEndBlock(kind, revision.Id));
    }

    private static void AppendMetadataBlocks(
        OpenXmlElement element,
        List<Block> blocks,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var metadata = BuildMetadataContainer(element);
        blocks.Add(new MetadataStartBlock(metadata));
        foreach (var child in EnumerateMetadataContent(element))
        {
            AppendBlockElement(child, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
        }
        blocks.Add(new MetadataEndBlock(metadata));
    }

    private static bool TryGetRevisionKind(OpenXmlElement element, out RevisionKind kind)
    {
        switch (element)
        {
            case Inserted:
                kind = RevisionKind.Insert;
                return true;
            case Deleted:
                kind = RevisionKind.Delete;
                return true;
            case MoveFrom:
                kind = RevisionKind.MoveFrom;
                return true;
            case MoveTo:
                kind = RevisionKind.MoveTo;
                return true;
        }

        if (!string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase))
        {
            kind = default;
            return false;
        }

        var localName = element.LocalName;
        if (string.Equals(localName, "ins", StringComparison.OrdinalIgnoreCase))
        {
            kind = RevisionKind.Insert;
            return true;
        }

        if (string.Equals(localName, "del", StringComparison.OrdinalIgnoreCase))
        {
            kind = RevisionKind.Delete;
            return true;
        }

        if (string.Equals(localName, "moveFrom", StringComparison.OrdinalIgnoreCase))
        {
            kind = RevisionKind.MoveFrom;
            return true;
        }

        if (string.Equals(localName, "moveTo", StringComparison.OrdinalIgnoreCase))
        {
            kind = RevisionKind.MoveTo;
            return true;
        }

        kind = default;
        return false;
    }

    private static List<Block> ParseParagraphBlocks(
        Paragraph paragraph,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver,
        bool splitOnPageBreaks)
    {
        var blocks = new List<Block>();
        var paragraphStyleId = styleResolver.GetParagraphStyleId(paragraph);
        var listInfo = listResolver.Resolve(paragraph);
        var current = CreateStyledParagraphBlock(paragraph, listInfo, paragraphStyleId, styleResolver, false);
        var builder = new StringBuilder();
        var fieldStack = new Stack<FieldParseState>();

        void FlushCurrent()
        {
            if (builder.Length == 0 && current.Inlines.Count == 0)
            {
                return;
            }

            current.Text = builder.ToString();
            blocks.Add(current);
            builder.Clear();
        }

        void SplitForBreak(Block breakBlock)
        {
            FlushCurrent();
            if (!splitOnPageBreaks)
            {
                return;
            }

            blocks.Add(breakBlock);
            current = CreateStyledParagraphBlock(paragraph, listInfo, paragraphStyleId, styleResolver, true);
        }

        void AddInline(Inline inline, bool appendReplacement, HyperlinkInfo? hyperlink)
        {
            if (hyperlink is not null)
            {
                inline.Hyperlink = hyperlink;
            }

            current.Inlines.Add(inline);
            if (appendReplacement)
            {
                builder.Append(DocumentConstants.ObjectReplacementChar);
            }
        }

        void AddFloatingObject(Inline inline, DW.Anchor anchorElement, HyperlinkInfo? hyperlink, int anchorOffset)
        {
            if (hyperlink is not null)
            {
                inline.Hyperlink = hyperlink;
            }

            var floating = new FloatingObject(inline);
            ApplyAnchorProperties(anchorElement, floating.Anchor);
            floating.Anchor.AnchorOffset = anchorOffset;
            current.FloatingObjects.Add(floating);
        }

        Inline? CreateEvaluatedFieldInline(FieldKind kind, TextStyle? style)
        {
            return kind switch
            {
                FieldKind.Page => new PageNumberInline(style),
                FieldKind.NumPages => new TotalPagesInline(style),
                _ => null
            };
        }

        static bool ReadOnOff(OnOffValue? value)
        {
            return value?.Value ?? false;
        }

        void FinalizeFieldInstruction(FieldParseState state)
        {
            if (state.InstructionFinalized)
            {
                return;
            }

            var instruction = state.Instruction.ToString();
            state.StartInline.Instruction = instruction;
            var definition = FieldInstructionParser.Parse(instruction);
            state.Definition = definition;
            state.StartInline.Definition = definition;
            state.EvaluationMode = FieldEvaluationPolicy.GetMode(definition);
            state.Kind = definition?.Kind ?? FieldKind.Unknown;
            state.InstructionFinalized = true;
        }

        void BeginField(bool isLocked, bool isDirty)
        {
            var startInline = new FieldStartInline(string.Empty);
            startInline.IsLocked = isLocked;
            startInline.IsDirty = isDirty;
            current.Inlines.Add(startInline);
            fieldStack.Push(new FieldParseState(startInline));
        }

        void SeparateField()
        {
            if (fieldStack.Count == 0)
            {
                return;
            }

            var state = fieldStack.Peek();
            FinalizeFieldInstruction(state);
            current.Inlines.Add(new FieldSeparatorInline());
            state.InResult = true;
        }

        void EndField()
        {
            if (fieldStack.Count == 0)
            {
                return;
            }

            var state = fieldStack.Pop();
            FinalizeFieldInstruction(state);
            if (state.EvaluationMode == FieldEvaluationMode.Evaluate && !state.AddedEvaluatedInline)
            {
                var fallbackStyle = styleResolver.ResolveRunStyle(paragraphStyleId, null, null);
                var inline = CreateEvaluatedFieldInline(state.Kind, fallbackStyle);
                if (inline is not null)
                {
                    AddInline(inline, true, null);
                }
            }

            current.Inlines.Add(new FieldEndInline());
        }

        void ProcessRevisionRun(OpenXmlCompositeElement element, RevisionKind kind, HyperlinkInfo? hyperlink)
        {
            var revision = ResolveRevision(element, kind, revisions);
            AddInline(new RevisionStartInline(revision), false, null);
            foreach (var child in element.Elements())
            {
                ProcessElement(child, hyperlink);
            }
            AddInline(new RevisionEndInline(kind, revision.Id), false, null);
        }

        void ProcessMetadataInline(OpenXmlElement element, HyperlinkInfo? hyperlink)
        {
            var metadata = BuildMetadataContainer(element);
            AddInline(new MetadataStartInline(metadata), false, null);
            foreach (var child in EnumerateMetadataContent(element))
            {
                ProcessElement(child, hyperlink);
            }
            AddInline(new MetadataEndInline(metadata), false, null);
        }

        void AddRevisionRangeStart(OpenXmlElement element, RevisionKind kind)
        {
            var revision = ResolveRevision(element, kind, revisions);
            AddInline(new RevisionRangeStartInline(revision), false, null);
        }

        void AddRevisionRangeEnd(OpenXmlElement element, RevisionKind kind)
        {
            var revision = ResolveRevision(element, kind, revisions);
            AddInline(new RevisionRangeEndInline(kind, revision.Id), false, null);
        }

        bool IsStandaloneBreak(Run run, OpenXmlElement breakNode)
        {
            foreach (var child in run.ChildElements)
            {
                if (child == breakNode || child is RunProperties || child is LastRenderedPageBreak)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        void ProcessRun(Run run, HyperlinkInfo? hyperlink)
        {
            var fieldState = fieldStack.Count > 0 ? fieldStack.Peek() : null;
            var suppressResultText = fieldState is not null
                                     && fieldState.InResult
                                     && fieldState.EvaluationMode == FieldEvaluationMode.Evaluate;
            if (suppressResultText && !fieldState!.AddedEvaluatedInline)
            {
                var fieldStyle = styleResolver.ResolveRunStyle(paragraphStyleId, run.RunProperties, styleResolver.GetRunStyleId(run));
                var inline = CreateEvaluatedFieldInline(fieldState.Kind, fieldStyle);
                if (inline is not null)
                {
                    AddInline(inline, true, hyperlink);
                    fieldState.AddedEvaluatedInline = true;
                }
            }

            var runStyleId = styleResolver.GetRunStyleId(run);
            var style = ExtractRunStyleProperties(run.RunProperties);
            var buffer = new StringBuilder();
            foreach (var node in run.Elements())
            {
                switch (node)
                {
                    case FieldChar fieldChar:
                    {
                        FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                        var type = fieldChar.FieldCharType?.Value;
                        if (type == FieldCharValues.Begin)
                        {
                            BeginField(ReadOnOff(fieldChar.FieldLock), ReadOnOff(fieldChar.Dirty));
                        }
                        else if (type == FieldCharValues.Separate)
                        {
                            SeparateField();
                        }
                        else if (type == FieldCharValues.End)
                        {
                            EndField();
                        }

                        break;
                    }
                    case FieldCode fieldCode:
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            fieldStack.Peek().Instruction.Append(fieldCode.Text);
                        }

                        break;
                    case DeletedFieldCode deletedFieldCode:
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            fieldStack.Peek().Instruction.Append(deletedFieldCode.Text);
                        }
                        else if (!suppressResultText)
                        {
                            buffer.Append(deletedFieldCode.Text);
                        }
                        break;
                    case Text text:
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            fieldStack.Peek().Instruction.Append(text.Text);
                        }
                        else if (!suppressResultText)
                        {
                            buffer.Append(text.Text);
                        }
                        break;
                    case DeletedText deletedText:
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            fieldStack.Peek().Instruction.Append(deletedText.Text);
                        }
                        else if (!suppressResultText)
                        {
                            buffer.Append(deletedText.Text);
                        }
                        break;
                    case OpenXmlElement element when element.LocalName == "sym":
                    {
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            break;
                        }

                        if (suppressResultText)
                        {
                            break;
                        }

                        var fontValue = GetAttributeValue(element, "font", element.NamespaceUri) ?? string.Empty;
                        var charValue = GetAttributeValue(element, "char", element.NamespaceUri) ?? string.Empty;
                        var symbolText = ParseSymbolChar(charValue);
                        if (symbolText.Length == 0)
                        {
                            break;
                        }

                        FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                        var symbolStyle = style?.Clone() ?? new TextStyleProperties();
                        if (!string.IsNullOrWhiteSpace(fontValue))
                        {
                            symbolStyle.FontFamily = fontValue;
                        }

                        var inline = new RunInline(symbolText, symbolStyle) { StyleId = runStyleId };
                        if (hyperlink is not null)
                        {
                            inline.Hyperlink = hyperlink;
                        }

                        current.Inlines.Add(inline);
                        builder.Append(symbolText);
                        break;
                    }
                    case TabChar:
                        if (fieldStack.Count == 0 || fieldStack.Peek().InResult)
                        {
                            if (!suppressResultText)
                            {
                                buffer.Append('\t');
                            }
                        }
                        break;
                    case FootnoteReference footnoteReference:
                    {
                        if (suppressResultText)
                        {
                            break;
                        }

                        FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                        var idValue = footnoteReference.Id?.Value;
                        var id = idValue.HasValue ? (int)idValue.Value : 0;
                        var referenceInline = new FootnoteReferenceInline(id, style?.Clone()) { StyleId = runStyleId };
                        AddInline(referenceInline, false, hyperlink);
                        builder.Append(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    }
                    case EndnoteReference endnoteReference:
                    {
                        if (suppressResultText)
                        {
                            break;
                        }

                        FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                        var idValue = endnoteReference.Id?.Value;
                        var id = idValue.HasValue ? (int)idValue.Value : 0;
                        var referenceInline = new EndnoteReferenceInline(id, style?.Clone()) { StyleId = runStyleId };
                        AddInline(referenceInline, false, hyperlink);
                        builder.Append(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    }
                    case CommentReference commentReference:
                    {
                        if (suppressResultText)
                        {
                            break;
                        }

                        FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                        var idText = commentReference.Id?.Value;
                        var id = 0;
                        if (idText is not null && int.TryParse(idText, out var parsedId))
                        {
                            id = parsedId;
                        }
                        var referenceInline = new CommentReferenceInline(id, style?.Clone()) { StyleId = runStyleId };
                        AddInline(referenceInline, false, hyperlink);
                        builder.Append(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    }
                    case LastRenderedPageBreak:
                        break;
                    case Break:
                    {
                        if (fieldStack.Count > 0 && !fieldStack.Peek().InResult)
                        {
                            break;
                        }

                        var breakType = ((Break)node).Type?.Value;
                        if (splitOnPageBreaks && breakType == BreakValues.Page)
                        {
                            if (IsStandaloneBreak(run, node))
                            {
                                FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                                SplitForBreak(new PageBreakBlock());
                            }
                            else if (!suppressResultText)
                            {
                                buffer.Append('\n');
                            }
                        }
                        else if (splitOnPageBreaks && breakType == BreakValues.Column)
                        {
                            if (IsStandaloneBreak(run, node))
                            {
                                FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                                SplitForBreak(new ColumnBreakBlock());
                            }
                            else if (!suppressResultText)
                            {
                                buffer.Append('\n');
                            }
                        }
                        else
                        {
                            if (!suppressResultText)
                            {
                                buffer.Append('\n');
                            }
                        }

                        break;
                    }
                    case CarriageReturn:
                        if (fieldStack.Count == 0 || fieldStack.Peek().InResult)
                        {
                            if (!suppressResultText)
                            {
                                buffer.Append('\n');
                            }
                        }
                        break;
                    case Drawing drawing:
                        if (!suppressResultText)
                        {
                            FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                            var anchor = drawing.Descendants<DW.Anchor>().FirstOrDefault();
                            if (anchor is not null)
                            {
                                var inline = TryCreateInlineObject(drawing, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
                                if (inline is not null)
                                {
                                    AddFloatingObject(inline, anchor, hyperlink, builder.Length);
                                }
                            }
                            else
                            {
                                var inline = TryCreateInlineObject(drawing, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
                                if (inline is not null)
                                {
                                    AddInline(inline, true, hyperlink);
                                }
                            }
                        }

                        break;
                    case OpenXmlElement element when element.LocalName == "object" || element.LocalName == "pict":
                        if (!suppressResultText)
                        {
                            FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                            var inline = TryCreateInlineObjectFromVml(element, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
                            if (inline is not null)
                            {
                                var floating = new FloatingObject(inline);
                                if (TryApplyVmlAnchor(element, floating.Anchor))
                                {
                                    if (hyperlink is not null)
                                    {
                                        inline.Hyperlink = hyperlink;
                                    }

                                    floating.Anchor.AnchorOffset = builder.Length;
                                    current.FloatingObjects.Add(floating);
                                }
                                else
                                {
                                    AddInline(inline, true, hyperlink);
                                }
                            }
                        }

                        break;
                }
            }

            FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
        }

        void ProcessSimpleField(SimpleField field, HyperlinkInfo? hyperlink)
        {
            var instruction = field.Instruction?.Value ?? string.Empty;
            var startInline = new FieldStartInline(instruction)
            {
                IsLocked = ReadOnOff(field.FieldLock),
                IsDirty = ReadOnOff(field.Dirty)
            };
            var definition = FieldInstructionParser.Parse(instruction);
            startInline.Definition = definition;
            AddInline(startInline, false, hyperlink);
            current.Inlines.Add(new FieldSeparatorInline());

            if (FieldEvaluationPolicy.GetMode(definition) == FieldEvaluationMode.Evaluate)
            {
                var run = field.Elements<Run>().FirstOrDefault();
                var fieldStyle = styleResolver.ResolveRunStyle(paragraphStyleId, run?.RunProperties, styleResolver.GetRunStyleId(run));
                var inline = CreateEvaluatedFieldInline(definition?.Kind ?? FieldKind.Unknown, fieldStyle);
                if (inline is not null)
                {
                    AddInline(inline, true, hyperlink);
                }
            }
            else
            {
                foreach (var nestedRun in field.Elements<Run>())
                {
                    ProcessRun(nestedRun, hyperlink);
                }
            }

            current.Inlines.Add(new FieldEndInline());
        }

        void ProcessRuby(Ruby ruby, HyperlinkInfo? hyperlink)
        {
            if (ruby is null)
            {
                return;
            }

            var baseText = ExtractRubyText(ruby.RubyBase, out var baseStyle, out var baseStyleId);
            var rubyText = ExtractRubyText(ruby.RubyContent, out var rubyStyle, out var rubyStyleId);
            if (string.IsNullOrEmpty(baseText) && string.IsNullOrEmpty(rubyText))
            {
                return;
            }

            var rubyScale = ResolveRubyScale(ruby.RubyProperties, baseStyle, rubyStyle);
            var inline = new RubyInline(baseText, rubyText)
            {
                BaseStyle = baseStyle,
                BaseStyleId = baseStyleId,
                RubyStyle = rubyStyle,
                RubyStyleId = rubyStyleId,
                RubyScale = rubyScale
            };

            AddInline(inline, false, hyperlink);
            builder.Append(baseText);
        }

        string ExtractRubyText(OpenXmlCompositeElement? container, out TextStyleProperties? style, out string? styleId)
        {
            style = null;
            styleId = null;
            if (container is null)
            {
                return string.Empty;
            }

            var textBuilder = new StringBuilder();
            foreach (var run in container.Elements<Run>())
            {
                if (style is null)
                {
                    style = ExtractRunStyleProperties(run.RunProperties);
                    styleId = styleResolver.GetRunStyleId(run);
                }

                AppendRubyRunText(run, textBuilder);
            }

            if (textBuilder.Length > 0)
            {
                return textBuilder.ToString();
            }

            var fallback = container.InnerText;
            return string.IsNullOrEmpty(fallback) ? string.Empty : fallback;
        }

        void AppendRubyRunText(Run run, StringBuilder buffer)
        {
            foreach (var node in run.Elements())
            {
                switch (node)
                {
                    case Text text:
                        buffer.Append(text.Text);
                        break;
                    case DeletedText deletedText:
                        buffer.Append(deletedText.Text);
                        break;
                    case TabChar:
                        buffer.Append('\t');
                        break;
                    case Break:
                    case CarriageReturn:
                        buffer.Append('\n');
                        break;
                }
            }
        }

        float ResolveRubyScale(RubyProperties? properties, TextStyleProperties? baseStyle, TextStyleProperties? rubyStyle)
        {
            if (properties is null)
            {
                return ResolveFallbackRubyScale(baseStyle, rubyStyle);
            }

            var baseSize = ParseHalfPoints(properties.PhoneticGuideBaseTextSize?.Val);
            var rubySize = ParseHalfPoints(properties.PhoneticGuideTextFontSize?.Val);
            if (!baseSize.HasValue || baseSize.Value <= 0f || !rubySize.HasValue || rubySize.Value <= 0f)
            {
                return ResolveFallbackRubyScale(baseStyle, rubyStyle);
            }

            return rubySize.Value / baseSize.Value;
        }

        float ResolveFallbackRubyScale(TextStyleProperties? baseStyle, TextStyleProperties? rubyStyle)
        {
            if (baseStyle?.FontSize is float baseSize && baseSize > 0f
                && rubyStyle?.FontSize is float rubySize && rubySize > 0f)
            {
                return rubySize / baseSize;
            }

            return 0.5f;
        }

        float? ParseHalfPoints(StringValue? value)
        {
            var raw = value?.Value;
            if (string.IsNullOrWhiteSpace(raw) || !float.TryParse(raw, out var halfPoints))
            {
                return null;
            }

            return HalfPointsToDip(halfPoints);
        }

        void ProcessElement(OpenXmlElement element, HyperlinkInfo? hyperlink)
        {
            switch (element)
            {
                case Run run:
                    ProcessRun(run, hyperlink);
                    break;
                case Ruby ruby:
                    ProcessRuby(ruby, hyperlink);
                    break;
                case SimpleField field:
                    ProcessSimpleField(field, hyperlink);
                    break;
                case OpenXmlElement metadataElement when IsMetadataWrapper(metadataElement):
                    ProcessMetadataInline(metadataElement, hyperlink);
                    break;
                case InsertedRun insertedRun:
                    ProcessRevisionRun(insertedRun, RevisionKind.Insert, hyperlink);
                    break;
                case DeletedRun deletedRun:
                    ProcessRevisionRun(deletedRun, RevisionKind.Delete, hyperlink);
                    break;
                case MoveFromRun moveFromRun:
                    ProcessRevisionRun(moveFromRun, RevisionKind.MoveFrom, hyperlink);
                    break;
                case MoveToRun moveToRun:
                    ProcessRevisionRun(moveToRun, RevisionKind.MoveTo, hyperlink);
                    break;
                case Hyperlink link:
                {
                    var linkInfo = hyperlinkResolver.Resolve(link);
                    foreach (var child in link.Elements())
                    {
                        ProcessElement(child, linkInfo);
                    }

                    break;
                }
                case SdtRun sdtRun:
                {
                    var properties = ParseContentControlProperties(sdtRun.SdtProperties, ContentControlKind.Run, placeholderResolver);
                    AddInline(new ContentControlStartInline(properties), false, null);
                    foreach (var child in sdtRun.SdtContentRun?.Elements() ?? Enumerable.Empty<OpenXmlElement>())
                    {
                        ProcessElement(child, hyperlink);
                    }

                    AddInline(new ContentControlEndInline(properties.Id), false, null);
                    break;
                }
                case OpenXmlElement mathElement when IsMathContainer(mathElement):
                {
                    var mathRoot = ParseMathContainer(mathElement);
                    if (mathRoot is not null)
                    {
                        AddInline(new EquationInline(mathRoot), true, hyperlink);
                    }

                    break;
                }
                case BookmarkStart bookmarkStart:
                {
                    var id = 0;
                    var idValue = bookmarkStart.Id?.Value;
                    if (idValue is not null && int.TryParse(idValue.ToString(), out var parsedId))
                    {
                        id = parsedId;
                    }

                    var name = bookmarkStart.Name?.Value ?? string.Empty;
                    AddInline(new BookmarkStartInline(id, name), false, hyperlink);
                    break;
                }
                case BookmarkEnd bookmarkEnd:
                {
                    var id = 0;
                    var idValue = bookmarkEnd.Id?.Value;
                    if (idValue is not null && int.TryParse(idValue.ToString(), out var parsedId))
                    {
                        id = parsedId;
                    }

                    AddInline(new BookmarkEndInline(id), false, hyperlink);
                    break;
                }
                case MoveFromRangeStart moveFromRangeStart:
                    AddRevisionRangeStart(moveFromRangeStart, RevisionKind.MoveFrom);
                    break;
                case MoveFromRangeEnd moveFromRangeEnd:
                    AddRevisionRangeEnd(moveFromRangeEnd, RevisionKind.MoveFrom);
                    break;
                case MoveToRangeStart moveToRangeStart:
                    AddRevisionRangeStart(moveToRangeStart, RevisionKind.MoveTo);
                    break;
                case MoveToRangeEnd moveToRangeEnd:
                    AddRevisionRangeEnd(moveToRangeEnd, RevisionKind.MoveTo);
                    break;
                case CommentRangeStart commentRangeStart:
                {
                    var idText = commentRangeStart.Id?.Value;
                    var id = 0;
                    if (idText is not null && int.TryParse(idText, out var parsedId))
                    {
                        id = parsedId;
                    }
                    AddInline(new CommentRangeStartInline(id), false, null);
                    break;
                }
                case CommentRangeEnd commentRangeEnd:
                {
                    var idText = commentRangeEnd.Id?.Value;
                    var id = 0;
                    if (idText is not null && int.TryParse(idText, out var parsedId))
                    {
                        id = parsedId;
                    }
                    AddInline(new CommentRangeEndInline(id), false, null);
                    break;
                }
            }
        }

        foreach (var element in paragraph.Elements())
        {
            ProcessElement(element, null);
        }

        if (builder.Length > 0 || current.Inlines.Count > 0 || blocks.Count == 0)
        {
            current.Text = builder.Length > 0 || current.Inlines.Count > 0
                ? builder.ToString()
                : paragraph.InnerText ?? string.Empty;
            blocks.Add(current);
        }

        return blocks;
    }

    private static ParagraphBlock CreateStyledParagraphBlock(
        Paragraph paragraph,
        ListInfo? listInfo,
        string? paragraphStyleId,
        StyleResolver styleResolver,
        bool suppressPageBreakBefore)
    {
        var block = new ParagraphBlock(string.Empty, listInfo)
        {
            StyleId = paragraphStyleId
        };
        ApplyParagraphProperties(paragraph.ParagraphProperties, block.Properties);
        if (suppressPageBreakBefore)
        {
            block.Properties.PageBreakBefore = null;
        }

        return block;
    }

    private static Vibe.Office.Documents.SectionProperties ParseSectionProperties(DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps)
    {
        var properties = new Vibe.Office.Documents.SectionProperties();
        var pageSize = sectionProps.GetFirstChild<PageSize>();
        var pageWidthTwips = TryParseTwips(pageSize?.Width);
        if (pageWidthTwips.HasValue)
        {
            properties.PageWidth = TwipsToDip(pageWidthTwips.Value);
        }

        var pageHeightTwips = TryParseTwips(pageSize?.Height);
        if (pageHeightTwips.HasValue)
        {
            properties.PageHeight = TwipsToDip(pageHeightTwips.Value);
        }

        if (pageSize?.Orient?.Value is PageOrientationValues orientation)
        {
            properties.Orientation = orientation == PageOrientationValues.Landscape
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
        }

        var pageMargin = sectionProps.GetFirstChild<PageMargin>();
        var marginLeftTwips = TryParseTwips(pageMargin?.Left);
        if (marginLeftTwips.HasValue && marginLeftTwips.Value >= 0)
        {
            properties.MarginLeft = TwipsToDip(marginLeftTwips.Value);
        }

        var marginRightTwips = TryParseTwips(pageMargin?.Right);
        if (marginRightTwips.HasValue && marginRightTwips.Value >= 0)
        {
            properties.MarginRight = TwipsToDip(marginRightTwips.Value);
        }

        var marginTopTwips = TryParseTwips(pageMargin?.Top);
        if (marginTopTwips.HasValue && marginTopTwips.Value >= 0)
        {
            properties.MarginTop = TwipsToDip(marginTopTwips.Value);
        }

        var marginBottomTwips = TryParseTwips(pageMargin?.Bottom);
        if (marginBottomTwips.HasValue && marginBottomTwips.Value >= 0)
        {
            properties.MarginBottom = TwipsToDip(marginBottomTwips.Value);
        }

        var headerTwips = TryParseTwips(pageMargin?.Header);
        if (headerTwips.HasValue)
        {
            properties.HeaderOffset = TwipsToDip(headerTwips.Value);
        }

        var footerTwips = TryParseTwips(pageMargin?.Footer);
        if (footerTwips.HasValue)
        {
            properties.FooterOffset = TwipsToDip(footerTwips.Value);
        }

        var gutterTwips = TryParseTwips(pageMargin?.Gutter);
        if (gutterTwips.HasValue)
        {
            properties.Gutter = TwipsToDip(gutterTwips.Value);
        }

        if (sectionProps.GetFirstChild<TitlePage>() is not null)
        {
            properties.DifferentFirstPageHeaderFooter = true;
        }

        if (properties.DifferentFirstPageHeaderFooter != true)
        {
            if (sectionProps.Elements<HeaderReference>().Any(item => item.Type?.Value == HeaderFooterValues.First)
                || sectionProps.Elements<FooterReference>().Any(item => item.Type?.Value == HeaderFooterValues.First))
            {
                properties.DifferentFirstPageHeaderFooter = true;
            }
        }

        var columns = sectionProps.GetFirstChild<Columns>();
        if (columns is not null)
        {
            int? columnCount = null;
            if (columns.ColumnCount?.Value is not null)
            {
                columnCount = columns.ColumnCount.Value;
                properties.ColumnCount = columnCount.Value;
            }

            var columnSpaceTwips = TryParseTwips(columns.Space);
            if (columnSpaceTwips.HasValue)
            {
                properties.ColumnGap = TwipsToDip(columnSpaceTwips.Value);
            }

            if (columns.EqualWidth?.Value is bool equalWidth)
            {
                properties.ColumnEqualWidth = equalWidth;
            }

            if (columns.Separator?.Value is bool separator)
            {
                properties.ColumnSeparator = separator;
            }

            var columnElements = columns.Elements<Column>().ToList();
            if (columnElements.Count > 0 && !columnCount.HasValue)
            {
                columnCount = columnElements.Count;
                properties.ColumnCount = columnCount.Value;
            }

            List<float?>? columnSpaceOverrides = null;
            foreach (var column in columnElements)
            {
                var columnWidthTwips = TryParseTwips(column.Width);
                if (columnWidthTwips.HasValue)
                {
                    properties.ColumnWidths.Add(TwipsToDip(columnWidthTwips.Value));
                }

                var columnSpace = TryParseTwips(column.Space);
                if (columnSpaceOverrides is null)
                {
                    columnSpaceOverrides = new List<float?>(columnElements.Count);
                }

                columnSpaceOverrides.Add(columnSpace.HasValue ? TwipsToDip(columnSpace.Value) : null);
            }

            if (columnSpaceOverrides is not null && columnSpaceOverrides.Any(space => space.HasValue))
            {
                var resolvedCount = columnCount ?? columnSpaceOverrides.Count;
                var gapCount = Math.Max(0, resolvedCount - 1);
                for (var i = 0; i < gapCount; i++)
                {
                    var overrideValue = i < columnSpaceOverrides.Count ? columnSpaceOverrides[i] : null;
                    properties.ColumnGaps.Add(overrideValue ?? float.NaN);
                }
            }

            if (!properties.ColumnCount.HasValue)
            {
                if (properties.ColumnWidths.Count > 0)
                {
                    properties.ColumnCount = properties.ColumnWidths.Count;
                }
                else if (properties.ColumnGaps.Count > 0)
                {
                    properties.ColumnCount = properties.ColumnGaps.Count + 1;
                }
                else
                {
                    properties.ColumnCount = 1;
                }
            }
        }

        var docGrid = sectionProps.GetFirstChild<DocGrid>();
        if (docGrid is not null)
        {
            var gridSettings = new DocGridSettings();
            if (docGrid.Type?.Value is DocGridValues gridType)
            {
                gridSettings.Type = MapDocGridType(gridType);
            }

            var linePitchTwips = TryParseTwips(docGrid.LinePitch);
            if (linePitchTwips.HasValue)
            {
                gridSettings.LinePitch = TwipsToDip(linePitchTwips.Value);
            }

            var charSpaceTwips = TryParseTwips(docGrid.CharacterSpace);
            if (charSpaceTwips.HasValue)
            {
                gridSettings.CharacterSpace = TwipsToDip(charSpaceTwips.Value);
            }

            if (gridSettings.HasValues)
            {
                properties.DocGrid = gridSettings;
            }
        }

        var pageBorders = sectionProps.GetFirstChild<OpenXmlPageBorders>();
        if (pageBorders is not null)
        {
            var borders = new Vibe.Office.Documents.PageBorders
            {
                Top = ParseBorderLine(pageBorders.TopBorder),
                Bottom = ParseBorderLine(pageBorders.BottomBorder),
                Left = ParseBorderLine(pageBorders.LeftBorder),
                Right = ParseBorderLine(pageBorders.RightBorder)
            };

            if (pageBorders.Display?.Value is PageBorderDisplayValues display)
            {
                borders.Display = MapPageBorderDisplay(display);
            }

            if (pageBorders.OffsetFrom?.Value is PageBorderOffsetValues offset)
            {
                borders.OffsetFrom = MapPageBorderOffset(offset);
            }

            if (pageBorders.ZOrder?.Value is PageBorderZOrderValues zOrder)
            {
                borders.ZOrder = MapPageBorderZOrder(zOrder);
            }

            if (borders.HasAny)
            {
                properties.PageBorders = borders;
            }
        }

        var lineNumbering = sectionProps.GetFirstChild<LineNumberType>();
        if (lineNumbering is not null)
        {
            var settings = new LineNumberingSettings();
            if (lineNumbering.Start?.Value is short start)
            {
                settings.Start = start;
            }

            if (lineNumbering.CountBy?.Value is short countBy)
            {
                settings.CountBy = countBy;
            }

            var distance = TryParseTwips(lineNumbering.Distance);
            if (distance.HasValue)
            {
                settings.Distance = TwipsToDip(distance.Value);
            }

            if (lineNumbering.Restart?.Value is LineNumberRestartValues restart)
            {
                settings.Restart = MapLineNumberRestart(restart);
            }

            properties.LineNumbering = settings;
        }

        var pageNumbering = sectionProps.GetFirstChild<PageNumberType>();
        if (pageNumbering is not null)
        {
            var settings = new PageNumberingSettings();
            if (pageNumbering.Start?.Value is int start)
            {
                settings.Start = start;
            }

            if (pageNumbering.Format?.Value is NumberFormatValues format)
            {
                settings.Format = MapPageNumberFormat(format);
            }

            properties.PageNumbering = settings;
        }

        var footnoteProps = sectionProps.GetFirstChild<FootnoteProperties>();
        if (footnoteProps is not null)
        {
            var settings = new FootnoteSettings();
            var start = footnoteProps.GetFirstChild<NumberingStart>()?.Val?.Value;
            if (start.HasValue)
            {
                settings.Start = start.Value;
            }

            var restart = footnoteProps.GetFirstChild<NumberingRestart>()?.Val?.Value;
            if (restart.HasValue)
            {
                settings.Restart = MapNoteNumberRestart(restart.Value);
            }

            var format = footnoteProps.GetFirstChild<NumberingFormat>()?.Val?.Value;
            if (format.HasValue)
            {
                settings.Format = MapNoteNumberFormat(format.Value);
            }

            var position = footnoteProps.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.FootnotePosition>()?.Val?.Value;
            if (position.HasValue)
            {
                settings.Position = MapFootnotePosition(position.Value);
            }

            if (settings.HasValues)
            {
                properties.Footnotes = settings;
            }
        }

        var endnoteProps = sectionProps.GetFirstChild<EndnoteProperties>();
        if (endnoteProps is not null)
        {
            var settings = new EndnoteSettings();
            var start = endnoteProps.GetFirstChild<NumberingStart>()?.Val?.Value;
            if (start.HasValue)
            {
                settings.Start = start.Value;
            }

            var restart = endnoteProps.GetFirstChild<NumberingRestart>()?.Val?.Value;
            if (restart.HasValue)
            {
                settings.Restart = MapNoteNumberRestart(restart.Value);
            }

            var format = endnoteProps.GetFirstChild<NumberingFormat>()?.Val?.Value;
            if (format.HasValue)
            {
                settings.Format = MapNoteNumberFormat(format.Value);
            }

            var position = endnoteProps.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.EndnotePosition>()?.Val?.Value;
            if (position.HasValue)
            {
                settings.Position = MapEndnotePosition(position.Value);
            }

            if (settings.HasValues)
            {
                properties.Endnotes = settings;
            }
        }

        return properties;
    }

    private static SectionBreakType ParseSectionBreakType(DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps)
    {
        var type = sectionProps.GetFirstChild<SectionType>()?.Val?.Value;
        if (type == SectionMarkValues.Continuous)
        {
            return SectionBreakType.Continuous;
        }

        if (type == SectionMarkValues.EvenPage)
        {
            return SectionBreakType.EvenPage;
        }

        if (type == SectionMarkValues.OddPage)
        {
            return SectionBreakType.OddPage;
        }

        if (type == SectionMarkValues.NextColumn)
        {
            return SectionBreakType.NextColumn;
        }

        if (sectionProps.GetFirstChild<TitlePage>() is not null
            || sectionProps.Elements<HeaderReference>().Any(item => item.Type?.Value == HeaderFooterValues.First)
            || sectionProps.Elements<FooterReference>().Any(item => item.Type?.Value == HeaderFooterValues.First))
        {
            return SectionBreakType.Continuous;
        }

        return SectionBreakType.NextPage;
    }

    private static void ApplySectionProperties(Vibe.Office.Documents.SectionProperties target, Vibe.Office.Documents.SectionProperties source)
    {
        if (source.PageWidth.HasValue)
        {
            target.PageWidth = source.PageWidth;
        }

        if (source.PageHeight.HasValue)
        {
            target.PageHeight = source.PageHeight;
        }

        if (source.MarginLeft.HasValue)
        {
            target.MarginLeft = source.MarginLeft;
        }

        if (source.MarginRight.HasValue)
        {
            target.MarginRight = source.MarginRight;
        }

        if (source.MarginTop.HasValue)
        {
            target.MarginTop = source.MarginTop;
        }

        if (source.MarginBottom.HasValue)
        {
            target.MarginBottom = source.MarginBottom;
        }

        if (source.HeaderOffset.HasValue)
        {
            target.HeaderOffset = source.HeaderOffset;
        }

        if (source.FooterOffset.HasValue)
        {
            target.FooterOffset = source.FooterOffset;
        }

        if (source.Gutter.HasValue)
        {
            target.Gutter = source.Gutter;
        }

        if (source.Orientation.HasValue)
        {
            target.Orientation = source.Orientation;
        }

        if (source.DifferentFirstPageHeaderFooter.HasValue)
        {
            target.DifferentFirstPageHeaderFooter = source.DifferentFirstPageHeaderFooter;
        }

        if (source.ColumnCount.HasValue)
        {
            target.ColumnCount = source.ColumnCount;
        }

        if (source.ColumnGap.HasValue)
        {
            target.ColumnGap = source.ColumnGap;
        }

        if (source.ColumnEqualWidth.HasValue)
        {
            target.ColumnEqualWidth = source.ColumnEqualWidth;
        }

        if (source.ColumnSeparator.HasValue)
        {
            target.ColumnSeparator = source.ColumnSeparator;
        }

        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        if (source.ColumnGaps.Count > 0)
        {
            target.ColumnGaps.Clear();
            target.ColumnGaps.AddRange(source.ColumnGaps);
        }

        if (source.DocGrid?.HasValues == true)
        {
            target.DocGrid = source.DocGrid.Clone();
        }

        if (source.PageBackgroundColor.HasValue)
        {
            target.PageBackgroundColor = source.PageBackgroundColor.Value;
        }

        if (source.PageBorders?.HasAny == true)
        {
            target.PageBorders = source.PageBorders.Clone();
        }

        if (source.LineNumbering is not null)
        {
            target.LineNumbering = source.LineNumbering.Clone();
        }

        if (source.PageNumbering is not null)
        {
            target.PageNumbering = source.PageNumbering.Clone();
        }
    }

    private static void FlushRunBuffer(StringBuilder buffer, TextStyleProperties? style, string? styleId, ParagraphBlock block, StringBuilder builder, HyperlinkInfo? hyperlink)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var text = buffer.ToString();
        var run = new RunInline(text, style) { StyleId = styleId, Hyperlink = hyperlink };
        block.Inlines.Add(run);
        builder.Append(text);
        buffer.Clear();
    }

    private sealed class FieldParseState
    {
        public FieldStartInline StartInline { get; }
        public StringBuilder Instruction { get; } = new StringBuilder();
        public bool InstructionFinalized { get; set; }
        public bool InResult { get; set; }
        public FieldDefinition? Definition { get; set; }
        public FieldEvaluationMode EvaluationMode { get; set; }
        public FieldKind Kind { get; set; }
        public bool AddedEvaluatedInline { get; set; }

        public FieldParseState(FieldStartInline startInline)
        {
            StartInline = startInline;
        }
    }

    private sealed class StyleResolver
    {
        private readonly Dictionary<string, StyleDefinition> _styles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? _defaultParagraphStyleId;
        private readonly string? _defaultCharacterStyleId;
        private readonly string? _defaultTableStyleId;
        private readonly OpenXmlElement? _defaultRunProperties;
        private readonly OpenXmlElement? _defaultParagraphProperties;
        private readonly VibeDocument _document;

        public DocumentThemeColorMap ThemeColors => _document.ThemeColors;

        public StyleResolver(MainDocumentPart? mainPart, VibeDocument document)
        {
            _document = document;
            var styles = mainPart?.StyleDefinitionsPart?.Styles;
            if (styles is not null)
            {
                _defaultRunProperties = styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
                _defaultParagraphProperties = styles.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;

                foreach (var style in styles.Elements<Style>())
                {
                    var styleId = style.StyleId?.Value;
                    if (string.IsNullOrWhiteSpace(styleId))
                    {
                        continue;
                    }

                    var type = style.Type?.Value ?? StyleValues.Paragraph;
                    var quickStyle = ReadOnOffValue(style.PrimaryStyle);
                    var definition = new StyleDefinition(
                        styleId,
                        type,
                        style.BasedOn?.Val?.Value,
                        style.StyleName?.Val?.Value,
                        style.GetFirstChild<NextParagraphStyle>()?.Val?.Value,
                        style.GetFirstChild<StyleLink>()?.Val?.Value,
                        style.UIPriority?.Val?.Value,
                        quickStyle,
                        ReadOnOffValue(style.GetFirstChild<SemiHidden>()),
                        ReadOnOffValue(style.GetFirstChild<UnhideWhenUsed>()),
                        ReadOnOffValue(style.GetFirstChild<AutoRedefine>()),
                        ReadOnOffValue(style.GetFirstChild<Hidden>()),
                        ReadOnOffValue(style.GetFirstChild<Locked>()),
                        quickStyle,
                        style.CustomStyle?.Value,
                        style.StyleRunProperties,
                        style.StyleParagraphProperties,
                        style.Elements<StyleTableProperties>().FirstOrDefault(),
                        style.Elements<StyleTableCellProperties>().FirstOrDefault(),
                        style.Elements<TableStyleProperties>().ToList());
                    _styles[styleId] = definition;

                    if (style.Default?.Value == true)
                    {
                        if (type == StyleValues.Paragraph)
                        {
                            _defaultParagraphStyleId = styleId;
                        }
                        else if (type == StyleValues.Character)
                        {
                            _defaultCharacterStyleId = styleId;
                        }
                        else if (type == StyleValues.Table)
                        {
                            _defaultTableStyleId = styleId;
                        }
                    }
                }
            }

            if (_defaultRunProperties is not null)
            {
                ApplyRunProperties(_document.DefaultTextStyle, _defaultRunProperties);
            }

            if (_defaultParagraphProperties is not null)
            {
                ApplyParagraphStyleProperties(_defaultParagraphProperties, _document.DefaultParagraphStyleProperties);
            }

            PopulateDocumentStyles();
        }

        private void PopulateDocumentStyles()
        {
            _document.Styles.DefaultParagraphStyleId = _defaultParagraphStyleId;
            _document.Styles.DefaultCharacterStyleId = _defaultCharacterStyleId;
            _document.Styles.DefaultTableStyleId = _defaultTableStyleId;

            foreach (var definition in _styles.Values)
            {
                if (definition.Type == StyleValues.Paragraph)
                {
                    var paragraphStyle = new ParagraphStyleDefinition(definition.Id)
                    {
                        Name = definition.Name,
                        BasedOnId = definition.BasedOnId,
                        NextStyleId = definition.NextStyleId,
                        LinkedStyleId = definition.LinkedStyleId,
                        ListId = definition.ParagraphProperties?.GetFirstChild<NumberingProperties>()?.NumberingId?.Val?.Value,
                        ListLevel = definition.ParagraphProperties?.GetFirstChild<NumberingProperties>()?.NumberingLevelReference?.Val?.Value,
                        UiPriority = definition.UiPriority,
                        QuickStyle = definition.QuickStyle,
                        SemiHidden = definition.SemiHidden,
                        UnhideWhenUsed = definition.UnhideWhenUsed,
                        AutoRedefine = definition.AutoRedefine,
                        Hidden = definition.Hidden,
                        Locked = definition.Locked,
                        PrimaryStyle = definition.PrimaryStyle,
                        CustomStyle = definition.CustomStyle
                    };
                    ApplyParagraphStyleProperties(definition.ParagraphProperties, paragraphStyle.ParagraphProperties);
                    ApplyRunStyleProperties(definition.RunProperties, paragraphStyle.RunProperties);
                    _document.Styles.ParagraphStyles[definition.Id] = paragraphStyle;
                }
                else if (definition.Type == StyleValues.Character)
                {
                    var characterStyle = new CharacterStyleDefinition(definition.Id)
                    {
                        Name = definition.Name,
                        BasedOnId = definition.BasedOnId,
                        NextStyleId = definition.NextStyleId,
                        LinkedStyleId = definition.LinkedStyleId,
                        UiPriority = definition.UiPriority,
                        QuickStyle = definition.QuickStyle,
                        SemiHidden = definition.SemiHidden,
                        UnhideWhenUsed = definition.UnhideWhenUsed,
                        AutoRedefine = definition.AutoRedefine,
                        Hidden = definition.Hidden,
                        Locked = definition.Locked,
                        PrimaryStyle = definition.PrimaryStyle,
                        CustomStyle = definition.CustomStyle
                    };
                    ApplyRunStyleProperties(definition.RunProperties, characterStyle.RunProperties);
                    _document.Styles.CharacterStyles[definition.Id] = characterStyle;
                }
                else if (definition.Type == StyleValues.Table)
                {
                    var tableStyle = new TableStyleDefinition(definition.Id)
                    {
                        Name = definition.Name,
                        BasedOnId = definition.BasedOnId,
                        NextStyleId = definition.NextStyleId,
                        LinkedStyleId = definition.LinkedStyleId,
                        UiPriority = definition.UiPriority,
                        QuickStyle = definition.QuickStyle,
                        SemiHidden = definition.SemiHidden,
                        UnhideWhenUsed = definition.UnhideWhenUsed,
                        AutoRedefine = definition.AutoRedefine,
                        Hidden = definition.Hidden,
                        Locked = definition.Locked,
                        PrimaryStyle = definition.PrimaryStyle,
                        CustomStyle = definition.CustomStyle
                    };
                    ApplyTableProperties(definition.TableProperties, tableStyle.TableProperties, ThemeColors);
                    ApplyTableCellProperties(definition.TableCellProperties, tableStyle.CellProperties, ThemeColors);
                    foreach (var overrideProperties in definition.TableStyleOverrides)
                    {
                        var condition = MapTableStyleCondition(overrideProperties.Type?.Value);
                        if (!condition.HasValue)
                        {
                            continue;
                        }

                        var conditionProperties = new TableStyleConditionProperties();
                        ApplyTableProperties(overrideProperties.TableStyleConditionalFormattingTableProperties, conditionProperties.TableProperties, ThemeColors);
                        ApplyTableCellProperties(overrideProperties.TableStyleConditionalFormattingTableCellProperties, conditionProperties.CellProperties, ThemeColors);
                        tableStyle.Conditions[condition.Value] = conditionProperties;
                    }

                    _document.Styles.TableStyles[definition.Id] = tableStyle;
                }
            }
        }

        public string? GetParagraphStyleId(Paragraph paragraph)
        {
            return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        }

        public string? GetRunStyleId(Run? run)
        {
            return run?.RunProperties?.RunStyle?.Val?.Value;
        }

        public bool TryGetNumbering(string? paragraphStyleId, out int numId, out int level)
        {
            numId = 0;
            level = 0;
            if (string.IsNullOrWhiteSpace(paragraphStyleId))
            {
                return false;
            }

            var found = false;
            foreach (var style in EnumerateStyleChain(paragraphStyleId))
            {
                var numbering = style.ParagraphProperties?.GetFirstChild<NumberingProperties>();
                if (numbering?.NumberingId?.Val?.Value is not int styleNumId)
                {
                    continue;
                }

                numId = styleNumId;
                level = numbering.NumberingLevelReference?.Val?.Value ?? 0;
                found = true;
            }

            return found;
        }

        public void ApplyParagraphStyle(Paragraph paragraph, ParagraphBlock block)
        {
            DocxImporter.ApplyParagraphProperties(paragraph.ParagraphProperties, block.Properties);
        }

        public TextStyle ResolveRunStyle(string? paragraphStyleId, RunProperties? runProperties, string? runStyleId)
        {
            var style = _document.DefaultTextStyle.Clone();
            if (!string.IsNullOrWhiteSpace(_defaultCharacterStyleId))
            {
                ApplyRunStyleChain(style, _defaultCharacterStyleId);
            }

            if (!string.IsNullOrWhiteSpace(paragraphStyleId))
            {
                ApplyRunStyleChain(style, paragraphStyleId);
            }

            if (!string.IsNullOrWhiteSpace(runStyleId))
            {
                ApplyRunStyleChain(style, runStyleId);
            }

            ApplyRunProperties(style, runProperties);
            return style;
        }

        private void ApplyParagraphStyleChain(Vibe.Office.Documents.ParagraphProperties target, string styleId)
        {
            foreach (var style in EnumerateStyleChain(styleId))
            {
                if (style.ParagraphProperties is not null)
                {
                    DocxImporter.ApplyParagraphProperties(style.ParagraphProperties, target);
                }
            }
        }

        private void ApplyRunStyleChain(TextStyle target, string styleId)
        {
            foreach (var style in EnumerateStyleChain(styleId))
            {
                if (style.RunProperties is not null)
                {
                    ApplyRunProperties(target, style.RunProperties);
                }
            }
        }

        private IEnumerable<StyleDefinition> EnumerateStyleChain(string styleId)
        {
            var stack = new Stack<StyleDefinition>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentId = styleId;

            while (!string.IsNullOrWhiteSpace(currentId) && _styles.TryGetValue(currentId, out var style) && visited.Add(currentId))
            {
                stack.Push(style);
                currentId = style.BasedOnId;
            }

            while (stack.Count > 0)
            {
                yield return stack.Pop();
            }
        }
    }

    private sealed record StyleDefinition(
        string Id,
        StyleValues Type,
        string? BasedOnId,
        string? Name,
        string? NextStyleId,
        string? LinkedStyleId,
        int? UiPriority,
        bool? QuickStyle,
        bool? SemiHidden,
        bool? UnhideWhenUsed,
        bool? AutoRedefine,
        bool? Hidden,
        bool? Locked,
        bool? PrimaryStyle,
        bool? CustomStyle,
        OpenXmlElement? RunProperties,
        OpenXmlElement? ParagraphProperties,
        StyleTableProperties? TableProperties,
        StyleTableCellProperties? TableCellProperties,
        List<TableStyleProperties> TableStyleOverrides);

    private sealed class ListResolver
    {
        private readonly Dictionary<int, ListDefinition> _listDefinitions = new();
        private readonly VibeDocument _document;
        private readonly StyleResolver _styleResolver;

        public ListResolver(MainDocumentPart? mainPart, VibeDocument document, StyleResolver styleResolver)
        {
            _document = document;
            _styleResolver = styleResolver;

            var numbering = mainPart?.NumberingDefinitionsPart?.Numbering;
            if (numbering is null)
            {
                return;
            }

            var abstractDefinitions = new Dictionary<int, ListDefinition>();
            foreach (var abstractNum in numbering.Elements<AbstractNum>())
            {
                if (abstractNum.AbstractNumberId?.Value is not int abstractId)
                {
                    continue;
                }

                var definition = new ListDefinition(abstractId);
                foreach (var level in abstractNum.Elements<Level>())
                {
                    var levelIndex = level.LevelIndex?.Value ?? 0;
                    definition.Levels[levelIndex] = BuildLevelDefinition(level, levelIndex);
                }

                abstractDefinitions[abstractId] = definition;
            }

            foreach (var instance in numbering.Elements<NumberingInstance>())
            {
                if (instance.NumberID?.Value is not int numId)
                {
                    continue;
                }

                var listDefinition = new ListDefinition(numId);
                if (instance.AbstractNumId?.Val?.Value is int abstractId
                    && abstractDefinitions.TryGetValue(abstractId, out var abstractDefinition))
                {
                    foreach (var pair in abstractDefinition.Levels)
                    {
                        listDefinition.Levels[pair.Key] = pair.Value.Clone();
                    }
                }

                foreach (var levelOverride in instance.Elements<LevelOverride>())
                {
                    var levelIndex = levelOverride.LevelIndex?.Value ?? 0;
                    var overrideLevel = levelOverride.Elements<Level>().FirstOrDefault();
                    if (overrideLevel is not null)
                    {
                        listDefinition.Levels[levelIndex] = BuildLevelDefinition(overrideLevel, levelIndex);
                    }

                    var startOverrideValue = levelOverride.GetFirstChild<StartOverrideNumberingValue>()?.Val?.Value;
                    if (startOverrideValue is int startOverride)
                    {
                        if (!listDefinition.Levels.TryGetValue(levelIndex, out var existing))
                        {
                            existing = new ListLevelDefinition(levelIndex);
                            listDefinition.Levels[levelIndex] = existing;
                        }

                        existing.StartAt = startOverride;
                    }
                }

                _listDefinitions[numId] = listDefinition;
                _document.ListDefinitions[numId] = listDefinition;
            }
        }

        public ListInfo? Resolve(Paragraph paragraph)
        {
            if (!TryGetNumbering(paragraph, out var numId, out var level))
            {
                return null;
            }

            var listDefinition = _listDefinitions.TryGetValue(numId, out var definition) ? definition : null;
            var levelDefinition = listDefinition?.Levels.TryGetValue(level, out var resolvedLevel) == true
                ? resolvedLevel
                : null;
            var kind = levelDefinition?.Format == ListNumberFormat.Bullet ? ListKind.Bullet : ListKind.Numbered;
            var info = new ListInfo(kind, level, numId);
            ApplyLevelDefinition(info, levelDefinition);
            return info;
        }

        private bool TryGetNumbering(Paragraph paragraph, out int numId, out int level)
        {
            numId = 0;
            level = 0;

            var numberingProps = paragraph.ParagraphProperties?.NumberingProperties;
            if (numberingProps?.NumberingId?.Val?.Value is int directNumId)
            {
                numId = directNumId;
                level = numberingProps.NumberingLevelReference?.Val?.Value ?? 0;
                return true;
            }

            var styleId = _styleResolver.GetParagraphStyleId(paragraph);
            return _styleResolver.TryGetNumbering(styleId, out numId, out level);
        }

        private static void ApplyLevelDefinition(ListInfo info, ListLevelDefinition? levelDefinition)
        {
            if (levelDefinition is null)
            {
                return;
            }

            info.NumberFormat = levelDefinition.Format;
            info.LevelText = levelDefinition.LevelText;
            info.BulletSymbol = levelDefinition.BulletSymbol;
            info.StartAt = levelDefinition.StartAt;
            info.LeftIndent = levelDefinition.LeftIndent;
            info.HangingIndent = levelDefinition.HangingIndent;
            info.TabStop = levelDefinition.TabStop;
        }

        private static ListLevelDefinition BuildLevelDefinition(Level level, int levelIndex)
        {
            var definition = new ListLevelDefinition(levelIndex)
            {
                Format = MapNumberFormat(level.NumberingFormat?.Val?.Value),
                LevelText = level.LevelText?.Val?.Value,
                StartAt = level.StartNumberingValue?.Val?.Value ?? 1
            };

            if (definition.Format == ListNumberFormat.Bullet)
            {
                definition.BulletSymbol = definition.LevelText;
            }

            var paragraphProperties = level.Elements<DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties>().FirstOrDefault();
            if (paragraphProperties is not null)
            {
                var indentation = paragraphProperties.Indentation;
                if (indentation is not null)
                {
                    var leftIndent = ParseTwips(indentation.Left) ?? ParseTwips(indentation.Start);
                    if (leftIndent.HasValue)
                    {
                        definition.LeftIndent = leftIndent;
                    }
                }

                if (indentation?.Hanging is not null)
                {
                    definition.HangingIndent = ParseTwips(indentation.Hanging);
                }

                if (indentation?.FirstLine is not null && !definition.HangingIndent.HasValue)
                {
                    var firstLine = ParseTwips(indentation.FirstLine);
                    if (firstLine.HasValue && firstLine.Value < 0)
                    {
                        definition.HangingIndent = MathF.Abs(firstLine.Value);
                    }
                }

                var tabs = paragraphProperties.GetFirstChild<Tabs>();
                var tab = tabs?.Elements<TabStop>().FirstOrDefault();
                var positionTwips = TryParseTwips(tab?.Position);
                if (positionTwips.HasValue)
                {
                    definition.TabStop = TwipsToDip(positionTwips.Value);
                }
            }

            if (string.IsNullOrWhiteSpace(definition.LevelText) && definition.Format != ListNumberFormat.Bullet)
            {
                definition.LevelText = "%1.";
            }

            return definition;
        }

        private static ListNumberFormat MapNumberFormat(NumberFormatValues? format)
        {
            if (!format.HasValue)
            {
                return ListNumberFormat.Decimal;
            }

            var value = format.Value;
            if (value.Equals(NumberFormatValues.Bullet))
            {
                return ListNumberFormat.Bullet;
            }

            if (value.Equals(NumberFormatValues.LowerLetter))
            {
                return ListNumberFormat.LowerLetter;
            }

            if (value.Equals(NumberFormatValues.UpperLetter))
            {
                return ListNumberFormat.UpperLetter;
            }

            if (value.Equals(NumberFormatValues.LowerRoman))
            {
                return ListNumberFormat.LowerRoman;
            }

            if (value.Equals(NumberFormatValues.UpperRoman))
            {
                return ListNumberFormat.UpperRoman;
            }

            return ListNumberFormat.Decimal;
        }
    }

    private static void ApplyParagraphProperties(Paragraph paragraph, Vibe.Office.Documents.ParagraphProperties properties)
    {
        ApplyParagraphProperties(paragraph.ParagraphProperties, properties);
    }

    private static void ApplyParagraphProperties(OpenXmlElement? props, Vibe.Office.Documents.ParagraphProperties properties)
    {
        if (props is null)
        {
            return;
        }

        var justification = props.GetFirstChild<Justification>()?.Val?.Value;
        if (justification is not null)
        {
            if (justification == JustificationValues.Center)
            {
                properties.Alignment = ParagraphAlignment.Center;
            }
            else if (justification == JustificationValues.Right)
            {
                properties.Alignment = ParagraphAlignment.Right;
            }
            else if (justification == JustificationValues.End)
            {
                properties.Alignment = ParagraphAlignment.Right;
            }
            else if (justification == JustificationValues.Start)
            {
                properties.Alignment = ParagraphAlignment.Left;
            }
            else if (justification == JustificationValues.Both)
            {
                properties.Alignment = ParagraphAlignment.Justify;
            }
            else
            {
                properties.Alignment = ParagraphAlignment.Left;
            }
        }

        var spacing = props.GetFirstChild<SpacingBetweenLines>();
        if (spacing is not null)
        {
            properties.SpacingBefore = ParseTwips(spacing.Before);
            properties.SpacingAfter = ParseTwips(spacing.After);
            if (spacing.BeforeLines?.Value is int beforeLines)
            {
                properties.SpacingBeforeLines = beforeLines;
            }

            if (spacing.AfterLines?.Value is int afterLines)
            {
                properties.SpacingAfterLines = afterLines;
            }

            if (spacing.BeforeAutoSpacing?.Value is bool beforeAuto)
            {
                properties.AutoSpacingBefore = beforeAuto;
            }

            if (spacing.AfterAutoSpacing?.Value is bool afterAuto)
            {
                properties.AutoSpacingAfter = afterAuto;
            }

            var lineSpacingTwips = TryParseTwips(spacing.Line);
            if (lineSpacingTwips.HasValue)
            {
                properties.LineSpacing = (int)MathF.Round(lineSpacingTwips.Value);
            }

            if (spacing.LineRule?.Value is LineSpacingRuleValues rule)
            {
                properties.LineSpacingRule = MapLineSpacingRule(rule);
            }
        }

        var indentation = props.GetFirstChild<Indentation>();
        if (indentation is not null)
        {
            properties.IndentLeft = ParseTwips(indentation.Left) ?? ParseTwips(indentation.Start);
            properties.IndentRight = ParseTwips(indentation.Right) ?? ParseTwips(indentation.End);
            var firstLine = ParseTwips(indentation.FirstLine);
            if (firstLine.HasValue)
            {
                properties.FirstLineIndent = firstLine;
            }
            else if (ParseTwips(indentation.Hanging) is { } hanging)
            {
                properties.FirstLineIndent = -hanging;
            }
        }

        var tabs = props.GetFirstChild<Tabs>();
        if (tabs is not null)
        {
            properties.TabStops.Clear();
            foreach (var tab in tabs.Elements<TabStop>())
            {
                if (tab.Val?.Value == TabStopValues.Clear)
                {
                    continue;
                }

                var positionTwips = TryParseTwips(tab.Position);
                if (positionTwips.HasValue)
                {
                    var value = TwipsToDip(positionTwips.Value);
                    properties.TabStops.Add(new TabStopDefinition(value)
                    {
                        Alignment = MapTabAlignment(tab.Val?.Value),
                        Leader = MapTabLeader(tab.Leader?.Value)
                    });
                }
            }
        }

        if (properties.TabStops.Count > 0)
        {
            properties.TabStops.Sort();
        }

        var keepNext = props.GetFirstChild<KeepNext>();
        if (keepNext is not null)
        {
            properties.KeepWithNext = keepNext.Val?.Value != false;
        }

        var keepLines = props.GetFirstChild<KeepLines>();
        if (keepLines is not null)
        {
            properties.KeepLinesTogether = keepLines.Val?.Value != false;
        }

        var widowControl = props.GetFirstChild<WidowControl>();
        if (widowControl is not null)
        {
            properties.WidowControl = widowControl.Val?.Value != false;
        }

        var pageBreakBefore = props.GetFirstChild<PageBreakBefore>();
        if (pageBreakBefore is not null)
        {
            properties.PageBreakBefore = pageBreakBefore.Val?.Value != false;
        }

        var contextualSpacing = props.GetFirstChild<ContextualSpacing>();
        if (contextualSpacing is not null)
        {
            properties.ContextualSpacing = contextualSpacing.Val?.Value != false;
        }

        var bidi = props.GetFirstChild<BiDi>();
        if (bidi is not null)
        {
            properties.Bidi = bidi.Val?.Value != false;
        }

        var textDirection = props.GetFirstChild<TextDirection>()?.Val?.Value;
        if (textDirection is not null)
        {
            properties.TextDirection = MapTextDirection(textDirection.Value);
        }

        var eastAsianLayout = props.GetFirstChild<EastAsianLayout>();
        if (eastAsianLayout is not null)
        {
            properties.EastAsianLayout = ParseEastAsianLayout(eastAsianLayout);
        }

        var shading = props.GetFirstChild<Shading>();
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var shadingColor))
        {
            properties.ShadingColor = shadingColor;
        }

        var suppressLineNumbers = props.GetFirstChild<SuppressLineNumbers>();
        if (suppressLineNumbers is not null)
        {
            properties.SuppressLineNumbers = suppressLineNumbers.Val?.Value != false;
        }

        var frame = props.GetFirstChild<FrameProperties>();
        if (frame is not null)
        {
            if (frame.DropCap?.Value is DropCapLocationValues dropCapLocation)
            {
                var dropCap = new DropCapSettings
                {
                    Kind = dropCapLocation == DropCapLocationValues.Margin
                        ? DropCapKind.Margin
                        : DropCapKind.Drop
                };
                if (frame.Lines?.Value is int lines && lines > 0)
                {
                    dropCap.Lines = lines;
                }

                var dropCapDistance = TryParseTwips(frame.HorizontalSpace);
                if (dropCapDistance.HasValue)
                {
                    dropCap.Distance = TwipsToDip(dropCapDistance.Value);
                }

                properties.DropCap = dropCap;
            }

            var frameProperties = new ParagraphFrameProperties();
            var width = TryParseTwips(frame.Width);
            if (width.HasValue)
            {
                frameProperties.Width = TwipsToDip(width.Value);
            }

            var height = TryParseTwips(frame.Height);
            if (height.HasValue)
            {
                frameProperties.Height = TwipsToDip(height.Value);
            }

            var x = TryParseTwips(frame.X);
            if (x.HasValue)
            {
                frameProperties.X = TwipsToDip(x.Value);
            }

            var y = TryParseTwips(frame.Y);
            if (y.HasValue)
            {
                frameProperties.Y = TwipsToDip(y.Value);
            }

            var hSpace = TryParseTwips(frame.HorizontalSpace);
            if (hSpace.HasValue)
            {
                frameProperties.HorizontalSpace = TwipsToDip(hSpace.Value);
            }

            var vSpace = TryParseTwips(frame.VerticalSpace);
            if (vSpace.HasValue)
            {
                frameProperties.VerticalSpace = TwipsToDip(vSpace.Value);
            }

            if (frame.HorizontalPosition?.Value is HorizontalAnchorValues hAnchor)
            {
                frameProperties.HorizontalReference = MapFrameHorizontalReference(hAnchor);
            }

            if (frame.VerticalPosition?.Value is VerticalAnchorValues vAnchor)
            {
                frameProperties.VerticalReference = MapFrameVerticalReference(vAnchor);
            }

            if (frame.XAlign?.Value is HorizontalAlignmentValues xAlign)
            {
                frameProperties.HorizontalAlignment = MapFrameHorizontalAlignment(xAlign);
            }

            if (frame.YAlign?.Value is VerticalAlignmentValues yAlign)
            {
                frameProperties.VerticalAlignment = MapFrameVerticalAlignment(yAlign);
            }

            if (frame.Wrap?.Value is TextWrappingValues wrap)
            {
                frameProperties.WrapStyle = MapFrameWrapStyle(wrap);
            }

            if (frame.AnchorLock?.Value is bool lockValue)
            {
                frameProperties.AnchorLock = lockValue;
            }

            if (frameProperties.HasValues)
            {
                properties.Frame = frameProperties;
            }
        }

        var borders = props.GetFirstChild<OpenXmlParagraphBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder);
        }
    }

    private static void ApplyParagraphStyleProperties(OpenXmlElement? props, ParagraphStyleProperties properties)
    {
        if (props is null)
        {
            return;
        }

        var justification = props.GetFirstChild<Justification>()?.Val?.Value;
        if (justification is not null)
        {
            if (justification == JustificationValues.Center)
            {
                properties.Alignment = ParagraphAlignment.Center;
            }
            else if (justification == JustificationValues.Right)
            {
                properties.Alignment = ParagraphAlignment.Right;
            }
            else if (justification == JustificationValues.End)
            {
                properties.Alignment = ParagraphAlignment.Right;
            }
            else if (justification == JustificationValues.Start)
            {
                properties.Alignment = ParagraphAlignment.Left;
            }
            else if (justification == JustificationValues.Both)
            {
                properties.Alignment = ParagraphAlignment.Justify;
            }
            else
            {
                properties.Alignment = ParagraphAlignment.Left;
            }
        }

        var spacing = props.GetFirstChild<SpacingBetweenLines>();
        if (spacing is not null)
        {
            properties.SpacingBefore = ParseTwips(spacing.Before);
            properties.SpacingAfter = ParseTwips(spacing.After);
            if (spacing.BeforeLines?.Value is int beforeLines)
            {
                properties.SpacingBeforeLines = beforeLines;
            }

            if (spacing.AfterLines?.Value is int afterLines)
            {
                properties.SpacingAfterLines = afterLines;
            }

            if (spacing.BeforeAutoSpacing?.Value is bool beforeAuto)
            {
                properties.AutoSpacingBefore = beforeAuto;
            }

            if (spacing.AfterAutoSpacing?.Value is bool afterAuto)
            {
                properties.AutoSpacingAfter = afterAuto;
            }

            var lineSpacingTwips = TryParseTwips(spacing.Line);
            if (lineSpacingTwips.HasValue)
            {
                properties.LineSpacing = (int)MathF.Round(lineSpacingTwips.Value);
            }

            if (spacing.LineRule?.Value is LineSpacingRuleValues rule)
            {
                properties.LineSpacingRule = MapLineSpacingRule(rule);
            }
        }

        var indentation = props.GetFirstChild<Indentation>();
        if (indentation is not null)
        {
            properties.IndentLeft = ParseTwips(indentation.Left) ?? ParseTwips(indentation.Start);
            properties.IndentRight = ParseTwips(indentation.Right) ?? ParseTwips(indentation.End);
            var firstLine = ParseTwips(indentation.FirstLine);
            if (firstLine.HasValue)
            {
                properties.FirstLineIndent = firstLine;
            }
            else if (ParseTwips(indentation.Hanging) is { } hanging)
            {
                properties.FirstLineIndent = -hanging;
            }
        }

        var tabs = props.GetFirstChild<Tabs>();
        if (tabs is not null)
        {
            properties.TabStops.Clear();
            foreach (var tab in tabs.Elements<TabStop>())
            {
                if (tab.Val?.Value == TabStopValues.Clear)
                {
                    continue;
                }

                var positionTwips = TryParseTwips(tab.Position);
                if (positionTwips.HasValue)
                {
                    var value = TwipsToDip(positionTwips.Value);
                    properties.TabStops.Add(new TabStopDefinition(value)
                    {
                        Alignment = MapTabAlignment(tab.Val?.Value),
                        Leader = MapTabLeader(tab.Leader?.Value)
                    });
                }
            }
        }

        if (properties.TabStops.Count > 0)
        {
            properties.TabStops.Sort();
        }

        var keepNext = props.GetFirstChild<KeepNext>();
        if (keepNext is not null)
        {
            properties.KeepWithNext = keepNext.Val?.Value != false;
        }

        var keepLines = props.GetFirstChild<KeepLines>();
        if (keepLines is not null)
        {
            properties.KeepLinesTogether = keepLines.Val?.Value != false;
        }

        var widowControl = props.GetFirstChild<WidowControl>();
        if (widowControl is not null)
        {
            properties.WidowControl = widowControl.Val?.Value != false;
        }

        var pageBreakBefore = props.GetFirstChild<PageBreakBefore>();
        if (pageBreakBefore is not null)
        {
            properties.PageBreakBefore = pageBreakBefore.Val?.Value != false;
        }

        var contextualSpacing = props.GetFirstChild<ContextualSpacing>();
        if (contextualSpacing is not null)
        {
            properties.ContextualSpacing = contextualSpacing.Val?.Value != false;
        }

        var bidi = props.GetFirstChild<BiDi>();
        if (bidi is not null)
        {
            properties.Bidi = bidi.Val?.Value != false;
        }

        var textDirection = props.GetFirstChild<TextDirection>()?.Val?.Value;
        if (textDirection is not null)
        {
            properties.TextDirection = MapTextDirection(textDirection.Value);
        }

        var eastAsianLayout = props.GetFirstChild<EastAsianLayout>();
        if (eastAsianLayout is not null)
        {
            properties.EastAsianLayout = ParseEastAsianLayout(eastAsianLayout);
        }

        var shading = props.GetFirstChild<Shading>();
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var shadingColor))
        {
            properties.ShadingColor = shadingColor;
        }

        var suppressLineNumbers = props.GetFirstChild<SuppressLineNumbers>();
        if (suppressLineNumbers is not null)
        {
            properties.SuppressLineNumbers = suppressLineNumbers.Val?.Value != false;
        }

        var frame = props.GetFirstChild<FrameProperties>();
        if (frame is not null)
        {
            if (frame.DropCap?.Value is DropCapLocationValues dropCapLocation)
            {
                var dropCap = new DropCapSettings
                {
                    Kind = dropCapLocation == DropCapLocationValues.Margin
                        ? DropCapKind.Margin
                        : DropCapKind.Drop
                };
                if (frame.Lines?.Value is int lines && lines > 0)
                {
                    dropCap.Lines = lines;
                }

                var dropCapDistance = TryParseTwips(frame.HorizontalSpace);
                if (dropCapDistance.HasValue)
                {
                    dropCap.Distance = TwipsToDip(dropCapDistance.Value);
                }

                properties.DropCap = dropCap;
            }

            var frameProperties = new ParagraphFrameProperties();
            var width = TryParseTwips(frame.Width);
            if (width.HasValue)
            {
                frameProperties.Width = TwipsToDip(width.Value);
            }

            var height = TryParseTwips(frame.Height);
            if (height.HasValue)
            {
                frameProperties.Height = TwipsToDip(height.Value);
            }

            var x = TryParseTwips(frame.X);
            if (x.HasValue)
            {
                frameProperties.X = TwipsToDip(x.Value);
            }

            var y = TryParseTwips(frame.Y);
            if (y.HasValue)
            {
                frameProperties.Y = TwipsToDip(y.Value);
            }

            var hSpace = TryParseTwips(frame.HorizontalSpace);
            if (hSpace.HasValue)
            {
                frameProperties.HorizontalSpace = TwipsToDip(hSpace.Value);
            }

            var vSpace = TryParseTwips(frame.VerticalSpace);
            if (vSpace.HasValue)
            {
                frameProperties.VerticalSpace = TwipsToDip(vSpace.Value);
            }

            if (frame.HorizontalPosition?.Value is HorizontalAnchorValues hAnchor)
            {
                frameProperties.HorizontalReference = MapFrameHorizontalReference(hAnchor);
            }

            if (frame.VerticalPosition?.Value is VerticalAnchorValues vAnchor)
            {
                frameProperties.VerticalReference = MapFrameVerticalReference(vAnchor);
            }

            if (frame.XAlign?.Value is HorizontalAlignmentValues xAlign)
            {
                frameProperties.HorizontalAlignment = MapFrameHorizontalAlignment(xAlign);
            }

            if (frame.YAlign?.Value is VerticalAlignmentValues yAlign)
            {
                frameProperties.VerticalAlignment = MapFrameVerticalAlignment(yAlign);
            }

            if (frame.Wrap?.Value is TextWrappingValues wrap)
            {
                frameProperties.WrapStyle = MapFrameWrapStyle(wrap);
            }

            if (frame.AnchorLock?.Value is bool lockValue)
            {
                frameProperties.AnchorLock = lockValue;
            }

            if (frameProperties.HasValues)
            {
                properties.Frame = frameProperties;
            }
        }

        var borders = props.GetFirstChild<OpenXmlParagraphBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder);
        }
    }

    private static TextStyle ExtractRunStyle(RunProperties? properties)
    {
        var style = new TextStyle();
        ApplyRunProperties(style, properties);
        return style;
    }

    private static TextStyleProperties? ExtractRunStyleProperties(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var style = new TextStyleProperties();
        ApplyRunStyleProperties(properties, style);
        return style.HasValues ? style : null;
    }

    private static void ApplyRunProperties(TextStyle style, OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return;
        }

        var bold = properties.GetFirstChild<Bold>();
        if (bold is not null)
        {
            var value = bold.Val?.Value ?? true;
            style.FontWeight = value ? DocFontWeight.Bold : DocFontWeight.Normal;
        }

        var italic = properties.GetFirstChild<Italic>();
        if (italic is not null)
        {
            var value = italic.Val?.Value ?? true;
            style.FontStyle = value ? DocFontStyle.Italic : DocFontStyle.Normal;
        }

        var underline = properties.GetFirstChild<Underline>();
        if (underline is not null)
        {
            var hasUnderlineTheme = false;
            var underlineStyle = MapUnderlineStyle(underline.Val?.Value);
            style.UnderlineStyle = underlineStyle;
            style.Underline = underlineStyle != DocUnderlineStyle.None;
            var underlineColor = underline.Color?.Value;
            if (!string.IsNullOrWhiteSpace(underlineColor) && !underlineColor.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(underlineColor, out var parsedUnderline))
            {
                style.UnderlineColor = parsedUnderline;
            }

            if (underline.ThemeColor?.Value is ThemeColorValues underlineTheme && TryMapThemeColor(underlineTheme, out var mappedUnderline))
            {
                style.UnderlineThemeColor = mappedUnderline;
                hasUnderlineTheme = true;
            }

            if (TryParseHexByte(underline.ThemeTint?.Value, out var underlineTint))
            {
                style.UnderlineThemeTint = underlineTint;
                hasUnderlineTheme = true;
            }

            if (TryParseHexByte(underline.ThemeShade?.Value, out var underlineShade))
            {
                style.UnderlineThemeShade = underlineShade;
                hasUnderlineTheme = true;
            }

            if (style.UnderlineColor.HasValue && !hasUnderlineTheme)
            {
                style.UnderlineThemeColor = null;
                style.UnderlineThemeTint = null;
                style.UnderlineThemeShade = null;
            }
        }

        var strike = properties.GetFirstChild<Strike>();
        if (strike is not null)
        {
            var value = strike.Val?.Value ?? true;
            style.Strikethrough = value;
        }

        var doubleStrike = properties.GetFirstChild<DoubleStrike>();
        if (doubleStrike is not null)
        {
            var value = doubleStrike.Val?.Value ?? true;
            style.Strikethrough = value;
        }

        var fontSize = properties.GetFirstChild<FontSize>()?.Val?.Value;
        if (fontSize is not null && float.TryParse(fontSize, out var halfPoints))
        {
            style.FontSize = HalfPointsToDip(halfPoints);
        }

        var fontSizeCs = properties.GetFirstChild<FontSizeComplexScript>()?.Val?.Value;
        if (fontSizeCs is not null && float.TryParse(fontSizeCs, out var halfPointsCs))
        {
            style.FontSizeComplexScript = HalfPointsToDip(halfPointsCs);
        }

        var colorElement = properties.GetFirstChild<Color>();
        var hasThemeColor = false;
        if (colorElement?.ThemeColor?.Value is ThemeColorValues themeColor && TryMapThemeColor(themeColor, out var mappedColor))
        {
            style.ThemeColor = mappedColor;
            hasThemeColor = true;
        }

        if (TryParseHexByte(colorElement?.ThemeTint?.Value, out var themeTint))
        {
            style.ThemeTint = themeTint;
            hasThemeColor = true;
        }

        if (TryParseHexByte(colorElement?.ThemeShade?.Value, out var themeShade))
        {
            style.ThemeShade = themeShade;
            hasThemeColor = true;
        }

        var color = colorElement?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(color) && !color.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(color, out var parsed))
            {
                style.Color = parsed;
            }

            if (!hasThemeColor)
            {
                style.ThemeColor = null;
                style.ThemeTint = null;
                style.ThemeShade = null;
            }
        }

        var verticalAlign = properties.GetFirstChild<VerticalTextAlignment>()?.Val?.Value;
        if (verticalAlign is not null)
        {
            style.VerticalPosition = MapVerticalPosition(verticalAlign.Value);
        }

        var position = properties.GetFirstChild<Position>();
        if (TryParseHalfPoints(position?.Val?.Value, out var positionValue))
        {
            style.BaselineOffset = positionValue;
        }

        var kern = properties.GetFirstChild<Kern>();
        if (kern?.Val?.Value is uint kernValue)
        {
            style.Kerning = HalfPointsToDip(kernValue);
        }

        var scale = properties.GetFirstChild<CharacterScale>();
        if (scale?.Val?.Value is long scaleValue && scaleValue > 0)
        {
            style.HorizontalScale = scaleValue / 100f;
        }

        var spacing = properties.GetFirstChild<Spacing>();
        if (spacing?.Val?.Value is int spacingValue)
        {
            style.LetterSpacing = TwipsToDip(spacingValue);
        }

        var smallCaps = properties.GetFirstChild<SmallCaps>();
        if (smallCaps is not null)
        {
            style.SmallCaps = smallCaps.Val?.Value != false;
        }

        var caps = properties.GetFirstChild<Caps>();
        if (caps is not null)
        {
            style.Caps = caps.Val?.Value != false;
        }

        var vanish = properties.GetFirstChild<Vanish>();
        if (vanish is not null)
        {
            style.Hidden = vanish.Val?.Value != false;
        }

        var highlight = properties.GetFirstChild<Highlight>()?.Val?.Value;
        if (highlight.HasValue && TryParseHighlightColor(highlight.Value, out var highlightColor))
        {
            style.HighlightColor = highlightColor;
        }

        if (!style.HighlightColor.HasValue)
        {
            var shading = properties.GetFirstChild<Shading>()?.Fill?.Value;
            if (!string.IsNullOrWhiteSpace(shading) && !shading.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(shading, out var shadingColor))
            {
                style.HighlightColor = shadingColor;
            }
        }

        var fonts = properties.GetFirstChild<RunFonts>();
        if (fonts is not null)
        {
            var ascii = fonts.Ascii?.Value;
            var highAnsi = fonts.HighAnsi?.Value;
            var eastAsia = fonts.EastAsia?.Value;
            var complex = fonts.ComplexScript?.Value;

            if (!string.IsNullOrWhiteSpace(ascii))
            {
                style.FontFamilyAscii = ascii;
            }

            if (!string.IsNullOrWhiteSpace(highAnsi))
            {
                style.FontFamilyHighAnsi = highAnsi;
            }

            if (!string.IsNullOrWhiteSpace(eastAsia))
            {
                style.FontFamilyEastAsia = eastAsia;
            }

            if (!string.IsNullOrWhiteSpace(complex))
            {
                style.FontFamilyComplexScript = complex;
            }

            var family = ascii ?? highAnsi ?? complex ?? eastAsia;
            if (!string.IsNullOrWhiteSpace(family))
            {
                style.FontFamily = family;
            }
        }

        if (fonts?.AsciiTheme?.Value is ThemeFontValues asciiTheme)
        {
            style.ThemeFontAscii = MapThemeFont(asciiTheme);
        }

        if (fonts?.HighAnsiTheme?.Value is ThemeFontValues highAnsiTheme)
        {
            style.ThemeFontHighAnsi = MapThemeFont(highAnsiTheme);
        }

        if (fonts?.EastAsiaTheme?.Value is ThemeFontValues eastAsiaTheme)
        {
            style.ThemeFontEastAsia = MapThemeFont(eastAsiaTheme);
        }

        if (fonts?.ComplexScriptTheme?.Value is ThemeFontValues complexTheme)
        {
            style.ThemeFontComplexScript = MapThemeFont(complexTheme);
        }

        var languages = properties.GetFirstChild<Languages>();
        if (languages is not null)
        {
            if (!string.IsNullOrWhiteSpace(languages.Val?.Value))
            {
                style.Language = languages.Val.Value;
            }

            if (!string.IsNullOrWhiteSpace(languages.EastAsia?.Value))
            {
                style.LanguageEastAsia = languages.EastAsia.Value;
            }

            if (!string.IsNullOrWhiteSpace(languages.Bidi?.Value))
            {
                style.LanguageBidi = languages.Bidi.Value;
            }
        }

        var eastAsianLayout = properties.GetFirstChild<EastAsianLayout>();
        if (eastAsianLayout is not null)
        {
            style.EastAsianLayout = ParseEastAsianLayout(eastAsianLayout);
        }

        var effects = ParseTextEffects(properties);
        if (effects is not null)
        {
            style.Effects ??= new TextEffects();
            style.Effects.ApplyOverrides(effects);
        }

        ApplyOpenTypeFeatures(style, properties);
    }

    private static void ApplyRunStyleProperties(OpenXmlElement? properties, TextStyleProperties style)
    {
        if (properties is null)
        {
            return;
        }

        var bold = properties.GetFirstChild<Bold>();
        if (bold is not null)
        {
            var value = bold.Val?.Value ?? true;
            style.FontWeight = value ? DocFontWeight.Bold : DocFontWeight.Normal;
        }

        var italic = properties.GetFirstChild<Italic>();
        if (italic is not null)
        {
            var value = italic.Val?.Value ?? true;
            style.FontStyle = value ? DocFontStyle.Italic : DocFontStyle.Normal;
        }

        var underline = properties.GetFirstChild<Underline>();
        if (underline is not null)
        {
            var hasUnderlineTheme = false;
            var underlineStyle = MapUnderlineStyle(underline.Val?.Value);
            style.UnderlineStyle = underlineStyle;
            style.Underline = underlineStyle != DocUnderlineStyle.None;
            var underlineColor = underline.Color?.Value;
            if (!string.IsNullOrWhiteSpace(underlineColor) && !underlineColor.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(underlineColor, out var parsedUnderline))
            {
                style.UnderlineColor = parsedUnderline;
            }

            if (underline.ThemeColor?.Value is ThemeColorValues underlineTheme && TryMapThemeColor(underlineTheme, out var mappedUnderline))
            {
                style.UnderlineThemeColor = mappedUnderline;
                hasUnderlineTheme = true;
            }

            if (TryParseHexByte(underline.ThemeTint?.Value, out var underlineTint))
            {
                style.UnderlineThemeTint = underlineTint;
                hasUnderlineTheme = true;
            }

            if (TryParseHexByte(underline.ThemeShade?.Value, out var underlineShade))
            {
                style.UnderlineThemeShade = underlineShade;
                hasUnderlineTheme = true;
            }

            if (style.UnderlineColor.HasValue && !hasUnderlineTheme)
            {
                style.UnderlineThemeColor = null;
                style.UnderlineThemeTint = null;
                style.UnderlineThemeShade = null;
            }
        }

        var strike = properties.GetFirstChild<Strike>();
        if (strike is not null)
        {
            var value = strike.Val?.Value ?? true;
            style.Strikethrough = value;
        }

        var doubleStrike = properties.GetFirstChild<DoubleStrike>();
        if (doubleStrike is not null)
        {
            var value = doubleStrike.Val?.Value ?? true;
            style.Strikethrough = value;
        }

        var fontSize = properties.GetFirstChild<FontSize>()?.Val?.Value;
        if (fontSize is not null && float.TryParse(fontSize, out var halfPoints))
        {
            style.FontSize = HalfPointsToDip(halfPoints);
        }

        var fontSizeCs = properties.GetFirstChild<FontSizeComplexScript>()?.Val?.Value;
        if (fontSizeCs is not null && float.TryParse(fontSizeCs, out var halfPointsCs))
        {
            style.FontSizeComplexScript = HalfPointsToDip(halfPointsCs);
        }

        var colorElement = properties.GetFirstChild<Color>();
        var hasThemeColor = false;
        if (colorElement?.ThemeColor?.Value is ThemeColorValues themeColor && TryMapThemeColor(themeColor, out var mappedColor))
        {
            style.ThemeColor = mappedColor;
            hasThemeColor = true;
        }

        if (TryParseHexByte(colorElement?.ThemeTint?.Value, out var themeTint))
        {
            style.ThemeTint = themeTint;
            hasThemeColor = true;
        }

        if (TryParseHexByte(colorElement?.ThemeShade?.Value, out var themeShade))
        {
            style.ThemeShade = themeShade;
            hasThemeColor = true;
        }

        var color = colorElement?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(color) && !color.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(color, out var parsed))
            {
                style.Color = parsed;
            }

            if (!hasThemeColor)
            {
                style.ThemeColor = null;
                style.ThemeTint = null;
                style.ThemeShade = null;
            }
        }

        var verticalAlign = properties.GetFirstChild<VerticalTextAlignment>()?.Val?.Value;
        if (verticalAlign is not null)
        {
            style.VerticalPosition = MapVerticalPosition(verticalAlign.Value);
        }

        var position = properties.GetFirstChild<Position>();
        if (TryParseHalfPoints(position?.Val?.Value, out var positionValue))
        {
            style.BaselineOffset = positionValue;
        }

        var kern = properties.GetFirstChild<Kern>();
        if (kern?.Val?.Value is uint kernValue)
        {
            style.Kerning = HalfPointsToDip(kernValue);
        }

        var scale = properties.GetFirstChild<CharacterScale>();
        if (scale?.Val?.Value is long scaleValue && scaleValue > 0)
        {
            style.HorizontalScale = scaleValue / 100f;
        }

        var spacing = properties.GetFirstChild<Spacing>();
        if (spacing?.Val?.Value is int spacingValue)
        {
            style.LetterSpacing = TwipsToDip(spacingValue);
        }

        var smallCaps = properties.GetFirstChild<SmallCaps>();
        if (smallCaps is not null)
        {
            style.SmallCaps = smallCaps.Val?.Value != false;
        }

        var caps = properties.GetFirstChild<Caps>();
        if (caps is not null)
        {
            style.Caps = caps.Val?.Value != false;
        }

        var vanish = properties.GetFirstChild<Vanish>();
        if (vanish is not null)
        {
            style.Hidden = vanish.Val?.Value != false;
        }

        var highlight = properties.GetFirstChild<Highlight>()?.Val?.Value;
        if (highlight.HasValue && TryParseHighlightColor(highlight.Value, out var highlightColor))
        {
            style.HighlightColor = highlightColor;
        }

        if (!style.HighlightColor.HasValue)
        {
            var shading = properties.GetFirstChild<Shading>()?.Fill?.Value;
            if (!string.IsNullOrWhiteSpace(shading) && !shading.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(shading, out var shadingColor))
            {
                style.HighlightColor = shadingColor;
            }
        }

        var fonts = properties.GetFirstChild<RunFonts>();
        if (fonts is not null)
        {
            var ascii = fonts.Ascii?.Value;
            var highAnsi = fonts.HighAnsi?.Value;
            var eastAsia = fonts.EastAsia?.Value;
            var complex = fonts.ComplexScript?.Value;

            if (!string.IsNullOrWhiteSpace(ascii))
            {
                style.FontFamilyAscii = ascii;
            }

            if (!string.IsNullOrWhiteSpace(highAnsi))
            {
                style.FontFamilyHighAnsi = highAnsi;
            }

            if (!string.IsNullOrWhiteSpace(eastAsia))
            {
                style.FontFamilyEastAsia = eastAsia;
            }

            if (!string.IsNullOrWhiteSpace(complex))
            {
                style.FontFamilyComplexScript = complex;
            }

            var family = ascii ?? highAnsi ?? complex ?? eastAsia;
            if (!string.IsNullOrWhiteSpace(family))
            {
                style.FontFamily = family;
            }
        }

        if (fonts?.AsciiTheme?.Value is ThemeFontValues asciiTheme)
        {
            style.ThemeFontAscii = MapThemeFont(asciiTheme);
        }

        if (fonts?.HighAnsiTheme?.Value is ThemeFontValues highAnsiTheme)
        {
            style.ThemeFontHighAnsi = MapThemeFont(highAnsiTheme);
        }

        if (fonts?.EastAsiaTheme?.Value is ThemeFontValues eastAsiaTheme)
        {
            style.ThemeFontEastAsia = MapThemeFont(eastAsiaTheme);
        }

        if (fonts?.ComplexScriptTheme?.Value is ThemeFontValues complexTheme)
        {
            style.ThemeFontComplexScript = MapThemeFont(complexTheme);
        }

        var languages = properties.GetFirstChild<Languages>();
        if (languages is not null)
        {
            if (!string.IsNullOrWhiteSpace(languages.Val?.Value))
            {
                style.Language = languages.Val.Value;
            }

            if (!string.IsNullOrWhiteSpace(languages.EastAsia?.Value))
            {
                style.LanguageEastAsia = languages.EastAsia.Value;
            }

            if (!string.IsNullOrWhiteSpace(languages.Bidi?.Value))
            {
                style.LanguageBidi = languages.Bidi.Value;
            }
        }

        var eastAsianLayout = properties.GetFirstChild<EastAsianLayout>();
        if (eastAsianLayout is not null)
        {
            style.EastAsianLayout = ParseEastAsianLayout(eastAsianLayout);
        }

        var effects = ParseTextEffects(properties);
        if (effects is not null)
        {
            style.Effects ??= new TextEffects();
            style.Effects.ApplyOverrides(effects);
        }

        ApplyOpenTypeFeatures(style, properties);
    }

    private static void ApplyOpenTypeFeatures(TextStyle style, OpenXmlElement properties)
    {
        var features = ExtractOpenTypeFeatures(properties);
        if (features is null)
        {
            return;
        }

        style.OpenTypeFeatures ??= new TextOpenTypeFeatures();
        style.OpenTypeFeatures.ApplyOverrides(features);
    }

    private static void ApplyOpenTypeFeatures(TextStyleProperties style, OpenXmlElement properties)
    {
        var features = ExtractOpenTypeFeatures(properties);
        if (features is null)
        {
            return;
        }

        style.OpenTypeFeatures ??= new TextOpenTypeFeatures();
        style.OpenTypeFeatures.ApplyOverrides(features);
    }

    private static TextOpenTypeFeatures? ExtractOpenTypeFeatures(OpenXmlElement properties)
    {
        TextOpenTypeFeatures? features = null;

        TextOpenTypeFeatures EnsureFeatures()
        {
            features ??= new TextOpenTypeFeatures();
            return features;
        }

        var ligatures = properties.GetFirstChild<W14.Ligatures>();
        if (ligatures?.Val?.Value is W14.LigaturesValues ligatureValue)
        {
            EnsureFeatures().Ligatures = MapLigatures(ligatureValue);
        }

        var contextual = properties.GetFirstChild<W14.ContextualAlternatives>();
        if (contextual is not null)
        {
            var value = contextual.Val?.Value;
            var enabled = value != W14.OnOffValues.False && value != W14.OnOffValues.Zero;
            EnsureFeatures().ContextualAlternates = enabled;
        }

        var numberingFormat = properties.GetFirstChild<W14.NumberingFormat>();
        if (numberingFormat?.Val?.Value is W14.NumberFormValues numberForm)
        {
            EnsureFeatures().NumberForm = MapNumberForm(numberForm);
        }

        var numberSpacing = properties.GetFirstChild<W14.NumberSpacing>();
        if (numberSpacing?.Val?.Value is W14.NumberSpacingValues spacing)
        {
            EnsureFeatures().NumberSpacing = MapNumberSpacing(spacing);
        }

        var stylisticSets = properties.GetFirstChild<W14.StylisticSets>();
        if (stylisticSets is not null)
        {
            uint mask = 0;
            foreach (var child in stylisticSets.ChildElements)
            {
                if (!string.Equals(child.LocalName, "stylisticSet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = GetAttributeValue(child, "val", child.NamespaceUri);
                if (int.TryParse(value, out var setIndex) && setIndex is >= 1 and <= 20)
                {
                    mask |= 1u << (setIndex - 1);
                }
            }

            EnsureFeatures().StylisticSets = mask;
        }

        return features?.HasValues == true ? features : null;
    }

    private static DocLigatureOptions MapLigatures(W14.LigaturesValues value)
    {
        if (value == W14.LigaturesValues.None)
        {
            return DocLigatureOptions.None;
        }

        if (value == W14.LigaturesValues.Standard)
        {
            return DocLigatureOptions.Standard;
        }

        if (value == W14.LigaturesValues.Contextual)
        {
            return DocLigatureOptions.Contextual;
        }

        if (value == W14.LigaturesValues.Discretional)
        {
            return DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.Historical)
        {
            return DocLigatureOptions.Historical;
        }

        if (value == W14.LigaturesValues.StandardContextual)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Contextual;
        }

        if (value == W14.LigaturesValues.StandardHistorical)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Historical;
        }

        if (value == W14.LigaturesValues.ContextualHistorical)
        {
            return DocLigatureOptions.Contextual | DocLigatureOptions.Historical;
        }

        if (value == W14.LigaturesValues.StandardDiscretional)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.ContextualDiscretional)
        {
            return DocLigatureOptions.Contextual | DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.HistoricalDiscretional)
        {
            return DocLigatureOptions.Historical | DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.StandardContextualHistorical)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Contextual | DocLigatureOptions.Historical;
        }

        if (value == W14.LigaturesValues.StandardContextualDiscretional)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Contextual | DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.StandardHistoricalDiscretional)
        {
            return DocLigatureOptions.Standard | DocLigatureOptions.Historical | DocLigatureOptions.Discretional;
        }

        if (value == W14.LigaturesValues.ContextualHistoricalDiscretional)
        {
            return DocLigatureOptions.Contextual | DocLigatureOptions.Historical | DocLigatureOptions.Discretional;
        }

        return DocLigatureOptions.Standard | DocLigatureOptions.Contextual | DocLigatureOptions.Discretional | DocLigatureOptions.Historical;
    }

    private static DocNumberForm MapNumberForm(W14.NumberFormValues value)
    {
        if (value == W14.NumberFormValues.Lining)
        {
            return DocNumberForm.Lining;
        }

        if (value == W14.NumberFormValues.OldStyle)
        {
            return DocNumberForm.OldStyle;
        }

        return DocNumberForm.Default;
    }

    private static DocNumberSpacing MapNumberSpacing(W14.NumberSpacingValues value)
    {
        if (value == W14.NumberSpacingValues.Proportional)
        {
            return DocNumberSpacing.Proportional;
        }

        if (value == W14.NumberSpacingValues.Tabular)
        {
            return DocNumberSpacing.Tabular;
        }

        return DocNumberSpacing.Default;
    }

    private static TextEffects? ParseTextEffects(OpenXmlElement properties)
    {
        TextEffects? effects = null;

        TextEffects EnsureEffects()
        {
            effects ??= new TextEffects();
            return effects;
        }

        var outline = properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Outline>();
        if (outline is not null)
        {
            var enabled = outline.Val?.Value ?? true;
            EnsureEffects().Outline = new TextOutlineEffect { Enabled = enabled };
        }

        var shadow = properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Shadow>();
        if (shadow is not null)
        {
            var enabled = shadow.Val?.Value ?? true;
            EnsureEffects().Shadow = new TextShadowEffect { Enabled = enabled };
        }

        var emboss = properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Emboss>();
        if (emboss is not null)
        {
            EnsureEffects().Emboss = emboss.Val?.Value ?? true;
        }

        var imprint = properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Imprint>();
        if (imprint is not null)
        {
            EnsureEffects().Imprint = imprint.Val?.Value ?? true;
        }

        return effects?.HasValues == true ? effects : null;
    }

    private static void ApplyTableProperties(Table table, Vibe.Office.Documents.TableProperties properties, DocumentThemeColorMap? themeColors)
    {
        var grid = table.Elements<TableGrid>().FirstOrDefault();
        if (grid is not null)
        {
            foreach (var column in grid.Elements<GridColumn>())
            {
                var widthTwips = TryParseTwips(column.Width);
                if (widthTwips.HasValue)
                {
                    properties.ColumnWidths.Add(TwipsToDip(widthTwips.Value));
                }
            }
        }

        var props = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableProperties>().FirstOrDefault();
        ApplyTableProperties(props, properties, themeColors);
    }

    private static void ApplyTableProperties(OpenXmlElement? props, Vibe.Office.Documents.TableProperties properties, DocumentThemeColorMap? themeColors)
    {
        if (props is null)
        {
            return;
        }

        var tableWidth = props.GetFirstChild<TableWidth>();
        if (tableWidth is not null)
        {
            ApplyTableWidth(tableWidth, properties);
        }

        var tableIndent = props.GetFirstChild<TableIndentation>();
        if (tableIndent is not null)
        {
            ApplyTableIndentation(tableIndent, properties);
        }

        var tableJustification = props.GetFirstChild<TableJustification>();
        var tableAlignment = tableJustification?.Val?.Value;
        if (tableAlignment is not null)
        {
            if (tableAlignment == TableRowAlignmentValues.Center)
            {
                properties.Alignment = Vibe.Office.Documents.TableAlignment.Center;
            }
            else if (tableAlignment == TableRowAlignmentValues.Right)
            {
                properties.Alignment = Vibe.Office.Documents.TableAlignment.Right;
            }
            else if (tableAlignment == TableRowAlignmentValues.Left)
            {
                properties.Alignment = Vibe.Office.Documents.TableAlignment.Left;
            }
        }

        var tableLayout = props.GetFirstChild<TableLayout>();
        if (tableLayout?.Type?.Value is TableLayoutValues layoutType)
        {
            properties.LayoutMode = layoutType == TableLayoutValues.Fixed
                ? Vibe.Office.Documents.TableLayoutMode.Fixed
                : Vibe.Office.Documents.TableLayoutMode.Auto;
        }

        var cellSpacing = props.GetFirstChild<TableCellSpacing>();
        if (cellSpacing is not null)
        {
            ApplyTableCellSpacing(cellSpacing, properties);
        }

        var borders = props.GetFirstChild<OpenXmlTableBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder, themeColors);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder, themeColors);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder, themeColors);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder, themeColors);
            properties.Borders.InsideHorizontal = ParseBorderLine(borders.InsideHorizontalBorder, themeColors);
            properties.Borders.InsideVertical = ParseBorderLine(borders.InsideVerticalBorder, themeColors);
        }

        var cellMargin = props.GetFirstChild<TableCellMarginDefault>();
        OpenXmlSimpleType? leftMargin = cellMargin?.TableCellLeftMargin?.Width;
        if (leftMargin is null)
        {
            leftMargin = cellMargin?.StartMargin?.Width;
        }

        OpenXmlSimpleType? rightMargin = cellMargin?.TableCellRightMargin?.Width;
        if (rightMargin is null)
        {
            rightMargin = cellMargin?.EndMargin?.Width;
        }
        properties.CellPadding = ParseCellPadding(leftMargin,
            rightMargin,
            cellMargin?.TopMargin?.Width,
            cellMargin?.BottomMargin?.Width);

        var shading = props.GetFirstChild<Shading>();
        if (shading is not null && TryResolveShadingColor(shading, themeColors, out var color))
        {
            properties.ShadingColor = color;
        }

        var look = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableLook>();
        if (look is not null)
        {
            var hasLookFlags = TryParseTableLookFlags(look, out var lookFlags);
            var firstRow = look.FirstRow?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.FirstRow) != 0);
            var lastRow = look.LastRow?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.LastRow) != 0);
            var firstColumn = look.FirstColumn?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.FirstColumn) != 0);
            var lastColumn = look.LastColumn?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.LastColumn) != 0);
            var noHorizontalBand = look.NoHorizontalBand?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.NoHorizontalBand) != 0);
            var noVerticalBand = look.NoVerticalBand?.Value ?? (hasLookFlags && (lookFlags & TableLookFlags.NoVerticalBand) != 0);
            properties.Look = new Vibe.Office.Documents.TableLook
            {
                FirstRow = firstRow,
                LastRow = lastRow,
                FirstColumn = firstColumn,
                LastColumn = lastColumn,
                BandedRows = !noHorizontalBand,
                BandedColumns = !noVerticalBand
            };
        }

        var tablePosition = props.GetFirstChild<TablePositionProperties>();
        if (tablePosition is not null)
        {
            properties.FloatingAnchor = BuildTableFloatingAnchor(tablePosition, properties.Alignment);
        }

        var overlap = props.GetFirstChild<TableOverlap>();
        if (overlap?.Val?.Value is TableOverlapValues overlapValue
            && properties.FloatingAnchor is not null)
        {
            properties.FloatingAnchor.AllowOverlap = overlapValue != TableOverlapValues.Never;
        }
    }

    private static void ApplyTableWidth(TableWidth tableWidth, Vibe.Office.Documents.TableProperties properties)
    {
        var widthType = tableWidth.Type?.Value;
        if (widthType == TableWidthUnitValues.Auto)
        {
            properties.Width = null;
            properties.WidthUnit = Vibe.Office.Documents.TableWidthUnit.Auto;
            return;
        }

        if (widthType == TableWidthUnitValues.Dxa)
        {
            var twips = TryParseTwips(tableWidth.Width);
            if (twips.HasValue)
            {
                properties.Width = TwipsToDip(twips.Value);
                properties.WidthUnit = Vibe.Office.Documents.TableWidthUnit.Dxa;
            }

            return;
        }

        if (widthType == TableWidthUnitValues.Pct)
        {
            var percent = TryParseTablePercentage(tableWidth.Width);
            if (percent.HasValue)
            {
                properties.Width = percent.Value;
                properties.WidthUnit = Vibe.Office.Documents.TableWidthUnit.Pct;
            }
        }
    }

    private static void ApplyTableCellWidth(TableCellWidth cellWidth, Vibe.Office.Documents.TableCellProperties properties)
    {
        var widthType = cellWidth.Type?.Value;
        if (widthType == TableWidthUnitValues.Auto)
        {
            properties.PreferredWidth = null;
            properties.PreferredWidthUnit = Vibe.Office.Documents.TableWidthUnit.Auto;
            return;
        }

        if (widthType == TableWidthUnitValues.Pct)
        {
            var percent = TryParseTablePercentage(cellWidth.Width);
            if (percent.HasValue)
            {
                properties.PreferredWidth = percent.Value;
                properties.PreferredWidthUnit = Vibe.Office.Documents.TableWidthUnit.Pct;
                return;
            }
        }

        var widthTwips = TryParseTwips(cellWidth.Width);
        if (widthTwips.HasValue)
        {
            properties.PreferredWidth = TwipsToDip(widthTwips.Value);
            properties.PreferredWidthUnit = Vibe.Office.Documents.TableWidthUnit.Dxa;
        }
    }

    private static void ApplyTableIndentation(TableIndentation indentation, Vibe.Office.Documents.TableProperties properties)
    {
        var indentType = indentation.Type?.Value;
        if (indentType == TableWidthUnitValues.Dxa)
        {
            var twips = TryParseTwips(indentation.Width);
            if (twips.HasValue)
            {
                properties.Indent = TwipsToDip(twips.Value);
                properties.IndentUnit = Vibe.Office.Documents.TableWidthUnit.Dxa;
            }

            return;
        }

        if (indentType == TableWidthUnitValues.Pct)
        {
            var percent = TryParseTablePercentage(indentation.Width);
            if (percent.HasValue)
            {
                properties.Indent = percent.Value;
                properties.IndentUnit = Vibe.Office.Documents.TableWidthUnit.Pct;
            }
        }
    }

    private static void ApplyTableCellSpacing(TableCellSpacing spacing, Vibe.Office.Documents.TableProperties properties)
    {
        var spacingType = spacing.Type?.Value;
        if (spacingType == TableWidthUnitValues.Dxa)
        {
            var twips = TryParseTwips(spacing.Width);
            if (twips.HasValue)
            {
                properties.CellSpacing = TwipsToDip(twips.Value);
                properties.CellSpacingUnit = Vibe.Office.Documents.TableWidthUnit.Dxa;
            }

            return;
        }

        if (spacingType == TableWidthUnitValues.Pct)
        {
            var percent = TryParseTablePercentage(spacing.Width);
            if (percent.HasValue)
            {
                properties.CellSpacing = percent.Value;
                properties.CellSpacingUnit = Vibe.Office.Documents.TableWidthUnit.Pct;
            }
        }
    }

    private static void ApplyTableCellProperties(DocumentFormat.OpenXml.Wordprocessing.TableCell cell, Vibe.Office.Documents.TableCellProperties properties, DocumentThemeColorMap? themeColors)
    {
        ApplyTableCellProperties(cell.TableCellProperties, properties, themeColors);
    }

    private static void ApplyTableCellStructure(DocumentFormat.OpenXml.Wordprocessing.TableCell cell, Vibe.Office.Documents.TableCell target)
    {
        var props = cell.TableCellProperties;
        if (props is null)
        {
            return;
        }

        var gridSpan = props.GetFirstChild<GridSpan>()?.Val?.Value;
        if (gridSpan.HasValue && gridSpan.Value > 1)
        {
            target.ColumnSpan = (int)gridSpan.Value;
        }

        var vMerge = props.GetFirstChild<VerticalMerge>();
        if (vMerge is not null)
        {
            target.VerticalMerge = MapVerticalMerge(vMerge.Val?.Value);
        }
    }

    private static void ApplyTableRowProperties(DocumentFormat.OpenXml.Wordprocessing.TableRow row, Vibe.Office.Documents.TableRowProperties properties, DocumentThemeColorMap? themeColors)
    {
        ApplyTableRowProperties(row.TableRowProperties, properties, themeColors);
    }

    private static void ApplyTableRowProperties(OpenXmlElement? props, Vibe.Office.Documents.TableRowProperties properties, DocumentThemeColorMap? themeColors)
    {
        if (props is null)
        {
            return;
        }

        var rowHeight = props.GetFirstChild<TableRowHeight>();
        var heightTwips = TryParseTwips(rowHeight?.Val);
        if (heightTwips.HasValue)
        {
            properties.Height = TwipsToDip(heightTwips.Value);
        }

        if (rowHeight?.HeightType?.Value is HeightRuleValues rule)
        {
            properties.HeightRule = MapRowHeightRule(rule);
        }

        if (props.GetFirstChild<CantSplit>() is not null)
        {
            properties.CantSplit = true;
        }

        if (props.GetFirstChild<TableHeader>() is not null)
        {
            properties.RepeatOnEachPage = true;
        }

        var gridBefore = props.GetFirstChild<GridBefore>();
        if (gridBefore?.Val?.Value is int beforeCount)
        {
            properties.GridBefore = beforeCount;
        }

        var gridAfter = props.GetFirstChild<GridAfter>();
        if (gridAfter?.Val?.Value is int afterCount)
        {
            properties.GridAfter = afterCount;
        }

        var shading = props.GetFirstChild<Shading>();
        if (shading is not null && TryResolveShadingColor(shading, themeColors, out var color))
        {
            properties.ShadingColor = color;
        }
    }

    private static void ApplyTableCellProperties(OpenXmlElement? props, Vibe.Office.Documents.TableCellProperties properties, DocumentThemeColorMap? themeColors)
    {
        if (props is null)
        {
            return;
        }

        var verticalAlignment = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableCellVerticalAlignment>()?.Val?.Value;
        if (verticalAlignment is not null)
        {
            if (verticalAlignment == TableVerticalAlignmentValues.Center)
            {
                properties.VerticalAlignment = Vibe.Office.Documents.TableCellVerticalAlignment.Center;
            }
            else if (verticalAlignment == TableVerticalAlignmentValues.Bottom)
            {
                properties.VerticalAlignment = Vibe.Office.Documents.TableCellVerticalAlignment.Bottom;
            }
            else
            {
                properties.VerticalAlignment = Vibe.Office.Documents.TableCellVerticalAlignment.Top;
            }
        }

        var textDirection = props.GetFirstChild<TextDirection>()?.Val?.Value;
        if (textDirection is not null)
        {
            properties.TextDirection = MapTextDirection(textDirection.Value);
        }

        var shading = props.GetFirstChild<Shading>();
        if (shading is not null && TryResolveShadingColor(shading, themeColors, out var color))
        {
            properties.ShadingColor = color;
        }

        var borders = props.GetFirstChild<OpenXmlTableCellBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder, themeColors);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder, themeColors);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder, themeColors);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder, themeColors);
        }

        var cellWidth = props.GetFirstChild<TableCellWidth>();
        if (cellWidth is not null)
        {
            ApplyTableCellWidth(cellWidth, properties);
        }

        var margin = props.GetFirstChild<TableCellMargin>();
        OpenXmlSimpleType? leftCellMargin = margin?.LeftMargin?.Width;
        if (leftCellMargin is null)
        {
            leftCellMargin = margin?.StartMargin?.Width;
        }

        OpenXmlSimpleType? rightCellMargin = margin?.RightMargin?.Width;
        if (rightCellMargin is null)
        {
            rightCellMargin = margin?.EndMargin?.Width;
        }
        properties.Padding = ParseCellPadding(
            leftCellMargin,
            rightCellMargin,
            margin?.TopMargin?.Width,
            margin?.BottomMargin?.Width);
    }

    private static TableStyleCondition? MapTableStyleCondition(TableStyleOverrideValues? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value == TableStyleOverrideValues.FirstRow)
        {
            return TableStyleCondition.FirstRow;
        }

        if (value == TableStyleOverrideValues.LastRow)
        {
            return TableStyleCondition.LastRow;
        }

        if (value == TableStyleOverrideValues.FirstColumn)
        {
            return TableStyleCondition.FirstColumn;
        }

        if (value == TableStyleOverrideValues.LastColumn)
        {
            return TableStyleCondition.LastColumn;
        }

        if (value == TableStyleOverrideValues.Band1Horizontal)
        {
            return TableStyleCondition.Band1Horizontal;
        }

        if (value == TableStyleOverrideValues.Band2Horizontal)
        {
            return TableStyleCondition.Band2Horizontal;
        }

        if (value == TableStyleOverrideValues.Band1Vertical)
        {
            return TableStyleCondition.Band1Vertical;
        }

        if (value == TableStyleOverrideValues.Band2Vertical)
        {
            return TableStyleCondition.Band2Vertical;
        }

        if (value == TableStyleOverrideValues.NorthWestCell)
        {
            return TableStyleCondition.NorthWestCell;
        }

        if (value == TableStyleOverrideValues.NorthEastCell)
        {
            return TableStyleCondition.NorthEastCell;
        }

        if (value == TableStyleOverrideValues.SouthWestCell)
        {
            return TableStyleCondition.SouthWestCell;
        }

        if (value == TableStyleOverrideValues.SouthEastCell)
        {
            return TableStyleCondition.SouthEastCell;
        }

        return null;
    }

    private static TableCellVerticalMerge MapVerticalMerge(MergedCellValues? value)
    {
        if (value == MergedCellValues.Restart)
        {
            return TableCellVerticalMerge.Restart;
        }

        return TableCellVerticalMerge.Continue;
    }

    private static TableRowHeightRule MapRowHeightRule(HeightRuleValues rule)
    {
        if (rule == HeightRuleValues.AtLeast)
        {
            return TableRowHeightRule.AtLeast;
        }

        if (rule == HeightRuleValues.Exact)
        {
            return TableRowHeightRule.Exact;
        }

        return TableRowHeightRule.Auto;
    }

    private static float? ParseTwips(StringValue? value)
    {
        var twips = TryParseTwips(value);
        return twips.HasValue ? TwipsToDip(twips.Value) : null;
    }

    private static float TwipsToDip(float twips)
    {
        return twips / 20f * 96f / 72f;
    }

    private static float BorderSizeToDip(uint? size)
    {
        if (size is null)
        {
            return 0f;
        }

        var points = size.Value / 8f;
        return points * 96f / 72f;
    }

    private static float? BorderSpaceToDip(uint? space)
    {
        if (space is null)
        {
            return null;
        }

        var points = space.Value;
        return points * 96f / 72f;
    }

    private static DocThickness? ParseCellPadding(object? leftTwips, object? rightTwips, object? topTwips, object? bottomTwips)
    {
        var left = TryParseTwips(leftTwips);
        var right = TryParseTwips(rightTwips);
        var top = TryParseTwips(topTwips);
        var bottom = TryParseTwips(bottomTwips);

        if (!left.HasValue && !right.HasValue && !top.HasValue && !bottom.HasValue)
        {
            return null;
        }

        return new DocThickness(
            left.HasValue ? TwipsToDip(left.Value) : float.NaN,
            top.HasValue ? TwipsToDip(top.Value) : float.NaN,
            right.HasValue ? TwipsToDip(right.Value) : float.NaN,
            bottom.HasValue ? TwipsToDip(bottom.Value) : float.NaN);
    }

    private static float? TryParseTwips(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            OpenXmlSimpleType simple => TryParseTwipsString(simple.InnerText),
            short shortValue => shortValue,
            int intValue => intValue,
            uint uintValue => uintValue,
            long longValue => longValue,
            float floatValue => floatValue,
            double doubleValue => (float)doubleValue,
            string stringValue => TryParseTwipsString(stringValue),
            _ => null
        };
    }

    private static float? TryParseTwipsString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var span = value.AsSpan().Trim();
        if (float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        var unitIndex = span.Length;
        while (unitIndex > 0 && char.IsLetter(span[unitIndex - 1]))
        {
            unitIndex--;
        }

        if (unitIndex <= 0 || unitIndex >= span.Length)
        {
            return null;
        }

        if (!float.TryParse(span[..unitIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var valuePart))
        {
            return null;
        }

        var unit = span[unitIndex..];
        if (unit.Equals("pt", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart * 20f;
        }

        if (unit.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart * 72f * 20f;
        }

        if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart / 2.54f * 72f * 20f;
        }

        if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart / 25.4f * 72f * 20f;
        }

        if (unit.Equals("pc", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("pi", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart * 12f * 20f;
        }

        if (unit.Equals("twip", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("twips", StringComparison.OrdinalIgnoreCase))
        {
            return valuePart;
        }

        return null;
    }

    private static float? TryParseTablePercentage(object? value)
    {
        string? raw = value switch
        {
            OpenXmlSimpleType simple => simple.InnerText,
            string stringValue => stringValue,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!float.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return parsed / 50f;
    }

    private static bool TryParseTableLookFlags(DocumentFormat.OpenXml.Wordprocessing.TableLook look, out TableLookFlags flags)
    {
        flags = TableLookFlags.None;
        var raw = look.Val?.InnerText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!long.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        flags = (TableLookFlags)parsed;
        return true;
    }

    [Flags]
    private enum TableLookFlags
    {
        None = 0,
        FirstRow = 0x0020,
        LastRow = 0x0040,
        FirstColumn = 0x0080,
        LastColumn = 0x0100,
        NoHorizontalBand = 0x0200,
        NoVerticalBand = 0x0400
    }

    private static BorderLine? ParseBorderLine(BorderType? border, DocumentThemeColorMap? themeColors = null)
    {
        if (border is null)
        {
            return null;
        }

        var thickness = BorderSizeToDip(border.Size?.Value);
        var style = MapBorderStyle(border.Val?.Value);
        var line = new BorderLine
        {
            Style = style,
            Thickness = thickness,
            Color = ParseBorderColor(border, themeColors),
            Spacing = BorderSpaceToDip(border.Space?.Value)
        };
        line.Compound = MapBorderCompound(border.Val?.Value);
        line.CompoundSpacing = MapBorderCompoundSpacing(border.Val?.Value, thickness);
        return line;
    }

    private static DocBorderStyle MapBorderStyle(BorderValues? value)
    {
        if (value is null)
        {
            return DocBorderStyle.Single;
        }

        if (value == BorderValues.Nil || value == BorderValues.None)
        {
            return DocBorderStyle.None;
        }

        if (value == BorderValues.Double || value == BorderValues.DoubleWave)
        {
            return DocBorderStyle.Double;
        }

        if (value == BorderValues.Triple)
        {
            return DocBorderStyle.Triple;
        }

        if (value == BorderValues.ThickThinSmallGap
            || value == BorderValues.ThickThinMediumGap
            || value == BorderValues.ThickThinLargeGap)
        {
            return DocBorderStyle.ThickThin;
        }

        if (value == BorderValues.ThinThickSmallGap
            || value == BorderValues.ThinThickMediumGap
            || value == BorderValues.ThinThickLargeGap)
        {
            return DocBorderStyle.ThinThick;
        }

        if (value == BorderValues.ThinThickThinSmallGap
            || value == BorderValues.ThinThickThinMediumGap
            || value == BorderValues.ThinThickThinLargeGap)
        {
            return DocBorderStyle.ThinThickThin;
        }

        if (value == BorderValues.Dotted)
        {
            return DocBorderStyle.Dotted;
        }

        if (value == BorderValues.Dashed || value == BorderValues.DashSmallGap)
        {
            return DocBorderStyle.Dashed;
        }

        if (value == BorderValues.DotDash || value == BorderValues.DashDotStroked)
        {
            return DocBorderStyle.DotDash;
        }

        if (value == BorderValues.DotDotDash)
        {
            return DocBorderStyle.DotDotDash;
        }

        if (value == BorderValues.Thick)
        {
            return DocBorderStyle.Thick;
        }

        if (value == BorderValues.Wave)
        {
            return DocBorderStyle.Dashed;
        }

        if (value == BorderValues.ThreeDEmboss
            || value == BorderValues.ThreeDEngrave
            || value == BorderValues.Outset
            || value == BorderValues.Inset)
        {
            return DocBorderStyle.Single;
        }

        return DocBorderStyle.Single;
    }

    private static DocCompoundLine MapBorderCompound(BorderValues? value)
    {
        if (value is null)
        {
            return DocCompoundLine.Single;
        }

        if (value == BorderValues.Double || value == BorderValues.DoubleWave)
        {
            return DocCompoundLine.Double;
        }

        if (value == BorderValues.Triple
            || value == BorderValues.ThinThickThinSmallGap
            || value == BorderValues.ThinThickThinMediumGap
            || value == BorderValues.ThinThickThinLargeGap)
        {
            return DocCompoundLine.Triple;
        }

        if (value == BorderValues.ThickThinSmallGap
            || value == BorderValues.ThickThinMediumGap
            || value == BorderValues.ThickThinLargeGap)
        {
            return DocCompoundLine.ThickThin;
        }

        if (value == BorderValues.ThinThickSmallGap
            || value == BorderValues.ThinThickMediumGap
            || value == BorderValues.ThinThickLargeGap)
        {
            return DocCompoundLine.ThinThick;
        }

        return DocCompoundLine.Single;
    }

    private static float? MapBorderCompoundSpacing(BorderValues? value, float thickness)
    {
        if (value is null || thickness <= 0f)
        {
            return null;
        }

        if (value == BorderValues.ThickThinSmallGap
            || value == BorderValues.ThinThickSmallGap
            || value == BorderValues.ThinThickThinSmallGap)
        {
            return MathF.Max(0.5f, thickness * 0.5f);
        }

        if (value == BorderValues.ThickThinMediumGap
            || value == BorderValues.ThinThickMediumGap
            || value == BorderValues.ThinThickThinMediumGap)
        {
            return MathF.Max(0.5f, thickness);
        }

        if (value == BorderValues.ThickThinLargeGap
            || value == BorderValues.ThinThickLargeGap
            || value == BorderValues.ThinThickThinLargeGap)
        {
            return MathF.Max(0.5f, thickness * 1.5f);
        }

        return null;
    }

    private static DocColor ParseBorderColor(BorderType border, DocumentThemeColorMap? themeColors)
    {
        var value = border.Color?.Value;
        if (!string.IsNullOrWhiteSpace(value) && !value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(value, out var parsed))
            {
                return parsed;
            }
        }

        if (border.ThemeColor?.Value is ThemeColorValues themeValue
            && TryMapThemeColor(themeValue, out var mapped))
        {
            var baseColor = ResolveThemeColor(themeColors, mapped);
            var tint = TryParseHexByte(border.ThemeTint?.Value, out var parsedTint) ? parsedTint : (byte?)null;
            var shade = TryParseHexByte(border.ThemeShade?.Value, out var parsedShade) ? parsedShade : (byte?)null;
            return ApplyThemeTintShade(baseColor, tint, shade);
        }

        return new DocColor(0, 0, 0);
    }

    private static bool TryParseHighlightColor(HighlightColorValues value, out Vibe.Office.Primitives.DocColor color)
    {
        if (value == HighlightColorValues.Black)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 0, 0);
            return true;
        }

        if (value == HighlightColorValues.Blue)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 0, 255);
            return true;
        }

        if (value == HighlightColorValues.Cyan)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 255, 255);
            return true;
        }

        if (value == HighlightColorValues.Green)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 255, 0);
            return true;
        }

        if (value == HighlightColorValues.Magenta)
        {
            color = new Vibe.Office.Primitives.DocColor(255, 0, 255);
            return true;
        }

        if (value == HighlightColorValues.Red)
        {
            color = new Vibe.Office.Primitives.DocColor(255, 0, 0);
            return true;
        }

        if (value == HighlightColorValues.Yellow)
        {
            color = new Vibe.Office.Primitives.DocColor(255, 255, 0);
            return true;
        }

        if (value == HighlightColorValues.White)
        {
            color = new Vibe.Office.Primitives.DocColor(255, 255, 255);
            return true;
        }

        if (value == HighlightColorValues.DarkBlue)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 0, 128);
            return true;
        }

        if (value == HighlightColorValues.DarkCyan)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 128, 128);
            return true;
        }

        if (value == HighlightColorValues.DarkGreen)
        {
            color = new Vibe.Office.Primitives.DocColor(0, 128, 0);
            return true;
        }

        if (value == HighlightColorValues.DarkMagenta)
        {
            color = new Vibe.Office.Primitives.DocColor(128, 0, 128);
            return true;
        }

        if (value == HighlightColorValues.DarkRed)
        {
            color = new Vibe.Office.Primitives.DocColor(128, 0, 0);
            return true;
        }

        if (value == HighlightColorValues.DarkYellow)
        {
            color = new Vibe.Office.Primitives.DocColor(128, 128, 0);
            return true;
        }

        if (value == HighlightColorValues.LightGray)
        {
            color = new Vibe.Office.Primitives.DocColor(211, 211, 211);
            return true;
        }

        if (value == HighlightColorValues.DarkGray)
        {
            color = new Vibe.Office.Primitives.DocColor(169, 169, 169);
            return true;
        }

        color = default;
        return false;
    }

    private static DocUnderlineStyle MapUnderlineStyle(UnderlineValues? value)
    {
        if (!value.HasValue)
        {
            return DocUnderlineStyle.Single;
        }

        var underline = value.Value;
        if (underline == UnderlineValues.Words)
        {
            return DocUnderlineStyle.Words;
        }

        if (underline == UnderlineValues.Double)
        {
            return DocUnderlineStyle.Double;
        }

        if (underline == UnderlineValues.Thick)
        {
            return DocUnderlineStyle.Thick;
        }

        if (underline == UnderlineValues.Dotted)
        {
            return DocUnderlineStyle.Dotted;
        }

        if (underline == UnderlineValues.DottedHeavy)
        {
            return DocUnderlineStyle.DottedHeavy;
        }

        if (underline == UnderlineValues.Dash)
        {
            return DocUnderlineStyle.Dash;
        }

        if (underline == UnderlineValues.DashedHeavy)
        {
            return DocUnderlineStyle.DashedHeavy;
        }

        if (underline == UnderlineValues.DashLong)
        {
            return DocUnderlineStyle.DashLong;
        }

        if (underline == UnderlineValues.DashLongHeavy)
        {
            return DocUnderlineStyle.DashLongHeavy;
        }

        if (underline == UnderlineValues.DotDash)
        {
            return DocUnderlineStyle.DotDash;
        }

        if (underline == UnderlineValues.DashDotHeavy)
        {
            return DocUnderlineStyle.DashDotHeavy;
        }

        if (underline == UnderlineValues.DotDotDash)
        {
            return DocUnderlineStyle.DotDotDash;
        }

        if (underline == UnderlineValues.DashDotDotHeavy)
        {
            return DocUnderlineStyle.DashDotDotHeavy;
        }

        if (underline == UnderlineValues.Wave)
        {
            return DocUnderlineStyle.Wave;
        }

        if (underline == UnderlineValues.WavyHeavy)
        {
            return DocUnderlineStyle.WavyHeavy;
        }

        if (underline == UnderlineValues.WavyDouble)
        {
            return DocUnderlineStyle.WavyDouble;
        }

        if (underline == UnderlineValues.None)
        {
            return DocUnderlineStyle.None;
        }

        return DocUnderlineStyle.Single;
    }

    private static DocThemeFont MapThemeFont(ThemeFontValues value)
    {
        if (value == ThemeFontValues.MajorEastAsia)
        {
            return DocThemeFont.MajorEastAsia;
        }

        if (value == ThemeFontValues.MajorBidi)
        {
            return DocThemeFont.MajorBidi;
        }

        if (value == ThemeFontValues.MajorHighAnsi)
        {
            return DocThemeFont.MajorHighAnsi;
        }

        if (value == ThemeFontValues.MinorEastAsia)
        {
            return DocThemeFont.MinorEastAsia;
        }

        if (value == ThemeFontValues.MinorBidi)
        {
            return DocThemeFont.MinorBidi;
        }

        if (value == ThemeFontValues.MinorHighAnsi)
        {
            return DocThemeFont.MinorHighAnsi;
        }

        if (value == ThemeFontValues.MinorAscii)
        {
            return DocThemeFont.MinorAscii;
        }

        return DocThemeFont.MajorAscii;
    }

    private static DocLineSpacingRule MapLineSpacingRule(LineSpacingRuleValues rule)
    {
        if (rule == LineSpacingRuleValues.AtLeast)
        {
            return DocLineSpacingRule.AtLeast;
        }

        if (rule == LineSpacingRuleValues.Exact)
        {
            return DocLineSpacingRule.Exactly;
        }

        return DocLineSpacingRule.Auto;
    }

    private static TabAlignment MapTabAlignment(TabStopValues? value)
    {
        if (!value.HasValue)
        {
            return TabAlignment.Left;
        }

        var resolved = value.Value;
        if (resolved == TabStopValues.Center)
        {
            return TabAlignment.Center;
        }

        if (resolved == TabStopValues.Right)
        {
            return TabAlignment.Right;
        }

        if (resolved == TabStopValues.End)
        {
            return TabAlignment.Right;
        }

        if (resolved == TabStopValues.Start)
        {
            return TabAlignment.Left;
        }

        if (resolved == TabStopValues.Decimal)
        {
            return TabAlignment.Decimal;
        }

        return TabAlignment.Left;
    }

    private static TabLeader MapTabLeader(TabStopLeaderCharValues? value)
    {
        if (!value.HasValue)
        {
            return TabLeader.None;
        }

        var resolved = value.Value;
        if (resolved == TabStopLeaderCharValues.Dot)
        {
            return TabLeader.Dot;
        }

        if (resolved == TabStopLeaderCharValues.Hyphen)
        {
            return TabLeader.Hyphen;
        }

        if (resolved == TabStopLeaderCharValues.Underscore)
        {
            return TabLeader.Underscore;
        }

        return TabLeader.None;
    }

    private static DocVerticalPosition MapVerticalPosition(VerticalPositionValues value)
    {
        if (value == VerticalPositionValues.Superscript)
        {
            return DocVerticalPosition.Superscript;
        }

        if (value == VerticalPositionValues.Subscript)
        {
            return DocVerticalPosition.Subscript;
        }

        return DocVerticalPosition.Normal;
    }

    private static DocTextDirection MapTextDirection(TextDirectionValues value)
    {
        if (value == TextDirectionValues.TopToBottomRightToLeft || value == TextDirectionValues.TopToBottomRightToLeft2010)
        {
            return DocTextDirection.TopToBottomRightToLeft;
        }

        if (value == TextDirectionValues.BottomToTopLeftToRight || value == TextDirectionValues.BottomToTopLeftToRight2010)
        {
            return DocTextDirection.BottomToTopLeftToRight;
        }

        if (value == TextDirectionValues.LefttoRightTopToBottomRotated || value == TextDirectionValues.LeftToRightTopToBottomRotated2010)
        {
            return DocTextDirection.LeftToRightTopToBottomRotated;
        }

        if (value == TextDirectionValues.TopToBottomRightToLeftRotated || value == TextDirectionValues.TopToBottomRightToLeftRotated2010)
        {
            return DocTextDirection.TopToBottomRightToLeftRotated;
        }

        if (value == TextDirectionValues.TopToBottomLeftToRightRotated || value == TextDirectionValues.TopToBottomLeftToRightRotated2010)
        {
            return DocTextDirection.TopToBottomLeftToRightRotated;
        }

        return DocTextDirection.LeftToRightTopToBottom;
    }

    private static DocGridType MapDocGridType(DocGridValues value)
    {
        if (value == DocGridValues.Lines)
        {
            return DocGridType.Lines;
        }

        if (value == DocGridValues.LinesAndChars)
        {
            return DocGridType.LinesAndChars;
        }

        if (value == DocGridValues.SnapToChars)
        {
            return DocGridType.SnapToChars;
        }

        return DocGridType.Default;
    }

    private static PageBorderDisplay MapPageBorderDisplay(PageBorderDisplayValues value)
    {
        if (value == PageBorderDisplayValues.FirstPage)
        {
            return PageBorderDisplay.FirstPage;
        }

        if (value == PageBorderDisplayValues.NotFirstPage)
        {
            return PageBorderDisplay.ExceptFirstPage;
        }

        return PageBorderDisplay.AllPages;
    }

    private static PageBorderOffset MapPageBorderOffset(PageBorderOffsetValues value)
    {
        return value == PageBorderOffsetValues.Text
            ? PageBorderOffset.Text
            : PageBorderOffset.Page;
    }

    private static PageBorderZOrder MapPageBorderZOrder(PageBorderZOrderValues value)
    {
        return value == PageBorderZOrderValues.Front
            ? PageBorderZOrder.Front
            : PageBorderZOrder.Back;
    }

    private static LineNumberRestart MapLineNumberRestart(LineNumberRestartValues value)
    {
        if (value == LineNumberRestartValues.NewPage)
        {
            return LineNumberRestart.NewPage;
        }

        if (value == LineNumberRestartValues.NewSection)
        {
            return LineNumberRestart.NewSection;
        }

        return LineNumberRestart.Continuous;
    }

    private static PageNumberFormat MapPageNumberFormat(NumberFormatValues value)
    {
        if (value == NumberFormatValues.UpperRoman)
        {
            return PageNumberFormat.UpperRoman;
        }

        if (value == NumberFormatValues.LowerRoman)
        {
            return PageNumberFormat.LowerRoman;
        }

        if (value == NumberFormatValues.UpperLetter)
        {
            return PageNumberFormat.UpperLetter;
        }

        if (value == NumberFormatValues.LowerLetter)
        {
            return PageNumberFormat.LowerLetter;
        }

        return PageNumberFormat.Decimal;
    }

    private static readonly NumberFormatValues SymbolNumberFormat = new("symbol");

    private static NoteNumberFormat MapNoteNumberFormat(NumberFormatValues value)
    {
        if (value == NumberFormatValues.UpperRoman)
        {
            return NoteNumberFormat.UpperRoman;
        }

        if (value == NumberFormatValues.LowerRoman)
        {
            return NoteNumberFormat.LowerRoman;
        }

        if (value == NumberFormatValues.UpperLetter)
        {
            return NoteNumberFormat.UpperLetter;
        }

        if (value == NumberFormatValues.LowerLetter)
        {
            return NoteNumberFormat.LowerLetter;
        }

        if (IsSymbolNumberFormat(value))
        {
            return NoteNumberFormat.Symbol;
        }

        return NoteNumberFormat.Decimal;
    }

    private static bool IsSymbolNumberFormat(NumberFormatValues value)
    {
        return value == SymbolNumberFormat
            || string.Equals(value.ToString(), "symbol", StringComparison.OrdinalIgnoreCase);
    }

    private static NoteNumberRestart MapNoteNumberRestart(RestartNumberValues value)
    {
        if (value == RestartNumberValues.EachSection)
        {
            return NoteNumberRestart.EachSection;
        }

        if (value == RestartNumberValues.EachPage)
        {
            return NoteNumberRestart.EachPage;
        }

        return NoteNumberRestart.Continuous;
    }

    private static Vibe.Office.Documents.FootnotePosition MapFootnotePosition(FootnotePositionValues value)
    {
        if (value == FootnotePositionValues.BeneathText)
        {
            return Vibe.Office.Documents.FootnotePosition.BeneathText;
        }

        return Vibe.Office.Documents.FootnotePosition.PageBottom;
    }

    private static Vibe.Office.Documents.EndnotePosition MapEndnotePosition(EndnotePositionValues value)
    {
        if (value == EndnotePositionValues.SectionEnd)
        {
            return Vibe.Office.Documents.EndnotePosition.EndOfSection;
        }

        return Vibe.Office.Documents.EndnotePosition.EndOfDocument;
    }

    private static FloatingHorizontalReference MapFrameHorizontalReference(HorizontalAnchorValues value)
    {
        if (value == HorizontalAnchorValues.Page)
        {
            return FloatingHorizontalReference.Page;
        }

        if (value == HorizontalAnchorValues.Margin)
        {
            return FloatingHorizontalReference.Margin;
        }

        if (value == HorizontalAnchorValues.Text)
        {
            return FloatingHorizontalReference.Column;
        }

        return FloatingHorizontalReference.Margin;
    }

    private static FloatingVerticalReference MapFrameVerticalReference(VerticalAnchorValues value)
    {
        if (value == VerticalAnchorValues.Page)
        {
            return FloatingVerticalReference.Page;
        }

        if (value == VerticalAnchorValues.Margin)
        {
            return FloatingVerticalReference.Margin;
        }

        return FloatingVerticalReference.Paragraph;
    }

    private static FloatingHorizontalAlignment MapFrameHorizontalAlignment(HorizontalAlignmentValues value)
    {
        if (value == HorizontalAlignmentValues.Center)
        {
            return FloatingHorizontalAlignment.Center;
        }

        if (value == HorizontalAlignmentValues.Right)
        {
            return FloatingHorizontalAlignment.Right;
        }

        if (value == HorizontalAlignmentValues.Outside)
        {
            return FloatingHorizontalAlignment.Outside;
        }

        if (value == HorizontalAlignmentValues.Inside)
        {
            return FloatingHorizontalAlignment.Inside;
        }

        return FloatingHorizontalAlignment.Left;
    }

    private static FloatingVerticalAlignment MapFrameVerticalAlignment(VerticalAlignmentValues value)
    {
        if (value == VerticalAlignmentValues.Center)
        {
            return FloatingVerticalAlignment.Center;
        }

        if (value == VerticalAlignmentValues.Bottom)
        {
            return FloatingVerticalAlignment.Bottom;
        }

        if (value == VerticalAlignmentValues.Outside)
        {
            return FloatingVerticalAlignment.Outside;
        }

        if (value == VerticalAlignmentValues.Inside)
        {
            return FloatingVerticalAlignment.Inside;
        }

        return FloatingVerticalAlignment.Top;
    }

    private static FloatingWrapStyle MapFrameWrapStyle(TextWrappingValues value)
    {
        if (value == TextWrappingValues.Around || value == TextWrappingValues.Auto)
        {
            return FloatingWrapStyle.Square;
        }

        if (value == TextWrappingValues.Tight)
        {
            return FloatingWrapStyle.Tight;
        }

        if (value == TextWrappingValues.Through)
        {
            return FloatingWrapStyle.Through;
        }

        if (value == TextWrappingValues.NotBeside)
        {
            return FloatingWrapStyle.TopBottom;
        }

        return FloatingWrapStyle.None;
    }

    private static FloatingAnchor BuildTableFloatingAnchor(
        TablePositionProperties position,
        Vibe.Office.Documents.TableAlignment? alignment)
    {
        var horizontalReference = position.HorizontalAnchor?.Value is { } hAnchor
            ? MapFrameHorizontalReference(hAnchor)
            : FloatingHorizontalReference.Column;
        var verticalReference = position.VerticalAnchor?.Value is { } vAnchor
            ? MapFrameVerticalReference(vAnchor)
            : FloatingVerticalReference.Paragraph;
        var horizontalAlignment = position.TablePositionXAlignment?.Value is { } xAlignment
            ? MapFrameHorizontalAlignment(xAlignment)
            : FloatingHorizontalAlignment.None;
        var verticalAlignment = position.TablePositionYAlignment?.Value is { } yAlignment
            ? MapFrameVerticalAlignment(yAlignment)
            : FloatingVerticalAlignment.None;

        var left = position.LeftFromText?.Value is short leftValue ? TwipsToDip(leftValue) : 0f;
        var top = position.TopFromText?.Value is short topValue ? TwipsToDip(topValue) : 0f;
        var right = position.RightFromText?.Value is short rightValue ? TwipsToDip(rightValue) : 0f;
        var bottom = position.BottomFromText?.Value is short bottomValue ? TwipsToDip(bottomValue) : 0f;

        var anchor = new FloatingAnchor
        {
            HorizontalReference = horizontalReference,
            VerticalReference = verticalReference,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            OffsetX = position.TablePositionX?.Value is { } x ? TwipsToDip(x) : 0f,
            OffsetY = position.TablePositionY?.Value is { } y ? TwipsToDip(y) : 0f,
            WrapStyle = FloatingWrapStyle.Square,
            WrapSide = ResolveTableWrapSide(position, alignment, horizontalAlignment),
            Distance = new DocThickness(left, top, right, bottom)
        };

        return anchor;
    }

    private static FloatingWrapSide ResolveTableWrapSide(
        TablePositionProperties position,
        Vibe.Office.Documents.TableAlignment? alignment,
        FloatingHorizontalAlignment horizontalAlignment)
    {
        var hasLeft = position.LeftFromText?.Value is not null;
        var hasRight = position.RightFromText?.Value is not null;
        if (hasRight && !hasLeft)
        {
            return FloatingWrapSide.Right;
        }

        if (hasLeft && !hasRight)
        {
            return FloatingWrapSide.Left;
        }

        if (horizontalAlignment is FloatingHorizontalAlignment.Left or FloatingHorizontalAlignment.Inside)
        {
            return FloatingWrapSide.Right;
        }

        if (horizontalAlignment is FloatingHorizontalAlignment.Right or FloatingHorizontalAlignment.Outside)
        {
            return FloatingWrapSide.Left;
        }

        if (alignment == Vibe.Office.Documents.TableAlignment.Left)
        {
            return FloatingWrapSide.Right;
        }

        if (alignment == Vibe.Office.Documents.TableAlignment.Right)
        {
            return FloatingWrapSide.Left;
        }

        return FloatingWrapSide.Both;
    }

    private static EastAsianLayoutProperties? ParseEastAsianLayout(EastAsianLayout layout)
    {
        var properties = new EastAsianLayoutProperties
        {
            Id = layout.Id?.Value,
            Combine = layout.Combine?.Value,
            CombineBrackets = layout.CombineBrackets?.Value.ToString(),
            Vertical = layout.Vertical?.Value,
            VerticalCompress = layout.VerticalCompress?.Value
        };

        return properties.HasValues ? properties : null;
    }

    private static float HalfPointsToDip(float halfPoints)
    {
        var points = halfPoints / 2f;
        return points * 96f / 72f;
    }

    private static bool? ReadOnOffValue(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return element switch
        {
            PrimaryStyle primary => ReadOnOffOnly(primary.Val),
            SemiHidden semiHidden => ReadOnOffOnly(semiHidden.Val),
            UnhideWhenUsed unhide => ReadOnOffOnly(unhide.Val),
            AutoRedefine autoRedefine => ReadOnOffOnly(autoRedefine.Val),
            Locked locked => ReadOnOffOnly(locked.Val),
            Hidden hidden => hidden.Val?.Value ?? true,
            _ => true
        };
    }

    private static bool ReadOnOffOnly(EnumValue<OnOffOnlyValues>? value)
    {
        if (value is null)
        {
            return true;
        }

        return value.Value != OnOffOnlyValues.Off;
    }

    private static bool TryParseHexColor(string value, out Vibe.Office.Primitives.DocColor color)
    {
        color = Vibe.Office.Primitives.DocColor.Black;
        if (value.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var packed))
        {
            return false;
        }

        var r = (byte)((packed >> 16) & 0xFF);
        var g = (byte)((packed >> 8) & 0xFF);
        var b = (byte)(packed & 0xFF);
        color = new Vibe.Office.Primitives.DocColor(r, g, b);
        return true;
    }

    private static bool TryParseHexByte(string? value, out byte result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryMapThemeColor(ThemeColorValues value, out DocThemeColor themeColor)
    {
        if (value == ThemeColorValues.Dark1 || value == ThemeColorValues.Text1)
        {
            themeColor = DocThemeColor.Dark1;
            return true;
        }

        if (value == ThemeColorValues.Light1 || value == ThemeColorValues.Background1)
        {
            themeColor = DocThemeColor.Light1;
            return true;
        }

        if (value == ThemeColorValues.Dark2 || value == ThemeColorValues.Text2)
        {
            themeColor = DocThemeColor.Dark2;
            return true;
        }

        if (value == ThemeColorValues.Light2 || value == ThemeColorValues.Background2)
        {
            themeColor = DocThemeColor.Light2;
            return true;
        }

        if (value == ThemeColorValues.Accent1)
        {
            themeColor = DocThemeColor.Accent1;
            return true;
        }

        if (value == ThemeColorValues.Accent2)
        {
            themeColor = DocThemeColor.Accent2;
            return true;
        }

        if (value == ThemeColorValues.Accent3)
        {
            themeColor = DocThemeColor.Accent3;
            return true;
        }

        if (value == ThemeColorValues.Accent4)
        {
            themeColor = DocThemeColor.Accent4;
            return true;
        }

        if (value == ThemeColorValues.Accent5)
        {
            themeColor = DocThemeColor.Accent5;
            return true;
        }

        if (value == ThemeColorValues.Accent6)
        {
            themeColor = DocThemeColor.Accent6;
            return true;
        }

        if (value == ThemeColorValues.Hyperlink)
        {
            themeColor = DocThemeColor.Hyperlink;
            return true;
        }

        if (value == ThemeColorValues.FollowedHyperlink)
        {
            themeColor = DocThemeColor.FollowedHyperlink;
            return true;
        }

        themeColor = default;
        return false;
    }

    private static DocColor ApplyThemeTintShade(DocColor baseColor, byte? tint, byte? shade)
    {
        var r = (float)baseColor.R;
        var g = (float)baseColor.G;
        var b = (float)baseColor.B;

        if (shade.HasValue)
        {
            var factor = shade.Value / 255f;
            r *= factor;
            g *= factor;
            b *= factor;
        }

        if (tint.HasValue)
        {
            var factor = tint.Value / 255f;
            r += (255f - r) * factor;
            g += (255f - g) * factor;
            b += (255f - b) * factor;
        }

        return new DocColor(ClampToByte(r), ClampToByte(g), ClampToByte(b), baseColor.A);
    }

    private static bool TryResolveShadingColor(Shading shading, DocumentThemeColorMap? themeColors, out DocColor color)
    {
        color = DocColor.Black;
        if (shading.Fill?.Value is string fill && TryParseHexColor(fill, out var parsed))
        {
            color = parsed;
            return true;
        }

        if (shading.ThemeFill?.Value is ThemeColorValues themeFill
            && TryMapThemeColor(themeFill, out var mapped))
        {
            var baseColor = ResolveThemeColor(themeColors, mapped);
            var tint = TryParseHexByte(shading.ThemeFillTint?.Value, out var parsedTint) ? parsedTint : (byte?)null;
            var shade = TryParseHexByte(shading.ThemeFillShade?.Value, out var parsedShade) ? parsedShade : (byte?)null;
            color = ApplyThemeTintShade(baseColor, tint, shade);
            return true;
        }

        return false;
    }

    private static bool TryParseHalfPoints(string? value, out float dipValue)
    {
        dipValue = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints))
        {
            return false;
        }

        dipValue = HalfPointsToDip(halfPoints);
        return true;
    }

    private static string ParseSymbolChar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code))
        {
            return string.Empty;
        }

        return code <= 0xFFFF ? new string((char)code, 1) : char.ConvertFromUtf32(code);
    }

    private static bool IsMathContainer(OpenXmlElement element)
    {
        if (element is null)
        {
            return false;
        }

        var localName = element.LocalName;
        if (!string.Equals(localName, "oMath", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(localName, "oMathPara", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsMathNamespace(element) || string.Equals(element.NamespaceUri, "http://schemas.openxmlformats.org/wordprocessingml/2006/main", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMathNamespace(OpenXmlElement element)
    {
        var ns = element.NamespaceUri;
        if (string.IsNullOrWhiteSpace(ns))
        {
            return false;
        }

        return ns.Contains("/math", StringComparison.OrdinalIgnoreCase)
               || ns.Contains("omml", StringComparison.OrdinalIgnoreCase);
    }

    private static MathElement? ParseMathContainer(OpenXmlElement element)
    {
        if (element is null)
        {
            return null;
        }

        var elements = new List<MathElement>();
        foreach (var child in element.Elements())
        {
            if (!IsMathNamespace(child))
            {
                continue;
            }

            var parsed = ParseMathElement(child);
            if (parsed is not null)
            {
                elements.Add(parsed);
            }
        }

        if (elements.Count == 1)
        {
            return elements[0];
        }

        if (elements.Count > 1)
        {
            var row = new MathRow();
            row.Elements.AddRange(elements);
            return row;
        }

        var fallbackText = CollectMathText(element);
        if (string.IsNullOrWhiteSpace(fallbackText))
        {
            return null;
        }

        return new MathRun { Text = fallbackText };
    }

    private static MathElement? ParseMathElement(OpenXmlElement element)
    {
        return element.LocalName switch
        {
            "oMath" => ParseMathContainer(element),
            "oMathPara" => ParseMathContainer(element),
            "row" => ParseMathGroup(element),
            "r" => ParseMathRun(element),
            "f" => ParseMathFraction(element),
            "acc" => ParseMathAccent(element),
            "d" => ParseMathDelimiter(element),
            "bar" => ParseMathBar(element),
            "box" => ParseMathBox(element),
            "borderBox" => ParseMathBorderBox(element),
            "func" => ParseMathFunction(element),
            "groupChr" => ParseMathGroupChar(element),
            "limLow" => ParseMathLimit(element, MathLimitPosition.Lower),
            "limUpp" => ParseMathLimit(element, MathLimitPosition.Upper),
            "nary" => ParseMathNary(element),
            "m" => ParseMathMatrix(element),
            "mr" => ParseMathMatrixRow(element),
            "sSup" => ParseMathScript(element),
            "sSub" => ParseMathScript(element),
            "sSubSup" => ParseMathScript(element),
            "sPre" => ParseMathPreScript(element),
            "rad" => ParseMathRadical(element),
            "phant" => ParseMathPhantom(element),
            "e" => ParseMathGroup(element),
            "num" => ParseMathGroup(element),
            "den" => ParseMathGroup(element),
            "sub" => ParseMathGroup(element),
            "sup" => ParseMathGroup(element),
            "deg" => ParseMathGroup(element),
            _ => ParseMathGroup(element)
        };
    }

    private static MathElement ParseMathGroup(OpenXmlElement element)
    {
        var elements = new List<MathElement>();
        foreach (var child in element.Elements())
        {
            if (!IsMathNamespace(child))
            {
                continue;
            }

            var parsed = ParseMathElement(child);
            if (parsed is not null)
            {
                elements.Add(parsed);
            }
        }

        if (elements.Count == 1)
        {
            return elements[0];
        }

        if (elements.Count > 1)
        {
            var row = new MathRow();
            row.Elements.AddRange(elements);
            return row;
        }

        var text = CollectMathText(element);
        return new MathRun { Text = text };
    }

    private static MathElement ParseMathRun(OpenXmlElement element)
    {
        TextStyleProperties? mathStyle = null;
        TextStyleProperties? wordStyle = null;
        var builder = new StringBuilder();

        foreach (var child in element.Elements())
        {
            if (string.Equals(child.LocalName, "rPr", StringComparison.OrdinalIgnoreCase))
            {
                if (IsMathNamespace(child))
                {
                    mathStyle = ExtractMathRunStyleProperties(child);
                }
                else
                {
                    wordStyle = ExtractRunStyleProperties(child);
                }

                continue;
            }

            if (string.Equals(child.LocalName, "t", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(child.InnerText);
            }
        }

        var text = builder.ToString();
        var style = MergeRunStyleProperties(wordStyle, mathStyle);
        return new MathRun { Text = text, Style = style };
    }

    private static MathElement ParseMathFraction(OpenXmlElement element)
    {
        var hasBar = true;
        var properties = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "fPr", StringComparison.OrdinalIgnoreCase));
        if (properties is not null)
        {
            var typeElement = properties.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "type", StringComparison.OrdinalIgnoreCase));
            var typeValue = typeElement is not null ? GetAttributeValue(typeElement, "val", typeElement.NamespaceUri) : null;
            if (!string.IsNullOrWhiteSpace(typeValue)
                && typeValue.Equals("noBar", StringComparison.OrdinalIgnoreCase))
            {
                hasBar = false;
            }
        }

        var numeratorElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "num", StringComparison.OrdinalIgnoreCase));
        var denominatorElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "den", StringComparison.OrdinalIgnoreCase));
        var numerator = numeratorElement is not null ? ParseMathGroup(numeratorElement) : new MathRun();
        var denominator = denominatorElement is not null ? ParseMathGroup(denominatorElement) : new MathRun();
        return new MathFraction(numerator, denominator) { HasBar = hasBar };
    }

    private static MathElement ParseMathScript(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var subElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sub", StringComparison.OrdinalIgnoreCase));
        var supElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sup", StringComparison.OrdinalIgnoreCase));

        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var script = new MathScript(baseValue)
        {
            Subscript = subElement is not null ? ParseMathGroup(subElement) : null,
            Superscript = supElement is not null ? ParseMathGroup(supElement) : null
        };

        return script;
    }

    private static MathElement ParseMathRadical(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var degreeElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "deg", StringComparison.OrdinalIgnoreCase));
        var radicand = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var radical = new MathRadical(radicand)
        {
            Degree = degreeElement is not null ? ParseMathGroup(degreeElement) : null
        };

        return radical;
    }

    private static MathElement ParseMathAccent(OpenXmlElement element)
    {
        var accentChar = string.Empty;
        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "accPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            var charElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "chr", StringComparison.OrdinalIgnoreCase));
            accentChar = charElement is not null ? GetAttributeValue(charElement, "val", charElement.NamespaceUri) ?? string.Empty : string.Empty;
        }

        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var accent = new MathAccent(baseValue);
        if (!string.IsNullOrWhiteSpace(accentChar))
        {
            accent.AccentChar = accentChar;
        }

        return accent;
    }

    private static MathElement ParseMathDelimiter(OpenXmlElement element)
    {
        string? beginChar = null;
        string? endChar = null;
        string? separatorChar = null;
        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "dPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            var beginElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "begChr", StringComparison.OrdinalIgnoreCase));
            var endElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "endChr", StringComparison.OrdinalIgnoreCase));
            var sepElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sepChr", StringComparison.OrdinalIgnoreCase));
            beginChar = beginElement is not null ? GetAttributeValue(beginElement, "val", beginElement.NamespaceUri) : null;
            endChar = endElement is not null ? GetAttributeValue(endElement, "val", endElement.NamespaceUri) : null;
            separatorChar = sepElement is not null ? GetAttributeValue(sepElement, "val", sepElement.NamespaceUri) : null;
        }

        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var body = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var delimiter = new MathDelimiter(body)
        {
            BeginChar = beginChar,
            EndChar = endChar,
            SeparatorChar = separatorChar
        };

        return delimiter;
    }

    private static MathElement ParseMathBar(OpenXmlElement element)
    {
        var position = MathBarPosition.Top;
        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "barPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            var posElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "pos", StringComparison.OrdinalIgnoreCase));
            var value = posElement is not null ? GetAttributeValue(posElement, "val", posElement.NamespaceUri) : null;
            if (!string.IsNullOrWhiteSpace(value)
                && (value.Equals("bot", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("bottom", StringComparison.OrdinalIgnoreCase)))
            {
                position = MathBarPosition.Bottom;
            }
        }

        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        return new MathBar(baseValue) { Position = position };
    }

    private static MathElement ParseMathBox(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        return new MathBoxElement(baseValue);
    }

    private static MathElement ParseMathBorderBox(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var border = new MathBorderBox(baseValue);

        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "borderBoxPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            border.HideTop = props.Elements().Any(child => string.Equals(child.LocalName, "hideTop", StringComparison.OrdinalIgnoreCase));
            border.HideBottom = props.Elements().Any(child => string.Equals(child.LocalName, "hideBot", StringComparison.OrdinalIgnoreCase));
            border.HideLeft = props.Elements().Any(child => string.Equals(child.LocalName, "hideLeft", StringComparison.OrdinalIgnoreCase));
            border.HideRight = props.Elements().Any(child => string.Equals(child.LocalName, "hideRight", StringComparison.OrdinalIgnoreCase));
            border.StrikeHorizontal = props.Elements().Any(child => string.Equals(child.LocalName, "strikeH", StringComparison.OrdinalIgnoreCase));
            border.StrikeVertical = props.Elements().Any(child => string.Equals(child.LocalName, "strikeV", StringComparison.OrdinalIgnoreCase));
            border.StrikeDiagonalUp = props.Elements().Any(child => string.Equals(child.LocalName, "strikeBLTR", StringComparison.OrdinalIgnoreCase));
            border.StrikeDiagonalDown = props.Elements().Any(child => string.Equals(child.LocalName, "strikeTLBR", StringComparison.OrdinalIgnoreCase));
        }

        return border;
    }

    private static MathElement ParseMathFunction(OpenXmlElement element)
    {
        var nameElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "fName", StringComparison.OrdinalIgnoreCase));
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var nameValue = nameElement is not null ? ParseMathGroup(nameElement) : new MathRun { Text = "f" };
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        return new MathFunction(nameValue, baseValue);
    }

    private static MathElement ParseMathGroupChar(OpenXmlElement element)
    {
        var character = string.Empty;
        var position = MathGroupCharacterPosition.Top;
        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "groupChrPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            var charElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "chr", StringComparison.OrdinalIgnoreCase));
            character = charElement is not null ? GetAttributeValue(charElement, "val", charElement.NamespaceUri) ?? string.Empty : string.Empty;

            var posElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "pos", StringComparison.OrdinalIgnoreCase));
            var value = posElement is not null ? GetAttributeValue(posElement, "val", posElement.NamespaceUri) : null;
            if (!string.IsNullOrWhiteSpace(value)
                && (value.Equals("bot", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("bottom", StringComparison.OrdinalIgnoreCase)))
            {
                position = MathGroupCharacterPosition.Bottom;
            }
        }

        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var group = new MathGroupCharacter(baseValue)
        {
            Position = position
        };

        if (!string.IsNullOrWhiteSpace(character))
        {
            group.Character = character;
        }

        return group;
    }

    private static MathElement ParseMathLimit(OpenXmlElement element, MathLimitPosition position)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var limitElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "lim", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var limitValue = limitElement is not null ? ParseMathGroup(limitElement) : new MathRun();
        return new MathLimit(baseValue, limitValue) { Position = position };
    }

    private static MathElement ParseMathPreScript(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var subElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sub", StringComparison.OrdinalIgnoreCase));
        var supElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sup", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();

        var script = new MathPreScript(baseValue)
        {
            Subscript = subElement is not null ? ParseMathGroup(subElement) : null,
            Superscript = supElement is not null ? ParseMathGroup(supElement) : null
        };

        return script;
    }

    private static MathElement ParseMathPhantom(OpenXmlElement element)
    {
        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();
        var phantom = new MathPhantom(baseValue);

        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "phantPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            phantom.Show = props.Elements().Any(child => string.Equals(child.LocalName, "show", StringComparison.OrdinalIgnoreCase));
            phantom.ZeroWidth = props.Elements().Any(child => string.Equals(child.LocalName, "zeroWid", StringComparison.OrdinalIgnoreCase));
            phantom.ZeroAscent = props.Elements().Any(child => string.Equals(child.LocalName, "zeroAsc", StringComparison.OrdinalIgnoreCase));
            phantom.ZeroDescent = props.Elements().Any(child => string.Equals(child.LocalName, "zeroDesc", StringComparison.OrdinalIgnoreCase));
            phantom.Transparent = props.Elements().Any(child => string.Equals(child.LocalName, "trans", StringComparison.OrdinalIgnoreCase));
        }

        return phantom;
    }

    private static MathElement ParseMathNary(OpenXmlElement element)
    {
        string? operatorChar = null;
        var hideSub = false;
        var hideSup = false;
        var props = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "naryPr", StringComparison.OrdinalIgnoreCase));
        if (props is not null)
        {
            var charElement = props.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "chr", StringComparison.OrdinalIgnoreCase));
            operatorChar = charElement is not null ? GetAttributeValue(charElement, "val", charElement.NamespaceUri) : null;
            hideSub = props.Elements().Any(child => string.Equals(child.LocalName, "subHide", StringComparison.OrdinalIgnoreCase));
            hideSup = props.Elements().Any(child => string.Equals(child.LocalName, "supHide", StringComparison.OrdinalIgnoreCase));
        }

        var baseElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase));
        var subElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sub", StringComparison.OrdinalIgnoreCase));
        var supElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "sup", StringComparison.OrdinalIgnoreCase));
        var baseValue = baseElement is not null ? ParseMathGroup(baseElement) : new MathRun();

        var nary = new MathNary(baseValue)
        {
            Subscript = subElement is not null ? ParseMathGroup(subElement) : null,
            Superscript = supElement is not null ? ParseMathGroup(supElement) : null,
            HideSub = hideSub,
            HideSup = hideSup
        };

        if (!string.IsNullOrWhiteSpace(operatorChar))
        {
            nary.OperatorChar = operatorChar;
        }

        return nary;
    }

    private static MathElement ParseMathMatrix(OpenXmlElement element)
    {
        var rows = new List<List<MathElement>>();
        foreach (var rowElement in element.Elements().Where(child => string.Equals(child.LocalName, "mr", StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add(ParseMathMatrixRowElements(rowElement));
        }

        if (rows.Count == 0)
        {
            return ParseMathGroup(element);
        }

        return new MathMatrix(rows);
    }

    private static MathElement ParseMathMatrixRow(OpenXmlElement element)
    {
        var row = new MathRow();
        row.Elements.AddRange(ParseMathMatrixRowElements(element));
        return row;
    }

    private static List<MathElement> ParseMathMatrixRowElements(OpenXmlElement rowElement)
    {
        var cells = new List<MathElement>();
        foreach (var cellElement in rowElement.Elements().Where(child => string.Equals(child.LocalName, "e", StringComparison.OrdinalIgnoreCase)))
        {
            cells.Add(ParseMathGroup(cellElement));
        }

        if (cells.Count == 0)
        {
            cells.Add(ParseMathGroup(rowElement));
        }

        return cells;
    }

    private static TextStyleProperties? ExtractMathRunStyleProperties(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var style = new TextStyleProperties();
        foreach (var child in properties.Elements())
        {
            if (string.Equals(child.LocalName, "sty", StringComparison.OrdinalIgnoreCase))
            {
                var value = GetAttributeValue(child, "val", child.NamespaceUri) ?? GetAttributeValue(child, "val");
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var span = value.AsSpan().Trim();
                if (span.Length == 0)
                {
                    continue;
                }

                if (span.Equals("b", StringComparison.OrdinalIgnoreCase))
                {
                    style.FontWeight = DocFontWeight.Bold;
                    style.FontStyle = DocFontStyle.Normal;
                }
                else if (span.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    style.FontWeight = DocFontWeight.Normal;
                    style.FontStyle = DocFontStyle.Italic;
                }
                else if (span.Equals("bi", StringComparison.OrdinalIgnoreCase))
                {
                    style.FontWeight = DocFontWeight.Bold;
                    style.FontStyle = DocFontStyle.Italic;
                }
                else if (span.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    style.FontWeight = DocFontWeight.Normal;
                    style.FontStyle = DocFontStyle.Normal;
                }
            }
            else if (string.Equals(child.LocalName, "nor", StringComparison.OrdinalIgnoreCase))
            {
                style.FontWeight = DocFontWeight.Normal;
                style.FontStyle = DocFontStyle.Normal;
            }
        }

        return style.HasValues ? style : null;
    }

    private static TextStyleProperties? MergeRunStyleProperties(TextStyleProperties? baseStyle, TextStyleProperties? overrides)
    {
        if (baseStyle is null)
        {
            return overrides?.Clone();
        }

        if (overrides is null)
        {
            return baseStyle;
        }

        var result = baseStyle.Clone();
        ApplyRunStyleOverrides(result, overrides);
        return result;
    }

    private static void ApplyRunStyleOverrides(TextStyleProperties target, TextStyleProperties overrides)
    {
        if (!string.IsNullOrWhiteSpace(overrides.FontFamily))
        {
            target.FontFamily = overrides.FontFamily;
        }

        if (overrides.FontSize.HasValue)
        {
            target.FontSize = overrides.FontSize;
        }

        if (overrides.FontWeight.HasValue)
        {
            target.FontWeight = overrides.FontWeight;
        }

        if (overrides.FontStyle.HasValue)
        {
            target.FontStyle = overrides.FontStyle;
        }

        if (!string.IsNullOrWhiteSpace(overrides.FontFamilyAscii))
        {
            target.FontFamilyAscii = overrides.FontFamilyAscii;
        }

        if (!string.IsNullOrWhiteSpace(overrides.FontFamilyHighAnsi))
        {
            target.FontFamilyHighAnsi = overrides.FontFamilyHighAnsi;
        }

        if (!string.IsNullOrWhiteSpace(overrides.FontFamilyEastAsia))
        {
            target.FontFamilyEastAsia = overrides.FontFamilyEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(overrides.FontFamilyComplexScript))
        {
            target.FontFamilyComplexScript = overrides.FontFamilyComplexScript;
        }

        if (overrides.Color.HasValue)
        {
            target.Color = overrides.Color;
        }

        if (overrides.FontSizeComplexScript.HasValue)
        {
            target.FontSizeComplexScript = overrides.FontSizeComplexScript;
        }

        if (overrides.VerticalPosition.HasValue)
        {
            target.VerticalPosition = overrides.VerticalPosition;
        }

        if (overrides.SmallCaps.HasValue)
        {
            target.SmallCaps = overrides.SmallCaps;
        }

        if (overrides.Underline.HasValue)
        {
            target.Underline = overrides.Underline;
        }

        if (overrides.UnderlineStyle.HasValue)
        {
            target.UnderlineStyle = overrides.UnderlineStyle;
        }

        if (overrides.UnderlineColor.HasValue)
        {
            target.UnderlineColor = overrides.UnderlineColor;
        }

        if (overrides.Strikethrough.HasValue)
        {
            target.Strikethrough = overrides.Strikethrough;
        }

        if (overrides.HighlightColor.HasValue)
        {
            target.HighlightColor = overrides.HighlightColor;
        }

        if (overrides.ThemeFontAscii.HasValue)
        {
            target.ThemeFontAscii = overrides.ThemeFontAscii;
        }

        if (overrides.ThemeFontHighAnsi.HasValue)
        {
            target.ThemeFontHighAnsi = overrides.ThemeFontHighAnsi;
        }

        if (overrides.ThemeFontEastAsia.HasValue)
        {
            target.ThemeFontEastAsia = overrides.ThemeFontEastAsia;
        }

        if (overrides.ThemeFontComplexScript.HasValue)
        {
            target.ThemeFontComplexScript = overrides.ThemeFontComplexScript;
        }

        if (!string.IsNullOrWhiteSpace(overrides.Language))
        {
            target.Language = overrides.Language;
        }

        if (!string.IsNullOrWhiteSpace(overrides.LanguageEastAsia))
        {
            target.LanguageEastAsia = overrides.LanguageEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(overrides.LanguageBidi))
        {
            target.LanguageBidi = overrides.LanguageBidi;
        }

        if (overrides.Effects?.HasValues == true)
        {
            target.Effects ??= new TextEffects();
            target.Effects.ApplyOverrides(overrides.Effects);
        }

        if (overrides.OpenTypeFeatures?.HasValues == true)
        {
            target.OpenTypeFeatures ??= new TextOpenTypeFeatures();
            target.OpenTypeFeatures.ApplyOverrides(overrides.OpenTypeFeatures);
        }
    }

    private static string CollectMathText(OpenXmlElement element)
    {
        var builder = new StringBuilder();
        foreach (var child in element.Descendants())
        {
            if (!string.Equals(child.LocalName, "t", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(child.InnerText);
        }

        return builder.ToString();
    }

    private static string? GetAttributeValue(OpenXmlElement element, string localName, string? namespaceUri = null)
    {
        foreach (var attribute in element.GetAttributes())
        {
            if (!string.Equals(attribute.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (namespaceUri is null
                || string.Equals(attribute.NamespaceUri, namespaceUri, StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static byte[] ReadPartData(OpenXmlPart part)
    {
        using var stream = part.GetStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static RevisionInfo ResolveRevision(OpenXmlElement element, RevisionKind kind, DocumentRevisions revisions)
    {
        var info = new RevisionInfo
        {
            Kind = kind,
            Author = GetAttributeValue(element, "author", WordprocessingNamespace),
            Name = GetAttributeValue(element, "name", WordprocessingNamespace)
        };

        var idText = GetAttributeValue(element, "id", WordprocessingNamespace);
        if (idText is not null && int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            info.Id = id;
        }

        var dateText = GetAttributeValue(element, "date", WordprocessingNamespace)
                       ?? GetAttributeValue(element, "dateUtc", Wordprocessing16DuNamespace);
        info.Date = ParseRevisionDate(dateText);

        return revisions.AddOrUpdate(info);
    }

    private static DateTimeOffset? ParseRevisionDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static Inline? TryCreateInlineObject(
        Drawing drawing,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var graphicData = drawing.Descendants<A.GraphicData>().FirstOrDefault();
        var uri = graphicData?.Uri?.Value ?? string.Empty;
        var diagramInfo = TryResolveDiagramInfo(drawing, graphicData, uri, imageResolver.Part);
        if (uri.Contains("drawingml/chart", StringComparison.OrdinalIgnoreCase) || uri.Contains("drawingml/2006/chart", StringComparison.OrdinalIgnoreCase) || uri.Contains("chart", StringComparison.OrdinalIgnoreCase))
        {
            var chartInline = chartResolver.TryCreateChart(drawing);
            if (chartInline is not null)
            {
                return chartInline;
            }
        }

        if (uri.Contains("wordprocessingShape", StringComparison.OrdinalIgnoreCase))
        {
            var shape = graphicData?.GetFirstChild<Wps.WordprocessingShape>()
                       ?? drawing.Descendants<Wps.WordprocessingShape>().FirstOrDefault();
            if (shape is not null)
            {
                var inline = TryCreateWordprocessingShape(drawing, shape, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
                if (inline is not null)
                {
                    return inline;
                }
            }
        }

        var embeddedInfo = TryResolveEmbeddedObjectInfo(drawing, imageResolver.Part);
        if (embeddedInfo is not null)
        {
            var preview = imageResolver.TryCreateImage(drawing);
            if (preview is not null)
            {
                preview.EmbeddedObject = embeddedInfo;
                return preview;
            }

            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 100f;
            var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 100f;
            if (string.IsNullOrWhiteSpace(embeddedInfo.ContentType))
            {
                embeddedInfo.ContentType = OleObjectContentType;
            }

            var contentType = embeddedInfo.ContentType!;
            var placeholder = new ImageInline(Array.Empty<byte>(), width, height, contentType)
            {
                EmbeddedObject = embeddedInfo
            };
            return placeholder;
        }

        var imageInline = imageResolver.TryCreateImage(drawing);
        if (imageInline is not null && diagramInfo is not null)
        {
            imageInline.Diagram = diagramInfo;
        }

        return imageInline;
    }

    private static Inline? TryCreateInlineObjectFromVml(
        OpenXmlElement element,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var embeddedInfo = TryResolveEmbeddedObjectInfo(element, imageResolver.Part);
        var hasImageData = element.Descendants()
            .Any(node => node.LocalName.Equals("imagedata", StringComparison.OrdinalIgnoreCase));
        if (hasImageData)
        {
            var imageInline = imageResolver.TryCreateImageFromVml(element);
            if (imageInline is not null)
            {
                if (embeddedInfo is not null)
                {
                    imageInline.EmbeddedObject = embeddedInfo;
                }

                return imageInline;
            }
        }

        if (embeddedInfo is not null)
        {
            var (placeholderWidth, placeholderHeight) = GetVmlSize(element);
            if (string.IsNullOrWhiteSpace(embeddedInfo.ContentType))
            {
                embeddedInfo.ContentType = OleObjectContentType;
            }

            var contentType = embeddedInfo.ContentType!;
            return new ImageInline(Array.Empty<byte>(), placeholderWidth, placeholderHeight, contentType)
            {
                EmbeddedObject = embeddedInfo
            };
        }

        var shapeElement = FindVmlShapeElement(element);
        if (shapeElement is null)
        {
            return null;
        }

        var (width, height) = GetVmlSize(element);
        var shapeInline = new ShapeInline(width, height)
        {
            Name = GetAttributeValue(shapeElement, "id")
        };

        var shapeTypeElement = ResolveVmlShapeType(shapeElement, element);
        shapeInline.Properties.PresetGeometry = ResolveVmlPresetGeometry(shapeElement, shapeTypeElement);

        var styleValue = GetAttributeValue(shapeElement, "style") ?? string.Empty;
        var shapeStyle = ParseVmlStyle(styleValue);
        ApplyVmlTransform(shapeInline.Properties, shapeElement, shapeStyle);

        ApplyVmlFill(shapeInline.Properties, shapeElement, shapeTypeElement);
        ApplyVmlStroke(shapeInline.Properties, shapeElement, shapeTypeElement);

        var textBoxContent = element.Descendants<TextBoxContent>().FirstOrDefault();
        var textBoxElement = element.Descendants()
            .FirstOrDefault(node => node.LocalName.Equals("textbox", StringComparison.OrdinalIgnoreCase));
        if (textBoxContent is not null)
        {
            var blocks = ParseTextBoxContent(textBoxContent, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            var textBox = new ShapeTextBox();
            textBox.Blocks.AddRange(blocks);
            ApplyVmlTextBoxProperties(textBox.Properties, textBoxElement, shapeStyle);
            shapeInline.TextBox = textBox;
        }

        return shapeInline;
    }

    private static EmbeddedObjectInfo? TryResolveEmbeddedObjectInfo(OpenXmlElement element, OpenXmlPart? part)
    {
        var oleElement = FindEmbeddedObjectElement(element);
        if (oleElement is null)
        {
            return null;
        }

        var info = new EmbeddedObjectInfo
        {
            RelationshipId = GetAttributeValue(oleElement, "id", RelationshipNamespace),
            ProgId = GetAttributeValue(oleElement, "ProgID") ?? GetAttributeValue(oleElement, "ProgId"),
            ClassId = GetAttributeValue(oleElement, "ClassID") ?? GetAttributeValue(oleElement, "ClassId"),
            ObjectId = GetAttributeValue(oleElement, "ObjectID") ?? GetAttributeValue(oleElement, "ObjectId"),
            UpdateMode = GetAttributeValue(oleElement, "UpdateMode")
        };

        var typeValue = GetAttributeValue(oleElement, "Type");
        if (!string.IsNullOrWhiteSpace(typeValue))
        {
            info.IsLinked = typeValue.Equals("Link", StringComparison.OrdinalIgnoreCase);
        }

        var relId = info.RelationshipId;
        if (part is null || string.IsNullOrWhiteSpace(relId))
        {
            return info;
        }

        if (part.GetPartById(relId) is OpenXmlPart embeddedPart)
        {
            info.ContentType = embeddedPart.ContentType;
            info.TargetUri = embeddedPart.Uri?.ToString();
            info.Data = ReadPartData(embeddedPart);
            return info;
        }

        var external = part.ExternalRelationships.FirstOrDefault(item => item.Id == relId);
        if (external is not null)
        {
            info.TargetUri = external.Uri?.ToString();
        }

        return info;
    }

    private static DiagramInfo? TryResolveDiagramInfo(Drawing drawing, A.GraphicData? graphicData, string uri, OpenXmlPart? part)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.Contains("diagram", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relIds = graphicData?.Descendants().FirstOrDefault(element => element.LocalName.Equals("relIds", StringComparison.OrdinalIgnoreCase))
                     ?? drawing.Descendants().FirstOrDefault(element =>
                         element.LocalName.Equals("relIds", StringComparison.OrdinalIgnoreCase)
                         && element.NamespaceUri?.Contains("diagram", StringComparison.OrdinalIgnoreCase) == true);

        if (relIds is null)
        {
            return new DiagramInfo();
        }

        var info = new DiagramInfo
        {
            DataRelationshipId = GetAttributeValue(relIds, "dm", RelationshipNamespace),
            LayoutRelationshipId = GetAttributeValue(relIds, "lo", RelationshipNamespace),
            QuickStyleRelationshipId = GetAttributeValue(relIds, "qs", RelationshipNamespace),
            ColorStyleRelationshipId = GetAttributeValue(relIds, "cs", RelationshipNamespace)
        };
        if (part is not null)
        {
            if (!string.IsNullOrWhiteSpace(info.DataRelationshipId)
                && part.GetPartById(info.DataRelationshipId) is OpenXmlPart dataPart)
            {
                info.DataPart = ReadPartData(dataPart);
            }

            if (!string.IsNullOrWhiteSpace(info.LayoutRelationshipId)
                && part.GetPartById(info.LayoutRelationshipId) is OpenXmlPart layoutPart)
            {
                info.LayoutPart = ReadPartData(layoutPart);
            }

            if (!string.IsNullOrWhiteSpace(info.QuickStyleRelationshipId)
                && part.GetPartById(info.QuickStyleRelationshipId) is OpenXmlPart stylePart)
            {
                info.QuickStylePart = ReadPartData(stylePart);
            }

            if (!string.IsNullOrWhiteSpace(info.ColorStyleRelationshipId)
                && part.GetPartById(info.ColorStyleRelationshipId) is OpenXmlPart colorPart)
            {
                info.ColorStylePart = ReadPartData(colorPart);
            }
        }

        return info;
    }

    private static OpenXmlElement? FindEmbeddedObjectElement(OpenXmlElement element)
    {
        if (IsEmbeddedObjectElement(element))
        {
            return element;
        }

        return element.Descendants().FirstOrDefault(IsEmbeddedObjectElement);
    }

    private static bool IsEmbeddedObjectElement(OpenXmlElement element)
    {
        return element.LocalName.Equals("OLEObject", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("oleObject", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("oleObj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryApplyVmlAnchor(OpenXmlElement element, FloatingAnchor target)
    {
        var shape = element.Descendants()
            .FirstOrDefault(node => node.LocalName.Equals("shape", StringComparison.OrdinalIgnoreCase));
        if (shape is null)
        {
            return false;
        }

        var styleValue = GetAttributeValue(shape, "style") ?? string.Empty;
        var style = ParseVmlStyle(styleValue);
        var hasWrapInfo = TryResolveVmlWrap(element, style, out var wrapStyle, out var wrapSide);
        var hasPosition = style.TryGetValue("position", out var position)
            && position.Equals("absolute", StringComparison.OrdinalIgnoreCase);
        var hasLegacyPosition = style.ContainsKey("mso-position-horizontal")
            || style.ContainsKey("mso-position-vertical")
            || style.ContainsKey("mso-position-horizontal-relative")
            || style.ContainsKey("mso-position-vertical-relative");
        var hasOffset = style.ContainsKey("left")
            || style.ContainsKey("top")
            || style.ContainsKey("margin-left")
            || style.ContainsKey("margin-top");

        if (!hasWrapInfo && !hasPosition && !hasLegacyPosition && !hasOffset)
        {
            return false;
        }

        target.WrapStyle = wrapStyle;
        target.WrapSide = wrapSide;

        if (style.TryGetValue("mso-position-horizontal-relative", out var horizontalReference))
        {
            target.HorizontalReference = MapVmlHorizontalReference(horizontalReference);
        }

        if (style.TryGetValue("mso-position-vertical-relative", out var verticalReference))
        {
            target.VerticalReference = MapVmlVerticalReference(verticalReference);
        }

        if (style.TryGetValue("mso-position-horizontal", out var horizontalAlignment))
        {
            target.HorizontalAlignment = MapHorizontalAlignment(horizontalAlignment);
        }

        if (style.TryGetValue("mso-position-vertical", out var verticalAlignment))
        {
            target.VerticalAlignment = MapVerticalAlignment(verticalAlignment);
        }

        if (TryGetVmlStyleLength(style, "margin-left", out var offsetX)
            || TryGetVmlStyleLength(style, "left", out offsetX))
        {
            target.OffsetX = offsetX;
        }

        if (TryGetVmlStyleLength(style, "margin-top", out var offsetY)
            || TryGetVmlStyleLength(style, "top", out offsetY))
        {
            target.OffsetY = offsetY;
        }

        if (style.TryGetValue("z-index", out var zIndexValue)
            && int.TryParse(zIndexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex))
        {
            target.BehindText = zIndex < 0;
            var abs = zIndex == int.MinValue ? int.MaxValue : Math.Abs(zIndex);
            target.ZOrder = (uint)abs;
        }

        var hasDistance = false;
        var left = 0f;
        var top = 0f;
        var right = 0f;
        var bottom = 0f;
        if (TryGetVmlStyleLength(style, "mso-wrap-distance-left", out var distanceLeft))
        {
            left = distanceLeft;
            hasDistance = true;
        }

        if (TryGetVmlStyleLength(style, "mso-wrap-distance-top", out var distanceTop))
        {
            top = distanceTop;
            hasDistance = true;
        }

        if (TryGetVmlStyleLength(style, "mso-wrap-distance-right", out var distanceRight))
        {
            right = distanceRight;
            hasDistance = true;
        }

        if (TryGetVmlStyleLength(style, "mso-wrap-distance-bottom", out var distanceBottom))
        {
            bottom = distanceBottom;
            hasDistance = true;
        }

        if (hasDistance)
        {
            target.Distance = new DocThickness(left, top, right, bottom);
        }

        return true;
    }

    private static Dictionary<string, string> ParseVmlStyle(string styleValue)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(styleValue))
        {
            return result;
        }

        var span = styleValue.AsSpan();
        var index = 0;
        while (index <= span.Length)
        {
            var next = span[index..].IndexOf(';');
            var end = next >= 0 ? index + next : span.Length;
            var segment = span.Slice(index, end - index);
            if (TrySplitStylePair(segment, out var key, out var value))
            {
                result[new string(key)] = new string(value);
            }

            index = end + 1;
        }

        return result;
    }

    private static bool TrySplitStylePair(ReadOnlySpan<char> segment, out ReadOnlySpan<char> key, out ReadOnlySpan<char> value)
    {
        var trimmed = segment.Trim();
        if (trimmed.IsEmpty)
        {
            key = ReadOnlySpan<char>.Empty;
            value = ReadOnlySpan<char>.Empty;
            return false;
        }

        var splitIndex = trimmed.IndexOf(':');
        if (splitIndex <= 0)
        {
            key = ReadOnlySpan<char>.Empty;
            value = ReadOnlySpan<char>.Empty;
            return false;
        }

        key = trimmed[..splitIndex].Trim();
        if (key.IsEmpty)
        {
            value = ReadOnlySpan<char>.Empty;
            return false;
        }

        value = trimmed[(splitIndex + 1)..].Trim();
        return true;
    }

    private static ReadOnlySpan<char> TrimSpan(string? value)
    {
        return value is null ? ReadOnlySpan<char>.Empty : value.AsSpan().Trim();
    }

    private static bool EqualsNormalizedToken(ReadOnlySpan<char> span, string token)
    {
        var tokenSpan = token.AsSpan();
        var tokenIndex = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch is '-' or '_' or ' ')
            {
                continue;
            }

            if (tokenIndex >= tokenSpan.Length)
            {
                return false;
            }

            if (char.ToUpperInvariant(ch) != char.ToUpperInvariant(tokenSpan[tokenIndex]))
            {
                return false;
            }

            tokenIndex++;
        }

        return tokenIndex == tokenSpan.Length;
    }

    private static bool TryResolveVmlWrap(
        OpenXmlElement element,
        IReadOnlyDictionary<string, string> style,
        out FloatingWrapStyle wrapStyle,
        out FloatingWrapSide wrapSide)
    {
        wrapStyle = FloatingWrapStyle.None;
        wrapSide = FloatingWrapSide.Both;
        var hasWrapInfo = false;

        if (style.TryGetValue("mso-wrap-style", out var styleValue))
        {
            wrapStyle = MapVmlWrapStyle(styleValue);
            hasWrapInfo = true;
        }

        if (style.TryGetValue("mso-wrap-side", out var sideValue))
        {
            wrapSide = MapVmlWrapSide(sideValue);
            hasWrapInfo = true;
        }

        var wrapElement = element.Descendants()
            .FirstOrDefault(node => node.LocalName.Equals("wrap", StringComparison.OrdinalIgnoreCase));
        if (wrapElement is not null)
        {
            var type = GetAttributeValue(wrapElement, "type");
            if (!string.IsNullOrWhiteSpace(type))
            {
                wrapStyle = MapVmlWrapStyle(type);
                hasWrapInfo = true;
            }

            var side = GetAttributeValue(wrapElement, "side");
            if (!string.IsNullOrWhiteSpace(side))
            {
                wrapSide = MapVmlWrapSide(side);
                hasWrapInfo = true;
            }
        }

        return hasWrapInfo;
    }

    private static FloatingWrapStyle MapVmlWrapStyle(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingWrapStyle.None;
        }

        if (EqualsNormalizedToken(span, "square"))
        {
            return FloatingWrapStyle.Square;
        }

        if (EqualsNormalizedToken(span, "tight"))
        {
            return FloatingWrapStyle.Tight;
        }

        if (EqualsNormalizedToken(span, "through"))
        {
            return FloatingWrapStyle.Through;
        }

        if (EqualsNormalizedToken(span, "topandbottom") || EqualsNormalizedToken(span, "topbottom"))
        {
            return FloatingWrapStyle.TopBottom;
        }

        return FloatingWrapStyle.None;
    }

    private static FloatingWrapSide MapVmlWrapSide(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingWrapSide.Both;
        }

        if (span.Equals("left", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingWrapSide.Left;
        }

        if (span.Equals("right", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingWrapSide.Right;
        }

        if (span.Equals("largest", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingWrapSide.Largest;
        }

        if (span.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingWrapSide.Both;
        }

        return FloatingWrapSide.Both;
    }

    private static FloatingHorizontalReference MapVmlHorizontalReference(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingHorizontalReference.Margin;
        }

        if (span.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalReference.Page;
        }

        if (span.Equals("margin", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalReference.Margin;
        }

        if (span.Equals("column", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalReference.Column;
        }

        if (span.Equals("character", StringComparison.OrdinalIgnoreCase) || span.Equals("char", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalReference.Character;
        }

        if (span.Equals("text", StringComparison.OrdinalIgnoreCase)
            || span.Equals("paragraph", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalReference.Paragraph;
        }

        return FloatingHorizontalReference.Margin;
    }

    private static FloatingVerticalReference MapVmlVerticalReference(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingVerticalReference.Margin;
        }

        if (span.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalReference.Page;
        }

        if (span.Equals("margin", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalReference.Margin;
        }

        if (span.Equals("paragraph", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalReference.Paragraph;
        }

        if (span.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalReference.Line;
        }

        return FloatingVerticalReference.Margin;
    }

    private static bool TryGetVmlStyleLength(
        IReadOnlyDictionary<string, string> style,
        string key,
        out float value)
    {
        value = 0f;
        if (!style.TryGetValue(key, out var raw))
        {
            return false;
        }

        return TryParseVmlLength(raw, out value);
    }

    private static ShapeInline? TryCreateWordprocessingShape(
        Drawing drawing,
        Wps.WordprocessingShape shape,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 0f;
        var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 0f;

        var spPr = shape.GetFirstChild<Wps.ShapeProperties>();
        var transform = spPr?.GetFirstChild<A.Transform2D>();
        if (width <= 0f && transform?.Extents?.Cx?.Value is long tcx)
        {
            width = EmuToDip(tcx);
        }

        if (height <= 0f && transform?.Extents?.Cy?.Value is long tcy)
        {
            height = EmuToDip(tcy);
        }

        if (width <= 0f)
        {
            width = 100f;
        }

        if (height <= 0f)
        {
            height = 100f;
        }

        var shapeInline = new ShapeInline(width, height);
        var docProps = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
        shapeInline.Name = docProps?.Name?.Value
            ?? shape.GetFirstChild<Wps.NonVisualDrawingProperties>()?.Name?.Value;

        ApplyShapeProperties(spPr, shapeInline.Properties, styleResolver.ThemeColors, imageResolver);
        if (transform is not null)
        {
            if (transform.Rotation?.Value is int rotation)
            {
                shapeInline.Properties.Rotation = rotation / 60000f;
            }

            shapeInline.Properties.FlipHorizontal = transform.HorizontalFlip?.Value ?? false;
            shapeInline.Properties.FlipVertical = transform.VerticalFlip?.Value ?? false;
        }

        var bodyPr = shape.GetFirstChild<Wps.TextBodyProperties>();
        var textBoxContent = shape.GetFirstChild<Wps.TextBoxInfo2>()?.TextBoxContent;
        if (textBoxContent is not null || bodyPr is not null)
        {
            var textBox = new ShapeTextBox();
            ApplyTextBodyProperties(bodyPr, textBox.Properties);
            if (textBoxContent is not null)
            {
                var blocks = ParseTextBoxContent(textBoxContent, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
                textBox.Blocks.AddRange(blocks);
            }

            if (textBox.Blocks.Count > 0 || bodyPr is not null)
            {
                shapeInline.TextBox = textBox;
            }
        }

        return shapeInline;
    }

    private static void ApplyAnchorProperties(DW.Anchor anchor, FloatingAnchor target)
    {
        target.BehindText = anchor.BehindDoc?.Value ?? false;
        target.AllowOverlap = anchor.AllowOverlap?.Value ?? true;
        if (anchor.RelativeHeight?.Value is uint relativeHeight)
        {
            target.ZOrder = relativeHeight;
        }

        var distance = target.Distance;
        var left = anchor.DistanceFromLeft?.Value;
        var top = anchor.DistanceFromTop?.Value;
        var right = anchor.DistanceFromRight?.Value;
        var bottom = anchor.DistanceFromBottom?.Value;
        if (left.HasValue || top.HasValue || right.HasValue || bottom.HasValue)
        {
            distance = new DocThickness(
                left.HasValue ? EmuToDip(left.Value) : distance.Left,
                top.HasValue ? EmuToDip(top.Value) : distance.Top,
                right.HasValue ? EmuToDip(right.Value) : distance.Right,
                bottom.HasValue ? EmuToDip(bottom.Value) : distance.Bottom);
        }

        if (anchor.SimplePos?.Value == true && anchor.SimplePosition is { } simplePosition)
        {
            if (simplePosition.X?.Value is long simpleX)
            {
                target.OffsetX = EmuToDip(simpleX);
            }

            if (simplePosition.Y?.Value is long simpleY)
            {
                target.OffsetY = EmuToDip(simpleY);
            }

            target.HorizontalReference = FloatingHorizontalReference.Page;
            target.VerticalReference = FloatingVerticalReference.Page;
        }

        if (anchor.HorizontalPosition is { } horizontal)
        {
            target.HorizontalReference = MapHorizontalReference(horizontal.RelativeFrom?.Value);
            var alignment = horizontal.HorizontalAlignment?.Text;
            target.HorizontalAlignment = MapHorizontalAlignment(alignment);
            if (horizontal.PositionOffset?.Text is { Length: > 0 } offsetText
                && long.TryParse(offsetText, out var offset))
            {
                target.OffsetX = EmuToDip(offset);
            }
        }

        if (anchor.VerticalPosition is { } vertical)
        {
            target.VerticalReference = MapVerticalReference(vertical.RelativeFrom?.Value);
            var alignment = vertical.VerticalAlignment?.Text;
            target.VerticalAlignment = MapVerticalAlignment(alignment);
            if (vertical.PositionOffset?.Text is { Length: > 0 } offsetText
                && long.TryParse(offsetText, out var offset))
            {
                target.OffsetY = EmuToDip(offset);
            }
        }

        var wrapSquare = anchor.GetFirstChild<DW.WrapSquare>();
        var wrapTight = anchor.GetFirstChild<DW.WrapTight>();
        var wrapThrough = anchor.GetFirstChild<DW.WrapThrough>();
        if (wrapSquare is not null)
        {
            target.WrapStyle = FloatingWrapStyle.Square;
            target.WrapSide = MapWrapSide(wrapSquare.WrapText?.Value);
            distance = ApplyWrapDistance(
                distance,
                EmuToDipOptional(wrapSquare.DistanceFromLeft),
                EmuToDipOptional(wrapSquare.DistanceFromTop),
                EmuToDipOptional(wrapSquare.DistanceFromRight),
                EmuToDipOptional(wrapSquare.DistanceFromBottom));
            target.WrapPolygon = null;
        }
        else if (wrapTight is not null)
        {
            target.WrapStyle = FloatingWrapStyle.Tight;
            target.WrapSide = MapWrapSide(wrapTight.WrapText?.Value);
            distance = ApplyWrapDistance(
                distance,
                EmuToDipOptional(wrapTight.DistanceFromLeft),
                null,
                EmuToDipOptional(wrapTight.DistanceFromRight),
                null);
            target.WrapPolygon = TryCreateWrapPolygon(wrapTight.WrapPolygon);
        }
        else if (anchor.GetFirstChild<DW.WrapTopBottom>() is not null)
        {
            target.WrapStyle = FloatingWrapStyle.TopBottom;
            target.WrapSide = FloatingWrapSide.Both;
            target.WrapPolygon = null;
        }
        else if (wrapThrough is not null)
        {
            target.WrapStyle = FloatingWrapStyle.Through;
            target.WrapSide = MapWrapSide(wrapThrough.WrapText?.Value);
            distance = ApplyWrapDistance(
                distance,
                EmuToDipOptional(wrapThrough.DistanceFromLeft),
                null,
                EmuToDipOptional(wrapThrough.DistanceFromRight),
                null);
            target.WrapPolygon = TryCreateWrapPolygon(wrapThrough.WrapPolygon);
        }
        else
        {
            target.WrapStyle = FloatingWrapStyle.None;
            target.WrapSide = FloatingWrapSide.Both;
            target.WrapPolygon = null;
        }

        target.Distance = distance;
    }

    private static DocThickness ApplyWrapDistance(
        DocThickness current,
        float? left,
        float? top,
        float? right,
        float? bottom)
    {
        if (!left.HasValue && !top.HasValue && !right.HasValue && !bottom.HasValue)
        {
            return current;
        }

        return new DocThickness(
            left ?? current.Left,
            top ?? current.Top,
            right ?? current.Right,
            bottom ?? current.Bottom);
    }

    private static float? EmuToDipOptional(UInt32Value? value)
    {
        return value is null ? null : EmuToDip((long)value.Value);
    }

    private static FloatingWrapPolygon? TryCreateWrapPolygon(DW.WrapPolygon? polygon)
    {
        if (polygon?.StartPoint is not DW.StartPoint startPoint)
        {
            return null;
        }

        if (!TryReadWrapPoint(startPoint, out var start))
        {
            return null;
        }

        var points = new List<DocPoint> { start };
        foreach (var lineTo in polygon.Elements<DW.LineTo>())
        {
            if (TryReadWrapPoint(lineTo, out var point))
            {
                points.Add(point);
            }
        }

        if (points.Count < 3)
        {
            return null;
        }

        return new FloatingWrapPolygon(points.ToArray());
    }

    private static bool TryReadWrapPoint(DW.Point2DType point, out DocPoint result)
    {
        var x = point.X?.Value;
        var y = point.Y?.Value;
        if (!x.HasValue || !y.HasValue)
        {
            result = default;
            return false;
        }

        result = new DocPoint(EmuToDip(x.Value), EmuToDip(y.Value));
        return true;
    }

    private static FloatingWrapSide MapWrapSide(DW.WrapTextValues? value)
    {
        if (!value.HasValue)
        {
            return FloatingWrapSide.Both;
        }

        if (value.Value == DW.WrapTextValues.Left)
        {
            return FloatingWrapSide.Left;
        }

        if (value.Value == DW.WrapTextValues.Right)
        {
            return FloatingWrapSide.Right;
        }

        if (value.Value == DW.WrapTextValues.Largest)
        {
            return FloatingWrapSide.Largest;
        }

        return FloatingWrapSide.Both;
    }

    private static FloatingHorizontalReference MapHorizontalReference(DW.HorizontalRelativePositionValues? value)
    {
        if (value is null)
        {
            return FloatingHorizontalReference.Margin;
        }

        if (value.Value == DW.HorizontalRelativePositionValues.Page)
        {
            return FloatingHorizontalReference.Page;
        }

        if (value.Value == DW.HorizontalRelativePositionValues.Column)
        {
            return FloatingHorizontalReference.Column;
        }

        if (value.Value == DW.HorizontalRelativePositionValues.Character)
        {
            return FloatingHorizontalReference.Character;
        }

        return FloatingHorizontalReference.Margin;
    }

    private static FloatingVerticalReference MapVerticalReference(DW.VerticalRelativePositionValues? value)
    {
        if (value is null)
        {
            return FloatingVerticalReference.Margin;
        }

        if (value.Value == DW.VerticalRelativePositionValues.Page)
        {
            return FloatingVerticalReference.Page;
        }

        if (value.Value == DW.VerticalRelativePositionValues.Paragraph)
        {
            return FloatingVerticalReference.Paragraph;
        }

        if (value.Value == DW.VerticalRelativePositionValues.Line)
        {
            return FloatingVerticalReference.Line;
        }

        return FloatingVerticalReference.Margin;
    }

    private static FloatingHorizontalAlignment MapHorizontalAlignment(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingHorizontalAlignment.None;
        }

        if (span.Equals("left", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalAlignment.Left;
        }

        if (span.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalAlignment.Center;
        }

        if (span.Equals("right", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalAlignment.Right;
        }

        if (span.Equals("inside", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalAlignment.Inside;
        }

        if (span.Equals("outside", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingHorizontalAlignment.Outside;
        }

        return FloatingHorizontalAlignment.None;
    }

    private static FloatingVerticalAlignment MapVerticalAlignment(string? value)
    {
        var span = TrimSpan(value);
        if (span.IsEmpty)
        {
            return FloatingVerticalAlignment.None;
        }

        if (span.Equals("top", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalAlignment.Top;
        }

        if (span.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalAlignment.Center;
        }

        if (span.Equals("bottom", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalAlignment.Bottom;
        }

        if (span.Equals("inside", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalAlignment.Inside;
        }

        if (span.Equals("outside", StringComparison.OrdinalIgnoreCase))
        {
            return FloatingVerticalAlignment.Outside;
        }

        return FloatingVerticalAlignment.None;
    }

    private static void ApplyShapeProperties(
        Wps.ShapeProperties? shapeProperties,
        ShapeProperties properties,
        DocumentThemeColorMap? themeColors,
        ImageResolver imageResolver)
    {
        if (shapeProperties is null)
        {
            return;
        }

        properties.CustomGeometry = null;
        properties.AdjustValues.Clear();

        var customGeometry = shapeProperties.GetFirstChild<A.CustomGeometry>();
        if (customGeometry is not null)
        {
            properties.CustomGeometry = ParseCustomGeometry(customGeometry);
            properties.PresetGeometry = null;
        }
        else
        {
            var presetGeometry = shapeProperties.GetFirstChild<A.PresetGeometry>();
            var preset = presetGeometry?.Preset?.Value;
            properties.PresetGeometry = preset?.ToString();
            ParseAdjustValues(presetGeometry?.AdjustValueList, properties.AdjustValues);
        }

        var fill = ParseShapeFill(shapeProperties, themeColors, imageResolver);
        properties.Fill = fill;
        properties.FillColor = fill switch
        {
            ShapeSolidFill solid => solid.Color,
            ShapePatternFill pattern => pattern.Foreground,
            ShapeGradientFill gradient when gradient.Stops.Count > 0 => gradient.Stops[0].Color,
            ShapeNoFill => null,
            null => TryParseDrawingFillColor(shapeProperties),
            _ => null
        };
        properties.Outline = ParseShapeOutline(shapeProperties.GetFirstChild<A.Outline>());
        properties.Effects = ParseDrawingEffects(shapeProperties, themeColors);
    }

    private static void ApplyTextBodyProperties(Wps.TextBodyProperties? bodyProperties, ShapeTextBoxProperties properties)
    {
        if (bodyProperties is null)
        {
            return;
        }

        var left = bodyProperties.LeftInset?.Value;
        var top = bodyProperties.TopInset?.Value;
        var right = bodyProperties.RightInset?.Value;
        var bottom = bodyProperties.BottomInset?.Value;

        if (left.HasValue || top.HasValue || right.HasValue || bottom.HasValue)
        {
            properties.Padding = new DocThickness(
                left.HasValue ? EmuToDip(left.Value) : 0f,
                top.HasValue ? EmuToDip(top.Value) : 0f,
                right.HasValue ? EmuToDip(right.Value) : 0f,
                bottom.HasValue ? EmuToDip(bottom.Value) : 0f);
        }

        var anchor = bodyProperties.Anchor?.Value;
        if (anchor is not null)
        {
            if (anchor == A.TextAnchoringTypeValues.Center)
            {
                properties.VerticalAlignment = ShapeTextVerticalAlignment.Center;
            }
            else if (anchor == A.TextAnchoringTypeValues.Bottom)
            {
                properties.VerticalAlignment = ShapeTextVerticalAlignment.Bottom;
            }
            else
            {
                properties.VerticalAlignment = ShapeTextVerticalAlignment.Top;
            }
        }

        if (bodyProperties.HorizontalOverflow?.Value is { } horizontalOverflow)
        {
            properties.HorizontalOverflow = MapShapeTextHorizontalOverflow(horizontalOverflow);
        }

        if (bodyProperties.VerticalOverflow?.Value is { } verticalOverflow)
        {
            properties.VerticalOverflow = MapShapeTextVerticalOverflow(verticalOverflow);
        }

        if (bodyProperties.Vertical?.Value is { } vertical)
        {
            var upright = bodyProperties.UpRight?.Value;
            properties.TextDirection = MapShapeTextDirection(vertical, upright);
        }

        if (bodyProperties.GetFirstChild<A.ShapeAutoFit>() is not null)
        {
            properties.AutoFit = ShapeTextAutoFit.ShapeToFitText;
        }
        else if (bodyProperties.GetFirstChild<A.NormalAutoFit>() is not null)
        {
            properties.AutoFit = ShapeTextAutoFit.TextToFitShape;
        }
        else if (bodyProperties.GetFirstChild<A.NoAutoFit>() is not null)
        {
            properties.AutoFit = ShapeTextAutoFit.None;
        }
    }

    private static ShapeTextOverflow MapShapeTextHorizontalOverflow(A.TextHorizontalOverflowValues value)
    {
        return value == A.TextHorizontalOverflowValues.Clip
            ? ShapeTextOverflow.Clip
            : ShapeTextOverflow.Overflow;
    }

    private static ShapeTextOverflow MapShapeTextVerticalOverflow(A.TextVerticalOverflowValues value)
    {
        return value switch
        {
            var overflow when overflow == A.TextVerticalOverflowValues.Clip => ShapeTextOverflow.Clip,
            var overflow when overflow == A.TextVerticalOverflowValues.Ellipsis => ShapeTextOverflow.Ellipsis,
            _ => ShapeTextOverflow.Overflow
        };
    }

    private static DocTextDirection MapShapeTextDirection(A.TextVerticalValues value, bool? upright)
    {
        if (value == A.TextVerticalValues.Horizontal)
        {
            return DocTextDirection.LeftToRightTopToBottom;
        }

        if (value == A.TextVerticalValues.Vertical270)
        {
            return upright == true
                ? DocTextDirection.BottomToTopLeftToRight
                : DocTextDirection.TopToBottomLeftToRightRotated;
        }

        if (value == A.TextVerticalValues.Vertical
            || value == A.TextVerticalValues.EastAsianVetical
            || value == A.TextVerticalValues.MongolianVertical
            || value == A.TextVerticalValues.WordArtVertical
            || value == A.TextVerticalValues.WordArtLeftToRight)
        {
            return upright == true
                ? DocTextDirection.TopToBottomRightToLeft
                : DocTextDirection.TopToBottomRightToLeftRotated;
        }

        return DocTextDirection.LeftToRightTopToBottom;
    }

    private static List<Block> ParseTextBoxContent(
        TextBoxContent content,
        ListResolver listResolver,
        ImageResolver imageResolver,
        ChartResolver chartResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
        DocumentRevisions revisions,
        ContentControlPlaceholderResolver? placeholderResolver)
    {
        var blocks = new List<Block>();
        foreach (var element in content.Elements())
        {
            AppendBlockElement(element, blocks, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new ParagraphBlock());
        }

        return blocks;
    }

    private static DocColor? TryParseDrawingFillColor(OpenXmlElement element)
    {
        if (element.GetFirstChild<A.NoFill>() is not null)
        {
            return null;
        }

        var solidFill = element.GetFirstChild<A.SolidFill>();
        return TryParseSolidFillColor(solidFill);
    }

    private static DocColor? TryParseSolidFillColor(A.SolidFill? solidFill)
    {
        if (solidFill is null)
        {
            return null;
        }

        var rgb = solidFill.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) && TryParseHexColor(rgb, out var color))
        {
            return color;
        }

        return null;
    }

    private static ShapeFill? ParseShapeFill(OpenXmlElement shapeProperties, DocumentThemeColorMap? themeColors, ImageResolver imageResolver)
    {
        if (shapeProperties.GetFirstChild<A.NoFill>() is not null)
        {
            return new ShapeNoFill();
        }

        var solidFill = shapeProperties.GetFirstChild<A.SolidFill>();
        if (solidFill is not null)
        {
            var color = TryResolveDrawingColor(solidFill, themeColors);
            if (color.HasValue)
            {
                return new ShapeSolidFill(color.Value);
            }
        }

        var gradientFill = shapeProperties.GetFirstChild<A.GradientFill>();
        if (gradientFill is not null)
        {
            var gradient = ParseGradientFill(gradientFill, themeColors);
            if (gradient is not null)
            {
                return gradient;
            }
        }

        var patternFill = shapeProperties.GetFirstChild<A.PatternFill>();
        if (patternFill is not null)
        {
            var pattern = ParsePatternFill(patternFill, themeColors);
            if (pattern is not null)
            {
                return pattern;
            }
        }

        var blipFill = shapeProperties.GetFirstChild<A.BlipFill>();
        if (blipFill is not null)
        {
            var imageFill = ParseImageFill(blipFill, imageResolver);
            if (imageFill is not null)
            {
                return imageFill;
            }
        }

        return null;
    }

    private static ShapeGradientFill? ParseGradientFill(A.GradientFill gradientFill, DocumentThemeColorMap? themeColors)
    {
        var gradient = new ShapeGradientFill();
        var stops = gradientFill.GradientStopList?.Elements<A.GradientStop>()
                    ?? gradientFill.Descendants<A.GradientStop>();
        foreach (var stop in stops)
        {
            var color = TryResolveDrawingColor(stop, themeColors);
            if (!color.HasValue)
            {
                continue;
            }

            var position = stop.Position?.Value;
            var offset = position.HasValue ? position.Value / 100000f : 0f;
            gradient.Stops.Add(new ShapeGradientStop(offset, color.Value));
        }

        if (gradient.Stops.Count == 0)
        {
            return null;
        }

        var path = gradientFill.GetFirstChild<A.PathGradientFill>();
        if (path is not null)
        {
            gradient.Type = ShapeGradientType.Radial;
            gradient.FillRect = ParseRelativeRect(path.FillToRectangle);
        }
        else
        {
            var linear = gradientFill.GetFirstChild<A.LinearGradientFill>();
            gradient.Type = ShapeGradientType.Linear;
            if (linear?.Angle?.Value is int angle)
            {
                gradient.Angle = angle / 60000f;
            }

            gradient.IsScaled = linear?.Scaled?.Value ?? false;
        }

        var fillRect = gradientFill.GetFirstChild<A.FillRectangle>();
        if (fillRect is not null)
        {
            gradient.FillRect ??= ParseRelativeRect(fillRect);
        }

        var tileRect = gradientFill.GetFirstChild<A.TileRectangle>();
        if (tileRect is not null)
        {
            gradient.TileRect = ParseRelativeRect(tileRect);
        }

        return gradient;
    }

    private static ShapePatternFill? ParsePatternFill(A.PatternFill patternFill, DocumentThemeColorMap? themeColors)
    {
        var preset = patternFill.Preset?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preset))
        {
            return null;
        }

        var fill = new ShapePatternFill
        {
            Pattern = preset
        };

        var foreground = TryResolveDrawingColor(patternFill.ForegroundColor, themeColors);
        if (foreground.HasValue)
        {
            fill.Foreground = foreground.Value;
        }

        var background = TryResolveDrawingColor(patternFill.BackgroundColor, themeColors);
        if (background.HasValue)
        {
            fill.Background = background.Value;
        }

        return fill;
    }

    private static ShapeImageFill? ParseImageFill(A.BlipFill blipFill, ImageResolver imageResolver)
    {
        if (imageResolver.Part is null)
        {
            return null;
        }

        var blip = blipFill.Blip;
        var embed = blip?.Embed?.Value ?? blip?.Link?.Value;
        if (string.IsNullOrWhiteSpace(embed))
        {
            return null;
        }

        if (imageResolver.Part.GetPartById(embed) is not ImagePart imagePart)
        {
            return null;
        }

        var data = ReadPartData(imagePart);
        var fill = new ShapeImageFill(data, imagePart.ContentType)
        {
            Crop = ParseImageCrop(blipFill.SourceRectangle)
        };

        var tile = blipFill.GetFirstChild<A.Tile>();
        if (tile is not null)
        {
            fill.Mode = ShapeImageFillMode.Tile;
            fill.Tile = ParseShapeImageTile(tile);
        }
        else
        {
            fill.Mode = ShapeImageFillMode.Stretch;
        }

        return fill;
    }

    private static ShapeImageTile? ParseShapeImageTile(A.Tile tile)
    {
        var tileInfo = new ShapeImageTile();
        if (tile.HorizontalOffset?.Value is long offsetX)
        {
            tileInfo.OffsetX = EmuToDip(offsetX);
        }

        if (tile.VerticalOffset?.Value is long offsetY)
        {
            tileInfo.OffsetY = EmuToDip(offsetY);
        }

        if (tile.HorizontalRatio?.Value is int ratioX && ratioX > 0)
        {
            tileInfo.ScaleX = ratioX / 100000f;
        }

        if (tile.VerticalRatio?.Value is int ratioY && ratioY > 0)
        {
            tileInfo.ScaleY = ratioY / 100000f;
        }

        return tileInfo;
    }

    private static ImageCrop? ParseImageCrop(A.SourceRectangle? source)
    {
        if (source is null)
        {
            return null;
        }

        var left = Math.Max(0, source.Left?.Value ?? 0);
        var top = Math.Max(0, source.Top?.Value ?? 0);
        var right = Math.Max(0, source.Right?.Value ?? 0);
        var bottom = Math.Max(0, source.Bottom?.Value ?? 0);
        if (left == 0 && top == 0 && right == 0 && bottom == 0)
        {
            return null;
        }

        return new ImageCrop(left / 100000f, top / 100000f, right / 100000f, bottom / 100000f);
    }

    private static A.SourceRectangle? ResolveDrawingSourceRectangle(Drawing drawing)
    {
        var pictureFill = drawing.Descendants<PIC.BlipFill>().FirstOrDefault();
        if (pictureFill?.SourceRectangle is not null)
        {
            return pictureFill.SourceRectangle;
        }

        var drawingFill = drawing.Descendants<A.BlipFill>().FirstOrDefault();
        return drawingFill?.SourceRectangle;
    }

    private static DrawingColorEffects? ParseBlipColorEffects(A.Blip? blip, DocumentThemeColorMap? themeColors)
    {
        if (blip is null)
        {
            return null;
        }

        var effects = new DrawingColorEffects();

        var tint = blip.GetFirstChild<A.Tint>();
        if (tint?.Val?.Value is int tintValue)
        {
            effects.Tint = ClampDrawingPercentage(tintValue);
        }

        var saturationMod = blip.GetFirstChild<A.SaturationModulation>();
        if (saturationMod?.Val?.Value is int saturationValue)
        {
            effects.Saturation = MathF.Max(0f, saturationValue / 100000f);
        }
        else
        {
            var saturation = blip.GetFirstChild<A.Saturation>();
            if (saturation?.Val?.Value is int saturationValue2)
            {
                effects.Saturation = MathF.Max(0f, saturationValue2 / 100000f);
            }
        }

        var duotone = blip.GetFirstChild<A.Duotone>();
        if (duotone is not null)
        {
            ParseDuotoneColors(duotone, themeColors, out var dark, out var light);
            effects.RecolorDark = dark;
            effects.RecolorLight = light;
        }

        if (!effects.RecolorDark.HasValue && !effects.RecolorLight.HasValue)
        {
            var colorReplacement = blip.GetFirstChild<A.ColorReplacement>();
            if (colorReplacement is not null)
            {
                var replacement = TryResolveDrawingColor(colorReplacement, themeColors);
                if (replacement.HasValue)
                {
                    effects.RecolorDark = DocColor.Black;
                    effects.RecolorLight = replacement.Value;
                }
            }
        }

        if (!effects.RecolorDark.HasValue && !effects.RecolorLight.HasValue)
        {
            var colorChange = blip.GetFirstChild<A.ColorChange>();
            if (colorChange?.ColorTo is not null)
            {
                var replacement = TryResolveDrawingColor(colorChange.ColorTo, themeColors);
                if (replacement.HasValue)
                {
                    effects.RecolorDark = DocColor.Black;
                    effects.RecolorLight = replacement.Value;
                }
            }
        }

        return effects.HasValues ? effects : null;
    }

    private static void ParseDuotoneColors(
        A.Duotone duotone,
        DocumentThemeColorMap? themeColors,
        out DocColor? dark,
        out DocColor? light)
    {
        dark = null;
        light = null;

        foreach (var child in duotone.ChildElements)
        {
            var color = TryResolveDrawingColor(child, themeColors);
            if (!color.HasValue)
            {
                continue;
            }

            if (!dark.HasValue)
            {
                dark = color.Value;
            }
            else if (!light.HasValue)
            {
                light = color.Value;
                break;
            }
        }
    }

    private static float ClampDrawingPercentage(int raw)
    {
        var value = raw / 100000f;
        if (value <= 0f)
        {
            return 0f;
        }

        if (value >= 1f)
        {
            return 1f;
        }

        return value;
    }

    private static ShapeGradientRect? ParseRelativeRect(A.RelativeRectangleType? rect)
    {
        if (rect is null)
        {
            return null;
        }

        var left = rect.Left?.Value ?? 0;
        var top = rect.Top?.Value ?? 0;
        var right = rect.Right?.Value ?? 0;
        var bottom = rect.Bottom?.Value ?? 0;
        if (left == 0 && top == 0 && right == 0 && bottom == 0)
        {
            return null;
        }

        return new ShapeGradientRect
        {
            Left = left / 100000f,
            Top = top / 100000f,
            Right = right / 100000f,
            Bottom = bottom / 100000f
        };
    }

    private static BorderLine? ParseShapeOutline(A.Outline? outline)
    {
        if (outline is null)
        {
            return null;
        }

        if (outline.GetFirstChild<A.NoFill>() is not null)
        {
            return new BorderLine { Style = DocBorderStyle.None };
        }

        var border = new BorderLine();
        if (outline.Width?.Value is int width)
        {
            border.Thickness = EmuToDip(width);
        }

        if (outline.CapType?.Value is A.LineCapValues cap)
        {
            border.LineCap = MapLineCap(cap);
        }

        if (outline.CompoundLineType?.Value is A.CompoundLineValues compound)
        {
            border.Compound = MapCompoundLine(compound);
        }

        if (outline.GetFirstChild<A.Round>() is not null)
        {
            border.LineJoin = DocLineJoin.Round;
        }
        else if (outline.GetFirstChild<A.LineJoinBevel>() is not null)
        {
            border.LineJoin = DocLineJoin.Bevel;
        }
        else if (outline.GetFirstChild<A.Miter>() is { } miter)
        {
            border.LineJoin = DocLineJoin.Miter;
            if (miter.Limit?.Value is int limit)
            {
                border.MiterLimit = ResolveLineJoinLimit(limit);
            }
        }

        border.HeadArrow = MapLineArrow(outline.GetFirstChild<A.HeadEnd>());
        border.TailArrow = MapLineArrow(outline.GetFirstChild<A.TailEnd>());

        var color = TryParseSolidFillColor(outline.GetFirstChild<A.SolidFill>());
        if (color.HasValue)
        {
            border.Color = color.Value;
        }

        var dash = outline.GetFirstChild<A.PresetDash>()?.Val?.Value;
        border.Style = MapLineDash(dash);
        return border;
    }

    private static DrawingEffects? ParseDrawingEffects(OpenXmlElement? element, DocumentThemeColorMap? themeColors)
    {
        if (element is null)
        {
            return null;
        }

        var effectList = element.GetFirstChild<A.EffectList>()
                         ?? element.Descendants<A.EffectList>().FirstOrDefault();
        if (effectList is null)
        {
            return null;
        }

        var effects = new DrawingEffects();

        var glow = effectList.GetFirstChild<A.Glow>();
        if (glow is not null)
        {
            var glowEffect = new DrawingGlowEffect();
            if (TryParseEmu(glow, "rad", out var glowRad))
            {
                glowEffect.Radius = EmuToDip(glowRad);
            }

            var color = TryResolveDrawingColor(glow, themeColors);
            if (color.HasValue)
            {
                glowEffect.Color = color.Value;
            }

            if (glowEffect.Radius > 0f || color.HasValue)
            {
                effects.Glow = glowEffect;
            }
        }

        var reflection = effectList.GetFirstChild<A.Reflection>();
        if (reflection is not null)
        {
            var reflectionEffect = new DrawingReflectionEffect();
            if (TryParseEmu(reflection, "blurRad", out var blur))
            {
                reflectionEffect.BlurRadius = EmuToDip(blur);
            }

            if (TryParseEmu(reflection, "dist", out var dist))
            {
                reflectionEffect.Distance = EmuToDip(dist);
            }

            if (TryParsePercentage(reflection, "st", out var startOpacity))
            {
                reflectionEffect.StartOpacity = startOpacity;
            }

            if (TryParsePercentage(reflection, "end", out var endOpacity))
            {
                reflectionEffect.EndOpacity = endOpacity;
            }

            if (TryParsePercentage(reflection, "sx", out var scaleX))
            {
                reflectionEffect.ScaleX = scaleX;
            }

            if (TryParsePercentage(reflection, "sy", out var scaleY))
            {
                reflectionEffect.ScaleY = scaleY;
            }

            effects.Reflection = reflectionEffect;
        }

        var softEdge = effectList.GetFirstChild<A.SoftEdge>();
        if (softEdge is not null)
        {
            var softEdgeEffect = new DrawingSoftEdgeEffect();
            if (TryParseEmu(softEdge, "rad", out var softRadius))
            {
                softEdgeEffect.Radius = EmuToDip(softRadius);
            }

            if (softEdgeEffect.Radius > 0f)
            {
                effects.SoftEdge = softEdgeEffect;
            }
        }

        var outerShadow = effectList.GetFirstChild<A.OuterShadow>();
        if (outerShadow is not null)
        {
            var shadowEffect = new DrawingShadowEffect();
            if (TryParseEmu(outerShadow, "blurRad", out var shadowBlur))
            {
                shadowEffect.BlurRadius = EmuToDip(shadowBlur);
            }

            if (TryParseEmu(outerShadow, "dist", out var shadowDist))
            {
                shadowEffect.Distance = EmuToDip(shadowDist);
            }

            if (TryParseIntAttribute(outerShadow, "dir", out var shadowDir))
            {
                shadowEffect.Direction = shadowDir / 60000f;
            }

            var shadowColor = TryResolveDrawingColor(outerShadow, themeColors);
            if (shadowColor.HasValue)
            {
                shadowEffect.Color = shadowColor.Value;
            }

            effects.Shadow = shadowEffect;
        }

        return effects.HasValues ? effects : null;
    }

    private static ShapeGeometry? ParseCustomGeometry(A.CustomGeometry customGeometry)
    {
        var geometry = new ShapeGeometry();

        var adjustList = customGeometry.AdjustValueList;
        if (adjustList is not null)
        {
            foreach (var guide in adjustList.Elements<A.ShapeGuide>())
            {
                var name = guide.Name?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var formula = guide.Formula?.Value;
                geometry.Adjusts.Add(new ShapeGuide(name, formula));
            }
        }

        var guideList = customGeometry.ShapeGuideList;
        if (guideList is not null)
        {
            foreach (var guide in guideList.Elements<A.ShapeGuide>())
            {
                var name = guide.Name?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var formula = guide.Formula?.Value;
                geometry.Guides.Add(new ShapeGuide(name, formula));
            }
        }

        var rect = customGeometry.Rectangle;
        if (rect is not null)
        {
            var left = rect.Left?.Value ?? "l";
            var top = rect.Top?.Value ?? "t";
            var right = rect.Right?.Value ?? "r";
            var bottom = rect.Bottom?.Value ?? "b";
            geometry.TextRectangle = new ShapeTextRectangle(left, top, right, bottom);
        }

        var pathList = customGeometry.PathList;
        if (pathList is not null)
        {
            foreach (var pathElement in pathList.Elements<A.Path>())
            {
                geometry.Paths.Add(ParseShapePath(pathElement));
            }
        }

        return geometry;
    }

    private static void ParseAdjustValues(A.AdjustValueList? list, IDictionary<string, double> target)
    {
        if (list is null)
        {
            return;
        }

        foreach (var guide in list.Elements<A.ShapeGuide>())
        {
            var name = guide.Name?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var formula = guide.Formula?.Value;
            if (TryParseGuideValue(formula, out var value))
            {
                target[name] = value;
            }
        }
    }

    private static bool TryParseGuideValue(string? formula, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(formula))
        {
            return false;
        }

        var span = formula.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (char.IsDigit(ch) || ch == '-' || ch == '+')
            {
                var start = i;
                i++;
                while (i < span.Length)
                {
                    var next = span[i];
                    if (!char.IsDigit(next) && next != '.')
                    {
                        break;
                    }

                    i++;
                }

                return double.TryParse(span.Slice(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        return false;
    }

    private static ShapePath ParseShapePath(A.Path pathElement)
    {
        var path = new ShapePath
        {
            Width = pathElement.Width?.Value ?? -1,
            Height = pathElement.Height?.Value ?? -1,
            FillMode = MapPathFillMode(pathElement.Fill?.Value),
            IsStroked = pathElement.Stroke?.Value ?? true,
            IsExtrusionOk = pathElement.ExtrusionOk?.Value ?? false
        };

        foreach (var command in pathElement.ChildElements)
        {
            switch (command)
            {
                case A.MoveTo moveTo:
                    if (TryParseShapePoint(moveTo.Point, out var movePoint))
                    {
                        path.Commands.Add(new ShapeMoveToCommand(movePoint));
                    }
                    break;
                case A.LineTo lineTo:
                    if (TryParseShapePoint(lineTo.Point, out var linePoint))
                    {
                        path.Commands.Add(new ShapeLineToCommand(linePoint));
                    }
                    break;
                case A.QuadraticBezierCurveTo quadBezier:
                {
                    var points = ParseShapePoints(quadBezier, 2);
                    if (points is not null)
                    {
                        path.Commands.Add(new ShapeQuadBezierToCommand(points[0], points[1]));
                    }
                    break;
                }
                case A.CubicBezierCurveTo cubicBezier:
                {
                    var points = ParseShapePoints(cubicBezier, 3);
                    if (points is not null)
                    {
                        path.Commands.Add(new ShapeCubicBezierToCommand(points[0], points[1], points[2]));
                    }
                    break;
                }
                case A.ArcTo arcTo:
                {
                    var radiusX = arcTo.WidthRadius?.Value;
                    var radiusY = arcTo.HeightRadius?.Value;
                    var start = arcTo.StartAngle?.Value;
                    var sweep = arcTo.SwingAngle?.Value;
                    if (!string.IsNullOrWhiteSpace(radiusX)
                        && !string.IsNullOrWhiteSpace(radiusY)
                        && !string.IsNullOrWhiteSpace(start)
                        && !string.IsNullOrWhiteSpace(sweep))
                    {
                        path.Commands.Add(new ShapeArcToCommand(radiusX, radiusY, start, sweep));
                    }
                    break;
                }
                case A.CloseShapePath:
                    path.Commands.Add(new ShapeClosePathCommand());
                    break;
            }
        }

        return path;
    }

    private static bool TryParseShapePoint(A.Point? point, out ShapeAdjustPoint adjustPoint)
    {
        adjustPoint = null!;
        if (point is null)
        {
            return false;
        }

        var x = point.X?.Value;
        var y = point.Y?.Value;
        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            return false;
        }

        adjustPoint = new ShapeAdjustPoint(x, y);
        return true;
    }

    private static ShapeAdjustPoint[]? ParseShapePoints(OpenXmlCompositeElement element, int expected)
    {
        var points = new List<ShapeAdjustPoint>(expected);
        foreach (var point in element.Elements<A.Point>())
        {
            if (!TryParseShapePoint(point, out var adjust))
            {
                continue;
            }

            points.Add(adjust);
            if (points.Count == expected)
            {
                break;
            }
        }

        return points.Count == expected ? points.ToArray() : null;
    }

    private static ShapePathFillMode MapPathFillMode(A.PathFillModeValues? value)
    {
        if (value is null)
        {
            return ShapePathFillMode.Normal;
        }

        if (value == A.PathFillModeValues.None)
        {
            return ShapePathFillMode.None;
        }

        if (value == A.PathFillModeValues.Lighten)
        {
            return ShapePathFillMode.Lighten;
        }

        if (value == A.PathFillModeValues.LightenLess)
        {
            return ShapePathFillMode.LightenLess;
        }

        if (value == A.PathFillModeValues.Darken)
        {
            return ShapePathFillMode.Darken;
        }

        if (value == A.PathFillModeValues.DarkenLess)
        {
            return ShapePathFillMode.DarkenLess;
        }

        return ShapePathFillMode.Normal;
    }

    private static bool TryParsePercentage(OpenXmlElement element, string attributeName, out float value)
    {
        value = 0f;
        var text = GetAttributeValue(element, attributeName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            return false;
        }

        value = raw / 100000f;
        return true;
    }

    private static DocBorderStyle MapLineDash(A.PresetLineDashValues? dash)
    {
        if (dash is null)
        {
            return DocBorderStyle.Single;
        }

        if (dash == A.PresetLineDashValues.Dot || dash == A.PresetLineDashValues.SystemDot)
        {
            return DocBorderStyle.Dotted;
        }

        if (dash == A.PresetLineDashValues.Dash
            || dash == A.PresetLineDashValues.SystemDash
            || dash == A.PresetLineDashValues.LargeDash)
        {
            return DocBorderStyle.Dashed;
        }

        if (dash == A.PresetLineDashValues.DashDot
            || dash == A.PresetLineDashValues.SystemDashDot
            || dash == A.PresetLineDashValues.LargeDashDot)
        {
            return DocBorderStyle.DotDash;
        }

        if (dash == A.PresetLineDashValues.SystemDashDotDot
            || dash == A.PresetLineDashValues.LargeDashDotDot)
        {
            return DocBorderStyle.DotDotDash;
        }

        return DocBorderStyle.Single;
    }

    private static DocLineCap MapLineCap(A.LineCapValues value)
    {
        if (value == A.LineCapValues.Round)
        {
            return DocLineCap.Round;
        }

        if (value == A.LineCapValues.Square)
        {
            return DocLineCap.Square;
        }

        return DocLineCap.Flat;
    }

    private static DocCompoundLine MapCompoundLine(A.CompoundLineValues value)
    {
        if (value == A.CompoundLineValues.Double)
        {
            return DocCompoundLine.Double;
        }

        if (value == A.CompoundLineValues.ThickThin)
        {
            return DocCompoundLine.ThickThin;
        }

        if (value == A.CompoundLineValues.ThinThick)
        {
            return DocCompoundLine.ThinThick;
        }

        if (value == A.CompoundLineValues.Triple)
        {
            return DocCompoundLine.Triple;
        }

        return DocCompoundLine.Single;
    }

    private static float ResolveLineJoinLimit(int limit)
    {
        if (limit <= 0)
        {
            return 0f;
        }

        return limit >= 1000 ? limit / 100000f : limit;
    }

    private static DocLineArrow MapLineArrow(A.LineEndPropertiesType? end)
    {
        if (end is null)
        {
            return new DocLineArrow
            {
                Type = DocLineArrowType.None,
                Width = DocLineArrowSize.Medium,
                Length = DocLineArrowSize.Medium
            };
        }

        return new DocLineArrow
        {
            Type = MapLineArrowType(end.Type?.Value),
            Width = MapLineArrowSize(end.Width?.Value),
            Length = MapLineArrowSize(end.Length?.Value)
        };
    }

    private static DocLineArrowType MapLineArrowType(A.LineEndValues? value)
    {
        if (value == A.LineEndValues.Triangle)
        {
            return DocLineArrowType.Triangle;
        }

        if (value == A.LineEndValues.Stealth)
        {
            return DocLineArrowType.Stealth;
        }

        if (value == A.LineEndValues.Diamond)
        {
            return DocLineArrowType.Diamond;
        }

        if (value == A.LineEndValues.Oval)
        {
            return DocLineArrowType.Oval;
        }

        if (value == A.LineEndValues.Arrow)
        {
            return DocLineArrowType.Arrow;
        }

        return DocLineArrowType.None;
    }

    private static DocLineArrowSize MapLineArrowSize(A.LineEndWidthValues? value)
    {
        if (value == A.LineEndWidthValues.Large)
        {
            return DocLineArrowSize.Large;
        }

        if (value == A.LineEndWidthValues.Small)
        {
            return DocLineArrowSize.Small;
        }

        return DocLineArrowSize.Medium;
    }

    private static DocLineArrowSize MapLineArrowSize(A.LineEndLengthValues? value)
    {
        if (value == A.LineEndLengthValues.Large)
        {
            return DocLineArrowSize.Large;
        }

        if (value == A.LineEndLengthValues.Small)
        {
            return DocLineArrowSize.Small;
        }

        return DocLineArrowSize.Medium;
    }

    private static float EmuToDip(long emu)
    {
        return (float)(emu / 914400d * 96d);
    }

    private static (float Width, float Height) GetVmlSize(OpenXmlElement element)
    {
        var shape = FindVmlShapeElement(element);
        if (shape is null)
        {
            return (100f, 100f);
        }

        var styleValue = GetAttributeValue(shape, "style") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(styleValue))
        {
            return (100f, 100f);
        }

        var width = 0f;
        var height = 0f;
        var span = styleValue.AsSpan();
        var index = 0;
        while (index <= span.Length)
        {
            var next = span[index..].IndexOf(';');
            var end = next >= 0 ? index + next : span.Length;
            var segment = span.Slice(index, end - index);
            if (TrySplitStylePair(segment, out var key, out var value))
            {
                if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseVmlLengthCore(value, out var parsed))
                    {
                        width = parsed;
                    }
                }
                else if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseVmlLengthCore(value, out var parsed))
                    {
                        height = parsed;
                    }
                }
            }

            index = end + 1;
        }

        if (width <= 0f)
        {
            width = 100f;
        }

        if (height <= 0f)
        {
            height = 100f;
        }

        return (width, height);
    }

    private static OpenXmlElement? FindVmlShapeElement(OpenXmlElement element)
    {
        return element.Descendants().FirstOrDefault(IsVmlShapeElement);
    }

    private static bool IsVmlShapeElement(OpenXmlElement element)
    {
        return element.LocalName.Equals("shape", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("rect", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("roundrect", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("oval", StringComparison.OrdinalIgnoreCase)
               || element.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase);
    }

    private static OpenXmlElement? ResolveVmlShapeType(OpenXmlElement shapeElement, OpenXmlElement scopeElement)
    {
        var typeValue = GetAttributeValue(shapeElement, "type") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(typeValue))
        {
            return null;
        }

        var typeId = typeValue.Trim();
        if (typeId.StartsWith("#", StringComparison.Ordinal))
        {
            typeId = typeId[1..];
        }

        if (string.IsNullOrWhiteSpace(typeId))
        {
            return null;
        }

        return scopeElement.Descendants().FirstOrDefault(node =>
            node.LocalName.Equals("shapetype", StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetAttributeValue(node, "id"), typeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveVmlPresetGeometry(OpenXmlElement shapeElement, OpenXmlElement? shapeTypeElement)
    {
        if (shapeElement.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            return "line";
        }

        if (shapeElement.LocalName.Equals("oval", StringComparison.OrdinalIgnoreCase))
        {
            return "ellipse";
        }

        if (shapeElement.LocalName.Equals("roundrect", StringComparison.OrdinalIgnoreCase))
        {
            return "roundrect";
        }

        if (shapeElement.LocalName.Equals("rect", StringComparison.OrdinalIgnoreCase))
        {
            return "rect";
        }

        if (shapeTypeElement is not null)
        {
            if (TryParseVmlBool(GetAttributeValue(shapeTypeElement, "oned"), out var isOneD) && isOneD)
            {
                return "line";
            }

            if (shapeTypeElement.LocalName.Equals("line", StringComparison.OrdinalIgnoreCase))
            {
                return "line";
            }

            if (shapeTypeElement.LocalName.Equals("oval", StringComparison.OrdinalIgnoreCase))
            {
                return "ellipse";
            }

            if (shapeTypeElement.LocalName.Equals("roundrect", StringComparison.OrdinalIgnoreCase))
            {
                return "roundrect";
            }

            if (shapeTypeElement.LocalName.Equals("rect", StringComparison.OrdinalIgnoreCase))
            {
                return "rect";
            }

            if (TryGetVmlShapeTypeId(shapeTypeElement, out var spt))
            {
                var mapped = MapVmlShapeType(spt);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    return mapped;
                }
            }
        }

        if (TryGetVmlShapeTypeId(shapeElement, out var shapeSpt))
        {
            var mapped = MapVmlShapeType(shapeSpt);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        if (TryParseVmlBool(GetAttributeValue(shapeElement, "oned"), out var isLine) && isLine)
        {
            return "line";
        }

        return "rect";
    }

    private static bool TryGetVmlShapeTypeId(OpenXmlElement element, out int spt)
    {
        spt = 0;
        var value = GetAttributeValue(element, "spt");
        return !string.IsNullOrWhiteSpace(value)
               && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out spt);
    }

    private static string? MapVmlShapeType(int spt)
    {
        return spt switch
        {
            1 => "rect",
            2 => "roundrect",
            3 => "ellipse",
            4 => "diamond",
            5 => "parallelogram",
            6 => "trapezoid",
            7 => "hexagon",
            8 => "octagon",
            13 => "triangle",
            32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 => "line",
            202 => "rect",
            _ => null
        };
    }

    private static void ApplyVmlTransform(
        ShapeProperties properties,
        OpenXmlElement shapeElement,
        IReadOnlyDictionary<string, string> style)
    {
        if (TryParseVmlFloat(GetAttributeValue(shapeElement, "rotation"), out var rotation)
            || TryParseVmlFloat(GetStyleValue(style, "rotation"), out rotation)
            || TryParseVmlFloat(GetStyleValue(style, "mso-rotate"), out rotation))
        {
            properties.Rotation = rotation;
        }

        var flipValue = GetAttributeValue(shapeElement, "flip") ?? GetStyleValue(style, "flip") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(flipValue))
        {
            var normalized = flipValue.ToLowerInvariant();
            properties.FlipHorizontal = normalized.Contains("x", StringComparison.Ordinal);
            properties.FlipVertical = normalized.Contains("y", StringComparison.Ordinal);
        }

        if (TryParseVmlBool(GetStyleValue(style, "flipx"), out var flipX) && flipX)
        {
            properties.FlipHorizontal = true;
        }

        if (TryParseVmlBool(GetStyleValue(style, "flipy"), out var flipY) && flipY)
        {
            properties.FlipVertical = true;
        }
    }

    private static void ApplyVmlFill(
        ShapeProperties properties,
        OpenXmlElement shapeElement,
        OpenXmlElement? shapeTypeElement)
    {
        if (TryParseVmlBool(GetAttributeValue(shapeElement, "filled"), out var filled) && !filled)
        {
            properties.FillColor = null;
            return;
        }

        if (shapeTypeElement is not null
            && TryParseVmlBool(GetAttributeValue(shapeTypeElement, "filled"), out var typeFilled)
            && !typeFilled)
        {
            properties.FillColor = null;
            return;
        }

        var fillElement = shapeElement.Descendants().FirstOrDefault(node =>
            node.LocalName.Equals("fill", StringComparison.OrdinalIgnoreCase));
        var fillColorValue = GetAttributeValue(shapeElement, "fillcolor")
                            ?? (fillElement is not null ? GetAttributeValue(fillElement, "color") : null)
                            ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(fillColorValue) && TryParseVmlColor(fillColorValue, out var fill))
        {
            if (fillElement is not null
                && TryParseVmlOpacity(GetAttributeValue(fillElement, "opacity"), out var opacity))
            {
                fill = ApplyOpacity(fill, opacity);
            }

            properties.FillColor = fill;
        }
    }

    private static void ApplyVmlStroke(
        ShapeProperties properties,
        OpenXmlElement shapeElement,
        OpenXmlElement? shapeTypeElement)
    {
        if (TryParseVmlBool(GetAttributeValue(shapeElement, "stroked"), out var stroked) && !stroked)
        {
            properties.Outline = null;
            return;
        }

        if (shapeTypeElement is not null
            && TryParseVmlBool(GetAttributeValue(shapeTypeElement, "stroked"), out var typeStroked)
            && !typeStroked)
        {
            properties.Outline = null;
            return;
        }

        var strokeElement = shapeElement.Descendants().FirstOrDefault(node =>
            node.LocalName.Equals("stroke", StringComparison.OrdinalIgnoreCase));
        var strokeColorValue = GetAttributeValue(shapeElement, "strokecolor")
                              ?? (strokeElement is not null ? GetAttributeValue(strokeElement, "color") : null)
                              ?? string.Empty;
        if (string.IsNullOrWhiteSpace(strokeColorValue) || !TryParseVmlColor(strokeColorValue, out var stroke))
        {
            return;
        }

        var border = new BorderLine { Color = stroke };
        var strokeWeight = GetAttributeValue(shapeElement, "strokeweight")
                          ?? (strokeElement is not null ? GetAttributeValue(strokeElement, "weight") : null)
                          ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(strokeWeight) && TryParseVmlLength(strokeWeight, out var weight))
        {
            border.Thickness = weight;
        }

        if (strokeElement is not null
            && TryParseVmlOpacity(GetAttributeValue(strokeElement, "opacity"), out var opacity))
        {
            border.Color = ApplyOpacity(border.Color, opacity);
        }

        var dash = strokeElement is not null ? GetAttributeValue(strokeElement, "dashstyle") ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(dash))
        {
            border.Style = MapVmlDashStyle(dash);
        }

        properties.Outline = border;
    }

    private static void ApplyVmlTextBoxProperties(
        ShapeTextBoxProperties properties,
        OpenXmlElement? textBoxElement,
        IReadOnlyDictionary<string, string> shapeStyle)
    {
        var insetValue = textBoxElement is not null ? GetAttributeValue(textBoxElement, "inset") ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(insetValue) && TryParseVmlInsets(insetValue, out var padding))
        {
            properties.Padding = padding;
        }

        var anchorValue = GetStyleValue(shapeStyle, "v-text-anchor")
                         ?? (textBoxElement is not null ? GetAttributeValue(textBoxElement, "v-text-anchor") : null)
                         ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(anchorValue))
        {
            properties.VerticalAlignment = MapVmlTextAnchor(anchorValue);
        }

        var overflowValue = GetStyleValue(shapeStyle, "overflow")
                           ?? GetStyleValue(shapeStyle, "mso-text-overflow")
                           ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(overflowValue) && TryMapVmlOverflow(overflowValue, out var overflow))
        {
            properties.HorizontalOverflow = overflow;
            properties.VerticalOverflow = overflow;
        }

        var fitShapeValue = GetStyleValue(shapeStyle, "mso-fit-shape-to-text") ?? string.Empty;
        var fitTextValue = GetStyleValue(shapeStyle, "mso-fit-text-to-shape") ?? string.Empty;
        if (TryParseVmlBool(fitShapeValue, out var fitShape) && fitShape)
        {
            properties.AutoFit = ShapeTextAutoFit.ShapeToFitText;
        }
        else if (TryParseVmlBool(fitTextValue, out var fitText) && fitText)
        {
            properties.AutoFit = ShapeTextAutoFit.TextToFitShape;
        }

        var textDirectionValue = GetStyleValue(shapeStyle, "writing-mode")
                                 ?? GetStyleValue(shapeStyle, "mso-text-direction-alt")
                                 ?? GetStyleValue(shapeStyle, "mso-layout-flow-alt")
                                 ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(textDirectionValue)
            && TryMapVmlTextDirection(textDirectionValue, out var textDirection))
        {
            properties.TextDirection = textDirection;
        }
    }

    private static ShapeTextVerticalAlignment MapVmlTextAnchor(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "center" or "middle" => ShapeTextVerticalAlignment.Center,
            "bottom" => ShapeTextVerticalAlignment.Bottom,
            _ => ShapeTextVerticalAlignment.Top
        };
    }

    private static bool TryMapVmlOverflow(string value, out ShapeTextOverflow overflow)
    {
        overflow = ShapeTextOverflow.Overflow;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized == "clip" || normalized == "hidden")
        {
            overflow = ShapeTextOverflow.Clip;
            return true;
        }

        if (normalized == "ellipsis")
        {
            overflow = ShapeTextOverflow.Ellipsis;
            return true;
        }

        if (normalized == "visible" || normalized == "auto")
        {
            overflow = ShapeTextOverflow.Overflow;
            return true;
        }

        return false;
    }

    private static bool TryMapVmlTextDirection(string value, out DocTextDirection direction)
    {
        direction = DocTextDirection.LeftToRightTopToBottom;
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "tb-rl":
            case "tbrl":
            case "top-to-bottom":
                direction = DocTextDirection.TopToBottomRightToLeft;
                return true;
            case "bt-lr":
            case "btlr":
                direction = DocTextDirection.BottomToTopLeftToRight;
                return true;
            case "lr-tb":
            case "lrtb":
            case "horizontal":
                direction = DocTextDirection.LeftToRightTopToBottom;
                return true;
            case "tb-rl-v":
            case "tbrlv":
                direction = DocTextDirection.TopToBottomRightToLeftRotated;
                return true;
            case "tb-lr":
            case "tblr":
                direction = DocTextDirection.TopToBottomLeftToRightRotated;
                return true;
            case "lr-tb-v":
            case "lrtbv":
                direction = DocTextDirection.LeftToRightTopToBottomRotated;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseVmlInsets(string value, out DocThickness padding)
    {
        padding = DocThickness.Uniform(0f);
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        Span<float> lengths = stackalloc float[4];
        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = float.NaN;
        }

        var count = Math.Min(parts.Length, 4);
        for (var i = 0; i < count; i++)
        {
            if (TryParseVmlLength(parts[i], out var dip))
            {
                lengths[i] = dip;
            }
        }

        if (count == 1)
        {
            if (!float.IsNaN(lengths[0]))
            {
                padding = DocThickness.Uniform(lengths[0]);
                return true;
            }

            return false;
        }

        if (count == 2)
        {
            if (!float.IsNaN(lengths[0]) && !float.IsNaN(lengths[1]))
            {
                padding = new DocThickness(lengths[0], lengths[1], lengths[0], lengths[1]);
                return true;
            }

            return false;
        }

        var left = !float.IsNaN(lengths[0]) ? lengths[0] : 0f;
        var top = !float.IsNaN(lengths[1]) ? lengths[1] : left;
        var right = !float.IsNaN(lengths[2]) ? lengths[2] : left;
        var bottom = !float.IsNaN(lengths[3]) ? lengths[3] : top;
        padding = new DocThickness(left, top, right, bottom);
        return true;
    }

    private static bool TryParseVmlBool(string? value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("t", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (trimmed.Equals("f", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }

    private static bool TryParseVmlOpacity(string? value, out float opacity)
    {
        opacity = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            var percentValue = trimmed[..^1];
            if (float.TryParse(percentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                opacity = Math.Clamp(percent / 100f, 0f, 1f);
                return true;
            }
        }

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            opacity = numeric > 1f ? numeric / 100f : numeric;
            opacity = Math.Clamp(opacity, 0f, 1f);
            return true;
        }

        return false;
    }

    private static bool TryParseVmlFloat(string? value, out float result)
    {
        result = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static string? GetStyleValue(IReadOnlyDictionary<string, string> style, string key)
    {
        return style.TryGetValue(key, out var value) ? value : null;
    }

    private static DocColor ApplyOpacity(DocColor color, float opacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return new DocColor(color.R, color.G, color.B, alpha);
    }

    private static DocBorderStyle MapVmlDashStyle(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "dash" or "longdash" or "longdashdot" or "longdashdotdot" => DocBorderStyle.Dashed,
            "dot" or "shortdot" => DocBorderStyle.Dotted,
            "dashdot" => DocBorderStyle.DotDash,
            "dashdotdot" => DocBorderStyle.DotDotDash,
            _ => DocBorderStyle.Single
        };
    }

    private static bool TryParseVmlLength(string value, out float dip)
    {
        return TryParseVmlLengthCore(value.AsSpan(), out dip);
    }

    private static bool TryParseVmlLengthCore(ReadOnlySpan<char> value, out float dip)
    {
        dip = 0f;
        var trimmed = value.Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        var index = 0;
        while (index < trimmed.Length && (char.IsDigit(trimmed[index]) || trimmed[index] == '.' || trimmed[index] == '-'))
        {
            index++;
        }

        if (index == 0)
        {
            return false;
        }

        if (!float.TryParse(trimmed[..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var unit = trimmed[index..].Trim();
        if (unit.IsEmpty)
        {
            dip = number;
            return true;
        }

        if (unit.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            dip = number * 96f;
            return true;
        }

        if (unit.Equals("pt", StringComparison.OrdinalIgnoreCase))
        {
            dip = number * 96f / 72f;
            return true;
        }

        if (unit.Equals("px", StringComparison.OrdinalIgnoreCase))
        {
            dip = number;
            return true;
        }

        if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase))
        {
            dip = number * 96f / 2.54f;
            return true;
        }

        if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase))
        {
            dip = number * 96f / 25.4f;
            return true;
        }

        dip = number;
        return true;
    }

    private static bool TryParseVmlColor(string value, out DocColor color)
    {
        color = DocColor.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return TryParseHexColor(trimmed, out color);
    }

    private sealed class ImageResolver
    {
        private readonly OpenXmlPart? _part;
        private readonly DocumentThemeColorMap? _themeColors;
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public ImageResolver(OpenXmlPart? part, DocumentThemeColorMap? themeColors = null)
        {
            _part = part;
            _themeColors = themeColors;
        }

        public OpenXmlPart? Part => _part;

        public ImageInline? TryCreateImage(Drawing drawing)
        {
            if (_part is null)
            {
                return null;
            }

            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            var effects = ParseDrawingEffects(drawing, _themeColors);
            var colorEffects = ParseBlipColorEffects(blip, _themeColors);
            if (colorEffects is not null)
            {
                effects ??= new DrawingEffects();
                effects.Color = colorEffects;
            }

            var crop = ParseImageCrop(ResolveDrawingSourceRectangle(drawing));

            var svgEmbed = TryGetSvgEmbedId(drawing);
            if (!string.IsNullOrWhiteSpace(svgEmbed)
                && _part.GetPartById(svgEmbed) is ImagePart svgPart)
            {
                var image = CreateImageFromPart(svgPart, drawing);
                image.Effects = effects;
                image.Crop = crop;
                ApplyImageTransform(image, drawing);
                return image;
            }

            var embed = blip?.Embed?.Value;
            if (string.IsNullOrWhiteSpace(embed))
            {
                var placeholder = CreateDrawingPlaceholder(drawing);
                if (placeholder is not null)
                {
                    placeholder.Effects = effects;
                    placeholder.Crop = crop;
                    ApplyImageTransform(placeholder, drawing);
                }

                return placeholder;
            }

            if (_part.GetPartById(embed) is not ImagePart imagePart)
            {
                var placeholder = CreateDrawingPlaceholder(drawing);
                if (placeholder is not null)
                {
                    placeholder.Effects = effects;
                    placeholder.Crop = crop;
                    ApplyImageTransform(placeholder, drawing);
                }

                return placeholder;
            }

            var inline = CreateImageFromPart(imagePart, drawing);
            inline.Effects = effects;
            inline.Crop = crop;
            ApplyImageTransform(inline, drawing);
            return inline;
        }

        public ImageInline? TryCreateImageFromVml(OpenXmlElement element)
        {
            if (_part is null)
            {
                return null;
            }

            var (width, height) = GetVmlSize(element);
            var imageData = element.Descendants()
                .FirstOrDefault(node => node.LocalName.Equals("imagedata", StringComparison.OrdinalIgnoreCase));
            if (imageData is null)
            {
                return new ImageInline(Array.Empty<byte>(), width, height, "application/vnd.ms-office.ole");
            }

            var idAttribute = GetAttributeValue(imageData, "id", RelationshipNamespace);
            if (string.IsNullOrWhiteSpace(idAttribute))
            {
                return new ImageInline(Array.Empty<byte>(), width, height, "application/vnd.ms-office.ole");
            }

            if (_part.GetPartById(idAttribute) is not ImagePart imagePart)
            {
                return new ImageInline(Array.Empty<byte>(), width, height, "application/vnd.ms-office.ole");
            }

            using var stream = imagePart.GetStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();

            var inline = new ImageInline(data, width, height, imagePart.ContentType)
            {
                Effects = ParseDrawingEffects(element, _themeColors)
            };

            ApplyVmlImageTransform(inline, element);
            return inline;
        }

        private static float EmuToDip(long emu)
        {
            return (float)(emu / 914400d * 96d);
        }

        private static ImageInline? CreateDrawingPlaceholder(Drawing drawing)
        {
            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 100f;
            var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 100f;
            var contentType = ResolveDrawingContentType(drawing);
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return null;
            }

            return new ImageInline(Array.Empty<byte>(), width, height, contentType);
        }

        private static void ApplyImageTransform(ImageInline image, Drawing drawing)
        {
            var transform = drawing.Descendants<PIC.ShapeProperties>()
                .Select(props => props.GetFirstChild<A.Transform2D>())
                .FirstOrDefault(value => value is not null);
            if (transform?.Rotation?.Value is int rotation)
            {
                image.Rotation = rotation / 60000f;
            }
        }

        private static void ApplyVmlImageTransform(ImageInline image, OpenXmlElement element)
        {
            var shape = FindVmlShapeElement(element);
            if (shape is null)
            {
                return;
            }

            var styleValue = GetAttributeValue(shape, "style") ?? string.Empty;
            var style = ParseVmlStyle(styleValue);
            if (TryParseVmlFloat(GetAttributeValue(shape, "rotation"), out var rotation)
                || TryParseVmlFloat(GetStyleValue(style, "rotation"), out rotation)
                || TryParseVmlFloat(GetStyleValue(style, "mso-rotate"), out rotation))
            {
                image.Rotation = rotation;
            }
        }

        private static string? TryGetSvgEmbedId(Drawing drawing)
        {
            var svgBlip = drawing.Descendants()
                .FirstOrDefault(element => element.LocalName.Equals("svgBlip", StringComparison.OrdinalIgnoreCase));
            if (svgBlip is null)
            {
                return null;
            }

            var embed = svgBlip.GetAttribute("embed", RelationshipNamespace).Value;
            if (string.IsNullOrWhiteSpace(embed))
            {
                embed = svgBlip.GetAttribute("link", RelationshipNamespace).Value;
            }

            return string.IsNullOrWhiteSpace(embed) ? null : embed;
        }

        private static ImageInline CreateImageFromPart(ImagePart imagePart, Drawing drawing)
        {
            using var stream = imagePart.GetStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();

            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 100f;
            var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 100f;

            return new ImageInline(data, width, height, imagePart.ContentType);
        }

        private static string? ResolveDrawingContentType(Drawing drawing)
        {
            var graphicData = drawing.Descendants<DocumentFormat.OpenXml.Drawing.GraphicData>().FirstOrDefault();
            var uri = graphicData?.Uri?.Value;
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            if (uri.Contains("drawingml/chart", StringComparison.OrdinalIgnoreCase))
            {
                return "application/vnd.openxmlformats-officedocument.drawingml.chart";
            }

            if (uri.Contains("drawingml/diagram", StringComparison.OrdinalIgnoreCase))
            {
                return "application/vnd.openxmlformats-officedocument.drawingml.diagram";
            }

            if (uri.Contains("wordprocessingShape", StringComparison.OrdinalIgnoreCase))
            {
                return "application/vnd.openxmlformats-officedocument.drawingml.shape";
            }

            return "application/vnd.openxmlformats-officedocument.oleObject";
        }

        private static (float Width, float Height) GetVmlSize(OpenXmlElement element)
        {
            var shape = FindVmlShapeElement(element);
            if (shape is null)
            {
                return (100f, 100f);
            }

            var styleValue = GetAttributeValue(shape, "style") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(styleValue))
            {
                return (100f, 100f);
            }

            var width = 0f;
            var height = 0f;
            var span = styleValue.AsSpan();
            var index = 0;
            while (index <= span.Length)
            {
                var next = span[index..].IndexOf(';');
                var end = next >= 0 ? index + next : span.Length;
                var segment = span.Slice(index, end - index);
                if (TrySplitStylePair(segment, out var key, out var value))
                {
                    if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseVmlLengthCore(value, out var parsed))
                        {
                            width = parsed;
                        }
                    }
                    else if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseVmlLengthCore(value, out var parsed))
                        {
                            height = parsed;
                        }
                    }
                }

                index = end + 1;
            }

            if (width <= 0f)
            {
                width = 100f;
            }

            if (height <= 0f)
            {
                height = 100f;
            }

            return (width, height);
        }

        private static bool TryParseVmlLength(string value, out float dip)
        {
            return TryParseVmlLengthCore(value.AsSpan(), out dip);
        }
    }

    private sealed class ChartResolver
    {
        private readonly OpenXmlPart? _part;
        private readonly DocumentThemeColorMap? _themeColors;

        public ChartResolver(OpenXmlPart? part, DocumentThemeColorMap? themeColors = null)
        {
            _part = part;
            _themeColors = themeColors;
        }

        public ChartInline? TryCreateChart(Drawing drawing)
        {
            if (_part is null)
            {
                return null;
            }

            var chartRef = drawing.Descendants<C.ChartReference>().FirstOrDefault();
            var relId = chartRef?.Id?.Value;
            if (string.IsNullOrWhiteSpace(relId))
            {
                return null;
            }

            if (_part.GetPartById(relId) is not ChartPart chartPart)
            {
                return null;
            }

            using var stream = chartPart.GetStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();

            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 100f;
            var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 100f;
            var model = TryParseChartModel(chartPart, _themeColors);
            var chart = new ChartInline(width, height, model, data)
            {
                Name = drawing.Descendants<DW.DocProperties>().FirstOrDefault()?.Name?.Value
            };

            return chart;
        }
    }

    private static ChartModel? TryParseChartModel(ChartPart chartPart, DocumentThemeColorMap? themeColors)
    {
        var chartSpace = chartPart.ChartSpace;
        var chart = chartSpace?.GetFirstChild<C.Chart>();
        if (chart is null)
        {
            return null;
        }

        var model = new ChartModel
        {
            Title = ExtractChartTitle(chart),
            ChartAreaStyle = ParseChartStyle(chartSpace?.GetFirstChild<C.ShapeProperties>(), themeColors),
            PlotAreaStyle = ParseChartStyle(chart.PlotArea?.GetFirstChild<C.ShapeProperties>(), themeColors)
        };

        var plotArea = chart.PlotArea;
        if (plotArea is null)
        {
            return model;
        }

        OpenXmlElement? chartElement = null;

        if (plotArea.GetFirstChild<C.BarChart>() is { } barChart)
        {
            model.Type = ChartType.Bar;
            model.BarDirection = ParseBarDirection(barChart.GetFirstChild<C.BarDirection>()?.Val?.Value);
            model.Stacking = ParseBarGrouping(barChart.GetFirstChild<C.BarGrouping>()?.Val?.Value);
            AddCategorySeries(barChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = barChart;
        }
        else if (plotArea.GetFirstChild<C.LineChart>() is { } lineChart)
        {
            model.Type = ChartType.Line;
            model.Stacking = ParseGrouping(lineChart.GetFirstChild<C.Grouping>()?.Val?.Value);
            AddCategorySeries(lineChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = lineChart;
        }
        else if (plotArea.GetFirstChild<C.PieChart>() is { } pieChart)
        {
            model.Type = ChartType.Pie;
            AddCategorySeries(pieChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors, forcePointPalette: true);
            chartElement = pieChart;
        }
        else if (plotArea.GetFirstChild<C.DoughnutChart>() is { } doughnutChart)
        {
            model.Type = ChartType.Doughnut;
            model.DoughnutHoleSize = ParseHoleSize(doughnutChart.GetFirstChild<C.HoleSize>()?.Val?.Value);
            AddCategorySeries(doughnutChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors, forcePointPalette: true);
            chartElement = doughnutChart;
        }
        else if (plotArea.GetFirstChild<C.ScatterChart>() is { } scatterChart)
        {
            model.Type = ChartType.Scatter;
            AddScatterSeries(scatterChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = scatterChart;
        }
        else if (plotArea.GetFirstChild<C.BubbleChart>() is { } bubbleChart)
        {
            model.Type = ChartType.Bubble;
            AddBubbleSeries(bubbleChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = bubbleChart;
        }
        else if (plotArea.GetFirstChild<C.RadarChart>() is { } radarChart)
        {
            model.Type = ChartType.Radar;
            model.RadarStyle = ParseRadarStyle(radarChart.GetFirstChild<C.RadarStyle>()?.Val?.Value);
            AddCategorySeries(radarChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = radarChart;
        }
        else if (plotArea.GetFirstChild<C.AreaChart>() is { } areaChart)
        {
            model.Type = ChartType.Area;
            model.Stacking = ParseGrouping(areaChart.GetFirstChild<C.Grouping>()?.Val?.Value);
            AddCategorySeries(areaChart, model, themeColors);
            ApplyDefaultSeriesColors(model, themeColors);
            chartElement = areaChart;
        }
        else
        {
            model.Type = ChartType.Unknown;
            return model;
        }

        if (chartElement is not null)
        {
            model.DataLabels = ParseChartDataLabels(chartElement.GetFirstChild<C.DataLabels>(), themeColors);
        }

        AddChartAxes(plotArea, model, themeColors);
        model.Legend = ParseChartLegend(chart.Legend, themeColors);
        return model;
    }

    private static string? ExtractChartTitle(C.Chart chart)
    {
        return ExtractTitleText(chart.Title);
    }

    private static void AddCategorySeries(OpenXmlElement chartElement, ChartModel model, DocumentThemeColorMap? themeColors)
    {
        foreach (var seriesElement in chartElement.Elements().Where(element => element.LocalName == "ser"))
        {
            var series = new ChartSeries
            {
                Name = ExtractSeriesName(seriesElement),
                Style = ParseChartStyle(seriesElement.GetFirstChild<C.ShapeProperties>(), themeColors),
                DataLabels = ParseChartDataLabels(seriesElement.GetFirstChild<C.DataLabels>(), themeColors)
            };

            var categories = ExtractCategories(seriesElement);
            var values = ExtractValues(seriesElement);
            var pointCount = Math.Max(categories.Count, values.Count);
            for (var i = 0; i < pointCount; i++)
            {
                var point = new ChartPoint
                {
                    Category = i < categories.Count ? categories[i] : null,
                    Value = i < values.Count ? values[i] : 0d
                };
                series.Points.Add(point);
            }

            ApplyPointStyles(seriesElement, series, themeColors);
            ApplyPointDataLabels(seriesElement, series, themeColors);

            if (series.Points.Count > 0)
            {
                model.Series.Add(series);
            }
        }
    }

    private static void AddScatterSeries(OpenXmlElement chartElement, ChartModel model, DocumentThemeColorMap? themeColors)
    {
        foreach (var seriesElement in chartElement.Elements().Where(element => element.LocalName == "ser"))
        {
            var series = new ChartSeries
            {
                Name = ExtractSeriesName(seriesElement),
                Style = ParseChartStyle(seriesElement.GetFirstChild<C.ShapeProperties>(), themeColors),
                DataLabels = ParseChartDataLabels(seriesElement.GetFirstChild<C.DataLabels>(), themeColors)
            };

            var xValues = ExtractXValues(seriesElement);
            var yValues = ExtractYValues(seriesElement);
            var pointCount = Math.Max(xValues.Count, yValues.Count);
            for (var i = 0; i < pointCount; i++)
            {
                var point = new ChartPoint
                {
                    XValue = i < xValues.Count ? xValues[i] : (double?)null,
                    Value = i < yValues.Count ? yValues[i] : 0d
                };
                series.Points.Add(point);
            }

            ApplyPointStyles(seriesElement, series, themeColors);
            ApplyPointDataLabels(seriesElement, series, themeColors);

            if (series.Points.Count > 0)
            {
                model.Series.Add(series);
            }
        }
    }

    private static void AddBubbleSeries(OpenXmlElement chartElement, ChartModel model, DocumentThemeColorMap? themeColors)
    {
        foreach (var seriesElement in chartElement.Elements().Where(element => element.LocalName == "ser"))
        {
            var series = new ChartSeries
            {
                Name = ExtractSeriesName(seriesElement),
                Style = ParseChartStyle(seriesElement.GetFirstChild<C.ShapeProperties>(), themeColors),
                DataLabels = ParseChartDataLabels(seriesElement.GetFirstChild<C.DataLabels>(), themeColors)
            };

            var xValues = ExtractXValues(seriesElement);
            var yValues = ExtractYValues(seriesElement);
            var sizes = ExtractBubbleSizes(seriesElement);
            var pointCount = Math.Max(xValues.Count, Math.Max(yValues.Count, sizes.Count));
            for (var i = 0; i < pointCount; i++)
            {
                var point = new ChartPoint
                {
                    XValue = i < xValues.Count ? xValues[i] : (double?)null,
                    Value = i < yValues.Count ? yValues[i] : 0d,
                    Size = i < sizes.Count ? sizes[i] : (double?)null
                };
                series.Points.Add(point);
            }

            ApplyPointStyles(seriesElement, series, themeColors);
            ApplyPointDataLabels(seriesElement, series, themeColors);

            if (series.Points.Count > 0)
            {
                model.Series.Add(series);
            }
        }
    }

    private static string? ExtractSeriesName(OpenXmlElement seriesElement)
    {
        var text = seriesElement.Descendants<C.SeriesText>().FirstOrDefault();
        if (text is null)
        {
            return null;
        }

        var value = text.Descendants<C.NumericValue>().FirstOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var inner = text.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(inner) ? null : inner;
    }

    private static List<string> ExtractCategories(OpenXmlElement seriesElement)
    {
        OpenXmlElement? categoryData = seriesElement.Descendants<C.CategoryAxisData>().FirstOrDefault();
        categoryData ??= seriesElement.Descendants<C.XValues>().FirstOrDefault();
        var categories = ReadStringCache(categoryData);
        if (categories.Count > 0)
        {
            return categories;
        }

        var numbers = ReadNumberCache(categoryData);
        return numbers.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToList();
    }

    private static List<double> ExtractValues(OpenXmlElement seriesElement)
    {
        OpenXmlElement? valuesElement = seriesElement.Descendants<C.Values>().FirstOrDefault();
        valuesElement ??= seriesElement.Descendants<C.YValues>().FirstOrDefault();
        return ReadNumberCache(valuesElement);
    }

    private static List<double> ExtractXValues(OpenXmlElement seriesElement)
    {
        var valuesElement = seriesElement.Descendants<C.XValues>().FirstOrDefault();
        return ReadNumberCache(valuesElement);
    }

    private static List<double> ExtractYValues(OpenXmlElement seriesElement)
    {
        var valuesElement = seriesElement.Descendants<C.YValues>().FirstOrDefault();
        return ReadNumberCache(valuesElement);
    }

    private static List<double> ExtractBubbleSizes(OpenXmlElement seriesElement)
    {
        var valuesElement = seriesElement.Descendants<C.BubbleSize>().FirstOrDefault();
        return ReadNumberCache(valuesElement);
    }

    private static void ApplyPointStyles(OpenXmlElement seriesElement, ChartSeries series, DocumentThemeColorMap? themeColors)
    {
        foreach (var dataPoint in seriesElement.Descendants<C.DataPoint>())
        {
            var indexValue = dataPoint.Index?.Val?.Value;
            if (!indexValue.HasValue)
            {
                continue;
            }

            var index = (int)indexValue.Value;
            if (index < 0 || index >= series.Points.Count)
            {
                continue;
            }

            var style = ParseChartStyle(dataPoint.GetFirstChild<C.ShapeProperties>(), themeColors);
            if (style is not null)
            {
                series.Points[index].Style = style;
            }
        }
    }

    private static void ApplyPointDataLabels(OpenXmlElement seriesElement, ChartSeries series, DocumentThemeColorMap? themeColors)
    {
        foreach (var dataLabel in seriesElement.Descendants<C.DataLabel>())
        {
            var indexValue = dataLabel.Index?.Val?.Value;
            if (!indexValue.HasValue)
            {
                continue;
            }

            var index = (int)indexValue.Value;
            if (index < 0 || index >= series.Points.Count)
            {
                continue;
            }

            var label = ParseChartDataLabels(dataLabel, themeColors);
            if (label is null)
            {
                var isHidden = dataLabel.GetFirstChild<C.Delete>()?.Val?.Value;
                if (isHidden == true)
                {
                    series.Points[index].DataLabel = new ChartDataLabelSettings { IsHidden = true };
                }

                continue;
            }

            series.Points[index].DataLabel = label;
        }
    }

    private static ChartStyle? ParseChartStyle(OpenXmlElement? shapeProperties, DocumentThemeColorMap? themeColors)
    {
        if (shapeProperties is null)
        {
            return null;
        }

        var fill = ParseChartFillStyle(shapeProperties, themeColors);
        var line = ParseChartLineStyle(shapeProperties, themeColors);
        var effects = ParseChartEffects(shapeProperties, themeColors);
        if (fill is null && line is null && effects is null)
        {
            return null;
        }

        return new ChartStyle
        {
            Fill = fill,
            Line = line,
            Effects = effects
        };
    }

    private static ChartFillStyle? ParseChartFillStyle(OpenXmlElement shapeProperties, DocumentThemeColorMap? themeColors)
    {
        if (shapeProperties.GetFirstChild<A.NoFill>() is not null)
        {
            return new ChartFillStyle { IsNone = true };
        }

        var solidFill = shapeProperties.GetFirstChild<A.SolidFill>();
        if (solidFill is not null)
        {
            var color = TryResolveDrawingColor(solidFill, themeColors);
            if (color.HasValue)
            {
                return new ChartFillStyle { Color = color.Value };
            }
        }

        var gradientFill = shapeProperties.GetFirstChild<A.GradientFill>();
        if (gradientFill is not null)
        {
            var stop = gradientFill.Descendants<A.GradientStop>().FirstOrDefault();
            var color = TryResolveDrawingColor(stop, themeColors);
            if (color.HasValue)
            {
                return new ChartFillStyle { Color = color.Value };
            }
        }

        return null;
    }

    private static ChartLineStyle? ParseChartLineStyle(OpenXmlElement shapeProperties, DocumentThemeColorMap? themeColors)
    {
        var outline = shapeProperties.GetFirstChild<A.Outline>();
        if (outline is null)
        {
            return null;
        }

        if (outline.GetFirstChild<A.NoFill>() is not null)
        {
            return new ChartLineStyle { IsNone = true };
        }

        var line = new ChartLineStyle();
        if (outline.Width?.Value is int width)
        {
            line.Width = EmuToDip(width);
        }

        var color = TryResolveDrawingColor(outline.GetFirstChild<A.SolidFill>(), themeColors);
        if (color.HasValue)
        {
            line.Color = color.Value;
        }

        var dash = outline.GetFirstChild<A.PresetDash>()?.Val?.Value;
        line.Style = MapLineDash(dash);
        return line;
    }

    private static ChartEffectStyle? ParseChartEffects(OpenXmlElement shapeProperties, DocumentThemeColorMap? themeColors)
    {
        var effectList = shapeProperties.ChildElements.FirstOrDefault(child => child.LocalName.Equals("effectLst", StringComparison.OrdinalIgnoreCase));
        if (effectList is null)
        {
            return null;
        }

        var outerShadow = effectList.ChildElements.FirstOrDefault(child => child.LocalName.Equals("outerShdw", StringComparison.OrdinalIgnoreCase));
        if (outerShadow is null)
        {
            return null;
        }

        var shadow = new ChartShadowEffect();
        if (TryParseEmu(outerShadow, "blurRad", out var blur))
        {
            shadow.BlurRadius = EmuToDip(blur);
        }

        if (TryParseEmu(outerShadow, "dist", out var distance))
        {
            shadow.Distance = EmuToDip(distance);
        }

        if (TryParseIntAttribute(outerShadow, "dir", out var direction))
        {
            shadow.Direction = direction / 60000f;
        }

        var shadowColor = TryResolveDrawingColor(outerShadow, themeColors);
        if (shadowColor.HasValue)
        {
            shadow.Color = shadowColor.Value;
        }

        return new ChartEffectStyle { Shadow = shadow };
    }

    private static void AddChartAxes(C.PlotArea plotArea, ChartModel model, DocumentThemeColorMap? themeColors)
    {
        foreach (var axis in plotArea.Elements<C.CategoryAxis>())
        {
            model.Axes.Add(ParseChartAxis(axis, ChartAxisKind.Category, themeColors));
        }

        foreach (var axis in plotArea.Elements<C.DateAxis>())
        {
            model.Axes.Add(ParseChartAxis(axis, ChartAxisKind.Category, themeColors));
        }

        foreach (var axis in plotArea.Elements<C.ValueAxis>())
        {
            model.Axes.Add(ParseChartAxis(axis, ChartAxisKind.Value, themeColors));
        }

        foreach (var axis in plotArea.Elements<C.SeriesAxis>())
        {
            model.Axes.Add(ParseChartAxis(axis, ChartAxisKind.Category, themeColors));
        }
    }

    private static ChartAxis ParseChartAxis(OpenXmlElement axisElement, ChartAxisKind kind, DocumentThemeColorMap? themeColors)
    {
        var axis = new ChartAxis
        {
            Kind = kind
        };

        axis.AxisId = axisElement.GetFirstChild<C.AxisId>()?.Val?.Value;
        axis.CrossAxisId = axisElement.GetFirstChild<C.CrossingAxis>()?.Val?.Value;
        axis.Position = ParseAxisPosition(axisElement.GetFirstChild<C.AxisPosition>()?.Val?.Value);

        var isDeleted = axisElement.GetFirstChild<C.Delete>()?.Val?.Value;
        axis.IsVisible = !(isDeleted ?? false);

        var scaling = axisElement.GetFirstChild<C.Scaling>();
        axis.Minimum = scaling?.GetFirstChild<C.MinAxisValue>()?.Val?.Value;
        axis.Maximum = scaling?.GetFirstChild<C.MaxAxisValue>()?.Val?.Value;

        axis.MajorUnit = axisElement.GetFirstChild<C.MajorUnit>()?.Val?.Value;
        axis.MinorUnit = axisElement.GetFirstChild<C.MinorUnit>()?.Val?.Value;

        axis.MajorTickMark = ParseTickMark(axisElement.GetFirstChild<C.MajorTickMark>()?.Val?.Value);
        axis.MinorTickMark = ParseTickMark(axisElement.GetFirstChild<C.MinorTickMark>()?.Val?.Value);
        axis.TickLabelPosition = ParseTickLabelPosition(axisElement.GetFirstChild<C.TickLabelPosition>()?.Val?.Value);

        axis.NumberFormat = axisElement.GetFirstChild<C.NumberingFormat>()?.FormatCode?.Value;
        axis.Title = ExtractTitleText(axisElement.GetFirstChild<C.Title>());

        var axisShape = axisElement.GetFirstChild<C.ShapeProperties>();
        if (axisShape is not null)
        {
            axis.LineStyle = ParseChartLineStyle(axisShape, themeColors);
        }

        var majorGridShape = axisElement.GetFirstChild<C.MajorGridlines>()?.GetFirstChild<C.ShapeProperties>();
        if (majorGridShape is not null)
        {
            axis.MajorGridlineStyle = ParseChartLineStyle(majorGridShape, themeColors);
        }

        var minorGridShape = axisElement.GetFirstChild<C.MinorGridlines>()?.GetFirstChild<C.ShapeProperties>();
        if (minorGridShape is not null)
        {
            axis.MinorGridlineStyle = ParseChartLineStyle(minorGridShape, themeColors);
        }

        axis.LabelTextStyle = ParseChartTextStyle(axisElement.GetFirstChild<C.TextProperties>(), themeColors);
        axis.TitleTextStyle = ParseChartTextStyle(axisElement.GetFirstChild<C.Title>()?.GetFirstChild<C.TextProperties>(), themeColors);

        return axis;
    }

    private static ChartLegend? ParseChartLegend(C.Legend? legend, DocumentThemeColorMap? themeColors)
    {
        if (legend is null)
        {
            return null;
        }

        var delete = legend.GetFirstChild<C.Delete>()?.Val?.Value ?? false;
        var legendModel = new ChartLegend
        {
            IsVisible = !delete,
            Position = ParseLegendPosition(legend.GetFirstChild<C.LegendPosition>()?.Val?.Value),
            Overlay = legend.GetFirstChild<C.Overlay>()?.Val?.Value ?? false,
            TextStyle = ParseChartTextStyle(legend.GetFirstChild<C.TextProperties>(), themeColors)
        };

        return legendModel;
    }

    private static ChartDataLabelSettings? ParseChartDataLabels(OpenXmlElement? element, DocumentThemeColorMap? themeColors)
    {
        if (element is null)
        {
            return null;
        }

        var labels = new ChartDataLabelSettings();
        var hasValues = false;

        var delete = element.GetFirstChild<C.Delete>()?.Val?.Value;
        if (delete.HasValue)
        {
            labels.IsHidden = delete.Value;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowValue>()?.Val?.Value is { } showValue)
        {
            labels.ShowValue = showValue;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowCategoryName>()?.Val?.Value is { } showCategory)
        {
            labels.ShowCategoryName = showCategory;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowSeriesName>()?.Val?.Value is { } showSeries)
        {
            labels.ShowSeriesName = showSeries;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowPercent>()?.Val?.Value is { } showPercent)
        {
            labels.ShowPercent = showPercent;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowBubbleSize>()?.Val?.Value is { } showBubble)
        {
            labels.ShowBubbleSize = showBubble;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowLegendKey>()?.Val?.Value is { } showLegendKey)
        {
            labels.ShowLegendKey = showLegendKey;
            hasValues = true;
        }

        if (element.GetFirstChild<C.ShowLeaderLines>()?.Val?.Value is { } showLeaderLines)
        {
            labels.ShowLeaderLines = showLeaderLines;
            hasValues = true;
        }

        if (element.GetFirstChild<C.DataLabelPosition>()?.Val?.Value is { } position)
        {
            labels.Position = ParseDataLabelPosition(position);
            hasValues = true;
        }

        var format = element.GetFirstChild<C.NumberingFormat>()?.FormatCode?.Value;
        if (!string.IsNullOrWhiteSpace(format))
        {
            labels.NumberFormat = format;
            hasValues = true;
        }

        var textStyle = ParseChartTextStyle(element.GetFirstChild<C.TextProperties>(), themeColors);
        if (textStyle is not null)
        {
            labels.TextStyle = textStyle;
            hasValues = true;
        }

        var shapeStyle = ParseChartStyle(element.GetFirstChild<C.ShapeProperties>(), themeColors);
        if (shapeStyle is not null)
        {
            labels.ShapeStyle = shapeStyle;
            hasValues = true;
        }

        return hasValues ? labels : null;
    }

    private static ChartTextStyle? ParseChartTextStyle(OpenXmlElement? textProperties, DocumentThemeColorMap? themeColors)
    {
        if (textProperties is null)
        {
            return null;
        }

        OpenXmlElement? runProperties = textProperties.Descendants<A.DefaultRunProperties>().FirstOrDefault();
        runProperties ??= textProperties.Descendants<A.RunProperties>().FirstOrDefault();

        if (runProperties is null)
        {
            return null;
        }

        var style = new ChartTextStyle();
        var hasValues = false;

        if (TryParseIntAttribute(runProperties, "sz", out var fontSize))
        {
            style.FontSize = ChartFontSizeToDip(fontSize);
            hasValues = true;
        }

        var bold = TryParseBoolAttribute(runProperties, "b");
        if (bold.HasValue)
        {
            style.Bold = bold.Value;
            hasValues = true;
        }

        var italic = TryParseBoolAttribute(runProperties, "i");
        if (italic.HasValue)
        {
            style.Italic = italic.Value;
            hasValues = true;
        }

        var font = runProperties.GetFirstChild<A.LatinFont>()?.Typeface?.Value
                   ?? runProperties.GetFirstChild<A.EastAsianFont>()?.Typeface?.Value
                   ?? runProperties.GetFirstChild<A.ComplexScriptFont>()?.Typeface?.Value;
        if (!string.IsNullOrWhiteSpace(font))
        {
            style.FontFamily = font;
            hasValues = true;
        }

        var color = TryResolveDrawingColor(runProperties.GetFirstChild<A.SolidFill>(), themeColors);
        if (color.HasValue)
        {
            style.Color = color.Value;
            hasValues = true;
        }

        return hasValues ? style : null;
    }

    private static string? ExtractTitleText(C.Title? title)
    {
        var text = title?.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static ChartAxisPosition ParseAxisPosition(C.AxisPositionValues? position)
    {
        if (position is null)
        {
            return ChartAxisPosition.Bottom;
        }

        var token = position.ToString();
        if (string.Equals(token, "Left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "L", StringComparison.OrdinalIgnoreCase))
        {
            return ChartAxisPosition.Left;
        }

        if (string.Equals(token, "Right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "R", StringComparison.OrdinalIgnoreCase))
        {
            return ChartAxisPosition.Right;
        }

        if (string.Equals(token, "Top", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
        {
            return ChartAxisPosition.Top;
        }

        return ChartAxisPosition.Bottom;
    }

    private static ChartTickMark ParseTickMark(C.TickMarkValues? value)
    {
        if (value is null)
        {
            return ChartTickMark.None;
        }

        var token = value.ToString();
        if (string.Equals(token, "Inside", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickMark.Inside;
        }

        if (string.Equals(token, "Outside", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickMark.Outside;
        }

        if (string.Equals(token, "Cross", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickMark.Cross;
        }

        return ChartTickMark.None;
    }

    private static ChartTickLabelPosition ParseTickLabelPosition(C.TickLabelPositionValues? value)
    {
        if (value is null)
        {
            return ChartTickLabelPosition.None;
        }

        var token = value.ToString();
        if (string.Equals(token, "High", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickLabelPosition.High;
        }

        if (string.Equals(token, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickLabelPosition.Low;
        }

        if (string.Equals(token, "NextToAxis", StringComparison.OrdinalIgnoreCase))
        {
            return ChartTickLabelPosition.NextToAxis;
        }

        return ChartTickLabelPosition.None;
    }

    private static ChartLegendPosition ParseLegendPosition(C.LegendPositionValues? value)
    {
        if (value is null)
        {
            return ChartLegendPosition.Right;
        }

        var token = value.ToString();
        if (string.Equals(token, "Left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "L", StringComparison.OrdinalIgnoreCase))
        {
            return ChartLegendPosition.Left;
        }

        if (string.Equals(token, "Top", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
        {
            return ChartLegendPosition.Top;
        }

        if (string.Equals(token, "Bottom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "B", StringComparison.OrdinalIgnoreCase))
        {
            return ChartLegendPosition.Bottom;
        }

        if (string.Equals(token, "TopRight", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "TR", StringComparison.OrdinalIgnoreCase))
        {
            return ChartLegendPosition.Corner;
        }

        return ChartLegendPosition.Right;
    }

    private static ChartDataLabelPosition ParseDataLabelPosition(C.DataLabelPositionValues value)
    {
        var token = value.ToString();
        if (string.Equals(token, "BestFit", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.BestFit;
        }

        if (string.Equals(token, "Center", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.Center;
        }

        if (string.Equals(token, "InsideEnd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "InEnd", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.InsideEnd;
        }

        if (string.Equals(token, "InsideBase", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "InBase", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.InsideBase;
        }

        if (string.Equals(token, "OutsideEnd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "OutEnd", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.OutsideEnd;
        }

        if (string.Equals(token, "Left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "L", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.Left;
        }

        if (string.Equals(token, "Right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "R", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.Right;
        }

        if (string.Equals(token, "Top", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.Top;
        }

        if (string.Equals(token, "Bottom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "B", StringComparison.OrdinalIgnoreCase))
        {
            return ChartDataLabelPosition.Bottom;
        }

        return ChartDataLabelPosition.Center;
    }

    private static bool? TryParseBoolAttribute(OpenXmlElement element, string attributeName)
    {
        var text = GetAttributeValue(element, attributeName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Equals("0", StringComparison.OrdinalIgnoreCase)
            || text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bool.TryParse(text, out var value) ? value : null;
    }

    private static float ChartFontSizeToDip(int value)
    {
        var points = value / 100f;
        return points * 96f / 72f;
    }

    private static bool TryParseEmu(OpenXmlElement element, string attributeName, out long value)
    {
        value = 0;
        var text = GetAttributeValue(element, attributeName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseIntAttribute(OpenXmlElement element, string attributeName, out int value)
    {
        value = 0;
        var text = GetAttributeValue(element, attributeName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static DocColor? TryResolveDrawingColor(OpenXmlElement? element, DocumentThemeColorMap? themeColors)
    {
        if (element is null)
        {
            return null;
        }

        var rgbElement = element.GetFirstChild<A.RgbColorModelHex>();
        if (element is A.RgbColorModelHex directRgb)
        {
            rgbElement = directRgb;
        }

        var rgb = rgbElement?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) && TryParseHexColor(rgb, out var color))
        {
            return ApplyColorTransforms(color, rgbElement!);
        }

        var schemeColor = element as A.SchemeColor ?? element.GetFirstChild<A.SchemeColor>();
        if (schemeColor is not null && schemeColor.Val?.Value is A.SchemeColorValues schemeValue)
        {
            var baseColor = ResolveSchemeColor(schemeValue, themeColors);
            return baseColor.HasValue ? ApplyColorTransforms(baseColor.Value, schemeColor) : null;
        }

        var systemColor = element as A.SystemColor ?? element.GetFirstChild<A.SystemColor>();
        var lastColor = systemColor?.LastColor?.Value;
        if (!string.IsNullOrWhiteSpace(lastColor) && TryParseHexColor(lastColor, out var sysColor))
        {
            return ApplyColorTransforms(sysColor, systemColor!);
        }

        return null;
    }

    private static DocColor ApplyColorTransforms(DocColor baseColor, OpenXmlElement colorElement)
    {
        var r = (float)baseColor.R;
        var g = (float)baseColor.G;
        var b = (float)baseColor.B;
        var a = (float)baseColor.A;

        foreach (var child in colorElement.ChildElements)
        {
            var name = child.LocalName;
            var valueText = GetAttributeValue(child, "val");
            if (string.IsNullOrWhiteSpace(valueText) || !int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            {
                continue;
            }

            var factor = raw / 100000f;
            if (name.Equals("tint", StringComparison.OrdinalIgnoreCase))
            {
                r += (255f - r) * factor;
                g += (255f - g) * factor;
                b += (255f - b) * factor;
            }
            else if (name.Equals("shade", StringComparison.OrdinalIgnoreCase))
            {
                r *= factor;
                g *= factor;
                b *= factor;
            }
            else if (name.Equals("lumMod", StringComparison.OrdinalIgnoreCase))
            {
                r *= factor;
                g *= factor;
                b *= factor;
            }
            else if (name.Equals("lumOff", StringComparison.OrdinalIgnoreCase))
            {
                r += 255f * factor;
                g += 255f * factor;
                b += 255f * factor;
            }
            else if (name.Equals("alpha", StringComparison.OrdinalIgnoreCase)
                     || name.Equals("alphaMod", StringComparison.OrdinalIgnoreCase))
            {
                a *= factor;
            }
            else if (name.Equals("alphaOff", StringComparison.OrdinalIgnoreCase))
            {
                a += 255f * factor;
            }
        }

        return new DocColor(ClampToByte(r), ClampToByte(g), ClampToByte(b), ClampToByte(a));
    }

    private static byte ClampToByte(float value)
    {
        if (value <= 0f)
        {
            return 0;
        }

        if (value >= 255f)
        {
            return 255;
        }

        return (byte)MathF.Round(value);
    }

    private static DocColor? ResolveSchemeColor(A.SchemeColorValues value, DocumentThemeColorMap? themeColors)
    {
        var themeColor = MapSchemeColor(value);
        if (themeColor is null)
        {
            return null;
        }

        if (themeColors is null)
        {
            return DocumentThemeColorMap.GetDefault(themeColor.Value);
        }

        return themeColors.GetOrDefault(themeColor.Value);
    }

    private static DocThemeColor? MapSchemeColor(A.SchemeColorValues value)
    {
        if (value == A.SchemeColorValues.Dark1)
        {
            return DocThemeColor.Dark1;
        }

        if (value == A.SchemeColorValues.Light1)
        {
            return DocThemeColor.Light1;
        }

        if (value == A.SchemeColorValues.Dark2)
        {
            return DocThemeColor.Dark2;
        }

        if (value == A.SchemeColorValues.Light2)
        {
            return DocThemeColor.Light2;
        }

        if (value == A.SchemeColorValues.Accent1)
        {
            return DocThemeColor.Accent1;
        }

        if (value == A.SchemeColorValues.Accent2)
        {
            return DocThemeColor.Accent2;
        }

        if (value == A.SchemeColorValues.Accent3)
        {
            return DocThemeColor.Accent3;
        }

        if (value == A.SchemeColorValues.Accent4)
        {
            return DocThemeColor.Accent4;
        }

        if (value == A.SchemeColorValues.Accent5)
        {
            return DocThemeColor.Accent5;
        }

        if (value == A.SchemeColorValues.Accent6)
        {
            return DocThemeColor.Accent6;
        }

        if (value == A.SchemeColorValues.Hyperlink)
        {
            return DocThemeColor.Hyperlink;
        }

        if (value == A.SchemeColorValues.FollowedHyperlink)
        {
            return DocThemeColor.FollowedHyperlink;
        }

        var name = value.ToString();
        if (name.Equals("Text1", StringComparison.OrdinalIgnoreCase))
        {
            return DocThemeColor.Dark1;
        }

        if (name.Equals("Background1", StringComparison.OrdinalIgnoreCase))
        {
            return DocThemeColor.Light1;
        }

        if (name.Equals("Text2", StringComparison.OrdinalIgnoreCase))
        {
            return DocThemeColor.Dark2;
        }

        if (name.Equals("Background2", StringComparison.OrdinalIgnoreCase))
        {
            return DocThemeColor.Light2;
        }

        return null;
    }

    private static ChartBarDirection ParseBarDirection(C.BarDirectionValues? direction)
    {
        return direction == C.BarDirectionValues.Bar ? ChartBarDirection.Bar : ChartBarDirection.Column;
    }

    private static ChartStacking ParseBarGrouping(C.BarGroupingValues? grouping)
    {
        if (grouping == C.BarGroupingValues.Stacked)
        {
            return ChartStacking.Stacked;
        }

        if (grouping == C.BarGroupingValues.PercentStacked)
        {
            return ChartStacking.Percent;
        }

        return ChartStacking.None;
    }

    private static ChartStacking ParseGrouping(C.GroupingValues? grouping)
    {
        if (grouping == C.GroupingValues.Stacked)
        {
            return ChartStacking.Stacked;
        }

        if (grouping == C.GroupingValues.PercentStacked)
        {
            return ChartStacking.Percent;
        }

        return ChartStacking.None;
    }

    private static ChartRadarStyle ParseRadarStyle(C.RadarStyleValues? value)
    {
        if (value == C.RadarStyleValues.Filled)
        {
            return ChartRadarStyle.Filled;
        }

        if (value == C.RadarStyleValues.Marker)
        {
            return ChartRadarStyle.Marker;
        }

        return ChartRadarStyle.Standard;
    }

    private static float ParseHoleSize(byte? holeSize)
    {
        if (!holeSize.HasValue)
        {
            return 0.5f;
        }

        var clamped = Math.Clamp(holeSize.Value, (byte)10, (byte)90);
        return clamped / 100f;
    }

    private static void ApplyDefaultSeriesColors(ChartModel model, DocumentThemeColorMap? themeColors, bool forcePointPalette = false)
    {
        if (model.Series.Count == 0)
        {
            return;
        }

        var palette = GetChartPalette(themeColors);
        for (var i = 0; i < model.Series.Count; i++)
        {
            var series = model.Series[i];
            if (forcePointPalette)
            {
                ApplyPointPalette(series, palette);
            }
            else
            {
                var color = palette[i % palette.Length];
                ApplySeriesPalette(model.Type, series, color);
            }
        }
    }

    private static DocColor[] GetChartPalette(DocumentThemeColorMap? themeColors)
    {
        return new[]
        {
            ResolveThemeColor(themeColors, DocThemeColor.Accent1),
            ResolveThemeColor(themeColors, DocThemeColor.Accent2),
            ResolveThemeColor(themeColors, DocThemeColor.Accent3),
            ResolveThemeColor(themeColors, DocThemeColor.Accent4),
            ResolveThemeColor(themeColors, DocThemeColor.Accent5),
            ResolveThemeColor(themeColors, DocThemeColor.Accent6)
        };
    }

    private static DocColor ResolveThemeColor(DocumentThemeColorMap? themeColors, DocThemeColor color)
    {
        return themeColors?.GetOrDefault(color) ?? DocumentThemeColorMap.GetDefault(color);
    }

    private static void ApplySeriesPalette(ChartType type, ChartSeries series, DocColor color)
    {
        series.Style ??= new ChartStyle();

        if (type == ChartType.Line || type == ChartType.Scatter || type == ChartType.Radar)
        {
            series.Style.Line ??= new ChartLineStyle();
            if (!series.Style.Line.IsNone && !series.Style.Line.Color.HasValue)
            {
                series.Style.Line.Color = color;
            }
        }

        if (type != ChartType.Line)
        {
            series.Style.Fill ??= new ChartFillStyle();
            if (!series.Style.Fill.IsNone && !series.Style.Fill.Color.HasValue)
            {
                series.Style.Fill.Color = color;
            }
        }
    }

    private static void ApplyPointPalette(ChartSeries series, DocColor[] palette)
    {
        for (var i = 0; i < series.Points.Count; i++)
        {
            var point = series.Points[i];
            var color = palette[i % palette.Length];
            point.Style ??= new ChartStyle();
            point.Style.Fill ??= new ChartFillStyle();
            if (!point.Style.Fill.IsNone && !point.Style.Fill.Color.HasValue)
            {
                point.Style.Fill.Color = color;
            }
        }
    }

    private static List<string> ReadStringCache(OpenXmlElement? element)
    {
        var cache = element?.Descendants<C.StringCache>().FirstOrDefault();
        if (cache is null)
        {
            return new List<string>();
        }

        return cache.Elements<C.StringPoint>()
            .OrderBy(point => point.Index?.Value ?? 0)
            .Select(point => point.NumericValue?.Text ?? point.InnerText ?? string.Empty)
            .ToList();
    }

    private static List<double> ReadNumberCache(OpenXmlElement? element)
    {
        var cache = element?.Descendants<C.NumberingCache>().FirstOrDefault();
        if (cache is null)
        {
            return new List<double>();
        }

        var values = new List<double>();
        foreach (var point in cache.Elements<C.NumericPoint>().OrderBy(point => point.Index?.Value ?? 0))
        {
            var text = point.NumericValue?.Text;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static void LoadBibliography(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart is null)
        {
            return;
        }

        foreach (var part in mainPart.CustomXmlParts)
        {
            if (string.Equals(part.ContentType, DocxMacroSerializer.MacroCustomPartContentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryLoadBibliographyPart(part, document))
            {
                break;
            }
        }
    }

    private static bool TryLoadBibliographyPart(CustomXmlPart part, VibeDocument document)
    {
        try
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            var xml = XDocument.Load(stream);
            var root = xml.Root;
            if (root is null
                || root.Name.LocalName != "Sources"
                || root.Name.Namespace != BibliographyNamespace)
            {
                return false;
            }

            var selectedStyle = (string?)root.Attribute("SelectedStyle")
                                ?? (string?)root.Attribute("StyleName");
            if (!string.IsNullOrWhiteSpace(selectedStyle))
            {
                document.CitationStyle = selectedStyle;
            }

            document.CitationSources.Sources.Clear();
            foreach (var sourceElement in root.Elements(BibliographyNamespace + "Source"))
            {
                var source = ParseBibliographySource(sourceElement);
                if (source is not null)
                {
                    document.CitationSources.Sources.Add(source);
                }
            }

            document.CitationSources.EnsureUniqueTags();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static CitationSource? ParseBibliographySource(XElement element)
    {
        if (element is null)
        {
            return null;
        }

        var source = new CitationSource();
        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;
            var value = child.Value?.Trim();
            if (string.Equals(localName, "Tag", StringComparison.OrdinalIgnoreCase))
            {
                source.Tag = value ?? string.Empty;
                continue;
            }

            if (string.Equals(localName, "SourceType", StringComparison.OrdinalIgnoreCase))
            {
                source.SourceType = value ?? source.SourceType;
                continue;
            }

            if (string.Equals(localName, "Guid", StringComparison.OrdinalIgnoreCase))
            {
                source.Guid = value;
                continue;
            }

            if (TryParseNameList(child, out var names))
            {
                source.SetField(localName, names);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                source.SetField(localName, value);
            }
        }

        if (string.IsNullOrWhiteSpace(source.Tag))
        {
            source.Tag = source.Guid ?? string.Empty;
        }

        return source;
    }

    private static bool TryParseNameList(XElement element, out string names)
    {
        names = string.Empty;
        var list = element.Element(BibliographyNamespace + "NameList");
        if (list is null)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var person in list.Elements(BibliographyNamespace + "Person"))
        {
            var name = BuildPersonName(person);
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name);
            }
        }

        foreach (var corp in list.Elements(BibliographyNamespace + "Corporate"))
        {
            var name = corp.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name);
            }
        }

        if (parts.Count == 0)
        {
            return false;
        }

        names = string.Join("; ", parts);
        return true;
    }

    private static string BuildPersonName(XElement person)
    {
        var last = person.Element(BibliographyNamespace + "Last")?.Value?.Trim();
        var first = person.Element(BibliographyNamespace + "First")?.Value?.Trim();
        var middle = person.Element(BibliographyNamespace + "Middle")?.Value?.Trim();
        var suffix = person.Element(BibliographyNamespace + "Suffix")?.Value?.Trim();

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(last))
        {
            builder.Append(last);
        }

        if (!string.IsNullOrWhiteSpace(first))
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(first);
        }

        if (!string.IsNullOrWhiteSpace(middle))
        {
            builder.Append(' ');
            builder.Append(middle);
        }

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            builder.Append(' ');
            builder.Append(suffix);
        }

        return builder.ToString();
    }

    private static void LoadMacros(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart is null)
        {
            return;
        }

        foreach (var part in mainPart.CustomXmlParts)
        {
            if (!string.Equals(part.ContentType, DocxMacroSerializer.MacroCustomPartContentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var json = reader.ReadToEnd();
            DocxMacroSerializer.TryDeserialize(json, document.Macros);
            break;
        }

        var hasCustomVbaSource = document.Macros.VbaModules.Any(module => !string.IsNullOrWhiteSpace(module.Source));
        EnsureVbaModuleProcedures(document.Macros.VbaModules);
        if (hasCustomVbaSource)
        {
            VbaMacroUtilities.SyncVbaDefinitions(document.Macros);
        }

        var vbaPart = mainPart.VbaProjectPart;
        if (vbaPart is null)
        {
            return;
        }

        using var vbaStream = vbaPart.GetStream(FileMode.Open, FileAccess.Read);
        using var buffer = new MemoryStream();
        vbaStream.CopyTo(buffer);
        var data = buffer.ToArray();
        if (data.Length == 0)
        {
            return;
        }

        document.Macros.VbaProject = data;
        var project = VbaProjectReader.ReadProject(data);
        if (hasCustomVbaSource)
        {
            MergeVbaModuleStreams(document.Macros.VbaModules, project.Modules);
            EnsureVbaModuleProcedures(document.Macros.VbaModules);
            VbaMacroUtilities.SyncVbaDefinitions(document.Macros);
            return;
        }

        document.Macros.VbaModules.Clear();
        document.Macros.VbaModules.AddRange(project.Modules);
        EnsureVbaModuleProcedures(document.Macros.VbaModules);
        VbaMacroUtilities.SyncVbaDefinitions(document.Macros);
    }

    private static void EnsureVbaModuleProcedures(IEnumerable<VbaModuleInfo> modules)
    {
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Source))
            {
                continue;
            }

            VbaMacroUtilities.UpdateModuleProcedures(module, module.Source);
        }
    }

    private static void MergeVbaModuleStreams(List<VbaModuleInfo> targetModules, IReadOnlyList<VbaModuleInfo> projectModules)
    {
        if (targetModules.Count == 0 || projectModules.Count == 0)
        {
            return;
        }

        foreach (var module in targetModules)
        {
            if (!string.IsNullOrWhiteSpace(module.StreamName))
            {
                continue;
            }

            foreach (var project in projectModules)
            {
                if (string.Equals(project.Name, module.Name, StringComparison.OrdinalIgnoreCase))
                {
                    module.StreamName = project.StreamName;
                    break;
                }
            }
        }
    }

    private sealed class HyperlinkResolver
    {
        private readonly OpenXmlPart? _part;

        public HyperlinkResolver(OpenXmlPart? part)
        {
            _part = part;
        }

        public HyperlinkInfo? Resolve(Hyperlink hyperlink)
        {
            if (hyperlink is null)
            {
                return null;
            }

            var anchor = hyperlink.Anchor?.Value;
            var tooltip = hyperlink.Tooltip?.Value;
            string? uri = null;

            if (_part is not null && hyperlink.Id?.Value is string id)
            {
                var relationship = _part.HyperlinkRelationships.FirstOrDefault(item => item.Id == id);
                if (relationship is not null)
                {
                    uri = relationship.Uri?.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(anchor)
                && string.IsNullOrWhiteSpace(uri)
                && string.IsNullOrWhiteSpace(tooltip))
            {
                return null;
            }

            return new HyperlinkInfo(uri, anchor, tooltip);
        }
    }
}
