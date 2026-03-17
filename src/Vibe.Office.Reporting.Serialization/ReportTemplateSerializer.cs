using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vibe.Office.Reporting.Serialization;

/// <summary>
/// JSON serializer for native VibeOffice report templates.
/// </summary>
public sealed class ReportTemplateSerializer : IReportTemplateSerializer
{
    public ReportTemplateReadResult Read(string json)
    {
        var diagnostics = new List<ReportDiagnostic>();
        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "Template JSON is empty.",
                "$"));
            return new ReportTemplateReadResult(null, diagnostics);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ParseFailed,
                ex.Message,
                ex.Path));
            return new ReportTemplateReadResult(null, diagnostics);
        }

        if (node is not JsonObject root)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "Template root must be a JSON object.",
                "$"));
            return new ReportTemplateReadResult(null, diagnostics);
        }

        var normalizedRoot = (JsonObject)root.DeepClone();
        if (!TryNormalizeSchema(normalizedRoot, diagnostics))
        {
            return new ReportTemplateReadResult(null, diagnostics);
        }

        ValidateReportDefinition(normalizedRoot, "$", diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error))
        {
            return new ReportTemplateReadResult(null, diagnostics);
        }

        PromoteItemTypeFirstRecursively(normalizedRoot);

        try
        {
            var reportDefinition = normalizedRoot.Deserialize(ReportTemplateJsonContext.Default.ReportDefinition);
            if (reportDefinition is null)
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.DeserializationFailed,
                    "Template JSON did not produce a report definition.",
                    "$"));
                return new ReportTemplateReadResult(null, diagnostics);
            }

            ReportTemplateModelNormalizer.Normalize(reportDefinition);
            reportDefinition.SchemaVersion = ReportDefinition.CurrentSchemaVersion;
            return new ReportTemplateReadResult(reportDefinition, diagnostics);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DeserializationFailed,
                ex.Message,
                ex.Path));
            return new ReportTemplateReadResult(null, diagnostics);
        }
    }

    public async ValueTask<ReportTemplateReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var json = await reader.ReadToEndAsync(cancellationToken);
        return Read(json);
    }

    public ReportTemplateWriteResult Write(ReportDefinition reportDefinition)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        var diagnostics = new List<ReportDiagnostic>();
        var node = JsonSerializer.SerializeToNode(reportDefinition, ReportTemplateJsonContext.Default.ReportDefinition);
        if (node is not JsonObject root)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "Could not serialize the report definition.",
                "$"));
            return new ReportTemplateWriteResult(string.Empty, diagnostics);
        }

        if (reportDefinition.SchemaVersion != ReportDefinition.CurrentSchemaVersion)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.WriteNormalized,
                $"Schema version was normalized from {reportDefinition.SchemaVersion} to {ReportDefinition.CurrentSchemaVersion}.",
                "$.schemaVersion"));
        }

        root["schemaVersion"] = ReportDefinition.CurrentSchemaVersion;
        var json = root.ToJsonString(ReportTemplateJsonContext.Default.Options);
        return new ReportTemplateWriteResult(json, diagnostics);
    }

    public async ValueTask<ReportTemplateWriteResult> WriteAsync(
        ReportDefinition reportDefinition,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var result = Write(reportDefinition);
        if (!result.HasErrors)
        {
            var buffer = Encoding.UTF8.GetBytes(result.Text);
            await stream.WriteAsync(buffer, cancellationToken);
        }

        return result;
    }

    private static bool TryNormalizeSchema(JsonObject root, List<ReportDiagnostic> diagnostics)
    {
        var version = ReadSchemaVersion(root);
        if (version > ReportDefinition.CurrentSchemaVersion)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.UnsupportedSchemaVersion,
                $"Schema version {version} is newer than the supported version {ReportDefinition.CurrentSchemaVersion}.",
                "$.schemaVersion"));
            return false;
        }

        if (version < 0)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.UnsupportedSchemaVersion,
                $"Schema version {version} is invalid.",
                "$.schemaVersion"));
            return false;
        }

        if (version == 0)
        {
            MigrateFromVersion0(root);
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.SchemaUpgraded,
                $"Schema version 0 was upgraded to {ReportDefinition.CurrentSchemaVersion}.",
                "$.schemaVersion"));
        }

        root["schemaVersion"] = ReportDefinition.CurrentSchemaVersion;
        return true;
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue("schemaVersion", out var versionNode) || versionNode is null)
        {
            return 0;
        }

        if (versionNode is JsonValue value && value.TryGetValue<int>(out var version))
        {
            return version;
        }

        return -1;
    }

    private static void MigrateFromVersion0(JsonObject root)
    {
        RenameProperty(root, "title", "name");
        RenameProperty(root, "reportParameters", "parameters");

        if (root.TryGetPropertyValue("sections", out var sectionsNode) && sectionsNode is JsonArray sections)
        {
            for (var index = 0; index < sections.Count; index++)
            {
                if (sections[index] is not JsonObject section)
                {
                    continue;
                }

                RenameProperty(section, "body", "bodyItems");
                RenameProperty(section, "header", "headerItems");
                RenameProperty(section, "footer", "footerItems");

                MigrateItems(section["headerItems"] as JsonArray);
                MigrateItems(section["footerItems"] as JsonArray);
                MigrateItems(section["bodyItems"] as JsonArray);
            }
        }

        NormalizeAliasesRecursively(root);
    }

    private static void MigrateItems(JsonArray? items)
    {
        if (items is null)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item)
            {
                continue;
            }

            RenameProperty(item, "kind", "itemType");
            if (item.TryGetPropertyValue("itemType", out var itemTypeNode)
                && itemTypeNode is JsonValue itemTypeValue
                && itemTypeValue.TryGetValue<string>(out var itemType)
                && !string.IsNullOrWhiteSpace(itemType))
            {
                item["itemType"] = NormalizeItemType(itemType);
            }
        }
    }

    private static void NormalizeAliasesRecursively(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                RenameProperty(obj, "kind", "itemType");
                if (obj.TryGetPropertyValue("itemType", out var itemTypeNode)
                    && itemTypeNode is JsonValue itemTypeValue
                    && itemTypeValue.TryGetValue<string>(out var itemType)
                    && !string.IsNullOrWhiteSpace(itemType))
                {
                    obj["itemType"] = NormalizeItemType(itemType);
                }

                foreach (var property in obj)
                {
                    NormalizeAliasesRecursively(property.Value);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    NormalizeAliasesRecursively(child);
                }

                break;
        }
    }

    private static void PromoteItemTypeFirstRecursively(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("itemType"))
                {
                    PromotePropertyToFront(obj, "itemType");
                }

                foreach (var property in obj)
                {
                    PromoteItemTypeFirstRecursively(property.Value);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    PromoteItemTypeFirstRecursively(child);
                }

                break;
        }
    }

    private static void PromotePropertyToFront(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var targetValue))
        {
            return;
        }

        var snapshot = obj.ToList();
        obj.Clear();
        obj[propertyName] = targetValue;
        foreach (var pair in snapshot)
        {
            if (string.Equals(pair.Key, propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            obj[pair.Key] = pair.Value;
        }
    }

    private static string NormalizeItemType(string itemType)
    {
        return itemType.Trim().ToLowerInvariant() switch
        {
            "text" => "TextItem",
            "textitem" => "TextItem",
            "image" => "ImageItem",
            "imageitem" => "ImageItem",
            "line" => "LineItem",
            "lineitem" => "LineItem",
            "shape" => "ShapeItem",
            "shapeitem" => "ShapeItem",
            "container" => "ContainerItem",
            "containeritem" => "ContainerItem",
            "chart" => "ChartItem",
            "chartitem" => "ChartItem",
            "gauge" => "GaugeItem",
            "gaugeitem" => "GaugeItem",
            "tablix" => "TablixItem",
            "tablixitem" => "TablixItem",
            "subreport" => "SubreportItem",
            "subreportitem" => "SubreportItem",
            "documenttemplate" => "DocumentTemplateItem",
            "documenttemplateitem" => "DocumentTemplateItem",
            _ => itemType
        };
    }

    private static void RenameProperty(JsonObject node, string oldName, string newName)
    {
        if (node.ContainsKey(newName))
        {
            node.Remove(oldName);
            return;
        }

        if (!node.TryGetPropertyValue(oldName, out var oldValue))
        {
            return;
        }

        node.Remove(oldName);
        node[newName] = oldValue;
    }

    private static void ValidateReportDefinition(JsonObject root, string path, List<ReportDiagnostic> diagnostics)
    {
        ValidateKnownProperties(root, path, diagnostics,
            "schemaVersion",
            "id",
            "name",
            "description",
            "sections",
            "parameters",
            "dataSources",
            "dataSets",
            "styles",
            "sharedTemplates",
            "metadata");

        if (root["sections"] is not JsonArray sections || sections.Count == 0)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "Report templates must declare at least one section.",
                $"{path}.sections"));
            return;
        }

        ValidateArray(sections, $"{path}.sections", diagnostics, ValidateSection);
        ValidateArray(root["parameters"] as JsonArray, $"{path}.parameters", diagnostics, ValidateParameter);
        ValidateArray(root["dataSources"] as JsonArray, $"{path}.dataSources", diagnostics, ValidateDataSource);
        ValidateArray(root["dataSets"] as JsonArray, $"{path}.dataSets", diagnostics, ValidateDataSet);
        ValidateArray(root["styles"] as JsonArray, $"{path}.styles", diagnostics, ValidateStyle);
        ValidateArray(root["sharedTemplates"] as JsonArray, $"{path}.sharedTemplates", diagnostics, ValidateSharedTemplate);
    }

    private static void ValidateSection(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject section)
        {
            return;
        }

        ValidateKnownProperties(section, path, diagnostics,
            "id",
            "name",
            "pageSettings",
            "visibilityExpression",
            "bookmarkExpression",
            "headerItems",
            "footerItems",
            "bodyItems");

        ValidatePageSettings(section["pageSettings"], $"{path}.pageSettings", diagnostics);
        ValidateArray(section["headerItems"] as JsonArray, $"{path}.headerItems", diagnostics, ValidateItem);
        ValidateArray(section["footerItems"] as JsonArray, $"{path}.footerItems", diagnostics, ValidateItem);
        ValidateArray(section["bodyItems"] as JsonArray, $"{path}.bodyItems", diagnostics, ValidateItem);
    }

    private static void ValidatePageSettings(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject pageSettings)
        {
            return;
        }

        ValidateKnownProperties(pageSettings, path, diagnostics,
            "width",
            "height",
            "orientation",
            "marginLeft",
            "marginTop",
            "marginRight",
            "marginBottom",
            "columnCount",
            "columnGap");
    }

    private static void ValidateParameter(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject parameter)
        {
            return;
        }

        ValidateKnownProperties(parameter, path, diagnostics,
            "id",
            "displayName",
            "dataType",
            "isMultiValue",
            "allowNull",
            "defaultValueExpression",
            "prompt",
            "availableValuesDataSetId",
            "valueField",
            "labelField",
            "visibility",
            "dependencies");
    }

    private static void ValidateDataSource(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject dataSource)
        {
            return;
        }

        ValidateKnownProperties(dataSource, path, diagnostics,
            "id",
            "providerId",
            "connectionName",
            "options",
            "credentialMode",
            "timeoutSeconds");
    }

    private static void ValidateDataSet(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject dataSet)
        {
            return;
        }

        ValidateKnownProperties(dataSet, path, diagnostics,
            "id",
            "dataSourceId",
            "query",
            "parameters",
            "calculatedFields",
            "filters",
            "sorts",
            "expectedFields");

        ValidateArray(dataSet["parameters"] as JsonArray, $"{path}.parameters", diagnostics, ValidateDataSetParameter);
        ValidateArray(dataSet["calculatedFields"] as JsonArray, $"{path}.calculatedFields", diagnostics, ValidateCalculatedField);
        ValidateArray(dataSet["filters"] as JsonArray, $"{path}.filters", diagnostics, ValidateFilter);
        ValidateArray(dataSet["sorts"] as JsonArray, $"{path}.sorts", diagnostics, ValidateSort);
        ValidateArray(dataSet["expectedFields"] as JsonArray, $"{path}.expectedFields", diagnostics, ValidateField);
    }

    private static void ValidateDataSetParameter(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject parameter)
        {
            return;
        }

        ValidateKnownProperties(parameter, path, diagnostics, "name", "valueExpression");
    }

    private static void ValidateCalculatedField(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject field)
        {
            return;
        }

        ValidateKnownProperties(field, path, diagnostics, "name", "expression", "dataType");
    }

    private static void ValidateFilter(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject filter)
        {
            return;
        }

        ValidateKnownProperties(filter, path, diagnostics, "expression", "operator", "valueExpression");
    }

    private static void ValidateSort(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject sort)
        {
            return;
        }

        ValidateKnownProperties(sort, path, diagnostics, "expression", "direction");
    }

    private static void ValidateField(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject field)
        {
            return;
        }

        ValidateKnownProperties(field, path, diagnostics, "name", "dataType");
    }

    private static void ValidateStyle(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject style)
        {
            return;
        }

        ValidateKnownProperties(style, path, diagnostics,
            "id",
            "parentStyleId",
            "fontFamily",
            "fontSize",
            "foreground",
            "background",
            "bold",
            "italic");
    }

    private static void ValidateSharedTemplate(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject template)
        {
            return;
        }

        ValidateKnownProperties(template, path, diagnostics,
            "id",
            "format",
            "isEmbedded",
            "source",
            "content");
    }

    private static void ValidateItem(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject item)
        {
            return;
        }

        string? normalizedItemType = null;
        if (item.TryGetPropertyValue("itemType", out var itemTypeNode)
            && itemTypeNode is JsonValue itemTypeValue
            && itemTypeValue.TryGetValue<string>(out var itemType))
        {
            normalizedItemType = NormalizeItemType(itemType);
            if (!KnownItemTypes.Contains(normalizedItemType))
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.UnknownItemType,
                    $"Unsupported report item type '{itemType}'.",
                    $"{path}.itemType"));
                return;
            }
        }
        else
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.UnknownItemType,
                "Report item is missing 'itemType'.",
                $"{path}.itemType"));
            return;
        }

        ValidateKnownProperties(item, path, diagnostics, GetKnownItemProperties(normalizedItemType));
        ValidateBounds(item["bounds"], $"{path}.bounds", diagnostics);
        ValidateDrillthroughAction(item["drillthroughAction"], $"{path}.drillthroughAction", diagnostics);

        ValidateArray(item["series"] as JsonArray, $"{path}.series", diagnostics, ValidateChartSeries);
        ValidateArray(item["items"] as JsonArray, $"{path}.items", diagnostics, ValidateItem);
        ValidateArray(item["columns"] as JsonArray, $"{path}.columns", diagnostics, ValidateTablixColumn);
        ValidateArray(item["rows"] as JsonArray, $"{path}.rows", diagnostics, ValidateTablixRow);
        ValidateArray(item["parameters"] as JsonArray, $"{path}.parameters", diagnostics, ValidateParameterBinding);
    }

    private static string[] GetKnownItemProperties(string itemType)
    {
        var properties = new List<string>
        {
            "itemType",
            "id",
            "name",
            "bounds",
            "visibilityExpression",
            "bookmarkExpression",
            "tooltipExpression",
            "drillthroughAction",
            "styleName"
        };

        switch (itemType)
        {
            case "TextItem":
                properties.AddRange(["staticText", "valueExpression", "formatString", "canGrow", "canShrink"]);
                break;
            case "ImageItem":
                properties.AddRange(["sourceKind", "valueExpression", "mimeType", "embeddedData", "sizingMode"]);
                break;
            case "LineItem":
                properties.AddRange(["x2", "y2"]);
                break;
            case "ShapeItem":
                properties.Add("shape");
                break;
            case "ContainerItem":
                properties.Add("items");
                break;
            case "ChartItem":
                properties.AddRange(["dataSetId", "categoryExpression", "titleExpression", "series"]);
                break;
            case "GaugeItem":
                properties.AddRange([
                    "dataSetId",
                    "gaugeKind",
                    "valueExpression",
                    "minimumExpression",
                    "maximumExpression",
                    "targetValueExpression",
                    "labelExpression",
                    "rawRdlXml"]);
                break;
            case "TablixItem":
                properties.AddRange(["dataSetId", "repeatHeaderRows", "columns", "rows"]);
                break;
            case "SubreportItem":
                properties.AddRange(["reportReferenceId", "parameters"]);
                break;
            case "DocumentTemplateItem":
                properties.AddRange(["templateId", "templateFormat", "embeddedContent", "bindings"]);
                break;
        }

        return properties.ToArray();
    }

    private static void ValidateBounds(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject bounds)
        {
            return;
        }

        ValidateKnownProperties(bounds, path, diagnostics, "x", "y", "width", "height");
    }

    private static void ValidateDrillthroughAction(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject action)
        {
            return;
        }

        ValidateKnownProperties(action, path, diagnostics, "reportReferenceId", "parameters");
        ValidateArray(action["parameters"] as JsonArray, $"{path}.parameters", diagnostics, ValidateParameterBinding);
    }

    private static void ValidateParameterBinding(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject binding)
        {
            return;
        }

        ValidateKnownProperties(binding, path, diagnostics, "parameterId", "valueExpression");
    }

    private static void ValidateChartSeries(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject series)
        {
            return;
        }

        ValidateKnownProperties(series, path, diagnostics, "nameExpression", "valueExpression", "colorExpression");
    }

    private static void ValidateTablixColumn(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject column)
        {
            return;
        }

        ValidateKnownProperties(column, path, diagnostics, "id", "width");
    }

    private static void ValidateTablixRow(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject row)
        {
            return;
        }

        ValidateKnownProperties(row, path, diagnostics, "id", "isHeader", "cells");
        ValidateArray(row["cells"] as JsonArray, $"{path}.cells", diagnostics, ValidateTablixCell);
    }

    private static void ValidateTablixCell(JsonNode? node, string path, List<ReportDiagnostic> diagnostics)
    {
        if (node is not JsonObject cell)
        {
            return;
        }

        ValidateKnownProperties(cell, path, diagnostics,
            "text",
            "valueExpression",
            "formatString",
            "styleName",
            "contentItem",
            "rowSpan",
            "columnSpan");
        ValidateItem(cell["contentItem"] as JsonObject, $"{path}.contentItem", diagnostics);
    }

    private static void ValidateArray(
        JsonArray? array,
        string path,
        List<ReportDiagnostic> diagnostics,
        Action<JsonNode?, string, List<ReportDiagnostic>> validator)
    {
        if (array is null)
        {
            return;
        }

        for (var index = 0; index < array.Count; index++)
        {
            validator(array[index], $"{path}[{index}]", diagnostics);
        }
    }

    private static void ValidateKnownProperties(
        JsonObject node,
        string path,
        List<ReportDiagnostic> diagnostics,
        params string[] knownProperties)
    {
        var known = new HashSet<string>(knownProperties, StringComparer.Ordinal);
        foreach (var property in node)
        {
            if (known.Contains(property.Key))
            {
                continue;
            }

            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.UnknownProperty,
                $"Unknown property '{property.Key}'.",
                $"{path}.{property.Key}"));
        }
    }

    private static readonly HashSet<string> KnownItemTypes = new(StringComparer.Ordinal)
    {
        "TextItem",
        "ImageItem",
        "LineItem",
        "ShapeItem",
        "ChartItem",
        "TablixItem",
        "SubreportItem",
        "DocumentTemplateItem"
    };
}
