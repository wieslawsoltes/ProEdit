using System.Globalization;
using Vibe.Office.Documents;

namespace Vibe.Office.Markdown;

public static class MarkdownDowngradePass
{
    public static MarkdownDowngradeResult Apply(
        Document document,
        MarkdownOptions? options = null,
        MarkdownConversionReport? report = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effectiveOptions = options ?? new MarkdownOptions();
        var downgrade = effectiveOptions.Downgrade;
        var conversionReport = report ?? new MarkdownConversionReport();

        var downgraded = new Document();
        downgraded.Blocks.Clear();
        AppendBlocks(downgraded.Blocks, document.Blocks, effectiveOptions, downgrade, conversionReport);
        if (downgraded.Blocks.Count == 0)
        {
            downgraded.Blocks.Add(new ParagraphBlock());
        }

        return new MarkdownDowngradeResult(downgraded, conversionReport);
    }

    private static void AppendBlocks(
        List<Block> target,
        IReadOnlyList<Block> blocks,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    target.Add(DowngradeParagraph(paragraph, options, downgrade, report));
                    break;
                case TableBlock table:
                    if (options.Flavor == MarkdownFlavor.GitHub && options.UseGfmTables)
                    {
                        target.Add(DowngradeTable(table, options, downgrade, report));
                    }
                    else
                    {
                        AppendUnsupportedBlock(target, MarkdownConversionFeature.UnsupportedBlock, "Table", options, downgrade, report);
                    }
                    break;
                case MetadataStartBlock metadataStart:
                    if (IsMarkdownMetadataBlock(metadataStart))
                    {
                        target.Add(metadataStart);
                    }
                    else
                    {
                        report.Add(MarkdownConversionFeature.UnsupportedBlock, MarkdownConversionAction.Dropped);
                    }
                    break;
                case MetadataEndBlock metadataEnd:
                    if (IsMarkdownMetadataBlock(metadataEnd))
                    {
                        target.Add(metadataEnd);
                    }
                    else
                    {
                        report.Add(MarkdownConversionFeature.UnsupportedBlock, MarkdownConversionAction.Dropped);
                    }
                    break;
                case PageBreakBlock:
                    AppendBreak(target, MarkdownConversionFeature.PageBreak, "Page Break", options, downgrade, report, downgrade.ConvertPageBreaksToThematicBreak);
                    break;
                case ColumnBreakBlock:
                    AppendBreak(target, MarkdownConversionFeature.ColumnBreak, "Column Break", options, downgrade, report, downgrade.ConvertColumnBreaksToThematicBreak);
                    break;
                case SectionBreakBlock:
                    AppendBreak(target, MarkdownConversionFeature.SectionBreak, "Section Break", options, downgrade, report, downgrade.ConvertSectionBreaksToThematicBreak);
                    break;
                case AltChunkBlock:
                    AppendUnsupportedBlock(target, MarkdownConversionFeature.AltChunk, "AltChunk", options, downgrade, report);
                    break;
                case ContentControlStartBlock:
                case ContentControlEndBlock:
                    report.Add(MarkdownConversionFeature.ContentControl, MarkdownConversionAction.Dropped);
                    break;
                case RevisionStartBlock:
                case RevisionEndBlock:
                    report.Add(MarkdownConversionFeature.Revision, MarkdownConversionAction.Dropped);
                    break;
                default:
                    AppendUnsupportedBlock(target, MarkdownConversionFeature.UnsupportedBlock, block.GetType().Name, options, downgrade, report);
                    break;
            }
        }
    }

    private static ParagraphBlock DowngradeParagraph(
        ParagraphBlock paragraph,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        var listInfo = paragraph.ListInfo?.Clone();
        var downgraded = new ParagraphBlock(paragraph.Text ?? string.Empty, listInfo)
        {
            StyleId = paragraph.StyleId
        };
        downgraded.Properties.Alignment = paragraph.Properties.Alignment;

        if (paragraph.Inlines.Count > 0)
        {
            downgraded.Text = string.Empty;
            foreach (var inline in paragraph.Inlines)
            {
                AppendInline(downgraded.Inlines, inline, options, downgrade, report);
            }
        }

        if (paragraph.FloatingObjects.Count > 0)
        {
            foreach (var floating in paragraph.FloatingObjects)
            {
                report.Add(MarkdownConversionFeature.FloatingObject, MarkdownConversionAction.Converted);
                AppendInline(downgraded.Inlines, floating.Content, options, downgrade, report);
            }
        }

        return downgraded;
    }

    private static TableBlock DowngradeTable(
        TableBlock table,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        var downgraded = new TableBlock();
        foreach (var row in table.Rows)
        {
            var newRow = new TableRow();
            foreach (var cell in row.Cells)
            {
                var newCell = new TableCell();
                AppendBlocks(newCell.Blocks, cell.Blocks, options, downgrade, report);
                if (newCell.Blocks.Count == 0)
                {
                    newCell.Blocks.Add(new ParagraphBlock());
                }

                newRow.Cells.Add(newCell);
            }

            downgraded.Rows.Add(newRow);
        }

        return downgraded;
    }

    private static void AppendInline(
        List<Inline> target,
        Inline inline,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        switch (inline)
        {
            case RunInline run:
                var copy = new RunInline(run.Text.GetText(), run.Style?.Clone())
                {
                    StyleId = run.StyleId,
                    Hyperlink = run.Hyperlink
                };
                target.Add(copy);
                break;
            case MetadataStartInline metadataStart:
                if (IsMarkdownMetadataInline(metadataStart))
                {
                    target.Add(metadataStart);
                }
                else
                {
                    report.Add(MarkdownConversionFeature.UnsupportedInline, MarkdownConversionAction.Dropped);
                }
                break;
            case MetadataEndInline metadataEnd:
                if (IsMarkdownMetadataInline(metadataEnd))
                {
                    target.Add(metadataEnd);
                }
                else
                {
                    report.Add(MarkdownConversionFeature.UnsupportedInline, MarkdownConversionAction.Dropped);
                }
                break;
            case ImageInline image:
                AppendImageInline(target, image, options, downgrade, report);
                break;
            case ContentControlStartInline:
            case ContentControlEndInline:
                report.Add(MarkdownConversionFeature.ContentControl, MarkdownConversionAction.Dropped);
                break;
            case RevisionStartInline:
            case RevisionEndInline:
            case RevisionRangeStartInline:
            case RevisionRangeEndInline:
                report.Add(MarkdownConversionFeature.Revision, MarkdownConversionAction.Dropped);
                break;
            case BookmarkStartInline:
            case BookmarkEndInline:
                report.Add(MarkdownConversionFeature.Bookmark, MarkdownConversionAction.Dropped);
                break;
            case CommentRangeStartInline:
            case CommentRangeEndInline:
                report.Add(MarkdownConversionFeature.Comment, MarkdownConversionAction.Dropped);
                break;
            case CommentReferenceInline comment:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Comment, $"Comment:{comment.Id}", options, downgrade, report);
                break;
            case FootnoteReferenceInline footnote:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Footnote, $"Footnote:{footnote.Id}", options, downgrade, report);
                break;
            case EndnoteReferenceInline endnote:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Endnote, $"Endnote:{endnote.Id}", options, downgrade, report);
                break;
            case FieldStartInline:
            case FieldSeparatorInline:
            case FieldEndInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Field, "Field", options, downgrade, report);
                break;
            case PageNumberInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.PageNumber, "PageNumber", options, downgrade, report);
                break;
            case TotalPagesInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.TotalPages, "TotalPages", options, downgrade, report);
                break;
            case TableInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.TableInline, "TableInline", options, downgrade, report);
                break;
            case ShapeInline shape:
                if (TryAppendShapeText(target, shape))
                {
                    report.Add(MarkdownConversionFeature.Shape, MarkdownConversionAction.Converted);
                    break;
                }

                AppendUnsupportedInline(target, MarkdownConversionFeature.Shape, "Shape", options, downgrade, report);
                break;
            case ChartInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Chart, "Chart", options, downgrade, report);
                break;
            case EquationInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.Equation, "Equation", options, downgrade, report);
                break;
            case RubyInline:
                AppendUnsupportedInline(target, MarkdownConversionFeature.UnsupportedInline, "Ruby", options, downgrade, report);
                break;
            default:
                AppendUnsupportedInline(target, MarkdownConversionFeature.UnsupportedInline, inline.GetType().Name, options, downgrade, report);
                break;
        }
    }

    private static void AppendImageInline(
        List<Inline> target,
        ImageInline image,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        if (TryBuildImageUrl(image, downgrade, out var url))
        {
            var attributes = new List<MetadataAttribute>
            {
                MarkdownMetadata.Attribute(MarkdownMetadata.AttrUrl, url)
            };
            var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.Image, attributes);
            target.Add(new MetadataStartInline(metadata));
            target.Add(new RunInline("Image"));
            target.Add(new MetadataEndInline(metadata));
            report.Add(MarkdownConversionFeature.Image, MarkdownConversionAction.Converted);
            return;
        }

        AppendUnsupportedInline(target, MarkdownConversionFeature.Image, "Image", options, downgrade, report);
    }

    private static void AppendBreak(
        List<Block> target,
        MarkdownConversionFeature feature,
        string label,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report,
        bool convertToThematicBreak)
    {
        if (convertToThematicBreak)
        {
            var metadata = MarkdownMetadata.CreateContainer(MarkdownMetadata.ThematicBreak);
            target.Add(new MetadataStartBlock(metadata));
            target.Add(new ParagraphBlock());
            target.Add(new MetadataEndBlock(metadata));
            report.Add(feature, MarkdownConversionAction.Converted);
            return;
        }

        AppendUnsupportedBlock(target, feature, label, options, downgrade, report);
    }

    private static void AppendUnsupportedBlock(
        List<Block> target,
        MarkdownConversionFeature feature,
        string label,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        var fallback = ResolveFallback(options, downgrade, isBlock: true);
        switch (fallback)
        {
            case MarkdownFallbackStrategy.Drop:
                report.Add(feature, MarkdownConversionAction.Dropped);
                return;
            case MarkdownFallbackStrategy.Html:
                AppendHtmlBlock(target, label, feature, report);
                return;
            default:
                var placeholder = BuildPlaceholder(label, downgrade);
                target.Add(new ParagraphBlock(placeholder));
                report.Add(feature, MarkdownConversionAction.Placeholder);
                return;
        }
    }

    private static void AppendUnsupportedInline(
        List<Inline> target,
        MarkdownConversionFeature feature,
        string label,
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        MarkdownConversionReport report)
    {
        var fallback = ResolveFallback(options, downgrade, isBlock: false);
        switch (fallback)
        {
            case MarkdownFallbackStrategy.Drop:
                report.Add(feature, MarkdownConversionAction.Dropped);
                return;
            case MarkdownFallbackStrategy.Html:
                AppendHtmlInline(target, label, feature, report);
                return;
            default:
                var placeholder = BuildPlaceholder(label, downgrade);
                target.Add(new RunInline(placeholder));
                report.Add(feature, MarkdownConversionAction.Placeholder);
                return;
        }
    }

    private static void AppendHtmlBlock(
        List<Block> target,
        string label,
        MarkdownConversionFeature feature,
        MarkdownConversionReport report)
    {
        var html = $"<!-- {label} -->";
        var metadata = MarkdownMetadata.CreateHtmlContainer(MarkdownMetadata.HtmlBlock, html);
        target.Add(new MetadataStartBlock(metadata));
        target.Add(new MetadataEndBlock(metadata));
        report.Add(feature, MarkdownConversionAction.Html);
    }

    private static void AppendHtmlInline(
        List<Inline> target,
        string label,
        MarkdownConversionFeature feature,
        MarkdownConversionReport report)
    {
        var html = $"<!-- {label} -->";
        var metadata = MarkdownMetadata.CreateHtmlContainer(MarkdownMetadata.HtmlInline, html);
        target.Add(new MetadataStartInline(metadata));
        target.Add(new MetadataEndInline(metadata));
        report.Add(feature, MarkdownConversionAction.Html);
    }

    private static MarkdownFallbackStrategy ResolveFallback(
        MarkdownOptions options,
        MarkdownDowngradeOptions downgrade,
        bool isBlock)
    {
        var fallback = downgrade.FallbackStrategy;
        if (fallback == MarkdownFallbackStrategy.Html)
        {
            if (isBlock && !options.AllowHtmlBlocks)
            {
                return MarkdownFallbackStrategy.Placeholder;
            }

            if (!isBlock && !options.AllowHtmlInlines)
            {
                return MarkdownFallbackStrategy.Placeholder;
            }
        }

        return fallback;
    }

    private static string BuildPlaceholder(string label, MarkdownDowngradeOptions options)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var format = options.PlaceholderFormat;
        if (string.IsNullOrWhiteSpace(format))
        {
            return $"[{label}]";
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, label);
        }
        catch (FormatException)
        {
            return $"[{label}]";
        }
    }

    private static bool TryBuildImageUrl(ImageInline image, MarkdownDowngradeOptions options, out string url)
    {
        if (options.EmbedImagesAsDataUri && image.Data.Length > 0)
        {
            var contentType = string.IsNullOrWhiteSpace(image.ContentType) ? "image/png" : image.ContentType;
            var base64 = Convert.ToBase64String(image.Data);
            url = string.Concat("data:", contentType, ";base64,", base64);
            return true;
        }

        url = string.Empty;
        return false;
    }

    private static bool IsMarkdownMetadataBlock(MetadataStartBlock metadataStart)
    {
        return MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.BlockQuote)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.CodeBlock)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.ThematicBreak)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.HtmlBlock);
    }

    private static bool IsMarkdownMetadataBlock(MetadataEndBlock metadataEnd)
    {
        return MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.BlockQuote)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.CodeBlock)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.ThematicBreak)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.HtmlBlock);
    }

    private static bool IsMarkdownMetadataInline(MetadataStartInline metadataStart)
    {
        return MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.CodeSpan)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.Image)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.TaskList)
               || MarkdownMetadata.IsMarkdownMetadata(metadataStart.Metadata, MarkdownMetadata.HtmlInline);
    }

    private static bool IsMarkdownMetadataInline(MetadataEndInline metadataEnd)
    {
        return MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.CodeSpan)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.Image)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.TaskList)
               || MarkdownMetadata.IsMarkdownMetadata(metadataEnd.Metadata, MarkdownMetadata.HtmlInline);
    }

    private static bool TryAppendShapeText(List<Inline> target, ShapeInline shape)
    {
        if (shape.TextBox is not { Blocks.Count: > 0 } textBox)
        {
            return false;
        }

        var text = ExtractTextFromBlocks(textBox.Blocks);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        target.Add(new RunInline(text));
        return true;
    }

    private static string ExtractTextFromBlocks(IReadOnlyList<Block> blocks)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < blocks.Count; index++)
        {
            if (index > 0 && builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendBlockText(builder, blocks[index]);
        }

        return builder.ToString().Trim();
    }

    private static void AppendBlockText(System.Text.StringBuilder builder, Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                AppendParagraphText(builder, paragraph);
                break;
            case TableBlock table:
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append(' ');
                        }

                        AppendCellText(builder, row.Cells[cellIndex]);
                    }
                }

                break;
        }
    }

    private static void AppendCellText(System.Text.StringBuilder builder, TableCell cell)
    {
        for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
        {
            if (blockIndex > 0 && builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendBlockText(builder, cell.Blocks[blockIndex]);
        }
    }

    private static void AppendParagraphText(System.Text.StringBuilder builder, ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(paragraph.Text))
            {
                builder.Append(paragraph.Text);
            }
        }
        else
        {
            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                AppendInlineText(builder, paragraph.Inlines[inlineIndex]);
            }
        }

        for (var floatingIndex = 0; floatingIndex < paragraph.FloatingObjects.Count; floatingIndex++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendInlineText(builder, paragraph.FloatingObjects[floatingIndex].Content);
        }
    }

    private static void AppendInlineText(System.Text.StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case RunInline run:
                builder.Append(run.Text.GetText());
                break;
            case ShapeInline shape when shape.TextBox is { Blocks.Count: > 0 }:
                builder.Append(ExtractTextFromBlocks(shape.TextBox.Blocks));
                break;
        }
    }
}
