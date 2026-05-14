using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProEdit.Reporting;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Export;
using ProEdit.Reporting.Serialization;
using ProEdit.Reporting.Service;

namespace ProEdit.Mcp.Reporting;

/// <summary>
/// Represents one report exposed through MCP.
/// </summary>
public sealed class ReportMcpEntry
{
    /// <summary>
    /// Gets or sets the stable report identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report definition.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the host execution environment.
    /// </summary>
    public ReportExecutionEnvironment Environment { get; set; } = ReportExecutionEnvironment.CreateDefault();
}

/// <summary>
/// Resolves reports exposed through MCP.
/// </summary>
public interface IReportMcpStore
{
    /// <summary>
    /// Lists the available reports.
    /// </summary>
    /// <returns>The exposed reports.</returns>
    IReadOnlyList<ReportMcpEntry> ListReports();

    /// <summary>
    /// Attempts to resolve one report by id.
    /// </summary>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="entry">Receives the report entry.</param>
    /// <returns><see langword="true" /> when the report exists; otherwise <see langword="false" />.</returns>
    bool TryGetReport(string reportId, out ReportMcpEntry entry);
}

/// <summary>
/// In-memory implementation of <see cref="IReportMcpStore" />.
/// </summary>
public sealed class InMemoryReportMcpStore : IReportMcpStore
{
    private readonly Dictionary<string, ReportMcpEntry> _reports = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers one report entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Register(ReportMcpEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        _reports[entry.Id] = entry;
    }

    /// <inheritdoc />
    public IReadOnlyList<ReportMcpEntry> ListReports()
    {
        return _reports.Values
            .OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public bool TryGetReport(string reportId, out ReportMcpEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        return _reports.TryGetValue(reportId, out entry!);
    }
}

/// <summary>
/// MCP feature that exposes reporting tools and resources.
/// </summary>
public sealed class ReportingMcpFeature : IMcpFeature
{
    private readonly ReportDataConnectorCatalog _connectorCatalog;
    private readonly IReportExporter _exporter;
    private readonly IReportMcpStore _store;
    private readonly IReportTemplateSerializer _templateSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportingMcpFeature" /> class.
    /// </summary>
    /// <param name="store">The report store.</param>
    /// <param name="exporter">The optional exporter.</param>
    /// <param name="templateSerializer">The optional template serializer.</param>
    /// <param name="connectorCatalog">The optional connector catalog.</param>
    public ReportingMcpFeature(
        IReportMcpStore store,
        IReportExporter? exporter = null,
        IReportTemplateSerializer? templateSerializer = null,
        ReportDataConnectorCatalog? connectorCatalog = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _exporter = exporter ?? new ReportExporter();
        _templateSerializer = templateSerializer ?? new ReportTemplateSerializer();
        _connectorCatalog = connectorCatalog ?? ReportDataConnectorCatalog.CreateDefault();
    }

    /// <inheritdoc />
    public void Register(McpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RegisterTool(new ReportDescribeToolHandler(_store));
        builder.RegisterTool(new ReportListConnectorsToolHandler(_connectorCatalog));
        builder.RegisterTool(new ReportExecuteToolHandler(_store));
        builder.RegisterTool(new ReportExportToolHandler(_store, _exporter));
        builder.RegisterResourceProvider(new ReportResourceProvider(_store, _templateSerializer, _connectorCatalog));
    }

    private sealed class ReportDescribeToolHandler : IMcpToolHandler
    {
        private readonly IReportMcpStore _store;

        public ReportDescribeToolHandler(IReportMcpStore store)
        {
            _store = store;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "report.describe",
            Title = "Describe Report",
            Description = "Returns a structural summary of one registered report.",
            InputSchema = CreateReportIdSchema()
        };

