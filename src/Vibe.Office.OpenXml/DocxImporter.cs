using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using OpenXmlTableCellBorders = DocumentFormat.OpenXml.Wordprocessing.TableCellBorders;
using OpenXmlParagraphBorders = DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;
using VibeDocument = Vibe.Office.Documents.Document;

namespace Vibe.Office.OpenXml;

public sealed class DocxImporter
{
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wordprocessing16DuNamespace = "http://schemas.microsoft.com/office/word/2023/wordml/word16du";
    private const string OleObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";

    public VibeDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        using var wordDocument = WordprocessingDocument.Open(filePath, false);
        var mainPart = wordDocument.MainDocumentPart;
        var body = mainPart?.Document?.Body;

        var document = new VibeDocument();
        document.Blocks.Clear();
        document.Sections.Clear();
        LoadFonts(mainPart, document);
        LoadDocumentSettings(mainPart, document);
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer, document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));

        if (body is null)
        {
            document.Blocks.Add(new ParagraphBlock());
            return document;
        }

        var styleResolver = new StyleResolver(mainPart, document);
        var listResolver = new ListResolver(mainPart, document, styleResolver);
        var imageResolver = new ImageResolver(mainPart);
        var chartResolver = new ChartResolver(mainPart);
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
                    document.Blocks.Add(CreateAltChunkBlock(altChunk, imageResolver.Part));
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

        ApplyTableProperties(table, tableBlock.Properties);

        Vibe.Office.Documents.TableCell ParseTableCell(DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
        {
            var tableCell = new Vibe.Office.Documents.TableCell();
            ApplyTableCellStructure(cell, tableCell);
            ApplyTableCellProperties(cell, tableCell.Properties);
            foreach (var paragraph in cell.Elements<Paragraph>())
            {
                tableCell.Paragraphs.Add(ParseParagraph(paragraph, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver));
            }

            if (tableCell.Paragraphs.Count == 0)
            {
                tableCell.Paragraphs.Add(new ParagraphBlock());
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
            ApplyTableRowProperties(row, tableRow.Properties);
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
            ListResolver listResolver,
            StyleResolver styleResolver,
            DocumentRevisions revisions,
            ContentControlPlaceholderResolver? placeholderResolver)
        {
            if (headerPart?.Header is null)
            {
                return;
            }

            target.Blocks.Clear();
            var headerBlocks = ParseHeaderFooter(headerPart.Header, listResolver, new ImageResolver(headerPart), new ChartResolver(headerPart), new HyperlinkResolver(headerPart), styleResolver, revisions, placeholderResolver);
            foreach (var block in headerBlocks)
            {
                target.Blocks.Add(block);
            }
        }

        static void PopulateFooter(
            HeaderFooter target,
            FooterPart? footerPart,
            ListResolver listResolver,
            StyleResolver styleResolver,
            DocumentRevisions revisions,
            ContentControlPlaceholderResolver? placeholderResolver)
        {
            if (footerPart?.Footer is null)
            {
                return;
            }

            target.Blocks.Clear();
            var footerBlocks = ParseHeaderFooter(footerPart.Footer, listResolver, new ImageResolver(footerPart), new ChartResolver(footerPart), new HyperlinkResolver(footerPart), styleResolver, revisions, placeholderResolver);
            foreach (var block in footerBlocks)
            {
                target.Blocks.Add(block);
            }
        }

        static HeaderPart? ResolveHeaderPart(MainDocumentPart mainPart, DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps, HeaderFooterValues type, bool allowFallback)
        {
            var headerRef = sectionProps.Elements<HeaderReference>()
                .FirstOrDefault(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
            if (headerRef?.Id?.Value is string headerId)
            {
                return mainPart.GetPartById(headerId) as HeaderPart;
            }

            if (allowFallback && mainPart.HeaderParts.Count() == 1)
            {
                return mainPart.HeaderParts.FirstOrDefault();
            }

            return null;
        }

        static FooterPart? ResolveFooterPart(MainDocumentPart mainPart, DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps, HeaderFooterValues type, bool allowFallback)
        {
            var footerRef = sectionProps.Elements<FooterReference>()
                .FirstOrDefault(item => (item.Type?.Value ?? HeaderFooterValues.Default) == type);
            if (footerRef?.Id?.Value is string footerId)
            {
                return mainPart.GetPartById(footerId) as FooterPart;
            }

            if (allowFallback && mainPart.FooterParts.Count() == 1)
            {
                return mainPart.FooterParts.FirstOrDefault();
            }

            return null;
        }

        if (sectionProps.Elements<HeaderReference>().Any(item => item.Type?.Value == HeaderFooterValues.Even)
            || sectionProps.Elements<FooterReference>().Any(item => item.Type?.Value == HeaderFooterValues.Even))
        {
            document.EvenAndOddHeaders = true;
        }

        PopulateHeader(section.Header, ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.Default, true), listResolver, styleResolver, document.Revisions, placeholderResolver);
        PopulateFooter(section.Footer, ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.Default, true), listResolver, styleResolver, document.Revisions, placeholderResolver);
        PopulateHeader(section.FirstHeader, ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.First, false), listResolver, styleResolver, document.Revisions, placeholderResolver);
        PopulateFooter(section.FirstFooter, ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.First, false), listResolver, styleResolver, document.Revisions, placeholderResolver);
        PopulateHeader(section.EvenHeader, ResolveHeaderPart(mainPart, sectionProps, HeaderFooterValues.Even, false), listResolver, styleResolver, document.Revisions, placeholderResolver);
        PopulateFooter(section.EvenFooter, ResolveFooterPart(mainPart, sectionProps, HeaderFooterValues.Even, false), listResolver, styleResolver, document.Revisions, placeholderResolver);
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
    }

    private static void LoadFonts(MainDocumentPart? mainPart, VibeDocument document)
    {
        if (mainPart is null)
        {
            return;
        }

        LoadThemeFonts(mainPart.ThemePart?.Theme, document.Fonts);
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

        if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
        {
            var imageResolver = new ImageResolver(mainPart.FootnotesPart);
            var chartResolver = new ChartResolver(mainPart.FootnotesPart);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.FootnotesPart);
            foreach (var footnote in footnotes.Elements<Footnote>())
            {
                var idValue = footnote.Id?.Value;
                var id = idValue.HasValue ? (int)idValue.Value : -1;
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
            var imageResolver = new ImageResolver(mainPart.EndnotesPart);
            var chartResolver = new ChartResolver(mainPart.EndnotesPart);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.EndnotesPart);
            foreach (var endnote in endnotes.Elements<Endnote>())
            {
                var idValue = endnote.Id?.Value;
                var id = idValue.HasValue ? (int)idValue.Value : -1;
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
            var imageResolver = new ImageResolver(mainPart.WordprocessingCommentsPart);
            var chartResolver = new ChartResolver(mainPart.WordprocessingCommentsPart);
            var hyperlinkResolver = new HyperlinkResolver(mainPart.WordprocessingCommentsPart);
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
            }
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

        return result;
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
                blocks.Add(CreateAltChunkBlock(altChunk, imageResolver.Part));
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

        void BeginField()
        {
            var startInline = new FieldStartInline(string.Empty);
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
                            BeginField();
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
            var startInline = new FieldStartInline(instruction);
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

        void ProcessElement(OpenXmlElement element, HyperlinkInfo? hyperlink)
        {
            switch (element)
            {
                case Run run:
                    ProcessRun(run, hyperlink);
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
        if (marginLeftTwips.HasValue)
        {
            properties.MarginLeft = TwipsToDip(marginLeftTwips.Value);
        }

        var marginRightTwips = TryParseTwips(pageMargin?.Right);
        if (marginRightTwips.HasValue)
        {
            properties.MarginRight = TwipsToDip(marginRightTwips.Value);
        }

        var marginTopTwips = TryParseTwips(pageMargin?.Top);
        if (marginTopTwips.HasValue)
        {
            properties.MarginTop = TwipsToDip(marginTopTwips.Value);
        }

        var marginBottomTwips = TryParseTwips(pageMargin?.Bottom);
        if (marginBottomTwips.HasValue)
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
            if (columns.ColumnCount?.Value is not null)
            {
                properties.ColumnCount = columns.ColumnCount.Value;
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

            foreach (var column in columns.Elements<Column>())
            {
                var columnWidthTwips = TryParseTwips(column.Width);
                if (columnWidthTwips.HasValue)
                {
                    properties.ColumnWidths.Add(TwipsToDip(columnWidthTwips.Value));
                }

                if (!properties.ColumnGap.HasValue)
                {
                    var columnSpace = TryParseTwips(column.Space);
                    if (columnSpace.HasValue)
                    {
                        properties.ColumnGap = TwipsToDip(columnSpace.Value);
                    }
                }
            }

            if (!properties.ColumnCount.HasValue && properties.ColumnWidths.Count == 0)
            {
                properties.ColumnCount = 1;
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

        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
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
                    var definition = new StyleDefinition(
                        styleId,
                        type,
                        style.BasedOn?.Val?.Value,
                        style.StyleName?.Val?.Value,
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
                        BasedOnId = definition.BasedOnId
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
                        BasedOnId = definition.BasedOnId
                    };
                    ApplyRunStyleProperties(definition.RunProperties, characterStyle.RunProperties);
                    _document.Styles.CharacterStyles[definition.Id] = characterStyle;
                }
                else if (definition.Type == StyleValues.Table)
                {
                    var tableStyle = new TableStyleDefinition(definition.Id)
                    {
                        Name = definition.Name,
                        BasedOnId = definition.BasedOnId
                    };
                    ApplyTableProperties(definition.TableProperties, tableStyle.TableProperties);
                    ApplyTableCellProperties(definition.TableCellProperties, tableStyle.CellProperties);
                    foreach (var overrideProperties in definition.TableStyleOverrides)
                    {
                        var condition = MapTableStyleCondition(overrideProperties.Type?.Value);
                        if (!condition.HasValue)
                        {
                            continue;
                        }

                        var conditionProperties = new TableStyleConditionProperties();
                        ApplyTableProperties(overrideProperties.TableStyleConditionalFormattingTableProperties, conditionProperties.TableProperties);
                        ApplyTableCellProperties(overrideProperties.TableStyleConditionalFormattingTableCellProperties, conditionProperties.CellProperties);
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

        var shading = props.GetFirstChild<Shading>();
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var shadingColor))
        {
            properties.ShadingColor = shadingColor;
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

        var shading = props.GetFirstChild<Shading>();
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var shadingColor))
        {
            properties.ShadingColor = shadingColor;
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
            var underlineStyle = MapUnderlineStyle(underline.Val?.Value);
            style.UnderlineStyle = underlineStyle;
            style.Underline = underlineStyle != DocUnderlineStyle.None;
            var underlineColor = underline.Color?.Value;
            if (!string.IsNullOrWhiteSpace(underlineColor) && !underlineColor.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(underlineColor, out var parsedUnderline))
            {
                style.UnderlineColor = parsedUnderline;
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

        var color = properties.GetFirstChild<Color>()?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(color) && !color.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(color, out var parsed))
            {
                style.Color = parsed;
            }
        }

        var verticalAlign = properties.GetFirstChild<VerticalTextAlignment>()?.Val?.Value;
        if (verticalAlign is not null)
        {
            style.VerticalPosition = MapVerticalPosition(verticalAlign.Value);
        }

        var smallCaps = properties.GetFirstChild<SmallCaps>();
        if (smallCaps is not null)
        {
            style.SmallCaps = smallCaps.Val?.Value != false;
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
            var underlineStyle = MapUnderlineStyle(underline.Val?.Value);
            style.UnderlineStyle = underlineStyle;
            style.Underline = underlineStyle != DocUnderlineStyle.None;
            var underlineColor = underline.Color?.Value;
            if (!string.IsNullOrWhiteSpace(underlineColor) && !underlineColor.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && TryParseHexColor(underlineColor, out var parsedUnderline))
            {
                style.UnderlineColor = parsedUnderline;
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

        var color = properties.GetFirstChild<Color>()?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(color) && !color.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(color, out var parsed))
            {
                style.Color = parsed;
            }
        }

        var verticalAlign = properties.GetFirstChild<VerticalTextAlignment>()?.Val?.Value;
        if (verticalAlign is not null)
        {
            style.VerticalPosition = MapVerticalPosition(verticalAlign.Value);
        }

        var smallCaps = properties.GetFirstChild<SmallCaps>();
        if (smallCaps is not null)
        {
            style.SmallCaps = smallCaps.Val?.Value != false;
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
    }

    private static void ApplyTableProperties(Table table, Vibe.Office.Documents.TableProperties properties)
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
        ApplyTableProperties(props, properties);
    }

    private static void ApplyTableProperties(OpenXmlElement? props, Vibe.Office.Documents.TableProperties properties)
    {
        if (props is null)
        {
            return;
        }

        var borders = props.GetFirstChild<OpenXmlTableBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder);
            properties.Borders.InsideHorizontal = ParseBorderLine(borders.InsideHorizontalBorder);
            properties.Borders.InsideVertical = ParseBorderLine(borders.InsideVerticalBorder);
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
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var color))
        {
            properties.ShadingColor = color;
        }

        var look = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableLook>();
        if (look is not null)
        {
            properties.Look = new Vibe.Office.Documents.TableLook
            {
                FirstRow = look.FirstRow?.Value ?? false,
                LastRow = look.LastRow?.Value ?? false,
                FirstColumn = look.FirstColumn?.Value ?? false,
                LastColumn = look.LastColumn?.Value ?? false,
                BandedRows = !(look.NoHorizontalBand?.Value ?? false),
                BandedColumns = !(look.NoVerticalBand?.Value ?? false)
            };
        }
    }

    private static void ApplyTableCellProperties(DocumentFormat.OpenXml.Wordprocessing.TableCell cell, Vibe.Office.Documents.TableCellProperties properties)
    {
        ApplyTableCellProperties(cell.TableCellProperties, properties);
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

    private static void ApplyTableRowProperties(DocumentFormat.OpenXml.Wordprocessing.TableRow row, Vibe.Office.Documents.TableRowProperties properties)
    {
        ApplyTableRowProperties(row.TableRowProperties, properties);
    }

    private static void ApplyTableRowProperties(OpenXmlElement? props, Vibe.Office.Documents.TableRowProperties properties)
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
    }

    private static void ApplyTableCellProperties(OpenXmlElement? props, Vibe.Office.Documents.TableCellProperties properties)
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

        var shading = props.GetFirstChild<Shading>();
        if (shading?.Fill?.Value is string fill && TryParseHexColor(fill, out var color))
        {
            properties.ShadingColor = color;
        }

        var borders = props.GetFirstChild<OpenXmlTableCellBorders>();
        if (borders is not null)
        {
            properties.Borders.Top = ParseBorderLine(borders.TopBorder);
            properties.Borders.Bottom = ParseBorderLine(borders.BottomBorder);
            properties.Borders.Left = ParseBorderLine(borders.LeftBorder);
            properties.Borders.Right = ParseBorderLine(borders.RightBorder);
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

    private static BorderLine? ParseBorderLine(BorderType? border)
    {
        if (border is null)
        {
            return null;
        }

        return new BorderLine
        {
            Style = MapBorderStyle(border.Val?.Value),
            Thickness = BorderSizeToDip(border.Size?.Value),
            Color = ParseBorderColor(border.Color?.Value),
            Spacing = BorderSpaceToDip(border.Space?.Value)
        };
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

        if (value == BorderValues.Double
            || value == BorderValues.Triple
            || value == BorderValues.ThickThinSmallGap
            || value == BorderValues.ThickThinMediumGap
            || value == BorderValues.ThickThinLargeGap
            || value == BorderValues.ThinThickSmallGap
            || value == BorderValues.ThinThickMediumGap
            || value == BorderValues.ThinThickLargeGap
            || value == BorderValues.ThinThickThinSmallGap
            || value == BorderValues.ThinThickThinMediumGap
            || value == BorderValues.ThinThickThinLargeGap
            || value == BorderValues.DoubleWave)
        {
            return DocBorderStyle.Double;
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

    private static Vibe.Office.Primitives.DocColor ParseBorderColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return new Vibe.Office.Primitives.DocColor(0, 0, 0);
        }

        return TryParseHexColor(value, out var color) ? color : new Vibe.Office.Primitives.DocColor(0, 0, 0);
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

    private static float HalfPointsToDip(float halfPoints)
    {
        var points = halfPoints / 2f;
        return points * 96f / 72f;
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
            "nary" => ParseMathNary(element),
            "m" => ParseMathMatrix(element),
            "mr" => ParseMathMatrixRow(element),
            "sSup" => ParseMathScript(element),
            "sSub" => ParseMathScript(element),
            "sSubSup" => ParseMathScript(element),
            "rad" => ParseMathRadical(element),
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
        var numeratorElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "num", StringComparison.OrdinalIgnoreCase));
        var denominatorElement = element.Elements().FirstOrDefault(child => string.Equals(child.LocalName, "den", StringComparison.OrdinalIgnoreCase));
        var numerator = numeratorElement is not null ? ParseMathGroup(numeratorElement) : new MathRun();
        var denominator = denominatorElement is not null ? ParseMathGroup(denominatorElement) : new MathRun();
        return new MathFraction(numerator, denominator);
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

        return imageResolver.TryCreateImage(drawing);
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

        var shapeElement = element.Descendants()
            .FirstOrDefault(node => node.LocalName.Equals("shape", StringComparison.OrdinalIgnoreCase));
        if (shapeElement is null)
        {
            return null;
        }

        var (width, height) = GetVmlSize(element);
        var shapeInline = new ShapeInline(width, height)
        {
            Name = GetAttributeValue(shapeElement, "id")
        };

        shapeInline.Properties.PresetGeometry = "rect";

        var fillColor = GetAttributeValue(shapeElement, "fillcolor") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(fillColor) && TryParseVmlColor(fillColor, out var fill))
        {
            shapeInline.Properties.FillColor = fill;
        }

        var strokeColor = GetAttributeValue(shapeElement, "strokecolor") ?? string.Empty;
        var strokeWeight = GetAttributeValue(shapeElement, "strokeweight") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(strokeColor) && TryParseVmlColor(strokeColor, out var stroke))
        {
            var border = new BorderLine { Color = stroke };
            if (!string.IsNullOrWhiteSpace(strokeWeight) && TryParseVmlLength(strokeWeight, out var weight))
            {
                border.Thickness = weight;
            }

            shapeInline.Properties.Outline = border;
        }

        var textBoxContent = element.Descendants<TextBoxContent>().FirstOrDefault();
        if (textBoxContent is not null)
        {
            var blocks = ParseTextBoxContent(textBoxContent, listResolver, imageResolver, chartResolver, hyperlinkResolver, styleResolver, revisions, placeholderResolver);
            var textBox = new ShapeTextBox();
            textBox.Blocks.AddRange(blocks);
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

        ApplyShapeProperties(spPr, shapeInline.Properties);
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

        var left = anchor.DistanceFromLeft?.Value;
        var top = anchor.DistanceFromTop?.Value;
        var right = anchor.DistanceFromRight?.Value;
        var bottom = anchor.DistanceFromBottom?.Value;
        if (left.HasValue || top.HasValue || right.HasValue || bottom.HasValue)
        {
            target.Distance = new DocThickness(
                left.HasValue ? EmuToDip(left.Value) : 0f,
                top.HasValue ? EmuToDip(top.Value) : 0f,
                right.HasValue ? EmuToDip(right.Value) : 0f,
                bottom.HasValue ? EmuToDip(bottom.Value) : 0f);
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
        }
        else if (wrapTight is not null)
        {
            target.WrapStyle = FloatingWrapStyle.Tight;
            target.WrapSide = MapWrapSide(wrapTight.WrapText?.Value);
        }
        else if (anchor.GetFirstChild<DW.WrapTopBottom>() is not null)
        {
            target.WrapStyle = FloatingWrapStyle.TopBottom;
            target.WrapSide = FloatingWrapSide.Both;
        }
        else if (wrapThrough is not null)
        {
            target.WrapStyle = FloatingWrapStyle.Through;
            target.WrapSide = MapWrapSide(wrapThrough.WrapText?.Value);
        }
        else
        {
            target.WrapStyle = FloatingWrapStyle.None;
            target.WrapSide = FloatingWrapSide.Both;
        }
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

    private static void ApplyShapeProperties(Wps.ShapeProperties? shapeProperties, ShapeProperties properties)
    {
        if (shapeProperties is null)
        {
            return;
        }

        var preset = shapeProperties.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        properties.PresetGeometry = preset?.ToString();
        properties.FillColor = TryParseDrawingFillColor(shapeProperties);
        properties.Outline = ParseShapeOutline(shapeProperties.GetFirstChild<A.Outline>());
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

        var color = TryParseSolidFillColor(outline.GetFirstChild<A.SolidFill>());
        if (color.HasValue)
        {
            border.Color = color.Value;
        }

        var dash = outline.GetFirstChild<A.PresetDash>()?.Val?.Value;
        border.Style = MapLineDash(dash);
        return border;
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

    private static float EmuToDip(long emu)
    {
        return (float)(emu / 914400d * 96d);
    }

    private static (float Width, float Height) GetVmlSize(OpenXmlElement element)
    {
        var shape = element.Descendants()
            .FirstOrDefault(node => node.LocalName.Equals("shape", StringComparison.OrdinalIgnoreCase));
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
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return TryParseHexColor(trimmed, out color);
    }

    private sealed class ImageResolver
    {
        private readonly OpenXmlPart? _part;
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public ImageResolver(OpenXmlPart? part)
        {
            _part = part;
        }

        public OpenXmlPart? Part => _part;

        public ImageInline? TryCreateImage(Drawing drawing)
        {
            if (_part is null)
            {
                return null;
            }

            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            var embed = blip?.Embed?.Value;
            if (string.IsNullOrWhiteSpace(embed))
            {
                return CreateDrawingPlaceholder(drawing);
            }

            if (_part.GetPartById(embed) is not ImagePart imagePart)
            {
                return CreateDrawingPlaceholder(drawing);
            }

            using var stream = imagePart.GetStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();

            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is long cx ? EmuToDip(cx) : 100f;
            var height = extent?.Cy?.Value is long cy ? EmuToDip(cy) : 100f;

            return new ImageInline(data, width, height, imagePart.ContentType);
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

            return new ImageInline(data, width, height, imagePart.ContentType);
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
            var shape = element.Descendants()
                .FirstOrDefault(node => node.LocalName.Equals("shape", StringComparison.OrdinalIgnoreCase));
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

        public ChartResolver(OpenXmlPart? part)
        {
            _part = part;
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
            var model = TryParseChartModel(chartPart);
            var chart = new ChartInline(width, height, model, data)
            {
                Name = drawing.Descendants<DW.DocProperties>().FirstOrDefault()?.Name?.Value
            };

            return chart;
        }
    }

    private static ChartModel? TryParseChartModel(ChartPart chartPart)
    {
        var chartSpace = chartPart.ChartSpace;
        var chart = chartSpace?.GetFirstChild<C.Chart>();
        if (chart is null)
        {
            return null;
        }

        var model = new ChartModel
        {
            Title = ExtractChartTitle(chart)
        };

        var plotArea = chart.PlotArea;
        if (plotArea is null)
        {
            return model;
        }

        if (plotArea.GetFirstChild<C.BarChart>() is { } barChart)
        {
            model.Type = ChartType.Bar;
            AddSeries(barChart, model);
            return model;
        }

        if (plotArea.GetFirstChild<C.LineChart>() is { } lineChart)
        {
            model.Type = ChartType.Line;
            AddSeries(lineChart, model);
            return model;
        }

        if (plotArea.GetFirstChild<C.PieChart>() is { } pieChart)
        {
            model.Type = ChartType.Pie;
            AddSeries(pieChart, model);
            return model;
        }

        if (plotArea.GetFirstChild<C.ScatterChart>() is { } scatterChart)
        {
            model.Type = ChartType.Scatter;
            AddSeries(scatterChart, model);
            return model;
        }

        if (plotArea.GetFirstChild<C.AreaChart>() is { } areaChart)
        {
            model.Type = ChartType.Area;
            AddSeries(areaChart, model);
            return model;
        }

        model.Type = ChartType.Unknown;
        return model;
    }

    private static string? ExtractChartTitle(C.Chart chart)
    {
        var title = chart.Title;
        if (title is null)
        {
            return null;
        }

        var text = title.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AddSeries(OpenXmlElement chartElement, ChartModel model)
    {
        foreach (var seriesElement in chartElement.Elements().Where(element => element.LocalName == "ser"))
        {
            var series = new ChartSeries
            {
                Name = ExtractSeriesName(seriesElement)
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
