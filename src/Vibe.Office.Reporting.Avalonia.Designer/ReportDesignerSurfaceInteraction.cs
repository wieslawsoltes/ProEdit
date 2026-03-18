using Avalonia.Input;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

internal enum ReportDesignerSurfaceResizeHandle
{
    None,
    Move,
    North,
    South,
    West,
    East,
    NorthWest,
    NorthEast,
    SouthWest,
    SouthEast
}

internal static class ReportDesignerDragDataFormats
{
    public static readonly DataFormat<string> Payload =
        DataFormat.CreateStringApplicationFormat("vibeoffice/reporting/designer-payload");
    private const char Separator = '\u001F';

    public static string Serialize(ReportDesignerDragPayload payload)
    {
        return payload switch
        {
            ReportDesignerDataFieldDragPayload fieldPayload => string.Join(
                Separator,
                "field",
                fieldPayload.DataSet.Id,
                fieldPayload.FieldName,
                fieldPayload.DataType.ToString()),
            ReportDesignerDataSetDragPayload dataSetPayload => string.Join(
                Separator,
                "dataset",
                dataSetPayload.DataSet.Id),
            ReportDesignerParameterDragPayload parameterPayload => string.Join(
                Separator,
                "parameter",
                parameterPayload.Parameter.Id),
            ReportDesignerBuiltInFieldDragPayload builtInFieldPayload => string.Join(
                Separator,
                "builtin",
                builtInFieldPayload.Definition.Id),
            ReportDesignerImageResourceDragPayload imageResourcePayload => string.Join(
                Separator,
                "image",
                imageResourcePayload.Definition.Id),
            _ => string.Empty
        };
    }

    public static ReportDesignerDragPayload? Deserialize(string? text, ReportDesignerViewModel designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(Separator);
        if (parts.Length < 2)
        {
            return null;
        }

        return parts[0] switch
        {
            "field" when parts.Length >= 4 => DeserializeFieldPayload(parts, designer),
            "dataset" => DeserializeDataSetPayload(parts, designer),
            "parameter" => DeserializeParameterPayload(parts, designer),
            "builtin" => DeserializeBuiltInFieldPayload(parts, designer),
            "image" => DeserializeImageResourcePayload(parts, designer),
            _ => null
        };
    }

    private static ReportDesignerDragPayload? DeserializeFieldPayload(string[] parts, ReportDesignerViewModel designer)
    {
        var dataSet = designer.ReportDefinition.DataSets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, parts[1], StringComparison.OrdinalIgnoreCase));
        if (dataSet is null)
        {
            return null;
        }

        var dataType = Enum.TryParse<ReportParameterDataType>(parts[3], ignoreCase: true, out var parsedDataType)
            ? parsedDataType
            : ReportParameterDataType.String;
        return new ReportDesignerDataFieldDragPayload(dataSet, parts[2], dataType);
    }

    private static ReportDesignerDragPayload? DeserializeDataSetPayload(string[] parts, ReportDesignerViewModel designer)
    {
        var dataSet = designer.ReportDefinition.DataSets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, parts[1], StringComparison.OrdinalIgnoreCase));
        return dataSet is null ? null : new ReportDesignerDataSetDragPayload(dataSet);
    }

    private static ReportDesignerDragPayload? DeserializeParameterPayload(string[] parts, ReportDesignerViewModel designer)
    {
        var parameter = designer.ReportDefinition.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, parts[1], StringComparison.OrdinalIgnoreCase));
        return parameter is null ? null : new ReportDesignerParameterDragPayload(parameter);
    }

    private static ReportDesignerDragPayload? DeserializeBuiltInFieldPayload(string[] parts, ReportDesignerViewModel designer)
    {
        var definition = designer.TryResolveBuiltInFieldDefinition(parts[1]);
        return definition is null ? null : new ReportDesignerBuiltInFieldDragPayload(definition);
    }

    private static ReportDesignerDragPayload? DeserializeImageResourcePayload(string[] parts, ReportDesignerViewModel designer)
    {
        var definition = designer.TryResolveImageResourceDefinition(parts[1]);
        return definition is null ? null : new ReportDesignerImageResourceDragPayload(definition);
    }
}

internal abstract class ReportDesignerDragPayload;

internal sealed class ReportDesignerDataFieldDragPayload : ReportDesignerDragPayload
{
    public ReportDesignerDataFieldDragPayload(
        ReportDataSetDefinition dataSet,
        string fieldName,
        ReportParameterDataType dataType)
    {
        DataSet = dataSet ?? throw new ArgumentNullException(nameof(dataSet));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        DataType = dataType;
    }

    public ReportDataSetDefinition DataSet { get; }

    public string FieldName { get; }

    public ReportParameterDataType DataType { get; }
}

internal sealed class ReportDesignerDataSetDragPayload : ReportDesignerDragPayload
{
    public ReportDesignerDataSetDragPayload(ReportDataSetDefinition dataSet)
    {
        DataSet = dataSet ?? throw new ArgumentNullException(nameof(dataSet));
    }

    public ReportDataSetDefinition DataSet { get; }
}

internal sealed class ReportDesignerParameterDragPayload : ReportDesignerDragPayload
{
    public ReportDesignerParameterDragPayload(ReportParameterDefinition parameter)
    {
        Parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
    }

    public ReportParameterDefinition Parameter { get; }
}

internal sealed class ReportDesignerBuiltInFieldDragPayload : ReportDesignerDragPayload
{
    public ReportDesignerBuiltInFieldDragPayload(ReportDesignerBuiltInFieldDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ReportDesignerBuiltInFieldDefinition Definition { get; }
}

internal sealed class ReportDesignerImageResourceDragPayload : ReportDesignerDragPayload
{
    public ReportDesignerImageResourceDragPayload(ReportDesignerImageResourceDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ReportDesignerImageResourceDefinition Definition { get; }
}

internal static class ReportDesignerDragPayloadFactory
{
    public static ReportDesignerDragPayload? Create(ReportDesignerDataNodeViewModel? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Kind == ReportDesignerDataNodeKind.DataSet
            && node.Target is ReportDataSetDefinition dataSet)
        {
            return new ReportDesignerDataSetDragPayload(dataSet);
        }

        if (node.Kind == ReportDesignerDataNodeKind.Parameter
            && node.Target is ReportParameterDefinition parameter)
        {
            return new ReportDesignerParameterDragPayload(parameter);
        }

        if (node.Kind == ReportDesignerDataNodeKind.BuiltInField
            && node.Target is ReportDesignerBuiltInFieldDefinition builtInField)
        {
            return new ReportDesignerBuiltInFieldDragPayload(builtInField);
        }

        if (node.Kind == ReportDesignerDataNodeKind.ImageResource
            && node.Target is ReportDesignerImageResourceDefinition imageResource)
        {
            return new ReportDesignerImageResourceDragPayload(imageResource);
        }

        if (node.SelectionTarget is not ReportDataSetDefinition ownerDataSet)
        {
            return null;
        }

        return node switch
        {
            { Kind: ReportDesignerDataNodeKind.QueryField, Target: ReportFieldDefinition queryField } =>
                new ReportDesignerDataFieldDragPayload(ownerDataSet, queryField.Name, queryField.DataType),
            { Kind: ReportDesignerDataNodeKind.CalculatedField, Target: ReportCalculatedFieldDefinition calculatedField } =>
                new ReportDesignerDataFieldDragPayload(ownerDataSet, calculatedField.Name, calculatedField.DataType),
            _ => null
        };
    }
}
