using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ProEdit.Documents;
using ProEdit.Documents.Data;
using ProEdit.Reporting;
using ProEdit.Reporting.Data;

namespace ProEdit.Mcp.Documents;

/// <summary>
/// Captures the reporting/data context used for MCP-driven document bindings.
/// </summary>
public sealed class DocumentMcpDataContext
{
    /// <summary>
    /// Gets or sets the backing report definition.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the provider registry.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; set; } = ReportDataProviders.CreateDefaultRegistry();

    /// <summary>
    /// Gets or sets the host data registry.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

    /// <summary>
    /// Gets or sets the execution culture.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the UI culture.
    /// </summary>
    public CultureInfo? UiCulture { get; set; }

    /// <summary>
    /// Gets or sets the execution time zone.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }
}

/// <summary>
/// Represents one document exposed through MCP.
/// </summary>
public sealed class DocumentMcpEntry
{
    /// <summary>
    /// Gets or sets the stable document identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document instance.
    /// </summary>
    public Document Document { get; set; } = new();

    /// <summary>
    /// Gets or sets the optional data-binding context used by the MCP bind tool.
    /// </summary>
    public DocumentMcpDataContext? DataContext { get; set; }
}

/// <summary>
/// Resolves documents exposed through MCP.
/// </summary>
public interface IDocumentMcpStore
{
    /// <summary>
    /// Lists the available documents.
    /// </summary>
    /// <returns>The exposed document entries.</returns>
    IReadOnlyList<DocumentMcpEntry> ListDocuments();

    /// <summary>
    /// Attempts to resolve one document by id.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="entry">Receives the document entry.</param>
    /// <returns><see langword="true" /> when the document exists; otherwise <see langword="false" />.</returns>
    bool TryGetDocument(string documentId, out DocumentMcpEntry entry);
}

