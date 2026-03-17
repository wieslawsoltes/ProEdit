using System.Globalization;
using System.Xml.Linq;

namespace Vibe.Office.Reporting.Rdl;

internal sealed class ReportRdlExporter
{
    private readonly List<ReportDiagnostic> _diagnostics = new();
    private readonly Dictionary<string, ReportStyleDefinition> _styles;
    private readonly List<XElement> _embeddedImages = new();
    private readonly ReportDefinition _reportDefinition;
    private readonly ReportRdlWriteOptions _options;

    private XNamespace _xmlNamespace = XNamespace.None;

    public ReportRdlExporter(ReportDefinition reportDefinition, ReportRdlWriteOptions options)
    {
        _reportDefinition = reportDefinition ?? throw new ArgumentNullException(nameof(reportDefinition));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _styles = reportDefinition.Styles.ToDictionary(static style => style.Id, StringComparer.OrdinalIgnoreCase);
    }

    public ReportRdlWriteResult Write()
    {
        try
        {
            _xmlNamespace = ReportRdlNamespaces.GetMainNamespace(_options.Version);
            var root = new XElement(
                _xmlNamespace + "Report",
                new XAttribute(XNamespace.Xmlns + "rd", ReportRdlNamespaces.Designer.NamespaceName));

            root.Add(new XElement(ReportRdlNamespaces.Designer + "ReportUnitType", "Inch"));
            root.Add(new XElement(ReportRdlNamespaces.Designer + "ReportID", string.IsNullOrWhiteSpace(_reportDefinition.Id) ? Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture) : _reportDefinition.Id));
            if (!string.IsNullOrWhiteSpace(_reportDefinition.Name))
            {
                root.Add(new XElement(_xmlNamespace + "Description", _reportDefinition.Name));
            }

            var dataSources = WriteDataSources();
            if (dataSources is not null)
            {
                root.Add(dataSources);
            }

            var dataSets = WriteDataSets();
            if (dataSets is not null)
            {
                root.Add(dataSets);
            }

            var parameters = WriteParameters();
            if (parameters is not null)
            {
                root.Add(parameters);
            }

            if (_options.Version == ReportRdlVersion.Rdl2008)
            {
                if (_reportDefinition.Sections.Count > 1)
                {
                    _diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        "RDL 2008 export supports only a single report section.",
                        "/Report"));
                    return new ReportRdlWriteResult(string.Empty, _diagnostics);
                }