        public ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(arguments, _store);
            var summary = BuildReportSummary(entry);
            var result = new McpToolCallResult
            {
                StructuredContent = summary
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"Report '{entry.Id}' defines {entry.ReportDefinition.Sections.Count} section(s), {entry.ReportDefinition.Parameters.Count} parameter(s), and {entry.ReportDefinition.DataSets.Count} dataset(s)."
            });
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReportListConnectorsToolHandler : IMcpToolHandler
    {
        private readonly ReportDataConnectorCatalog _catalog;

        public ReportListConnectorsToolHandler(ReportDataConnectorCatalog catalog)
        {
            _catalog = catalog;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "report.list_connectors",
            Title = "List Report Connectors",
            Description = "Lists the reporting connectors exposed by the current MCP host.",
            InputSchema = new JsonObject
            {
                ["type"] = "object"
            }
        };

        public ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var connectors = BuildConnectorPayload(_catalog);
            var result = new McpToolCallResult
            {
                StructuredContent = connectors
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"The MCP host exposes {_catalog.ListConnectors().Count} reporting connector(s)."
            });
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReportExecuteToolHandler : IMcpToolHandler
    {
        private readonly IReportMcpStore _store;

        public ReportExecuteToolHandler(IReportMcpStore store)
        {
            _store = store;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "report.execute",
            Title = "Execute Report",
            Description = "Executes one registered report and returns a semantic summary of the result.",
            InputSchema = CreateExecuteSchema()
        };

        public async ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(arguments, _store);
            var executionResult = await ExecuteAsync(entry, arguments, cancellationToken);
            var payload = BuildExecutionPayload(entry, executionResult);

            var result = new McpToolCallResult
            {
                StructuredContent = payload,
                IsError = executionResult.Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error)
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"Executed report '{entry.Id}' with {executionResult.Metrics.PageCount} page(s), {executionResult.Metrics.DataRowCount} row(s), and {executionResult.Diagnostics.Count} diagnostic(s)."
            });
            return result;
        }
    }

    private sealed class ReportExportToolHandler : IMcpToolHandler
    {
        private readonly IReportExporter _exporter;
        private readonly IReportMcpStore _store;

        public ReportExportToolHandler(
            IReportMcpStore store,
            IReportExporter exporter)
        {
            _store = store;
            _exporter = exporter;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "report.export",
            Title = "Export Report",
            Description = "Executes one registered report and returns an embedded export payload.",
            InputSchema = CreateExportSchema()
        };

        public async ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(arguments, _store);
            var format = ParseExportFormat(arguments);
            var executionResult = await ExecuteAsync(entry, arguments, cancellationToken);
            var exportRequest = new ReportExportRequest
            {
                ExecutionResult = executionResult,
                Format = format,
                Profile = CreateExportProfile(format, arguments)
            };

            await using var stream = new MemoryStream();
            var exportResult = await _exporter.ExportAsync(exportRequest, stream, cancellationToken);
            var bytes = stream.ToArray();
            var payload = BuildExportPayload(entry, format, exportResult);

            var resourceUri = $"proedit://reports/{Uri.EscapeDataString(entry.Id)}/exports/{format.ToString().ToLowerInvariant()}";
            var resource = CreateExportResource(resourceUri, exportResult.MediaType, bytes, IsTextFormat(format));

            var result = new McpToolCallResult
            {
                StructuredContent = payload,
                IsError = exportResult.HasErrors
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"Exported report '{entry.Id}' as {format} ({exportResult.MediaType})."
            });
            result.Content.Add(new McpEmbeddedResourceContentItem
            {
                Resource = resource
            });
            return result;
        }
    }

    private sealed class ReportResourceProvider : IMcpResourceProvider
    {
        private readonly ReportDataConnectorCatalog _connectorCatalog;
        private readonly IReportMcpStore _store;
        private readonly IReportTemplateSerializer _templateSerializer;

        public ReportResourceProvider(
            IReportMcpStore store,
            IReportTemplateSerializer templateSerializer,
            ReportDataConnectorCatalog connectorCatalog)
        {
            _store = store;
            _templateSerializer = templateSerializer;
            _connectorCatalog = connectorCatalog;
        }

        public IReadOnlyList<McpResourceDefinition> ListResources(McpRequestContext context)
        {
            var resources = new List<McpResourceDefinition>
            {
                new()
                {
                    Uri = "proedit://reports/connectors",
                    Name = "Reporting Connectors",
                    Description = "Connector metadata exposed by the reporting MCP feature."
                }
            };

            var entries = _store.ListReports();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                resources.Add(new McpResourceDefinition
                {
                    Uri = GetSummaryUri(entry.Id),
                    Name = $"{entry.Name} Summary",
                    Description = $"Structural summary for report '{entry.Id}'."
                });
                resources.Add(new McpResourceDefinition
                {
                    Uri = GetDefinitionUri(entry.Id),
                    Name = $"{entry.Name} Definition",
                    Description = $"Native ProEdit JSON definition for report '{entry.Id}'."
                });
            }

            return resources;
        }

        public ValueTask<McpResourceReadResult?> TryReadAsync(
            string uri,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(uri, "proedit://reports/connectors", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<McpResourceReadResult?>(CreateJsonResourceResult(
                    uri,
                    BuildConnectorPayload(_connectorCatalog)));
            }

            if (!TryParseReportUri(uri, out var reportId, out var kind)
                || !_store.TryGetReport(reportId, out var entry))
            {
                return ValueTask.FromResult<McpResourceReadResult?>(null);
            }

            return kind switch
            {
                "summary" => ValueTask.FromResult<McpResourceReadResult?>(CreateJsonResourceResult(uri, BuildReportSummary(entry))),
                "definition" => ValueTask.FromResult<McpResourceReadResult?>(CreateTemplateResourceResult(uri, entry, _templateSerializer)),
                _ => ValueTask.FromResult<McpResourceReadResult?>(null)
            };
        }
    }

    private static ReportMcpEntry ResolveEntry(JsonElement arguments, IReportMcpStore store)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Report tool arguments must be an object.");
        }

        var reportId = RequireString(arguments, "reportId");
        if (!store.TryGetReport(reportId, out var entry))
        {
            throw new KeyNotFoundException($"Report '{reportId}' is not registered.");
        }

        return entry;
    }

    private static async ValueTask<ReportExecutionResult> ExecuteAsync(
        ReportMcpEntry entry,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var request = new ReportExecutionRequest
        {
            ReportDefinition = entry.ReportDefinition,
            ExecutionMode = ParseExecutionMode(arguments),
            CachePolicy = ParseCachePolicy(arguments),
            Culture = TryParseCulture(arguments, "culture"),
            UiCulture = TryParseCulture(arguments, "uiCulture"),
            TimeZone = TryParseTimeZone(arguments, "timeZoneId")
        };

        if (arguments.TryGetProperty("parameters", out var parametersElement)
            && parametersElement.ValueKind == JsonValueKind.Object)
        {
            PopulateParameterValues(parametersElement, request.ParameterValues);
        }

        var executor = new ReportExecutor(entry.Environment);
        return await executor.ExecuteAsync(request, cancellationToken);
    }

    private static JsonObject BuildReportSummary(ReportMcpEntry entry)
    {
        return new JsonObject
        {
            ["reportId"] = entry.Id,
            ["name"] = entry.Name,
            ["sectionCount"] = entry.ReportDefinition.Sections.Count,
            ["parameterCount"] = entry.ReportDefinition.Parameters.Count,
            ["dataSourceCount"] = entry.ReportDefinition.DataSources.Count,
            ["dataSetCount"] = entry.ReportDefinition.DataSets.Count,
            ["sharedTemplateCount"] = entry.ReportDefinition.SharedTemplates.Count
        };
    }

    private static JsonObject BuildExecutionPayload(
        ReportMcpEntry entry,
        ReportExecutionResult executionResult)
    {
        return new JsonObject
        {
            ["reportId"] = entry.Id,
            ["pageCount"] = executionResult.Metrics.PageCount,
            ["dataRowCount"] = executionResult.Metrics.DataRowCount,
            ["hasDocument"] = executionResult.Document is not null,
            ["hasMaterializedReport"] = executionResult.MaterializedReport is not null,
            ["resolvedParameters"] = BuildResolvedParameters(executionResult.ResolvedParameters),
            ["diagnostics"] = CreateDiagnosticsArray(executionResult.Diagnostics)
        };
    }

    private static JsonObject BuildExportPayload(
        ReportMcpEntry entry,
        ReportExportFormat format,
        ReportExportResult exportResult)
    {
        return new JsonObject
        {
            ["reportId"] = entry.Id,
            ["format"] = format.ToString(),
            ["mediaType"] = exportResult.MediaType,
            ["fileExtension"] = exportResult.FileExtension,
            ["bytesWritten"] = exportResult.BytesWritten,
            ["diagnostics"] = CreateDiagnosticsArray(exportResult.Diagnostics)
        };
    }

    private static JsonObject BuildConnectorPayload(ReportDataConnectorCatalog catalog)
    {
        var connectors = new JsonArray();
        var entries = catalog.ListConnectors();
        for (var index = 0; index < entries.Count; index++)
        {
            connectors.Add(new JsonObject
            {
                ["providerId"] = entries[index].ProviderId,
                ["displayName"] = entries[index].DisplayName,
                ["category"] = entries[index].Category.ToString(),
                ["capabilities"] = entries[index].Capabilities.ToString(),
                ["providerInvariantName"] = entries[index].ProviderInvariantName,
                ["description"] = entries[index].Description
            });
        }

        return new JsonObject
        {
            ["connectors"] = connectors
        };
    }

    private static JsonObject BuildResolvedParameters(
        IReadOnlyDictionary<string, ReportParameterValue> parameters)
    {
        var result = new JsonObject();
        foreach (var pair in parameters.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value.IsNull)
            {
                result[pair.Key] = null;
                continue;
            }

            if (pair.Value.Values.Count <= 1)
            {
                result[pair.Key] = ConvertScalarNode(pair.Value.GetScalarValue());
                continue;
            }

            var values = new JsonArray();
            for (var index = 0; index < pair.Value.Values.Count; index++)
            {
                values.Add(ConvertScalarNode(pair.Value.Values[index]));
            }

            result[pair.Key] = values;
        }

        return result;
    }

    private static JsonArray CreateDiagnosticsArray(IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        var array = new JsonArray();
        for (var index = 0; index < diagnostics.Count; index++)
        {
            array.Add(new JsonObject
            {
                ["severity"] = diagnostics[index].Severity.ToString(),
                ["code"] = diagnostics[index].Code,
                ["message"] = diagnostics[index].Message,
                ["path"] = diagnostics[index].Path
            });
        }

        return array;
    }

    private static void PopulateParameterValues(
        JsonElement source,
        Dictionary<string, ReportParameterValue> target)
    {
        foreach (var property in source.EnumerateObject())
        {
            target[property.Name] = ConvertParameterValue(property.Value);
        }
    }

    private static ReportParameterValue ConvertParameterValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return ReportParameterValue.FromScalar(null);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var parameterValue = new ReportParameterValue();
            foreach (var item in value.EnumerateArray())
            {
                parameterValue.Values.Add(ConvertScalarValue(item));
            }

            return parameterValue;
        }

        return ReportParameterValue.FromScalar(ConvertScalarValue(value));
    }

    private static object? ConvertScalarValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => value.GetDouble(),
            _ => value.GetRawText()
        };
    }

    private static JsonNode? ConvertScalarNode(object? value)
    {
        return value switch
        {
            null => null,
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            DateTimeOffset dateTimeOffset => JsonValue.Create(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
            DateTime dateTime => JsonValue.Create(dateTime.ToString("O", CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private static ReportExecutionMode ParseExecutionMode(JsonElement arguments)
    {
        var value = GetOptionalString(arguments, "executionMode");
        return string.IsNullOrWhiteSpace(value)
            ? ReportExecutionMode.Default
            : Enum.Parse<ReportExecutionMode>(value, ignoreCase: true);
    }

    private static ReportCachePolicy ParseCachePolicy(JsonElement arguments)
    {
        var value = GetOptionalString(arguments, "cachePolicy");
        return string.IsNullOrWhiteSpace(value)
            ? ReportCachePolicy.PreferCache
            : Enum.Parse<ReportCachePolicy>(value, ignoreCase: true);
    }

    private static CultureInfo? TryParseCulture(JsonElement arguments, string propertyName)
    {
        var value = GetOptionalString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : CultureInfo.GetCultureInfo(value);
    }

    private static TimeZoneInfo? TryParseTimeZone(JsonElement arguments, string propertyName)
    {
        var value = GetOptionalString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : TimeZoneInfo.FindSystemTimeZoneById(value);
    }

    private static ReportExportFormat ParseExportFormat(JsonElement arguments)
    {
        var value = RequireString(arguments, "format");
        return Enum.Parse<ReportExportFormat>(value, ignoreCase: true);
    }

    private static ReportExportProfile? CreateExportProfile(
        ReportExportFormat format,
        JsonElement arguments)
    {
        switch (format)
        {
            case ReportExportFormat.Csv:
            {
                var profile = new CsvReportExportProfile();
                var delimiter = GetOptionalString(arguments, "delimiter");
                if (!string.IsNullOrEmpty(delimiter))
                {
                    profile.Delimiter = delimiter[0];
                }

                profile.TablixItemId = GetOptionalString(arguments, "tablixItemId");
                profile.IncludeHeaderRows = GetOptionalBoolean(arguments, "includeHeaderRows") ?? true;
                return profile;
            }
            case ReportExportFormat.Xlsx:
            {
                var profile = new XlsxReportExportProfile
                {
                    TablixItemId = GetOptionalString(arguments, "tablixItemId"),
                    IncludeHeaderRows = GetOptionalBoolean(arguments, "includeHeaderRows") ?? true,
                    WorkbookAuthor = GetOptionalString(arguments, "workbookAuthor") ?? "ProEdit"
                };
                return profile;
            }
            case ReportExportFormat.Html:
                return new PaginatedReportExportProfile
                {
                    PrettyPrintHtml = GetOptionalBoolean(arguments, "prettyPrintHtml") ?? false
                };
            default:
                return format switch
                {
                    ReportExportFormat.Pdf or ReportExportFormat.Docx or ReportExportFormat.Rtf or ReportExportFormat.Markdown or ReportExportFormat.Xps or ReportExportFormat.Ps
                        => new PaginatedReportExportProfile(),
                    _ => null
                };
        }
    }

    private static bool IsTextFormat(ReportExportFormat format)
    {
        return format is ReportExportFormat.Csv
            or ReportExportFormat.Html
            or ReportExportFormat.Markdown
            or ReportExportFormat.Rtf;
    }

    private static McpResourceContents CreateExportResource(
        string uri,
        string mediaType,
        byte[] bytes,
        bool isText)
    {
        if (isText)
        {
            return new McpTextResourceContents
            {
                Uri = uri,
                MimeType = mediaType,
                Text = Encoding.UTF8.GetString(bytes)
            };
        }

        return new McpBlobResourceContents
        {
            Uri = uri,
            MimeType = mediaType,
            Blob = Convert.ToBase64String(bytes)
        };
    }

    private static McpResourceReadResult CreateJsonResourceResult(string uri, JsonObject payload)
    {
        var result = new McpResourceReadResult();
        result.Contents.Add(new McpTextResourceContents
        {
            Uri = uri,
            MimeType = "application/json",
            Text = payload.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            })
        });
        return result;
    }

    private static McpResourceReadResult CreateTemplateResourceResult(
        string uri,
        ReportMcpEntry entry,
        IReportTemplateSerializer serializer)
    {
        var writeResult = serializer.Write(entry.ReportDefinition);
        var payload = new McpResourceReadResult();
        payload.Contents.Add(new McpTextResourceContents
        {
            Uri = uri,
            MimeType = "application/json",
            Text = writeResult.Text
        });
        return payload;
    }

    private static JsonObject CreateReportIdSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reportId"] = new JsonObject
                {
                    ["type"] = "string"
                }
            },
            ["required"] = new JsonArray("reportId")
        };
    }

    private static JsonObject CreateExecuteSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reportId"] = new JsonObject { ["type"] = "string" },
                ["parameters"] = new JsonObject { ["type"] = "object" },
                ["executionMode"] = new JsonObject { ["type"] = "string" },
                ["cachePolicy"] = new JsonObject { ["type"] = "string" },
                ["culture"] = new JsonObject { ["type"] = "string" },
                ["uiCulture"] = new JsonObject { ["type"] = "string" },
                ["timeZoneId"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("reportId")
        };
    }

    private static JsonObject CreateExportSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reportId"] = new JsonObject { ["type"] = "string" },
                ["format"] = new JsonObject { ["type"] = "string" },
                ["parameters"] = new JsonObject { ["type"] = "object" },
                ["tablixItemId"] = new JsonObject { ["type"] = "string" },
                ["includeHeaderRows"] = new JsonObject { ["type"] = "boolean" },
                ["delimiter"] = new JsonObject { ["type"] = "string" },
                ["workbookAuthor"] = new JsonObject { ["type"] = "string" },
                ["prettyPrintHtml"] = new JsonObject { ["type"] = "boolean" }
            },
            ["required"] = new JsonArray("reportId", "format")
        };
    }

    private static string GetSummaryUri(string reportId) => $"proedit://reports/{Uri.EscapeDataString(reportId)}/summary";

    private static string GetDefinitionUri(string reportId) => $"proedit://reports/{Uri.EscapeDataString(reportId)}/definition";

    private static bool TryParseReportUri(string uri, out string reportId, out string kind)
    {
        reportId = string.Empty;
        kind = string.Empty;
        const string prefix = "proedit://reports/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = uri[prefix.Length..];
        var separatorIndex = suffix.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
        {
            return false;
        }

        reportId = Uri.UnescapeDataString(suffix[..separatorIndex]);
        kind = suffix[(separatorIndex + 1)..];
        return true;
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException($"Property '{propertyName}' is required and must be a non-empty string.");
        }

        return property.GetString()!;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null
            || property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null
            || property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.True
            ? true
            : property.ValueKind == JsonValueKind.False
                ? false
                : bool.Parse(property.GetRawText());
    }
}
