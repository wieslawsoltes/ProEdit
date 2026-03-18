using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting.Rdl;

internal sealed class ReportRdlImporter
{
    private readonly List<ReportDiagnostic> _diagnostics = new();
    private readonly Dictionary<string, EmbeddedImageInfo> _embeddedImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReportRdlStyleCatalog _styleCatalog = new();

    private XNamespace _xmlNamespace = XNamespace.None;
    private XNamespace? _defaultFontNamespace;

    public ReportRdlReadResult Read(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlParseFailed,
                "The RDL payload is empty.",
                "/Report"));
            return new ReportRdlReadResult(null, _diagnostics);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlParseFailed,
                exception.Message,
                "/Report"));
            return new ReportRdlReadResult(null, _diagnostics);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Report")
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlParseFailed,
                "The RDL payload does not contain a Report root element.",
                "/Report"));
            return new ReportRdlReadResult(null, _diagnostics);
        }

        _xmlNamespace = root.Name.Namespace;
        _defaultFontNamespace = root.GetNamespaceOfPrefix("df");
        if (!ReportRdlNamespaces.TryGetVersion(_xmlNamespace, out var version))
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlNamespaceUnsupported,
                $"The RDL namespace '{_xmlNamespace.NamespaceName}' is not supported.",
                "/Report"));
            return new ReportRdlReadResult(null, _diagnostics);
        }

        var reportId = root.Element(ReportRdlNamespaces.Designer + "ReportID")?.Value;
        var description = root.Element(_xmlNamespace + "Description")?.Value;
        var report = new ReportDefinition
        {
            SchemaVersion = ReportDefinition.CurrentSchemaVersion,
            Id = string.IsNullOrWhiteSpace(reportId) ? "rdl-report" : reportId.Trim(),
            Name = !string.IsNullOrWhiteSpace(description)
                ? description.Trim()
                : string.IsNullOrWhiteSpace(reportId)
                    ? "RDL Report"
                    : reportId.Trim()
        };

        report.Metadata["rdlVersion"] = version.ToString();
        report.Metadata["rdlNamespace"] = _xmlNamespace.NamespaceName;
        ReadReportMetadata(root, report);

        ReadEmbeddedImages(root);
        ReadDataSources(root, report);
        ReadDataSets(root, report);
        ReadParameters(root, report);
        ReadParameterLayout(root, report);
        ReadSections(root, report);
        _styleCatalog.CopyTo(report);

        if (report.Sections.Count == 0)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "The RDL payload did not produce any report sections.",
                "/Report"));
            return new ReportRdlReadResult(null, _diagnostics);
        }

        return new ReportRdlReadResult(report, _diagnostics);
    }

    private void ReadEmbeddedImages(XElement root)
    {
        var imagesElement = root.Element(_xmlNamespace + "EmbeddedImages");
        if (imagesElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var imageElement in imagesElement.Elements(_xmlNamespace + "EmbeddedImage"))
        {
            var path = $"/Report/EmbeddedImages/EmbeddedImage[{index}]";
            var name = (string?)imageElement.Attribute("Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                index++;
                continue;
            }

            var imageData = imageElement.Element(_xmlNamespace + "ImageData")?.Value;
            if (string.IsNullOrWhiteSpace(imageData))
            {
                index++;
                continue;
            }

            try
            {
                _embeddedImages[name.Trim()] = new EmbeddedImageInfo(
                    Convert.FromBase64String(imageData.Trim()),
                    imageElement.Element(_xmlNamespace + "MIMEType")?.Value);
            }
            catch (FormatException exception)
            {
                _diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Warning,
                    ReportDiagnosticCodes.RdlParseFailed,
                    $"Embedded image '{name}' could not be decoded: {exception.Message}",
                    path));
            }

            index++;
        }
    }

    private void ReadReportMetadata(XElement root, ReportDefinition report)
    {
        report.ConsumeContainerWhitespace = ParseBoolean(root.Element(_xmlNamespace + "ConsumeContainerWhitespace")?.Value);
        report.DefaultFontFamily = ReadDefaultFontFamily(root);

        var code = root.Element(_xmlNamespace + "Code")?.Value;
        if (!string.IsNullOrWhiteSpace(code))
        {
            report.Metadata["rdlCode"] = code.Trim();
        }

        var customProperties = root.Element(_xmlNamespace + "CustomProperties");
        if (customProperties is null)
        {
            return;
        }

        var index = 0;
        foreach (var propertyElement in customProperties.Elements(_xmlNamespace + "CustomProperty"))
        {
            var name = propertyElement.Element(_xmlNamespace + "Name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                index++;
                continue;
            }

            var value = propertyElement.Element(_xmlNamespace + "Value")?.Value ?? string.Empty;
            report.Metadata["rdlCustomProperty:" + name] = value;
            index++;
        }
    }

    private string? ReadDefaultFontFamily(XElement root)
    {
        var defaultFontElement = _defaultFontNamespace is not null
            ? root.Element(_defaultFontNamespace + "DefaultFontFamily")
            : null;

        if (defaultFontElement is null)
        {
            defaultFontElement = root.Elements().FirstOrDefault(static element => string.Equals(element.Name.LocalName, "DefaultFontFamily", StringComparison.Ordinal));
        }

        var value = defaultFontElement?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void ReadDataSources(XElement root, ReportDefinition report)
    {
        var dataSourcesElement = root.Element(_xmlNamespace + "DataSources");
        if (dataSourcesElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var dataSourceElement in dataSourcesElement.Elements(_xmlNamespace + "DataSource"))
        {
            var path = $"/Report/DataSources/DataSource[{index}]";
            var definition = new ReportDataSourceDefinition
            {
                Id = ((string?)dataSourceElement.Attribute("Name"))?.Trim() ?? $"DataSource{index + 1}"
            };

            var dataSourceReference = dataSourceElement.Element(_xmlNamespace + "DataSourceReference")?.Value;
            if (!string.IsNullOrWhiteSpace(dataSourceReference))
            {
                definition.ConnectionName = dataSourceReference.Trim();
            }
            else
            {
                var connection = dataSourceElement.Element(_xmlNamespace + "ConnectionProperties");
                var providerId = connection?.Element(_xmlNamespace + "DataProvider")?.Value?.Trim() ?? string.Empty;
                definition.ProviderId = NormalizeRdlProviderId(providerId);
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    definition.Options["rdlDataProvider"] = providerId;
                }

                var connectString = connection?.Element(_xmlNamespace + "ConnectString")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(connectString))
                {
                    definition.Options["connectionString"] = connectString;
                    ParseConnectionStringOptions(connectString, definition.Options);
                }
            }

            report.DataSources.Add(definition);
            index++;
        }
    }

    private void ReadDataSets(XElement root, ReportDefinition report)
    {
        var dataSetsElement = root.Element(_xmlNamespace + "DataSets");
        if (dataSetsElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var dataSetElement in dataSetsElement.Elements(_xmlNamespace + "DataSet"))
        {
            var path = $"/Report/DataSets/DataSet[{index}]";
            var definition = new ReportDataSetDefinition
            {
                Id = ((string?)dataSetElement.Attribute("Name"))?.Trim() ?? $"DataSet{index + 1}"
            };

            var queryElement = dataSetElement.Element(_xmlNamespace + "Query");
            definition.DataSourceId = queryElement?.Element(_xmlNamespace + "DataSourceName")?.Value?.Trim() ?? string.Empty;
            definition.Query = queryElement?.Element(_xmlNamespace + "CommandText")?.Value ?? string.Empty;

            ReadDataSetParameters(queryElement, definition, path + "/Query");
            ReadFieldDefinitions(dataSetElement, definition, path);
            ReadFilters(dataSetElement, definition, path);
            ReadSorts(dataSetElement, definition, path);

            report.DataSets.Add(definition);
            index++;
        }
    }

    private void ReadDataSetParameters(XElement? queryElement, ReportDataSetDefinition definition, string path)
    {
        var parametersElement = queryElement?.Element(_xmlNamespace + "QueryParameters");
        if (parametersElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var parameterElement in parametersElement.Elements(_xmlNamespace + "QueryParameter"))
        {
            definition.Parameters.Add(new ReportDataSetParameterDefinition
            {
                Name = ((string?)parameterElement.Attribute("Name"))?.Trim() ?? $"Parameter{index + 1}",
                ValueExpression = ReportRdlExpressions.ToNativeValueExpression(parameterElement.Element(_xmlNamespace + "Value")?.Value) ?? string.Empty
            });
            index++;
        }
    }

    private void ReadFieldDefinitions(XElement dataSetElement, ReportDataSetDefinition definition, string path)
    {
        var fieldsElement = dataSetElement.Element(_xmlNamespace + "Fields");
        if (fieldsElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var fieldElement in fieldsElement.Elements(_xmlNamespace + "Field"))
        {
            var fieldPath = $"{path}/Fields/Field[{index}]";
            var dataField = fieldElement.Element(_xmlNamespace + "DataField")?.Value;
            var valueExpression = fieldElement.Element(_xmlNamespace + "Value")?.Value;
            var typeName = fieldElement.Element(ReportRdlNamespaces.Designer + "TypeName")?.Value;
            var dataType = ParseFieldDataType(typeName);
            if (!string.IsNullOrWhiteSpace(valueExpression) && string.IsNullOrWhiteSpace(dataField))
            {
                definition.CalculatedFields.Add(new ReportCalculatedFieldDefinition
                {
                    Name = ((string?)fieldElement.Attribute("Name"))?.Trim() ?? $"Field{index + 1}",
                    Expression = ReportRdlExpressions.ToNativeExpression(valueExpression) ?? string.Empty
                });
            }
            else
            {
                definition.ExpectedFields.Add(new ReportFieldDefinition
                {
                    Name = string.IsNullOrWhiteSpace(dataField)
                        ? ((string?)fieldElement.Attribute("Name"))?.Trim() ?? $"Field{index + 1}"
                        : dataField.Trim(),
                    DataType = dataType
                });
            }

            index++;
        }
    }

    private void ReadFilters(XElement dataSetElement, ReportDataSetDefinition definition, string path)
    {
        var filtersElement = dataSetElement.Element(_xmlNamespace + "Filters");
        if (filtersElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var filterElement in filtersElement.Elements(_xmlNamespace + "Filter"))
        {
            var operatorText = filterElement.Element(_xmlNamespace + "Operator")?.Value?.Trim();
            definition.Filters.Add(new ReportFilterDefinition
            {
                Expression = ReportRdlExpressions.ToNativeExpression(filterElement.Element(_xmlNamespace + "FilterExpression")?.Value) ?? string.Empty,
                Operator = ParseFilterOperator(operatorText),
                ValueExpression = ReportRdlExpressions.ToNativeValueExpression(
                    filterElement.Element(_xmlNamespace + "FilterValues")?.Element(_xmlNamespace + "FilterValue")?.Value) ?? string.Empty
            });
            index++;
        }
    }

    private void ReadSorts(XElement dataSetElement, ReportDataSetDefinition definition, string path)
    {
        var sortsElement = dataSetElement.Element(_xmlNamespace + "SortExpressions");
        if (sortsElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var sortElement in sortsElement.Elements(_xmlNamespace + "SortExpression"))
        {
            definition.Sorts.Add(new ReportSortDefinition
            {
                Expression = ReportRdlExpressions.ToNativeExpression(sortElement.Element(_xmlNamespace + "Value")?.Value) ?? string.Empty,
                Direction = ParseSortDirection(sortElement.Element(_xmlNamespace + "Direction")?.Value)
            });
            index++;
        }
    }

    private void ReadParameters(XElement root, ReportDefinition report)
    {
        var parametersElement = root.Element(_xmlNamespace + "ReportParameters");
        if (parametersElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var parameterElement in parametersElement.Elements(_xmlNamespace + "ReportParameter"))
        {
            var path = $"/Report/ReportParameters/ReportParameter[{index}]";
            var definition = new ReportParameterDefinition
            {
                Id = ((string?)parameterElement.Attribute("Name"))?.Trim() ?? $"Parameter{index + 1}",
                DisplayName = parameterElement.Element(_xmlNamespace + "Prompt")?.Value?.Trim()
                    ?? ((string?)parameterElement.Attribute("Name"))?.Trim()
                    ?? $"Parameter {index + 1}",
                DataType = ParseParameterDataType(parameterElement.Element(_xmlNamespace + "DataType")?.Value),
                AllowNull = ParseBoolean(parameterElement.Element(_xmlNamespace + "Nullable")?.Value),
                IsMultiValue = ParseBoolean(parameterElement.Element(_xmlNamespace + "MultiValue")?.Value),
                Prompt = parameterElement.Element(_xmlNamespace + "Prompt")?.Value,
                Visibility = ParseBoolean(parameterElement.Element(_xmlNamespace + "Hidden")?.Value)
                    ? ReportParameterVisibility.Hidden
                    : ReportParameterVisibility.Visible
            };

            var defaultValue = parameterElement
                .Element(_xmlNamespace + "DefaultValue")
                ?.Element(_xmlNamespace + "Values")
                ?.Element(_xmlNamespace + "Value")
                ?.Value;
            definition.DefaultValueExpression = ReportRdlExpressions.ToNativeValueExpression(defaultValue);

            var validValues = parameterElement.Element(_xmlNamespace + "ValidValues")?.Element(_xmlNamespace + "DataSetReference");
            if (validValues is not null)
            {
                definition.AvailableValuesDataSetId = validValues.Element(_xmlNamespace + "DataSetName")?.Value?.Trim();
                definition.ValueField = validValues.Element(_xmlNamespace + "ValueField")?.Value?.Trim();
                definition.LabelField = validValues.Element(_xmlNamespace + "LabelField")?.Value?.Trim();
            }

            report.Parameters.Add(definition);
            index++;
        }
    }

    private void ReadParameterLayout(XElement root, ReportDefinition report)
    {
        var layoutElement = root.Element(_xmlNamespace + "ReportParametersLayout")?.Element(_xmlNamespace + "GridLayoutDefinition");
        if (layoutElement is null)
        {
            return;
        }

        var layout = report.ParameterLayout;
        layout.ColumnCount = Math.Max(1, ParseInt(layoutElement.Element(_xmlNamespace + "NumberOfColumns")?.Value, 1));
        layout.RowCount = Math.Max(1, ParseInt(layoutElement.Element(_xmlNamespace + "NumberOfRows")?.Value, 1));
        layout.Cells.Clear();

        var parameterIds = report.Parameters
            .Select(static parameter => parameter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cellDefinitions = layoutElement.Element(_xmlNamespace + "CellDefinitions");
        if (cellDefinitions is null)
        {
            return;
        }

        foreach (var cellElement in cellDefinitions.Elements(_xmlNamespace + "CellDefinition"))
        {
            var parameterName = cellElement.Element(_xmlNamespace + "ParameterName")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(parameterName) || !parameterIds.Contains(parameterName))
            {
                continue;
            }

            layout.Cells.Add(new ReportParameterLayoutCellDefinition
            {
                ParameterId = parameterName,
                ColumnIndex = Math.Max(0, ParseInt(cellElement.Element(_xmlNamespace + "ColumnIndex")?.Value, 0)),
                RowIndex = Math.Max(0, ParseInt(cellElement.Element(_xmlNamespace + "RowIndex")?.Value, 0))
            });
        }
    }

    private void ReadSections(XElement root, ReportDefinition report)
    {
        var reportSectionsElement = root.Element(_xmlNamespace + "ReportSections");
        if (reportSectionsElement is null)
        {
            var section = ReadSection(root, "/Report", 0, root.Element(_xmlNamespace + "Width"));
            report.Sections.Add(section);
            return;
        }

        var index = 0;
        foreach (var sectionElement in reportSectionsElement.Elements(_xmlNamespace + "ReportSection"))
        {
            report.Sections.Add(ReadSection(
                sectionElement,
                $"/Report/ReportSections/ReportSection[{index}]",
                index,
                sectionElement.Element(_xmlNamespace + "Width")));
            index++;
        }
    }

    private ReportSection ReadSection(XElement container, string path, int sectionIndex, XElement? widthElement)
    {
        var section = new ReportSection
        {
            Id = $"section{sectionIndex + 1}",
            Name = $"Section {sectionIndex + 1}"
        };

        ReadPageSettings(container.Element(_xmlNamespace + "Page"), widthElement?.Value, section.PageSettings, path);

        var bodyElement = container.Element(_xmlNamespace + "Body");
        ReadItemCollection(
            bodyElement?.Element(_xmlNamespace + "ReportItems"),
            path + "/Body/ReportItems",
            section.BodyItems,
            0f,
            0f);

        var pageElement = container.Element(_xmlNamespace + "Page");
        ReadHeaderFooter(
            pageElement?.Element(_xmlNamespace + "PageHeader"),
            path + "/Page/PageHeader",
            section.HeaderItems);
        ReadHeaderFooter(
            pageElement?.Element(_xmlNamespace + "PageFooter"),
            path + "/Page/PageFooter",
            section.FooterItems);

        return section;
    }

    private void ReadPageSettings(
        XElement? pageElement,
        string? bodyWidthText,
        ReportPageSettings settings,
        string path)
    {
        if (pageElement is null)
        {
            if (!string.IsNullOrWhiteSpace(bodyWidthText))
            {
                settings.Width = ReportRdlMeasurements.Parse(bodyWidthText, path + "/Width", _diagnostics, settings.Width);
            }

            return;
        }

        settings.Width = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "PageWidth")?.Value,
            path + "/Page/PageWidth",
            _diagnostics,
            settings.Width);
        settings.Height = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "PageHeight")?.Value,
            path + "/Page/PageHeight",
            _diagnostics,
            settings.Height);
        settings.MarginLeft = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "LeftMargin")?.Value,
            path + "/Page/LeftMargin",
            _diagnostics,
            0f);
        settings.MarginRight = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "RightMargin")?.Value,
            path + "/Page/RightMargin",
            _diagnostics,
            0f);
        settings.MarginTop = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "TopMargin")?.Value,
            path + "/Page/TopMargin",
            _diagnostics,
            0f);
        settings.MarginBottom = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "BottomMargin")?.Value,
            path + "/Page/BottomMargin",
            _diagnostics,
            0f);
        settings.HeaderHeight = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "PageHeader")?.Element(_xmlNamespace + "Height")?.Value,
            path + "/Page/PageHeader/Height",
            _diagnostics,
            0f);
        settings.FooterHeight = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "PageFooter")?.Element(_xmlNamespace + "Height")?.Value,
            path + "/Page/PageFooter/Height",
            _diagnostics,
            0f);

        if (int.TryParse(pageElement.Element(_xmlNamespace + "Columns")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns))
        {
            settings.ColumnCount = Math.Max(1, columns);
        }

        settings.ColumnGap = ReportRdlMeasurements.Parse(
            pageElement.Element(_xmlNamespace + "ColumnSpacing")?.Value,
            path + "/Page/ColumnSpacing",
            _diagnostics,
            settings.ColumnGap);

        settings.Orientation = settings.Width > settings.Height
            ? ReportPageOrientation.Landscape
            : ReportPageOrientation.Portrait;
    }

    private void ReadHeaderFooter(XElement? element, string path, List<ReportItem> target)
    {
        if (element is null)
        {
            return;
        }

        ReadItemCollection(
            element.Element(_xmlNamespace + "ReportItems"),
            path + "/ReportItems",
            target,
            0f,
            0f);
    }

    private void ReadItemCollection(
        XElement? reportItemsElement,
        string path,
        List<ReportItem> target,
        float offsetX,
        float offsetY)
    {
        if (reportItemsElement is null)
        {
            return;
        }

        var index = 0;
        foreach (var itemElement in reportItemsElement.Elements())
        {
            var itemPath = $"{path}/{itemElement.Name.LocalName}[{index}]";
            ReadItem(itemElement, itemPath, target, offsetX, offsetY);
            index++;
        }
    }

    private void ReadItem(
        XElement itemElement,
        string path,
        List<ReportItem> target,
        float offsetX,
        float offsetY)
    {
        var item = ReadStandaloneItem(itemElement, path, offsetX, offsetY);
        if (item is not null)
        {
            target.Add(item);
        }
    }

    private ReportItem? ReadStandaloneItem(
        XElement itemElement,
        string path,
        float offsetX,
        float offsetY,
        float fallbackWidth = 0f,
        float fallbackHeight = 0f)
    {
        var item = itemElement.Name.LocalName switch
        {
            "Textbox" => ReadTextbox(itemElement, path, offsetX, offsetY),
            "Image" => ReadImage(itemElement, path, offsetX, offsetY),
            "Line" => ReadLine(itemElement, path, offsetX, offsetY),
            "Rectangle" => ReadRectangle(itemElement, path, offsetX, offsetY),
            "Chart" => ReadChart(itemElement, path, offsetX, offsetY),
            "GaugePanel" => ReadGaugePanel(itemElement, path, offsetX, offsetY),
            "Tablix" => ReadTablix(itemElement, path, offsetX, offsetY),
            "Subreport" => ReadSubreport(itemElement, path, offsetX, offsetY),
            _ => SkipUnsupportedItem(itemElement.Name.LocalName, path)
        };

        if (item is not null)
        {
            FinalizeImplicitBounds(item, itemElement, fallbackWidth, fallbackHeight);
        }

        return item;
    }

    private ReportItem? SkipUnsupportedItem(string itemName, string path)
    {
        _diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Warning,
            ReportDiagnosticCodes.UnsupportedFeature,
            $"RDL report item '{itemName}' is not supported and was skipped.",
            path));
        return null;
    }

    private TextItem? ReadTextbox(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var (staticText, expression) = ReadTextboxValue(itemElement, path);
        var item = new TextItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.StaticText = staticText;
        item.ValueExpression = expression;
        item.CanGrow = ParseBoolean(itemElement.Element(_xmlNamespace + "CanGrow")?.Value, defaultValue: true);
        item.CanShrink = ParseBoolean(itemElement.Element(_xmlNamespace + "CanShrink")?.Value, defaultValue: false);
        item.Paragraphs = ReadTextboxParagraphs(itemElement, path);

        var styleElement = GetEffectiveTextboxStyleElement(itemElement);
        item.StyleName = _styleCatalog.Intern(styleElement, _xmlNamespace, path, _diagnostics);
        ReportRdlStyleCatalog.Parse(styleElement, _xmlNamespace, path, _diagnostics, out var formatString);
        item.FormatString = formatString;
        return item;
    }

    private ImageItem? ReadImage(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new ImageItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);

        var sourceText = itemElement.Element(_xmlNamespace + "Source")?.Value?.Trim();
        switch (sourceText?.ToLowerInvariant())
        {
            case "embedded":
                item.SourceKind = ReportImageSourceKind.Embedded;
                var embeddedName = itemElement.Element(_xmlNamespace + "Value")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(embeddedName) && _embeddedImages.TryGetValue(embeddedName, out var embeddedImage))
                {
                    item.EmbeddedData = embeddedImage.Data;
                    item.MimeType = embeddedImage.ContentType;
                    item.ValueExpression = embeddedName;
                }

                break;

            case "external":
                item.SourceKind = ReportImageSourceKind.Uri;
                item.ValueExpression = itemElement.Element(_xmlNamespace + "Value")?.Value;
                break;

            default:
                item.SourceKind = ReportImageSourceKind.Expression;
                item.ValueExpression = ReportRdlExpressions.ToNativeExpression(itemElement.Element(_xmlNamespace + "Value")?.Value);
                break;
        }

        item.MimeType ??= itemElement.Element(_xmlNamespace + "MIMEType")?.Value?.Trim();
        item.SizingMode = ParseSizingMode(itemElement.Element(_xmlNamespace + "Sizing")?.Value);
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);
        return item;
    }

    private LineItem ReadLine(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new LineItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.X2 = item.Bounds.X + item.Bounds.Width;
        item.Y2 = item.Bounds.Y + item.Bounds.Height;
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);
        return item;
    }

    private ReportItem ReadRectangle(
        XElement itemElement,
        string path,
        float offsetX,
        float offsetY)
    {
        var nestedItems = itemElement.Element(_xmlNamespace + "ReportItems");
        if (nestedItems is null || !nestedItems.Elements().Any())
        {
            var item = new ShapeItem();
            ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
            item.Shape = ReportShapeKind.Rectangle;
            item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);
            return item;
        }

        var container = new ContainerItem();
        ApplyCommonProperties(container, itemElement, path, offsetX, offsetY);
        container.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);
        ReadItemCollection(
            nestedItems,
            path + "/ReportItems",
            container.Items,
            0f,
            0f);
        return container;
    }

    private GaugeItem ReadGaugePanel(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new GaugeItem
        {
            RawRdlXml = itemElement.ToString(SaveOptions.DisableFormatting)
        };
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.DataSetId = itemElement.Element(_xmlNamespace + "DataSetName")?.Value?.Trim();
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);

        var stateIndicator = itemElement
            .Element(_xmlNamespace + "StateIndicators")
            ?.Element(_xmlNamespace + "StateIndicator");
        if (stateIndicator is not null)
        {
            item.GaugeKind = ReportGaugeKind.StateIndicator;
            item.ValueExpression = ReportRdlExpressions.ToNativeExpression(
                stateIndicator.Element(_xmlNamespace + "GaugeInputValue")?.Element(_xmlNamespace + "Value")?.Value);
            item.MinimumExpression = ReportRdlExpressions.ToNativeValueExpression(
                stateIndicator.Element(_xmlNamespace + "MinimumValue")?.Element(_xmlNamespace + "Value")?.Value);
            item.MaximumExpression = ReportRdlExpressions.ToNativeValueExpression(
                stateIndicator.Element(_xmlNamespace + "MaximumValue")?.Element(_xmlNamespace + "Value")?.Value);
            item.LabelExpression = ReportRdlExpressions.ToNativeScalarExpression(
                itemElement.Element(_xmlNamespace + "GaugeLabels")
                    ?.Element(_xmlNamespace + "GaugeLabel")
                    ?.Element(_xmlNamespace + "Text")
                    ?.Value);
            return item;
        }

        var radialGauge = itemElement
            .Element(_xmlNamespace + "RadialGauges")
            ?.Element(_xmlNamespace + "RadialGauge");
        if (radialGauge is not null)
        {
            item.GaugeKind = ReportGaugeKind.Radial;
            ReadGaugeScale(item, radialGauge, path);
            return item;
        }

        var linearGauge = itemElement
            .Element(_xmlNamespace + "LinearGauges")
            ?.Element(_xmlNamespace + "LinearGauge");
        if (linearGauge is not null)
        {
            item.GaugeKind = ReportGaugeKind.Linear;
            ReadGaugeScale(item, linearGauge, path);
            return item;
        }

        _diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Warning,
            ReportDiagnosticCodes.UnsupportedFeature,
            "Gauge panels without state, radial, or linear gauge content are imported as empty gauge items.",
            path));
        return item;
    }

    private void ReadGaugeScale(GaugeItem item, XElement gaugeElement, string path)
    {
        var pointer = gaugeElement
            .Descendants(_xmlNamespace + "GaugePointers")
            .Elements()
            .FirstOrDefault();
        item.ValueExpression = ReportRdlExpressions.ToNativeExpression(
            pointer?.Element(_xmlNamespace + "GaugeInputValue")?.Element(_xmlNamespace + "Value")?.Value);

        var scale = gaugeElement
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName.EndsWith("Scale", StringComparison.Ordinal));
        item.MinimumExpression = ReportRdlExpressions.ToNativeValueExpression(
            scale?.Element(_xmlNamespace + "MinimumValue")?.Element(_xmlNamespace + "Value")?.Value);
        item.MaximumExpression = ReportRdlExpressions.ToNativeValueExpression(
            scale?.Element(_xmlNamespace + "MaximumValue")?.Element(_xmlNamespace + "Value")?.Value);
        item.TargetValueExpression = item.MaximumExpression;
        item.LabelExpression = ReportRdlExpressions.ToNativeScalarExpression(
            gaugeElement.Parent?.Parent?
                .Element(_xmlNamespace + "GaugeLabels")
                ?.Element(_xmlNamespace + "GaugeLabel")
                ?.Element(_xmlNamespace + "Text")
                ?.Value);
    }

    private ChartItem ReadChart(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new ChartItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.DataSetId = itemElement.Element(_xmlNamespace + "DataSetName")?.Value?.Trim();
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);

        var categoryMember = itemElement
            .Element(_xmlNamespace + "ChartCategoryHierarchy")
            ?.Element(_xmlNamespace + "ChartMembers")
            ?.Element(_xmlNamespace + "ChartMember");
        item.CategoryExpression =
            ReportRdlExpressions.ToNativeScalarExpression(
                categoryMember?.Element(_xmlNamespace + "Group")
                    ?.Element(_xmlNamespace + "GroupExpressions")
                    ?.Element(_xmlNamespace + "GroupExpression")
                    ?.Value)
            ?? ReportRdlExpressions.ToNativeScalarExpression(categoryMember?.Element(_xmlNamespace + "Label")?.Value);

        item.TitleExpression =
            ReportRdlExpressions.ToNativeScalarExpression(
                itemElement.Element(_xmlNamespace + "ChartTitles")
                    ?.Element(_xmlNamespace + "ChartTitle")
                    ?.Element(_xmlNamespace + "Caption")
                    ?.Value)
            ?? ReportRdlExpressions.ToNativeScalarExpression(itemElement.Element(_xmlNamespace + "Title")?.Value);

        var seriesLabels = itemElement
            .Element(_xmlNamespace + "ChartSeriesHierarchy")
            ?.Element(_xmlNamespace + "ChartMembers")
            ?.Elements(_xmlNamespace + "ChartMember")
            .ToList() ?? new List<XElement>();
        var seriesCollection = itemElement
            .Element(_xmlNamespace + "ChartData")
            ?.Element(_xmlNamespace + "ChartSeriesCollection")
            ?.Elements(_xmlNamespace + "ChartSeries")
            .ToList() ?? new List<XElement>();

        for (var index = 0; index < seriesCollection.Count; index++)
        {
            var seriesElement = seriesCollection[index];
            var labelElement = index < seriesLabels.Count ? seriesLabels[index] : null;
            item.Series.Add(new ReportChartSeriesDefinition
            {
                NameExpression =
                    ReportRdlExpressions.ToNativeScalarExpression(labelElement?.Element(_xmlNamespace + "Label")?.Value)
                    ?? ReportRdlExpressions.ToNativeScalarExpression((string?)seriesElement.Attribute("Name")),
                ValueExpression =
                    ReportRdlExpressions.ToNativeExpression(
                        seriesElement.Element(_xmlNamespace + "ChartDataPoints")
                            ?.Element(_xmlNamespace + "ChartDataPoint")
                            ?.Element(_xmlNamespace + "ChartDataPointValues")
                            ?.Element(_xmlNamespace + "Y")
                            ?.Value)
                    ?? ReportRdlExpressions.ToNativeExpression(
                        seriesElement.Element(_xmlNamespace + "DataPoints")
                            ?.Element(_xmlNamespace + "DataPoint")
                            ?.Element(_xmlNamespace + "DataValues")
                            ?.Element(_xmlNamespace + "DataValue")
                            ?.Element(_xmlNamespace + "Value")
                            ?.Value),
                ColorExpression = ReportRdlExpressions.ToNativeScalarExpression(
                    seriesElement.Element(_xmlNamespace + "Style")?.Element(_xmlNamespace + "Color")?.Value)
            });
        }

        return item;
    }

    private TablixItem ReadTablix(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new TablixItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.DataSetId = itemElement.Element(_xmlNamespace + "DataSetName")?.Value?.Trim();
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);

        var body = itemElement.Element(_xmlNamespace + "TablixBody");
        var columns = body?.Element(_xmlNamespace + "TablixColumns")?.Elements(_xmlNamespace + "TablixColumn").ToList()
            ?? new List<XElement>();
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            item.Columns.Add(new ReportTablixColumnDefinition
            {
                Id = $"column{columnIndex + 1}",
                Width = ReportRdlMeasurements.Parse(
                    columns[columnIndex].Element(_xmlNamespace + "Width")?.Value,
                    $"{path}/TablixBody/TablixColumns/TablixColumn[{columnIndex}]/Width",
                    _diagnostics,
                    120f)
            });
        }

        var rowMembers = itemElement
            .Element(_xmlNamespace + "TablixRowHierarchy")
            ?.Element(_xmlNamespace + "TablixMembers")
            ?.Elements(_xmlNamespace + "TablixMember")
            .ToList() ?? new List<XElement>();
        var rows = body?.Element(_xmlNamespace + "TablixRows")?.Elements(_xmlNamespace + "TablixRow").ToList()
            ?? new List<XElement>();
        var rowHeights = new List<float>(rows.Count);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rowHeights.Add(ReportRdlMeasurements.Parse(
                rows[rowIndex].Element(_xmlNamespace + "Height")?.Value,
                $"{path}/TablixBody/TablixRows/TablixRow[{rowIndex}]/Height",
                _diagnostics,
                24f));
        }

        var repeatHeaderRows = false;
        var spanningColumns = item.Columns.Count > 0
            ? new int[item.Columns.Count]
            : Array.Empty<int>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowElement = rows[rowIndex];
            var row = new ReportTablixRowDefinition
            {
                Id = $"row{rowIndex + 1}",
                Height = rowHeights[rowIndex]
            };

            var cellIndex = 0;
            var logicalColumnIndex = 0;
            var rowSpanAdditions = spanningColumns.Length > 0
                ? new int[spanningColumns.Length]
                : Array.Empty<int>();
            foreach (var cellElement in rowElement.Element(_xmlNamespace + "TablixCells")?.Elements(_xmlNamespace + "TablixCell") ?? [])
            {
                while (logicalColumnIndex < spanningColumns.Length && spanningColumns[logicalColumnIndex] > 0)
                {
                    logicalColumnIndex++;
                }

                var cellContents = cellElement.Element(_xmlNamespace + "CellContents");
                if (IsTablixPlaceholderCell(cellElement, cellContents))
                {
                    cellIndex++;
                    continue;
                }

                var columnSpan = ResolveTablixColumnSpan(
                    ParseInt(
                        cellElement.Element(_xmlNamespace + "ColSpan")?.Value
                        ?? cellContents?.Element(_xmlNamespace + "ColSpan")?.Value,
                        1),
                    logicalColumnIndex,
                    item.Columns.Count);
                var rowSpan = ParseInt(
                    cellElement.Element(_xmlNamespace + "RowSpan")?.Value
                    ?? cellContents?.Element(_xmlNamespace + "RowSpan")?.Value,
                    1);
                row.Cells.Add(ReadTablixCell(
                    cellElement,
                    $"{path}/TablixBody/TablixRows/TablixRow[{rowIndex}]/TablixCells/TablixCell[{cellIndex}]",
                    columnSpan,
                    rowSpan,
                    CalculateCellWidth(item.Columns, logicalColumnIndex, columnSpan),
                    CalculateCellHeight(rowHeights, rowIndex, rowSpan)));

                if (rowSpan > 1)
                {
                    for (var spanColumnIndex = logicalColumnIndex;
                         spanColumnIndex < logicalColumnIndex + columnSpan && spanColumnIndex < rowSpanAdditions.Length;
                         spanColumnIndex++)
                    {
                        rowSpanAdditions[spanColumnIndex] = Math.Max(rowSpanAdditions[spanColumnIndex], rowSpan - 1);
                    }
                }

                logicalColumnIndex += Math.Max(1, columnSpan);
                cellIndex++;
            }

            item.Rows.Add(row);

            for (var columnIndex = 0; columnIndex < spanningColumns.Length; columnIndex++)
            {
                if (spanningColumns[columnIndex] > 0)
                {
                    spanningColumns[columnIndex]--;
                }

                if (rowSpanAdditions[columnIndex] > spanningColumns[columnIndex])
                {
                    spanningColumns[columnIndex] = rowSpanAdditions[columnIndex];
                }
            }
        }

        item.RowMembers.AddRange(ReadTablixMembers(
            itemElement.Element(_xmlNamespace + "TablixRowHierarchy")?.Element(_xmlNamespace + "TablixMembers"),
            path + "/TablixRowHierarchy/TablixMembers"));
        if (item.RowMembers.Count == 0)
        {
            for (var rowIndex = 0; rowIndex < item.Rows.Count; rowIndex++)
            {
                item.RowMembers.Add(new ReportTablixMemberDefinition
                {
                    Id = $"rowMember{rowIndex + 1}",
                    Kind = item.Rows[rowIndex].IsHeader ? ReportTablixMemberKind.Static : ReportTablixMemberKind.Details,
                    RowDefinitionIndex = rowIndex
                });
            }
        }

        var leafMembers = new List<(ReportTablixMemberDefinition Member, ReportTablixMemberKind EffectiveKind)>();
        CollectLeafMembers(item.RowMembers, leafMembers);
        var mappedLeafCount = Math.Min(leafMembers.Count, item.Rows.Count);
        for (var rowIndex = 0; rowIndex < mappedLeafCount; rowIndex++)
        {
            leafMembers[rowIndex].Member.RowDefinitionIndex = rowIndex;
            item.Rows[rowIndex].IsHeader = leafMembers[rowIndex].EffectiveKind == ReportTablixMemberKind.Static;
            repeatHeaderRows |= item.Rows[rowIndex].IsHeader && leafMembers[rowIndex].Member.RepeatOnNewPage;
        }

        if (leafMembers.Count != item.Rows.Count)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                $"Tablix row hierarchy leaf count '{leafMembers.Count}' does not match body row count '{item.Rows.Count}'. Rows beyond the mapped range may render approximately.",
                path + "/TablixRowHierarchy"));
        }

        item.RepeatHeaderRows = repeatHeaderRows;
        return item;
    }

    private static bool IsTablixPlaceholderCell(XElement cellElement, XElement? cellContents)
    {
        if (cellContents is not null)
        {
            return false;
        }

        return !cellElement.HasElements && !cellElement.HasAttributes;
    }

    private static int ResolveTablixColumnSpan(int requestedSpan, int startColumnIndex, int columnCount)
    {
        var span = Math.Max(1, requestedSpan);
        if (columnCount <= 0 || startColumnIndex < 0 || startColumnIndex >= columnCount)
        {
            return span;
        }

        return Math.Max(1, Math.Min(span, columnCount - startColumnIndex));
    }

    private List<ReportTablixMemberDefinition> ReadTablixMembers(XElement? membersElement, string path)
    {
        var members = new List<ReportTablixMemberDefinition>();
        if (membersElement is null)
        {
            return members;
        }

        var index = 0;
        foreach (var memberElement in membersElement.Elements(_xmlNamespace + "TablixMember"))
        {
            members.Add(ReadTablixMember(memberElement, $"{path}/TablixMember[{index}]"));
            index++;
        }

        return members;
    }

    private ReportTablixMemberDefinition ReadTablixMember(XElement memberElement, string path)
    {
        var definition = new ReportTablixMemberDefinition
        {
            Id = ResolveTablixMemberId(memberElement, path),
            RepeatOnNewPage = ParseBoolean(memberElement.Element(_xmlNamespace + "RepeatOnNewPage")?.Value),
            KeepWithGroup = memberElement.Element(_xmlNamespace + "KeepWithGroup")?.Value?.Trim(),
            VisibilityExpression = ReportRdlExpressions.ToNativeExpression(
                memberElement.Element(_xmlNamespace + "Visibility")?.Element(_xmlNamespace + "Hidden")?.Value),
            ToggleItemId = memberElement.Element(_xmlNamespace + "Visibility")?.Element(_xmlNamespace + "ToggleItem")?.Value?.Trim()
        };

        var groupElement = memberElement.Element(_xmlNamespace + "Group");
        if (groupElement is not null)
        {
            definition.GroupName = ((string?)groupElement.Attribute("Name"))?.Trim();
            definition.GroupExpression = ReportRdlExpressions.ToNativeExpression(
                groupElement.Element(_xmlNamespace + "GroupExpressions")
                    ?.Elements(_xmlNamespace + "GroupExpression")
                    .FirstOrDefault()
                    ?.Value);
            var groupPageBreak = groupElement.Element(_xmlNamespace + "PageBreak");
            if (groupPageBreak is not null)
            {
                definition.PageBreak = ReadPageBreak(groupPageBreak, path + "/Group/PageBreak");
            }

            definition.Kind = string.IsNullOrWhiteSpace(definition.GroupExpression)
                ? ReportTablixMemberKind.Details
                : ReportTablixMemberKind.Group;
        }
        else
        {
            var memberPageBreak = memberElement.Element(_xmlNamespace + "PageBreak");
            if (memberPageBreak is not null)
            {
                definition.PageBreak = ReadPageBreak(memberPageBreak, path + "/PageBreak");
            }
        }

        var sortExpressionElement = memberElement
            .Element(_xmlNamespace + "SortExpressions")
            ?.Elements(_xmlNamespace + "SortExpression")
            .FirstOrDefault();
        if (sortExpressionElement is not null)
        {
            definition.SortExpression = ReportRdlExpressions.ToNativeExpression(
                sortExpressionElement.Element(_xmlNamespace + "Value")?.Value);
            definition.SortDirection = ParseSortDirection(sortExpressionElement.Element(_xmlNamespace + "Direction")?.Value);
        }

        definition.Members.AddRange(ReadTablixMembers(
            memberElement.Element(_xmlNamespace + "TablixMembers"),
            path + "/TablixMembers"));

        return definition;
    }

    private static void CollectLeafMembers(
        IReadOnlyList<ReportTablixMemberDefinition> members,
        List<(ReportTablixMemberDefinition Member, ReportTablixMemberKind EffectiveKind)> leaves,
        ReportTablixMemberKind? inheritedKind = null)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            var effectiveKind = inheritedKind == ReportTablixMemberKind.Details
                && member.Kind == ReportTablixMemberKind.Static
                ? ReportTablixMemberKind.Details
                : member.Kind;
            if (member.Members.Count == 0)
            {
                leaves.Add((member, effectiveKind));
                continue;
            }

            CollectLeafMembers(member.Members, leaves, effectiveKind);
        }
    }

    private static string ResolveTablixMemberId(XElement memberElement, string path)
    {
        var groupName = ((string?)memberElement.Element(memberElement.Name.Namespace + "Group")?.Attribute("Name"))?.Trim();
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            return groupName;
        }

        var sanitizedPath = path.Replace('/', '_')
            .Replace('[', '_')
            .Replace(']', '_');
        return sanitizedPath.Trim('_');
    }

    private ReportTablixCellDefinition ReadTablixCell(
        XElement cellElement,
        string path,
        int columnSpan,
        int rowSpan,
        float cellWidth,
        float cellHeight)
    {
        var cell = new ReportTablixCellDefinition
        {
            ColumnSpan = Math.Max(1, columnSpan),
            RowSpan = Math.Max(1, rowSpan)
        };

        if (cell.RowSpan > 1)
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                "Tablix row spans are not supported by the current RDL mapper and were preserved only in the native model when explicitly present.",
                path + "/RowSpan"));
        }

        var cellContents = cellElement.Element(_xmlNamespace + "CellContents");
        var textbox = cellContents?.Element(_xmlNamespace + "Textbox");
        if (textbox is null)
        {
            var nestedItem = cellContents?.Elements().FirstOrDefault();
            if (nestedItem is not null)
            {
                cell.ContentItem = ReadStandaloneItem(
                    nestedItem,
                    path + "/CellContents",
                    0f,
                    0f,
                    cellWidth,
                    cellHeight);
            }

            return cell;
        }

        var (staticText, expression) = ReadTextboxValue(textbox, path + "/CellContents/Textbox");
        cell.Text = staticText;
        cell.ValueExpression = expression;
        var styleElement = GetEffectiveTextboxStyleElement(textbox);
        cell.StyleName = _styleCatalog.Intern(styleElement, _xmlNamespace, path, _diagnostics);
        ReportRdlStyleCatalog.Parse(styleElement, _xmlNamespace, path, _diagnostics, out var formatString);
        cell.FormatString = formatString;
        return cell;
    }

    private SubreportItem ReadSubreport(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var item = new SubreportItem();
        ApplyCommonProperties(item, itemElement, path, offsetX, offsetY);
        item.ReportReferenceId = itemElement.Element(_xmlNamespace + "ReportName")?.Value?.Trim() ?? string.Empty;
        item.StyleName = _styleCatalog.Intern(itemElement.Element(_xmlNamespace + "Style"), _xmlNamespace, path, _diagnostics);

        var parametersElement = itemElement.Element(_xmlNamespace + "Parameters");
        if (parametersElement is not null)
        {
            foreach (var parameterElement in parametersElement.Elements(_xmlNamespace + "Parameter"))
            {
                item.Parameters.Add(new ReportParameterBinding
                {
                    ParameterId = ((string?)parameterElement.Attribute("Name"))?.Trim() ?? string.Empty,
                    ValueExpression = ReportRdlExpressions.ToNativeExpression(parameterElement.Element(_xmlNamespace + "Value")?.Value) ?? string.Empty
                });
            }
        }

        return item;
    }

    private void ApplyCommonProperties(ReportItem item, XElement itemElement, string path, float offsetX, float offsetY)
    {
        ArgumentNullException.ThrowIfNull(item);

        var name = ((string?)itemElement.Attribute("Name"))?.Trim();
        item.Id = string.IsNullOrWhiteSpace(name) ? itemElement.Name.LocalName.ToLowerInvariant() : name;
        item.Name = item.Id;
        item.Bounds = ReadBounds(itemElement, path, offsetX, offsetY);
        item.ZIndex = ParseInt(itemElement.Element(_xmlNamespace + "ZIndex")?.Value, 0);
        item.KeepTogether = ParseBoolean(itemElement.Element(_xmlNamespace + "KeepTogether")?.Value);
        item.VisibilityExpression = ReportRdlExpressions.ToNativeExpression(
            itemElement.Element(_xmlNamespace + "Visibility")?.Element(_xmlNamespace + "Hidden")?.Value);
        var pageBreakElement = itemElement.Element(_xmlNamespace + "PageBreak");
        if (pageBreakElement is not null)
        {
            item.PageBreak = ReadPageBreak(pageBreakElement, path + "/PageBreak");
        }

        var bookmark = itemElement.Element(_xmlNamespace + "Bookmark")?.Value;
        var documentMapLabel = itemElement.Element(_xmlNamespace + "DocumentMapLabel")?.Value;
        item.BookmarkExpression = ReportRdlExpressions.ToNativeScalarExpression(bookmark)
            ?? ReportRdlExpressions.ToNativeScalarExpression(documentMapLabel);
        if (!string.IsNullOrWhiteSpace(bookmark) && !string.IsNullOrWhiteSpace(documentMapLabel) && !string.Equals(bookmark, documentMapLabel, StringComparison.Ordinal))
        {
            _diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnsupportedFeature,
                "RDL Bookmark and DocumentMapLabel differ; the native report model preserves only one bookmark expression.",
                path));
        }

        item.TooltipExpression = ReportRdlExpressions.ToNativeScalarExpression(itemElement.Element(_xmlNamespace + "ToolTip")?.Value);
        item.DrillthroughAction = ReadDrillthrough(itemElement.Element(_xmlNamespace + "ActionInfo"), path + "/ActionInfo");
    }

    private ReportPageBreakDefinition ReadPageBreak(XElement pageBreakElement, string path)
    {
        return new ReportPageBreakDefinition
        {
            Location = ParsePageBreakLocation(pageBreakElement.Element(_xmlNamespace + "BreakLocation")?.Value),
            DisabledExpression = ReportRdlExpressions.ToNativeExpression(pageBreakElement.Element(_xmlNamespace + "Disabled")?.Value),
            ResetPageNumber = ParseBoolean(pageBreakElement.Element(_xmlNamespace + "ResetPageNumber")?.Value)
        };
    }

    private ReportDrillthroughAction? ReadDrillthrough(XElement? actionInfoElement, string path)
    {
        var actionElement = actionInfoElement?
            .Element(_xmlNamespace + "Actions")
            ?.Element(_xmlNamespace + "Action")
            ?.Element(_xmlNamespace + "Drillthrough");
        if (actionElement is null)
        {
            return null;
        }

        var action = new ReportDrillthroughAction
        {
            ReportReferenceId = actionElement.Element(_xmlNamespace + "ReportName")?.Value?.Trim() ?? string.Empty
        };

        var index = 0;
        foreach (var parameterElement in actionElement.Element(_xmlNamespace + "Parameters")?.Elements(_xmlNamespace + "Parameter") ?? [])
        {
            action.Parameters.Add(new ReportParameterBinding
            {
                ParameterId = ((string?)parameterElement.Attribute("Name"))?.Trim() ?? $"Parameter{index + 1}",
                ValueExpression = ReportRdlExpressions.ToNativeExpression(parameterElement.Element(_xmlNamespace + "Value")?.Value) ?? string.Empty
            });
            index++;
        }

        return action;
    }

    private (string? StaticText, string? Expression) ReadTextboxValue(XElement textboxElement, string path)
    {
        var paragraphs = textboxElement
            .Element(_xmlNamespace + "Paragraphs")
            ?.Elements(_xmlNamespace + "Paragraph")
            .ToList();

        if (paragraphs is null || paragraphs.Count == 0)
        {
            var fallback = textboxElement.Element(_xmlNamespace + "Value")?.Value;
            ReportRdlExpressions.SplitTextboxValue(fallback, out var fallbackStaticText, out var fallbackExpression);
            return (fallbackStaticText, fallbackExpression);
        }

        if (paragraphs.Count == 1)
        {
            var textRuns = paragraphs[0]
                .Element(_xmlNamespace + "TextRuns")
                ?.Elements(_xmlNamespace + "TextRun")
                .ToList();
            if (textRuns is null || textRuns.Count == 0)
            {
                var fallback = paragraphs[0].Element(_xmlNamespace + "TextRuns")?.Value;
                ReportRdlExpressions.SplitTextboxValue(fallback, out var paragraphStaticText, out var paragraphExpression);
                return (paragraphStaticText, paragraphExpression);
            }

            if (textRuns.Count > 1)
            {
                return CombineTextRuns(textRuns);
            }

            var value = textRuns[0].Element(_xmlNamespace + "Value")?.Value;
            ReportRdlExpressions.SplitTextboxValue(value, out var staticText, out var expression);
            return (staticText, expression);
        }

        var paragraphValues = new List<(string? StaticText, string? Expression)>(paragraphs.Count);
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            var textRuns = paragraphs[paragraphIndex]
                .Element(_xmlNamespace + "TextRuns")
                ?.Elements(_xmlNamespace + "TextRun")
                .ToList();
            if (textRuns is null || textRuns.Count == 0)
            {
                paragraphValues.Add((string.Empty, null));
                continue;
            }

            paragraphValues.Add(textRuns.Count == 1
                ? SplitTextRunValue(textRuns[0])
                : CombineTextRuns(textRuns));
        }

        return CombineTextboxParagraphs(paragraphValues);
    }

    private List<ReportTextParagraph> ReadTextboxParagraphs(XElement textboxElement, string path)
    {
        var paragraphElements = textboxElement
            .Element(_xmlNamespace + "Paragraphs")
            ?.Elements(_xmlNamespace + "Paragraph")
            .ToList();
        if (paragraphElements is null || paragraphElements.Count == 0)
        {
            return new List<ReportTextParagraph>();
        }

        var paragraphs = new List<ReportTextParagraph>(paragraphElements.Count);
        for (var paragraphIndex = 0; paragraphIndex < paragraphElements.Count; paragraphIndex++)
        {
            var paragraphElement = paragraphElements[paragraphIndex];
            var paragraph = new ReportTextParagraph
            {
                TextAlign = ParseParagraphAlignment(paragraphElement.Element(_xmlNamespace + "Style")?.Element(_xmlNamespace + "TextAlign")?.Value)
            };

            var runElements = paragraphElement
                .Element(_xmlNamespace + "TextRuns")
                ?.Elements(_xmlNamespace + "TextRun")
                .ToList();
            if (runElements is not null && runElements.Count > 0)
            {
                for (var runIndex = 0; runIndex < runElements.Count; runIndex++)
                {
                    var runElement = runElements[runIndex];
                    var (runStaticText, runExpression) = SplitTextRunValue(runElement);
                    paragraph.Runs.Add(new ReportTextRun
                    {
                        StaticText = runStaticText,
                        ValueExpression = runExpression,
                        StyleName = _styleCatalog.Intern(
                            runElement.Element(_xmlNamespace + "Style"),
                            _xmlNamespace,
                            $"{path}/Paragraphs[{paragraphIndex}]/TextRuns[{runIndex}]",
                            _diagnostics)
                    });
                }
            }

            if (paragraph.Runs.Count == 0)
            {
                paragraph.Runs.Add(new ReportTextRun
                {
                    StaticText = string.Empty
                });
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private (string? StaticText, string? Expression) SplitTextRunValue(XElement textRunElement)
    {
        var value = textRunElement.Element(_xmlNamespace + "Value")?.Value;
        ReportRdlExpressions.SplitTextboxValue(value, out var staticText, out var expression);
        return (staticText, expression);
    }

    private static (string? StaticText, string? Expression) CombineTextRuns(IReadOnlyList<XElement> textRuns)
    {
        var parts = new List<string>(textRuns.Count);
        var allStatic = true;
        for (var index = 0; index < textRuns.Count; index++)
        {
            var value = textRuns[index].Element(textRuns[index].Name.Namespace + "Value")?.Value;
            ReportRdlExpressions.SplitTextboxValue(value, out var staticText, out var expression);
            if (!string.IsNullOrWhiteSpace(expression))
            {
                parts.Add(expression);
                allStatic = false;
            }
            else
            {
                parts.Add(ReportRdlExpressions.QuoteNativeString(staticText ?? string.Empty));
            }
        }

        if (allStatic)
        {
            return (string.Concat(parts.Select(static part => part.Length >= 2 && part[0] == '\'' && part[^1] == '\'' ? part[1..^1].Replace("''", "'", StringComparison.Ordinal) : part)), null);
        }

        return (null, string.Join(" + ", parts));
    }

    private static (string? StaticText, string? Expression) CombineTextboxParagraphs(
        IReadOnlyList<(string? StaticText, string? Expression)> paragraphs)
    {
        var parts = new List<string>(paragraphs.Count * 2);
        var allStatic = true;
        for (var index = 0; index < paragraphs.Count; index++)
        {
            if (index > 0)
            {
                parts.Add(ReportRdlExpressions.QuoteNativeString(Environment.NewLine));
            }

            var paragraph = paragraphs[index];
            if (!string.IsNullOrWhiteSpace(paragraph.Expression))
            {
                parts.Add(paragraph.Expression);
                allStatic = false;
            }
            else
            {
                parts.Add(ReportRdlExpressions.QuoteNativeString(paragraph.StaticText ?? string.Empty));
            }
        }

        if (allStatic)
        {
            return (string.Join(Environment.NewLine, paragraphs.Select(static paragraph => paragraph.StaticText ?? string.Empty)), null);
        }

        return (null, string.Join(" + ", parts));
    }

    private XElement? GetEffectiveTextboxStyleElement(XElement textboxElement)
    {
        var textboxStyle = textboxElement.Element(_xmlNamespace + "Style");
        var paragraphStyle = textboxElement
            .Element(_xmlNamespace + "Paragraphs")
            ?.Elements(_xmlNamespace + "Paragraph")
            .FirstOrDefault()
            ?.Element(_xmlNamespace + "Style");
        var textRuns = textboxElement
            .Element(_xmlNamespace + "Paragraphs")
            ?.Elements(_xmlNamespace + "Paragraph")
            .SelectMany(paragraph => paragraph.Element(_xmlNamespace + "TextRuns")?.Elements(_xmlNamespace + "TextRun") ?? [])
            .ToList();

        if (textRuns is null || textRuns.Count == 0)
        {
            return MergeStyleElements(textboxStyle, paragraphStyle);
        }

        XElement? runStyle;
        if (textRuns.Count == 1)
        {
            runStyle = textRuns[0].Element(_xmlNamespace + "Style");
        }
        else
        {
            runStyle = GetCommonRunStyleElement(textRuns);
            runStyle ??= GetRepresentativeRunStyleElement(textRuns);
        }

        return MergeStyleElements(MergeStyleElements(textboxStyle, paragraphStyle), runStyle);
    }

    private XElement? MergeStyleElements(XElement? baseStyle, XElement? overrideStyle)
    {
        if (baseStyle is null)
        {
            return overrideStyle;
        }

        if (overrideStyle is null)
        {
            return baseStyle;
        }

        var merged = new XElement(_xmlNamespace + "Style");
        foreach (var child in baseStyle.Elements())
        {
            merged.Add(new XElement(child));
        }

        foreach (var child in overrideStyle.Elements())
        {
            var existing = merged.Element(child.Name);
            if (existing is not null)
            {
                existing.ReplaceWith(new XElement(child));
            }
            else
            {
                merged.Add(new XElement(child));
            }
        }

        return merged;
    }

    private XElement? GetCommonRunStyleElement(IReadOnlyList<XElement> textRuns)
    {
        XElement? commonStyle = null;
        var hasStyle = false;
        var candidates = GetMeaningfulTextRuns(textRuns);
        for (var index = 0; index < candidates.Count; index++)
        {
            var style = candidates[index].Element(_xmlNamespace + "Style");
            if (!hasStyle)
            {
                commonStyle = style;
                hasStyle = true;
                continue;
            }

            if (!StyleElementsEquivalent(commonStyle, style))
            {
                return null;
            }
        }

        return commonStyle;
    }

    private List<XElement> GetMeaningfulTextRuns(IReadOnlyList<XElement> textRuns)
    {
        var meaningful = new List<XElement>(textRuns.Count);
        for (var index = 0; index < textRuns.Count; index++)
        {
            var value = textRuns[index].Element(_xmlNamespace + "Value")?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            meaningful.Add(textRuns[index]);
        }

        return meaningful.Count > 0 ? meaningful : textRuns.ToList();
    }

    private XElement? GetRepresentativeRunStyleElement(IReadOnlyList<XElement> textRuns)
    {
        var candidates = GetMeaningfulTextRuns(textRuns);
        for (var index = 0; index < candidates.Count; index++)
        {
            var style = candidates[index].Element(_xmlNamespace + "Style");
            if (style is not null)
            {
                return style;
            }
        }

        return null;
    }

    private bool StyleElementsEquivalent(XElement? left, XElement? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        var leftElements = left.Elements().ToList();
        var rightElements = right.Elements().ToList();
        if (leftElements.Count != rightElements.Count)
        {
            return false;
        }

        for (var index = 0; index < leftElements.Count; index++)
        {
            var leftElement = leftElements[index];
            var rightElement = right.Element(leftElement.Name);
            if (rightElement is null)
            {
                return false;
            }

            if (!string.Equals(leftElement.Value, rightElement.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static ParagraphAlignment? ParseParagraphAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "center" => ParagraphAlignment.Center,
            "right" => ParagraphAlignment.Right,
            "justify" => ParagraphAlignment.Justify,
            "left" or "general" => ParagraphAlignment.Left,
            _ => null
        };
    }

    private ReportItemBounds ReadBounds(XElement itemElement, string path, float offsetX, float offsetY)
    {
        var left = ReportRdlMeasurements.Parse(itemElement.Element(_xmlNamespace + "Left")?.Value, path + "/Left", _diagnostics);
        var top = ReportRdlMeasurements.Parse(itemElement.Element(_xmlNamespace + "Top")?.Value, path + "/Top", _diagnostics);
        var width = ReportRdlMeasurements.Parse(itemElement.Element(_xmlNamespace + "Width")?.Value, path + "/Width", _diagnostics);
        var height = ReportRdlMeasurements.Parse(itemElement.Element(_xmlNamespace + "Height")?.Value, path + "/Height", _diagnostics);
        return new ReportItemBounds(offsetX + left, offsetY + top, width, height);
    }

    private void FinalizeImplicitBounds(
        ReportItem item,
        XElement itemElement,
        float fallbackWidth,
        float fallbackHeight)
    {
        var hasExplicitWidth = HasExplicitMeasurement(itemElement, "Width");
        var hasExplicitHeight = HasExplicitMeasurement(itemElement, "Height");
        if (hasExplicitWidth && hasExplicitHeight)
        {
            return;
        }

        var inferredWidth = InferItemWidth(item);
        var inferredHeight = InferItemHeight(item);
        var width = item.Bounds.Width;
        var height = item.Bounds.Height;

        if (!hasExplicitWidth)
        {
            width = Math.Max(width, Math.Max(inferredWidth, fallbackWidth));
        }

        if (!hasExplicitHeight)
        {
            height = Math.Max(height, Math.Max(inferredHeight, fallbackHeight));
        }

        if (width <= 0f && height <= 0f)
        {
            return;
        }

        item.Bounds = item.Bounds with
        {
            Width = width,
            Height = height
        };

        if (item is LineItem line)
        {
            line.X2 = line.Bounds.X + line.Bounds.Width;
            line.Y2 = line.Bounds.Y + line.Bounds.Height;
        }
    }

    private static float CalculateCellWidth(
        IReadOnlyList<ReportTablixColumnDefinition> columns,
        int startColumnIndex,
        int columnSpan)
    {
        var width = 0f;
        var span = Math.Max(1, columnSpan);
        for (var index = 0; index < span && startColumnIndex + index < columns.Count; index++)
        {
            width += Math.Max(0f, columns[startColumnIndex + index].Width);
        }

        return width;
    }

    private static float CalculateCellHeight(
        IReadOnlyList<float> rowHeights,
        int startRowIndex,
        int rowSpan)
    {
        var height = 0f;
        var span = Math.Max(1, rowSpan);
        for (var index = 0; index < span && startRowIndex + index < rowHeights.Count; index++)
        {
            height += Math.Max(0f, rowHeights[startRowIndex + index]);
        }

        return height;
    }

    private static float InferItemWidth(ReportItem item)
    {
        return item switch
        {
            ContainerItem container => InferContainerBounds(container).Width,
            TablixItem tablix => tablix.Columns.Sum(static column => Math.Max(0f, column.Width)),
            _ => 0f
        };
    }

    private static float InferItemHeight(ReportItem item)
    {
        return item switch
        {
            ContainerItem container => InferContainerBounds(container).Height,
            TablixItem tablix => tablix.Rows.Sum(static row => Math.Max(0f, row.Height)),
            _ => 0f
        };
    }

    private static (float Width, float Height) InferContainerBounds(ContainerItem container)
    {
        var maxRight = 0f;
        var maxBottom = 0f;
        for (var index = 0; index < container.Items.Count; index++)
        {
            var child = container.Items[index];
            var childWidth = child.Bounds.Width > 0f ? child.Bounds.Width : InferItemWidth(child);
            var childHeight = child.Bounds.Height > 0f ? child.Bounds.Height : InferItemHeight(child);
            var right = child.Bounds.X + childWidth;
            var bottom = child.Bounds.Y + childHeight;
            if (right > maxRight)
            {
                maxRight = right;
            }

            if (bottom > maxBottom)
            {
                maxBottom = bottom;
            }
        }

        return (maxRight, maxBottom);
    }

    private bool HasExplicitMeasurement(XElement itemElement, string localName)
    {
        return !string.IsNullOrWhiteSpace(itemElement.Element(_xmlNamespace + localName)?.Value);
    }

    private static void ParseConnectionStringOptions(string connectString, Dictionary<string, string> options)
    {
        var segments = connectString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var separator = segments[index].IndexOf('=');
            if (separator <= 0 || separator == segments[index].Length - 1)
            {
                continue;
            }

            var key = segments[index][..separator].Trim();
            var value = segments[index][(separator + 1)..].Trim();
            if (!options.ContainsKey(key))
            {
                options[key] = value;
            }
        }
    }

    private static string NormalizeRdlProviderId(string providerId)
    {
        return providerId.Trim().ToUpperInvariant() switch
        {
            "ENTERDATA" => "enterdata",
            "SQL" or "SQLAZURE" => "sqlserver",
            "OBJECT" => "in-memory",
            "XML" => "json",
            _ => providerId
        };
    }

    private static ReportParameterDataType ParseParameterDataType(string? dataType)
    {
        return dataType?.Trim().ToLowerInvariant() switch
        {
            "integer" => ReportParameterDataType.Integer,
            "float" => ReportParameterDataType.Number,
            "boolean" => ReportParameterDataType.Boolean,
            "datetime" => ReportParameterDataType.DateTime,
            _ => ReportParameterDataType.String
        };
    }

    private static ReportParameterDataType ParseFieldDataType(string? typeName)
    {
        return typeName?.Trim() switch
        {
            "System.Boolean" => ReportParameterDataType.Boolean,
            "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
                => ReportParameterDataType.Integer,
            "System.Int64" or "System.UInt64" or "System.Single" or "System.Double"
                => ReportParameterDataType.Number,
            "System.Decimal" => ReportParameterDataType.Decimal,
            "System.DateOnly" => ReportParameterDataType.Date,
            "System.DateTime" or "System.DateTimeOffset" => ReportParameterDataType.DateTime,
            _ => ReportParameterDataType.String
        };
    }

    private static ReportFilterOperator ParseFilterOperator(string? operatorText)
    {
        return operatorText?.Trim().ToLowerInvariant() switch
        {
            "notequal" => ReportFilterOperator.NotEqual,
            "greaterthan" => ReportFilterOperator.GreaterThan,
            "greaterthanorequal" => ReportFilterOperator.GreaterThanOrEqual,
            "lessthan" => ReportFilterOperator.LessThan,
            "lessthanorequal" => ReportFilterOperator.LessThanOrEqual,
            "like" or "contains" => ReportFilterOperator.Contains,
            _ => ReportFilterOperator.Equal
        };
    }

    private static ReportSortDirection ParseSortDirection(string? direction)
    {
        return string.Equals(direction?.Trim(), "Descending", StringComparison.OrdinalIgnoreCase)
            ? ReportSortDirection.Descending
            : ReportSortDirection.Ascending;
    }

    private static ReportSizingMode ParseSizingMode(string? sizing)
    {
        return sizing?.Trim().ToLowerInvariant() switch
        {
            "stretch" => ReportSizingMode.Stretch,
            "fitproportional" => ReportSizingMode.FitProportional,
            _ => ReportSizingMode.OriginalSize
        };
    }

    private static ReportPageBreakLocation ParsePageBreakLocation(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "end" => ReportPageBreakLocation.End,
            "startandend" => ReportPageBreakLocation.StartAndEnd,
            "between" => ReportPageBreakLocation.Between,
            _ => ReportPageBreakLocation.Start
        };
    }

    private static bool ParseBoolean(string? value, bool defaultValue = false)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private readonly record struct EmbeddedImageInfo(byte[] Data, string? ContentType);
}
