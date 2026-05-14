using System.Globalization;
using ProEdit.Documents;
using ProEdit.Html;
using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown;

public static class MarkdownAstConverter
{
    public static Document ToDocument(MarkdownDocument document, MarkdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effectiveOptions = options ?? new MarkdownOptions();
        var context = new MarkdownToDocumentContext(effectiveOptions);
        var doc = new Document();
        MarkdownStyleSeeder.Seed(doc, effectiveOptions);
        doc.Blocks.Clear();
        AppendBlocks(doc.Blocks, document.Blocks, context);
        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new ParagraphBlock());
        }

        return doc;
    }

    public static MarkdownDocument FromDocument(Document document, MarkdownOptions? options = null, MarkdownNodeIdProvider? idProvider = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effectiveOptions = options ?? new MarkdownOptions();
        var provider = idProvider ?? new MarkdownNodeIdProvider();
        var ast = new MarkdownDocument(provider.NextId(), MarkdownTextSpan.Unknown);
        var index = 0;
        var blocks = TrimTrailingImplicitParagraph(document.Blocks);
        ast.Blocks.AddRange(ConvertBlocks(blocks, provider, effectiveOptions, ref index, stopOnMetadata: null));
        return ast;
    }

    private static IReadOnlyList<Block> TrimTrailingImplicitParagraph(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return blocks;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is not ParagraphBlock paragraph || !IsImplicitTrailingParagraph(paragraph))
        {
            return blocks;
        }

        if (lastIndex == 0)
        {
            return Array.Empty<Block>();
        }

        var trimmed = new List<Block>(lastIndex);
        for (var i = 0; i < lastIndex; i++)
        {
            trimmed.Add(blocks[i]);
        }

        return trimmed;
    }

    private static bool IsImplicitTrailingParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.ListInfo is not null)
        {
            return false;
        }

        if (paragraph.Inlines.Count > 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(paragraph.Text))
        {
            return false;
        }

        if (paragraph.FloatingObjects.Count > 0)
        {
            return false;
        }

        return true;
    }

    private static void AppendBlocks(List<Block> target, IReadOnlyList<MarkdownBlock> blocks, MarkdownToDocumentContext context)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownParagraphBlock paragraph:
                    target.Add(BuildParagraph(paragraph, context));
                    break;
                case MarkdownHeadingBlock heading:
                    target.Add(BuildHeading(heading, context));
                    break;
                case MarkdownBlockQuoteBlock quote:
                    AppendBlockQuote(target, quote, context);
                    break;
                case MarkdownListBlock list:
                    AppendList(target, list, context, level: 0);
                    break;
                case MarkdownTableBlock table:
                    AppendTable(target, table, context);
                    break;
                case MarkdownCodeBlock code:
                    AppendCodeBlock(target, code, context);
                    break;
                case MarkdownThematicBreakBlock:
                    AppendThematicBreak(target);
                    break;
                case MarkdownHtmlBlock html:
                    if (context.Options.AllowHtmlBlocks && TryAppendHtmlBlock(target, html.Html, context.HtmlOptions))
                    {
                        break;
                    }

                    target.Add(new ParagraphBlock(html.Html));
                    break;
            }
        }
    }

    private static ParagraphBlock BuildHeading(MarkdownHeadingBlock heading, MarkdownToDocumentContext context)
    {
        var paragraph = new ParagraphBlock();
        var level = Math.Clamp(heading.Level, 1, 6);
        paragraph.StyleId = MarkdownStyleIds.Heading(level);
        AppendInlines(paragraph.Inlines, heading.Inlines, style: null, hyperlink: null, context);
        ApplyBlockQuoteIndentIfNeeded(paragraph, context);
        return paragraph;
    }

    private static ParagraphBlock BuildParagraph(MarkdownParagraphBlock paragraph, MarkdownToDocumentContext context)
    {
        var block = new ParagraphBlock();
        if (context.BlockQuoteDepth > 0)
        {
            block.StyleId = MarkdownStyleIds.BlockQuote;
        }
        AppendInlines(block.Inlines, paragraph.Inlines, style: null, hyperlink: null, context);
        ApplyBlockQuoteIndentIfNeeded(block, context);
        return block;
    }

    private static void AppendBlockQuote(List<Block> target, MarkdownBlockQuoteBlock quote, MarkdownToDocumentContext context)
    {
        var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.BlockQuote);
        target.Add(new MetadataStartBlock(metadata));
        context.BlockQuoteDepth++;
        AppendBlocks(target, quote.Blocks, context);
        context.BlockQuoteDepth = Math.Max(0, context.BlockQuoteDepth - 1);
        target.Add(new MetadataEndBlock(metadata));
    }

    private static void AppendList(List<Block> target, MarkdownListBlock list, MarkdownToDocumentContext context, int level)
    {
        var listId = ++context.ListIdCounter;
        foreach (var item in list.Items)
        {
            AppendListItem(target, item, list, context, listId, level);
        }
    }

    private static void AppendListItem(
        List<Block> target,
        MarkdownListItemBlock item,
        MarkdownListBlock list,
        MarkdownToDocumentContext context,
        int listId,
        int level)
    {
        var listInfo = new ListInfo(list.Kind == MarkdownListKind.Ordered ? ListKind.Numbered : ListKind.Bullet, level, listId)
        {
            StartAt = list.StartNumber
        };

        if (item.Blocks.Count == 0)
        {
            var empty = new ParagraphBlock();
            empty.ListInfo = listInfo;
            target.Add(empty);
            return;
        }

        foreach (var block in item.Blocks)
        {
            switch (block)
            {
                case MarkdownParagraphBlock paragraph:
                {
                    var docParagraph = BuildParagraph(paragraph, context);
                    if (string.IsNullOrWhiteSpace(docParagraph.StyleId)
                        || string.Equals(docParagraph.StyleId, MarkdownStyleIds.Normal, StringComparison.OrdinalIgnoreCase))
                    {
                        docParagraph.StyleId = MarkdownStyleIds.ListParagraph;
                    }
                    docParagraph.ListInfo = listInfo;
                    if (item.IsTask == true)
                    {
                        AppendTaskMetadata(docParagraph, item.TaskChecked == true);
                    }
                    target.Add(docParagraph);
                    break;
                }
                case MarkdownHeadingBlock heading:
                {
                    var docHeading = BuildHeading(heading, context);
                    docHeading.ListInfo = listInfo;
                    if (item.IsTask == true)
                    {
                        AppendTaskMetadata(docHeading, item.TaskChecked == true);
                    }
                    target.Add(docHeading);
                    break;
                }
                case MarkdownListBlock nestedList:
                    AppendList(target, nestedList, context, level + 1);
                    break;
                case MarkdownCodeBlock code:
                    AppendCodeBlock(target, code, context);
                    break;
                case MarkdownBlockQuoteBlock quote:
                    AppendBlockQuote(target, quote, context);
                    break;
            }
        }
    }

    private static void AppendTable(List<Block> target, MarkdownTableBlock table, MarkdownToDocumentContext context)
    {
        var docTable = new TableBlock
        {
            StyleId = MarkdownStyleIds.MarkdownTable
        };
        foreach (var row in table.Rows)
        {
            var docRow = new TableRow();
            foreach (var cell in row.Cells)
            {
                var paragraph = new ParagraphBlock
                {
                    StyleId = row.IsHeader ? MarkdownStyleIds.TableHeader : MarkdownStyleIds.TableCell
                };
                AppendInlines(paragraph.Inlines, cell.Inlines, style: null, hyperlink: null, context);
                ApplyTableAlignment(table, paragraph, docRow.Cells.Count);
                var docCell = new TableCell(new[] { paragraph });
                docRow.Cells.Add(docCell);
            }

            docTable.Rows.Add(docRow);
        }

        target.Add(docTable);
    }

    private static void AppendCodeBlock(List<Block> target, MarkdownCodeBlock code, MarkdownToDocumentContext context)
    {
        var attributes = new List<MetadataAttribute>();
        if (!string.IsNullOrWhiteSpace(code.Info))
        {
            attributes.Add(MarkdownMetadata.Attribute(MarkdownMetadata.AttrInfo, code.Info));
        }

        attributes.Add(MarkdownMetadata.Attribute(MarkdownMetadata.AttrFence, code.IsFenced ? "1" : "0"));
        var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.CodeBlock, attributes);
        target.Add(new MetadataStartBlock(metadata));

        var paragraph = new ParagraphBlock(code.Text ?? string.Empty)
        {
            StyleId = MarkdownStyleIds.CodeBlock
        };
        ApplyBlockQuoteIndentIfNeeded(paragraph, context);
        target.Add(paragraph);

        target.Add(new MetadataEndBlock(metadata));
    }

    private static void AppendThematicBreak(List<Block> target)
    {
        var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.ThematicBreak);
        target.Add(new MetadataStartBlock(metadata));
        target.Add(new ParagraphBlock());
        target.Add(new MetadataEndBlock(metadata));
    }

    private static void AppendInlines(
        List<Inline> target,
        IReadOnlyList<MarkdownInline> inlines,
        TextStyleProperties? style,
        HyperlinkInfo? hyperlink,
        MarkdownToDocumentContext context)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    AppendRun(target, text.Text, style, hyperlink);
                    break;
                case MarkdownEmphasisInline emphasis:
                {
                    var nextStyle = CloneStyle(style);
                    if (emphasis.IsStrong)
                    {
                        nextStyle.FontWeight = DocFontWeight.Bold;
                    }
                    else
                    {
                        nextStyle.FontStyle = DocFontStyle.Italic;
                    }

                    AppendInlines(target, emphasis.Inlines, nextStyle, hyperlink, context);
                    break;
                }
                case MarkdownStrikethroughInline strike:
                {
                    var nextStyle = CloneStyle(style);
                    nextStyle.Strikethrough = true;
                    AppendInlines(target, strike.Inlines, nextStyle, hyperlink, context);
                    break;
                }
                case MarkdownCodeInline code:
                {
                    var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.CodeSpan);
                    target.Add(new MetadataStartInline(metadata));
                    if (hyperlink is null)
                    {
                        AppendRun(target, code.Code, style, hyperlink, MarkdownStyleIds.CodeInline);
                    }
                    else
                    {
                        var codeStyle = style?.Clone() ?? new TextStyleProperties();
                        ApplyCodeInlineStyle(codeStyle);
                        AppendRun(target, code.Code, codeStyle, hyperlink);
                    }
                    target.Add(new MetadataEndInline(metadata));
                    break;
                }
                case MarkdownLinkInline link:
                {
                    var linkInfo = new HyperlinkInfo(link.Url, null, link.Title);
                    AppendInlines(target, link.Inlines, style, linkInfo, context);
                    break;
                }
                case MarkdownImageInline image:
                {
                    var attributes = new List<MetadataAttribute>
                    {
                        MarkdownMetadata.Attribute(MarkdownMetadata.AttrUrl, image.Url)
                    };
                    if (!string.IsNullOrWhiteSpace(image.Title))
                    {
                        attributes.Add(MarkdownMetadata.Attribute(MarkdownMetadata.AttrTitle, image.Title));
                    }

                    var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.Image, attributes);
                    target.Add(new MetadataStartInline(metadata));
                    var altText = ExtractInlineText(image.AltText);
                    AppendRun(target, altText, style, hyperlink);
                    target.Add(new MetadataEndInline(metadata));
                    break;
                }
                case MarkdownHardBreakInline:
                case MarkdownSoftBreakInline:
                    AppendRun(target, "\n", style, hyperlink);
                    break;
                case MarkdownHtmlInline html:
                    if (context.Options.AllowHtmlInlines && TryAppendHtmlInline(target, html.Html, context.HtmlOptions))
                    {
                        break;
                    }

                    AppendRun(target, html.Html, style, hyperlink);
                    break;
            }
        }
    }

    private static void AppendTaskMetadata(ParagraphBlock paragraph, bool isChecked)
    {
        var attributes = new List<MetadataAttribute>
        {
            MarkdownMetadata.Attribute(MarkdownMetadata.AttrChecked, isChecked ? "1" : "0")
        };

        var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.TaskList, attributes);
        paragraph.Inlines.Insert(0, new MetadataStartInline(metadata));
        paragraph.Inlines.Add(new MetadataEndInline(metadata));
    }

    private static void AppendRun(
        List<Inline> target,
        string text,
        TextStyleProperties? style,
        HyperlinkInfo? hyperlink,
        string? styleId = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var runStyle = style is null ? null : style.Clone();
        var run = new RunInline(text, runStyle);
        run.StyleId = styleId;
        if (hyperlink is not null)
        {
            run.Hyperlink = hyperlink;
        }

        target.Add(run);
    }

    private static TextStyleProperties CloneStyle(TextStyleProperties? style)
    {
        return style?.Clone() ?? new TextStyleProperties();
    }

    private static string ExtractInlineText(IReadOnlyList<MarkdownInline> inlines)
    {
        if (inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    builder.Append(text.Text);
                    break;
                case MarkdownCodeInline code:
                    builder.Append(code.Code);
                    break;
                case MarkdownEmphasisInline emphasis:
                    builder.Append(ExtractInlineText(emphasis.Inlines));
                    break;
                case MarkdownStrikethroughInline strike:
                    builder.Append(ExtractInlineText(strike.Inlines));
                    break;
                case MarkdownSoftBreakInline:
                case MarkdownHardBreakInline:
                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString();
    }

    private static List<MarkdownBlock> ConvertBlocks(
        IReadOnlyList<Block> blocks,
        MarkdownNodeIdProvider idProvider,
        MarkdownOptions options,
        ref int index,
        string? stopOnMetadata)
    {
        var result = new List<MarkdownBlock>();
        while (index < blocks.Count)
        {
            var block = blocks[index];
            if (block is MetadataStartBlock metadataStart)
            {
                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.BlockQuote))
                {
                    index++;
                    var innerBlocks = ConvertBlocks(blocks, idProvider, options, ref index, MarkdownMetadata.BlockQuote);
                    var quote = new MarkdownBlockQuoteBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                    quote.Blocks.AddRange(innerBlocks);
                    result.Add(quote);
                    continue;
                }

                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.CodeBlock))
                {
                    var code = new MarkdownCodeBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                    var info = MarkdownMetadata.GetAttribute(metadataStart.Metadata, MarkdownMetadata.AttrInfo);
                    code.Info = info;
                    var fence = MarkdownMetadata.GetAttribute(metadataStart.Metadata, MarkdownMetadata.AttrFence);
                    code.IsFenced = string.Equals(fence, "1", StringComparison.Ordinal);
                    index++;
                    if (index < blocks.Count && blocks[index] is ParagraphBlock paragraph)
                    {
                        code.Text = GetParagraphText(paragraph);
                        index++;
                    }

                    while (index < blocks.Count)
                    {
                        if (blocks[index] is MetadataEndBlock metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.CodeBlock))
                        {
                            index++;
                            break;
                        }

                        index++;
                    }

                    result.Add(code);
                    continue;
                }

                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.ThematicBreak))
                {
                    index++;
                    while (index < blocks.Count)
                    {
                        if (blocks[index] is MetadataEndBlock metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.ThematicBreak))
                        {
                            index++;
                            break;
                        }

                        index++;
                    }

                    result.Add(new MarkdownThematicBreakBlock(idProvider.NextId(), MarkdownTextSpan.Unknown));
                    continue;
                }

                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.HtmlBlock))
                {
                    var html = metadataStart.Metadata.Element.Text ?? string.Empty;
                    index++;
                    while (index < blocks.Count)
                    {
                        if (blocks[index] is MetadataEndBlock metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.HtmlBlock))
                        {
                            index++;
                            break;
                        }

                        index++;
                    }

                    var htmlBlock = new MarkdownHtmlBlock(idProvider.NextId(), MarkdownTextSpan.Unknown)
                    {
                        Html = html
                    };
                    result.Add(htmlBlock);
                    continue;
                }
            }

            if (block is MetadataEndBlock metadataEndBlock)
            {
                if (!string.IsNullOrWhiteSpace(stopOnMetadata)
                    && MarkdownMetadata.IsMarkdownMetadata(metadataEndBlock.Metadata, stopOnMetadata))
                {
                    index++;
                    break;
                }

                index++;
                continue;
            }

            if (block is TableBlock tableBlock)
            {
                var table = new MarkdownTableBlock(idProvider.NextId(), MarkdownTextSpan.Unknown)
                {
                    HasHeader = tableBlock.Rows.Count > 0
                };
                table.Alignments.AddRange(GetTableAlignments(tableBlock));
                for (var rowIndex = 0; rowIndex < tableBlock.Rows.Count; rowIndex++)
                {
                    var row = tableBlock.Rows[rowIndex];
                    var rowNode = new MarkdownTableRow(idProvider.NextId(), MarkdownTextSpan.Unknown)
                    {
                        IsHeader = rowIndex == 0
                    };
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        var cellNode = new MarkdownTableCell(idProvider.NextId(), MarkdownTextSpan.Unknown);
                        if (cell.Paragraphs.Count > 0)
                        {
                            cellNode.Inlines.AddRange(ConvertInlines(cell.Paragraphs[0], idProvider));
                        }
                        rowNode.Cells.Add(cellNode);
                    }
                    table.Rows.Add(rowNode);
                }

                result.Add(table);
                index++;
                continue;
            }

            if (block is ParagraphBlock paragraphBlock)
            {
                if (paragraphBlock.ListInfo is not null)
                {
                    result.Add(BuildListFromParagraphs(blocks, idProvider, options, ref index));
                    continue;
                }

                if (IsCodeBlockStyle(paragraphBlock))
                {
                    var code = new MarkdownCodeBlock(idProvider.NextId(), MarkdownTextSpan.Unknown)
                    {
                        Text = GetParagraphText(paragraphBlock),
                        IsFenced = options.PreferFencedCode
                    };
                    result.Add(code);
                    index++;
                    continue;
                }

                if (!string.Equals(stopOnMetadata, MarkdownMetadata.BlockQuote, StringComparison.Ordinal)
                    && IsBlockQuoteStyle(paragraphBlock))
                {
                    var quote = new MarkdownBlockQuoteBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                    var inner = new MarkdownParagraphBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                    inner.Inlines.AddRange(ConvertInlines(paragraphBlock, idProvider));
                    quote.Blocks.Add(inner);
                    result.Add(quote);
                    index++;
                    continue;
                }

                if (TryParseHeading(paragraphBlock.StyleId, out var level))
                {
                    var heading = new MarkdownHeadingBlock(idProvider.NextId(), MarkdownTextSpan.Unknown, level);
                    heading.Inlines.AddRange(ConvertInlines(paragraphBlock, idProvider));
                    result.Add(heading);
                    index++;
                    continue;
                }

                var paragraph = new MarkdownParagraphBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                paragraph.Inlines.AddRange(ConvertInlines(paragraphBlock, idProvider));
                result.Add(paragraph);
                index++;
                continue;
            }

            index++;
        }

        return result;
    }

    private static MarkdownListBlock BuildListFromParagraphs(
        IReadOnlyList<Block> blocks,
        MarkdownNodeIdProvider idProvider,
        MarkdownOptions options,
        ref int index)
    {
        var paragraph = (ParagraphBlock)blocks[index];
        var listInfo = paragraph.ListInfo!;
        var kind = listInfo.Kind == ListKind.Numbered ? MarkdownListKind.Ordered : MarkdownListKind.Bullet;
        var list = new MarkdownListBlock(idProvider.NextId(), MarkdownTextSpan.Unknown, kind)
        {
            StartNumber = listInfo.StartAt
        };

        var listId = listInfo.ListId;
        var level = listInfo.Level;
        while (index < blocks.Count)
        {
            if (blocks[index] is not ParagraphBlock current || current.ListInfo is null)
            {
                break;
            }

            if (current.ListInfo.ListId != listId || current.ListInfo.Level != level)
            {
                break;
            }

            var item = new MarkdownListItemBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
            if (TryGetTaskMetadata(current, out var isChecked))
            {
                item.IsTask = true;
                item.TaskChecked = isChecked;
            }
            if (TryParseHeading(current.StyleId, out var headingLevel))
            {
                var heading = new MarkdownHeadingBlock(idProvider.NextId(), MarkdownTextSpan.Unknown, headingLevel);
                heading.Inlines.AddRange(ConvertInlines(current, idProvider));
                item.Blocks.Add(heading);
            }
            else if (IsCodeBlockStyle(current))
            {
                var code = new MarkdownCodeBlock(idProvider.NextId(), MarkdownTextSpan.Unknown)
                {
                    Text = GetParagraphText(current),
                    IsFenced = options.PreferFencedCode
                };
                item.Blocks.Add(code);
            }
            else
            {
                var paragraphBlock = new MarkdownParagraphBlock(idProvider.NextId(), MarkdownTextSpan.Unknown);
                paragraphBlock.Inlines.AddRange(ConvertInlines(current, idProvider));
                item.Blocks.Add(paragraphBlock);
            }

            list.Items.Add(item);
            index++;
        }

        return list;
    }

    private static bool TryGetTaskMetadata(ParagraphBlock paragraph, out bool isChecked)
    {
        isChecked = false;
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            if (paragraph.Inlines[i] is MetadataStartInline start
                && MarkdownMetadata.IsMarkdownMetadata(start.Metadata, MarkdownMetadata.TaskList))
            {
                var value = MarkdownMetadata.GetAttribute(start.Metadata, MarkdownMetadata.AttrChecked);
                isChecked = string.Equals(value, "1", StringComparison.Ordinal);
                return true;
            }
        }

        return false;
    }

    private static List<MarkdownInline> ConvertInlines(ParagraphBlock paragraph, MarkdownNodeIdProvider idProvider)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return ConvertTextWithBreaks(paragraph.Text ?? string.Empty, idProvider);
        }

        var result = new List<MarkdownInline>();
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            var inline = paragraph.Inlines[i];
            if (inline is MetadataStartInline metadataStart)
            {
                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.CodeSpan))
                {
                    var codeText = new System.Text.StringBuilder();
                    i++;
                    while (i < paragraph.Inlines.Count)
                    {
                        if (paragraph.Inlines[i] is MetadataEndInline metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.CodeSpan))
                        {
                            break;
                        }

                        if (paragraph.Inlines[i] is RunInline runInline)
                        {
                            codeText.Append(runInline.Text.GetText());
                        }

                        i++;
                    }

                    result.Add(new MarkdownCodeInline(idProvider.NextId(), MarkdownTextSpan.Unknown, codeText.ToString()));
                    continue;
                }

                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.Image))
                {
                    var url = MarkdownMetadata.GetAttribute(metadataStart.Metadata, MarkdownMetadata.AttrUrl) ?? string.Empty;
                    var title = MarkdownMetadata.GetAttribute(metadataStart.Metadata, MarkdownMetadata.AttrTitle);
                    var altText = new System.Text.StringBuilder();
                    i++;
                    while (i < paragraph.Inlines.Count)
                    {
                        if (paragraph.Inlines[i] is MetadataEndInline metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.Image))
                        {
                            break;
                        }

                        if (paragraph.Inlines[i] is RunInline runInline)
                        {
                            altText.Append(runInline.Text.GetText());
                        }

                        i++;
                    }

                    var image = new MarkdownImageInline(idProvider.NextId(), MarkdownTextSpan.Unknown, url);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        image.Title = title;
                    }

                    image.AltText.AddRange(ConvertTextWithBreaks(altText.ToString(), idProvider));
                    result.Add(image);
                    continue;
                }

                if (MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.HtmlInline))
                {
                    var html = metadataStart.Metadata.Element.Text ?? string.Empty;
                    i++;
                    while (i < paragraph.Inlines.Count)
                    {
                        if (paragraph.Inlines[i] is MetadataEndInline metadataEnd
                            && MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.HtmlInline))
                        {
                            break;
                        }

                        i++;
                    }

                    result.Add(new MarkdownHtmlInline(idProvider.NextId(), MarkdownTextSpan.Unknown, html));
                    continue;
                }
            }

            if (inline is RunInline run)
            {
                AppendRunInlines(result, run, idProvider);
                continue;
            }

            if (inline is ImageInline)
            {
                result.Add(new MarkdownTextInline(idProvider.NextId(), MarkdownTextSpan.Unknown, "[Image]"));
            }
        }

        return result;
    }

    private static void AppendRunInlines(List<MarkdownInline> target, RunInline run, MarkdownNodeIdProvider idProvider)
    {
        if (string.Equals(run.StyleId, MarkdownStyleIds.CodeInline, StringComparison.OrdinalIgnoreCase))
        {
            var codeInline = new MarkdownCodeInline(idProvider.NextId(), MarkdownTextSpan.Unknown, run.Text.GetText());
            if (run.Hyperlink is not null && !run.Hyperlink.IsEmpty)
            {
                var link = new MarkdownLinkInline(idProvider.NextId(), MarkdownTextSpan.Unknown, run.Hyperlink.Uri ?? string.Empty)
                {
                    Title = run.Hyperlink.Tooltip
                };
                link.Inlines.Add(codeInline);
                target.Add(link);
            }
            else
            {
                target.Add(codeInline);
            }

            return;
        }

        var text = run.Text.GetText();
        var hasBold = run.Style?.FontWeight == DocFontWeight.Bold;
        var hasItalic = run.Style?.FontStyle == DocFontStyle.Italic;
        var hasStrike = run.Style?.Strikethrough == true;
        var hyperlink = run.Hyperlink;

        var current = ConvertTextWithBreaks(text, idProvider);
        if (hasBold && hasItalic)
        {
            var italic = new MarkdownEmphasisInline(idProvider.NextId(), MarkdownTextSpan.Unknown) { IsStrong = false };
            italic.Inlines.AddRange(current);
            var strong = new MarkdownEmphasisInline(idProvider.NextId(), MarkdownTextSpan.Unknown) { IsStrong = true };
            strong.Inlines.Add(italic);
            current = new List<MarkdownInline> { strong };
        }
        else if (hasBold || hasItalic)
        {
            var emphasis = new MarkdownEmphasisInline(idProvider.NextId(), MarkdownTextSpan.Unknown)
            {
                IsStrong = hasBold
            };
            emphasis.Inlines.AddRange(current);
            current = new List<MarkdownInline> { emphasis };
        }

        if (hasStrike)
        {
            var strike = new MarkdownStrikethroughInline(idProvider.NextId(), MarkdownTextSpan.Unknown);
            strike.Inlines.AddRange(current);
            current = new List<MarkdownInline> { strike };
        }

        if (hyperlink is not null && !hyperlink.IsEmpty)
        {
            var link = new MarkdownLinkInline(idProvider.NextId(), MarkdownTextSpan.Unknown, hyperlink.Uri ?? string.Empty)
            {
                Title = hyperlink.Tooltip
            };
            link.Inlines.AddRange(current);
            current = new List<MarkdownInline> { link };
        }

        target.AddRange(current);
    }

    private static List<MarkdownInline> ConvertTextWithBreaks(string text, MarkdownNodeIdProvider idProvider)
    {
        var result = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (i > start)
                {
                    var slice = text.Substring(start, i - start);
                    result.Add(new MarkdownTextInline(idProvider.NextId(), MarkdownTextSpan.Unknown, slice));
                }

                result.Add(new MarkdownSoftBreakInline(idProvider.NextId(), MarkdownTextSpan.Unknown));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            result.Add(new MarkdownTextInline(idProvider.NextId(), MarkdownTextSpan.Unknown, text.Substring(start)));
        }

        return result;
    }

    private static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    builder.Append(run.Text.GetText());
                    break;
                case ImageInline:
                case ShapeInline:
                case ChartInline:
                case EquationInline:
                case PageNumberInline:
                case TotalPagesInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
                case FootnoteReferenceInline footnote:
                    builder.Append(footnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryParseHeading(string? styleId, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var trimmed = styleId.Trim();
        if (trimmed.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed.AsSpan(7);
            if (int.TryParse(suffix, out var parsed) && parsed is >= 1 and <= 6)
            {
                level = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool IsCodeBlockStyle(ParagraphBlock paragraph)
    {
        return string.Equals(paragraph.StyleId, MarkdownStyleIds.CodeBlock, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockQuoteStyle(ParagraphBlock paragraph)
    {
        return string.Equals(paragraph.StyleId, MarkdownStyleIds.BlockQuote, StringComparison.OrdinalIgnoreCase);
    }

    private static List<MarkdownTableAlignment> GetTableAlignments(TableBlock table)
    {
        var columnCount = 0;
        foreach (var row in table.Rows)
        {
            if (row.Cells.Count > columnCount)
            {
                columnCount = row.Cells.Count;
            }
        }

        var alignments = new List<MarkdownTableAlignment>(columnCount);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            ParagraphAlignment? resolved = null;
            var consistent = true;
            foreach (var row in table.Rows)
            {
                if (columnIndex >= row.Cells.Count)
                {
                    continue;
                }

                var cell = row.Cells[columnIndex];
                if (cell.Paragraphs.Count == 0 || cell.Paragraphs[0] is not ParagraphBlock paragraph)
                {
                    consistent = false;
                    break;
                }

                var alignment = paragraph.Properties.Alignment;
                if (!alignment.HasValue)
                {
                    consistent = false;
                    break;
                }

                if (!resolved.HasValue)
                {
                    resolved = alignment.Value;
                }
                else if (resolved.Value != alignment.Value)
                {
                    consistent = false;
                    break;
                }
            }

            if (!consistent || !resolved.HasValue)
            {
                alignments.Add(MarkdownTableAlignment.None);
                continue;
            }

            alignments.Add(resolved.Value switch
            {
                ParagraphAlignment.Left => MarkdownTableAlignment.Left,
                ParagraphAlignment.Center => MarkdownTableAlignment.Center,
                ParagraphAlignment.Right => MarkdownTableAlignment.Right,
                _ => MarkdownTableAlignment.None
            });
        }

        return alignments;
    }

    private sealed class MarkdownToDocumentContext
    {
        public MarkdownToDocumentContext(MarkdownOptions options)
        {
            Options = options ?? new MarkdownOptions();
            HtmlOptions = CreateHtmlOptions(Options);
        }

        public int ListIdCounter;
        public int BlockQuoteDepth;
        public MarkdownOptions Options;
        public HtmlOptions HtmlOptions;
    }

    private static bool TryAppendHtmlBlock(List<Block> target, string html, HtmlOptions htmlOptions)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), htmlOptions);
        if (document.Blocks.Count == 0)
        {
            return false;
        }

        foreach (var block in document.Blocks)
        {
            target.Add(DocumentClone.CloneBlock(block));
        }

        return true;
    }

    private static bool TryAppendHtmlInline(List<Inline> target, string html, HtmlOptions htmlOptions)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), htmlOptions);
        if (document.Blocks.Count != 1 || document.Blocks[0] is not ParagraphBlock paragraph)
        {
            return false;
        }

        if (paragraph.Inlines.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(paragraph.Text))
            {
                return false;
            }

            target.Add(new RunInline(paragraph.Text));
            return true;
        }

        foreach (var inline in paragraph.Inlines)
        {
            if (TryCloneHtmlInline(inline, out var clone))
            {
                target.Add(clone);
            }
        }

        return true;
    }

    private static bool TryCloneHtmlInline(Inline source, out Inline clone)
    {
        switch (source)
        {
            case RunInline run:
                clone = new RunInline(run.Text.GetText(), run.Style?.Clone())
                {
                    StyleId = run.StyleId,
                    Hyperlink = run.Hyperlink
                };
                return true;
            case ImageInline image:
                clone = CloneHtmlImage(image);
                return true;
        }

        clone = null!;
        return false;
    }

    private static ImageInline CloneHtmlImage(ImageInline source)
    {
        var data = new byte[source.Data.Length];
        Array.Copy(source.Data, data, data.Length);
        var clone = new ImageInline(data, source.Width, source.Height, source.ContentType)
        {
            Rotation = source.Rotation,
            Effects = source.Effects?.Clone(),
            Crop = source.Crop?.Clone()
        };
        return clone;
    }

    private static HtmlOptions CreateHtmlOptions(MarkdownOptions options)
    {
        _ = options;
        return new HtmlOptions
        {
            AllowScripts = false,
            AllowStyles = true,
            NormalizeLineEndings = true,
            PreserveUnknownElements = true
        };
    }

    private static void ApplyBlockQuoteIndentIfNeeded(ParagraphBlock paragraph, MarkdownToDocumentContext context)
    {
        if (context.BlockQuoteDepth <= 0)
        {
            return;
        }

        var depth = context.BlockQuoteDepth;
        if (string.Equals(paragraph.StyleId, MarkdownStyleIds.BlockQuote, StringComparison.OrdinalIgnoreCase))
        {
            depth = Math.Max(0, depth - 1);
        }

        if (depth == 0)
        {
            return;
        }

        var indent = MarkdownStyleDefaults.BlockQuoteIndentDips;
        paragraph.Properties.IndentLeft = (paragraph.Properties.IndentLeft ?? 0f) + indent * depth;
    }

    private static void ApplyCodeInlineStyle(TextStyleProperties properties)
    {
        properties.FontFamily = MarkdownStyleDefaults.CodeFontFamily;
        properties.FontSize = MarkdownStyleDefaults.PointsToDips(MarkdownStyleDefaults.CodeFontSizePoints);
    }

    private static void ApplyTableAlignment(MarkdownTableBlock table, ParagraphBlock paragraph, int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= table.Alignments.Count)
        {
            return;
        }

        var alignment = table.Alignments[cellIndex];
        paragraph.Properties.Alignment = alignment switch
        {
            MarkdownTableAlignment.Left => ParagraphAlignment.Left,
            MarkdownTableAlignment.Center => ParagraphAlignment.Center,
            MarkdownTableAlignment.Right => ParagraphAlignment.Right,
            _ => paragraph.Properties.Alignment
        };
    }
}
