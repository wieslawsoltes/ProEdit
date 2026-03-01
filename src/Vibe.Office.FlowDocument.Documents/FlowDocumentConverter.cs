using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;
using DocumentBlock = Vibe.Office.Documents.Block;
using DocumentTableCell = Vibe.Office.Documents.TableCell;
using DocumentTableRow = Vibe.Office.Documents.TableRow;

namespace Vibe.Office.FlowDocument.Documents;

/// <summary>
/// Converts FlowDocument content into <see cref="Document"/> instances.
/// </summary>
public sealed class FlowDocumentConverter
{
    private int _nextListId = 1;
    private int _nextEmbeddedUiId = 1;
    private readonly FlowDocumentConverterOptions _options;
    private readonly List<EmbeddedFlowUiElement> _embeddedUiElements = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentConverter"/> class.
    /// </summary>
    public FlowDocumentConverter()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentConverter"/> class.
    /// </summary>
    /// <param name="options">Converter options.</param>
    public FlowDocumentConverter(FlowDocumentConverterOptions? options)
    {
        _options = options ?? new FlowDocumentConverterOptions();
    }

    /// <summary>
    /// Gets the embedded UI elements collected during the last conversion.
    /// </summary>
    public IReadOnlyList<EmbeddedFlowUiElement> EmbeddedUiElements => _embeddedUiElements;

    /// <summary>
    /// Converts the specified <see cref="Vibe.Office.FlowDocument.FlowDocument"/> into a <see cref="Document"/>.
    /// </summary>
    /// <param name="source">The FlowDocument to convert.</param>
    /// <returns>The converted document.</returns>
    public Document Convert(Vibe.Office.FlowDocument.FlowDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _nextListId = 1;
        _nextEmbeddedUiId = 1;
        _embeddedUiElements.Clear();

        var document = new Document();
        document.Blocks.Clear();
        ApplyDocumentDefaults(source, document);

        foreach (var block in source.Blocks)
        {
            AppendBlock(block, document.Blocks);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }

    private void ApplyDocumentDefaults(Vibe.Office.FlowDocument.FlowDocument source, Document target)
    {
        ApplyTextStyle(source.Style, target.DefaultTextStyle);

        if (source.TextAlignment.HasValue)
        {
            target.DefaultParagraphStyleProperties.Alignment = ConvertAlignment(source.TextAlignment.Value);
        }

        if (source.PageWidth.HasValue)
        {
            target.SectionProperties.PageWidth = (float)source.PageWidth.Value;
        }

        if (source.PageHeight.HasValue)
        {
            target.SectionProperties.PageHeight = (float)source.PageHeight.Value;
        }

        if (!source.PagePadding.IsEmpty)
        {
            target.SectionProperties.MarginLeft = (float)source.PagePadding.Left;
            target.SectionProperties.MarginTop = (float)source.PagePadding.Top;
            target.SectionProperties.MarginRight = (float)source.PagePadding.Right;
            target.SectionProperties.MarginBottom = (float)source.PagePadding.Bottom;
        }

        if (source.ColumnGap.HasValue)
        {
            target.SectionProperties.ColumnGap = (float)source.ColumnGap.Value;
        }

        if (FlowColorParser.TryParse(source.Background, out var pageColor))
        {
            target.SectionProperties.PageBackgroundColor = pageColor;
        }
    }

    private void AppendBlock(Vibe.Office.FlowDocument.Block block, IList<DocumentBlock> target)
    {
        switch (block)
        {
            case Vibe.Office.FlowDocument.Paragraph paragraph:
                target.Add(ConvertParagraph(paragraph, null));
                break;
            case Vibe.Office.FlowDocument.Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlock(child, target);
                }
                break;
            case Vibe.Office.FlowDocument.List list:
                AppendList(list, target, 0);
                break;
            case Vibe.Office.FlowDocument.Table table:
                target.Add(ConvertTable(table));
                break;
            case Vibe.Office.FlowDocument.BlockUIContainer blockUi:
                target.Add(ConvertBlockUiContainer(blockUi));
                break;
            default:
                target.Add(new ParagraphBlock());
                break;
        }
    }

    private void AppendList(Vibe.Office.FlowDocument.List list, IList<DocumentBlock> target, int level)
    {
        var listId = _nextListId++;
        var startApplied = false;
        foreach (var item in list.ListItems)
        {
            foreach (var block in item.Blocks)
            {
                AppendBlockWithListInfo(block, target, list, listId, level, ref startApplied);
            }
        }
    }

