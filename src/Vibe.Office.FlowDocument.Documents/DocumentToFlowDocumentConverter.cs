using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;
using DocumentBlock = Vibe.Office.Documents.Block;
using DocumentInline = Vibe.Office.Documents.Inline;
using DocumentParagraph = Vibe.Office.Documents.ParagraphBlock;
using DocumentTableCell = Vibe.Office.Documents.TableCell;
using DocumentTableRow = Vibe.Office.Documents.TableRow;
using FlowAnchoredBlock = Vibe.Office.FlowDocument.AnchoredBlock;
using FlowBlock = Vibe.Office.FlowDocument.Block;
using FlowBlockCollection = Vibe.Office.FlowDocument.BlockCollection;
using FlowInline = Vibe.Office.FlowDocument.Inline;
using FlowInlineCollection = Vibe.Office.FlowDocument.InlineCollection;
using FlowList = Vibe.Office.FlowDocument.List;
using FlowListItem = Vibe.Office.FlowDocument.ListItem;
using FlowParagraph = Vibe.Office.FlowDocument.Paragraph;
using FlowTableCell = Vibe.Office.FlowDocument.TableCell;
using FlowTableRow = Vibe.Office.FlowDocument.TableRow;
using FlowTableRowGroup = Vibe.Office.FlowDocument.TableRowGroup;

namespace Vibe.Office.FlowDocument.Documents;

/// <summary>
/// Converts <see cref="Document"/> content into <see cref="Vibe.Office.FlowDocument.FlowDocument"/> instances.
/// </summary>
public sealed class DocumentToFlowDocumentConverter
{
    private readonly DocumentToFlowDocumentConverterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentToFlowDocumentConverter"/> class.
    /// </summary>
    public DocumentToFlowDocumentConverter()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentToFlowDocumentConverter"/> class.
    /// </summary>
    /// <param name="options">Converter options.</param>
    public DocumentToFlowDocumentConverter(DocumentToFlowDocumentConverterOptions? options)
    {
        _options = options ?? new DocumentToFlowDocumentConverterOptions();
    }

