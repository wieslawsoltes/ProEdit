using System.Globalization;
using Vibe.Office.Reporting;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;

namespace Vibe.Reporting.App.Services;

internal sealed class ReportingStudioWorkspaceFactory
{
    private readonly ReportDataConnectorCatalog _connectorCatalog = ReportDataConnectorCatalog.CreateDefault();

    public ReportDataConnectorCatalog ConnectorCatalog => _connectorCatalog;

    public ReportingStudioWorkspace CreateSampleWorkspace()
    {
        var detailReport = CreateRegionalDetailReportDefinition();
        var mainReport = CreateSalesOverviewReportDefinition();
        var source = CreateSource(
            mainReport,
            new Dictionary<string, ReportDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [detailReport.Id] = detailReport
            });

        return new ReportingStudioWorkspace(
            source,
            ReportingStudioDocumentKind.Sample,
            path: null,
            diagnostics: Array.Empty<ReportDiagnostic>());
    }

    public ReportViewerSource CreateSource(
        ReportDefinition reportDefinition,
        IReadOnlyDictionary<string, ReportDefinition>? referencedReports = null)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        var source = new ReportViewerSource
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = CreateHostDataRegistry(),
            Culture = CultureInfo.GetCultureInfo("en-US"),
            UiCulture = CultureInfo.GetCultureInfo("en-US"),
            TimeZone = TimeZoneInfo.Local
        };

        source.Globals["StudioName"] = "VibeOffice Reporting Studio";
        source.Globals["StudioVersion"] = "Preview";

        if (referencedReports is not null)
        {
            foreach (var pair in referencedReports)
            {
                source.ReferencedReports[pair.Key] = pair.Value;
            }
        }

        return source;
    }

    private static ReportHostDataRegistry CreateHostDataRegistry()
    {
        var registry = new ReportHostDataRegistry();
        registry.RegisterInMemorySource("sales", new ReportDictionaryDataSource(CreateSalesRows(), CreateSalesFields()));
        registry.RegisterInMemorySource("regions", new ReportDictionaryDataSource(CreateRegionRows(), CreateRegionFields()));
        registry.RegisterJsonSource("targets-json", """
            {
              "targets": [
                { "Region": "West", "Target": 1400000 },
                { "Region": "East", "Target": 1250000 },
                { "Region": "North", "Target": 1500000 }
              ]
            }
            """);
        registry.RegisterCsvSource(
            "targets-csv",
            """
            Region,Target
            West,1400000
            East,1250000
            North,1500000
            """
        );

        return registry;
    }

    private static ReportDefinition CreateSalesOverviewReportDefinition()
    {
        var report = new ReportDefinition
        {
            Id = "sales-overview",
            Name = "FY26 Revenue Review",
            Description = "Bundled sample report used by the standalone reporting studio."
        };

        report.Metadata["studio.sample"] = "true";
        report.Metadata["studio.category"] = "Revenue";

        report.Styles.Add(new ReportStyleDefinition
        {
            Id = "title",
            FontFamily = "Inter",
            FontSize = 24f,
            Bold = true,
            Foreground = "#17324D"
        });
        report.Styles.Add(new ReportStyleDefinition
        {
            Id = "heading",
            FontFamily = "Inter",
            FontSize = 16f,
            Bold = true,
            Foreground = "#0F3D56"
        });
        report.Styles.Add(new ReportStyleDefinition
        {
            Id = "muted",
            FontFamily = "Inter",
            FontSize = 10f,
            Foreground = "#5C6E84"
        });

        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "Title",
            DisplayName = "Title",
            Prompt = "Report Title",
            DataType = ReportParameterDataType.String,
            DefaultValueExpression = "'FY26 Revenue Review'",
            Visibility = ReportParameterVisibility.Visible
        });
        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "MinimumRevenue",
            DisplayName = "Minimum Revenue",
            Prompt = "Minimum Revenue",
            DataType = ReportParameterDataType.Decimal,
            DefaultValueExpression = "1000000",
            Visibility = ReportParameterVisibility.Visible
        });
        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "FocusRegion",
            DisplayName = "Focus Region",
            Prompt = "Focus Region",
            DataType = ReportParameterDataType.String,
            DefaultValueExpression = "'All'",
            AvailableValuesDataSetId = "regions",
            ValueField = "Region",
            LabelField = "Region",
            Visibility = ReportParameterVisibility.Visible
        });

        report.DataSources.Add(new ReportDataSourceDefinition
        {
            Id = "sales-source",
            ProviderId = ReportProviderIds.InMemory,
            Options =
            {
                ["sourceKey"] = "sales"
            }
        });
        report.DataSources.Add(new ReportDataSourceDefinition
        {
            Id = "regions-source",
            ProviderId = ReportProviderIds.InMemory,
            Options =
            {
                ["sourceKey"] = "regions"
            }
        });

        report.DataSets.Add(new ReportDataSetDefinition
        {
            Id = "sales",
            DataSourceId = "sales-source",
            CalculatedFields =
            {
                new ReportCalculatedFieldDefinition
                {
                    Name = "Status",
                    Expression = "Iif(Fields.Revenue >= 1300000, 'Ahead', Iif(Fields.Revenue >= 1100000, 'On Track', 'Watch'))",
                    DataType = ReportParameterDataType.String
                }
            },
            Filters =
            {
                new ReportFilterDefinition
                {
                    Expression = "Fields.Revenue",
                    Operator = ReportFilterOperator.GreaterThanOrEqual,
                    ValueExpression = "Parameters.MinimumRevenue"
                }
            },
            Sorts =
            {
                new ReportSortDefinition
                {
                    Expression = "Fields.Revenue",
                    Direction = ReportSortDirection.Descending
                }
            }
        });
        report.DataSets.Add(new ReportDataSetDefinition
        {
            Id = "regions",
            DataSourceId = "regions-source",
            Sorts =
            {
                new ReportSortDefinition
                {
                    Expression = "Fields.Region",
                    Direction = ReportSortDirection.Ascending
                }
            }
        });

        report.SharedTemplates.Add(new ReportSharedTemplateDefinition
        {
            Id = "narrative-brief",
            Format = ReportDocumentTemplateFormat.Markdown,
            IsEmbedded = true,
            Content = """
                Generated **{{GeneratedOn}}** for **{{FocusRegion}}** with a minimum revenue threshold of **{{MinimumRevenue}}**.

                The overview pairs a live chart, a focused regional detail, and the filtered revenue table so design changes stay aligned with preview and export.
                """
        });

        var section = new ReportSection
        {
            Id = "overview",
            Name = "Overview",
            BookmarkExpression = "'overview'"
        };

        section.HeaderItems.Add(new TextItem
        {
            Id = "header-brand",
            Name = "Header Brand",
            StaticText = "VibeOffice Reporting Studio",
            StyleName = "muted",
            Bounds = new ReportItemBounds(0f, 0f, 240f, 16f)
        });
        section.FooterItems.Add(new TextItem
        {
            Id = "page-number",
            Name = "Page Number",
            ValueExpression = "Globals.PageNumber",
            StyleName = "muted",
            Bounds = new ReportItemBounds(0f, 0f, 80f, 16f)
        });

        section.BodyItems.Add(new TextItem
        {
            Id = "title",
            Name = "Title",
            ValueExpression = "Parameters.Title",
            StyleName = "title",
            BookmarkExpression = "'revenue-title'",
            Bounds = new ReportItemBounds(36f, 28f, 540f, 32f)
        });
        section.BodyItems.Add(new DocumentTemplateItem
        {
            Id = "brief",
            Name = "Executive Brief",
            TemplateId = "narrative-brief",
            Bounds = new ReportItemBounds(36f, 78f, 700f, 76f),
            Bindings =
            {
                ["GeneratedOn"] = "Format(Globals.ExecutionTime, 'yyyy-MM-dd HH:mm')",
                ["FocusRegion"] = "Iif(Parameters.FocusRegion = 'All', 'all regions', Parameters.FocusRegion)",
                ["MinimumRevenue"] = "Format(Parameters.MinimumRevenue, 'C0')"
            }
        });
        section.BodyItems.Add(new ChartItem
        {
            Id = "revenue-chart",
            Name = "Revenue by Region",
            DataSetId = "sales",
            TitleExpression = "'Revenue by Region'",
            CategoryExpression = "Fields.Region",
            Bounds = new ReportItemBounds(36f, 186f, 460f, 220f),
            Series =
            {
                new ReportChartSeriesDefinition
                {
                    NameExpression = "'Revenue'",
                    ValueExpression = "Fields.Revenue",
                    ColorExpression = "Iif(Fields.Revenue >= 1300000, '#0E7490', '#C2410C')"
                }
            }
        });
        section.BodyItems.Add(new TextItem
        {
            Id = "drillthrough-west",
            Name = "Open West Detail",
            StaticText = "Open West Detail",
            StyleName = "heading",
            DrillthroughAction = new ReportDrillthroughAction
            {
                ReportReferenceId = "regional-detail",
                Parameters =
                {
                    new ReportParameterBinding
                    {
                        ParameterId = "RegionName",
                        ValueExpression = "'West'"
                    }
                }
            },
            Bounds = new ReportItemBounds(528f, 196f, 220f, 24f)
        });
        section.BodyItems.Add(new SubreportItem
        {
            Id = "central-detail",
            Name = "Embedded Central Detail",
            ReportReferenceId = "regional-detail",
            Bounds = new ReportItemBounds(528f, 238f, 220f, 120f),
            Parameters =
            {
                new ReportParameterBinding
                {
                    ParameterId = "RegionName",
                    ValueExpression = "'Central'"
                }
            }
        });
        section.BodyItems.Add(new TablixItem
        {
            Id = "sales-table",
            Name = "Revenue Table",
            DataSetId = "sales",
            Bounds = new ReportItemBounds(36f, 444f, 710f, 244f),
            Columns =
            {
                new ReportTablixColumnDefinition { Id = "region", Width = 140f },
                new ReportTablixColumnDefinition { Id = "quarter", Width = 110f },
                new ReportTablixColumnDefinition { Id = "revenue", Width = 150f },
                new ReportTablixColumnDefinition { Id = "status", Width = 120f }
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
                        new ReportTablixCellDefinition { Text = "Quarter" },
                        new ReportTablixCellDefinition { Text = "Revenue" },
                        new ReportTablixCellDefinition { Text = "Status" }
                    }
                },
                new ReportTablixRowDefinition
                {
                    Id = "detail",
                    Cells =
                    {
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Quarter" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Revenue", FormatString = "C0" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Status" }
                    }
                }
            }
        });

        report.Sections.Add(section);
        return report;
    }

    private static ReportDefinition CreateRegionalDetailReportDefinition()
    {
        var report = new ReportDefinition
        {
            Id = "regional-detail",
            Name = "Regional Detail"
        };

        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "RegionName",
            DisplayName = "Region Name",
            DataType = ReportParameterDataType.String,
            DefaultValueExpression = "'West'",
            Visibility = ReportParameterVisibility.Hidden
        });

        report.DataSources.Add(new ReportDataSourceDefinition
        {
            Id = "sales-source",
            ProviderId = ReportProviderIds.InMemory,
            Options =
            {
                ["sourceKey"] = "sales"
            }
        });

        report.DataSets.Add(new ReportDataSetDefinition
        {
            Id = "regional-sales",
            DataSourceId = "sales-source",
            Filters =
            {
                new ReportFilterDefinition
                {
                    Expression = "Fields.Region",
                    Operator = ReportFilterOperator.Equal,
                    ValueExpression = "Parameters.RegionName"
                }
            },
            Sorts =
            {
                new ReportSortDefinition
                {
                    Expression = "Fields.Quarter",
                    Direction = ReportSortDirection.Ascending
                }
            }
        });

        var section = new ReportSection
        {
            Id = "detail",
            Name = "Regional Detail"
        };

        section.BodyItems.Add(new TextItem
        {
            Id = "detail-title",
            Name = "Detail Title",
            ValueExpression = "'Regional Detail: ' + Parameters.RegionName",
            Bounds = new ReportItemBounds(24f, 24f, 280f, 24f)
        });
        section.BodyItems.Add(new TablixItem
        {
            Id = "detail-table",
            Name = "Regional Revenue Table",
            DataSetId = "regional-sales",
            Bounds = new ReportItemBounds(24f, 60f, 360f, 160f),
            Columns =
            {
                new ReportTablixColumnDefinition { Id = "quarter", Width = 120f },
                new ReportTablixColumnDefinition { Id = "revenue", Width = 140f }
            },
            Rows =
            {
                new ReportTablixRowDefinition
                {
                    Id = "header",
                    IsHeader = true,
                    Cells =
                    {
                        new ReportTablixCellDefinition { Text = "Quarter" },
                        new ReportTablixCellDefinition { Text = "Revenue" }
                    }
                },
                new ReportTablixRowDefinition
                {
                    Id = "detail",
                    Cells =
                    {
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Quarter" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Revenue", FormatString = "C0" }
                    }
                }
            }
        });

        report.Sections.Add(section);
        return report;
    }

    private static IReadOnlyList<ReportFieldDefinition> CreateSalesFields()
    {
        return
        [
            new ReportFieldDefinition { Name = "Region", DataType = ReportParameterDataType.String },
            new ReportFieldDefinition { Name = "Quarter", DataType = ReportParameterDataType.String },
            new ReportFieldDefinition { Name = "Revenue", DataType = ReportParameterDataType.Decimal },
            new ReportFieldDefinition { Name = "Margin", DataType = ReportParameterDataType.Decimal }
        ];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CreateSalesRows()
    {
        return
        [
            CreateSalesRow("North", "Q1", 1430000m, 0.37m),
            CreateSalesRow("West", "Q1", 1325000m, 0.34m),
            CreateSalesRow("Central", "Q2", 1215000m, 0.31m),
            CreateSalesRow("East", "Q2", 1180000m, 0.29m),
            CreateSalesRow("South", "Q3", 980000m, 0.24m)
        ];
    }

    private static IReadOnlyDictionary<string, object?> CreateSalesRow(
        string region,
        string quarter,
        decimal revenue,
        decimal margin)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Region"] = region,
            ["Quarter"] = quarter,
            ["Revenue"] = revenue,
            ["Margin"] = margin
        };
    }

    private static IReadOnlyList<ReportFieldDefinition> CreateRegionFields()
    {
        return
        [
            new ReportFieldDefinition
            {
                Name = "Region",
                DataType = ReportParameterDataType.String
            }
        ];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CreateRegionRows()
    {
        return
        [
            CreateRegionRow("All"),
            CreateRegionRow("Central"),
            CreateRegionRow("East"),
            CreateRegionRow("North"),
            CreateRegionRow("South"),
            CreateRegionRow("West")
        ];
    }

    private static IReadOnlyDictionary<string, object?> CreateRegionRow(string value)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Region"] = value
        };
    }
}
