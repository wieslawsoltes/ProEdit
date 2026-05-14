using System.Text.Json.Serialization;

namespace ProEdit.Reporting.Serialization;

/// <summary>
/// Source-generated JSON metadata for report templates.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ReportDefinition))]
internal partial class ReportTemplateJsonContext : JsonSerializerContext
{
}
