using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ProEdit.WinUICompat.Documents;
using CompatList = ProEdit.WinUICompat.Documents.List;
using CompatParagraph = ProEdit.WinUICompat.Documents.Paragraph;
using CompatTable = ProEdit.WinUICompat.Documents.Table;
using CompatHyperlink = ProEdit.WinUICompat.Documents.Hyperlink;
using CompatRun = ProEdit.WinUICompat.Documents.Run;
using CompatSpan = ProEdit.WinUICompat.Documents.Span;
using XamlDocs = Microsoft.UI.Xaml.Documents;

namespace ProEdit.WinUICompat.Controls;

internal static class CompatDocumentVisualFactory
{
    public static void Populate(Panel targetPanel, IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(targetPanel);
        ArgumentNullException.ThrowIfNull(blocks);

        targetPanel.Children.Clear();
        foreach (var block in blocks)
        {
            targetPanel.Children.Add(BuildBlockElement(block));
        }
    }

    private static UIElement BuildBlockElement(Block block)
    {
        return block switch
        {
            Section section => BuildSectionElement(section),
            CompatParagraph paragraph => BuildParagraphElement(paragraph),
            CompatList list => BuildListElement(list),
            CompatTable table => BuildTableElement(table),
            BlockUIContainer container => BuildBlockUiElement(container),
            _ => BuildFallbackText($"[{block.GetType().Name}]")
        };
    }

    private static UIElement BuildSectionElement(Section section)
    {
        var panel = new StackPanel { Spacing = 2 };
        if (section.Margin.HasValue && !section.Margin.Value.IsEmpty)
        {
            panel.Margin = ToXamlThickness(section.Margin.Value);
        }

        if (TryParseColor(section.Background, out var background))
        {
            panel.Background = new SolidColorBrush(background);
        }

        for (var i = 0; i < section.Blocks.Count; i++)
        {
            panel.Children.Add(BuildBlockElement(section.Blocks[i]));
        }

        return panel;
    }

