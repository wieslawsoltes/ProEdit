using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Expressions;
using Xunit;

namespace Vibe.Office.Reporting.Data.Tests;

public sealed class ReportConnectorRuntimeTests
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    [Fact]
    public void ConnectorCatalog_CreateDefault_IncludesMainstreamDatabaseAndApiConnectors()
    {
        var catalog = ReportDataConnectorCatalog.CreateDefault();
        var connectors = catalog.ListConnectors();

        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.SqlServer);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.PostgreSql);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.MySql);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.Oracle);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.Sqlite);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.Snowflake);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.RestJson);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.OData);
        Assert.Contains(connectors, static connector => connector.ProviderId == ReportProviderIds.GraphQl);

        Assert.True(catalog.TryGetConnector(ReportProviderIds.SqlServer, out var sqlServer));
        Assert.Equal("Microsoft.Data.SqlClient", sqlServer.ProviderInvariantName);
    }

    [Fact]
    public async Task ExecuteAsync_AdoNetProvider_UsesRegisteredConnectionAndFactory()
    {
        var providerFactory = new StubDbProviderFactory(CreateSalesDataTable());
        var hostData = new ReportHostDataRegistry();
        hostData.RegisterConnection(new ReportConnectionDefinition
        {
            Name = "sales-db",
            ProviderId = ReportProviderIds.SqlServer,
            ConnectionString = "Server=fake;"
        });
        hostData.RegisterDbProviderFactory("Microsoft.Data.SqlClient", providerFactory);

        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "sales-source",
                    ProviderId = ReportProviderIds.SqlServer,
                    ConnectionName = "sales-db"
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales",
                    DataSourceId = "sales-source",
                    Query = "select Region, Total from Sales where Region = @Region",
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

        var result = await ExecuteDataSetAsync(reportDefinition, "sales", hostData, parameters =>
        {
            parameters["Region"] = ReportParameterValue.FromScalar("West");
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
        Assert.Equal("West", Assert.IsType<string>(result.DataSet.Rows[0].Values["Region"]));

        var command = Assert.IsType<StubDbCommand>(providerFactory.LastCommand);
        Assert.Equal("select Region, Total from Sales where Region = @Region", command.CommandText);
        Assert.Single(command.RecordedParameters);
        Assert.Equal("@Region", command.RecordedParameters[0].ParameterName);
        Assert.Equal("West", command.RecordedParameters[0].Value);
    }

    [Fact]
    public async Task ExecuteAsync_RestJsonProvider_UsesRegisteredHttpClientAndDataSetParameters()
    {
        var handler = new StubHttpMessageHandler(_ =>
            CreateJsonResponse(
                """
                {
                  "items": [
                    { "Customer": "Contoso", "Total": 12.5 }
                  ]
                }
                """));
        var hostData = new ReportHostDataRegistry();
        hostData.RegisterConnection(new ReportConnectionDefinition
        {
            Name = "crm-api",
            ProviderId = ReportProviderIds.RestJson,
            BaseAddress = "https://api.example.com/v1/"
        });
        hostData.RegisterHttpClient("crm-api", new HttpClient(handler));

        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "crm-source",
                    ProviderId = ReportProviderIds.RestJson,
                    ConnectionName = "crm-api",
                    Options =
                    {
                        ["method"] = "GET",
                        ["jsonPath"] = "$.items",
                        ["header:X-Tenant"] = "blue"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "customers",
                    DataSourceId = "crm-source",
                    Query = "customers",
                    Parameters =
                    {
                        new ReportDataSetParameterDefinition
                        {
                            Name = "region",
                            ValueExpression = "Parameters.Region"
                        }
                    }
                }
            }
        };

        var result = await ExecuteDataSetAsync(reportDefinition, "customers", hostData, parameters =>
        {
            parameters["Region"] = ReportParameterValue.FromScalar("west");
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
        Assert.Equal("Contoso", Assert.IsType<string>(result.DataSet.Rows[0].Values["Customer"]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.example.com/v1/customers?region=west", request.RequestUri!.ToString());
        Assert.True(request.Headers.TryGetValues("X-Tenant", out var tenantHeader));
        Assert.Contains("blue", tenantHeader);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task ExecuteAsync_ODataProvider_UsesDefaultValueEnvelope()
    {
        var handler = new StubHttpMessageHandler(_ =>
            CreateJsonResponse(
                """
                {
                  "value": [
                    { "Id": 1, "Name": "Northwind" }
                  ]
                }
                """));
        var hostData = new ReportHostDataRegistry();
        hostData.RegisterConnection(new ReportConnectionDefinition
        {
            Name = "odata",
            ProviderId = ReportProviderIds.OData,
            BaseAddress = "https://services.example.com/"
        });
        hostData.RegisterHttpClient("odata", new HttpClient(handler));

        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "odata-source",
                    ProviderId = ReportProviderIds.OData,
                    ConnectionName = "odata"
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "companies",
                    DataSourceId = "odata-source",
                    Query = "Companies"
                }
            }
        };

        var result = await ExecuteDataSetAsync(reportDefinition, "companies", hostData);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
        Assert.Equal("Northwind", Assert.IsType<string>(result.DataSet.Rows[0].Values["Name"]));
    }

    [Fact]
    public async Task ExecuteAsync_GraphQlProvider_UsesInlineConnectionAndDataEnvelope()
    {
        var handler = new StubHttpMessageHandler(_ =>
            CreateJsonResponse(
                """
                {
                  "data": {
                    "sales": [
                      { "name": "Contoso", "amount": 42.0 }
                    ]
                  }
                }
                """));
        var hostData = new ReportHostDataRegistry();
        hostData.RegisterHttpClient("graphql-source", new HttpClient(handler));

        var reportDefinition = new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "graphql-source",
                    ProviderId = ReportProviderIds.GraphQl,
                    Options =
                    {
                        ["baseAddress"] = "https://api.example.com/graphql",
                        ["header:Authorization"] = "Bearer token"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales",
                    DataSourceId = "graphql-source",
                    Query = "query ($region: String!) { sales(region: $region) { name amount } }",
                    Parameters =
                    {
                        new ReportDataSetParameterDefinition
                        {
                            Name = "region",
                            ValueExpression = "Parameters.Region"
                        }
                    }
                }
            }
        };

        var result = await ExecuteDataSetAsync(reportDefinition, "sales", hostData, parameters =>
        {
            parameters["Region"] = ReportParameterValue.FromScalar("west");
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.DataSet);
        Assert.Single(result.DataSet!.Rows);
        Assert.Equal("Contoso", Assert.IsType<string>(result.DataSet.Rows[0].Values["name"]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.example.com/graphql", request.RequestUri!.ToString());
        Assert.True(request.Headers.TryGetValues("Authorization", out var authorizationHeader));
        Assert.Contains("Bearer token", authorizationHeader);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"query\":\"query ($region: String!) { sales(region: $region) { name amount } }\"", body, StringComparison.Ordinal);
        Assert.Contains("\"region\":\"west\"", body, StringComparison.Ordinal);
    }

    private static async Task<ReportDataSetExecutionResult> ExecuteDataSetAsync(
        ReportDefinition reportDefinition,
        string dataSetId,
        ReportHostDataRegistry hostDataRegistry,
        Action<Dictionary<string, ReportParameterValue>>? configureParameters = null)
    {
        var executor = new ReportDataSetExecutor(new ReportExpressionCompiler());
        var request = new ReportDataSetExecutionRequest
        {
            ReportDefinition = reportDefinition,
            DataSetId = dataSetId,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostDataRegistry,
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };

        configureParameters?.Invoke(request.ParameterValues);
        return await executor.ExecuteAsync(request);
    }

    private static DataTable CreateSalesDataTable()
    {
        var table = new DataTable("Sales");
        table.Columns.Add("Region", typeof(string));
        table.Columns.Add("Total", typeof(decimal));
        table.Rows.Add("West", 12.5m);
        return table;
    }

    private static HttpResponseMessage CreateJsonResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequestWithoutBody(request));
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _responseFactory(request);
        }

        private static HttpRequestMessage CloneRequestWithoutBody(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class StubDbProviderFactory : DbProviderFactory
    {
        private readonly DataTable _resultTable;

        public StubDbProviderFactory(DataTable resultTable)
        {
            _resultTable = resultTable ?? throw new ArgumentNullException(nameof(resultTable));
        }

        public DbCommand? LastCommand { get; private set; }

        public override DbConnection CreateConnection()
        {
            return new StubDbConnection(this, _resultTable);
        }

        public override DbParameter CreateParameter()
        {
            return new StubDbParameter();
        }

        private sealed class StubDbConnection : DbConnection
        {
            private readonly StubDbProviderFactory _factory;
            private readonly DataTable _resultTable;
            private string _connectionString = string.Empty;

            public StubDbConnection(StubDbProviderFactory factory, DataTable resultTable)
            {
                _factory = factory;
                _resultTable = resultTable;
            }

            [AllowNull]
            public override string ConnectionString
            {
                get => _connectionString;
                set => _connectionString = value ?? string.Empty;
            }

            public override string Database => "Stub";

            public override string DataSource => "Stub";

            public override string ServerVersion => "1.0";

            public override ConnectionState State => ConnectionState.Open;

            public override void ChangeDatabase(string databaseName)
            {
            }

            public override void Close()
            {
            }

            public override void Open()
            {
            }

            public override Task OpenAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            {
                throw new NotSupportedException();
            }

            protected override DbCommand CreateDbCommand()
            {
                var command = new StubDbCommand(_resultTable);
                _factory.LastCommand = command;
                return command;
            }
        }
    }

    private sealed class StubDbCommand : DbCommand
    {
        private readonly DataTable _resultTable;
        private readonly StubDbParameterCollection _parameters = new();

        public StubDbCommand(DataTable resultTable)
        {
            _resultTable = resultTable ?? throw new ArgumentNullException(nameof(resultTable));
        }

        public List<StubDbParameter> RecordedParameters => _parameters.Parameters;

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new StubDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return _resultTable.CreateDataReader();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<DbDataReader>(_resultTable.CreateDataReader());
        }
    }

    private sealed class StubDbParameterCollection : DbParameterCollection
    {
        private readonly List<StubDbParameter> _parameters = new();

        public List<StubDbParameter> Parameters => _parameters;

        public override int Count => _parameters.Count;

        public override object SyncRoot => this;

        public override int Add(object value)
        {
            var parameter = Assert.IsType<StubDbParameter>(value);
            _parameters.Add(parameter);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear()
        {
            _parameters.Clear();
        }

        public override bool Contains(object value)
        {
            return _parameters.Contains(Assert.IsType<StubDbParameter>(value));
        }

        public override bool Contains(string value)
        {
            return _parameters.Any(parameter => string.Equals(parameter.ParameterName, value, StringComparison.OrdinalIgnoreCase));
        }

        public override void CopyTo(Array array, int index)
        {
            _parameters.ToArray().CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator()
        {
            return _parameters.GetEnumerator();
        }

        public override int IndexOf(object value)
        {
            return _parameters.IndexOf(Assert.IsType<StubDbParameter>(value));
        }

        public override int IndexOf(string parameterName)
        {
            return _parameters.FindIndex(parameter => string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
        }

        public override void Insert(int index, object value)
        {
            _parameters.Insert(index, Assert.IsType<StubDbParameter>(value));
        }

        public override void Remove(object value)
        {
            _parameters.Remove(Assert.IsType<StubDbParameter>(value));
        }

        public override void RemoveAt(int index)
        {
            _parameters.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index)
        {
            return _parameters[index];
        }

        protected override DbParameter GetParameter(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                throw new IndexOutOfRangeException(parameterName);
            }

            return _parameters[index];
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            _parameters[index] = (StubDbParameter)value;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                _parameters.Add((StubDbParameter)value);
                return;
            }

            _parameters[index] = (StubDbParameter)value;
        }
    }

    private sealed class StubDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
