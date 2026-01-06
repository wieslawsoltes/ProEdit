using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using OpenXmlTableCellBorders = DocumentFormat.OpenXml.Wordprocessing.TableCellBorders;
using OpenXmlParagraphBorders = DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;
using VibeDocument = Vibe.Office.Documents.Document;

namespace Vibe.Office.OpenXml;

public sealed class DocxImporter
{
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
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer));

        if (body is null)
        {
            document.Blocks.Add(new ParagraphBlock());
            return document;
        }

        var styleResolver = new StyleResolver(mainPart, document);
        var listResolver = new ListResolver(mainPart, document, styleResolver);
        var imageResolver = new ImageResolver(mainPart);
        var hyperlinkResolver = new HyperlinkResolver(mainPart);
        LoadNotesAndComments(mainPart, document, listResolver, styleResolver);
        var currentSectionIndex = 0;
        var currentSection = document.GetSection(currentSectionIndex);
        foreach (var element in body.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    foreach (var block in ParseParagraphBlocks(paragraph, listResolver, imageResolver, hyperlinkResolver, styleResolver, true))
                    {
                        document.Blocks.Add(block);
                    }
                    var sectionProps = paragraph.ParagraphProperties?.SectionProperties;
                    if (sectionProps is not null)
                    {
                        ApplySectionProperties(currentSection.Properties, ParseSectionProperties(sectionProps));
                        LoadSectionHeaderFooter(mainPart, sectionProps, currentSection, listResolver, styleResolver);

                        var breakType = ParseSectionBreakType(sectionProps);
                        var nextSection = new DocumentSection();
                        document.Sections.Add(nextSection);
                        currentSectionIndex = document.Sections.Count - 1;
                        document.Blocks.Add(new SectionBreakBlock
                        {
                            BreakType = breakType,
                            SectionIndex = currentSectionIndex
                        });
                        currentSection = nextSection;
                    }
                    break;
                case Table table:
                    document.Blocks.Add(ParseTable(table, listResolver, imageResolver, hyperlinkResolver, styleResolver));
                    break;
                case SdtBlock sdtBlock:
                    foreach (var block in ParseSdtBlock(sdtBlock, listResolver, imageResolver, hyperlinkResolver, styleResolver))
                    {
                        document.Blocks.Add(block);
                    }
                    break;
            }
        }

        var bodySectionProps = body.Elements<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>().LastOrDefault();
        if (bodySectionProps is not null)
        {
            ApplySectionProperties(currentSection.Properties, ParseSectionProperties(bodySectionProps));
            LoadSectionHeaderFooter(mainPart, bodySectionProps, currentSection, listResolver, styleResolver);
        }

        if (document.Blocks.Count == 0 || document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }

    private static ParagraphBlock ParseParagraph(Paragraph paragraph, ListResolver listResolver, ImageResolver imageResolver, HyperlinkResolver hyperlinkResolver, StyleResolver styleResolver)
    {
        var blocks = ParseParagraphBlocks(paragraph, listResolver, imageResolver, hyperlinkResolver, styleResolver, false);
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock paragraphBlock)
            {
                return paragraphBlock;
            }
        }

        return new ParagraphBlock();
    }

    private static TableBlock ParseTable(Table table, ListResolver listResolver, ImageResolver imageResolver, HyperlinkResolver hyperlinkResolver, StyleResolver styleResolver)
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
                tableCell.Paragraphs.Add(ParseParagraph(paragraph, listResolver, imageResolver, hyperlinkResolver, styleResolver));
            }

            if (tableCell.Paragraphs.Count == 0)
            {
                tableCell.Paragraphs.Add(new ParagraphBlock());
            }

            return tableCell;
        }

        void AppendRow(Vibe.Office.Documents.TableRow tableRow)
        {
            tableBlock.Rows.Add(tableRow);
        }

        foreach (var rowElement in table.Elements())
        {
            DocumentFormat.OpenXml.Wordprocessing.TableRow? row = null;
            ContentControlProperties? rowContentControl = null;
            if (rowElement is DocumentFormat.OpenXml.Wordprocessing.TableRow directRow)
            {
                row = directRow;
            }
            else if (rowElement is SdtRow sdtRow)
            {
                rowContentControl = ParseContentControlProperties(sdtRow.SdtProperties);
                row = sdtRow.SdtContentRow?.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().FirstOrDefault();
            }

            if (row is null)
            {
                continue;
            }

            var tableRow = new Vibe.Office.Documents.TableRow { ContentControl = rowContentControl };
            ApplyTableRowProperties(row, tableRow.Properties);
            foreach (var cellElement in row.Elements())
            {
                if (cellElement is DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
                {
                    tableRow.Cells.Add(ParseTableCell(cell));
                }
                else if (cellElement is SdtCell sdtCell)
                {
                    var cellControl = ParseContentControlProperties(sdtCell.SdtProperties);
                    var innerCell = sdtCell.SdtContentCell?.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().FirstOrDefault();
                    if (innerCell is null)
                    {
                        continue;
                    }

                    var tableCell = ParseTableCell(innerCell);
                    tableCell.ContentControl = cellControl;
                    tableRow.Cells.Add(tableCell);
                }
            }

            AppendRow(tableRow);
        }

        return tableBlock;
    }

    private static void LoadSectionHeaderFooter(MainDocumentPart? mainPart, DocumentFormat.OpenXml.Wordprocessing.SectionProperties sectionProps, DocumentSection section, ListResolver listResolver, StyleResolver styleResolver)
    {
        if (mainPart is null)
        {
            return;
        }

        var headerRef = sectionProps.Elements<HeaderReference>()
            .FirstOrDefault(item => item.Type is null || item.Type == HeaderFooterValues.Default);
        HeaderPart? headerPart = null;
        if (headerRef?.Id?.Value is string headerId)
        {
            headerPart = mainPart.GetPartById(headerId) as HeaderPart;
        }
        else if (mainPart.HeaderParts.Count() == 1)
        {
            headerPart = mainPart.HeaderParts.FirstOrDefault();
        }

        if (headerPart?.Header is not null)
        {
            section.Header.Blocks.Clear();
            var headerBlocks = ParseHeaderFooter(headerPart.Header, listResolver, new ImageResolver(headerPart), new HyperlinkResolver(headerPart), styleResolver);
            foreach (var block in headerBlocks)
            {
                section.Header.Blocks.Add(block);
            }
        }

        var footerRef = sectionProps.Elements<FooterReference>()
            .FirstOrDefault(item => item.Type is null || item.Type == HeaderFooterValues.Default);
        FooterPart? footerPart = null;
        if (footerRef?.Id?.Value is string footerId)
        {
            footerPart = mainPart.GetPartById(footerId) as FooterPart;
        }
        else if (mainPart.FooterParts.Count() == 1)
        {
            footerPart = mainPart.FooterParts.FirstOrDefault();
        }

        if (footerPart?.Footer is not null)
        {
            section.Footer.Blocks.Clear();
            var footerBlocks = ParseHeaderFooter(footerPart.Footer, listResolver, new ImageResolver(footerPart), new HyperlinkResolver(footerPart), styleResolver);
            foreach (var block in footerBlocks)
            {
                section.Footer.Blocks.Add(block);
            }
        }
    }

    private static void LoadNotesAndComments(MainDocumentPart? mainPart, VibeDocument document, ListResolver listResolver, StyleResolver styleResolver)
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
                foreach (var block in ParseHeaderFooter(footnote, listResolver, imageResolver, hyperlinkResolver, styleResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Footnotes[id] = definition;
            }
        }

        if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
        {
            var imageResolver = new ImageResolver(mainPart.EndnotesPart);
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
                foreach (var block in ParseHeaderFooter(endnote, listResolver, imageResolver, hyperlinkResolver, styleResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Endnotes[id] = definition;
            }
        }

        if (mainPart.WordprocessingCommentsPart?.Comments is { } comments)
        {
            var imageResolver = new ImageResolver(mainPart.WordprocessingCommentsPart);
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

                foreach (var block in ParseHeaderFooter(comment, listResolver, imageResolver, hyperlinkResolver, styleResolver))
                {
                    definition.Blocks.Add(block);
                }

                document.Comments[id] = definition;
            }
        }
    }

    private static List<Block> ParseHeaderFooter(OpenXmlCompositeElement root, ListResolver listResolver, ImageResolver imageResolver, HyperlinkResolver hyperlinkResolver, StyleResolver styleResolver)
    {
        var blocks = new List<Block>();
        foreach (var element in root.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    blocks.AddRange(ParseParagraphBlocks(paragraph, listResolver, imageResolver, hyperlinkResolver, styleResolver, false));
                    break;
                case Table table:
                    blocks.Add(ParseTable(table, listResolver, imageResolver, hyperlinkResolver, styleResolver));
                    break;
                case SdtBlock sdtBlock:
                    blocks.AddRange(ParseSdtBlock(sdtBlock, listResolver, imageResolver, hyperlinkResolver, styleResolver));
                    break;
            }
        }

        return blocks;
    }

    private static List<Block> ParseSdtBlock(
        SdtBlock sdtBlock,
        ListResolver listResolver,
        ImageResolver imageResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver)
    {
        var blocks = new List<Block>();
        var properties = ParseContentControlProperties(sdtBlock.SdtProperties);
        blocks.Add(new ContentControlStartBlock(properties));

        var content = sdtBlock.SdtContentBlock;
        if (content is not null)
        {
            foreach (var element in content.Elements())
            {
                switch (element)
                {
                    case Paragraph paragraph:
                        blocks.AddRange(ParseParagraphBlocks(paragraph, listResolver, imageResolver, hyperlinkResolver, styleResolver, false));
                        break;
                    case Table table:
                        blocks.Add(ParseTable(table, listResolver, imageResolver, hyperlinkResolver, styleResolver));
                        break;
                    case SdtBlock nestedBlock:
                        blocks.AddRange(ParseSdtBlock(nestedBlock, listResolver, imageResolver, hyperlinkResolver, styleResolver));
                        break;
                }
            }
        }

        blocks.Add(new ContentControlEndBlock(properties.Id));
        return blocks;
    }

    private static ContentControlProperties ParseContentControlProperties(SdtProperties? properties)
    {
        var result = new ContentControlProperties();
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
            result.Lock = lockValue.ToString();
        }

        var placeholder = properties.GetFirstChild<SdtPlaceholder>()?.DocPartReference?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            result.Placeholder = placeholder;
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

    private static List<Block> ParseParagraphBlocks(
        Paragraph paragraph,
        ListResolver listResolver,
        ImageResolver imageResolver,
        HyperlinkResolver hyperlinkResolver,
        StyleResolver styleResolver,
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

        void FinalizeFieldInstruction(FieldParseState state)
        {
            if (state.InstructionFinalized)
            {
                return;
            }

            var instruction = state.Instruction.ToString();
            state.StartInline.Instruction = instruction;
            state.IsPageField = IsPageFieldInstruction(instruction);
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
            if (state.IsPageField && !state.AddedPageInline)
            {
                var fallbackStyle = styleResolver.ResolveRunStyle(paragraphStyleId, null, null);
                AddInline(new PageNumberInline(fallbackStyle), true, null);
            }

            current.Inlines.Add(new FieldEndInline());
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
            var suppressResultText = fieldState is not null && fieldState.InResult && fieldState.IsPageField;
            if (suppressResultText && !fieldState!.AddedPageInline)
            {
                var fieldStyle = styleResolver.ResolveRunStyle(paragraphStyleId, run.RunProperties, styleResolver.GetRunStyleId(run));
                AddInline(new PageNumberInline(fieldStyle), true, hyperlink);
                fieldState.AddedPageInline = true;
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

                        var fontValue = element.GetAttribute("font", element.NamespaceUri).Value;
                        var charValue = element.GetAttribute("char", element.NamespaceUri).Value;
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
                            var imageInline = imageResolver.TryCreateImage(drawing);
                            if (imageInline is not null)
                            {
                                AddInline(imageInline, true, hyperlink);
                            }
                        }

                        break;
                    case OpenXmlElement element when element.LocalName == "object" || element.LocalName == "pict":
                        if (!suppressResultText)
                        {
                            FlushRunBuffer(buffer, style, runStyleId, current, builder, hyperlink);
                            var imageInline = imageResolver.TryCreateImageFromVml(element);
                            if (imageInline is not null)
                            {
                                AddInline(imageInline, true, hyperlink);
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
            AddInline(startInline, false, hyperlink);
            current.Inlines.Add(new FieldSeparatorInline());

            if (IsPageFieldInstruction(instruction))
            {
                var run = field.Elements<Run>().FirstOrDefault();
                var fieldStyle = styleResolver.ResolveRunStyle(paragraphStyleId, run?.RunProperties, styleResolver.GetRunStyleId(run));
                AddInline(new PageNumberInline(fieldStyle), true, hyperlink);
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
                    var properties = ParseContentControlProperties(sdtRun.SdtProperties);
                    AddInline(new ContentControlStartInline(properties), false, null);
                    foreach (var child in sdtRun.SdtContentRun?.Elements() ?? Enumerable.Empty<OpenXmlElement>())
                    {
                        ProcessElement(child, hyperlink);
                    }

                    AddInline(new ContentControlEndInline(properties.Id), false, null);
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

    private static bool IsPageFieldInstruction(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return false;
        }

        return instruction.IndexOf("PAGE", StringComparison.OrdinalIgnoreCase) >= 0
               || instruction.IndexOf("NUMPAGES", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class FieldParseState
    {
        public FieldStartInline StartInline { get; }
        public StringBuilder Instruction { get; } = new StringBuilder();
        public bool InstructionFinalized { get; set; }
        public bool InResult { get; set; }
        public bool IsPageField { get; set; }
        public bool AddedPageInline { get; set; }

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
        var family = fonts?.Ascii?.Value
                     ?? fonts?.HighAnsi?.Value
                     ?? fonts?.ComplexScript?.Value
                     ?? fonts?.EastAsia?.Value;
        if (!string.IsNullOrWhiteSpace(family))
        {
            style.FontFamily = family;
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
        var family = fonts?.Ascii?.Value
                     ?? fonts?.HighAnsi?.Value
                     ?? fonts?.ComplexScript?.Value
                     ?? fonts?.EastAsia?.Value;
        if (!string.IsNullOrWhiteSpace(family))
        {
            style.FontFamily = family;
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

        var trimmed = value.Trim();
        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        var unitIndex = trimmed.Length;
        while (unitIndex > 0 && char.IsLetter(trimmed[unitIndex - 1]))
        {
            unitIndex--;
        }

        if (unitIndex <= 0 || unitIndex >= trimmed.Length)
        {
            return null;
        }

        var numberPart = trimmed.Substring(0, unitIndex);
        if (!float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var valuePart))
        {
            return null;
        }

        var unit = trimmed.Substring(unitIndex).ToLowerInvariant();
        return unit switch
        {
            "pt" => valuePart * 20f,
            "in" => valuePart * 72f * 20f,
            "cm" => valuePart / 2.54f * 72f * 20f,
            "mm" => valuePart / 25.4f * 72f * 20f,
            "pc" => valuePart * 12f * 20f,
            "pi" => valuePart * 12f * 20f,
            "twip" => valuePart,
            "twips" => valuePart,
            _ => null
        };
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

    private sealed class ImageResolver
    {
        private readonly OpenXmlPart? _part;
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public ImageResolver(OpenXmlPart? part)
        {
            _part = part;
        }

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

            var idAttribute = imageData.GetAttribute("id", RelationshipNamespace);
            if (string.IsNullOrWhiteSpace(idAttribute.Value))
            {
                return new ImageInline(Array.Empty<byte>(), width, height, "application/vnd.ms-office.ole");
            }

            if (_part.GetPartById(idAttribute.Value) is not ImagePart imagePart)
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

            var styleValue = shape.GetAttribute("style", string.Empty).Value;
            if (string.IsNullOrWhiteSpace(styleValue))
            {
                return (100f, 100f);
            }

            var width = 0f;
            var height = 0f;
            foreach (var part in styleValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = part.Trim();
                if (piece.StartsWith("width", StringComparison.OrdinalIgnoreCase))
                {
                    var value = piece.Split(':', 2);
                    if (value.Length == 2 && TryParseVmlLength(value[1], out var parsed))
                    {
                        width = parsed;
                    }
                }
                else if (piece.StartsWith("height", StringComparison.OrdinalIgnoreCase))
                {
                    var value = piece.Split(':', 2);
                    if (value.Length == 2 && TryParseVmlLength(value[1], out var parsed))
                    {
                        height = parsed;
                    }
                }
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
            dip = 0f;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var index = 0;
            while (index < trimmed.Length && (char.IsDigit(trimmed[index]) || trimmed[index] == '.' || trimmed[index] == '-'))
            {
                index++;
            }

            if (index == 0)
            {
                return false;
            }

            if (!float.TryParse(trimmed[..index], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            var unit = trimmed[index..].Trim().ToLowerInvariant();
            dip = unit switch
            {
                "in" => number * 96f,
                "pt" => number * 96f / 72f,
                "px" => number,
                "cm" => number * 96f / 2.54f,
                "mm" => number * 96f / 25.4f,
                "" => number,
                _ => number
            };

            return true;
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
