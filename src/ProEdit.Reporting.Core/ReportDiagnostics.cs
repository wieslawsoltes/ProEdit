namespace ProEdit.Reporting;

/// <summary>
/// Severity for reporting diagnostics.
/// </summary>
public enum ReportDiagnosticSeverity
{
    /// <summary>
    /// Informational diagnostic.
    /// </summary>
    Info,

    /// <summary>
    /// Warning diagnostic.
    /// </summary>
    Warning,

    /// <summary>
    /// Error diagnostic.
    /// </summary>
    Error
}

/// <summary>
/// Common diagnostic codes emitted by reporting components.
/// </summary>
public static class ReportDiagnosticCodes
{
    /// <summary>
    /// A JSON payload could not be parsed.
    /// </summary>
    public const string ParseFailed = "report.parse.failed";

    /// <summary>
    /// A schema upgrade was applied while reading a template.
    /// </summary>
    public const string SchemaUpgraded = "report.schema.upgraded";

    /// <summary>
    /// The template schema version is not supported.
    /// </summary>
    public const string UnsupportedSchemaVersion = "report.schema.unsupported";

    /// <summary>
    /// A property is not recognized by the template contract.
    /// </summary>
    public const string UnknownProperty = "report.property.unknown";

    /// <summary>
    /// A report item discriminator is not recognized.
    /// </summary>
    public const string UnknownItemType = "report.itemType.unknown";

    /// <summary>
    /// A required root object is missing or invalid.
    /// </summary>
    public const string InvalidTemplate = "report.template.invalid";

    /// <summary>
    /// A template could not be deserialized into the model.
    /// </summary>
    public const string DeserializationFailed = "report.deserialization.failed";

    /// <summary>
    /// The template was normalized before serialization.
    /// </summary>
    public const string WriteNormalized = "report.write.normalized";

    /// <summary>
    /// An expression could not be parsed.
    /// </summary>
    public const string ExpressionParseFailed = "report.expression.parse.failed";

    /// <summary>
    /// An expression could not be evaluated.
    /// </summary>
    public const string ExpressionEvaluationFailed = "report.expression.evaluate.failed";

    /// <summary>
    /// A required data provider could not be resolved.
    /// </summary>
    public const string DataProviderNotFound = "report.data.provider.notFound";

    /// <summary>
    /// A required data source could not be resolved.
    /// </summary>
    public const string DataSourceNotFound = "report.data.source.notFound";

    /// <summary>
    /// A dataset definition could not be resolved.
    /// </summary>
    public const string DataSetNotFound = "report.data.dataset.notFound";

    /// <summary>
    /// A data provider failed while reading source data.
    /// </summary>
    public const string DataReadFailed = "report.data.read.failed";

    /// <summary>
    /// A dataset failed during execution.
    /// </summary>
    public const string DataSetExecutionFailed = "report.data.dataset.failed";

    /// <summary>
    /// A parameter value could not be resolved.
    /// </summary>
    public const string ParameterResolutionFailed = "report.parameter.resolve.failed";

    /// <summary>
    /// A parameter or field value could not be coerced to the target type.
    /// </summary>
    public const string ValueCoercionFailed = "report.value.coercion.failed";

    /// <summary>
    /// A referenced subreport definition could not be resolved.
    /// </summary>
    public const string SubreportNotFound = "report.subreport.notFound";

    /// <summary>
    /// A recursive subreport reference was detected.
    /// </summary>
    public const string SubreportCycleDetected = "report.subreport.cycle";

    /// <summary>
    /// A referenced document template could not be resolved.
    /// </summary>
    public const string DocumentTemplateNotFound = "report.template.notFound";

    /// <summary>
    /// A document template payload could not be loaded or parsed.
    /// </summary>
    public const string DocumentTemplateLoadFailed = "report.template.load.failed";

    /// <summary>
    /// Report composition into a document failed.
    /// </summary>
    public const string DocumentCompositionFailed = "report.document.compose.failed";

    /// <summary>
    /// Report export failed.
    /// </summary>
    public const string ExportFailed = "report.export.failed";

    /// <summary>
    /// The requested export requires a paginated document.
    /// </summary>
    public const string ExportDocumentRequired = "report.export.document.required";

    /// <summary>
    /// The requested export requires a semantic materialized report.
    /// </summary>
    public const string ExportMaterializedReportRequired = "report.export.materialized.required";

    /// <summary>
    /// The requested tablix item could not be resolved for export.
    /// </summary>
    public const string ExportTablixNotFound = "report.export.tablix.notFound";

    /// <summary>
    /// The requested export is ambiguous without a tablix selection.
    /// </summary>
    public const string ExportTablixSelectionRequired = "report.export.tablix.selectionRequired";

    /// <summary>
    /// The requested export profile is incompatible with the selected format.
    /// </summary>
    public const string ExportProfileInvalid = "report.export.profile.invalid";

    /// <summary>
    /// The requested feature is not yet supported by the current runtime.
    /// </summary>
    public const string UnsupportedFeature = "report.feature.unsupported";

    /// <summary>
    /// An RDL payload could not be parsed.
    /// </summary>
    public const string RdlParseFailed = "report.rdl.parse.failed";

    /// <summary>
    /// The RDL namespace version is not supported.
    /// </summary>
    public const string RdlNamespaceUnsupported = "report.rdl.namespace.unsupported";

    /// <summary>
    /// An RDL size value could not be parsed.
    /// </summary>
    public const string RdlLengthInvalid = "report.rdl.length.invalid";

    /// <summary>
    /// An RDL payload could not be written.
    /// </summary>
    public const string RdlWriteFailed = "report.rdl.write.failed";

    /// <summary>
    /// A requested repository item could not be resolved.
    /// </summary>
    public const string RepositoryItemNotFound = "report.repository.item.notFound";

    /// <summary>
    /// A requested schedule could not be resolved.
    /// </summary>
    public const string ScheduleNotFound = "report.schedule.notFound";

    /// <summary>
    /// A requested delivery target could not be resolved.
    /// </summary>
    public const string DeliveryTargetNotFound = "report.delivery.target.notFound";

    /// <summary>
    /// A requested delivery channel could not be resolved.
    /// </summary>
    public const string DeliveryChannelNotFound = "report.delivery.channel.notFound";

    /// <summary>
    /// Report delivery failed after export.
    /// </summary>
    public const string DeliveryFailed = "report.delivery.failed";

    /// <summary>
    /// A repository or service-layer operation failed unexpectedly.
    /// </summary>
    public const string ServiceOperationFailed = "report.service.operation.failed";

    /// <summary>
    /// A viewer operation failed unexpectedly.
    /// </summary>
    public const string ViewerOperationFailed = "report.viewer.operation.failed";
}

/// <summary>
/// Represents one diagnostic produced by the reporting stack.
/// </summary>
public sealed record ReportDiagnostic(
    ReportDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Path = null);