    private static UIElement BuildParagraphElement(CompatParagraph paragraph)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords
        };
        ApplyBlockStyles(paragraph, textBlock);

        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            textBlock.Inlines.Add(BuildInline(paragraph.Inlines[i]));
        }

        return textBlock;
    }

    private static UIElement BuildListElement(CompatList list)
    {
        var listPanel = new StackPanel
        {
            Spacing = 2
        };

        var startIndex = list.StartIndex.GetValueOrDefault(1);
        for (var i = 0; i < list.ListItems.Count; i++)
        {
            var itemGrid = new Grid
            {
                ColumnSpacing = 8
            };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = BuildListMarkerText(list.MarkerStyle, startIndex + i),
                VerticalAlignment = VerticalAlignment.Top
            };

            var itemPanel = new StackPanel
            {
                Spacing = 2
            };

            var listItem = list.ListItems[i];
            for (var blockIndex = 0; blockIndex < listItem.Blocks.Count; blockIndex++)
            {
                itemPanel.Children.Add(BuildBlockElement(listItem.Blocks[blockIndex]));
            }

            Grid.SetColumn(marker, 0);
            Grid.SetColumn(itemPanel, 1);
            itemGrid.Children.Add(marker);
            itemGrid.Children.Add(itemPanel);
            listPanel.Children.Add(itemGrid);
        }

        return listPanel;
    }

    private static UIElement BuildTableElement(CompatTable table)
    {
        var grid = new Grid
        {
            RowSpacing = 2,
            ColumnSpacing = 2
        };

        var logicalRows = new List<(TableRow Row, int RowIndex)>();
        for (var groupIndex = 0; groupIndex < table.RowGroups.Count; groupIndex++)
        {
            var rowGroup = table.RowGroups[groupIndex];
            for (var rowIndex = 0; rowIndex < rowGroup.Rows.Count; rowIndex++)
            {
                logicalRows.Add((rowGroup.Rows[rowIndex], logicalRows.Count));
            }
        }

        var columnCount = ResolveTableColumnCount(table, logicalRows);
        for (var i = 0; i < columnCount; i++)
        {
            var configuredWidth = i < table.Columns.Count ? table.Columns[i].Width : null;
            grid.ColumnDefinitions.Add(
                configuredWidth.HasValue
                    ? new ColumnDefinition { Width = new GridLength(Math.Max(1d, configuredWidth.Value), GridUnitType.Pixel) }
                    : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < logicalRows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var logicalRowIndex = 0; logicalRowIndex < logicalRows.Count; logicalRowIndex++)
        {
            var row = logicalRows[logicalRowIndex].Row;
            var currentColumn = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var border = BuildCellElement(cell);

                Grid.SetRow(border, logicalRowIndex);
                Grid.SetColumn(border, currentColumn);
                Grid.SetRowSpan(border, Math.Max(1, cell.RowSpan));
                Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);

                currentColumn += Math.Max(1, cell.ColumnSpan);
            }
        }

        return grid;
    }

    private static UIElement BuildCellElement(TableCell cell)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.LightGray),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
            Padding = new Microsoft.UI.Xaml.Thickness(6, 4, 6, 4)
        };

        if (cell.BorderThickness.HasValue && !cell.BorderThickness.Value.IsEmpty)
        {
            border.BorderThickness = ToXamlThickness(cell.BorderThickness.Value);
        }

        if (TryParseColor(cell.BorderBrush, out var borderColor))
        {
            border.BorderBrush = new SolidColorBrush(borderColor);
        }

        if (cell.Padding.HasValue && !cell.Padding.Value.IsEmpty)
        {
            border.Padding = ToXamlThickness(cell.Padding.Value);
        }

        if (TryParseColor(cell.Background, out var background))
        {
            border.Background = new SolidColorBrush(background);
        }

        var content = new StackPanel { Spacing = 2 };
        for (var i = 0; i < cell.Blocks.Count; i++)
        {
            content.Children.Add(BuildBlockElement(cell.Blocks[i]));
        }

        border.Child = content;
        return border;
    }

    private static UIElement BuildBlockUiElement(BlockUIContainer container)
    {
        if (container.Child is UIElement element)
        {
            return element;
        }

        return BuildFallbackText(container.Child?.ToString() ?? "[Block UI]");
    }

    private static UIElement BuildFallbackText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.WrapWholeWords,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(Colors.DimGray)
        };
    }

    private static XamlDocs.Inline BuildInline(ProEdit.WinUICompat.Documents.Inline inline)
    {
        switch (inline)
        {
            case CompatRun run:
                return BuildRun(run);
            case ProEdit.WinUICompat.Documents.Bold bold:
                return BuildSpanLike(bold, new XamlDocs.Bold());
            case ProEdit.WinUICompat.Documents.Italic italic:
                return BuildSpanLike(italic, new XamlDocs.Italic());
            case ProEdit.WinUICompat.Documents.Underline underline:
                return BuildSpanLike(underline, new XamlDocs.Underline());
            case CompatHyperlink hyperlink:
                return BuildHyperlink(hyperlink);
            case CompatSpan span:
                return BuildSpanLike(span, new XamlDocs.Span());
            case ProEdit.WinUICompat.Documents.Figure figure:
                return BuildAnchoredInline(figure, "Figure");
            case ProEdit.WinUICompat.Documents.Floater floater:
                return BuildAnchoredInline(floater, "Floater");
            case ProEdit.WinUICompat.Documents.LineBreak:
                return new XamlDocs.LineBreak();
            case InlineUIContainer inlineUi:
                return BuildInlineUi(inlineUi);
            default:
                return new XamlDocs.Run();
        }
    }

    private static XamlDocs.Run BuildRun(CompatRun run)
    {
        var xamlRun = new XamlDocs.Run
        {
            Text = run.Text ?? string.Empty
        };
        ApplyTextStyles(run, xamlRun);
        return xamlRun;
    }

    private static XamlDocs.Span BuildSpanLike(CompatSpan source, XamlDocs.Span target)
    {
        ApplyTextStyles(source, target);
        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(BuildInline(source.Inlines[i]));
        }

        return target;
    }

    private static XamlDocs.Hyperlink BuildHyperlink(CompatHyperlink source)
    {
        var target = new XamlDocs.Hyperlink();
        if (Uri.TryCreate(source.NavigateUri, UriKind.Absolute, out var uri))
        {
            target.NavigateUri = uri;
        }

        ApplyTextStyles(source, target);

        for (var i = 0; i < source.Inlines.Count; i++)
        {
            target.Inlines.Add(BuildInline(source.Inlines[i]));
        }

        return target;
    }

    private static XamlDocs.Inline BuildInlineUi(InlineUIContainer source)
    {
        var placeholder = new XamlDocs.Run
        {
            Text = source.Child?.ToString() ?? "[Inline UI]"
        };
        placeholder.FontStyle = Windows.UI.Text.FontStyle.Italic;
        placeholder.Foreground = new SolidColorBrush(Colors.DimGray);
        return placeholder;
    }

    private static XamlDocs.Inline BuildAnchoredInline(ProEdit.WinUICompat.Documents.AnchoredBlock source, string label)
    {
        var placeholder = new XamlDocs.Run
        {
            Text = $"[{label}]"
        };
        placeholder.FontStyle = Windows.UI.Text.FontStyle.Italic;
        placeholder.Foreground = new SolidColorBrush(Colors.DimGray);
        return placeholder;
    }

    private static void ApplyBlockStyles(Block source, TextBlock target)
    {
        if (source.Margin.HasValue && !source.Margin.Value.IsEmpty)
        {
            target.Margin = ToXamlThickness(source.Margin.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.TextAlignment)
            && Enum.TryParse<TextAlignment>(source.TextAlignment, true, out var textAlignment))
        {
            target.TextAlignment = textAlignment;
        }

        if (source.LineHeight.HasValue)
        {
            target.LineHeight = source.LineHeight.Value;
        }

        if (TryParseColor(source.Background, out var background))
        {
            target.Background = new SolidColorBrush(background);
        }

        ApplyTextStyles(source, target);
    }

    private static void ApplyTextStyles(ProEdit.WinUICompat.Documents.TextElement source, TextBlock target)
    {
        if (source.FontSize.HasValue)
        {
            target.FontSize = source.FontSize.Value;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamily))
        {
            target.FontFamily = new FontFamily(source.FontFamily);
        }

        if (TryParseColor(source.Foreground, out var color))
        {
            target.Foreground = new SolidColorBrush(color);
        }
    }

    private static void ApplyTextStyles(ProEdit.WinUICompat.Documents.TextElement source, XamlDocs.TextElement target)
    {
        if (source.FontSize.HasValue)
        {
            target.FontSize = source.FontSize.Value;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamily))
        {
            target.FontFamily = new FontFamily(source.FontFamily);
        }

        if (TryParseColor(source.Foreground, out var color))
        {
            target.Foreground = new SolidColorBrush(color);
        }
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            return false;
        }

        if (text.Length == 7)
        {
            if (!byte.TryParse(text.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
                || !byte.TryParse(text.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
                || !byte.TryParse(text.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return false;
            }

            color = Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
            return true;
        }

        if (text.Length == 9)
        {
            if (!byte.TryParse(text.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var a)
                || !byte.TryParse(text.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
                || !byte.TryParse(text.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
                || !byte.TryParse(text.AsSpan(7, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return false;
            }

            color = Microsoft.UI.ColorHelper.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    private static int ResolveTableColumnCount(CompatTable table, List<(TableRow Row, int RowIndex)> logicalRows)
    {
        var configuredColumns = table.Columns.Count;
        var maxFromCells = 0;
        for (var i = 0; i < logicalRows.Count; i++)
        {
            var row = logicalRows[i].Row;
            var rowColumns = 0;
            for (var j = 0; j < row.Cells.Count; j++)
            {
                rowColumns += Math.Max(1, row.Cells[j].ColumnSpan);
            }

            maxFromCells = Math.Max(maxFromCells, rowColumns);
        }

        return Math.Max(1, Math.Max(configuredColumns, maxFromCells));
    }

    private static string BuildListMarkerText(string? markerStyle, int itemNumber)
    {
        var style = markerStyle ?? "Disc";
        if (style.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
        {
            return $"{Math.Max(1, itemNumber)}.";
        }

        if (style.Equals("UpperRoman", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ToRoman(Math.Max(1, itemNumber)).ToUpperInvariant()}.";
        }

        if (style.Equals("LowerRoman", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ToRoman(Math.Max(1, itemNumber)).ToLowerInvariant()}.";
        }

        if (style.Equals("Circle", StringComparison.OrdinalIgnoreCase))
        {
            return "\u25E6";
        }

        if (style.Equals("Square", StringComparison.OrdinalIgnoreCase))
        {
            return "\u25AA";
        }

        return "\u2022";
    }

    private static string ToRoman(int value)
    {
        if (value <= 0)
        {
            return "I";
        }

        var numerals = new (int Value, string Symbol)[]
        {
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I")
        };

        var remaining = value;
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < numerals.Length && remaining > 0; i++)
        {
            var numeral = numerals[i];
            while (remaining >= numeral.Value)
            {
                result.Append(numeral.Symbol);
                remaining -= numeral.Value;
            }
        }

        return result.ToString();
    }

    private static Microsoft.UI.Xaml.Thickness ToXamlThickness(ProEdit.WinUICompat.Documents.Thickness source)
    {
        return new Microsoft.UI.Xaml.Thickness(source.Left, source.Top, source.Right, source.Bottom);
    }
}