    /// <summary>
    /// Converts the specified <see cref="Document"/> into a <see cref="Vibe.Office.FlowDocument.FlowDocument"/>.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <returns>The converted flow document.</returns>
    public Vibe.Office.FlowDocument.FlowDocument Convert(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var document = new Vibe.Office.FlowDocument.FlowDocument();
        ApplyDocumentDefaults(source, document);
        AppendBlocks(source.Blocks, document.Blocks);

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new FlowParagraph());
        }

        return document;
    }

    /// <summary>
    /// Converts a single top-level block from a <see cref="Document"/> into a flow block.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <param name="blockIndex">The top-level block index to convert.</param>
    /// <param name="block">The converted flow block when conversion succeeds.</param>
    /// <returns><see langword="true"/> when conversion succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryConvertTopLevelBlock(
        Document source,
        int blockIndex,
        out Vibe.Office.FlowDocument.Block block)
    {
        ArgumentNullException.ThrowIfNull(source);

        block = null!;
        if ((uint)blockIndex >= (uint)source.Blocks.Count)
        {
            return false;
        }

        if (source.Blocks[blockIndex] is DocumentParagraph paragraph)
        {
            if (paragraph.ListInfo is not null)
            {
                return false;
            }

            if (TryConvertBlockUiContainer(paragraph, out var blockUiContainer))
            {
                block = blockUiContainer;
                return true;
            }

            block = ConvertParagraph(paragraph);
            return true;
        }

        if (source.Blocks[blockIndex] is TableBlock table)
        {
            block = ConvertTable(table);
            return true;
        }

        return false;
    }

    private void ApplyDocumentDefaults(Document source, Vibe.Office.FlowDocument.FlowDocument target)
    {
        var text = source.DefaultTextStyle;
        if (!string.IsNullOrWhiteSpace(text.FontFamily))
        {
            target.FontFamily = text.FontFamily;
        }

        if (text.FontSize > 0f)
        {
            target.FontSize = text.FontSize;
        }

        target.FontWeight = text.FontWeight == DocFontWeight.Bold
            ? Vibe.Office.FlowDocument.FlowFontWeight.Bold
            : Vibe.Office.FlowDocument.FlowFontWeight.Normal;
        target.FontStyle = text.FontStyle == DocFontStyle.Italic
            ? Vibe.Office.FlowDocument.FlowFontStyle.Italic
            : Vibe.Office.FlowDocument.FlowFontStyle.Normal;

        if (text.Color != DocColor.Black)
        {
            target.Foreground = ToFlowColor(text.Color);
        }

        if (text.HighlightColor.HasValue)
        {
            target.Background = ToFlowColor(text.HighlightColor.Value);
        }

        if (source.DefaultParagraphStyleProperties.Alignment.HasValue)
        {
            target.TextAlignment = ToFlowAlignment(source.DefaultParagraphStyleProperties.Alignment.Value);
        }

        var section = source.SectionProperties;
        if (section.PageWidth.HasValue && section.PageWidth.Value > 0f)
        {
            target.PageWidth = section.PageWidth.Value;
        }

        if (section.PageHeight.HasValue && section.PageHeight.Value > 0f)
        {
            target.PageHeight = section.PageHeight.Value;
        }

        var pagePadding = new Vibe.Office.FlowDocument.FlowThickness(
            section.MarginLeft.GetValueOrDefault(0f),
            section.MarginTop.GetValueOrDefault(0f),
            section.MarginRight.GetValueOrDefault(0f),
            section.MarginBottom.GetValueOrDefault(0f));
        if (!pagePadding.IsEmpty)
        {
            target.PagePadding = pagePadding;
        }

        if (section.ColumnGap.HasValue && section.ColumnGap.Value >= 0f)
        {
            target.ColumnGap = section.ColumnGap.Value;
        }

        if (section.ColumnWidths.Count > 0 && section.ColumnWidths[0] > 0f)
        {
            target.ColumnWidth = section.ColumnWidths[0];
        }

        if (section.ColumnSeparator == true)
        {
            target.ColumnRuleWidth = 1d;
        }

        if (section.PageBackgroundColor.HasValue)
        {
            target.Background = ToFlowColor(section.PageBackgroundColor.Value);
        }
    }

    private void AppendBlocks(IReadOnlyList<DocumentBlock> source, FlowBlockCollection target)
    {
        var index = 0;
        while (index < source.Count)
        {
            if (source[index] is DocumentParagraph paragraphWithList && paragraphWithList.ListInfo is not null)
            {
                var list = ConvertListSequence(source, ref index);
                target.Add(list);
                continue;
            }

            if (source[index] is DocumentParagraph paragraph && TryConvertBlockUiContainer(paragraph, out var blockUi))
            {
                target.Add(blockUi);
                index++;
                continue;
            }

            if (source[index] is DocumentParagraph paragraphBlock)
            {
                target.Add(ConvertParagraph(paragraphBlock));
                index++;
                continue;
            }

            if (source[index] is TableBlock tableBlock)
            {
                target.Add(ConvertTable(tableBlock));
                index++;
                continue;
            }

            index++;
        }
    }

    private FlowList ConvertListSequence(IReadOnlyList<DocumentBlock> source, ref int index)
    {
        var firstParagraph = (DocumentParagraph)source[index];
        var firstInfo = firstParagraph.ListInfo ?? new ListInfo(ListKind.Bullet);
        var root = CreateList(firstInfo);

        var stack = new Stack<ListContext>();
        stack.Push(new ListContext(root, firstInfo));

        while (index < source.Count && source[index] is DocumentParagraph paragraph && paragraph.ListInfo is not null)
        {
            var info = paragraph.ListInfo;
            if (info.Level < firstInfo.Level)
            {
                break;
            }

            while (stack.Count > 0 && info.Level < stack.Peek().Info.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                break;
            }

            if (info.Level > stack.Peek().Info.Level)
            {
                var parent = stack.Peek();
                if (parent.LastItem is null)
                {
                    var syntheticItem = new FlowListItem();
                    parent.List.ListItems.Add(syntheticItem);
                    parent.LastItem = syntheticItem;
                }

                var nested = CreateList(info);
                parent.LastItem.Blocks.Add(nested);
                stack.Push(new ListContext(nested, info));
            }
            else if (!CanContinueList(stack.Peek().Info, info))
            {
                break;
            }

            var current = stack.Peek();
            var item = new FlowListItem();
            item.Blocks.Add(ConvertParagraph(paragraph));
            current.List.ListItems.Add(item);
            current.LastItem = item;
            index++;
        }

        return root;
    }

    private static bool CanContinueList(ListInfo current, ListInfo next)
    {
        if (current.Level != next.Level)
        {
            return false;
        }

        if (current.ListId.HasValue && next.ListId.HasValue && current.ListId.Value != next.ListId.Value)
        {
            return false;
        }

        var currentMarker = ToFlowMarkerStyle(current);
        var nextMarker = ToFlowMarkerStyle(next);
        return currentMarker == nextMarker;
    }

    private static FlowList CreateList(ListInfo info)
    {
        var list = new FlowList
        {
            MarkerStyle = ToFlowMarkerStyle(info)
        };

        if (info.StartAt.HasValue)
        {
            list.StartIndex = info.StartAt.Value;
        }

        if (info.LeftIndent.HasValue)
        {
            list.MarkerOffset = info.LeftIndent.Value;
        }

        return list;
    }

    private static Vibe.Office.FlowDocument.FlowListMarkerStyle ToFlowMarkerStyle(ListInfo info)
    {
        if (info.Kind == ListKind.None)
        {
            return Vibe.Office.FlowDocument.FlowListMarkerStyle.None;
        }

        if (info.Kind == ListKind.Bullet)
        {
            return info.BulletSymbol switch
            {
                "○" => Vibe.Office.FlowDocument.FlowListMarkerStyle.Circle,
                "■" => Vibe.Office.FlowDocument.FlowListMarkerStyle.Square,
                _ => Vibe.Office.FlowDocument.FlowListMarkerStyle.Disc
            };
        }

        return info.NumberFormat switch
        {
            ListNumberFormat.LowerLetter => Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerLatin,
            ListNumberFormat.UpperLetter => Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperLatin,
            ListNumberFormat.LowerRoman => Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerRoman,
            ListNumberFormat.UpperRoman => Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman,
            _ => Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal
        };
    }

    private FlowParagraph ConvertParagraph(DocumentParagraph source)
    {
        var paragraph = new FlowParagraph();
        ApplyParagraphProperties(source.Properties, paragraph);

        if (source.Inlines.Count == 0)
        {
            if (!string.IsNullOrEmpty(source.Text))
            {
                paragraph.Inlines.Add(new Vibe.Office.FlowDocument.Run(source.Text));
            }
        }
        else
        {
            for (var inlineIndex = 0; inlineIndex < source.Inlines.Count; inlineIndex++)
            {
                AppendInline(source.Inlines[inlineIndex], paragraph.Inlines);
            }
        }

        AppendFloatingObjects(source, paragraph);
        return paragraph;
    }

    private bool TryConvertBlockUiContainer(DocumentParagraph source, out Vibe.Office.FlowDocument.BlockUIContainer blockUiContainer)
    {
        blockUiContainer = null!;

        if (source.Inlines.Count == 1 && source.Inlines[0] is ShapeInline shape)
        {
            var resolved = ResolveEmbeddedUiChild(shape, false);
            if (resolved.IsMatch && !resolved.IsInline)
            {
                var container = new Vibe.Office.FlowDocument.BlockUIContainer
                {
                    Child = resolved.Child
                };
                ApplyParagraphProperties(source.Properties, container);
                blockUiContainer = container;
                return true;
            }
        }

        if (source.Inlines.Count == 1
            && source.Inlines[0] is RunInline placeholderRun
            && string.Equals(placeholderRun.GetText(), _options.BlockUiPlaceholderText, StringComparison.Ordinal))
        {
            var container = new Vibe.Office.FlowDocument.BlockUIContainer
            {
                Child = _options.BlockUiPlaceholderChild
            };
            ApplyParagraphProperties(source.Properties, container);
            blockUiContainer = container;
            return true;
        }

        return false;
    }

    private void AppendFloatingObjects(DocumentParagraph source, FlowParagraph target)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var index = 0; index < source.FloatingObjects.Count; index++)
        {
            var floating = source.FloatingObjects[index];
            if (floating.Content is not ShapeInline shape)
            {
                continue;
            }

            var anchored = ConvertFloatingObject(floating, shape);
            target.Inlines.Add(anchored);
        }
    }

    private FlowAnchoredBlock ConvertFloatingObject(FloatingObject source, ShapeInline shape)
    {
        FlowAnchoredBlock anchored = source.Anchor.HorizontalAlignment == FloatingHorizontalAlignment.Right
            ? new Vibe.Office.FlowDocument.Floater
            {
                Width = Math.Max(1f, shape.Width),
                HorizontalAlignment = source.Anchor.HorizontalAlignment.ToString()
            }
            : new Vibe.Office.FlowDocument.Figure
            {
                Width = Math.Max(1f, shape.Width),
                Height = Math.Max(1f, shape.Height),
                HorizontalAnchor = source.Anchor.HorizontalReference.ToString(),
                VerticalAnchor = source.Anchor.VerticalReference.ToString(),
                HorizontalOffset = source.Anchor.OffsetX,
                VerticalOffset = source.Anchor.OffsetY
            };

        anchored.Margin = ToFlowThickness(source.Anchor.Distance);

        if (shape.TextBox is { } textBox)
        {
            AppendBlocks(textBox.Blocks, anchored.Blocks);
        }

        if (anchored.Blocks.Count == 0)
        {
            anchored.Blocks.Add(new FlowParagraph());
        }

        return anchored;
    }

    private void AppendInline(DocumentInline source, FlowInlineCollection target)
    {
        if (source is RunInline runInline)
        {
            AppendRunInline(runInline, target);
            return;
        }

        if (source is ShapeInline shapeInline)
        {
            var resolved = ResolveEmbeddedUiChild(shapeInline, true);
            if (resolved.IsMatch)
            {
                target.Add(new Vibe.Office.FlowDocument.InlineUIContainer
                {
                    Child = resolved.Child
                });
                return;
            }

            target.Add(new Vibe.Office.FlowDocument.Run(_options.InlineUiPlaceholderText));
            return;
        }

        if (source is ImageInline)
        {
            target.Add(new Vibe.Office.FlowDocument.Run(_options.InlineUiPlaceholderText));
            return;
        }
    }

    private void AppendRunInline(RunInline source, FlowInlineCollection target)
    {
        var text = source.GetText();
        if (text.Length == 0)
        {
            return;
        }

        var parts = text.Split('\n');
        for (var index = 0; index < parts.Length; index++)
        {
            var segment = parts[index];
            if (segment.Length > 0)
            {
                var run = new Vibe.Office.FlowDocument.Run(segment);
                AddStyledInline(target, run, source.Style, source.Hyperlink);
            }

            if (index + 1 < parts.Length)
            {
                var lineBreak = new Vibe.Office.FlowDocument.LineBreak();
                AddStyledInline(target, lineBreak, source.Style, source.Hyperlink);
            }
        }
    }

    private static void AddStyledInline(
        FlowInlineCollection target,
        FlowInline leaf,
        TextStyleProperties? style,
        HyperlinkInfo? hyperlink)
    {
        FlowInline current = leaf;

        var styleSpan = CreateStyleSpan(style);
        if (styleSpan is not null)
        {
            styleSpan.Inlines.Add(current);
            current = styleSpan;
        }

        if (hyperlink is not null && !hyperlink.IsEmpty)
        {
            var link = new Vibe.Office.FlowDocument.Hyperlink();
            if (!string.IsNullOrWhiteSpace(hyperlink.Uri))
            {
                link.NavigateUri = hyperlink.Uri;
            }
            else if (!string.IsNullOrWhiteSpace(hyperlink.Anchor))
            {
                link.NavigateUri = $"#{hyperlink.Anchor}";
            }

            if (!string.IsNullOrWhiteSpace(hyperlink.Anchor))
            {
                link.TargetName = hyperlink.Anchor;
            }

            if (!string.IsNullOrWhiteSpace(hyperlink.Tooltip))
            {
                link.SetValue(Vibe.Office.FlowDocument.Hyperlink.ToolTipProperty, hyperlink.Tooltip);
            }

            link.Inlines.Add(current);
            current = link;
        }

        target.Add(current);
    }

    private static Vibe.Office.FlowDocument.Span? CreateStyleSpan(TextStyleProperties? style)
    {
        if (style is null || !style.HasValues)
        {
            return null;
        }

        var span = new Vibe.Office.FlowDocument.Span();
        var hasValues = false;

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            span.FontFamily = style.FontFamily;
            hasValues = true;
        }

        if (style.FontSize.HasValue && style.FontSize.Value > 0f)
        {
            span.FontSize = style.FontSize.Value;
            hasValues = true;
        }

        if (style.FontWeight.HasValue)
        {
            span.FontWeight = style.FontWeight == DocFontWeight.Bold
                ? Vibe.Office.FlowDocument.FlowFontWeight.Bold
                : Vibe.Office.FlowDocument.FlowFontWeight.Normal;
            hasValues = true;
        }

        if (style.FontStyle.HasValue)
        {
            span.FontStyle = style.FontStyle == DocFontStyle.Italic
                ? Vibe.Office.FlowDocument.FlowFontStyle.Italic
                : Vibe.Office.FlowDocument.FlowFontStyle.Normal;
            hasValues = true;
        }

        if (style.Color.HasValue)
        {
            span.Foreground = ToFlowColor(style.Color.Value);
            hasValues = true;
        }

        if (style.HighlightColor.HasValue)
        {
            span.Background = ToFlowColor(style.HighlightColor.Value);
            hasValues = true;
        }

        if (style.VerticalPosition.HasValue)
        {
            var baseline = style.VerticalPosition.Value switch
            {
                DocVerticalPosition.Superscript => Vibe.Office.FlowDocument.FlowBaselineAlignment.Superscript,
                DocVerticalPosition.Subscript => Vibe.Office.FlowDocument.FlowBaselineAlignment.Subscript,
                _ => Vibe.Office.FlowDocument.FlowBaselineAlignment.Baseline
            };
            if (baseline != Vibe.Office.FlowDocument.FlowBaselineAlignment.Baseline)
            {
                span.BaselineAlignment = baseline;
                hasValues = true;
            }
        }

        var decorations = ToFlowTextDecorations(style.Underline, style.UnderlineStyle, style.Strikethrough);
        if (decorations != Vibe.Office.FlowDocument.FlowTextDecorations.None)
        {
            span.TextDecorations = decorations;
            hasValues = true;
        }

        return hasValues ? span : null;
    }

    private static Vibe.Office.FlowDocument.FlowTextDecorations ToFlowTextDecorations(
        bool? underline,
        DocUnderlineStyle? underlineStyle,
        bool? strikethrough)
    {
        var result = Vibe.Office.FlowDocument.FlowTextDecorations.None;
        if (underline == true || (underlineStyle.HasValue && underlineStyle.Value != DocUnderlineStyle.None))
        {
            result |= Vibe.Office.FlowDocument.FlowTextDecorations.Underline;
        }

        if (strikethrough == true)
        {
            result |= Vibe.Office.FlowDocument.FlowTextDecorations.Strikethrough;
        }

        return result;
    }

    private EmbeddedUiResolution ResolveEmbeddedUiChild(ShapeInline shape, bool inlineFallback)
    {
        if (!FlowDocumentConverter.TryParseEmbeddedUiElementId(shape.Name, _options.EmbeddedUiShapePrefix, out var elementId))
        {
            return EmbeddedUiResolution.NoMatch;
        }

        if (_options.EmbeddedUiElementsById is not null
            && _options.EmbeddedUiElementsById.TryGetValue(elementId, out var element))
        {
            return new EmbeddedUiResolution(true, element.IsInline, element.Child);
        }

        var child = _options.EmbeddedUiElementFactory?.Invoke(elementId, inlineFallback);
        if (child is not null)
        {
            return new EmbeddedUiResolution(true, inlineFallback, child);
        }

        return new EmbeddedUiResolution(
            true,
            inlineFallback,
            inlineFallback ? _options.InlineUiPlaceholderChild : _options.BlockUiPlaceholderChild);
    }

    private static void ApplyParagraphProperties(ParagraphProperties source, FlowBlock target)
    {
        if (source.Alignment.HasValue)
        {
            target.TextAlignment = ToFlowAlignment(source.Alignment.Value);
        }

        var margin = new Vibe.Office.FlowDocument.FlowThickness(
            source.IndentLeft.GetValueOrDefault(0f),
            source.SpacingBefore.GetValueOrDefault(0f),
            source.IndentRight.GetValueOrDefault(0f),
            source.SpacingAfter.GetValueOrDefault(0f));
        if (!margin.IsEmpty)
        {
            target.Margin = margin;
        }

        if (source.LineSpacing.HasValue && source.LineSpacing.Value > 0)
        {
            target.LineHeight = source.LineSpacing.Value;
        }

        target.BreakPageBefore = source.PageBreakBefore;

        if (target is FlowParagraph paragraph)
        {
            paragraph.TextIndent = source.FirstLineIndent;
            paragraph.KeepWithNext = source.KeepWithNext;
            paragraph.KeepTogether = source.KeepLinesTogether;
        }
    }

    private static Vibe.Office.FlowDocument.FlowTextAlignment ToFlowAlignment(ParagraphAlignment alignment)
    {
        return alignment switch
        {
            ParagraphAlignment.Center => Vibe.Office.FlowDocument.FlowTextAlignment.Center,
            ParagraphAlignment.Right => Vibe.Office.FlowDocument.FlowTextAlignment.Right,
            ParagraphAlignment.Justify => Vibe.Office.FlowDocument.FlowTextAlignment.Justify,
            _ => Vibe.Office.FlowDocument.FlowTextAlignment.Left
        };
    }

    private Vibe.Office.FlowDocument.Table ConvertTable(TableBlock source)
    {
        var table = new Vibe.Office.FlowDocument.Table();
        if (source.Properties.CellSpacing.HasValue && source.Properties.CellSpacing.Value >= 0f)
        {
            table.CellSpacing = source.Properties.CellSpacing.Value;
        }

        if (source.Properties.ColumnWidths.Count > 0)
        {
            for (var columnIndex = 0; columnIndex < source.Properties.ColumnWidths.Count; columnIndex++)
            {
                var width = source.Properties.ColumnWidths[columnIndex];
                table.Columns.Add(new Vibe.Office.FlowDocument.TableColumn
                {
                    Width = width > 0f ? width : null
                });
            }
        }

        var group = new FlowTableRowGroup();
        table.RowGroups.Add(group);

        for (var rowIndex = 0; rowIndex < source.Rows.Count; rowIndex++)
        {
            var sourceRow = source.Rows[rowIndex];
            var row = new FlowTableRow();
            group.Rows.Add(row);

            var column = Math.Max(0, sourceRow.Properties.GridBefore ?? 0);
            for (var cellIndex = 0; cellIndex < sourceRow.Cells.Count; cellIndex++)
            {
                var sourceCell = sourceRow.Cells[cellIndex];
                var columnSpan = Math.Max(1, sourceCell.ColumnSpan);

                if (sourceCell.VerticalMerge == TableCellVerticalMerge.Continue)
                {
                    column += columnSpan;
                    continue;
                }

                var cell = new FlowTableCell
                {
                    ColumnSpan = columnSpan,
                    RowSpan = ComputeRowSpan(source.Rows, rowIndex, column, columnSpan)
                };

                ApplyTableCellProperties(sourceCell, cell);
                AppendBlocks(sourceCell.Blocks, cell.Blocks);
                if (cell.Blocks.Count == 0)
                {
                    cell.Blocks.Add(new FlowParagraph());
                }

                row.Cells.Add(cell);
                column += columnSpan;
            }
        }

        return table;
    }

    private static int ComputeRowSpan(IReadOnlyList<DocumentTableRow> rows, int rowIndex, int column, int columnSpan)
    {
        var rowSpan = 1;
        for (var nextRow = rowIndex + 1; nextRow < rows.Count; nextRow++)
        {
            if (!TryGetCellAtColumn(rows[nextRow], column, out var cell, out var startColumn))
            {
                break;
            }

            if (startColumn != column
                || Math.Max(1, cell.ColumnSpan) != columnSpan
                || cell.VerticalMerge != TableCellVerticalMerge.Continue)
            {
                break;
            }

            rowSpan++;
        }

        return rowSpan;
    }

    private static bool TryGetCellAtColumn(DocumentTableRow row, int targetColumn, out DocumentTableCell cell, out int startColumn)
    {
        var column = Math.Max(0, row.Properties.GridBefore ?? 0);
        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
        {
            var current = row.Cells[cellIndex];
            var span = Math.Max(1, current.ColumnSpan);
            if (targetColumn >= column && targetColumn < column + span)
            {
                cell = current;
                startColumn = column;
                return true;
            }

            column += span;
        }

        cell = null!;
        startColumn = -1;
        return false;
    }

    private static void ApplyTableCellProperties(DocumentTableCell source, FlowTableCell target)
    {
        if (source.Properties.Padding.HasValue)
        {
            target.Padding = ToFlowThickness(source.Properties.Padding.Value);
        }

        var borderThickness = new Vibe.Office.FlowDocument.FlowThickness(
            source.Properties.Borders.Left?.Thickness ?? 0f,
            source.Properties.Borders.Top?.Thickness ?? 0f,
            source.Properties.Borders.Right?.Thickness ?? 0f,
            source.Properties.Borders.Bottom?.Thickness ?? 0f);
        if (!borderThickness.IsEmpty)
        {
            target.BorderThickness = borderThickness;
        }

        var borderColor = source.Properties.Borders.Left?.Color
                          ?? source.Properties.Borders.Top?.Color
                          ?? source.Properties.Borders.Right?.Color
                          ?? source.Properties.Borders.Bottom?.Color;
        if (borderColor.HasValue)
        {
            target.BorderBrush = ToFlowColor(borderColor.Value);
        }

        var alignment = InferCellTextAlignment(source.Blocks);
        if (alignment.HasValue)
        {
            target.TextAlignment = alignment.Value;
        }
    }

    private static Vibe.Office.FlowDocument.FlowTextAlignment? InferCellTextAlignment(IReadOnlyList<DocumentBlock> blocks)
    {
        Vibe.Office.FlowDocument.FlowTextAlignment? result = null;
        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not DocumentParagraph paragraph || !paragraph.Properties.Alignment.HasValue)
            {
                continue;
            }

            var current = ToFlowAlignment(paragraph.Properties.Alignment.Value);
            if (!result.HasValue)
            {
                result = current;
                continue;
            }

            if (result.Value != current)
            {
                return null;
            }
        }

        return result;
    }

    private static Vibe.Office.FlowDocument.FlowThickness ToFlowThickness(DocThickness value)
    {
        return new Vibe.Office.FlowDocument.FlowThickness(value.Left, value.Top, value.Right, value.Bottom);
    }

    private static string ToFlowColor(DocColor color)
    {
        return color.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private sealed class ListContext
    {
        public ListContext(FlowList list, ListInfo info)
        {
            List = list;
            Info = info;
        }

        public FlowList List { get; }

        public ListInfo Info { get; }

        public FlowListItem? LastItem { get; set; }
    }

    private readonly record struct EmbeddedUiResolution(bool IsMatch, bool IsInline, object? Child)
    {
        public static EmbeddedUiResolution NoMatch => new(false, false, null);
    }
}
