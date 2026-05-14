using System.Text;
using ProEdit.Reporting.Serialization;
using Xunit;

namespace ProEdit.Reporting.Serialization.Tests;

public sealed class ReportTemplateSerializerTests
{
    private readonly ReportTemplateSerializer _serializer = new();

    [Fact]
    public void WriteAndRead_RoundTripsCurrentSchema()
    {
        var report = CreateSampleReport();

        var writeResult = _serializer.Write(report);

        Assert.False(writeResult.HasErrors);
        Assert.Contains("\"schemaVersion\": 1", writeResult.Text, StringComparison.Ordinal);

        var readResult = _serializer.Read(writeResult.Text);

        Assert.False(readResult.HasErrors);
        Assert.NotNull(readResult.ReportDefinition);
        Assert.DoesNotContain(readResult.Diagnostics, static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);

        var roundTripped = readResult.ReportDefinition!;
        Assert.Equal(ReportDefinition.CurrentSchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal("sales-summary", roundTripped.Id);
        Assert.Equal("Sales Summary", roundTripped.Name);
        Assert.Single(roundTripped.Sections);
        Assert.Single(roundTripped.Parameters);
        Assert.Single(roundTripped.DataSources);
        Assert.Single(roundTripped.DataSets);
        Assert.Single(roundTripped.Styles);
        Assert.Single(roundTripped.SharedTemplates);

        var section = roundTripped.Sections[0];
        Assert.Equal(3, section.BodyItems.Count);
        Assert.IsType<TextItem>(section.BodyItems[0]);
        Assert.IsType<TablixItem>(section.BodyItems[1]);
        Assert.IsType<DocumentTemplateItem>(section.BodyItems[2]);
    }

    [Fact]
    public void Read_UpgradesLegacySchemaVersionZero()
    {
        const string legacyJson =
            """
            {
              "schemaVersion": 0,
              "id": "legacy-report",
              "title": "Legacy Report",
              "reportParameters": [
                {
                  "id": "region",
                  "displayName": "Region",
                  "dataType": "String"
                }
              ],
              "sections": [
                {
                  "id": "main",
                  "body": [
                    {
                      "kind": "text",
                      "id": "title",
                      "name": "Title",
                      "staticText": "Legacy"
                    }
                  ]
                }
              ]
            }
            """;

        var result = _serializer.Read(legacyJson);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.ReportDefinition);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.SchemaUpgraded);
        Assert.Equal(ReportDefinition.CurrentSchemaVersion, result.ReportDefinition!.SchemaVersion);
        Assert.Equal("Legacy Report", result.ReportDefinition.Name);
        Assert.Single(result.ReportDefinition.Parameters);
        Assert.Single(result.ReportDefinition.Sections[0].BodyItems);
        Assert.IsType<TextItem>(result.ReportDefinition.Sections[0].BodyItems[0]);
    }

    [Fact]
    public void Read_ReportsUnknownPropertiesAsWarnings()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "id": "report",
              "name": "Report",
              "unexpectedRoot": true,
              "sections": [
                {
                  "id": "main",
                  "bodyItems": [
                    {
                      "itemType": "TextItem",
                      "id": "title",
                      "name": "Title",
                      "staticText": "Hello",
                      "bogus": 42
                    }
                  ]
                }
              ]
            }
            """;

        var result = _serializer.Read(json);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.ReportDefinition);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnknownProperty && diagnostic.Path == "$.unexpectedRoot");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnknownProperty && diagnostic.Path == "$.sections[0].bodyItems[0].bogus");
    }

    [Fact]
    public void Read_PreservesCaseInsensitiveTemplateDictionaries()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "id": "report",
              "name": "Report",
              "metadata": {
                "Region": "EMEA"
              },
              "sections": [
                {
                  "id": "main",
                  "bodyItems": [
                    {
                      "itemType": "DocumentTemplateItem",
                      "id": "intro",
                      "name": "Intro",
                      "bindings": {
                        "CustomerName": "=Fields!CustomerName.Value"
                      }
                    }
                  ]
                }
              ],
              "dataSources": [
                {
                  "id": "source",
                  "providerId": "json",
                  "options": {
                    "Path": "data.json"
                  }
                }
              ]
            }
            """;

        var result = _serializer.Read(json);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.ReportDefinition);
        Assert.True(result.ReportDefinition!.Metadata.ContainsKey("region"));

        var item = Assert.IsType<DocumentTemplateItem>(Assert.Single(result.ReportDefinition.Sections[0].BodyItems));
        Assert.True(item.Bindings.ContainsKey("customername"));

        var dataSource = Assert.Single(result.ReportDefinition.DataSources);
        Assert.True(dataSource.Options.ContainsKey("path"));
    }

    [Fact]
    public void Read_ReportsItemPropertiesThatBelongToWrongItemType()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "id": "report",
              "name": "Report",
              "sections": [
                {
                  "id": "main",
                  "bodyItems": [
                    {
                      "itemType": "TextItem",
                      "id": "title",
                      "name": "Title",
                      "staticText": "Hello",
                      "columns": []
                    }
                  ]
                }
              ]
            }
            """;

        var result = _serializer.Read(json);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnknownProperty && diagnostic.Path == "$.sections[0].bodyItems[0].columns");
    }

    [Fact]
    public void Read_ReportsUnknownItemTypeAsError()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "id": "report",
              "name": "Report",
              "sections": [
                {
                  "id": "main",
                  "bodyItems": [
                    {
                      "itemType": "VideoItem",
                      "id": "video",
                      "name": "Video"
                    }
                  ]
                }
              ]
            }
            """;

        var result = _serializer.Read(json);

        Assert.True(result.HasErrors);
        Assert.Null(result.ReportDefinition);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnknownItemType && diagnostic.Path == "$.sections[0].bodyItems[0].itemType");
    }

    [Fact]
    public async Task WriteAsync_WritesUtf8Payload()
    {
        var report = CreateSampleReport();
        await using var stream = new MemoryStream();

        var result = await _serializer.WriteAsync(report, stream);

        Assert.False(result.HasErrors);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.Equal(result.Text, json);
    }

    [Fact]
    public void Read_RejectsTemplatesWithoutSections()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "id": "report",
              "name": "Report",
              "sections": []
            }
            """;

        var result = _serializer.Read(json);

        Assert.True(result.HasErrors);
        Assert.Null(result.ReportDefinition);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.InvalidTemplate && diagnostic.Path == "$.sections");
    }

    private static ReportDefinition CreateSampleReport()
    {
        var report = new ReportDefinition
        {
            Id = "sales-summary",
            Name = "Sales Summary",
            Description = "Sample report for serializer tests."
        };

        report.Metadata["domain"] = "sales";
        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "region",
            DisplayName = "Region",
            DataType = ReportParameterDataType.String,
            Prompt = "Select region"
        });

        report.DataSources.Add(new ReportDataSourceDefinition
        {
            Id = "salesDb",
            ProviderId = "sql",
            ConnectionName = "Sales"
        });

        var dataSet = new ReportDataSetDefinition
        {
            Id = "sales",
            DataSourceId = "salesDb",
            Query = "SalesSummary"
        };
        dataSet.ExpectedFields.Add(new ReportFieldDefinition
        {
            Name = "Amount",
            DataType = ReportParameterDataType.Decimal
        });
        report.DataSets.Add(dataSet);

        report.Styles.Add(new ReportStyleDefinition
        {
            Id = "Heading",
            FontFamily = "Aptos",
            FontSize = 16f,
            Bold = true
        });

        report.SharedTemplates.Add(new ReportSharedTemplateDefinition
        {
            Id = "intro",
            Format = ReportDocumentTemplateFormat.Markdown,
            IsEmbedded = true,
            Content = "# Overview"
        });

        var section = new ReportSection
        {
            Id = "main",
            Name = "Main"
        };
        section.BodyItems.Add(new TextItem
        {
            Id = "title",
            Name = "Title",
            Bounds = new ReportItemBounds(0, 0, 400, 40),
            StaticText = "Sales Summary",
            StyleName = "Heading"
        });

        var tablix = new TablixItem
        {
            Id = "table",
            Name = "Table",
            Bounds = new ReportItemBounds(0, 48, 500, 200),
            DataSetId = "sales"
        };
        tablix.Columns.Add(new ReportTablixColumnDefinition { Id = "amount", Width = 120f });
        var row = new ReportTablixRowDefinition { Id = "header", IsHeader = true };
        row.Cells.Add(new ReportTablixCellDefinition { Text = "Amount" });
        tablix.Rows.Add(row);
        section.BodyItems.Add(tablix);

        var templateItem = new DocumentTemplateItem
        {
            Id = "intro",
            Name = "Intro",
            Bounds = new ReportItemBounds(0, 260, 500, 120),
            TemplateId = "intro",
            TemplateFormat = ReportDocumentTemplateFormat.Markdown
        };
        templateItem.Bindings["Region"] = "=Parameters!region.Value";
        section.BodyItems.Add(templateItem);

        report.Sections.Add(section);
        return report;
    }
}
