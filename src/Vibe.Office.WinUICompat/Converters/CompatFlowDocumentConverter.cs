using FlowBlock = Vibe.Office.FlowDocument.Block;
using FlowBold = Vibe.Office.FlowDocument.Bold;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;
using FlowHyperlink = Vibe.Office.FlowDocument.Hyperlink;
using FlowInline = Vibe.Office.FlowDocument.Inline;
using FlowInlineUiContainer = Vibe.Office.FlowDocument.InlineUIContainer;
using FlowAnchoredBlock = Vibe.Office.FlowDocument.AnchoredBlock;
using FlowFigure = Vibe.Office.FlowDocument.Figure;
using FlowFloater = Vibe.Office.FlowDocument.Floater;
using FlowItalic = Vibe.Office.FlowDocument.Italic;
using FlowLineBreak = Vibe.Office.FlowDocument.LineBreak;
using FlowList = Vibe.Office.FlowDocument.List;
using FlowListItem = Vibe.Office.FlowDocument.ListItem;
using FlowSection = Vibe.Office.FlowDocument.Section;
using FlowParagraph = Vibe.Office.FlowDocument.Paragraph;
using FlowRun = Vibe.Office.FlowDocument.Run;
using FlowSpan = Vibe.Office.FlowDocument.Span;
using FlowUnderline = Vibe.Office.FlowDocument.Underline;
using FlowBlockUiContainer = Vibe.Office.FlowDocument.BlockUIContainer;
using FlowTable = Vibe.Office.FlowDocument.Table;
using FlowTableCell = Vibe.Office.FlowDocument.TableCell;
using FlowTableColumn = Vibe.Office.FlowDocument.TableColumn;
using FlowTableRow = Vibe.Office.FlowDocument.TableRow;
using FlowTableRowGroup = Vibe.Office.FlowDocument.TableRowGroup;
using FlowFlowThickness = Vibe.Office.FlowDocument.FlowThickness;
using CompatBlock = Vibe.Office.WinUICompat.Documents.Block;
using CompatInline = Vibe.Office.WinUICompat.Documents.Inline;
using CompatList = Vibe.Office.WinUICompat.Documents.List;
using CompatListItem = Vibe.Office.WinUICompat.Documents.ListItem;
using CompatSection = Vibe.Office.WinUICompat.Documents.Section;
using CompatParagraph = Vibe.Office.WinUICompat.Documents.Paragraph;
using CompatRun = Vibe.Office.WinUICompat.Documents.Run;
using CompatSpan = Vibe.Office.WinUICompat.Documents.Span;
using CompatBold = Vibe.Office.WinUICompat.Documents.Bold;
using CompatItalic = Vibe.Office.WinUICompat.Documents.Italic;
using CompatUnderline = Vibe.Office.WinUICompat.Documents.Underline;
using CompatHyperlink = Vibe.Office.WinUICompat.Documents.Hyperlink;
using CompatLineBreak = Vibe.Office.WinUICompat.Documents.LineBreak;
using CompatInlineUiContainer = Vibe.Office.WinUICompat.Documents.InlineUIContainer;
using CompatAnchoredBlock = Vibe.Office.WinUICompat.Documents.AnchoredBlock;
using CompatFigure = Vibe.Office.WinUICompat.Documents.Figure;
using CompatFloater = Vibe.Office.WinUICompat.Documents.Floater;
using CompatBlockUiContainer = Vibe.Office.WinUICompat.Documents.BlockUIContainer;
using CompatTable = Vibe.Office.WinUICompat.Documents.Table;
using CompatTableColumn = Vibe.Office.WinUICompat.Documents.TableColumn;
using CompatTableRowGroup = Vibe.Office.WinUICompat.Documents.TableRowGroup;
using CompatTableRow = Vibe.Office.WinUICompat.Documents.TableRow;
using CompatTableCell = Vibe.Office.WinUICompat.Documents.TableCell;
using CompatDocument = Vibe.Office.WinUICompat.Documents.RichTextDocument;
using CompatThickness = Vibe.Office.WinUICompat.Documents.Thickness;
using FlowListMarkerStyle = Vibe.Office.FlowDocument.FlowListMarkerStyle;

namespace Vibe.Office.WinUICompat.Converters;

