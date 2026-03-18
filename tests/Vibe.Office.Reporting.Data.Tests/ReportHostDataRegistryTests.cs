using System.Net.Http;
using Vibe.Office.Reporting.Data;
using Xunit;

namespace Vibe.Office.Reporting.Data.Tests;

public sealed class ReportHostDataRegistryTests
{
    [Fact]
    public void ListApis_ReturnRegisteredSourcesInStableOrder()
    {
        var registry = new ReportHostDataRegistry();
        registry.RegisterInMemorySource("sales", new ReportDictionaryDataSource([]));
        registry.RegisterInMemorySource("customers", new ReportDictionaryDataSource([]));
        registry.RegisterJsonSource("targets", "{}");
        registry.RegisterCsvSource("inventory", "Name\nPaper");
        registry.RegisterSqlConnector("warehouse", new StubSqlConnector());
        registry.RegisterConnection(new ReportConnectionDefinition
        {
            Name = "sql-main",
            ProviderId = ReportProviderIds.SqlServer,
            DisplayName = "Main SQL"
        });
        registry.RegisterHttpClient("odata", new HttpClient());

        Assert.Equal(["customers", "sales"], registry.ListInMemorySourceKeys());
        Assert.Equal(["targets"], registry.ListJsonSourceKeys());
        Assert.Equal(["inventory"], registry.ListCsvSourceKeys());
        Assert.Equal(["warehouse"], registry.ListSqlConnectorKeys());
        Assert.Equal(["odata"], registry.ListHttpClientKeys());

        var connection = Assert.Single(registry.ListConnections());
        Assert.Equal("sql-main", connection.Name);
        Assert.Equal("Main SQL", connection.DisplayName);
    }

    private sealed class StubSqlConnector : IReportSqlConnector
    {
        public ValueTask<ReportDataTable> ExecuteAsync(ReportSqlQueryRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ReportDataTable());
        }
    }
}
