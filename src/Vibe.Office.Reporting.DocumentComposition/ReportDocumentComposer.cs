using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Html;
using Vibe.Office.Markdown;
using Vibe.Office.OpenXml;
using Vibe.Office.Primitives;

namespace Vibe.Office.Reporting.DocumentComposition;

/// <summary>
/// Default implementation of <see cref="IReportDocumentComposer" />.
/// </summary>
public sealed class ReportDocumentComposer : IReportDocumentComposer
{
    private readonly record struct ComposedItemLayout(MaterializedReportItem Item, ReportItemBounds Bounds);

    /// <inheritdoc />
    public ValueTask<ReportDocumentCompositionResult> ComposeAsync(
        ReportDocumentCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ReportDocumentCompositionResult();
        try
        {
            var document = CreateEmptyDocument(request.MaterializedReport);
            ComposeSections(request.MaterializedReport, document, result.Diagnostics, cancellationToken);
            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new ParagraphBlock());
            }

            result.Document = document;
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentCompositionFailed,
                ex.Message,
                "$"));
        }

        return ValueTask.FromResult(result);
    }

    private static void ComposeSections(
        MaterializedReport report,
        Document document,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        for (var sectionIndex = 0; sectionIndex < report.Sections.Count; sectionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = report.Sections[sectionIndex];
            DocumentSection targetSection;

            if (sectionIndex == 0)
            {
                targetSection = document.Sections[0];
                ApplyPageSettings(section.PageSettings, targetSection.Properties);
                ComposeHeaderFooter(section.HeaderItems, document, targetSection.Header, diagnostics, cancellationToken, consumeContainerWhitespace: false);
                ComposeHeaderFooter(section.FooterItems, document, targetSection.Footer, diagnostics, cancellationToken, consumeContainerWhitespace: false);
                if (!string.IsNullOrWhiteSpace(section.Bookmark))
                {
                    document.Blocks.Add(CreateBookmarkParagraph(section.Bookmark));
                }

                ComposeItems(section.BodyItems, document, document.Blocks, diagnostics, cancellationToken, consumeContainerWhitespace: report.ConsumeContainerWhitespace);
                continue;
            }

            var sectionProperties = new SectionProperties();
            ApplyPageSettings(section.PageSettings, sectionProperties);
            targetSection = new DocumentSection(
                sectionProperties,
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter());

            document.Sections.Add(targetSection);
            document.Blocks.Add(new SectionBreakBlock
            {
                BreakType = SectionBreakType.NextPage,
                SectionIndex = document.Sections.Count - 1,
                Properties = sectionProperties.Clone()
            });

            ComposeHeaderFooter(section.HeaderItems, document, targetSection.Header, diagnostics, cancellationToken, consumeContainerWhitespace: false);
            ComposeHeaderFooter(section.FooterItems, document, targetSection.Footer, diagnostics, cancellationToken, consumeContainerWhitespace: false);
            if (!string.IsNullOrWhiteSpace(section.Bookmark))
            {
                document.Blocks.Add(CreateBookmarkParagraph(section.Bookmark));
            }

            ComposeItems(section.BodyItems, document, document.Blocks, diagnostics, cancellationToken, consumeContainerWhitespace: report.ConsumeContainerWhitespace);
        }
    }

    private static void ComposeHeaderFooter(
        IReadOnlyList<MaterializedReportItem> items,
        Document document,
        HeaderFooter headerFooter,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool consumeContainerWhitespace)
    {
        headerFooter.Blocks.Clear();
        ComposeItems(
            items,
            document,
            headerFooter.Blocks,
            diagnostics,
            cancellationToken,
            consumeContainerWhitespace: consumeContainerWhitespace,
            anchorToParagraphOrigin: true);
        headerFooter.IsDefined = headerFooter.Blocks.Count > 0;
    }

    private static void ComposeItems(
        IReadOnlyList<MaterializedReportItem> items,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        float offsetX = 0f,
        float offsetY = 0f,
        bool consumeContainerWhitespace = false,
        bool anchorToParagraphOrigin = false)
    {
        var layouts = NormalizeItemLayouts(items, offsetX, offsetY, consumeContainerWhitespace);
        ComposeNormalizedItems(
            layouts,
            document,
            targetBlocks,
            diagnostics,
            cancellationToken,
            consumeContainerWhitespace,
            anchorToParagraphOrigin);
    }

    private static void ComposeNormalizedItems(
        IReadOnlyList<ComposedItemLayout> layouts,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool consumeContainerWhitespace,
        bool anchorToParagraphOrigin)
    {
        ParagraphBlock? floatingAnchor = null;

        static ParagraphBlock EnsureFloatingAnchor(List<Block> blocks, ref ParagraphBlock? anchor)
        {
            anchor ??= new ParagraphBlock();
            if (!blocks.Contains(anchor))
            {
                blocks.Add(anchor);
            }

            return anchor;
        }

        for (var itemIndex = 0; itemIndex < layouts.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = layouts[itemIndex].Item;
            var localBounds = layouts[itemIndex].Bounds;
            if (ShouldInsertPageBreakBefore(item.PageBreak) && targetBlocks.Count > 0)
            {
                targetBlocks.Add(new PageBreakBlock());
                floatingAnchor = null;
            }

            switch (item)
            {
                case MaterializedTextReportItem textItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateTextShape(textItem, localBounds),
                        localBounds,
                        textItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedImageReportItem imageItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateImageInline(imageItem, diagnostics),
                        localBounds,
                        imageItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedLineReportItem lineItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateLineInline(lineItem),
                        localBounds,
                        lineItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedShapeReportItem shapeItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateShapeInline(shapeItem),
                        localBounds,
                        shapeItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedContainerReportItem containerItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateContainerInline(containerItem, localBounds, document, diagnostics, cancellationToken, consumeContainerWhitespace),
                        localBounds,
                        containerItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedChartReportItem chartItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateChartInline(chartItem),
                        localBounds,
                        chartItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedGaugeReportItem gaugeItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateGaugeInline(gaugeItem, localBounds),
                        localBounds,
                        gaugeItem.ZIndex,
                        anchorToParagraphOrigin);
                    break;
                case MaterializedTablixReportItem tablixItem:
                    if (!string.IsNullOrWhiteSpace(tablixItem.Bookmark))
                    {
                        targetBlocks.Add(CreateBookmarkParagraph(tablixItem.Bookmark));
                    }

                    var tables = CreateTables(tablixItem, diagnostics);
                    for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
                    {
                        if (tableIndex > 0)
                        {
                            targetBlocks.Add(new PageBreakBlock());
                            floatingAnchor = null;
                        }

                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor);
                        tables[tableIndex].Properties.FloatingAnchor = CreateFloatingAnchor(localBounds, tablixItem.ZIndex, anchorToParagraphOrigin);
                        targetBlocks.Add(tables[tableIndex]);
                    }

                    break;
                case MaterializedSubreportReportItem subreportItem:
                    ComposeSubreport(subreportItem, localBounds, document, targetBlocks, diagnostics, cancellationToken, anchorToParagraphOrigin);
                    break;
                case MaterializedDocumentTemplateReportItem templateItem:
                    ComposeDocumentTemplate(templateItem, document, targetBlocks, diagnostics);
                    break;
            }

            if (ShouldInsertPageBreakAfter(item.PageBreak))
            {
                targetBlocks.Add(new PageBreakBlock());
                floatingAnchor = null;
            }
        }
    }

    private static ParagraphBlock CreateTextParagraph(MaterializedTextReportItem item)
    {
        var paragraph = new ParagraphBlock(item.Text);
        AddBookmark(paragraph, item.Bookmark);
        ApplyParagraphAlignment(paragraph, item.Style);
        if (item.KeepTogether)
        {
            paragraph.Properties.KeepLinesTogether = true;
        }

        switch (item.ValueKind)
        {
            case MaterializedTextValueKind.PageNumber:
                paragraph.Inlines.Add(new PageNumberInline(CreateInlineStyle(item.Style)));
                break;
            case MaterializedTextValueKind.TotalPages:
                paragraph.Inlines.Add(new TotalPagesInline(CreateInlineStyle(item.Style)));
                break;
            default:
                paragraph.Inlines.Add(CreateStyledRun(item.Text, item.Style));
                break;
        }

        return paragraph;
    }

    private static List<ParagraphBlock> CreateTextParagraphs(MaterializedTextReportItem item)
    {
        if (item.Paragraphs.Count == 0)
        {
            return new List<ParagraphBlock> { CreateTextParagraph(item) };
        }

        var paragraphs = new List<ParagraphBlock>(item.Paragraphs.Count);
        for (var paragraphIndex = 0; paragraphIndex < item.Paragraphs.Count; paragraphIndex++)
        {
            var sourceParagraph = item.Paragraphs[paragraphIndex];
            var paragraph = new ParagraphBlock();
            if (paragraphIndex == 0)
            {
                AddBookmark(paragraph, item.Bookmark);
            }

            paragraph.Properties.Alignment = sourceParagraph.TextAlign ?? item.Style?.TextAlign;
            if (item.KeepTogether)
            {
                paragraph.Properties.KeepLinesTogether = true;
            }

            for (var runIndex = 0; runIndex < sourceParagraph.Runs.Count; runIndex++)
            {
                paragraph.Inlines.Add(CreateTextRunInline(sourceParagraph.Runs[runIndex]));
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private static Inline CreateTextRunInline(MaterializedTextRun run)
    {
        return run.ValueKind switch
        {
            MaterializedTextValueKind.PageNumber => new PageNumberInline(CreateInlineStyle(run.Style)),
            MaterializedTextValueKind.TotalPages => new TotalPagesInline(CreateInlineStyle(run.Style)),
            _ => CreateStyledRun(run.Text, run.Style)
        };
    }

    private static Inline CreateImageInline(
        MaterializedImageReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        if (item.Data is null || item.Data.Length == 0)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentCompositionFailed,
                $"Image item '{item.SourceItemId}' does not contain data.",
                "$.sections"));
            return CreateEmptyTextShapeInline(item.Bounds.Width, item.Bounds.Height);
        }

        var (width, height) = ResolveImageSize(item);
        return new ImageInline(
            item.Data,
            width,
            height,
            item.ContentType);
    }

    private static Inline CreateChartInline(MaterializedChartReportItem item)
    {
        return new ChartInline(
            Math.Max(1f, item.Bounds.Width),
            Math.Max(1f, item.Bounds.Height),
            item.Model,
            partData: null)
        {
            Name = item.Name
        };
    }

    private static Inline CreateLineInline(MaterializedLineReportItem item)
    {
        var width = Math.Max(1f, Math.Abs(item.X2 - item.Bounds.X));
        var height = Math.Max(1f, Math.Abs(item.Y2 - item.Bounds.Y));
        return new ShapeInline(
            width,
            height,
            new ShapeProperties
            {
                PresetGeometry = "line",
                Fill = new ShapeNoFill(),
                Outline = CreateBorderLine(ResolveBorder(item.Style, ReportBorderSide.Top)) ?? new BorderLine()
            },
            name: item.Name);
    }

    private static Inline CreateShapeInline(MaterializedShapeReportItem item)
    {
        var properties = new ShapeProperties
        {
            PresetGeometry = item.Shape switch
            {
                ReportShapeKind.RoundedRectangle => "roundRect",
                ReportShapeKind.Ellipse => "ellipse",
                _ => "rect"
            }
        };
        ApplyShapeStyle(properties, item.Style, showOutline: true);

        return new ShapeInline(
            Math.Max(1f, item.Bounds.Width),
            Math.Max(1f, item.Bounds.Height),
            properties,
            name: item.Name);
    }

    private static TableBlock CreateTable(
        MaterializedTablixReportItem item,
        IReadOnlyList<MaterializedTablixRow> rows,
        List<ReportDiagnostic> diagnostics)
    {
        var table = new TableBlock();
        table.Properties.LayoutMode = TableLayoutMode.Fixed;

        var totalWidth = 0f;
        for (var columnIndex = 0; columnIndex < item.Columns.Count; columnIndex++)
        {
            var width = Math.Max(1f, item.Columns[columnIndex].Width);
            table.Properties.ColumnWidths.Add(width);
            totalWidth += width;
        }

        if (totalWidth > 0f)
        {
            table.Properties.Width = totalWidth;
            table.Properties.WidthUnit = TableWidthUnit.Dxa;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var sourceRow = rows[rowIndex];
            var row = new TableRow();
            if (sourceRow.Height > 0f)
            {
                row.Properties.Height = sourceRow.Height;
                row.Properties.HeightRule = TableRowHeightRule.AtLeast;
            }

            if (item.KeepTogether)
            {
                row.Properties.CantSplit = true;
            }

            if (item.RepeatHeaderRows && sourceRow.IsHeader)
            {
                row.Properties.RepeatOnEachPage = true;
            }

            for (var cellIndex = 0; cellIndex < sourceRow.Cells.Count; cellIndex++)
            {
                var sourceCell = sourceRow.Cells[cellIndex];
                if (sourceCell.RowSpan > 1)
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        $"Row spans are not yet composed into the document model for tablix '{item.SourceItemId}'.",
                        "$.sections"));
                }

                var cell = new TableCell
                {
                    ColumnSpan = Math.Max(1, sourceCell.ColumnSpan)
                };
                ApplyCellStyle(sourceCell, cell.Properties);
                cell.Paragraphs.Add(CreateCellParagraph(sourceCell, diagnostics));
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static List<TableBlock> CreateTables(
        MaterializedTablixReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        var rowSegments = SplitTablixRows(item);
        var tables = new List<TableBlock>(rowSegments.Count);
        for (var index = 0; index < rowSegments.Count; index++)
        {
            tables.Add(CreateTable(item, rowSegments[index], diagnostics));
        }

        return tables;
    }

    private static List<IReadOnlyList<MaterializedTablixRow>> SplitTablixRows(MaterializedTablixReportItem item)
    {
        var headerRows = item.Rows.TakeWhile(static row => row.IsHeader).ToList();
        var segments = new List<IReadOnlyList<MaterializedTablixRow>>();
        var current = new List<MaterializedTablixRow>();

        for (var rowIndex = 0; rowIndex < item.Rows.Count; rowIndex++)
        {
            var row = item.Rows[rowIndex];
            if (row.PageBreakBefore && current.Exists(static existing => !existing.IsHeader))
            {
                segments.Add(current);
                current = new List<MaterializedTablixRow>();
                if (item.RepeatHeaderRows && headerRows.Count > 0)
                {
                    current.AddRange(headerRows);
                }
            }

            current.Add(row);

            if (row.PageBreakAfter && rowIndex < item.Rows.Count - 1)
            {
                segments.Add(current);
                current = new List<MaterializedTablixRow>();
                if (item.RepeatHeaderRows && headerRows.Count > 0)
                {
                    current.AddRange(headerRows);
                }
            }
        }

        if (current.Count > 0)
        {
            segments.Add(current);
        }

        if (segments.Count == 0)
        {
            segments.Add(item.Rows);
        }

        return segments;
    }

    private static List<ComposedItemLayout> NormalizeItemLayouts(
        IReadOnlyList<MaterializedReportItem> items,
        float offsetX,
        float offsetY,
        bool consumeContainerWhitespace)
    {
        var layouts = new List<ComposedItemLayout>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var localBounds = TranslateBounds(item.Bounds, offsetX, offsetY);
            localBounds = AdjustBoundsForContentGrowth(item, localBounds, consumeContainerWhitespace);
            localBounds = ApplyGrowthReflow(layouts, item, localBounds, offsetX, offsetY);
            layouts.Add(new ComposedItemLayout(item, localBounds));
        }

        return layouts;
    }

    private static ReportItemBounds AdjustBoundsForContentGrowth(
        MaterializedReportItem item,
        ReportItemBounds bounds,
        bool consumeContainerWhitespace)
    {
        return item switch
        {
            MaterializedTextReportItem textItem => bounds with { Height = EstimateTextHeight(textItem, bounds) },
            MaterializedContainerReportItem containerItem => bounds with { Height = EstimateContainerHeight(containerItem, bounds, consumeContainerWhitespace) },
            MaterializedTablixReportItem tablixItem => bounds with { Height = EstimateTablixHeight(tablixItem, bounds) },
            MaterializedSubreportReportItem subreportItem => bounds with { Height = EstimateSubreportHeight(subreportItem, bounds) },
            _ => bounds
        };
    }

    private static float EstimateTextHeight(
        MaterializedTextReportItem item,
        ReportItemBounds bounds)
    {
        if (!item.CanGrow && !item.CanShrink)
        {
            return bounds.Height;
        }

        var style = item.Style;
        var padding = ResolvePadding(style, 0f);
        var availableWidth = Math.Max(1f, bounds.Width - padding.Left - padding.Right);
        var fontSize = Math.Max(8f, style?.FontSize ?? 10f);
        var lineHeight = Math.Max(fontSize * 1.2f, fontSize + 1f);
        var lineCount = EstimateWrappedLineCount(GetEstimatedTextContent(item), availableWidth, fontSize);
        var desiredHeight = padding.Top + padding.Bottom + (lineCount * lineHeight);

        if (item.CanGrow && desiredHeight > bounds.Height)
        {
            return desiredHeight;
        }

        if (item.CanShrink && desiredHeight < bounds.Height)
        {
            return Math.Max(lineHeight + padding.Top + padding.Bottom, desiredHeight);
        }

        return bounds.Height;
    }

    private static float EstimateContainerHeight(
        MaterializedContainerReportItem item,
        ReportItemBounds bounds,
        bool consumeContainerWhitespace)
    {
        if (item.Items.Count == 0)
        {
            return bounds.Height;
        }

        var childLayouts = NormalizeItemLayouts(item.Items, item.Bounds.X, item.Bounds.Y, consumeContainerWhitespace);
        var originalBottom = 0f;
        for (var index = 0; index < item.Items.Count; index++)
        {
            var child = item.Items[index];
            var bottom = child.Bounds.Y - item.Bounds.Y + child.Bounds.Height;
            if (bottom > originalBottom)
            {
                originalBottom = bottom;
            }
        }

        var adjustedBottom = 0f;
        for (var index = 0; index < childLayouts.Count; index++)
        {
            var bottom = childLayouts[index].Bounds.Y + childLayouts[index].Bounds.Height;
            if (bottom > adjustedBottom)
            {
                adjustedBottom = bottom;
            }
        }

        if (adjustedBottom <= originalBottom)
        {
            return bounds.Height;
        }

        if (consumeContainerWhitespace)
        {
            return Math.Max(bounds.Height, adjustedBottom);
        }

        return bounds.Height + (adjustedBottom - originalBottom);
    }

    private static float EstimateSubreportHeight(
        MaterializedSubreportReportItem item,
        ReportItemBounds bounds)
    {
        if (item.Report is null || item.Report.Sections.Count == 0)
        {
            return bounds.Height;
        }

        var section = item.Report.Sections[0];
        if (section.BodyItems.Count == 0)
        {
            return bounds.Height;
        }

        var layouts = NormalizeItemLayouts(
            section.BodyItems,
            offsetX: 0f,
            offsetY: 0f,
            consumeContainerWhitespace: item.Report.ConsumeContainerWhitespace);

        var maxBottom = 0f;
        for (var index = 0; index < layouts.Count; index++)
        {
            var bottom = layouts[index].Bounds.Y + layouts[index].Bounds.Height;
            if (bottom > maxBottom)
            {
                maxBottom = bottom;
            }
        }

        return Math.Max(bounds.Height, maxBottom);
    }

    private static float EstimateTablixHeight(
        MaterializedTablixReportItem item,
        ReportItemBounds bounds)
    {
        if (item.Rows.Count == 0)
        {
            return bounds.Height;
        }

        var height = 0f;
        for (var index = 0; index < item.Rows.Count; index++)
        {
            height += Math.Max(1f, item.Rows[index].Height);
        }

        return Math.Max(bounds.Height, height);
    }

    private static ReportItemBounds ApplyGrowthReflow(
        IReadOnlyList<ComposedItemLayout> existingLayouts,
        MaterializedReportItem currentItem,
        ReportItemBounds currentBounds,
        float offsetX,
        float offsetY)
    {
        if (existingLayouts.Count == 0)
        {
            return currentBounds;
        }

        var adjustedBounds = currentBounds;
        var currentOriginal = TranslateBounds(currentItem.Bounds, offsetX, offsetY);
        for (var index = 0; index < existingLayouts.Count; index++)
        {
            var previousLayout = existingLayouts[index];
            var previousOriginal = TranslateBounds(previousLayout.Item.Bounds, offsetX, offsetY);
            if (currentOriginal.Y + 0.01f < previousOriginal.Y)
            {
                continue;
            }

            if (!ShouldReflowFollowingItems(previousLayout.Item))
            {
                continue;
            }

            var overlapWidth = ComputeOverlapWidth(previousOriginal, currentOriginal);
            if (overlapWidth <= 0f)
            {
                continue;
            }

            var previousGrowth = (previousLayout.Bounds.Y + previousLayout.Bounds.Height)
                - (previousOriginal.Y + previousOriginal.Height);
            if (previousGrowth <= 0.01f)
            {
                continue;
            }

            var originalGap = Math.Max(0f, currentOriginal.Y - (previousOriginal.Y + previousOriginal.Height));
            var requiredY = previousLayout.Bounds.Y + previousLayout.Bounds.Height + originalGap;
            if (adjustedBounds.Y < requiredY)
            {
                adjustedBounds = adjustedBounds with { Y = requiredY };
            }
        }

        return adjustedBounds;
    }

    private static bool ShouldReflowFollowingItems(MaterializedReportItem item)
    {
        return item is MaterializedContainerReportItem
            or MaterializedTablixReportItem
            or MaterializedSubreportReportItem
            or MaterializedDocumentTemplateReportItem;
    }

    private static float ComputeOverlapWidth(ReportItemBounds left, ReportItemBounds right)
    {
        var start = Math.Max(left.X, right.X);
        var end = Math.Min(left.X + left.Width, right.X + right.Width);
        return Math.Max(0f, end - start);
    }

    private static string GetEstimatedTextContent(MaterializedTextReportItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return item.Text;
        }

        return item.ValueKind switch
        {
            MaterializedTextValueKind.PageNumber => "999",
            MaterializedTextValueKind.TotalPages => "999",
            _ => string.Empty
        };
    }

    private static int EstimateWrappedLineCount(string text, float availableWidth, float fontSize)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var averageCharacterWidth = Math.Max(1f, fontSize * 0.47f);
        var maxCharsPerLine = Math.Max(1, (int)MathF.Floor(availableWidth / averageCharacterWidth));
        var total = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            total += EstimateWrappedLineCount(lines[index].AsSpan(), maxCharsPerLine);
        }

        return Math.Max(1, total);
    }

    private static int EstimateWrappedLineCount(ReadOnlySpan<char> text, int maxCharsPerLine)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        var lineCount = 1;
        var current = 0;
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                if (current == 0)
                {
                    index++;
                    continue;
                }

                if (current + 1 > maxCharsPerLine)
                {
                    lineCount++;
                    current = 0;
                }
                else
                {
                    current++;
                }

                index++;
            }

            var wordStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var wordLength = index - wordStart;
            if (wordLength <= 0)
            {
                continue;
            }

            if (current > 0)
            {
                if (current + wordLength > maxCharsPerLine)
                {
                    lineCount++;
                    current = 0;
                }
            }

            while (wordLength > 0)
            {
                var remaining = maxCharsPerLine - current;
                if (remaining <= 0)
                {
                    lineCount++;
                    current = 0;
                    remaining = maxCharsPerLine;
                }

                if (wordLength <= remaining)
                {
                    current += wordLength;
                    wordLength = 0;
                }
                else
                {
                    current += remaining;
                    wordLength -= remaining;
                    if (wordLength > 0)
                    {
                        lineCount++;
                        current = 0;
                    }
                }
            }
        }

        return lineCount;
    }

    private static ParagraphBlock CreateCellParagraph(
        MaterializedTablixCell cell,
        List<ReportDiagnostic> diagnostics)
    {
        if (cell.Content is not null)
        {
            return CreateCellContentParagraph(cell, diagnostics);
        }

        var paragraph = new ParagraphBlock(cell.Text);
        if (cell.Style?.TextAlign.HasValue == true)
        {
            paragraph.Properties.Alignment = cell.Style.TextAlign.Value;
        }

        paragraph.Inlines.Add(CreateStyledRun(cell.Text, cell.Style));
        return paragraph;
    }

    private static ParagraphBlock CreateCellContentParagraph(
        MaterializedTablixCell cell,
        List<ReportDiagnostic> diagnostics)
    {
        return cell.Content switch
        {
            MaterializedTextReportItem textItem => CreateTextParagraph(textItem),
            MaterializedImageReportItem imageItem => CreateInlineParagraph(CreateImageInline(imageItem, new List<ReportDiagnostic>())),
            MaterializedChartReportItem chartItem => CreateInlineParagraph(CreateChartInline(chartItem)),
            MaterializedGaugeReportItem gaugeItem => CreateInlineParagraph(
                CreateGaugeInline(
                    gaugeItem,
                    GetPreferredEmbeddedBounds(gaugeItem, consumeContainerWhitespace: false))),
            MaterializedShapeReportItem shapeItem => CreateInlineParagraph(CreateShapeInline(shapeItem)),
            MaterializedContainerReportItem containerItem => CreateContainerCellParagraph(containerItem, diagnostics),
            _ => new ParagraphBlock(cell.Text)
        };
    }

    private static void ApplyCellStyle(
        MaterializedTablixCell source,
        TableCellProperties target)
    {
        if (ReportDocumentColorParser.TryParse(source.Style?.Background, out var background))
        {
            target.ShadingColor = background;
        }

        var defaultPadding = source.Content is null ? 4f : 0f;
        target.Padding = ResolvePadding(source.Style, defaultPadding);
        target.VerticalAlignment = source.Style?.VerticalAlign switch
        {
            ReportVerticalAlignment.Middle => TableCellVerticalAlignment.Center,
            ReportVerticalAlignment.Bottom => TableCellVerticalAlignment.Bottom,
            ReportVerticalAlignment.Top => TableCellVerticalAlignment.Top,
            _ => null
        };
        target.Borders.Top = CreateBorderLine(ResolveBorder(source.Style, ReportBorderSide.Top));
        target.Borders.Bottom = CreateBorderLine(ResolveBorder(source.Style, ReportBorderSide.Bottom));
        target.Borders.Left = CreateBorderLine(ResolveBorder(source.Style, ReportBorderSide.Left));
        target.Borders.Right = CreateBorderLine(ResolveBorder(source.Style, ReportBorderSide.Right));
    }

    private static void ApplyParagraphAlignment(ParagraphBlock paragraph, MaterializedReportStyle? style)
    {
        if (style?.TextAlign.HasValue == true)
        {
            paragraph.Properties.Alignment = style.TextAlign.Value;
        }
    }

    private static void ApplyTextBoxStyle(
        ShapeTextBoxProperties properties,
        MaterializedReportStyle? style,
        float fallbackPadding)
    {
        properties.Padding = ResolvePadding(style, fallbackPadding);
        properties.VerticalAlignment = style?.VerticalAlign switch
        {
            ReportVerticalAlignment.Middle => ShapeTextVerticalAlignment.Center,
            ReportVerticalAlignment.Bottom => ShapeTextVerticalAlignment.Bottom,
            _ => ShapeTextVerticalAlignment.Top
        };
    }

    private static void ApplyTextBoxParagraphBorders(ParagraphBlock paragraph, MaterializedReportStyle? style)
    {
        var top = CreateBorderLine(ResolveBorder(style, ReportBorderSide.Top));
        var bottom = CreateBorderLine(ResolveBorder(style, ReportBorderSide.Bottom));
        var left = CreateBorderLine(ResolveBorder(style, ReportBorderSide.Left));
        var right = CreateBorderLine(ResolveBorder(style, ReportBorderSide.Right));
        if (top is null && bottom is null && left is null && right is null)
        {
            return;
        }

        paragraph.Properties.Borders.Top = top;
        paragraph.Properties.Borders.Bottom = bottom;
        paragraph.Properties.Borders.Left = left;
        paragraph.Properties.Borders.Right = right;
    }

    private static DocThickness ResolvePadding(MaterializedReportStyle? style, float fallback)
    {
        if (style is null)
        {
            return DocThickness.Uniform(fallback);
        }

        if (!style.PaddingLeft.HasValue
            && !style.PaddingRight.HasValue
            && !style.PaddingTop.HasValue
            && !style.PaddingBottom.HasValue)
        {
            return DocThickness.Uniform(fallback);
        }

        return new DocThickness(
            style.PaddingLeft ?? fallback,
            style.PaddingTop ?? fallback,
            style.PaddingRight ?? fallback,
            style.PaddingBottom ?? fallback);
    }

    private static MaterializedReportBorder? ResolveBorder(
        MaterializedReportStyle? style,
        ReportBorderSide side)
    {
        if (style is null)
        {
            return null;
        }

        return side switch
        {
            ReportBorderSide.Top => style.TopBorder ?? style.Border,
            ReportBorderSide.Bottom => style.BottomBorder ?? style.Border,
            ReportBorderSide.Left => style.LeftBorder ?? style.Border,
            ReportBorderSide.Right => style.RightBorder ?? style.Border,
            _ => style.Border
        };
    }

    private static BorderLine? TryCreateUniformOutline(MaterializedReportStyle? style)
    {
        var top = ResolveBorder(style, ReportBorderSide.Top);
        var bottom = ResolveBorder(style, ReportBorderSide.Bottom);
        var left = ResolveBorder(style, ReportBorderSide.Left);
        var right = ResolveBorder(style, ReportBorderSide.Right);

        if (!BordersEquivalent(top, bottom) || !BordersEquivalent(top, left) || !BordersEquivalent(top, right))
        {
            return null;
        }

        return CreateBorderLine(top);
    }

    private static bool BordersEquivalent(MaterializedReportBorder? left, MaterializedReportBorder? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Color, right.Color, StringComparison.OrdinalIgnoreCase)
            && left.Style == right.Style
            && Nullable.Equals(left.Width, right.Width);
    }

    private static BorderLine? CreateBorderLine(MaterializedReportBorder? border)
    {
        if (border is null)
        {
            return null;
        }

        var style = border.Style ?? ReportBorderLineStyle.Solid;
        var line = new BorderLine
        {
            Style = style switch
            {
                ReportBorderLineStyle.None => DocBorderStyle.None,
                ReportBorderLineStyle.Dashed => DocBorderStyle.Dashed,
                ReportBorderLineStyle.Dotted => DocBorderStyle.Dotted,
                ReportBorderLineStyle.Double => DocBorderStyle.Double,
                _ => DocBorderStyle.Single
            },
            Thickness = border.Width ?? 1f
        };

        if (ReportDocumentColorParser.TryParse(border.Color, out var color))
        {
            line.Color = color;
        }

        if (line.Style == DocBorderStyle.None)
        {
            line.Thickness = 0f;
        }

        return line;
    }

    private static void ComposeSubreport(
        MaterializedSubreportReportItem item,
        ReportItemBounds bounds,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool anchorToParagraphOrigin)
    {
        if (item.Report is null)
        {
            return;
        }

        for (var sectionIndex = 0; sectionIndex < item.Report.Sections.Count; sectionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = item.Report.Sections[sectionIndex];
            if (sectionIndex > 0)
            {
                targetBlocks.Add(new PageBreakBlock());
            }

            ComposeItems(
                section.BodyItems,
                document,
                targetBlocks,
                diagnostics,
                cancellationToken,
                offsetX: -bounds.X,
                offsetY: sectionIndex == 0 ? -bounds.Y : 0f,
                consumeContainerWhitespace: item.Report.ConsumeContainerWhitespace,
                anchorToParagraphOrigin: anchorToParagraphOrigin);
        }
    }

    private static void ComposeDocumentTemplate(
        MaterializedDocumentTemplateReportItem item,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                $"Template item '{item.SourceItemId}' does not contain content.",
                "$.sections"));
            return;
        }

        try
        {
            var templateDocument = LoadTemplateDocument(item);
            ReportTemplateDocumentBinder.Bind(
                templateDocument,
                item.Bindings,
                $"vibeoffice-report-bindings-{Guid.NewGuid():N}");
            if (!string.IsNullOrWhiteSpace(item.Bookmark))
            {
                targetBlocks.Add(CreateBookmarkParagraph(item.Bookmark));
            }

            ReportDocumentFragmentImporter.ImportBlocks(templateDocument, document, targetBlocks);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                ex.Message,
                "$.sections"));
        }
    }

    private static Document LoadTemplateDocument(MaterializedDocumentTemplateReportItem item)
    {
        var content = item.Content ?? string.Empty;
        return item.TemplateFormat switch
        {
            ReportDocumentTemplateFormat.Html => HtmlDocumentConverter.FromHtml(content.AsSpan()),
            ReportDocumentTemplateFormat.Markdown => MarkdownDocumentConverter.FromMarkdown(content.AsSpan()),
            ReportDocumentTemplateFormat.Docx => new DocxImporter().Load(new MemoryStream(Convert.FromBase64String(content))),
            _ => DocumentPlainTextParser.FromPlainText(content)
        };
    }

    private static ParagraphBlock CreateBookmarkParagraph(string bookmark)
    {
        var paragraph = new ParagraphBlock();
        AddBookmark(paragraph, bookmark);
        return paragraph;
    }

    private static void AddBookmark(
        ParagraphBlock paragraph,
        string? bookmark)
    {
        if (string.IsNullOrWhiteSpace(bookmark))
        {
            return;
        }

        var bookmarkId = Math.Abs(bookmark.GetHashCode(StringComparison.Ordinal)) + 1;
        paragraph.Inlines.Add(new BookmarkStartInline(bookmarkId, bookmark));
        paragraph.Inlines.Add(new BookmarkEndInline(bookmarkId));
    }

    private static RunInline CreateStyledRun(
        string text,
        MaterializedReportStyle? style)
    {
        var runStyle = new TextStyleProperties();
        if (style is not null)
        {
            if (!string.IsNullOrWhiteSpace(style.FontFamily))
            {
                runStyle.FontFamily = style.FontFamily;
            }

            if (style.FontSize.HasValue)
            {
                runStyle.FontSize = style.FontSize.Value;
            }

            if (style.Bold == true)
            {
                runStyle.FontWeight = DocFontWeight.Bold;
            }

            if (style.Italic == true)
            {
                runStyle.FontStyle = DocFontStyle.Italic;
            }

            if (style.TextDecoration == ReportTextDecoration.Underline)
            {
                runStyle.Underline = true;
            }

            if (style.TextDecoration == ReportTextDecoration.LineThrough)
            {
                runStyle.Strikethrough = true;
            }

            if (ReportDocumentColorParser.TryParse(style.Foreground, out var foreground))
            {
                runStyle.Color = foreground;
            }

            if (ReportDocumentColorParser.TryParse(style.Background, out var highlight))
            {
                runStyle.HighlightColor = highlight;
            }
        }

        return runStyle.HasValues
            ? new RunInline(text, runStyle)
            : new RunInline(text);
    }

    private static void AddFloatingObject(
        ParagraphBlock anchorParagraph,
        Inline content,
        ReportItemBounds bounds,
        int zIndex,
        bool anchorToParagraphOrigin)
    {
        var floating = new FloatingObject(content);
        CopyAnchor(CreateFloatingAnchor(bounds, zIndex, anchorToParagraphOrigin), floating.Anchor);
        anchorParagraph.FloatingObjects.Add(floating);
    }

    private static ShapeInline CreateTextShape(MaterializedTextReportItem item, ReportItemBounds bounds)
    {
        var shape = CreateEmptyTextShapeInline(bounds.Width, bounds.Height);
        ApplyShapeStyle(shape.Properties, item.Style, showOutline: false);
        shape.Name = item.Name;
        ApplyTextBoxStyle(shape.TextBox!.Properties, item.Style, 0f);
        shape.TextBox.Properties.AutoFit = item.CanShrink ? ShapeTextAutoFit.TextToFitShape : ShapeTextAutoFit.None;
        shape.TextBox.Properties.HorizontalOverflow = ShapeTextOverflow.Clip;
        shape.TextBox.Properties.VerticalOverflow = ShapeTextOverflow.Clip;
        var paragraphs = CreateTextParagraphs(item);
        if (TryCreateUniformOutline(item.Style) is null)
        {
            for (var index = 0; index < paragraphs.Count; index++)
            {
                ApplyTextBoxParagraphBorders(paragraphs[index], item.Style);
            }
        }

        for (var index = 0; index < paragraphs.Count; index++)
        {
            shape.TextBox.Blocks.Add(paragraphs[index]);
        }
        return shape;
    }

    private static ShapeInline CreateGaugeInline(MaterializedGaugeReportItem item, ReportItemBounds bounds)
    {
        var shape = CreateEmptyTextShapeInline(bounds.Width, bounds.Height);
        shape.Name = item.Name;
        ApplyGaugeStyle(shape.Properties, item);
        shape.TextBox!.Properties.HorizontalOverflow = ShapeTextOverflow.Clip;
        shape.TextBox!.Properties.VerticalOverflow = ShapeTextOverflow.Clip;

        var text = item.Label;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = item.GaugeKind == ReportGaugeKind.StateIndicator
                ? FormatGaugeState(item.Value)
                : FormatGaugeValue(item);
        }
        else if (item.GaugeKind != ReportGaugeKind.StateIndicator)
        {
            text = string.Concat(text, "\n", FormatGaugeValue(item));
        }

        var paragraph = new ParagraphBlock(text ?? string.Empty);
        ApplyParagraphAlignment(paragraph, item.Style);
        paragraph.Inlines.Add(CreateStyledRun(text ?? string.Empty, item.Style));
        ApplyTextBoxStyle(shape.TextBox!.Properties, item.Style, 0f);
        if (TryCreateUniformOutline(item.Style) is null)
        {
            ApplyTextBoxParagraphBorders(paragraph, item.Style);
        }

        shape.TextBox!.Blocks.Add(paragraph);
        return shape;
    }

    private static ShapeInline CreateContainerInline(
        MaterializedContainerReportItem item,
        ReportItemBounds bounds,
        Document document,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool consumeContainerWhitespace)
    {
        var shape = CreateEmptyTextShapeInline(bounds.Width, bounds.Height);
        shape.Name = item.Name;
        shape.TextBox!.Properties.Padding = DocThickness.Uniform(0f);
        ApplyTextBoxStyle(shape.TextBox.Properties, item.Style, 0f);
        shape.TextBox.Properties.HorizontalOverflow = ShapeTextOverflow.Overflow;
        shape.TextBox.Properties.VerticalOverflow = ShapeTextOverflow.Overflow;
        ApplyShapeStyle(shape.Properties, item.Style, showOutline: false);
        var childLayouts = NormalizeItemLayouts(item.Items, item.Bounds.X, item.Bounds.Y, consumeContainerWhitespace);
        ComposeNormalizedItems(
            childLayouts,
            document,
            shape.TextBox.Blocks,
            diagnostics,
            cancellationToken,
            consumeContainerWhitespace,
            anchorToParagraphOrigin: true);
        return shape;
    }

    private static ParagraphBlock CreateContainerCellParagraph(
        MaterializedContainerReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(CreateContainerInline(
            item,
            GetPreferredEmbeddedBounds(item, consumeContainerWhitespace: false),
            CreateEmptyDocument(),
            diagnostics,
            CancellationToken.None,
            consumeContainerWhitespace: false));
        return paragraph;
    }

    private static ReportItemBounds GetPreferredEmbeddedBounds(
        MaterializedReportItem item,
        bool consumeContainerWhitespace)
    {
        return AdjustBoundsForContentGrowth(item, item.Bounds, consumeContainerWhitespace);
    }

    private static ReportItemBounds TranslateBounds(ReportItemBounds bounds, float offsetX, float offsetY)
    {
        if (offsetX == 0f && offsetY == 0f)
        {
            return bounds;
        }

        return bounds with
        {
            X = bounds.X - offsetX,
            Y = bounds.Y - offsetY
        };
    }

    private static ParagraphBlock CreateInlineParagraph(Inline inline)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(inline);
        return paragraph;
    }

    private static ShapeInline CreateEmptyTextShapeInline(float width, float height)
    {
        var textBox = new ShapeTextBox();
        textBox.Properties.Padding = DocThickness.Uniform(4f);
        return new ShapeInline(
            Math.Max(1f, width),
            Math.Max(1f, height),
            new ShapeProperties
            {
                PresetGeometry = "rect",
                Fill = new ShapeNoFill(),
                Outline = new BorderLine
                {
                    Style = DocBorderStyle.None,
                    Thickness = 0f
                }
            },
            textBox);
    }

    private static void ApplyShapeStyle(
        ShapeProperties properties,
        MaterializedReportStyle? style,
        bool showOutline)
    {
        properties.Fill = CreateShapeFill(style);

        var outline = TryCreateUniformOutline(style);
        properties.Outline = outline ?? (showOutline
            ? new BorderLine()
            : new BorderLine
            {
                Style = DocBorderStyle.None,
                Thickness = 0f
            });
    }

    private static ShapeFill CreateShapeFill(MaterializedReportStyle? style)
    {
        if (style?.BackgroundGradientType is { } gradientType
            && gradientType != ReportBackgroundGradientType.None
            && ReportDocumentColorParser.TryParse(style.Background, out var startColor)
            && ReportDocumentColorParser.TryParse(style.BackgroundGradientEndColor, out var endColor))
        {
            var gradientFill = new ShapeGradientFill
            {
                Type = gradientType == ReportBackgroundGradientType.Center
                    ? ShapeGradientType.Radial
                    : ShapeGradientType.Linear,
                Angle = gradientType switch
                {
                    ReportBackgroundGradientType.TopBottom => 90f,
                    ReportBackgroundGradientType.DiagonalLeft => 45f,
                    ReportBackgroundGradientType.DiagonalRight => 135f,
                    _ => 0f
                }
            };

            switch (gradientType)
            {
                case ReportBackgroundGradientType.HorizontalCenter:
                case ReportBackgroundGradientType.VerticalCenter:
                    gradientFill.Stops.Add(new ShapeGradientStop(0f, endColor));
                    gradientFill.Stops.Add(new ShapeGradientStop(0.5f, startColor));
                    gradientFill.Stops.Add(new ShapeGradientStop(1f, endColor));
                    if (gradientType == ReportBackgroundGradientType.VerticalCenter)
                    {
                        gradientFill.Angle = 90f;
                    }

                    break;

                default:
                    gradientFill.Stops.Add(new ShapeGradientStop(0f, startColor));
                    gradientFill.Stops.Add(new ShapeGradientStop(1f, endColor));
                    break;
            }

            return gradientFill;
        }

        if (ReportDocumentColorParser.TryParse(style?.Background, out var fillColor))
        {
            return new ShapeSolidFill(fillColor);
        }

        return new ShapeNoFill();
    }

    private static void ApplyGaugeStyle(
        ShapeProperties properties,
        MaterializedGaugeReportItem item)
    {
        var fillColor = item.GaugeKind switch
        {
            ReportGaugeKind.StateIndicator => ResolveGaugeStateColor(item.Value),
            _ when item.TargetValue.HasValue && item.Value.HasValue && item.Value.Value >= item.TargetValue.Value
                => new DocColor(98, 162, 69),
            _ when item.TargetValue.HasValue && item.Value.HasValue
                => new DocColor(192, 67, 58),
            _ => new DocColor(68, 67, 67)
        };

        properties.Fill = new ShapeSolidFill(fillColor);
        properties.Outline = new BorderLine
        {
            Color = fillColor
        };
    }

    private static string FormatGaugeState(double? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value switch
        {
            >= 1d => "OK",
            >= 0d => "Warning",
            _ => "Alert"
        };
    }

    private static string FormatGaugeValue(MaterializedGaugeReportItem item)
    {
        if (!item.Value.HasValue)
        {
            return string.Empty;
        }

        if (item.Maximum.HasValue)
        {
            return $"{item.Value.Value:0.##} / {item.Maximum.Value:0.##}";
        }

        if (item.TargetValue.HasValue)
        {
            return $"{item.Value.Value:0.##} ({item.TargetValue.Value:0.##})";
        }

        return item.Value.Value.ToString("0.##");
    }

    private static DocColor ResolveGaugeStateColor(double? value)
    {
        if (!value.HasValue)
        {
            return new DocColor(120, 120, 120);
        }

        return value.Value switch
        {
            >= 1d => new DocColor(98, 162, 69),
            >= 0d => new DocColor(232, 214, 46),
            _ => new DocColor(192, 67, 58)
        };
    }

    private static FloatingAnchor CreateFloatingAnchor(ReportItemBounds bounds, int zIndex, bool anchorToParagraphOrigin = false)
    {
        return new FloatingAnchor
        {
            HorizontalReference = anchorToParagraphOrigin
                ? FloatingHorizontalReference.Paragraph
                : FloatingHorizontalReference.Margin,
            VerticalReference = anchorToParagraphOrigin
                ? FloatingVerticalReference.Paragraph
                : FloatingVerticalReference.Margin,
            OffsetX = bounds.X,
            OffsetY = bounds.Y,
            WrapStyle = FloatingWrapStyle.None,
            WrapSide = FloatingWrapSide.Both,
            AllowOverlap = true,
            ZOrder = zIndex < 0 ? 0u : (uint)zIndex
        };
    }

    private static void CopyAnchor(FloatingAnchor source, FloatingAnchor target)
    {
        target.HorizontalReference = source.HorizontalReference;
        target.VerticalReference = source.VerticalReference;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.OffsetX = source.OffsetX;
        target.OffsetY = source.OffsetY;
        target.WrapStyle = source.WrapStyle;
        target.WrapSide = source.WrapSide;
        target.BehindText = source.BehindText;
        target.AllowOverlap = source.AllowOverlap;
        target.ZOrder = source.ZOrder;
        target.Distance = source.Distance;
        target.AnchorOffset = source.AnchorOffset;
    }

    private static TextStyle? CreateInlineStyle(MaterializedReportStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        var textStyle = new TextStyle();
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            textStyle.FontFamily = style.FontFamily;
        }

        if (style.FontSize.HasValue)
        {
            textStyle.FontSize = style.FontSize.Value;
        }

        if (style.Bold == true)
        {
            textStyle.FontWeight = DocFontWeight.Bold;
        }

        if (style.Italic == true)
        {
            textStyle.FontStyle = DocFontStyle.Italic;
        }

        if (style.TextDecoration == ReportTextDecoration.Underline)
        {
            textStyle.Underline = true;
        }

        if (style.TextDecoration == ReportTextDecoration.LineThrough)
        {
            textStyle.Strikethrough = true;
        }

        if (ReportDocumentColorParser.TryParse(style.Foreground, out var foreground))
        {
            textStyle.Color = foreground;
        }

        if (ReportDocumentColorParser.TryParse(style.Background, out var highlight))
        {
            textStyle.HighlightColor = highlight;
        }

        return textStyle;
    }

    private static void ApplyPageSettings(
        ReportPageSettings source,
        SectionProperties target)
    {
        target.PageWidth = source.Width;
        target.PageHeight = source.Height;
        target.Orientation = source.Orientation == ReportPageOrientation.Landscape
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        target.MarginLeft = source.MarginLeft;
        target.MarginTop = source.MarginTop + source.HeaderHeight;
        target.MarginRight = source.MarginRight;
        target.MarginBottom = source.MarginBottom + source.FooterHeight;
        target.HeaderOffset = source.MarginTop;
        target.FooterOffset = source.MarginBottom;
        target.ColumnCount = source.ColumnCount;
        target.ColumnGap = source.ColumnGap;
        target.ColumnEqualWidth = true;
    }

    private static (float Width, float Height) ResolveImageSize(MaterializedImageReportItem item)
    {
        var targetWidth = Math.Max(1f, item.Bounds.Width);
        var targetHeight = Math.Max(1f, item.Bounds.Height);
        if (item.Data is null || !TryGetImageSize(item.Data, out var actualWidth, out var actualHeight))
        {
            return (targetWidth, targetHeight);
        }

        switch (item.SizingMode)
        {
            case ReportSizingMode.OriginalSize:
                return (Math.Max(1f, actualWidth), Math.Max(1f, actualHeight));
            case ReportSizingMode.FitProportional:
            {
                var scale = Math.Min(targetWidth / actualWidth, targetHeight / actualHeight);
                if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                {
                    return (targetWidth, targetHeight);
                }

                return (Math.Max(1f, actualWidth * scale), Math.Max(1f, actualHeight * scale));
            }
            case ReportSizingMode.Stretch:
            default:
                return (targetWidth, targetHeight);
        }
    }

    private static bool TryGetImageSize(byte[] data, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        if (data.Length < 10)
        {
            return false;
        }

        if (TryGetPngSize(data, out width, out height))
        {
            return true;
        }

        if (TryGetJpegSize(data, out width, out height))
        {
            return true;
        }

        if (TryGetGifSize(data, out width, out height))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetPngSize(byte[] data, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 24 || !data.AsSpan(0, signature.Length).SequenceEqual(signature))
        {
            return false;
        }

        width = ReadBigEndianUInt32(data, 16);
        height = ReadBigEndianUInt32(data, 20);
        return width > 0f && height > 0f;
    }

    private static bool TryGetGifSize(byte[] data, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        if (data.Length < 10
            || !(data[0] == 'G' && data[1] == 'I' && data[2] == 'F'))
        {
            return false;
        }

        width = data[6] | (data[7] << 8);
        height = data[8] | (data[9] << 8);
        return width > 0f && height > 0f;
    }

    private static bool TryGetJpegSize(byte[] data, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;
            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }

            if (offset + 1 >= data.Length)
            {
                break;
            }

            var segmentLength = (data[offset] << 8) | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                break;
            }

            if (marker is >= 0xC0 and <= 0xC3
                or >= 0xC5 and <= 0xC7
                or >= 0xC9 and <= 0xCB
                or >= 0xCD and <= 0xCF)
            {
                if (offset + 7 >= data.Length)
                {
                    break;
                }

                height = (data[offset + 3] << 8) | data[offset + 4];
                width = (data[offset + 5] << 8) | data[offset + 6];
                return width > 0f && height > 0f;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static uint ReadBigEndianUInt32(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24)
            | (data[offset + 1] << 16)
            | (data[offset + 2] << 8)
            | data[offset + 3]);
    }

    private static Document CreateEmptyDocument(MaterializedReport report)
    {
        var document = CreateEmptyDocument();
        ApplyDocumentDefaults(report, document);
        return document;
    }

    private static Document CreateEmptyDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        return document;
    }

    private static void ApplyDocumentDefaults(MaterializedReport report, Document document)
    {
        if (string.IsNullOrWhiteSpace(report.DefaultFontFamily))
        {
            return;
        }

        document.DefaultTextStyle.FontFamily = report.DefaultFontFamily;
        document.DefaultTextStyle.FontFamilyAscii = report.DefaultFontFamily;
        document.DefaultTextStyle.FontFamilyHighAnsi = report.DefaultFontFamily;
        document.DefaultTextStyle.FontFamilyEastAsia = report.DefaultFontFamily;
        document.DefaultTextStyle.FontFamilyComplexScript = report.DefaultFontFamily;
    }

    private static bool ShouldInsertPageBreakBefore(MaterializedReportPageBreak? pageBreak)
    {
        return pageBreak?.Location is ReportPageBreakLocation.Start or ReportPageBreakLocation.StartAndEnd;
    }

    private static bool ShouldInsertPageBreakAfter(MaterializedReportPageBreak? pageBreak)
    {
        return pageBreak?.Location is ReportPageBreakLocation.End or ReportPageBreakLocation.StartAndEnd;
    }

    private enum ReportBorderSide
    {
        Top,
        Bottom,
        Left,
        Right
    }
}
