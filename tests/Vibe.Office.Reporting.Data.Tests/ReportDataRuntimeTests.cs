using System.Globalization;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Expressions;
using Xunit;

namespace Vibe.Office.Reporting.Data.Tests;

public sealed class ReportDataRuntimeTests
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    [Fact]
    public async Task ExecuteAsync_InMemoryProvider_AppliesCalculatedFieldsFiltersAndSorts()
    {
        var reportDefinition = new ReportDefinition
        {
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
                    Id = "sales",
                    DataSourceId = "sales-source",
                    CalculatedFields =
                    {
                        new ReportCalculatedFieldDefinition
                        {
                            Name = "Net",
                            DataType = ReportParameterDataType.Decimal,
                            Expression = "Fields.Amount - Parameters.Discount"
                        }
                    },
                    Filters =
                    {
                        new ReportFilterDefinition
                        {
                            Expression = "Fields.Region",
                            Operator = ReportFilterOperator.Equal,
                            ValueExpression = "Parameters.Region"
                        }
                    },
                    Sorts =
                    {
                        new ReportSortDefinition
                        {
                            Expression = "Fields.Amount",
                            Direction = ReportSortDirection.Descending
                        }
                    },
                    ExpectedFields =
                    {
                        new ReportFieldDefinition { Name = "Region", DataType = ReportParameterDataType.String },
                        new ReportFieldDefinition { Name = "Amount", DataType = ReportParameterDataType.Decimal }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "sales",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "West",
                        ["Amount"] = 10m
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "East",
                        ["Amount"] = 4m
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "West",
                        ["Amount"] = 6m
                    }
                }));

        var executor = new ReportDataSetExecutor(new ReportExpressionCompiler());
        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = reportDefinition,
            DataSetId = "sales",
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        executionRequest.ParameterValues["Region"] = ReportParameterValue.FromScalar("West");
        executionRequest.ParameterValues["Discount"] = ReportParameterValue.FromScalar(1m);

        var result = await executor.ExecuteAsync(executionRequest);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Equal(2, result.DataSet!.Rows.Count);
        Assert.Equal(10m, Assert.IsType<decimal>(result.DataSet.Rows[0].Values["Amount"]));
        Assert.Equal(9m, Assert.IsType<decimal>(result.DataSet.Rows[0].Values["Net"]));
        Assert.Equal(6m, Assert.IsType<decimal>(result.DataSet.Rows[1].Values["Amount"]));
        Assert.Equal(5m, Assert.IsType<decimal>(result.DataSet.Rows[1].Values["Net"]));
    }

    [Fact]
    public async Task ExecuteAsync_JsonProvider_ReadsArrayPath()
    {
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "json-source",
                    ProviderId = ReportProviderIds.Json,
                    Options =
                    {
                        ["sourceKey"] = "orders"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "orders",
                    DataSourceId = "json-source",
                    Query = "$.items"
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterJsonSource(
            "orders",
            """
            {
              "items": [
                { "Id": 1, "Customer": "Contoso", "Total": 12.5 },
                { "Id": 2, "Customer": "Fabrikam", "Total": 8.25 }
              ]
            }
            """);

        var result = await ExecuteDataSetAsync(reportDefinition, "orders", hostData);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Equal(2, result.DataSet!.Rows.Count);
        Assert.Equal("Contoso", Assert.IsType<string>(result.DataSet.Rows[0].Values["Customer"]));
        Assert.Contains(result.DataSet.Fields, field => field.Name == "Total");
    }

    [Fact]
    public async Task ExecuteAsync_CsvProvider_ParsesHeadersAndTypedValues()
    {
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "csv-source",
                    ProviderId = ReportProviderIds.Csv,
                    Options =
                    {
                        ["sourceKey"] = "inventory",
                        ["hasHeaders"] = "true"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "inventory",
                    DataSourceId = "csv-source"
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterCsvSource(
            "inventory",
            """
            Name,Quantity,Price
            Paper,10,2.50
            Pens,5,1.25
            """);

        var result = await ExecuteDataSetAsync(reportDefinition, "inventory", hostData);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Equal(2, result.DataSet!.Rows.Count);
        Assert.Equal(10, Assert.IsType<int>(result.DataSet.Rows[0].Values["Quantity"]));
        Assert.Equal(2.50m, Assert.IsType<decimal>(result.DataSet.Rows[0].Values["Price"]));
        Assert.Contains(
            result.DataSet.Fields,
            field => field.Name == "Quantity" && field.DataType == ReportParameterDataType.Integer);
        Assert.Contains(
            result.DataSet.Fields,
            field => field.Name == "Price" && field.DataType == ReportParameterDataType.Decimal);
    }

    [Fact]
    public async Task ExecuteAsync_EnterDataProvider_UsesExpectedFieldTypesInsteadOfBlindScalarInference()
    {
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "enterdata-source",
                    ProviderId = ReportProviderIds.EnterData
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "invoice",
                    DataSourceId = "enterdata-source",
                    Query =
                        """
                        <Query>
                          <XmlData>
                            <Data>
                              <Row>
                                <InvoiceId>000007-1</InvoiceId>
                                <InvoiceDate>30 November 2019</InvoiceDate>
                                <HeaderNotes></HeaderNotes>
                                <Amount>4329.00</Amount>
                              </Row>
                            </Data>
                          </XmlData>
                        </Query>
                        """,
                    ExpectedFields =
                    {
                        new ReportFieldDefinition { Name = "InvoiceId", DataType = ReportParameterDataType.String },
                        new ReportFieldDefinition { Name = "InvoiceDate", DataType = ReportParameterDataType.String },
                        new ReportFieldDefinition { Name = "HeaderNotes", DataType = ReportParameterDataType.String },
                        new ReportFieldDefinition { Name = "Amount", DataType = ReportParameterDataType.Number }
                    }
                }
            }
        };

        var result = await ExecuteDataSetAsync(reportDefinition, "invoice", new ReportHostDataRegistry());

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        var row = Assert.Single(result.DataSet!.Rows);
        Assert.Equal("000007-1", Assert.IsType<string>(row.Values["InvoiceId"]));
        Assert.Equal("30 November 2019", Assert.IsType<string>(row.Values["InvoiceDate"]));
        Assert.Equal(string.Empty, Assert.IsType<string>(row.Values["HeaderNotes"]));
        Assert.Equal(4329d, Assert.IsType<double>(row.Values["Amount"]));
        Assert.Contains(
            result.DataSet.Fields,
            field => field.Name == "InvoiceId" && field.DataType == ReportParameterDataType.String);
        Assert.Contains(
            result.DataSet.Fields,
            field => field.Name == "Amount" && field.DataType == ReportParameterDataType.Number);
    }

    [Fact]
    public async Task ExecuteAsync_SqlProvider_UsesResolvedDataSetParameters()
    {
        var connector = new StubSqlConnector();
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "sql-source",
                    ProviderId = ReportProviderIds.Sql,
                    ConnectionName = "main-db"
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales",
                    DataSourceId = "sql-source",
                    Query = "select * from sales where region = @Region",
                    Parameters =
                    {
                        new ReportDataSetParameterDefinition
                        {
                            Name = "Region",
                            ValueExpression = "Parameters.Region"
                        }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterSqlConnector("main-db", connector);

        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = reportDefinition,
            DataSetId = "sales",
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        executionRequest.ParameterValues["Region"] = ReportParameterValue.FromScalar("West");

        var executor = new ReportDataSetExecutor(new ReportExpressionCompiler());
        var result = await executor.ExecuteAsync(executionRequest);

        Assert.False(result.HasErrors);
        Assert.NotNull(connector.LastRequest);
        Assert.Equal("select * from sales where region = @Region", connector.LastRequest!.Query);
        Assert.Equal("West", Assert.IsType<string>(connector.LastRequest.Parameters["Region"]));
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
    }

    [Fact]
    public async Task ExecuteAsync_SqlProvider_PassesResolvedConnectorKeyToHostConnector()
    {
        var connector = new StubSqlConnector();
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "sql-source",
                    ProviderId = ReportProviderIds.Sql,
                    Options =
                    {
                        ["connectorKey"] = "shared-sql"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales",
                    DataSourceId = "sql-source",
                    Query = "select * from sales"
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterSqlConnector("shared-sql", connector);

        var result = await ExecuteDataSetAsync(reportDefinition, "sales", hostData);

        Assert.False(result.HasErrors);
        Assert.NotNull(connector.LastRequest);
        Assert.Equal("shared-sql", connector.LastRequest!.ConnectionName);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesDefaultsAndCascadingAvailableValues()
    {
        var reportDefinition = new ReportDefinition
        {
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'West'"
                },
                new ReportParameterDefinition
                {
                    Id = "SalesPerson",
                    DisplayName = "Sales Person",
                    DataType = ReportParameterDataType.String,
                    AvailableValuesDataSetId = "sales-people",
                    ValueField = "Id",
                    LabelField = "Name",
                    Dependencies = { "Region" }
                }
            },
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "people-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "people"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales-people",
                    DataSourceId = "people-source",
                    Filters =
                    {
                        new ReportFilterDefinition
                        {
                            Expression = "Fields.Region",
                            Operator = ReportFilterOperator.Equal,
                            ValueExpression = "Parameters.Region"
                        }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "people",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Id"] = "A1",
                        ["Name"] = "Alice",
                        ["Region"] = "West"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Id"] = "B1",
                        ["Name"] = "Bob",
                        ["Region"] = "West"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Id"] = "C1",
                        ["Name"] = "Carol",
                        ["Region"] = "East"
                    }
                }));

        var resolver = new ReportParameterResolver(new ReportExpressionCompiler());
        var resolutionRequest = new ReportParameterResolutionRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        var result = await resolver.ResolveAsync(resolutionRequest);

        Assert.False(result.HasErrors);
        Assert.Equal("West", Assert.IsType<string>(result.ResolvedValues["Region"].GetScalarValue()));
        Assert.True(result.AvailableValues.TryGetValue("SalesPerson", out var availableValues));
        Assert.NotNull(availableValues);
        Assert.Equal(2, availableValues!.Count);
        Assert.Contains(availableValues, value => Equals(value.Value, "A1") && value.Label == "Alice");
        Assert.Contains(availableValues, value => Equals(value.Value, "B1") && value.Label == "Bob");
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingRequiredParameter()
    {
        var reportDefinition = new ReportDefinition
        {
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    DataType = ReportParameterDataType.String,
                    AllowNull = false,
                    Visibility = ReportParameterVisibility.Internal
                }
            }
        };

        var resolver = new ReportParameterResolver(new ReportExpressionCompiler());
        var result = await resolver.ResolveAsync(new ReportParameterResolutionRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = new ReportHostDataRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        });

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ReportDiagnosticCodes.ParameterResolutionFailed
                && diagnostic.Message.Contains("requires a value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_ReportsMultiValueInputForScalarParameter()
    {
        var reportDefinition = new ReportDefinition
        {
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    DataType = ReportParameterDataType.String,
                    AllowNull = false
                }
            }
        };

        var request = new ReportParameterResolutionRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = new ReportHostDataRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.SuppliedValues["Region"] = new ReportParameterValue
        {
            Values = { "West", "East" }
        };

        var resolver = new ReportParameterResolver(new ReportExpressionCompiler());
        var result = await resolver.ResolveAsync(request);

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ReportDiagnosticCodes.ValueCoercionFailed
                && diagnostic.Message.Contains("does not allow multiple values", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_ClearsInvalidVisibleDefaultAgainstAvailableValues()
    {
        var reportDefinition = new ReportDefinition
        {
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Company",
                    DisplayName = "Company",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'Microsoft'",
                    AvailableValuesDataSetId = "companies",
                    ValueField = "Company",
                    LabelField = "Company",
                    Visibility = ReportParameterVisibility.Visible
                }
            },
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "companies-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "companies"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "companies",
                    DataSourceId = "companies-source"
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "companies",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Company"] = "Contoso Pharmaceuticals"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Company"] = "Contoso Suites"
                    }
                }));

        var resolver = new ReportParameterResolver(new ReportExpressionCompiler());
        var result = await resolver.ResolveAsync(new ReportParameterResolutionRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        });

        Assert.False(result.HasErrors);
        Assert.True(result.AvailableValues.TryGetValue("Company", out var availableValues));
        Assert.NotNull(availableValues);
        Assert.Equal(2, availableValues!.Count);
        Assert.True(result.ResolvedValues["Company"].IsNull);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFractionalIntegerCoercionErrors()
    {
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "numbers-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "numbers"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "numbers",
                    DataSourceId = "numbers-source",
                    ExpectedFields =
                    {
                        new ReportFieldDefinition
                        {
                            Name = "Quantity",
                            DataType = ReportParameterDataType.Integer
                        }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "numbers",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Quantity"] = 1.5m
                    }
                }));

        var result = await ExecuteDataSetAsync(reportDefinition, "numbers", hostData);

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ReportDiagnosticCodes.ValueCoercionFailed
                && diagnostic.Message.Contains("whole number", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ContainsFilterUsesExecutionCulture()
    {
        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "culture-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "culture"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "culture",
                    DataSourceId = "culture-source",
                    Filters =
                    {
                        new ReportFilterDefinition
                        {
                            Expression = "Fields.Name",
                            Operator = ReportFilterOperator.Contains,
                            ValueExpression = "'ı'"
                        }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "culture",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "ISPARTA"
                    }
                }));

        using var _ = new CurrentCultureScope(new CultureInfo("en-US"));
        var result = await ExecuteDataSetAsync(reportDefinition, "culture", hostData, new CultureInfo("tr-TR"));

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
    }

    private static async Task<ReportDataSetExecutionResult> ExecuteDataSetAsync(
        ReportDefinition reportDefinition,
        string dataSetId,
        ReportHostDataRegistry hostDataRegistry,
        CultureInfo? culture = null)
    {
        var executor = new ReportDataSetExecutor(new ReportExpressionCompiler());
        var executionRequest = new ReportDataSetExecutionRequest
        {
            ReportDefinition = reportDefinition,
            DataSetId = dataSetId,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostDataRegistry,
            Culture = culture ?? InvariantCulture,
            UiCulture = culture ?? InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        return await executor.ExecuteAsync(executionRequest);
    }

    private sealed class StubSqlConnector : IReportSqlConnector
    {
        public ReportSqlQueryRequest? LastRequest { get; private set; }

        public ValueTask<ReportDataTable> ExecuteAsync(
            ReportSqlQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            request.Parameters.TryGetValue("Region", out var region);

            var table = new ReportDataTable
            {
                DataSetId = "sales"
            };
            table.Fields.Add(new ReportFieldDefinition
            {
                Name = "Region",
                DataType = ReportParameterDataType.String
            });
            table.Rows.Add(new ReportDataRecord(new Dictionary<string, object?>
            {
                ["Region"] = region ?? request.ConnectionName ?? "All"
            }));

            return ValueTask.FromResult(table);
        }
    }

    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CurrentCultureScope(CultureInfo culture)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