/// <summary>
/// In-memory implementation of <see cref="IDocumentMcpStore" />.
/// </summary>
public sealed class InMemoryDocumentMcpStore : IDocumentMcpStore
{
    private readonly Dictionary<string, DocumentMcpEntry> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers one document entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Register(DocumentMcpEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        _documents[entry.Id] = entry;
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentMcpEntry> ListDocuments()
    {
        return _documents.Values
            .OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public bool TryGetDocument(string documentId, out DocumentMcpEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return _documents.TryGetValue(documentId, out entry!);
    }
}

/// <summary>
/// MCP feature that exposes document tools and resources.
/// </summary>
public sealed class DocumentMcpFeature : IMcpFeature
{
    private readonly IDocumentDataBinder _binder;
    private readonly IDocumentMcpStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentMcpFeature" /> class.
    /// </summary>
    /// <param name="store">The document store.</param>
    /// <param name="binder">The optional document data binder.</param>
    public DocumentMcpFeature(
        IDocumentMcpStore store,
        IDocumentDataBinder? binder = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _binder = binder ?? new DocumentDataBinder();
    }

    /// <inheritdoc />
    public void Register(McpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RegisterTool(new DocumentDescribeToolHandler(_store));
        builder.RegisterTool(new DocumentBindDataToolHandler(_store, _binder));
        builder.RegisterResourceProvider(new DocumentResourceProvider(_store));
    }

    private sealed class DocumentDescribeToolHandler : IMcpToolHandler
    {
        private readonly IDocumentMcpStore _store;

        public DocumentDescribeToolHandler(IDocumentMcpStore store)
        {
            _store = store;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "document.describe",
            Title = "Describe Document",
            Description = "Returns a structural summary of one registered document.",
            InputSchema = CreateDocumentIdSchema()
        };

        public ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(arguments, _store);
            var summary = BuildDocumentSummary(entry);
            var result = new McpToolCallResult
            {
                StructuredContent = summary
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"Document '{entry.Id}' has {entry.Document.SectionCount} section(s) and {entry.Document.ParagraphCount} paragraph(s)."
            });
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DocumentBindDataToolHandler : IMcpToolHandler
    {
        private readonly IDocumentDataBinder _binder;
        private readonly IDocumentMcpStore _store;

        public DocumentBindDataToolHandler(
            IDocumentMcpStore store,
            IDocumentDataBinder binder)
        {
            _store = store;
            _binder = binder;
        }

        public McpToolDefinition Definition { get; } = new()
        {
            Name = "document.bind_data",
            Title = "Bind Document Data",
            Description = "Executes connector-backed bindings into a registered document's custom XML parts and mail merge state.",
            InputSchema = CreateBindSchema()
        };

        public async ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(arguments, _store);
            if (entry.DataContext is null)
            {
                return CreateErrorResult($"Document '{entry.Id}' does not have a data-binding context.");
            }

            var request = new DocumentDataBindingRequest
            {
                Document = entry.Document,
                ReportDefinition = entry.DataContext.ReportDefinition,
                ProviderRegistry = entry.DataContext.ProviderRegistry,
                HostDataRegistry = entry.DataContext.HostDataRegistry,
                Culture = entry.DataContext.Culture,
                UiCulture = entry.DataContext.UiCulture,
                TimeZone = entry.DataContext.TimeZone
            };

            if (arguments.TryGetProperty("parameters", out var parametersElement)
                && parametersElement.ValueKind == JsonValueKind.Object)
            {
                PopulateParameterValues(parametersElement, request.ParameterValues);
            }

            if (arguments.TryGetProperty("customXmlBindings", out var customXmlBindingsElement)
                && customXmlBindingsElement.ValueKind == JsonValueKind.Array)
            {
                for (var index = 0; index < customXmlBindingsElement.GetArrayLength(); index++)
                {
                    var item = customXmlBindingsElement[index];
                    request.CustomXmlBindings.Add(new DocumentCustomXmlBindingDefinition
                    {
                        DataSetId = RequireString(item, "dataSetId"),
                        StoreItemId = RequireString(item, "storeItemId"),
                        RootElementName = GetOptionalString(item, "rootElementName") ?? "root",
                        RowElementName = GetOptionalString(item, "rowElementName") ?? "row",
                        NamespaceUri = GetOptionalString(item, "namespaceUri")
                    });
                }
            }

            if (arguments.TryGetProperty("mailMergeBindings", out var mailMergeBindingsElement)
                && mailMergeBindingsElement.ValueKind == JsonValueKind.Array)
            {
                for (var index = 0; index < mailMergeBindingsElement.GetArrayLength(); index++)
                {
                    var item = mailMergeBindingsElement[index];
                    request.MailMergeBindings.Add(new DocumentMailMergeBindingDefinition
                    {
                        DataSetId = RequireString(item, "dataSetId"),
                        MainDocumentType = GetOptionalString(item, "mainDocumentType") ?? MailMergeData.DefaultMainDocumentType
                    });
                }
            }

            var bindingResult = await _binder.BindAsync(request, cancellationToken);
            var summary = BuildDocumentSummary(entry);
            summary["diagnostics"] = CreateDiagnosticsArray(bindingResult.Diagnostics);

            var result = new McpToolCallResult
            {
                StructuredContent = summary,
                IsError = bindingResult.HasErrors
            };
            result.Content.Add(new McpTextContentItem
            {
                Text = $"Document '{entry.Id}' updated with {entry.Document.CustomXmlParts.Count} custom XML part(s) and {(entry.Document.MailMergeData?.Records.Count ?? 0)} mail merge record(s)."
            });
            return result;
        }
    }

    private sealed class DocumentResourceProvider : IMcpResourceProvider
    {
        private readonly IDocumentMcpStore _store;

        public DocumentResourceProvider(IDocumentMcpStore store)
        {
            _store = store;
        }

        public IReadOnlyList<McpResourceDefinition> ListResources(McpRequestContext context)
        {
            var resources = new List<McpResourceDefinition>();
            var entries = _store.ListDocuments();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                resources.Add(new McpResourceDefinition
                {
                    Uri = GetSummaryUri(entry.Id),
                    Name = $"{entry.Name} Summary",
                    Description = $"Structural summary for document '{entry.Id}'."
                });
                resources.Add(new McpResourceDefinition
                {
                    Uri = GetCustomXmlUri(entry.Id),
                    Name = $"{entry.Name} Custom XML",
                    Description = $"Custom XML parts for document '{entry.Id}'."
                });
                resources.Add(new McpResourceDefinition
                {
                    Uri = GetMailMergeUri(entry.Id),
                    Name = $"{entry.Name} Mail Merge",
                    Description = $"Mail merge state for document '{entry.Id}'."
                });
            }

            return resources;
        }

        public ValueTask<McpResourceReadResult?> TryReadAsync(
            string uri,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uri);

            if (!TryParseDocumentUri(uri, out var documentId, out var kind)
                || !_store.TryGetDocument(documentId, out var entry))
            {
                return ValueTask.FromResult<McpResourceReadResult?>(null);
            }

            JsonObject payload = kind switch
            {
                "summary" => BuildDocumentSummary(entry),
                "custom-xml" => BuildCustomXmlPayload(entry.Document),
                "mail-merge" => BuildMailMergePayload(entry.Document.MailMergeData),
                _ => throw new KeyNotFoundException($"Unsupported document MCP resource '{uri}'.")
            };

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
            return ValueTask.FromResult<McpResourceReadResult?>(result);
        }
    }

    private static DocumentMcpEntry ResolveEntry(JsonElement arguments, IDocumentMcpStore store)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Document tool arguments must be an object.");
        }

        var documentId = RequireString(arguments, "documentId");
        if (!store.TryGetDocument(documentId, out var entry))
        {
            throw new KeyNotFoundException($"Document '{documentId}' is not registered.");
        }

        return entry;
    }

    private static JsonObject BuildDocumentSummary(DocumentMcpEntry entry)
    {
        var document = entry.Document;
        return new JsonObject
        {
            ["documentId"] = entry.Id,
            ["name"] = entry.Name,
            ["sectionCount"] = document.SectionCount,
            ["paragraphCount"] = document.ParagraphCount,
            ["blockCount"] = document.Blocks.Count,
            ["customXmlPartCount"] = document.CustomXmlParts.Count,
            ["customXmlPartKeys"] = new JsonArray(document.CustomXmlParts.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).Select(static key => (JsonNode?)key).ToArray()),
            ["hasMailMergeData"] = document.MailMergeData is not null,
            ["mailMergeFieldCount"] = document.MailMergeData?.FieldNames.Count ?? 0,
            ["mailMergeRecordCount"] = document.MailMergeData?.Records.Count ?? 0,
            ["hasDataBindingContext"] = entry.DataContext is not null
        };
    }

    private static JsonObject BuildCustomXmlPayload(Document document)
    {
        var parts = new JsonObject();
        foreach (var pair in document.CustomXmlParts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            parts[pair.Key] = pair.Value.ToString(SaveOptions.DisableFormatting);
        }

        return new JsonObject
        {
            ["parts"] = parts
        };
    }

    private static JsonObject BuildMailMergePayload(MailMergeData? mailMergeData)
    {
        var fields = new JsonArray();
        var records = new JsonArray();
        if (mailMergeData is not null)
        {
            for (var index = 0; index < mailMergeData.FieldNames.Count; index++)
            {
                fields.Add(mailMergeData.FieldNames[index]);
            }

            for (var recordIndex = 0; recordIndex < mailMergeData.Records.Count; recordIndex++)
            {
                var recordObject = new JsonObject();
                foreach (var pair in mailMergeData.Records[recordIndex].Fields.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    recordObject[pair.Key] = pair.Value;
                }

                records.Add(recordObject);
            }
        }

        return new JsonObject
        {
            ["mainDocumentType"] = mailMergeData?.MainDocumentType,
            ["fields"] = fields,
            ["records"] = records
        };
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

    private static McpToolCallResult CreateErrorResult(string message)
    {
        var result = new McpToolCallResult
        {
            IsError = true,
            StructuredContent = new JsonObject
            {
                ["error"] = message
            }
        };
        result.Content.Add(new McpTextContentItem
        {
            Text = message
        });
        return result;
    }

    private static JsonObject CreateDocumentIdSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["documentId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The registered document identifier."
                }
            },
            ["required"] = new JsonArray("documentId")
        };
    }

    private static JsonObject CreateBindSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["documentId"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Optional report parameter values supplied as scalars or arrays."
                },
                ["customXmlBindings"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object"
                    }
                },
                ["mailMergeBindings"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object"
                    }
                }
            },
            ["required"] = new JsonArray("documentId")
        };
    }

    private static bool TryParseDocumentUri(string uri, out string documentId, out string kind)
    {
        documentId = string.Empty;
        kind = string.Empty;
        const string prefix = "proedit://documents/";
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

        documentId = Uri.UnescapeDataString(suffix[..separatorIndex]);
        kind = suffix[(separatorIndex + 1)..];
        return true;
    }

    private static string GetSummaryUri(string documentId) => $"proedit://documents/{Uri.EscapeDataString(documentId)}/summary";

    private static string GetCustomXmlUri(string documentId) => $"proedit://documents/{Uri.EscapeDataString(documentId)}/custom-xml";

    private static string GetMailMergeUri(string documentId) => $"proedit://documents/{Uri.EscapeDataString(documentId)}/mail-merge";

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
}