                var section = _reportDefinition.Sections.Count == 0 ? new ReportSection() : _reportDefinition.Sections[0];
                WriteBodyAndPage(root, section);
            }
            else
            {
                var sectionsElement = new XElement(_xmlNamespace + "ReportSections");
                for (var index = 0; index < _reportDefinition.Sections.Count; index++)
                {
                    var sectionElement = new XElement(_xmlNamespace + "ReportSection");
                    WriteBodyAndPage(sectionElement, _reportDefinition.Sections[index]);
                    sectionsElement.Add(sectionElement);
                }

                root.Add(sectionsElement);
            }

            if (_embeddedImages.Count > 0)
            {
                root.Add(new XElement(_xmlNamespace + "EmbeddedImages", _embeddedImages));
            }

            var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            var xml = _options.Indent
                ? document.ToString()
                : document.ToString(SaveOptions.DisableFormatting);

            return new ReportRdlWriteResult(xml, _diagnostics);
        }
        catch (Exception exception)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlWriteFailed,
                exception.Message,
                "/Report"));
            return new ReportRdlWriteResult(string.Empty, _diagnostics);
        }
    }

    private XElement? WriteDataSources()
    {
        if (_reportDefinition.DataSources.Count == 0)
        {
            return null;
        }

        var dataSourcesElement = new XElement(_xmlNamespace + "DataSources");
        for (var index = 0; index < _reportDefinition.DataSources.Count; index++)
        {
            var dataSource = _reportDefinition.DataSources[index];
            var dataSourceElement = new XElement(
                _xmlNamespace + "DataSource",
                new XAttribute("Name", ResolveName(dataSource.Id, $"DataSource{index + 1}")));

            if (!string.IsNullOrWhiteSpace(dataSource.ConnectionName))
            {
                dataSourceElement.Add(new XElement(_xmlNamespace + "DataSourceReference", dataSource.ConnectionName));
            }
            else
            {
                dataSourceElement.Add(
                    new XElement(
                        _xmlNamespace + "ConnectionProperties",
                        new XElement(_xmlNamespace + "DataProvider", string.IsNullOrWhiteSpace(dataSource.ProviderId) ? "OBJECT" : dataSource.ProviderId),
                        new XElement(_xmlNamespace + "ConnectString", BuildConnectionString(dataSource))));
            }

            dataSourcesElement.Add(dataSourceElement);
        }

        return dataSourcesElement;
    }

    private XElement? WriteDataSets()
    {
        if (_reportDefinition.DataSets.Count == 0)
        {
            return null;
        }

        var dataSetsElement = new XElement(_xmlNamespace + "DataSets");
        for (var index = 0; index < _reportDefinition.DataSets.Count; index++)
        {
            var dataSet = _reportDefinition.DataSets[index];
            var dataSetElement = new XElement(
                _xmlNamespace + "DataSet",
                new XAttribute("Name", ResolveName(dataSet.Id, $"DataSet{index + 1}")));

            dataSetElement.Add(
                new XElement(
                    _xmlNamespace + "Query",
                    new XElement(_xmlNamespace + "DataSourceName", ResolveName(dataSet.DataSourceId, "DataSource1")),
                    new XElement(_xmlNamespace + "CommandText", dataSet.Query ?? string.Empty),
                    WriteDataSetParameters(dataSet)));

            dataSetElement.Add(WriteFields(dataSet));

            var filters = WriteFilters(dataSet);
            if (filters is not null)
            {
                dataSetElement.Add(filters);
            }

            var sorts = WriteSorts(dataSet);
            if (sorts is not null)
            {
                dataSetElement.Add(sorts);
            }

            dataSetsElement.Add(dataSetElement);
        }

        return dataSetsElement;
    }

    private XElement? WriteDataSetParameters(ReportDataSetDefinition dataSet)
    {
        if (dataSet.Parameters.Count == 0)
        {
            return null;
        }

        var parametersElement = new XElement(_xmlNamespace + "QueryParameters");
        for (var index = 0; index < dataSet.Parameters.Count; index++)
        {
            var parameter = dataSet.Parameters[index];
            parametersElement.Add(
                new XElement(
                    _xmlNamespace + "QueryParameter",
                    new XAttribute("Name", ResolveName(parameter.Name, $"Parameter{index + 1}")),
                    new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(parameter.ValueExpression) ?? "=Nothing")));
        }

        return parametersElement;
    }

    private XElement WriteFields(ReportDataSetDefinition dataSet)
    {
        var fieldsElement = new XElement(_xmlNamespace + "Fields");
        var fieldIndex = 0;
        foreach (var field in dataSet.ExpectedFields)
        {
            fieldsElement.Add(
                new XElement(
                    _xmlNamespace + "Field",
                    new XAttribute("Name", ResolveName(field.Name, $"Field{fieldIndex + 1}")),
                    new XElement(_xmlNamespace + "DataField", field.Name)));
            fieldIndex++;
        }

        foreach (var field in dataSet.CalculatedFields)
        {
            fieldsElement.Add(
                new XElement(
                    _xmlNamespace + "Field",
                    new XAttribute("Name", ResolveName(field.Name, $"Field{fieldIndex + 1}")),
                    new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(field.Expression) ?? "=Nothing")));
            fieldIndex++;
        }

        return fieldsElement;
    }

    private XElement? WriteFilters(ReportDataSetDefinition dataSet)
    {
        if (dataSet.Filters.Count == 0)
        {
            return null;
        }

        var filtersElement = new XElement(_xmlNamespace + "Filters");
        for (var index = 0; index < dataSet.Filters.Count; index++)
        {
            var filter = dataSet.Filters[index];
            filtersElement.Add(
                new XElement(
                    _xmlNamespace + "Filter",
                    new XElement(_xmlNamespace + "FilterExpression", ReportRdlExpressions.ToRdlExpression(filter.Expression) ?? "=Nothing"),
                    new XElement(_xmlNamespace + "Operator", FilterOperatorToRdl(filter.Operator)),
                    new XElement(
                        _xmlNamespace + "FilterValues",
                        new XElement(_xmlNamespace + "FilterValue", ReportRdlExpressions.ToRdlExpression(filter.ValueExpression) ?? "=Nothing"))));
        }

        return filtersElement;
    }

    private XElement? WriteSorts(ReportDataSetDefinition dataSet)
    {
        if (dataSet.Sorts.Count == 0)
        {
            return null;
        }

        var sortsElement = new XElement(_xmlNamespace + "SortExpressions");
        for (var index = 0; index < dataSet.Sorts.Count; index++)
        {
            var sort = dataSet.Sorts[index];
            sortsElement.Add(
                new XElement(
                    _xmlNamespace + "SortExpression",
                    new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(sort.Expression) ?? "=Nothing"),
                    new XElement(_xmlNamespace + "Direction", sort.Direction == ReportSortDirection.Descending ? "Descending" : "Ascending")));
        }

        return sortsElement;
    }

    private XElement? WriteParameters()
    {
        if (_reportDefinition.Parameters.Count == 0)
        {
            return null;
        }

        var parametersElement = new XElement(_xmlNamespace + "ReportParameters");
        for (var index = 0; index < _reportDefinition.Parameters.Count; index++)
        {
            var parameter = _reportDefinition.Parameters[index];
            var element = new XElement(
                _xmlNamespace + "ReportParameter",
                new XAttribute("Name", ResolveName(parameter.Id, $"Parameter{index + 1}")),
                new XElement(_xmlNamespace + "DataType", ParameterDataTypeToRdl(parameter.DataType)));

            var prompt = string.IsNullOrWhiteSpace(parameter.Prompt) ? parameter.DisplayName : parameter.Prompt;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                element.Add(new XElement(_xmlNamespace + "Prompt", prompt));
            }

            if (parameter.AllowNull)
            {
                element.Add(new XElement(_xmlNamespace + "Nullable", true));
            }

            if (parameter.IsMultiValue)
            {
                element.Add(new XElement(_xmlNamespace + "MultiValue", true));
            }

            if (!string.IsNullOrWhiteSpace(parameter.DefaultValueExpression))
            {
                element.Add(
                    new XElement(
                        _xmlNamespace + "DefaultValue",
                        new XElement(
                            _xmlNamespace + "Values",
                            new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(parameter.DefaultValueExpression)))));
            }

            if (!string.IsNullOrWhiteSpace(parameter.AvailableValuesDataSetId)
                && !string.IsNullOrWhiteSpace(parameter.ValueField))
            {
                var dataSetReference = new XElement(
                    _xmlNamespace + "DataSetReference",
                    new XElement(_xmlNamespace + "DataSetName", parameter.AvailableValuesDataSetId),
                    new XElement(_xmlNamespace + "ValueField", parameter.ValueField));
                if (!string.IsNullOrWhiteSpace(parameter.LabelField))
                {
                    dataSetReference.Add(new XElement(_xmlNamespace + "LabelField", parameter.LabelField));
                }

                element.Add(new XElement(_xmlNamespace + "ValidValues", dataSetReference));
            }

            if (parameter.Visibility != ReportParameterVisibility.Visible)
            {
                element.Add(new XElement(_xmlNamespace + "Hidden", true));
                if (parameter.Visibility == ReportParameterVisibility.Internal)
                {
                    _diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        $"Parameter '{parameter.Id}' uses Internal visibility, which is exported as Hidden in RDL.",
                        $"/Report/ReportParameters/ReportParameter[{index}]"));
                }
            }

            parametersElement.Add(element);
        }

        return parametersElement;
    }

    private void WriteBodyAndPage(XElement parent, ReportSection section)
    {
        parent.Add(WriteBody(section));
        parent.Add(new XElement(_xmlNamespace + "Width", ReportRdlMeasurements.Format(ComputeBodyWidth(section))));
        parent.Add(WritePage(section));
    }

    private XElement WriteBody(ReportSection section)
    {
        return new XElement(
            _xmlNamespace + "Body",
            new XElement(_xmlNamespace + "ReportItems", WriteItems(section.BodyItems, "/Report/Body/ReportItems")),
            new XElement(_xmlNamespace + "Height", ReportRdlMeasurements.Format(ComputeItemExtent(section.BodyItems, static item => GetBottom(item), 96f))));
    }

    private XElement WritePage(ReportSection section)
    {
        var page = new XElement(
            _xmlNamespace + "Page",
            new XElement(_xmlNamespace + "PageHeight", ReportRdlMeasurements.Format(section.PageSettings.Height)),
            new XElement(_xmlNamespace + "PageWidth", ReportRdlMeasurements.Format(section.PageSettings.Width)),
            new XElement(_xmlNamespace + "LeftMargin", ReportRdlMeasurements.Format(section.PageSettings.MarginLeft)),
            new XElement(_xmlNamespace + "RightMargin", ReportRdlMeasurements.Format(section.PageSettings.MarginRight)),
            new XElement(_xmlNamespace + "TopMargin", ReportRdlMeasurements.Format(section.PageSettings.MarginTop)),
            new XElement(_xmlNamespace + "BottomMargin", ReportRdlMeasurements.Format(section.PageSettings.MarginBottom)),
            new XElement(_xmlNamespace + "Columns", Math.Max(1, section.PageSettings.ColumnCount)),
            new XElement(_xmlNamespace + "ColumnSpacing", ReportRdlMeasurements.Format(section.PageSettings.ColumnGap)));

        if (section.HeaderItems.Count > 0)
        {
            page.Add(WriteHeaderFooter("PageHeader", section.HeaderItems, "/Report/Page/PageHeader"));
        }

        if (section.FooterItems.Count > 0)
        {
            page.Add(WriteHeaderFooter("PageFooter", section.FooterItems, "/Report/Page/PageFooter"));
        }

        return page;
    }

    private XElement WriteHeaderFooter(string elementName, IReadOnlyList<ReportItem> items, string path)
    {
        return new XElement(
            _xmlNamespace + elementName,
            new XElement(_xmlNamespace + "Height", ReportRdlMeasurements.Format(ComputeItemExtent(items, static item => GetBottom(item), 24f))),
            new XElement(_xmlNamespace + "PrintOnFirstPage", true),
            new XElement(_xmlNamespace + "PrintOnLastPage", true),
            new XElement(_xmlNamespace + "ReportItems", WriteItems(items, path + "/ReportItems")));
    }

    private IEnumerable<XElement> WriteItems(IReadOnlyList<ReportItem> items, string path)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var element = WriteItem(item, $"{path}/{item.GetType().Name}[{index}]");
            if (element is not null)
            {
                yield return element;
            }
        }
    }

    private XElement? WriteItem(ReportItem item, string path)
    {
        return item switch
        {
            TextItem textItem => WriteTextbox(textItem),
            ImageItem imageItem => WriteImage(imageItem, path),
            LineItem lineItem => WriteLine(lineItem),
            ShapeItem shapeItem => WriteShape(shapeItem, path),
            ChartItem chartItem => WriteChart(chartItem, path),
            TablixItem tablixItem => WriteTablix(tablixItem, path),
            SubreportItem subreportItem => WriteSubreport(subreportItem),
            DocumentTemplateItem => SkipUnsupported(path, $"Document template item '{item.Name}' is not supported by RDL export."),
            _ => SkipUnsupported(path, $"Report item '{item.GetType().Name}' is not supported by RDL export.")
        };
    }

    private XElement WriteTextbox(TextItem item)
    {
        var element = new XElement(
            _xmlNamespace + "Textbox",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Textbox")),
            new XElement(_xmlNamespace + "CanGrow", item.CanGrow),
            new XElement(_xmlNamespace + "CanShrink", item.CanShrink),
            new XElement(
                _xmlNamespace + "Paragraphs",
                new XElement(
                    _xmlNamespace + "Paragraph",
                    new XElement(
                        _xmlNamespace + "TextRuns",
                        new XElement(
                            _xmlNamespace + "TextRun",
                            new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToTextboxValue(item.StaticText, item.ValueExpression)))))));

        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, item.FormatString);
        return element;
    }

    private XElement WriteImage(ImageItem item, string path)
    {
        var element = new XElement(
            _xmlNamespace + "Image",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Image")));

        switch (item.SourceKind)
        {
            case ReportImageSourceKind.Embedded:
            {
                var embeddedName = BuildEmbeddedImageName(item);
                element.Add(new XElement(_xmlNamespace + "Source", "Embedded"));
                element.Add(new XElement(_xmlNamespace + "Value", embeddedName));
                if (!string.IsNullOrWhiteSpace(item.MimeType))
                {
                    element.Add(new XElement(_xmlNamespace + "MIMEType", item.MimeType));
                }

                if (item.EmbeddedData is not null)
                {
                    _embeddedImages.Add(
                        new XElement(
                            _xmlNamespace + "EmbeddedImage",
                            new XAttribute("Name", embeddedName),
                            new XElement(_xmlNamespace + "MIMEType", string.IsNullOrWhiteSpace(item.MimeType) ? "image/png" : item.MimeType),
                            new XElement(_xmlNamespace + "ImageData", Convert.ToBase64String(item.EmbeddedData))));
                }

                break;
            }

            case ReportImageSourceKind.Uri:
                element.Add(new XElement(_xmlNamespace + "Source", "External"));
                element.Add(new XElement(_xmlNamespace + "Value", item.ValueExpression ?? string.Empty));
                break;

            default:
                element.Add(new XElement(_xmlNamespace + "Source", "Database"));
                element.Add(new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(item.ValueExpression) ?? "=Nothing"));
                if (!string.IsNullOrWhiteSpace(item.MimeType))
                {
                    element.Add(new XElement(_xmlNamespace + "MIMEType", item.MimeType));
                }

                break;
        }

        element.Add(new XElement(_xmlNamespace + "Sizing", SizingModeToRdl(item.SizingMode)));
        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private XElement WriteLine(LineItem item)
    {
        var element = new XElement(
            _xmlNamespace + "Line",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Line")));
        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private XElement? WriteShape(ShapeItem item, string path)
    {
        if (item.Shape != ReportShapeKind.Rectangle)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                $"Shape '{item.Shape}' is exported as a Rectangle because RDL shape coverage in v1 is limited.",
                path));
        }

        var element = new XElement(
            _xmlNamespace + "Rectangle",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Rectangle")));
        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private XElement WriteChart(ChartItem item, string path)
    {
        var element = new XElement(
            _xmlNamespace + "Chart",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Chart")));

        var categoryMember = new XElement(_xmlNamespace + "ChartMember");
        if (!string.IsNullOrWhiteSpace(item.CategoryExpression))
        {
            categoryMember.Add(
                new XElement(
                    _xmlNamespace + "Group",
                    new XAttribute("Name", ResolveName(item.Id, "Chart", "CategoryGroup")),
                    new XElement(
                        _xmlNamespace + "GroupExpressions",
                        new XElement(_xmlNamespace + "GroupExpression", ReportRdlExpressions.ToRdlExpression(item.CategoryExpression)))));
            categoryMember.Add(new XElement(_xmlNamespace + "Label", ReportRdlExpressions.ToRdlScalarValue(item.CategoryExpression) ?? string.Empty));
        }

        element.Add(
            new XElement(
                _xmlNamespace + "ChartCategoryHierarchy",
                new XElement(_xmlNamespace + "ChartMembers", categoryMember)));

        var chartMembers = new XElement(_xmlNamespace + "ChartMembers");
        var seriesCollection = new XElement(_xmlNamespace + "ChartSeriesCollection");
        for (var index = 0; index < item.Series.Count; index++)
        {
            var series = item.Series[index];
            var member = new XElement(_xmlNamespace + "ChartMember");
            if (!string.IsNullOrWhiteSpace(series.NameExpression) && series.NameExpression.Contains("Fields.", StringComparison.Ordinal))
            {
                member.Add(
                    new XElement(
                        _xmlNamespace + "Group",
                        new XAttribute("Name", ResolveName(item.Id, $"Series{index + 1}", "Group")),
                        new XElement(
                            _xmlNamespace + "GroupExpressions",
                            new XElement(_xmlNamespace + "GroupExpression", ReportRdlExpressions.ToRdlExpression(series.NameExpression)))));
            }

            member.Add(new XElement(_xmlNamespace + "Label", ReportRdlExpressions.ToRdlScalarValue(series.NameExpression) ?? $"Series {index + 1}"));
            chartMembers.Add(member);

            var chartSeries = new XElement(
                _xmlNamespace + "ChartSeries",
                new XAttribute("Name", ResolveName(item.Id, $"Series{index + 1}")),
                new XElement(_xmlNamespace + "Type", "Column"),
                new XElement(_xmlNamespace + "Subtype", "Plain"),
                new XElement(
                    _xmlNamespace + "ChartDataPoints",
                    new XElement(
                        _xmlNamespace + "ChartDataPoint",
                        new XElement(
                            _xmlNamespace + "ChartDataPointValues",
                            new XElement(_xmlNamespace + "Y", ReportRdlExpressions.ToRdlExpression(series.ValueExpression) ?? "=Nothing")))));

            if (!string.IsNullOrWhiteSpace(series.ColorExpression))
            {
                chartSeries.Add(
                    new XElement(
                        _xmlNamespace + "Style",
                        new XElement(_xmlNamespace + "Color", ReportRdlExpressions.ToRdlScalarValue(series.ColorExpression) ?? series.ColorExpression)));
            }

            seriesCollection.Add(chartSeries);
        }

        element.Add(new XElement(_xmlNamespace + "ChartSeriesHierarchy", chartMembers));
        element.Add(new XElement(_xmlNamespace + "ChartData", seriesCollection));
        element.Add(new XElement(_xmlNamespace + "ChartAreas", new XElement(_xmlNamespace + "ChartArea", new XAttribute("Name", "Default"))));
        if (!string.IsNullOrWhiteSpace(item.DataSetId))
        {
            element.Add(new XElement(_xmlNamespace + "DataSetName", item.DataSetId));
        }

        if (!string.IsNullOrWhiteSpace(item.TitleExpression))
        {
            element.Add(
                new XElement(
                    _xmlNamespace + "ChartTitles",
                    new XElement(
                        _xmlNamespace + "ChartTitle",
                        new XElement(_xmlNamespace + "Caption", ReportRdlExpressions.ToRdlScalarValue(item.TitleExpression) ?? string.Empty))));
        }

        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private XElement WriteTablix(TablixItem item, string path)
    {
        var element = new XElement(
            _xmlNamespace + "Tablix",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Tablix")));

        var tablixColumns = new XElement(_xmlNamespace + "TablixColumns");
        for (var columnIndex = 0; columnIndex < item.Columns.Count; columnIndex++)
        {
            tablixColumns.Add(
                new XElement(
                    _xmlNamespace + "TablixColumn",
                    new XElement(
                        _xmlNamespace + "Width",
                        ReportRdlMeasurements.Format(item.Columns[columnIndex].Width > 0f ? item.Columns[columnIndex].Width : 120f))));
        }

        var detailRowCount = item.Rows.Count(static row => !row.IsHeader);
        if (detailRowCount > 1 && !string.IsNullOrWhiteSpace(item.DataSetId))
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                $"Tablix '{item.Name}' contains multiple detail rows; only one flat detail group is expressible in the current RDL export subset.",
                path));
        }

        var rowHeight = item.Rows.Count > 0
            ? Math.Max(18f, item.Bounds.Height > 0f ? item.Bounds.Height / item.Rows.Count : 24f)
            : 24f;

        var tablixRows = new XElement(_xmlNamespace + "TablixRows");
        for (var rowIndex = 0; rowIndex < item.Rows.Count; rowIndex++)
        {
            var row = item.Rows[rowIndex];
            var rowElement = new XElement(
                _xmlNamespace + "TablixRow",
                new XElement(_xmlNamespace + "Height", ReportRdlMeasurements.Format(rowHeight)));
            var cellsElement = new XElement(_xmlNamespace + "TablixCells");
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var cellElement = new XElement(
                    _xmlNamespace + "TablixCell",
                    new XElement(
                        _xmlNamespace + "CellContents",
                        WriteTablixCellTextbox(item, row, cell, rowIndex, cellIndex)));
                if (cell.ColumnSpan > 1)
                {
                    cellElement.Add(new XElement(_xmlNamespace + "ColSpan", cell.ColumnSpan));
                }

                if (cell.RowSpan > 1)
                {
                    _diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        $"Tablix cell row span '{cell.RowSpan}' is not emitted because the current RDL export subset targets flat tablix cells.",
                        $"{path}/Rows[{rowIndex}]/Cells[{cellIndex}]"));
                }

                cellsElement.Add(cellElement);
            }

            rowElement.Add(cellsElement);
            tablixRows.Add(rowElement);
        }

        element.Add(
            new XElement(
                _xmlNamespace + "TablixBody",
                tablixColumns,
                tablixRows));
        element.Add(WriteTablixColumnHierarchy(item));
        element.Add(WriteTablixRowHierarchy(item));
        if (!string.IsNullOrWhiteSpace(item.DataSetId))
        {
            element.Add(new XElement(_xmlNamespace + "DataSetName", item.DataSetId));
        }

        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private XElement WriteTablixCellTextbox(
        TablixItem tablix,
        ReportTablixRowDefinition row,
        ReportTablixCellDefinition cell,
        int rowIndex,
        int cellIndex)
    {
        var textbox = new XElement(
            _xmlNamespace + "Textbox",
            new XAttribute("Name", ResolveName(tablix.Id, $"Cell{rowIndex + 1}_{cellIndex + 1}", "Textbox")),
            new XElement(_xmlNamespace + "CanGrow", !row.IsHeader),
            new XElement(
                _xmlNamespace + "Paragraphs",
                new XElement(
                    _xmlNamespace + "Paragraph",
                    new XElement(
                        _xmlNamespace + "TextRuns",
                        new XElement(
                            _xmlNamespace + "TextRun",
                            new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToTextboxValue(cell.Text, cell.ValueExpression)))))));

        AppendStyle(textbox, cell.StyleName, cell.FormatString);
        return textbox;
    }

    private XElement WriteTablixColumnHierarchy(TablixItem item)
    {
        var members = new XElement(_xmlNamespace + "TablixMembers");
        var count = Math.Max(1, item.Columns.Count);
        for (var index = 0; index < count; index++)
        {
            members.Add(new XElement(_xmlNamespace + "TablixMember"));
        }

        return new XElement(_xmlNamespace + "TablixColumnHierarchy", members);
    }

    private XElement WriteTablixRowHierarchy(TablixItem item)
    {
        var members = new XElement(_xmlNamespace + "TablixMembers");
        var detailGroupWritten = false;
        for (var index = 0; index < item.Rows.Count; index++)
        {
            var row = item.Rows[index];
            var member = new XElement(_xmlNamespace + "TablixMember");
            if (row.IsHeader)
            {
                if (item.RepeatHeaderRows)
                {
                    member.Add(new XElement(_xmlNamespace + "RepeatOnNewPage", true));
                    member.Add(new XElement(_xmlNamespace + "KeepWithGroup", "After"));
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.DataSetId) && !detailGroupWritten)
            {
                member.Add(new XElement(_xmlNamespace + "Group", new XAttribute("Name", ResolveName(item.Id, "Details", "Group"))));
                detailGroupWritten = true;
            }

            members.Add(member);
        }

        return new XElement(_xmlNamespace + "TablixRowHierarchy", members);
    }

    private XElement WriteSubreport(SubreportItem item)
    {
        var element = new XElement(
            _xmlNamespace + "Subreport",
            new XAttribute("Name", ResolveName(item.Name, item.Id, "Subreport")),
            new XElement(_xmlNamespace + "ReportName", item.ReportReferenceId));

        if (item.Parameters.Count > 0)
        {
            var parametersElement = new XElement(_xmlNamespace + "Parameters");
            for (var index = 0; index < item.Parameters.Count; index++)
            {
                var parameter = item.Parameters[index];
                parametersElement.Add(
                    new XElement(
                        _xmlNamespace + "Parameter",
                        new XAttribute("Name", ResolveName(parameter.ParameterId, $"Parameter{index + 1}")),
                        new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(parameter.ValueExpression) ?? "=Nothing")));
            }

            element.Add(parametersElement);
        }

        WriteCommonItemProperties(element, item);
        AppendStyle(element, item.StyleName, null);
        return element;
    }

    private void WriteCommonItemProperties(XElement element, ReportItem item)
    {
        switch (item)
        {
            case LineItem lineItem:
                WriteLineBounds(element, lineItem);
                break;

            default:
                element.Add(new XElement(_xmlNamespace + "Top", ReportRdlMeasurements.Format(item.Bounds.Y)));
                element.Add(new XElement(_xmlNamespace + "Left", ReportRdlMeasurements.Format(item.Bounds.X)));
                element.Add(new XElement(_xmlNamespace + "Height", ReportRdlMeasurements.Format(Math.Max(0f, item.Bounds.Height))));
                element.Add(new XElement(_xmlNamespace + "Width", ReportRdlMeasurements.Format(Math.Max(0f, item.Bounds.Width))));
                break;
        }

        if (!string.IsNullOrWhiteSpace(item.VisibilityExpression))
        {
            element.Add(
                new XElement(
                    _xmlNamespace + "Visibility",
                    new XElement(_xmlNamespace + "Hidden", ReportRdlExpressions.ToRdlExpression(item.VisibilityExpression))));
        }

        if (!string.IsNullOrWhiteSpace(item.BookmarkExpression))
        {
            var bookmarkValue = ReportRdlExpressions.ToRdlScalarValue(item.BookmarkExpression) ?? string.Empty;
            element.Add(new XElement(_xmlNamespace + "Bookmark", bookmarkValue));
            element.Add(new XElement(_xmlNamespace + "DocumentMapLabel", bookmarkValue));
        }

        if (!string.IsNullOrWhiteSpace(item.TooltipExpression))
        {
            element.Add(new XElement(_xmlNamespace + "ToolTip", ReportRdlExpressions.ToRdlScalarValue(item.TooltipExpression) ?? string.Empty));
        }

        if (item.DrillthroughAction is not null)
        {
            var parameters = new XElement(_xmlNamespace + "Parameters");
            for (var index = 0; index < item.DrillthroughAction.Parameters.Count; index++)
            {
                var parameter = item.DrillthroughAction.Parameters[index];
                parameters.Add(
                    new XElement(
                        _xmlNamespace + "Parameter",
                        new XAttribute("Name", ResolveName(parameter.ParameterId, $"Parameter{index + 1}")),
                        new XElement(_xmlNamespace + "Value", ReportRdlExpressions.ToRdlExpression(parameter.ValueExpression) ?? "=Nothing")));
            }

            element.Add(
                new XElement(
                    _xmlNamespace + "ActionInfo",
                    new XElement(
                        _xmlNamespace + "Actions",
                        new XElement(
                            _xmlNamespace + "Action",
                            new XElement(
                                _xmlNamespace + "Drillthrough",
                                new XElement(_xmlNamespace + "ReportName", item.DrillthroughAction.ReportReferenceId),
                                item.DrillthroughAction.Parameters.Count == 0 ? null : parameters)))));
        }
    }

    private void WriteLineBounds(XElement element, LineItem item)
    {
        var left = MathF.Min(item.Bounds.X, item.X2);
        var top = MathF.Min(item.Bounds.Y, item.Y2);
        var width = MathF.Abs(item.X2 - item.Bounds.X);
        var height = MathF.Abs(item.Y2 - item.Bounds.Y);

        element.Add(new XElement(_xmlNamespace + "Top", ReportRdlMeasurements.Format(top)));
        element.Add(new XElement(_xmlNamespace + "Left", ReportRdlMeasurements.Format(left)));
        element.Add(new XElement(_xmlNamespace + "Height", ReportRdlMeasurements.Format(height)));
        element.Add(new XElement(_xmlNamespace + "Width", ReportRdlMeasurements.Format(width)));
    }

    private void AppendStyle(XElement element, string? styleName, string? formatString)
    {
        ReportStyleDefinition? style = null;
        if (!string.IsNullOrWhiteSpace(styleName) && !_styles.TryGetValue(styleName, out style))
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                $"Style '{styleName}' could not be resolved for RDL export and was omitted.",
                $"/Report/Styles/{styleName}"));
        }

        var styleElement = ReportRdlStyleCatalog.CreateStyleElement(style, formatString, _xmlNamespace);
        if (styleElement is not null)
        {
            element.Add(styleElement);
        }
    }

    private XElement? SkipUnsupported(string path, string message)
    {
        _diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Warning,
            ReportDiagnosticCodes.UnsupportedFeature,
            message,
            path));
        return null;
    }

    private string BuildConnectionString(ReportDataSourceDefinition dataSource)
    {
        if (dataSource.Options.TryGetValue("connectionString", out var connectionString) && !string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (dataSource.Options.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            ";",
            dataSource.Options.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private string BuildEmbeddedImageName(ImageItem item)
    {
        return ResolveName(item.Id, item.Name, "EmbeddedImage");
    }

    private float ComputeBodyWidth(ReportSection section)
    {
        var contentWidth = Math.Max(1f, section.PageSettings.Width - section.PageSettings.MarginLeft - section.PageSettings.MarginRight);
        var itemWidth = ComputeItemExtent(section.BodyItems, static item => GetRight(item), contentWidth);
        return Math.Max(contentWidth, itemWidth);
    }

    private static float ComputeItemExtent(
        IReadOnlyList<ReportItem> items,
        Func<ReportItem, float> selector,
        float fallback)
    {
        if (items.Count == 0)
        {
            return fallback;
        }

        var maximum = fallback;
        for (var index = 0; index < items.Count; index++)
        {
            maximum = MathF.Max(maximum, selector(items[index]));
        }

        return maximum;
    }

    private static float GetRight(ReportItem item)
    {
        return item switch
        {
            LineItem lineItem => MathF.Max(lineItem.Bounds.X, lineItem.X2),
            _ => item.Bounds.X + item.Bounds.Width
        };
    }

    private static float GetBottom(ReportItem item)
    {
        return item switch
        {
            LineItem lineItem => MathF.Max(lineItem.Bounds.Y, lineItem.Y2),
            _ => item.Bounds.Y + item.Bounds.Height
        };
    }

    private static string ParameterDataTypeToRdl(ReportParameterDataType dataType)
    {
        return dataType switch
        {
            ReportParameterDataType.Integer => "Integer",
            ReportParameterDataType.Number => "Float",
            ReportParameterDataType.Decimal => "Float",
            ReportParameterDataType.Boolean => "Boolean",
            ReportParameterDataType.Date => "DateTime",
            ReportParameterDataType.DateTime => "DateTime",
            _ => "String"
        };
    }

    private static string FilterOperatorToRdl(ReportFilterOperator filterOperator)
    {
        return filterOperator switch
        {
            ReportFilterOperator.NotEqual => "NotEqual",
            ReportFilterOperator.GreaterThan => "GreaterThan",
            ReportFilterOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
            ReportFilterOperator.LessThan => "LessThan",
            ReportFilterOperator.LessThanOrEqual => "LessThanOrEqual",
            ReportFilterOperator.Contains => "Like",
            _ => "Equal"
        };
    }

    private static string SizingModeToRdl(ReportSizingMode sizingMode)
    {
        return sizingMode switch
        {
            ReportSizingMode.Stretch => "Stretch",
            ReportSizingMode.FitProportional => "FitProportional",
            _ => "OriginalSize"
        };
    }

    private static string ResolveName(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return SanitizeName(candidate);
            }
        }

        return "Item";
    }

    private static string SanitizeName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "Item";
        }

        var buffer = new char[trimmed.Length];
        var writeIndex = 0;
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (char.IsLetterOrDigit(current) || current == '_')
            {
                buffer[writeIndex++] = current;
            }
            else if (writeIndex == 0 || buffer[writeIndex - 1] != '_')
            {
                buffer[writeIndex++] = '_';
            }
        }

        if (writeIndex == 0)
        {
            return "Item";
        }

        if (!char.IsLetter(buffer[0]) && buffer[0] != '_')
        {
            return "_" + new string(buffer, 0, writeIndex);
        }

        return new string(buffer, 0, writeIndex);
    }
}
