using System.Globalization;
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
            CategoryExpression = item.CategoryExpression,
            TitleExpression = item.TitleExpression,
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
            NameExpression = series.NameExpression,
            ValueExpression = series.ValueExpression,
            ColorExpression = series.ColorExpression
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