    private void AppendBlockWithListInfo(
        Vibe.Office.FlowDocument.Block block,
        IList<DocumentBlock> target,
        Vibe.Office.FlowDocument.List list,
        int listId,
        int level,
        ref bool startApplied)
    {
        switch (block)
        {
            case Vibe.Office.FlowDocument.Paragraph paragraph:
            {
                var listInfo = CreateListInfo(list.MarkerStyle, level, listId, list.StartIndex, ref startApplied);
                target.Add(ConvertParagraph(paragraph, listInfo));
                break;
            }
            case Vibe.Office.FlowDocument.Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlockWithListInfo(child, target, list, listId, level, ref startApplied);
                }
                break;
            case Vibe.Office.FlowDocument.List nestedList:
                AppendList(nestedList, target, level + 1);
                break;
            case Vibe.Office.FlowDocument.Table table:
                target.Add(ConvertTable(table));
                break;
            case Vibe.Office.FlowDocument.BlockUIContainer blockUi:
                target.Add(ConvertBlockUiContainer(blockUi));
                break;
            default:
                var fallbackInfo = CreateListInfo(list.MarkerStyle, level, listId, list.StartIndex, ref startApplied);
                target.Add(new ParagraphBlock { ListInfo = fallbackInfo });
                break;
        }
    }

    private ParagraphBlock ConvertParagraph(Vibe.Office.FlowDocument.Paragraph paragraph, ListInfo? listInfo)
    {
        var paragraphBlock = new ParagraphBlock
        {
            ListInfo = listInfo
        };

        ApplyParagraphProperties(paragraph, paragraphBlock);

        var baseStyle = paragraph.Style.HasValues ? paragraph.Style.Clone() : new Vibe.Office.FlowDocument.FlowTextStyleProperties();
        var document = FindDocument(paragraph);
        if (document is not null && document.Style.HasValues)
        {
            baseStyle = document.Style.Clone();
            baseStyle.ApplyOverrides(paragraph.Style);
        }

        AppendInlines(paragraph.Inlines, paragraphBlock, baseStyle, null);
        return paragraphBlock;
    }

    private void ApplyParagraphProperties(Vibe.Office.FlowDocument.Block source, ParagraphBlock target)
    {
        if (source.TextAlignment.HasValue)
        {
            target.Properties.Alignment = ConvertAlignment(source.TextAlignment.Value);
        }

        if (!source.Margin.IsEmpty)
        {
            target.Properties.SpacingBefore = (float)source.Margin.Top;
            target.Properties.SpacingAfter = (float)source.Margin.Bottom;
            target.Properties.IndentLeft = (float)source.Margin.Left;
            target.Properties.IndentRight = (float)source.Margin.Right;
        }

        if (source.LineHeight.HasValue && source.LineHeight.Value > 0d)
        {
            target.Properties.LineSpacing = Math.Max(1, (int)Math.Round(source.LineHeight.Value));
            target.Properties.LineSpacingRule = DocLineSpacingRule.Exactly;
        }

        if (source.BreakPageBefore.HasValue)
        {
            target.Properties.PageBreakBefore = source.BreakPageBefore.Value;
        }

        // BreakColumnBefore has no dedicated equivalent in the document model;
        // page breaks are preserved while column-break intent is explicitly degraded.
        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy)
            && Enum.TryParse<DocLineSpacingRule>(source.LineStackingStrategy, ignoreCase: true, out var spacingRule))
        {
            target.Properties.LineSpacingRule = spacingRule;
        }

        if (FlowColorParser.TryParse(source.Background, out var shading))
        {
            target.Properties.ShadingColor = shading;
        }

        if (!source.BorderThickness.IsEmpty)
        {
            ApplyFlowBordersToParagraph(source.BorderThickness, source.BorderBrush, target.Properties.Borders);
        }

        ApplyFlowDirectionToParagraph(source.FlowDirection, target.Properties);

        if (source is Vibe.Office.FlowDocument.Paragraph paragraph && paragraph.TextIndent.HasValue)
        {
            target.Properties.FirstLineIndent = (float)paragraph.TextIndent.Value;
        }

        if (source is Vibe.Office.FlowDocument.Paragraph flowParagraph)
        {
            if (flowParagraph.KeepWithNext.HasValue)
            {
                target.Properties.KeepWithNext = flowParagraph.KeepWithNext.Value;
            }

            if (flowParagraph.KeepTogether.HasValue)
            {
                target.Properties.KeepLinesTogether = flowParagraph.KeepTogether.Value;
            }
        }
    }

    private void AppendInlines(
        Vibe.Office.FlowDocument.InlineCollection inlines,
        ParagraphBlock paragraph,
        Vibe.Office.FlowDocument.FlowTextStyleProperties currentStyle,
        HyperlinkInfo? hyperlink)
    {
        foreach (var inline in inlines)
        {
            AppendInline(inline, paragraph, currentStyle, hyperlink);
        }
    }

    private void AppendInline(
        Vibe.Office.FlowDocument.Inline inline,
        ParagraphBlock paragraph,
        Vibe.Office.FlowDocument.FlowTextStyleProperties currentStyle,
        HyperlinkInfo? hyperlink)
    {
        if (inline is Vibe.Office.FlowDocument.Run run)
        {
            AppendRun(run.Text, paragraph, currentStyle, hyperlink);
            return;
        }

        if (inline is Vibe.Office.FlowDocument.LineBreak)
        {
            AppendRun("\n", paragraph, currentStyle, hyperlink);
            return;
        }

        if (inline is Vibe.Office.FlowDocument.InlineUIContainer inlineUi)
        {
            if (TryAppendInlineImage(inlineUi, paragraph, hyperlink))
            {
                return;
            }

            if (!TryAppendInlineUiContainer(inlineUi, paragraph, hyperlink))
            {
                var placeholder = ResolveInlinePlaceholder(inlineUi.Child);
                AppendRun(placeholder, paragraph, currentStyle, hyperlink);
            }
            return;
        }

        if (inline is Vibe.Office.FlowDocument.AnchoredBlock anchored)
        {
            paragraph.FloatingObjects.Add(ConvertAnchoredBlock(anchored));
            return;
        }

        if (inline is Vibe.Office.FlowDocument.Span span)
        {
            var nextStyle = currentStyle;
            if (span.Style.HasValues)
            {
                nextStyle = currentStyle.Clone();
                nextStyle.ApplyOverrides(span.Style);
            }

            var nextHyperlink = hyperlink;
            if (span is Vibe.Office.FlowDocument.Hyperlink link)
            {
                nextHyperlink = BuildHyperlink(link) ?? hyperlink;
            }

            AppendInlines(span.Inlines, paragraph, nextStyle, nextHyperlink);
        }
    }

    private void AppendRun(
        string? text,
        ParagraphBlock paragraph,
        Vibe.Office.FlowDocument.FlowTextStyleProperties style,
        HyperlinkInfo? hyperlink)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var runStyle = ConvertTextStyle(style);
        var run = new RunInline(text, runStyle);
        if (hyperlink is not null)
        {
            run.Hyperlink = hyperlink;
        }

        paragraph.Inlines.Add(run);
    }

    private TableBlock ConvertTable(Vibe.Office.FlowDocument.Table table)
    {
        var tableBlock = new TableBlock();
        if (table.CellSpacing.HasValue && table.CellSpacing.Value >= 0d)
        {
            tableBlock.Properties.CellSpacing = (float)table.CellSpacing.Value;
            tableBlock.Properties.CellSpacingUnit = TableWidthUnit.Dxa;
        }

        if (table.Columns.Count > 0)
        {
            foreach (var column in table.Columns)
            {
                if (column.Width.HasValue && column.Width.Value > 0)
                {
                    tableBlock.Properties.ColumnWidths.Add((float)column.Width.Value);
                }
            }
        }

        foreach (var group in table.RowGroups)
        {
            var pendingSpans = new List<PendingRowSpan>();
            foreach (var row in group.Rows)
            {
                var tableRow = new DocumentTableRow();
                var newPending = new List<PendingRowSpan>();
                var orderedPending = pendingSpans.OrderBy(span => span.StartColumn).ToList();
                var pendingIndex = 0;
                var columnIndex = 0;

                void AddContinuation(PendingRowSpan span)
                {
                    tableRow.Cells.Add(CreateRowSpanPlaceholder(span.ColumnSpan));
                    if (span.RemainingRows > 1)
                    {
                        newPending.Add(new PendingRowSpan(span.StartColumn, span.ColumnSpan, span.RemainingRows - 1));
                    }
                    columnIndex += span.ColumnSpan;
                }

                void FlushPendingAtOrBeforeCurrent()
                {
                    while (pendingIndex < orderedPending.Count && orderedPending[pendingIndex].StartColumn <= columnIndex)
                    {
                        AddContinuation(orderedPending[pendingIndex++]);
                    }
                }

                FlushPendingAtOrBeforeCurrent();

                foreach (var cell in row.Cells)
                {
                    FlushPendingAtOrBeforeCurrent();
                    var columnSpan = Math.Max(1, cell.ColumnSpan);
                    var rowSpan = Math.Max(1, cell.RowSpan);
                    var tableCell = ConvertTableCell(cell, columnSpan, rowSpan, columnIndex);
                    tableRow.Cells.Add(tableCell);
                    if (rowSpan > 1)
                    {
                        newPending.Add(new PendingRowSpan(columnIndex, columnSpan, rowSpan - 1));
                    }
                    columnIndex += columnSpan;
                }

                while (pendingIndex < orderedPending.Count)
                {
                    var span = orderedPending[pendingIndex++];
                    if (span.StartColumn > columnIndex)
                    {
                        columnIndex = span.StartColumn;
                    }
                    AddContinuation(span);
                }

                tableBlock.Rows.Add(tableRow);
                pendingSpans = newPending;
            }
        }

        return tableBlock;
    }

    private ParagraphBlock ConvertBlockUiContainer(Vibe.Office.FlowDocument.BlockUIContainer container)
    {
        var paragraph = new ParagraphBlock();
        ApplyParagraphProperties(container, paragraph);

        if (TryCreateEmbeddedUiShape(container.Child, false, out var shape))
        {
            paragraph.Inlines.Add(shape);
            return paragraph;
        }

        var placeholder = ResolveBlockPlaceholder(container.Child);
        if (!string.IsNullOrEmpty(placeholder))
        {
            paragraph.Inlines.Add(new RunInline(placeholder));
        }
        return paragraph;
    }

    /// <summary>
    /// Tries to parse an embedded UI element ID from a shape marker name.
    /// </summary>
    /// <param name="shapeName">The shape marker name.</param>
    /// <param name="prefix">The configured shape marker prefix.</param>
    /// <param name="elementId">The parsed element ID.</param>
    /// <returns><see langword="true"/> if the name contains an embedded UI marker ID; otherwise <see langword="false"/>.</returns>
    public static bool TryParseEmbeddedUiElementId(string? shapeName, string prefix, out string elementId)
    {
        elementId = string.Empty;
        if (string.IsNullOrWhiteSpace(shapeName))
        {
            return false;
        }

        var effectivePrefix = string.IsNullOrWhiteSpace(prefix)
            ? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
            : prefix;
        if (!shapeName.StartsWith(effectivePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = shapeName[effectivePrefix.Length..];
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        elementId = suffix;
        return true;
    }

    private FloatingObject ConvertAnchoredBlock(Vibe.Office.FlowDocument.AnchoredBlock anchored)
    {
        var alignment = anchored is Vibe.Office.FlowDocument.Floater
            ? FloatingHorizontalAlignment.Right
            : FloatingHorizontalAlignment.Left;
        var width = anchored switch
        {
            Vibe.Office.FlowDocument.Figure figure when figure.Width.HasValue => (float)Math.Max(1d, figure.Width.Value),
            Vibe.Office.FlowDocument.Floater floater when floater.Width.HasValue => (float)Math.Max(1d, floater.Width.Value),
            _ => 200f
        };
        var height = anchored is Vibe.Office.FlowDocument.Figure withHeight && withHeight.Height.HasValue
            ? (float)Math.Max(1d, withHeight.Height.Value)
            : 120f;
        var shape = TryCreateAnchoredEmbeddedUiShape(anchored, out var embeddedShape)
            ? embeddedShape
            : new ShapeInline(width, height)
            {
                TextBox = BuildShapeTextBox(anchored)
            };
        shape.Width = width;
        shape.Height = height;

        var floating = new FloatingObject(shape);
        floating.Anchor.HorizontalReference = FloatingHorizontalReference.Margin;
        floating.Anchor.VerticalReference = FloatingVerticalReference.Paragraph;
        floating.Anchor.HorizontalAlignment = alignment;
        floating.Anchor.WrapStyle = FloatingWrapStyle.Square;

        if (!anchored.Margin.IsEmpty)
        {
            floating.Anchor.Distance = new DocThickness(
                (float)anchored.Margin.Left,
                (float)anchored.Margin.Top,
                (float)anchored.Margin.Right,
                (float)anchored.Margin.Bottom);
        }

        return floating;
    }

    private bool TryCreateAnchoredEmbeddedUiShape(
        Vibe.Office.FlowDocument.AnchoredBlock anchored,
        out ShapeInline shape)
    {
        shape = null!;
        if (anchored.Blocks.Count != 1)
        {
            return false;
        }

        if (anchored.Blocks[0] is Vibe.Office.FlowDocument.BlockUIContainer blockUi)
        {
            return TryCreateEmbeddedUiShape(blockUi.Child, isInline: false, out shape);
        }

        if (anchored.Blocks[0] is Vibe.Office.FlowDocument.Paragraph paragraph
            && paragraph.Inlines.Count == 1
            && paragraph.Inlines[0] is Vibe.Office.FlowDocument.InlineUIContainer inlineUi)
        {
            return TryCreateEmbeddedUiShape(inlineUi.Child, isInline: false, out shape);
        }

        return false;
    }

    private ShapeTextBox BuildShapeTextBox(Vibe.Office.FlowDocument.AnchoredBlock anchored)
    {
        var textBox = new ShapeTextBox();
        if (anchored.Blocks.Count == 0)
        {
            textBox.Blocks.Add(new ParagraphBlock());
            return textBox;
        }

        foreach (var block in anchored.Blocks)
        {
            AppendBlock(block, textBox.Blocks);
        }

        return textBox;
    }

    private DocumentTableCell ConvertTableCell(
        Vibe.Office.FlowDocument.TableCell cell,
        int columnSpan,
        int rowSpan,
        int columnIndex)
    {
        var tableCell = new DocumentTableCell
        {
            ColumnSpan = columnSpan
        };

        if (rowSpan > 1)
        {
            tableCell.VerticalMerge = TableCellVerticalMerge.Restart;
        }

        if (cell.Blocks.Count == 0)
        {
            tableCell.Blocks.Add(new ParagraphBlock());
        }
        else
        {
            foreach (var block in cell.Blocks)
            {
                AppendBlock(block, tableCell.Blocks);
            }
        }

        if (_options.ExportCellVisualProperties)
        {
            ApplyFlowTableCellVisualProperties(cell, tableCell, columnIndex);
        }

        return tableCell;
    }

    private static void ApplyFlowTableCellVisualProperties(
        Vibe.Office.FlowDocument.TableCell source,
        DocumentTableCell target,
        int columnIndex)
    {
        if (!source.Padding.IsEmpty)
        {
            target.Properties.Padding = new DocThickness(
                (float)source.Padding.Left,
                (float)source.Padding.Top,
                (float)source.Padding.Right,
                (float)source.Padding.Bottom);
        }

        if (FlowColorParser.TryParse(source.Background, out var shading))
        {
            target.Properties.ShadingColor = shading;
        }

        if (!source.BorderThickness.IsEmpty)
        {
            ApplyFlowBordersToCell(source.BorderThickness, source.BorderBrush, target.Properties.Borders, columnIndex);
        }

        if (TryParseDocTextDirection(source.FlowDirection, out var direction))
        {
            target.Properties.TextDirection = direction;
        }

        if (source.TextAlignment.HasValue)
        {
            ApplyCellAlignmentToParagraphs(target, ConvertAlignment(source.TextAlignment.Value));
        }
    }

    private static void ApplyCellAlignmentToParagraphs(DocumentTableCell cell, ParagraphAlignment alignment)
    {
        for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
        {
            if (cell.Blocks[blockIndex] is ParagraphBlock paragraph && !paragraph.Properties.Alignment.HasValue)
            {
                paragraph.Properties.Alignment = alignment;
            }
        }
    }

    private static void ApplyFlowBordersToParagraph(
        Vibe.Office.FlowDocument.FlowThickness thickness,
        string? brush,
        ParagraphBorders target)
    {
        var hasColor = FlowColorParser.TryParse(brush, out var color);
        if (thickness.Top > 0d)
        {
            target.Top = new BorderLine
            {
                Thickness = (float)thickness.Top,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Bottom > 0d)
        {
            target.Bottom = new BorderLine
            {
                Thickness = (float)thickness.Bottom,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Left > 0d)
        {
            target.Left = new BorderLine
            {
                Thickness = (float)thickness.Left,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Right > 0d)
        {
            target.Right = new BorderLine
            {
                Thickness = (float)thickness.Right,
                Color = hasColor ? color : DocColor.Black
            };
        }
    }

    private static void ApplyFlowBordersToCell(
        Vibe.Office.FlowDocument.FlowThickness thickness,
        string? brush,
        TableCellBorders target,
        int columnIndex)
    {
        var hasColor = FlowColorParser.TryParse(brush, out var color);
        if (thickness.Top > 0d)
        {
            target.Top = new BorderLine
            {
                Thickness = (float)thickness.Top,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Bottom > 0d)
        {
            target.Bottom = new BorderLine
            {
                Thickness = (float)thickness.Bottom,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Left > 0d)
        {
            target.Left = new BorderLine
            {
                Thickness = (float)thickness.Left,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (thickness.Right > 0d)
        {
            target.Right = new BorderLine
            {
                Thickness = (float)thickness.Right,
                Color = hasColor ? color : DocColor.Black
            };
        }

        if (columnIndex == 0 && target.Left is null && thickness.Left > 0d)
        {
            target.Left = new BorderLine
            {
                Thickness = (float)thickness.Left,
                Color = hasColor ? color : DocColor.Black
            };
        }
    }

    private static void ApplyFlowDirectionToParagraph(string? flowDirection, ParagraphProperties target)
    {
        if (string.IsNullOrWhiteSpace(flowDirection))
        {
            return;
        }

        if (string.Equals(flowDirection, "RightToLeft", StringComparison.OrdinalIgnoreCase))
        {
            target.Bidi = true;
            return;
        }

        if (TryParseDocTextDirection(flowDirection, out var direction))
        {
            target.TextDirection = direction;
        }
    }

    private static bool TryParseDocTextDirection(string? text, out DocTextDirection direction)
    {
        direction = default;
        return !string.IsNullOrWhiteSpace(text)
               && Enum.TryParse(text, ignoreCase: true, out direction);
    }

    private static DocumentTableCell CreateRowSpanPlaceholder(int columnSpan)
    {
        return new DocumentTableCell
        {
            ColumnSpan = Math.Max(1, columnSpan),
            VerticalMerge = TableCellVerticalMerge.Continue
        };
    }

    private static ListInfo CreateListInfo(
        Vibe.Office.FlowDocument.FlowListMarkerStyle style,
        int level,
        int listId,
        int? startIndex,
        ref bool startApplied)
    {
        var info = new ListInfo(ConvertListKind(style), level, listId);

        if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.Disc)
        {
            info.NumberFormat = ListNumberFormat.Bullet;
            info.BulletSymbol = "•";
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.Circle)
        {
            info.NumberFormat = ListNumberFormat.Bullet;
            info.BulletSymbol = "○";
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.Square)
        {
            info.NumberFormat = ListNumberFormat.Bullet;
            info.BulletSymbol = "■";
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerRoman)
        {
            info.NumberFormat = ListNumberFormat.LowerRoman;
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman)
        {
            info.NumberFormat = ListNumberFormat.UpperRoman;
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerLatin)
        {
            info.NumberFormat = ListNumberFormat.LowerLetter;
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperLatin)
        {
            info.NumberFormat = ListNumberFormat.UpperLetter;
        }
        else if (style == Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal)
        {
            info.NumberFormat = ListNumberFormat.Decimal;
        }

        if (!startApplied && startIndex.HasValue)
        {
            info.StartAt = startIndex.Value;
            startApplied = true;
        }

        return info;
    }

    private static ListKind ConvertListKind(Vibe.Office.FlowDocument.FlowListMarkerStyle style)
    {
        return style switch
        {
            Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal => ListKind.Numbered,
            Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerLatin => ListKind.Numbered,
            Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperLatin => ListKind.Numbered,
            Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerRoman => ListKind.Numbered,
            Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman => ListKind.Numbered,
            Vibe.Office.FlowDocument.FlowListMarkerStyle.None => ListKind.None,
            _ => ListKind.Bullet
        };
    }

    private static ParagraphAlignment ConvertAlignment(Vibe.Office.FlowDocument.FlowTextAlignment alignment)
    {
        return alignment switch
        {
            Vibe.Office.FlowDocument.FlowTextAlignment.Center => ParagraphAlignment.Center,
            Vibe.Office.FlowDocument.FlowTextAlignment.Right => ParagraphAlignment.Right,
            Vibe.Office.FlowDocument.FlowTextAlignment.Justify => ParagraphAlignment.Justify,
            _ => ParagraphAlignment.Left
        };
    }

    private static HyperlinkInfo? BuildHyperlink(Vibe.Office.FlowDocument.Hyperlink link)
    {
        if (link is null)
        {
            return null;
        }

        var uri = link.NavigateUri;
        var anchor = link.TargetName;
        if (!string.IsNullOrWhiteSpace(uri) && uri.StartsWith('#'))
        {
            anchor = uri.TrimStart('#');
            uri = null;
        }

        if (string.IsNullOrWhiteSpace(uri) && string.IsNullOrWhiteSpace(anchor))
        {
            return null;
        }

        return new HyperlinkInfo(uri, anchor, link.ToolTip);
    }

    private static TextStyleProperties? ConvertTextStyle(Vibe.Office.FlowDocument.FlowTextStyleProperties style)
    {
        if (!style.HasValues)
        {
            return null;
        }

        var result = new TextStyleProperties();
        ApplyTextStyle(style, result);
        return result.HasValues ? result : null;
    }

    private static void ApplyTextStyle(Vibe.Office.FlowDocument.FlowTextStyleProperties style, TextStyle target)
    {
        if (style.FontFamily is not null)
        {
            target.FontFamily = style.FontFamily;
        }

        if (style.FontSize.HasValue)
        {
            target.FontSize = (float)style.FontSize.Value;
        }

        if (style.FontWeight.HasValue)
        {
            target.FontWeight = style.FontWeight.Value == Vibe.Office.FlowDocument.FlowFontWeight.Bold
                ? DocFontWeight.Bold
                : DocFontWeight.Normal;
        }

        if (style.FontStyle.HasValue)
        {
            target.FontStyle = style.FontStyle.Value == Vibe.Office.FlowDocument.FlowFontStyle.Italic
                ? DocFontStyle.Italic
                : DocFontStyle.Normal;
        }

        if (style.TextDecorations.HasValue)
        {
            if (style.TextDecorations.Value.HasFlag(Vibe.Office.FlowDocument.FlowTextDecorations.Underline))
            {
                target.Underline = true;
                target.UnderlineStyle = DocUnderlineStyle.Single;
            }

            if (style.TextDecorations.Value.HasFlag(Vibe.Office.FlowDocument.FlowTextDecorations.Strikethrough))
            {
                target.Strikethrough = true;
            }
        }

        if (style.BaselineAlignment.HasValue)
        {
            target.VerticalPosition = style.BaselineAlignment.Value switch
            {
                Vibe.Office.FlowDocument.FlowBaselineAlignment.Superscript => DocVerticalPosition.Superscript,
                Vibe.Office.FlowDocument.FlowBaselineAlignment.Subscript => DocVerticalPosition.Subscript,
                _ => DocVerticalPosition.Normal
            };
        }

        if (FlowColorParser.TryParse(style.Foreground, out var color))
        {
            target.Color = color;
        }

        if (FlowColorParser.TryParse(style.Background, out var highlight))
        {
            target.HighlightColor = highlight;
        }
    }

    private static void ApplyTextStyle(Vibe.Office.FlowDocument.FlowTextStyleProperties style, TextStyleProperties target)
    {
        if (style.FontFamily is not null)
        {
            target.FontFamily = style.FontFamily;
        }

        if (style.FontSize.HasValue)
        {
            target.FontSize = (float)style.FontSize.Value;
        }

        if (style.FontWeight.HasValue)
        {
            target.FontWeight = style.FontWeight.Value == Vibe.Office.FlowDocument.FlowFontWeight.Bold
                ? DocFontWeight.Bold
                : DocFontWeight.Normal;
        }

        if (style.FontStyle.HasValue)
        {
            target.FontStyle = style.FontStyle.Value == Vibe.Office.FlowDocument.FlowFontStyle.Italic
                ? DocFontStyle.Italic
                : DocFontStyle.Normal;
        }

        if (style.TextDecorations.HasValue)
        {
            if (style.TextDecorations.Value.HasFlag(Vibe.Office.FlowDocument.FlowTextDecorations.Underline))
            {
                target.Underline = true;
                target.UnderlineStyle = DocUnderlineStyle.Single;
            }

            if (style.TextDecorations.Value.HasFlag(Vibe.Office.FlowDocument.FlowTextDecorations.Strikethrough))
            {
                target.Strikethrough = true;
            }
        }

        if (style.BaselineAlignment.HasValue)
        {
            target.VerticalPosition = style.BaselineAlignment.Value switch
            {
                Vibe.Office.FlowDocument.FlowBaselineAlignment.Superscript => DocVerticalPosition.Superscript,
                Vibe.Office.FlowDocument.FlowBaselineAlignment.Subscript => DocVerticalPosition.Subscript,
                _ => DocVerticalPosition.Normal
            };
        }

        if (FlowColorParser.TryParse(style.Foreground, out var color))
        {
            target.Color = color;
        }

        if (FlowColorParser.TryParse(style.Background, out var highlight))
        {
            target.HighlightColor = highlight;
        }
    }

    private static Vibe.Office.FlowDocument.FlowDocument? FindDocument(Vibe.Office.FlowDocument.FlowElement element)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (current is Vibe.Office.FlowDocument.FlowDocument document)
            {
                return document;
            }

            current = current.Parent;
        }

        return null;
    }

    private string ResolveInlinePlaceholder(object? child)
    {
        var placeholder = _options.InlineUiPlaceholderFactory?.Invoke(child);
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            placeholder = _options.InlineUiPlaceholderText;
        }

        if (string.IsNullOrWhiteSpace(placeholder))
        {
            placeholder = FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText;
        }

        return placeholder;
    }

    private string ResolveBlockPlaceholder(object? child)
    {
        var placeholder = _options.BlockUiPlaceholderFactory?.Invoke(child);
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            placeholder = _options.BlockUiPlaceholderText;
        }

        if (string.IsNullOrWhiteSpace(placeholder))
        {
            placeholder = FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText;
        }

        return placeholder;
    }

    private bool TryAppendInlineUiContainer(
        Vibe.Office.FlowDocument.InlineUIContainer container,
        ParagraphBlock paragraph,
        HyperlinkInfo? hyperlink)
    {
        if (!TryCreateEmbeddedUiShape(container.Child, true, out var shape))
        {
            return false;
        }

        if (hyperlink is not null)
        {
            shape.Hyperlink = hyperlink;
        }

        paragraph.Inlines.Add(shape);
        return true;
    }

    private static bool TryAppendInlineImage(
        Vibe.Office.FlowDocument.InlineUIContainer container,
        ParagraphBlock paragraph,
        HyperlinkInfo? hyperlink)
    {
        if (container.Child is FlowInlineImageData payload)
        {
            if (payload.Data.Length == 0)
            {
                return false;
            }

            var data = new byte[payload.Data.Length];
            payload.Data.CopyTo(data, 0);
            var imageInline = new ImageInline(
                data,
                payload.Width > 0f ? payload.Width : 1f,
                payload.Height > 0f ? payload.Height : 1f,
                payload.ContentType);
            if (hyperlink is not null)
            {
                imageInline.Hyperlink = hyperlink;
            }

            paragraph.Inlines.Add(imageInline);
            return true;
        }

        if (container.Child is not Image imageControl || imageControl.Source is not Bitmap bitmap)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            bytes = stream.ToArray();
            if (bytes.Length == 0)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var width = ResolveImageDimension(imageControl.Width, imageControl.Bounds.Width, bitmap.Size.Width);
        var height = ResolveImageDimension(imageControl.Height, imageControl.Bounds.Height, bitmap.Size.Height);
        var controlImageInline = new ImageInline(bytes, width, height, "image/png");
        if (hyperlink is not null)
        {
            controlImageInline.Hyperlink = hyperlink;
        }

        paragraph.Inlines.Add(controlImageInline);
        return true;
    }

    private static float ResolveImageDimension(double preferred, double bounds, double fallback)
    {
        if (!double.IsNaN(preferred) && !double.IsInfinity(preferred) && preferred > 0d)
        {
            return (float)preferred;
        }

        if (!double.IsNaN(bounds) && !double.IsInfinity(bounds) && bounds > 0d)
        {
            return (float)bounds;
        }

        if (!double.IsNaN(fallback) && !double.IsInfinity(fallback) && fallback > 0d)
        {
            return (float)fallback;
        }

        return 1f;
    }

    private bool TryCreateEmbeddedUiShape(object? child, bool isInline, out ShapeInline shape)
    {
        shape = null!;
        if (!_options.EnableEmbeddedUiElements || child is null || !CanEmbedUiChild(child))
        {
            return false;
        }

        var elementId = _nextEmbeddedUiId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _nextEmbeddedUiId++;
        _embeddedUiElements.Add(new EmbeddedFlowUiElement(elementId, child, isInline));

        var (width, height) = ResolveEmbeddedUiSize(child, isInline);
        shape = new ShapeInline(width, height)
        {
            Name = BuildEmbeddedUiShapeName(elementId),
            TextBox = CreateEmptyShapeTextBox()
        };
        return true;
    }

    private bool CanEmbedUiChild(object child)
    {
        if (_options.EmbeddedUiElementPredicate is not null)
        {
            return _options.EmbeddedUiElementPredicate(child);
        }

        return child is Control;
    }

    private static ShapeTextBox CreateEmptyShapeTextBox()
    {
        var textBox = new ShapeTextBox();
        textBox.Blocks.Add(new ParagraphBlock());
        return textBox;
    }

    private string BuildEmbeddedUiShapeName(string elementId)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.EmbeddedUiShapePrefix)
            ? FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix
            : _options.EmbeddedUiShapePrefix;
        return string.Concat(prefix, elementId);
    }

    private (float Width, float Height) ResolveEmbeddedUiSize(object child, bool isInline)
    {
        var fallbackWidth = isInline ? _options.InlineUiFallbackWidth : _options.BlockUiFallbackWidth;
        var fallbackHeight = isInline ? _options.InlineUiFallbackHeight : _options.BlockUiFallbackHeight;

        if (_options.EmbeddedUiSizeResolver is { } resolver)
        {
            var resolved = resolver(child, isInline);
            if (resolved.HasValue)
            {
                var resolvedWidth = ResolvePreferredDimension(resolved.Value.Width, minimum: 0d, fallbackWidth);
                var resolvedHeight = ResolvePreferredDimension(resolved.Value.Height, minimum: 0d, fallbackHeight);
                return ((float)Math.Max(1d, resolvedWidth), (float)Math.Max(1d, resolvedHeight));
            }
        }

        if (child is not Control control)
        {
            return ((float)Math.Max(1d, fallbackWidth), (float)Math.Max(1d, fallbackHeight));
        }

        var width = ResolvePreferredDimension(control.Width, control.MinWidth, fallbackWidth);
        var height = ResolvePreferredDimension(control.Height, control.MinHeight, fallbackHeight);

        if (!double.IsNaN(control.MaxWidth) && !double.IsInfinity(control.MaxWidth) && control.MaxWidth > 0d)
        {
            width = Math.Min(width, control.MaxWidth);
        }

        if (!double.IsNaN(control.MaxHeight) && !double.IsInfinity(control.MaxHeight) && control.MaxHeight > 0d)
        {
            height = Math.Min(height, control.MaxHeight);
        }

        return ((float)Math.Max(1d, width), (float)Math.Max(1d, height));
    }

    private static double ResolvePreferredDimension(double value, double minimum, double fallback)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0d)
        {
            return value;
        }

        if (!double.IsNaN(minimum) && !double.IsInfinity(minimum) && minimum > 0d)
        {
            return minimum;
        }

        return Math.Max(1d, fallback);
    }

    private readonly record struct PendingRowSpan(int StartColumn, int ColumnSpan, int RemainingRows);
}