public sealed class CompatFlowDocumentConverter
{
    public FlowDocumentModel ToFlowDocument(CompatDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var flow = new FlowDocumentModel();
        flow.Blocks.Clear();
        ApplyDocumentToFlow(source, flow);

        for (var i = 0; i < source.Blocks.Count; i++)
        {
            flow.Blocks.Add(ConvertBlockToFlow(source.Blocks[i]));
        }

        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new FlowParagraph());
        }

        return flow;
    }

    public CompatDocument FromFlowDocument(FlowDocumentModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var compat = new CompatDocument();
        ApplyFlowToDocument(source, compat);
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            compat.Blocks.Add(ConvertBlockFromFlow(source.Blocks[i]));
        }

        if (compat.Blocks.Count == 0)
        {
            compat.Blocks.Add(new CompatParagraph());
        }

        return compat;
    }

    private static void ApplyDocumentToFlow(CompatDocument source, FlowDocumentModel target)
    {
        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && ParseTextAlignment(source.TextAlignment) is { } alignment)
        {
            target.TextAlignment = alignment;
        }

        if (source.PageWidth.HasValue)
        {
            target.PageWidth = source.PageWidth;
        }

        if (source.PageHeight.HasValue)
        {
            target.PageHeight = source.PageHeight;
        }

        if (source.PagePadding.HasValue && !source.PagePadding.Value.IsEmpty)
        {
            target.PagePadding = ToFlowThickness(source.PagePadding.Value);
        }

        if (source.ColumnWidth.HasValue)
        {
            target.ColumnWidth = source.ColumnWidth;
        }

        if (source.ColumnGap.HasValue)
        {
            target.ColumnGap = source.ColumnGap;
        }

        if (!string.IsNullOrWhiteSpace(source.ColumnRuleBrush))
        {
            target.ColumnRuleBrush = source.ColumnRuleBrush;
        }

        if (source.ColumnRuleWidth.HasValue)
        {
            target.ColumnRuleWidth = source.ColumnRuleWidth;
        }

        if (!string.IsNullOrWhiteSpace(source.FlowDirection))
        {
            target.FlowDirection = source.FlowDirection;
        }

        if (source.IsColumnWidthFlexible.HasValue)
        {
            target.IsColumnWidthFlexible = source.IsColumnWidthFlexible;
        }

        if (source.IsHyphenationEnabled.HasValue)
        {
            target.IsHyphenationEnabled = source.IsHyphenationEnabled;
        }

        if (source.IsOptimalParagraphEnabled.HasValue)
        {
            target.IsOptimalParagraphEnabled = source.IsOptimalParagraphEnabled;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight;
        }

        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy))
        {
            target.LineStackingStrategy = source.LineStackingStrategy;
        }

        if (source.MaxPageHeight.HasValue)
        {
            target.MaxPageHeight = source.MaxPageHeight;
        }

        if (source.MaxPageWidth.HasValue)
        {
            target.MaxPageWidth = source.MaxPageWidth;
        }

        if (source.MinPageHeight.HasValue)
        {
            target.MinPageHeight = source.MinPageHeight;
        }

        if (source.MinPageWidth.HasValue)
        {
            target.MinPageWidth = source.MinPageWidth;
        }

        ApplyTextStyleToFlow(source, target);
    }

    private static void ApplyFlowToDocument(FlowDocumentModel source, CompatDocument target)
    {
        target.TextAlignment = source.TextAlignment?.ToString();
        target.PageWidth = source.PageWidth;
        target.PageHeight = source.PageHeight;
        target.PagePadding = source.PagePadding.IsEmpty ? null : ToCompatThickness(source.PagePadding);
        target.ColumnWidth = source.ColumnWidth;
        target.ColumnGap = source.ColumnGap;
        target.ColumnRuleBrush = source.ColumnRuleBrush;
        target.ColumnRuleWidth = source.ColumnRuleWidth;
        target.FlowDirection = source.FlowDirection;
        target.IsColumnWidthFlexible = source.IsColumnWidthFlexible;
        target.IsHyphenationEnabled = source.IsHyphenationEnabled;
        target.IsOptimalParagraphEnabled = source.IsOptimalParagraphEnabled;
        target.LineHeight = source.LineHeight;
        target.LineStackingStrategy = source.LineStackingStrategy;
        target.MaxPageHeight = source.MaxPageHeight;
        target.MaxPageWidth = source.MaxPageWidth;
        target.MinPageHeight = source.MinPageHeight;
        target.MinPageWidth = source.MinPageWidth;
        ApplyFlowTextStyle(source, target);
    }

    private FlowBlock ConvertBlockToFlow(CompatBlock block)
    {
        FlowBlock mapped = block switch
        {
            CompatSection section => ConvertSectionToFlow(section),
            CompatParagraph paragraph => ConvertParagraphToFlow(paragraph),
            CompatList list => ConvertListToFlow(list),
            CompatTable table => ConvertTableToFlow(table),
            CompatBlockUiContainer container => new FlowBlockUiContainer { Child = container.Child },
            _ => new FlowParagraph()
        };

        ApplyBlockToFlow(block, mapped);
        return mapped;
    }

    private CompatBlock ConvertBlockFromFlow(FlowBlock block)
    {
        CompatBlock mapped = block switch
        {
            FlowSection section => ConvertSectionFromFlow(section),
            FlowParagraph paragraph => ConvertParagraphFromFlow(paragraph),
            FlowList list => ConvertListFromFlow(list),
            FlowTable table => ConvertTableFromFlow(table),
            FlowBlockUiContainer container => new CompatBlockUiContainer { Child = container.Child },
            _ => new CompatParagraph()
        };

        ApplyFlowToBlock(block, mapped);
        return mapped;
    }

    private FlowSection ConvertSectionToFlow(CompatSection source)
    {
        var target = new FlowSection
        {
            HasTrailingParagraphBreakOnPaste = source.HasTrailingParagraphBreakOnPaste
        };

        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockToFlow(source.Blocks[i]));
        }

        return target;
    }

    private CompatSection ConvertSectionFromFlow(FlowSection source)
    {
        var target = new CompatSection
        {
            HasTrailingParagraphBreakOnPaste = source.HasTrailingParagraphBreakOnPaste
        };

        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockFromFlow(source.Blocks[i]));
        }

        return target;
    }

    private FlowParagraph ConvertParagraphToFlow(CompatParagraph paragraph)
    {
        var flowParagraph = new FlowParagraph();
        if (paragraph.TextIndent.HasValue)
        {
            flowParagraph.TextIndent = paragraph.TextIndent;
        }

        if (paragraph.KeepTogether.HasValue)
        {
            flowParagraph.KeepTogether = paragraph.KeepTogether;
        }

        if (paragraph.KeepWithNext.HasValue)
        {
            flowParagraph.KeepWithNext = paragraph.KeepWithNext;
        }

        if (paragraph.MinOrphanLines.HasValue)
        {
            flowParagraph.MinOrphanLines = paragraph.MinOrphanLines;
        }

        if (paragraph.MinWidowLines.HasValue)
        {
            flowParagraph.MinWidowLines = paragraph.MinWidowLines;
        }

        if (!string.IsNullOrWhiteSpace(paragraph.TextDecorations)
            && ParseTextDecorations(paragraph.TextDecorations) is { } decorations)
        {
            flowParagraph.TextDecorations = decorations;
        }

        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            flowParagraph.Inlines.Add(ConvertInlineToFlow(paragraph.Inlines[i]));
        }

        return flowParagraph;
    }

    private CompatParagraph ConvertParagraphFromFlow(FlowParagraph paragraph)
    {
        var compat = new CompatParagraph
        {
            TextIndent = paragraph.TextIndent,
            KeepTogether = paragraph.KeepTogether,
            KeepWithNext = paragraph.KeepWithNext,
            MinOrphanLines = paragraph.MinOrphanLines,
            MinWidowLines = paragraph.MinWidowLines,
            TextDecorations = paragraph.TextDecorations?.ToString()
        };

        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            compat.Inlines.Add(ConvertInlineFromFlow(paragraph.Inlines[i]));
        }

        return compat;
    }

    private FlowInline ConvertInlineToFlow(CompatInline inline)
    {
        FlowInline mapped = inline switch
        {
            CompatRun run => new FlowRun(run.Text),
            CompatBold bold => ConvertSpanToFlow(bold, new FlowBold()),
            CompatItalic italic => ConvertSpanToFlow(italic, new FlowItalic()),
            CompatUnderline underline => ConvertSpanToFlow(underline, new FlowUnderline()),
            CompatHyperlink hyperlink => ConvertHyperlinkToFlow(hyperlink),
            CompatSpan span => ConvertSpanToFlow(span, new FlowSpan()),
            CompatFigure figure => ConvertFigureToFlow(figure),
            CompatFloater floater => ConvertFloaterToFlow(floater),
            CompatLineBreak => new FlowLineBreak(),
            CompatInlineUiContainer container => new FlowInlineUiContainer { Child = container.Child },
            _ => new FlowRun(string.Empty)
        };

        ApplyInlineToFlow(inline, mapped);
        return mapped;
    }

    private CompatInline ConvertInlineFromFlow(FlowInline inline)
    {
        CompatInline mapped = inline switch
        {
            FlowRun run => new CompatRun(run.Text),
            FlowBold bold => ConvertSpanFromFlow(bold, new CompatBold()),
            FlowItalic italic => ConvertSpanFromFlow(italic, new CompatItalic()),
            FlowUnderline underline => ConvertSpanFromFlow(underline, new CompatUnderline()),
            FlowHyperlink hyperlink => ConvertHyperlinkFromFlow(hyperlink),
            FlowSpan span => ConvertSpanFromFlow(span, new CompatSpan()),
            FlowFigure figure => ConvertFigureFromFlow(figure),
            FlowFloater floater => ConvertFloaterFromFlow(floater),
            FlowLineBreak => new CompatLineBreak(),
            FlowInlineUiContainer container => new CompatInlineUiContainer { Child = container.Child },
            _ => new CompatRun(string.Empty)
        };

        ApplyFlowToInline(inline, mapped);
        return mapped;
    }

    private FlowSpan ConvertSpanToFlow(CompatSpan source, FlowSpan target)
    {
        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(ConvertInlineToFlow(source.Inlines[i]));
        }

        return target;
    }

    private CompatSpan ConvertSpanFromFlow(FlowSpan source, CompatSpan target)
    {
        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(ConvertInlineFromFlow(source.Inlines[i]));
        }

        return target;
    }

    private FlowHyperlink ConvertHyperlinkToFlow(CompatHyperlink source)
    {
        var target = new FlowHyperlink
        {
            NavigateUri = source.NavigateUri,
            TargetName = source.TargetName,
            Command = source.Command,
            CommandParameter = source.CommandParameter,
            CommandTarget = source.CommandTarget
        };

        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(ConvertInlineToFlow(source.Inlines[i]));
        }

        return target;
    }

    private CompatHyperlink ConvertHyperlinkFromFlow(FlowHyperlink source)
    {
        var target = new CompatHyperlink
        {
            NavigateUri = source.NavigateUri,
            TargetName = source.TargetName,
            Command = source.Command,
            CommandParameter = source.CommandParameter,
            CommandTarget = source.CommandTarget
        };

        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(ConvertInlineFromFlow(source.Inlines[i]));
        }

        return target;
    }

    private FlowFigure ConvertFigureToFlow(CompatFigure source)
    {
        var target = new FlowFigure
        {
            Width = source.Width,
            Height = source.Height,
            HorizontalAnchor = source.HorizontalAnchor,
            VerticalAnchor = source.VerticalAnchor,
            HorizontalOffset = source.HorizontalOffset,
            VerticalOffset = source.VerticalOffset,
            CanDelayPlacement = source.CanDelayPlacement,
            WrapDirection = source.WrapDirection
        };
        ConvertAnchoredBlocksToFlow(source, target);
        return target;
    }

    private CompatFigure ConvertFigureFromFlow(FlowFigure source)
    {
        var target = new CompatFigure
        {
            Width = source.Width,
            Height = source.Height,
            HorizontalAnchor = source.HorizontalAnchor,
            VerticalAnchor = source.VerticalAnchor,
            HorizontalOffset = source.HorizontalOffset,
            VerticalOffset = source.VerticalOffset,
            CanDelayPlacement = source.CanDelayPlacement,
            WrapDirection = source.WrapDirection
        };
        ConvertAnchoredBlocksFromFlow(source, target);
        return target;
    }

    private FlowFloater ConvertFloaterToFlow(CompatFloater source)
    {
        var target = new FlowFloater
        {
            Width = source.Width,
            HorizontalAlignment = source.HorizontalAlignment
        };
        ConvertAnchoredBlocksToFlow(source, target);
        return target;
    }

    private CompatFloater ConvertFloaterFromFlow(FlowFloater source)
    {
        var target = new CompatFloater
        {
            Width = source.Width,
            HorizontalAlignment = source.HorizontalAlignment
        };
        ConvertAnchoredBlocksFromFlow(source, target);
        return target;
    }

    private void ConvertAnchoredBlocksToFlow(CompatAnchoredBlock source, FlowAnchoredBlock target)
    {
        ApplyAnchoredBlockToFlow(source, target);
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockToFlow(source.Blocks[i]));
        }
    }

    private void ConvertAnchoredBlocksFromFlow(FlowAnchoredBlock source, CompatAnchoredBlock target)
    {
        ApplyFlowToAnchoredBlock(source, target);
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockFromFlow(source.Blocks[i]));
        }
    }

    private FlowList ConvertListToFlow(CompatList source)
    {
        var target = new FlowList
        {
            MarkerStyle = ParseMarkerStyle(source.MarkerStyle),
            StartIndex = source.StartIndex,
            MarkerOffset = source.MarkerOffset
        };

        for (var i = 0; i < source.ListItems.Count; i++)
        {
            target.ListItems.Add(ConvertListItemToFlow(source.ListItems[i]));
        }

        return target;
    }

    private CompatList ConvertListFromFlow(FlowList source)
    {
        var target = new CompatList
        {
            MarkerStyle = source.MarkerStyle.ToString(),
            StartIndex = source.StartIndex,
            MarkerOffset = source.MarkerOffset
        };

        for (var i = 0; i < source.ListItems.Count; i++)
        {
            target.ListItems.Add(ConvertListItemFromFlow(source.ListItems[i]));
        }

        return target;
    }

    private FlowListItem ConvertListItemToFlow(CompatListItem source)
    {
        var target = new FlowListItem();
        ApplyListItemToFlow(source, target);
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockToFlow(source.Blocks[i]));
        }

        return target;
    }

    private CompatListItem ConvertListItemFromFlow(FlowListItem source)
    {
        var target = new CompatListItem();
        ApplyFlowToListItem(source, target);
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            target.Blocks.Add(ConvertBlockFromFlow(source.Blocks[i]));
        }

        return target;
    }

    private FlowTable ConvertTableToFlow(CompatTable source)
    {
        var table = new FlowTable
        {
            CellSpacing = source.CellSpacing
        };

        for (var i = 0; i < source.Columns.Count; i++)
        {
            var column = source.Columns[i];
            table.Columns.Add(new FlowTableColumn
            {
                Width = column.Width,
                Background = column.Background
            });
        }

        for (var i = 0; i < source.RowGroups.Count; i++)
        {
            table.RowGroups.Add(ConvertRowGroupToFlow(source.RowGroups[i]));
        }

        return table;
    }

    private CompatTable ConvertTableFromFlow(FlowTable source)
    {
        var table = new CompatTable
        {
            CellSpacing = source.CellSpacing
        };

        for (var i = 0; i < source.Columns.Count; i++)
        {
            var column = source.Columns[i];
            table.Columns.Add(new CompatTableColumn
            {
                Width = column.Width,
                Background = column.Background
            });
        }

        for (var i = 0; i < source.RowGroups.Count; i++)
        {
            table.RowGroups.Add(ConvertRowGroupFromFlow(source.RowGroups[i]));
        }

        return table;
    }

    private FlowTableRowGroup ConvertRowGroupToFlow(CompatTableRowGroup source)
    {
        var rowGroup = new FlowTableRowGroup();
        for (var i = 0; i < source.Rows.Count; i++)
        {
            rowGroup.Rows.Add(ConvertRowToFlow(source.Rows[i]));
        }

        return rowGroup;
    }

    private CompatTableRowGroup ConvertRowGroupFromFlow(FlowTableRowGroup source)
    {
        var rowGroup = new CompatTableRowGroup();
        for (var i = 0; i < source.Rows.Count; i++)
        {
            rowGroup.Rows.Add(ConvertRowFromFlow(source.Rows[i]));
        }

        return rowGroup;
    }

    private FlowTableRow ConvertRowToFlow(CompatTableRow source)
    {
        var row = new FlowTableRow();
        for (var i = 0; i < source.Cells.Count; i++)
        {
            row.Cells.Add(ConvertCellToFlow(source.Cells[i]));
        }

        return row;
    }

    private CompatTableRow ConvertRowFromFlow(FlowTableRow source)
    {
        var row = new CompatTableRow();
        for (var i = 0; i < source.Cells.Count; i++)
        {
            row.Cells.Add(ConvertCellFromFlow(source.Cells[i]));
        }

        return row;
    }

    private FlowTableCell ConvertCellToFlow(CompatTableCell source)
    {
        var cell = new FlowTableCell
        {
            ColumnSpan = Math.Max(1, source.ColumnSpan),
            RowSpan = Math.Max(1, source.RowSpan)
        };
        ApplyTableCellToFlow(source, cell);

        for (var i = 0; i < source.Blocks.Count; i++)
        {
            cell.Blocks.Add(ConvertBlockToFlow(source.Blocks[i]));
        }

        return cell;
    }

    private CompatTableCell ConvertCellFromFlow(FlowTableCell source)
    {
        var cell = new CompatTableCell
        {
            ColumnSpan = source.ColumnSpan,
            RowSpan = source.RowSpan
        };
        ApplyFlowToTableCell(source, cell);

        for (var i = 0; i < source.Blocks.Count; i++)
        {
            cell.Blocks.Add(ConvertBlockFromFlow(source.Blocks[i]));
        }

        return cell;
    }

    private static void ApplyBlockToFlow(CompatBlock source, FlowBlock target)
    {
        if (source.Margin.HasValue && !source.Margin.Value.IsEmpty)
        {
            target.Margin = ToFlowThickness(source.Margin.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && ParseTextAlignment(source.TextAlignment) is { } alignment)
        {
            target.TextAlignment = alignment;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight;
        }

        if (source.Padding.HasValue && !source.Padding.Value.IsEmpty)
        {
            target.Padding = ToFlowThickness(source.Padding.Value);
        }

        if (source.BorderThickness.HasValue && !source.BorderThickness.Value.IsEmpty)
        {
            target.BorderThickness = ToFlowThickness(source.BorderThickness.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.BorderBrush))
        {
            target.BorderBrush = source.BorderBrush;
        }

        if (!string.IsNullOrWhiteSpace(source.FlowDirection))
        {
            target.FlowDirection = source.FlowDirection;
        }

        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy))
        {
            target.LineStackingStrategy = source.LineStackingStrategy;
        }

        if (source.BreakColumnBefore.HasValue)
        {
            target.BreakColumnBefore = source.BreakColumnBefore;
        }

        if (source.BreakPageBefore.HasValue)
        {
            target.BreakPageBefore = source.BreakPageBefore;
        }

        if (!string.IsNullOrWhiteSpace(source.ClearFloaters))
        {
            target.ClearFloaters = source.ClearFloaters;
        }

        if (source.IsHyphenationEnabled.HasValue)
        {
            target.IsHyphenationEnabled = source.IsHyphenationEnabled;
        }

        ApplyTextElementToFlow(source, target);
    }

    private static void ApplyFlowToBlock(FlowBlock source, CompatBlock target)
    {
        target.Margin = source.Margin.IsEmpty ? null : ToCompatThickness(source.Margin);
        target.TextAlignment = source.TextAlignment?.ToString();
        target.LineHeight = source.LineHeight;
        target.Padding = source.Padding.IsEmpty ? null : ToCompatThickness(source.Padding);
        target.BorderThickness = source.BorderThickness.IsEmpty ? null : ToCompatThickness(source.BorderThickness);
        target.BorderBrush = source.BorderBrush;
        target.FlowDirection = source.FlowDirection;
        target.LineStackingStrategy = source.LineStackingStrategy;
        target.BreakColumnBefore = source.BreakColumnBefore;
        target.BreakPageBefore = source.BreakPageBefore;
        target.ClearFloaters = source.ClearFloaters;
        target.IsHyphenationEnabled = source.IsHyphenationEnabled;
        ApplyFlowTextElement(source, target);
    }

    private static void ApplyListItemToFlow(CompatListItem source, FlowListItem target)
    {
        if (source.Margin.HasValue && !source.Margin.Value.IsEmpty)
        {
            target.Margin = ToFlowThickness(source.Margin.Value);
        }

        if (source.Padding.HasValue && !source.Padding.Value.IsEmpty)
        {
            target.Padding = ToFlowThickness(source.Padding.Value);
        }

        if (source.BorderThickness.HasValue && !source.BorderThickness.Value.IsEmpty)
        {
            target.BorderThickness = ToFlowThickness(source.BorderThickness.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.BorderBrush))
        {
            target.BorderBrush = source.BorderBrush;
        }

        if (!string.IsNullOrWhiteSpace(source.FlowDirection))
        {
            target.FlowDirection = source.FlowDirection;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight;
        }

        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy))
        {
            target.LineStackingStrategy = source.LineStackingStrategy;
        }

        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && ParseTextAlignment(source.TextAlignment) is { } alignment)
        {
            target.TextAlignment = alignment;
        }

        ApplyTextElementToFlow(source, target);
    }

    private static void ApplyFlowToListItem(FlowListItem source, CompatListItem target)
    {
        target.Margin = source.Margin.IsEmpty ? null : ToCompatThickness(source.Margin);
        target.Padding = source.Padding.IsEmpty ? null : ToCompatThickness(source.Padding);
        target.BorderThickness = source.BorderThickness.IsEmpty ? null : ToCompatThickness(source.BorderThickness);
        target.BorderBrush = source.BorderBrush;
        target.FlowDirection = source.FlowDirection;
        target.LineHeight = source.LineHeight;
        target.LineStackingStrategy = source.LineStackingStrategy;
        target.TextAlignment = source.TextAlignment?.ToString();
        ApplyFlowTextElement(source, target);
    }

    private static void ApplyTableCellToFlow(CompatTableCell source, FlowTableCell target)
    {
        if (source.Padding.HasValue && !source.Padding.Value.IsEmpty)
        {
            target.Padding = ToFlowThickness(source.Padding.Value);
        }

        if (source.BorderThickness.HasValue && !source.BorderThickness.Value.IsEmpty)
        {
            target.BorderThickness = ToFlowThickness(source.BorderThickness.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.BorderBrush))
        {
            target.BorderBrush = source.BorderBrush;
        }

        if (!string.IsNullOrWhiteSpace(source.FlowDirection))
        {
            target.FlowDirection = source.FlowDirection;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight;
        }

        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy))
        {
            target.LineStackingStrategy = source.LineStackingStrategy;
        }

        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && ParseTextAlignment(source.TextAlignment) is { } alignment)
        {
            target.TextAlignment = alignment;
        }

        ApplyTextElementToFlow(source, target);
    }

    private static void ApplyFlowToTableCell(FlowTableCell source, CompatTableCell target)
    {
        target.Padding = source.Padding.IsEmpty ? null : ToCompatThickness(source.Padding);
        target.BorderThickness = source.BorderThickness.IsEmpty ? null : ToCompatThickness(source.BorderThickness);
        target.BorderBrush = source.BorderBrush;
        target.FlowDirection = source.FlowDirection;
        target.LineHeight = source.LineHeight;
        target.LineStackingStrategy = source.LineStackingStrategy;
        target.TextAlignment = source.TextAlignment?.ToString();
        ApplyFlowTextElement(source, target);
    }

    private static void ApplyAnchoredBlockToFlow(CompatAnchoredBlock source, FlowAnchoredBlock target)
    {
        if (source.Margin.HasValue && !source.Margin.Value.IsEmpty)
        {
            target.Margin = ToFlowThickness(source.Margin.Value);
        }

        if (source.Padding.HasValue && !source.Padding.Value.IsEmpty)
        {
            target.Padding = ToFlowThickness(source.Padding.Value);
        }

        if (source.BorderThickness.HasValue && !source.BorderThickness.Value.IsEmpty)
        {
            target.BorderThickness = ToFlowThickness(source.BorderThickness.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.BorderBrush))
        {
            target.BorderBrush = source.BorderBrush;
        }

        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && ParseTextAlignment(source.TextAlignment) is { } alignment)
        {
            target.TextAlignment = alignment;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight;
        }

        if (!string.IsNullOrWhiteSpace(source.LineStackingStrategy))
        {
            target.LineStackingStrategy = source.LineStackingStrategy;
        }
    }

    private static void ApplyFlowToAnchoredBlock(FlowAnchoredBlock source, CompatAnchoredBlock target)
    {
        target.Margin = source.Margin.IsEmpty ? null : ToCompatThickness(source.Margin);
        target.Padding = source.Padding.IsEmpty ? null : ToCompatThickness(source.Padding);
        target.BorderThickness = source.BorderThickness.IsEmpty ? null : ToCompatThickness(source.BorderThickness);
        target.BorderBrush = source.BorderBrush;
        target.TextAlignment = source.TextAlignment?.ToString();
        target.LineHeight = source.LineHeight;
        target.LineStackingStrategy = source.LineStackingStrategy;
    }

    private static void ApplyInlineToFlow(CompatInline source, FlowInline target)
    {
        if (!string.IsNullOrWhiteSpace(source.BaselineAlignment)
            && ParseBaselineAlignment(source.BaselineAlignment) is { } baselineAlignment)
        {
            target.BaselineAlignment = baselineAlignment;
        }

        if (!string.IsNullOrWhiteSpace(source.TextDecorations)
            && ParseTextDecorations(source.TextDecorations) is { } decorations)
        {
            target.TextDecorations = decorations;
        }

        if (!string.IsNullOrWhiteSpace(source.FlowDirection))
        {
            target.FlowDirection = source.FlowDirection;
        }

        ApplyTextElementToFlow(source, target);
    }

    private static void ApplyFlowToInline(FlowInline source, CompatInline target)
    {
        target.BaselineAlignment = source.BaselineAlignment?.ToString();
        target.TextDecorations = source.TextDecorations?.ToString();
        target.FlowDirection = source.FlowDirection;
        ApplyFlowTextElement(source, target);
    }

    private static void ApplyTextElementToFlow(Vibe.Office.WinUICompat.Documents.TextElement source, Vibe.Office.FlowDocument.TextElement target)
    {
        ApplyTextStyleToFlow(
            source.FontFamily,
            source.FontSize,
            source.FontWeight,
            source.FontStyle,
            source.FontStretch,
            source.Foreground,
            source.Background,
            source.TextEffects,
            source.Typography,
            target);
    }

    private static void ApplyFlowTextElement(Vibe.Office.FlowDocument.TextElement source, Vibe.Office.WinUICompat.Documents.TextElement target)
    {
        ApplyFlowTextStyle(
            source.FontFamily,
            source.FontSize,
            source.FontWeight?.ToString(),
            source.FontStyle?.ToString(),
            source.FontStretch,
            source.Foreground,
            source.Background,
            source.TextEffects,
            source.Typography,
            target);
    }

    private static void ApplyTextStyleToFlow(
        CompatDocument source,
        FlowDocumentModel target)
    {
        ApplyTextStyleToFlow(
            source.FontFamily,
            source.FontSize,
            source.FontWeight,
            source.FontStyle,
            source.FontStretch,
            source.Foreground,
            source.Background,
            source.TextEffects,
            source.Typography,
            target);
    }

    private static void ApplyFlowTextStyle(FlowDocumentModel source, CompatDocument target)
    {
        ApplyFlowTextStyle(
            source.FontFamily,
            source.FontSize,
            source.FontWeight?.ToString(),
            source.FontStyle?.ToString(),
            source.FontStretch,
            source.Foreground,
            source.Background,
            source.TextEffects,
            source.Typography,
            target);
    }

    private static void ApplyTextStyleToFlow(
        string? fontFamily,
        double? fontSize,
        string? fontWeight,
        string? fontStyle,
        string? fontStretch,
        string? foreground,
        string? background,
        object? textEffects,
        object? typography,
        Vibe.Office.FlowDocument.TextElement target)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            target.FontFamily = fontFamily;
        }

        if (fontSize.HasValue)
        {
            target.FontSize = fontSize;
        }

        if (!string.IsNullOrWhiteSpace(fontWeight)
            && ParseFontWeight(fontWeight) is { } parsedWeight)
        {
            target.FontWeight = parsedWeight;
        }

        if (!string.IsNullOrWhiteSpace(fontStyle)
            && ParseFontStyle(fontStyle) is { } parsedStyle)
        {
            target.FontStyle = parsedStyle;
        }

        if (!string.IsNullOrWhiteSpace(fontStretch))
        {
            target.FontStretch = fontStretch;
        }

        if (!string.IsNullOrWhiteSpace(foreground))
        {
            target.Foreground = foreground;
        }

        if (!string.IsNullOrWhiteSpace(background))
        {
            target.Background = background;
        }

        if (textEffects is not null)
        {
            target.TextEffects = textEffects;
        }

        if (typography is not null)
        {
            target.Typography = typography;
        }
    }

    private static void ApplyTextStyleToFlow(
        string? fontFamily,
        double? fontSize,
        string? fontWeight,
        string? fontStyle,
        string? fontStretch,
        string? foreground,
        string? background,
        object? textEffects,
        object? typography,
        FlowDocumentModel target)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            target.FontFamily = fontFamily;
        }

        if (fontSize.HasValue)
        {
            target.FontSize = fontSize;
        }

        if (!string.IsNullOrWhiteSpace(fontWeight)
            && ParseFontWeight(fontWeight) is { } parsedWeight)
        {
            target.FontWeight = parsedWeight;
        }

        if (!string.IsNullOrWhiteSpace(fontStyle)
            && ParseFontStyle(fontStyle) is { } parsedStyle)
        {
            target.FontStyle = parsedStyle;
        }

        if (!string.IsNullOrWhiteSpace(fontStretch))
        {
            target.FontStretch = fontStretch;
        }

        if (!string.IsNullOrWhiteSpace(foreground))
        {
            target.Foreground = foreground;
        }

        if (!string.IsNullOrWhiteSpace(background))
        {
            target.Background = background;
        }

        if (textEffects is not null)
        {
            target.TextEffects = textEffects;
        }

        if (typography is not null)
        {
            target.Typography = typography;
        }
    }

    private static void ApplyFlowTextStyle(
        string? fontFamily,
        double? fontSize,
        string? fontWeight,
        string? fontStyle,
        string? fontStretch,
        string? foreground,
        string? background,
        object? textEffects,
        object? typography,
        Vibe.Office.WinUICompat.Documents.TextElement target)
    {
        target.FontFamily = fontFamily;
        target.FontSize = fontSize;
        target.FontWeight = fontWeight;
        target.FontStyle = fontStyle;
        target.FontStretch = fontStretch;
        target.Foreground = foreground;
        target.Background = background;
        target.TextEffects = textEffects;
        target.Typography = typography;
    }

    private static void ApplyFlowTextStyle(
        string? fontFamily,
        double? fontSize,
        string? fontWeight,
        string? fontStyle,
        string? fontStretch,
        string? foreground,
        string? background,
        object? textEffects,
        object? typography,
        CompatDocument target)
    {
        target.FontFamily = fontFamily;
        target.FontSize = fontSize;
        target.FontWeight = fontWeight;
        target.FontStyle = fontStyle;
        target.FontStretch = fontStretch;
        target.Foreground = foreground;
        target.Background = background;
        target.TextEffects = textEffects;
        target.Typography = typography;
    }

    private static FlowFlowThickness ToFlowThickness(CompatThickness source)
    {
        return new FlowFlowThickness(source.Left, source.Top, source.Right, source.Bottom);
    }

    private static CompatThickness ToCompatThickness(FlowFlowThickness source)
    {
        return new CompatThickness(source.Left, source.Top, source.Right, source.Bottom);
    }

    private static FlowListMarkerStyle ParseMarkerStyle(string? markerStyle)
    {
        if (string.IsNullOrWhiteSpace(markerStyle))
        {
            return FlowListMarkerStyle.Disc;
        }

        return Enum.TryParse<FlowListMarkerStyle>(markerStyle, true, out var parsed)
            ? parsed
            : FlowListMarkerStyle.Disc;
    }

    private static Vibe.Office.FlowDocument.FlowTextAlignment? ParseTextAlignment(string? textAlignment)
    {
        if (string.IsNullOrWhiteSpace(textAlignment))
        {
            return null;
        }

        return Enum.TryParse<Vibe.Office.FlowDocument.FlowTextAlignment>(textAlignment, true, out var parsed)
            ? parsed
            : null;
    }

    private static Vibe.Office.FlowDocument.FlowFontWeight? ParseFontWeight(string? fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontWeight))
        {
            return null;
        }

        return Enum.TryParse<Vibe.Office.FlowDocument.FlowFontWeight>(fontWeight, true, out var parsed)
            ? parsed
            : null;
    }

    private static Vibe.Office.FlowDocument.FlowFontStyle? ParseFontStyle(string? fontStyle)
    {
        if (string.IsNullOrWhiteSpace(fontStyle))
        {
            return null;
        }

        return Enum.TryParse<Vibe.Office.FlowDocument.FlowFontStyle>(fontStyle, true, out var parsed)
            ? parsed
            : null;
    }

    private static Vibe.Office.FlowDocument.FlowBaselineAlignment? ParseBaselineAlignment(string? baselineAlignment)
    {
        if (string.IsNullOrWhiteSpace(baselineAlignment))
        {
            return null;
        }

        return Enum.TryParse<Vibe.Office.FlowDocument.FlowBaselineAlignment>(baselineAlignment, true, out var parsed)
            ? parsed
            : null;
    }

    private static Vibe.Office.FlowDocument.FlowTextDecorations? ParseTextDecorations(string? textDecorations)
    {
        if (string.IsNullOrWhiteSpace(textDecorations))
        {
            return null;
        }

        return Enum.TryParse<Vibe.Office.FlowDocument.FlowTextDecorations>(textDecorations, true, out var parsed)
            ? parsed
            : null;
    }

}
