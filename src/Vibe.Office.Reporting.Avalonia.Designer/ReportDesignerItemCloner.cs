using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

internal static class ReportDesignerItemCloner
{
    public static ReportItem Clone(ReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            TextItem textItem => CloneTextItem(textItem),
            ImageItem imageItem => CloneImageItem(imageItem),
            LineItem lineItem => CloneLineItem(lineItem),
            ShapeItem shapeItem => CloneShapeItem(shapeItem),
            ContainerItem containerItem => CloneContainerItem(containerItem),
            ChartItem chartItem => CloneChartItem(chartItem),
            GaugeItem gaugeItem => CloneGaugeItem(gaugeItem),
            TablixItem tablixItem => CloneTablixItem(tablixItem),
            SubreportItem subreportItem => CloneSubreportItem(subreportItem),
            DocumentTemplateItem templateItem => CloneTemplateItem(templateItem),
            _ => throw new NotSupportedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cloning report item type '{item.GetType().FullName}' is not supported by the designer."))
        };
    }

    private static TextItem CloneTextItem(TextItem item)
    {
        var clone = new TextItem
        {
            StaticText = item.StaticText,
            ValueExpression = item.ValueExpression,
            FormatString = item.FormatString,
            CanGrow = item.CanGrow,
            CanShrink = item.CanShrink,
            Paragraphs = item.Paragraphs.Select(CloneParagraph).ToList()
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static ImageItem CloneImageItem(ImageItem item)
    {
        var clone = new ImageItem
        {
            SourceKind = item.SourceKind,
            ValueExpression = item.ValueExpression,
            MimeType = item.MimeType,
            EmbeddedData = item.EmbeddedData?.ToArray(),
            SizingMode = item.SizingMode
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static LineItem CloneLineItem(LineItem item)
    {
        var clone = new LineItem
        {
            X2 = item.X2,
            Y2 = item.Y2
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static ShapeItem CloneShapeItem(ShapeItem item)
    {
        var clone = new ShapeItem
        {
            Shape = item.Shape
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static ContainerItem CloneContainerItem(ContainerItem item)
    {
        var clone = new ContainerItem
        {
            Items = item.Items.Select(Clone).ToList()
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static ChartItem CloneChartItem(ChartItem item)
    {
        var clone = new ChartItem
        {
            DataSetId = item.DataSetId,
            Type = item.Type,
            BarDirection = item.BarDirection,
            CategoryExpression = item.CategoryExpression,
            CategoryLabelExpression = item.CategoryLabelExpression,
            CategorySortExpression = item.CategorySortExpression,
            CategorySortDirection = item.CategorySortDirection,
            TitleExpression = item.TitleExpression,
            ChartAreaStyle = CloneChartStyle(item.ChartAreaStyle),
            PlotAreaStyle = CloneChartStyle(item.PlotAreaStyle),
            Legend = CloneChartLegend(item.Legend),
            Axes = item.Axes.Select(CloneAxis).ToList(),
            Series = item.Series.Select(CloneSeries).ToList()
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static GaugeItem CloneGaugeItem(GaugeItem item)
    {
        var clone = new GaugeItem
        {
            DataSetId = item.DataSetId,
            GaugeKind = item.GaugeKind,
            ValueExpression = item.ValueExpression,
            MinimumExpression = item.MinimumExpression,
            MaximumExpression = item.MaximumExpression,
            TargetValueExpression = item.TargetValueExpression,
            LabelExpression = item.LabelExpression,
            RawRdlXml = item.RawRdlXml
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static TablixItem CloneTablixItem(TablixItem item)
    {
        var clone = new TablixItem
        {
            DataSetId = item.DataSetId,
            RepeatHeaderRows = item.RepeatHeaderRows,
            Filters = item.Filters.Select(CloneFilter).ToList(),
            Columns = item.Columns.Select(CloneColumn).ToList(),
            Rows = item.Rows.Select(CloneRow).ToList(),
            RowMembers = item.RowMembers.Select(CloneMember).ToList(),
            ColumnMembers = item.ColumnMembers.Select(CloneMember).ToList()
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static SubreportItem CloneSubreportItem(SubreportItem item)
    {
        var clone = new SubreportItem
        {
            ReportReferenceId = item.ReportReferenceId,
            Parameters = item.Parameters.Select(CloneParameterBinding).ToList()
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static DocumentTemplateItem CloneTemplateItem(DocumentTemplateItem item)
    {
        var clone = new DocumentTemplateItem
        {
            TemplateId = item.TemplateId,
            TemplateFormat = item.TemplateFormat,
            EmbeddedContent = item.EmbeddedContent,
            Bindings = new Dictionary<string, string>(item.Bindings, StringComparer.OrdinalIgnoreCase)
        };

        CopyCommonProperties(item, clone);
        return clone;
    }

    private static void CopyCommonProperties(ReportItem source, ReportItem target)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.Bounds = source.Bounds;
        target.ZIndex = source.ZIndex;
        target.VisibilityExpression = source.VisibilityExpression;
        target.BookmarkExpression = source.BookmarkExpression;
        target.TooltipExpression = source.TooltipExpression;
        target.DrillthroughAction = CloneDrillthroughAction(source.DrillthroughAction);
        target.PageBreak = ClonePageBreak(source.PageBreak);
        target.KeepTogether = source.KeepTogether;
        target.StyleName = source.StyleName;
    }

    private static ReportTextParagraph CloneParagraph(ReportTextParagraph paragraph)
    {
        return new ReportTextParagraph
        {
            TextAlign = paragraph.TextAlign,
            Runs = paragraph.Runs.Select(CloneTextRun).ToList()
        };
    }

    private static ReportTextRun CloneTextRun(ReportTextRun run)
    {
        return new ReportTextRun
        {
            StaticText = run.StaticText,
            ValueExpression = run.ValueExpression,
            StyleName = run.StyleName
        };
    }

    private static ReportChartSeriesDefinition CloneSeries(ReportChartSeriesDefinition series)
    {
        return new ReportChartSeriesDefinition
        {
            Type = series.Type,
            BarDirection = series.BarDirection,
            NameExpression = series.NameExpression,
            ValueExpression = series.ValueExpression,
            ColorExpression = series.ColorExpression,
            Style = CloneChartStyle(series.Style),
            UseSmoothedLine = series.UseSmoothedLine
        };
    }

    private static ChartStyle? CloneChartStyle(ChartStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ChartStyle
        {
            Fill = style.Fill is null
                ? null
                : new ChartFillStyle
                {
                    IsNone = style.Fill.IsNone,
                    Color = style.Fill.Color
                },
            Line = style.Line is null
                ? null
                : new ChartLineStyle
                {
                    IsNone = style.Line.IsNone,
                    Color = style.Line.Color,
                    Width = style.Line.Width,
                    Style = style.Line.Style
                },
            Effects = style.Effects is null
                ? null
                : new ChartEffectStyle
                {
                    Shadow = style.Effects.Shadow is null
                        ? null
                        : new ChartShadowEffect
                        {
                            BlurRadius = style.Effects.Shadow.BlurRadius,
                            Distance = style.Effects.Shadow.Distance,
                            Direction = style.Effects.Shadow.Direction,
                            Color = style.Effects.Shadow.Color
                        }
                }
        };
    }

    private static ChartLegend? CloneChartLegend(ChartLegend? legend)
    {
        if (legend is null)
        {
            return null;
        }

        return new ChartLegend
        {
            IsVisible = legend.IsVisible,
            Position = legend.Position,
            Overlay = legend.Overlay,
            TextStyle = CloneChartTextStyle(legend.TextStyle)
        };
    }

    private static ChartAxis CloneAxis(ChartAxis axis)
    {
        return new ChartAxis
        {
            AxisId = axis.AxisId,
            CrossAxisId = axis.CrossAxisId,
            Kind = axis.Kind,
            Position = axis.Position,
            IsVisible = axis.IsVisible,
            Minimum = axis.Minimum,
            Maximum = axis.Maximum,
            MajorUnit = axis.MajorUnit,
            MinorUnit = axis.MinorUnit,
            MajorTickMark = axis.MajorTickMark,
            MinorTickMark = axis.MinorTickMark,
            TickLabelPosition = axis.TickLabelPosition,
            NumberFormat = axis.NumberFormat,
            Title = axis.Title,
            LineStyle = axis.LineStyle is null
                ? null
                : new ChartLineStyle
                {
                    IsNone = axis.LineStyle.IsNone,
                    Color = axis.LineStyle.Color,
                    Width = axis.LineStyle.Width,
                    Style = axis.LineStyle.Style
                },
            MajorGridlineStyle = axis.MajorGridlineStyle is null
                ? null
                : new ChartLineStyle
                {
                    IsNone = axis.MajorGridlineStyle.IsNone,
                    Color = axis.MajorGridlineStyle.Color,
                    Width = axis.MajorGridlineStyle.Width,
                    Style = axis.MajorGridlineStyle.Style
                },
            MinorGridlineStyle = axis.MinorGridlineStyle is null
                ? null
                : new ChartLineStyle
                {
                    IsNone = axis.MinorGridlineStyle.IsNone,
                    Color = axis.MinorGridlineStyle.Color,
                    Width = axis.MinorGridlineStyle.Width,
                    Style = axis.MinorGridlineStyle.Style
                },
            LabelTextStyle = CloneChartTextStyle(axis.LabelTextStyle),
            TitleTextStyle = CloneChartTextStyle(axis.TitleTextStyle)
        };
    }

    private static ChartTextStyle? CloneChartTextStyle(ChartTextStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ChartTextStyle
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Color = style.Color,
            Bold = style.Bold,
            Italic = style.Italic
        };
    }

    private static ReportTablixColumnDefinition CloneColumn(ReportTablixColumnDefinition column)
    {
        return new ReportTablixColumnDefinition
        {
            Id = column.Id,
            Width = column.Width
        };
    }

    private static ReportFilterDefinition CloneFilter(ReportFilterDefinition filter)
    {
        return new ReportFilterDefinition
        {
            Expression = filter.Expression,
            Operator = filter.Operator,
            ValueExpression = filter.ValueExpression
        };
    }

    private static ReportTablixRowDefinition CloneRow(ReportTablixRowDefinition row)
    {
        return new ReportTablixRowDefinition
        {
            Id = row.Id,
            IsHeader = row.IsHeader,
            Height = row.Height,
            Cells = row.Cells.Select(CloneCell).ToList()
        };
    }

    private static ReportTablixMemberDefinition CloneMember(ReportTablixMemberDefinition member)
    {
        return new ReportTablixMemberDefinition
        {
            Id = member.Id,
            Kind = member.Kind,
            GroupName = member.GroupName,
            GroupExpression = member.GroupExpression,
            SortExpression = member.SortExpression,
            SortDirection = member.SortDirection,
            VisibilityExpression = member.VisibilityExpression,
            ToggleItemId = member.ToggleItemId,
            RepeatOnNewPage = member.RepeatOnNewPage,
            KeepWithGroup = member.KeepWithGroup,
            PageBreak = ClonePageBreak(member.PageBreak),
            RowDefinitionIndex = member.RowDefinitionIndex,
            ColumnDefinitionIndex = member.ColumnDefinitionIndex,
            Members = member.Members.Select(CloneMember).ToList()
        };
    }

    private static ReportTablixCellDefinition CloneCell(ReportTablixCellDefinition cell)
    {
        return new ReportTablixCellDefinition
        {
            Text = cell.Text,
            ValueExpression = cell.ValueExpression,
            FormatString = cell.FormatString,
            StyleName = cell.StyleName,
            ContentItem = cell.ContentItem is null ? null : Clone(cell.ContentItem),
            RowSpan = cell.RowSpan,
            ColumnSpan = cell.ColumnSpan
        };
    }

    private static ReportParameterBinding CloneParameterBinding(ReportParameterBinding binding)
    {
        return new ReportParameterBinding
        {
            ParameterId = binding.ParameterId,
            ValueExpression = binding.ValueExpression
        };
    }

    private static ReportDrillthroughAction? CloneDrillthroughAction(ReportDrillthroughAction? action)
    {
        if (action is null)
        {
            return null;
        }

        return new ReportDrillthroughAction
        {
            ReportReferenceId = action.ReportReferenceId,
            Parameters = action.Parameters.Select(CloneParameterBinding).ToList()
        };
    }

    private static ReportPageBreakDefinition? ClonePageBreak(ReportPageBreakDefinition? pageBreak)
    {
        if (pageBreak is null)
        {
            return null;
        }

        return new ReportPageBreakDefinition
        {
            DisabledExpression = pageBreak.DisabledExpression,
            Location = pageBreak.Location
        };
    }
}
