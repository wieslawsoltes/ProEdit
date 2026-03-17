using System.Globalization;
using Vibe.Office.Documents.Data;
using Vibe.Office.Reporting;
using Vibe.Office.Reporting.Data;
using Xunit;

namespace Vibe.Office.Documents.Data.Tests;

public sealed class DocumentDataBinderTests
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    [Fact]
    public async Task BindAsync_PopulatesCustomXmlPartsForContentControlBindings()
    {
        var reportDefinition = CreateCustomerReportDefinition();
        var hostData = CreateCustomerHostData();
        var document = new Document();
        var binder = new DocumentDataBinder();
        var request = new DocumentDataBindingRequest
        {
            Document = document,
            ReportDefinition = reportDefinition,
            HostDataRegistry = hostData,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.CustomXmlBindings.Add(new DocumentCustomXmlBindingDefinition
        {
            DataSetId = "customers",
            StoreItemId = "{customer-store}",
            RootElementName = "customers",
            RowElementName = "customer"
        });

        var result = await binder.BindAsync(request);

        Assert.False(result.HasErrors);
        Assert.True(document.CustomXmlParts.TryGetValue("customer-store", out _));

        var properties = new ContentControlProperties
        {
            DataBinding = new ContentControlDataBinding
            {
                StoreItemId = "{customer-store}",
                XPath = "string(/customers/customer[1]/Name)"
            }
        };

        Assert.True(ContentControlValueResolver.TryResolveContentControlBinding(properties.DataBinding, document, out var value));
        Assert.Equal("Contoso", value);
    }

    [Fact]
    public async Task BindAsync_PopulatesMailMergeDataFromSharedConnectorRuntime()
    {
        var reportDefinition = CreateCustomerReportDefinition();
        var hostData = CreateCustomerHostData();
        var document = new Document();
        var binder = new DocumentDataBinder();
        var request = new DocumentDataBindingRequest
        {
            Document = document,
            ReportDefinition = reportDefinition,
            HostDataRegistry = hostData,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.MailMergeBindings.Add(new DocumentMailMergeBindingDefinition
        {
            DataSetId = "customers",
            MainDocumentType = "catalog"
        });

        var result = await binder.BindAsync(request);

        Assert.False(result.HasErrors);
        var mergeData = Assert.IsType<MailMergeData>(document.MailMergeData);
        Assert.Equal("catalog", mergeData.MainDocumentType);
        Assert.Contains("Name", mergeData.FieldNames);
        Assert.Contains("City", mergeData.FieldNames);
        Assert.Equal(2, mergeData.Records.Count);
        Assert.Equal("Contoso", mergeData.Records[0].Fields["Name"]);
        Assert.Equal("Warsaw", mergeData.Records[0].Fields["City"]);
    }

    [Fact]
    public async Task BindAsync_DoesNotOverwriteExistingMailMergeDataWhenBindingFails()
    {
        var document = new Document
        {
            MailMergeData = new MailMergeData
            {
                MainDocumentType = "labels"
            }
        };
        document.MailMergeData.FieldNames.Add("Existing");
        document.MailMergeData.Records.Add(new MailMergeRecord());
        document.MailMergeData.Records[0].Fields["Existing"] = "Preserve me";

        var binder = new DocumentDataBinder();
        var request = new DocumentDataBindingRequest
        {
            Document = document,
            ReportDefinition = new ReportDefinition(),
            HostDataRegistry = new ReportHostDataRegistry(),
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.MailMergeBindings.Add(new DocumentMailMergeBindingDefinition
        {
            DataSetId = "missing"
        });

        var result = await binder.BindAsync(request);

        Assert.True(result.HasErrors);
        var mergeData = Assert.IsType<MailMergeData>(document.MailMergeData);
        Assert.Equal("labels", mergeData.MainDocumentType);
        Assert.Single(mergeData.FieldNames);
        Assert.Single(mergeData.Records);
        Assert.Equal("Preserve me", mergeData.Records[0].Fields["Existing"]);
    }

    [Fact]
    public async Task BindAsync_ReportsEmptyCustomXmlStoreItemIdWithoutMutatingDocument()
    {
        var reportDefinition = CreateCustomerReportDefinition();
        var hostData = CreateCustomerHostData();
        var document = new Document();
        var binder = new DocumentDataBinder();
        var request = new DocumentDataBindingRequest
        {
            Document = document,
            ReportDefinition = reportDefinition,
            HostDataRegistry = hostData,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            Culture = InvariantCulture,
            UiCulture = InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.CustomXmlBindings.Add(new DocumentCustomXmlBindingDefinition
        {
            DataSetId = "customers",
            StoreItemId = " "
        });

        var result = await binder.BindAsync(request);

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ReportDiagnosticCodes.InvalidTemplate
                && diagnostic.Path == "customXmlBindings[0].storeItemId");
        Assert.Empty(document.CustomXmlParts);
    }

    private static ReportDefinition CreateCustomerReportDefinition()
    {
        return new ReportDefinition
        {
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "customer-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "customers"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "customers",
                    DataSourceId = "customer-source"
                }
            }
        };
    }

    private static ReportHostDataRegistry CreateCustomerHostData()
    {
        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "customers",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "Contoso",
                        ["City"] = "Warsaw"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "Fabrikam",
                        ["City"] = "Berlin"
                    }
                }));
        return hostData;
    }
}
