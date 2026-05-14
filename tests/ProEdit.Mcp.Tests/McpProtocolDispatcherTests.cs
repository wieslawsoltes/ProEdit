using System.Globalization;
using System.Text.Json;
using ProEdit.Documents;
using ProEdit.Documents.Data;
using ProEdit.Mcp.Documents;
using ProEdit.Mcp.Reporting;
using ProEdit.Reporting;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Service;
using Xunit;

namespace ProEdit.Mcp.Tests;

public sealed class McpProtocolDispatcherTests
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    [Fact]
    public async Task Dispatcher_InitializesAndListsDocumentAndReportingCapabilities()
    {
        var dispatcher = CreateDispatcher();

        using var ping = await SendRequestAsync(dispatcher, 0, "ping");
        Assert.Equal("{}", ping.RootElement.GetProperty("result").GetRawText());

        using var initialize = await SendRequestAsync(
            dispatcher,
            1,
            "initialize",
            """
            {
              "protocolVersion": "2025-11-25",
              "clientInfo": { "name": "tests", "version": "1.0" }
            }
            """);

        Assert.Equal("2025-11-25", initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

        using var beforeReady = await SendRequestAsync(dispatcher, 99, "tools/list");
        Assert.Equal(-32002, beforeReady.RootElement.GetProperty("error").GetProperty("code").GetInt32());

        await SendNotificationAsync(dispatcher, "notifications/initialized");

        using var tools = await SendRequestAsync(dispatcher, 2, "tools/list");
        var toolNames = tools.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("document.describe", toolNames);
        Assert.Contains("document.bind_data", toolNames);
        Assert.Contains("report.describe", toolNames);
        Assert.Contains("report.execute", toolNames);
        Assert.Contains("report.export", toolNames);
        Assert.Contains("report.list_connectors", toolNames);

        using var resources = await SendRequestAsync(dispatcher, 3, "resources/list");
        var uris = resources.RootElement
            .GetProperty("result")
            .GetProperty("resources")
            .EnumerateArray()
            .Select(static item => item.GetProperty("uri").GetString())
            .ToArray();

        Assert.Contains("proedit://documents/doc-1/summary", uris);
        Assert.Contains("proedit://reports/report-1/definition", uris);
        Assert.Contains("proedit://reports/connectors", uris);
    }

    [Fact]
    public async Task Dispatcher_DocumentBindDataToolUpdatesCustomXmlAndMailMergeState()
    {
        var dispatcher = CreateDispatcher();
        await SendRequestAsync(dispatcher, 1, "initialize", """{"protocolVersion":"2025-11-25"}""");
        await SendNotificationAsync(dispatcher, "notifications/initialized");

        using var bind = await SendRequestAsync(
            dispatcher,
            2,
            "tools/call",
            """
            {
              "name": "document.bind_data",
              "arguments": {
                "documentId": "doc-1",
                "customXmlBindings": [
                  {
                    "dataSetId": "customers",
                    "storeItemId": "{customers}"
                  }
                ],
                "mailMergeBindings": [
                  {
                    "dataSetId": "customers",
                    "mainDocumentType": "catalog"
                  }
                ]
              }
            }
            """);

        var result = bind.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal(1, result.GetProperty("structuredContent").GetProperty("customXmlPartCount").GetInt32());
        Assert.Equal(2, result.GetProperty("structuredContent").GetProperty("mailMergeRecordCount").GetInt32());

        using var customXml = await SendRequestAsync(
            dispatcher,
            3,
            "resources/read",
            """
            {
              "uri": "proedit://documents/doc-1/custom-xml"
            }
            """);

        var customXmlText = customXml.RootElement
            .GetProperty("result")
            .GetProperty("contents")[0]
            .GetProperty("text")
            .GetString();

        Assert.Contains("Contoso", customXmlText, StringComparison.Ordinal);
        Assert.Contains("Fabrikam", customXmlText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_ReportExportToolReturnsEmbeddedCsvAndDefinitionResource()
    {
        var dispatcher = CreateDispatcher();
        await SendRequestAsync(dispatcher, 1, "initialize", """{"protocolVersion":"2025-11-25"}""");
        await SendNotificationAsync(dispatcher, "notifications/initialized");

        using var export = await SendRequestAsync(
            dispatcher,
            2,
            "tools/call",
            """
            {
              "name": "report.export",
              "arguments": {
                "reportId": "report-1",
                "format": "Csv"
              }
            }
            """);

        var exportResult = export.RootElement.GetProperty("result");
        Assert.False(exportResult.GetProperty("isError").GetBoolean());
        Assert.Equal("Csv", exportResult.GetProperty("structuredContent").GetProperty("format").GetString());
        var resourceText = exportResult.GetProperty("content")[1].GetProperty("resource").GetProperty("text").GetString();
        Assert.Contains("Region", resourceText, StringComparison.Ordinal);
        Assert.Contains("West", resourceText, StringComparison.Ordinal);

        using var definition = await SendRequestAsync(
            dispatcher,
            3,
            "resources/read",
            """
            {
              "uri": "proedit://reports/report-1/definition"
            }
            """);

        var definitionText = definition.RootElement
            .GetProperty("result")
            .GetProperty("contents")[0]
            .GetProperty("text")
            .GetString();

        Assert.Contains("\"id\": \"report-1\"", definitionText, StringComparison.Ordinal);
        Assert.Contains("\"dataSets\"", definitionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_RejectsDuplicateInitializeRequests()
    {
        var dispatcher = CreateDispatcher();

        using var first = await SendRequestAsync(dispatcher, 1, "initialize", """{"protocolVersion":"2025-11-25"}""");
        Assert.Equal("2025-11-25", first.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

        using var second = await SendRequestAsync(dispatcher, 2, "initialize", """{"protocolVersion":"2025-11-25"}""");
        Assert.Equal(-32600, second.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Dispatcher_ReportExport_RejectsInvalidFormatBeforeExecutingReport()
    {
        var reportStore = new InMemoryReportMcpStore();
        reportStore.Register(new ReportMcpEntry
        {
            Id = "report-invalid-format",
            Name = "Invalid Format Report",
            ReportDefinition = new ReportDefinition
            {
                Id = "report-invalid-format",
                Name = "Invalid Format Report",
                DataSources =
                {
                    new ReportDataSourceDefinition
                    {
                        Id = "missing-source",
                        ProviderId = ReportProviderIds.InMemory,
                        Options =
                        {
                            ["sourceKey"] = "missing"
                        }
                    }
                },
                DataSets =
                {
                    new ReportDataSetDefinition
                    {
                        Id = "rows",
                        DataSourceId = "missing-source"
                    }
                },
                Sections =
                {
                    new ReportSection
                    {
                        Id = "main",
                        Name = "Main",
                        BodyItems =
                        {
                            new TablixItem
                            {
                                Id = "rows",
                                DataSetId = "rows"
                            }
                        }
                    }
                }
            },
            Environment = new ReportExecutionEnvironment(
                ReportDataProviders.CreateDefaultRegistry(),
                new ReportHostDataRegistry())
        });

        var builder = new McpServerBuilder();
        builder.UseFeature(new ReportingMcpFeature(reportStore));
        var dispatcher = new McpProtocolDispatcher(builder.Build(), "test-session");

        await SendRequestAsync(dispatcher, 1, "initialize", """{"protocolVersion":"2025-11-25"}""");
        await SendNotificationAsync(dispatcher, "notifications/initialized");

        using var export = await SendRequestAsync(
            dispatcher,
            2,
            "tools/call",
            """
            {
              "name": "report.export",
              "arguments": {
                "reportId": "report-invalid-format",
                "format": "NotAFormat"
              }
            }
            """);

        var error = export.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("NotAFormat", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void McpServerBuilder_RegisterTool_RejectsDuplicateToolNames()
    {
        var builder = new McpServerBuilder();
        builder.RegisterTool(new TestToolHandler("dup.tool"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterTool(new TestToolHandler("dup.tool")));

        Assert.Contains("dup.tool", exception.Message, StringComparison.Ordinal);
    }

    private static McpProtocolDispatcher CreateDispatcher()
    {
        var documentStore = new InMemoryDocumentMcpStore();
        documentStore.Register(CreateDocumentEntry());

        var reportStore = new InMemoryReportMcpStore();
        reportStore.Register(CreateReportEntry());

        var builder = new McpServerBuilder(new McpServerOptions
        {
            ServerInfo = new McpImplementationInfo
            {
                Name = "proedit-tests",
                Version = "0.1.0",
                Title = "ProEdit MCP Test Host"
            },
            Instructions = "Expose ProEdit document and reporting workflows through MCP."
        });
        builder.UseFeature(new DocumentMcpFeature(documentStore));
        builder.UseFeature(new ReportingMcpFeature(reportStore));
        return new McpProtocolDispatcher(builder.Build(), "test-session");
    }

    private static DocumentMcpEntry CreateDocumentEntry()
    {
        return new DocumentMcpEntry
        {
            Id = "doc-1",
            Name = "Document One",
            Document = new Document(),
            DataContext = new DocumentMcpDataContext
            {
                ReportDefinition = CreateReportDefinition(),
                ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
                HostDataRegistry = CreateHostDataRegistry(),
                Culture = InvariantCulture,
                UiCulture = InvariantCulture,
                TimeZone = TimeZoneInfo.Utc
            }
        };
    }

    private static ReportMcpEntry CreateReportEntry()
    {
        return new ReportMcpEntry
        {
            Id = "report-1",
            Name = "Report One",
            ReportDefinition = CreateReportDefinition(),
            Environment = new ReportExecutionEnvironment(
                ReportDataProviders.CreateDefaultRegistry(),
                CreateHostDataRegistry())
        };
    }

    private static ReportDefinition CreateReportDefinition()
    {
        return new ReportDefinition
        {
            Id = "report-1",
            Name = "Report One",
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "sales-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "sales"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "customers",
                    DataSourceId = "sales-source"
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TablixItem
                        {
                            Id = "sales-table",
                            Name = "Sales Table",
                            DataSetId = "customers",
                            Bounds = new ReportItemBounds(0f, 0f, 280f, 120f),
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "region", Width = 140f },
                                new ReportTablixColumnDefinition { Id = "city", Width = 140f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "header",
                                    IsHeader = true,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = "Region" },
                                        new ReportTablixCellDefinition { Text = "City" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.City" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static ReportHostDataRegistry CreateHostDataRegistry()
    {
        var registry = new ReportHostDataRegistry();
        registry.RegisterInMemorySource(
            "sales",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "West",
                        ["City"] = "Contoso"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "East",
                        ["City"] = "Fabrikam"
                    }
                }));
        return registry;
    }

    private static async Task<JsonDocument> SendRequestAsync(
        McpProtocolDispatcher dispatcher,
        int id,
        string method,
        string paramsJson = "{}")
    {
        var response = await dispatcher.HandleAsync(
            $$"""
            {
              "jsonrpc": "2.0",
              "id": {{id}},
              "method": "{{method}}",
              "params": {{paramsJson}}
            }
            """);

        return JsonDocument.Parse(response);
    }

    private static async Task SendNotificationAsync(
        McpProtocolDispatcher dispatcher,
        string method,
        string paramsJson = "{}")
    {
        var response = await dispatcher.HandleAsync(
            $$"""
            {
              "jsonrpc": "2.0",
              "method": "{{method}}",
              "params": {{paramsJson}}
            }
            """);

        Assert.Equal(string.Empty, response);
    }

    private sealed class TestToolHandler : IMcpToolHandler
    {
        public TestToolHandler(string name)
        {
            Definition = new McpToolDefinition
            {
                Name = name,
                Description = "Test tool"
            };
        }

        public McpToolDefinition Definition { get; }

        public ValueTask<McpToolCallResult> InvokeAsync(
            JsonElement arguments,
            McpRequestContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new McpToolCallResult());
        }
    }
}
