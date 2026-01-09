using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonGroupPanel : Panel
{
    private LayoutResult _lastLayout = LayoutResult.Empty;
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(RowSpacing), 2d);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(ColumnSpacing), 6d);

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(RowHeight), 26d);

    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(ColumnWidth), 0d);

    public static readonly StyledProperty<double> ColumnMinWidthProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(ColumnMinWidth), 72d);

    public static readonly StyledProperty<double> ColumnMaxWidthProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(ColumnMaxWidth), 160d);

    public static readonly StyledProperty<double> MinColumnHeightProperty =
        AvaloniaProperty.Register<RibbonGroupPanel, double>(nameof(MinColumnHeight), 82d);

    public static readonly AttachedProperty<RibbonControlSize> ItemSizeProperty =
        AvaloniaProperty.RegisterAttached<RibbonGroupPanel, Control, RibbonControlSize>(
            "ItemSize",
            defaultValue: RibbonControlSize.Medium);

    static RibbonGroupPanel()
    {
        AffectsMeasure<RibbonGroupPanel>(
            RowSpacingProperty,
            ColumnSpacingProperty,
            RowHeightProperty,
            ColumnWidthProperty,
            ColumnMinWidthProperty,
            ColumnMaxWidthProperty,
            MinColumnHeightProperty);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double ColumnMinWidth
    {
        get => GetValue(ColumnMinWidthProperty);
        set => SetValue(ColumnMinWidthProperty, value);
    }

    public double ColumnMaxWidth
    {
        get => GetValue(ColumnMaxWidthProperty);
        set => SetValue(ColumnMaxWidthProperty, value);
    }

    public double MinColumnHeight
    {
        get => GetValue(MinColumnHeightProperty);
        set => SetValue(MinColumnHeightProperty, value);
    }

    public static void SetItemSize(Control control, RibbonControlSize value)
    {
        control.SetValue(ItemSizeProperty, value);
    }

    public static RibbonControlSize GetItemSize(Control control)
    {
        return control.GetValue(ItemSizeProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = BuildLayout(availableSize, useAvailableHeight: false);
        _lastLayout = layout;
        return new Size(layout.TotalWidth, layout.DesiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = _lastLayout;
        if (layout.Columns.Count == 0)
        {
            return finalSize;
        }
        var offsetY = 0d;

        var x = 0d;
        foreach (var column in layout.Columns)
        {
            foreach (var item in column.Items)
            {
                var y = offsetY + item.Row * (layout.RowHeight + RowSpacing);
                var height = layout.RowHeight * item.RowSpan + RowSpacing * (item.RowSpan - 1);
                var width = column.Width;
                item.Control.Arrange(new Rect(x, y, width, height));
            }

            x += column.Width + ColumnSpacing;
        }

        return finalSize;
    }

    private LayoutResult BuildLayout(Size availableSize, bool useAvailableHeight)
    {
        if (Children.Count == 0)
        {
            return LayoutResult.Empty;
        }

        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        var columns = new List<ColumnLayout>();
        foreach (var child in Children)
        {
            var size = ResolveSize(child);
            var rowSpan = size switch
            {
                RibbonControlSize.Large => 3,
                RibbonControlSize.Small => 1,
                _ => 2
            };

            var placed = false;
            foreach (var column in columns)
            {
                if (column.TryPlace(child, rowSpan))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                var column = new ColumnLayout();
                column.TryPlace(child, rowSpan);
                columns.Add(column);
            }
        }

        var rowHeight = RowHeight > 0 ? RowHeight : 1;
        var totalWidth = 0d;
        foreach (var column in columns)
        {
            var columnWidth = ColumnWidth > 0 ? ColumnWidth : ColumnMinWidth;
            foreach (var item in column.Items)
            {
                columnWidth = Math.Max(columnWidth, item.Control.DesiredSize.Width);
            }

            column.UpdateWidth(columnWidth, ColumnMinWidth, ColumnMaxWidth);
            totalWidth += column.Width;

            foreach (var item in column.Items)
            {
                var height = rowHeight * item.RowSpan + RowSpacing * (item.RowSpan - 1);
                item.Control.Measure(new Size(column.Width, height));
            }
        }

        if (columns.Count > 1)
        {
            totalWidth += ColumnSpacing * (columns.Count - 1);
        }

        var contentHeight = rowHeight * 3 + RowSpacing * 2;
        var columnHeight = Math.Max(MinColumnHeight, contentHeight);
        if (useAvailableHeight && !double.IsInfinity(availableSize.Height) && availableSize.Height > 0)
        {
            columnHeight = Math.Max(columnHeight, availableSize.Height);
        }

        var desiredHeight = columnHeight;

        return new LayoutResult(columns, rowHeight, contentHeight, totalWidth, desiredHeight);
    }

    private RibbonControlSize ResolveSize(Control control)
    {
        var size = GetItemSize(control);
        if (control.DataContext is IRibbonControl ribbonControl)
        {
            size = ribbonControl.LayoutSize;
        }

        return size;
    }

    private sealed class ColumnLayout
    {
        private readonly bool[] _occupied = new bool[3];

        public List<LayoutItem> Items { get; } = new();

        public double Width { get; private set; }

        public bool TryPlace(Control control, int rowSpan)
        {
            for (var row = 0; row <= 3 - rowSpan; row++)
            {
                var canFit = true;
                for (var i = 0; i < rowSpan; i++)
                {
                    if (_occupied[row + i])
                    {
                        canFit = false;
                        break;
                    }
                }

                if (!canFit)
                {
                    continue;
                }

                for (var i = 0; i < rowSpan; i++)
                {
                    _occupied[row + i] = true;
                }

                Items.Add(new LayoutItem(control, row, rowSpan));
                return true;
            }

            return false;
        }

        public void UpdateWidth(double columnWidth, double columnMinWidth, double columnMaxWidth)
        {
            var width = columnWidth;
            if (columnMinWidth > 0)
            {
                width = Math.Max(width, columnMinWidth);
            }

            if (columnMaxWidth > 0)
            {
                width = Math.Min(width, columnMaxWidth);
            }

            Width = Math.Max(1, width);
        }
    }

    private readonly record struct LayoutItem(Control Control, int Row, int RowSpan);

    private readonly record struct LayoutResult(
        List<ColumnLayout> Columns,
        double RowHeight,
        double ContentHeight,
        double TotalWidth,
        double DesiredHeight)
    {
        public static LayoutResult Empty => new(new List<ColumnLayout>(), 0, 0, 0, 0);
    }
}
