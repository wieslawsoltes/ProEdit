using ProEdit.Documents;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.Expressions;
using ProEdit.Reporting.Materialization;
using Xunit;

namespace ProEdit.Reporting.Materialization.Tests;

public sealed class ReportMaterializerTests
{
    [Fact]
    public async Task MaterializeAsync_BuildsSemanticSectionsTextTablixAndChart()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "sales-report",
            Name = "Sales Report",
            Styles =
            {
                new ReportStyleDefinition
                {
                    Id = "heading",
                    FontFamily = "Aptos",
                    FontSize = 20f,
                    Bold = true,
                    Foreground = "#112233"
                }
            },
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
                        new TextItem
                        {
                            Id = "title",
                            Name = "Title",
                            StaticText = "Sales Summary",
                            StyleName = "heading",
                            BookmarkExpression = "'sales-summary'",
                            Bounds = new ReportItemBounds(0f, 0f, 300f, 24f)
                        },
                        new TablixItem
                        {
                            Id = "sales-table",
                            Name = "Sales Table",
                            DataSetId = "sales",
                            Bounds = new ReportItemBounds(0f, 30f, 300f, 120f),
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "region", Width = 120f },
                                new ReportTablixColumnDefinition { Id = "amount", Width = 80f }
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
                                        new ReportTablixCellDefinition { Text = "Amount" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Amount", FormatString = "0.00" }
                                    }
                                }
                            }
                        },
                        new ChartItem
                        {
                            Id = "sales-chart",
                            Name = "Sales Chart",
                            DataSetId = "sales",
                            TitleExpression = "'Sales by Region'",
                            CategoryExpression = "Fields.Region",
                            Bounds = new ReportItemBounds(0f, 160f, 320f, 180f),
                            Series =
                            {
                                new ReportChartSeriesDefinition
                                {
                                    NameExpression = "'Amount'",
                                    ValueExpression = "Fields.Amount",
                                    ColorExpression = "'#336699'"
                                }
                            }
                        }
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
                    }
                }));

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var request = new ReportMaterializationRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData
        };

        var result = await materializer.MaterializeAsync(request);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.MaterializedReport);
        Assert.Single(result.MaterializedReport!.Sections);
        Assert.Single(result.MaterializedReport.DataSets);

        var section = result.MaterializedReport.Sections[0];
        Assert.Equal(3, section.BodyItems.Count);

        var textItem = Assert.IsType<MaterializedTextReportItem>(section.BodyItems[0]);
        Assert.Equal("Sales Summary", textItem.Text);
        Assert.Equal("sales-summary", textItem.Bookmark);
        Assert.NotNull(textItem.Style);
        Assert.Equal("Aptos", textItem.Style!.FontFamily);
        Assert.Equal(20f, textItem.Style.FontSize);

        var tablixItem = Assert.IsType<MaterializedTablixReportItem>(section.BodyItems[1]);
        Assert.Equal(2, tablixItem.Columns.Count);
        Assert.Equal(3, tablixItem.Rows.Count);
        Assert.True(tablixItem.Rows[0].IsHeader);
        Assert.Equal("West", tablixItem.Rows[1].Cells[0].Text);
        Assert.Equal("10.00", tablixItem.Rows[1].Cells[1].Text);

        var chartItem = Assert.IsType<MaterializedChartReportItem>(section.BodyItems[2]);
        Assert.NotNull(chartItem.Model);
        Assert.Equal("Sales by Region", chartItem.Model!.Title);
        Assert.Single(chartItem.Model.Series);
        Assert.Equal(2, chartItem.Model.Series[0].Points.Count);
        Assert.Equal("West", chartItem.Model.Series[0].Points[0].Category);
        Assert.Equal(10d, chartItem.Model.Series[0].Points[0].Value);
    }

    [Fact]
    public async Task MaterializeAsync_EvaluatesChartAxisBoundsFromNamedScopes()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "chart-axis",
            Name = "Chart Axis",
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
                        new ChartItem
                        {
                            Id = "sales-chart",
                            Name = "Sales Chart",
                            DataSetId = "sales",
                            CategoryExpression = "Fields.Region",
                            Bounds = new ReportItemBounds(0f, 0f, 320f, 180f),
                            Axes =
                            {
                                new ChartAxis
                                {
                                    Kind = ChartAxisKind.Value,
                                    MinimumExpression = "0",
                                    MaximumExpression = "Max(Fields.Amount, \"sales\")",
                                    SyncScopeName = "sales",
                                    SyncMaximum = true
                                }
                            },
                            Series =
                            {
                                new ReportChartSeriesDefinition
                                {
                                    NameExpression = "'Amount'",
                                    ValueExpression = "Fields.Amount"
                                }
                            }
                        }
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
                        ["Amount"] = 20m
                    }
                }));

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var result = await materializer.MaterializeAsync(new ReportMaterializationRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData
        });

        Assert.False(result.HasErrors);
        var chart = Assert.IsType<MaterializedChartReportItem>(Assert.Single(result.MaterializedReport!.Sections[0].BodyItems));
        var valueAxis = Assert.Single(chart.Model!.Axes, static axis => axis.Kind == ChartAxisKind.Value);
        Assert.Equal(0d, valueAxis.Minimum);
        Assert.Equal(20d, valueAxis.Maximum);
    }

    [Fact]
    public async Task MaterializeAsync_AppliesTablixFiltersBeforeDetailExpansion()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "filtered-tablix",
            Name = "Filtered Tablix",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Donor",
                    DisplayName = "Donor",
                    DataType = ReportParameterDataType.String
                }
            },
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "donors-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "donors"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "donors",
                    DataSourceId = "donors-source"
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
                            Id = "donor-table",
                            Name = "Donor Table",
                            DataSetId = "donors",
                            Bounds = new ReportItemBounds(0f, 0f, 300f, 90f),
                            Filters =
                            {
                                new ReportFilterDefinition
                                {
                                    Expression = "Fields.Name",
                                    Operator = ReportFilterOperator.Equal,
                                    ValueExpression = "Parameters.Donor"
                                }
                            },
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "name", Width = 150f },
                                new ReportTablixColumnDefinition { Id = "amount", Width = 80f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "header",
                                    IsHeader = true,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = "Name" },
                                        new ReportTablixCellDefinition { Text = "Amount" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Name" },
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Amount" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var hostData = new ReportHostDataRegistry();
        hostData.RegisterInMemorySource(
            "donors",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "Jarred Pierce",
                        ["Amount"] = 2500
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "Roberto Hilario",
                        ["Amount"] = 3700
                    }
                }));

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var request = new ReportMaterializationRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostData
        };
        request.ParameterValues["Donor"] = ReportParameterValue.FromScalar("Roberto Hilario");

        var result = await materializer.MaterializeAsync(request);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<MaterializedReport>(result.MaterializedReport);
        Assert.Equal(2, report.DataSets[0].Rows.Count);
        var tablix = Assert.IsType<MaterializedTablixReportItem>(Assert.Single(report.Sections[0].BodyItems));
        Assert.Equal(2, tablix.Rows.Count);
        Assert.True(tablix.Rows[0].IsHeader);
        Assert.Equal("Roberto Hilario", tablix.Rows[1].Cells[0].Text);
        Assert.Equal("3700", tablix.Rows[1].Cells[1].Text);
    }

    [Fact]
    public async Task MaterializeAsync_ResolvesSharedTemplatesAndSubreports()
    {
        var subreportDefinition = new ReportDefinition
        {
            Id = "details",
            Name = "Details",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "OrderId",
                    DisplayName = "Order Id",
                    DataType = ReportParameterDataType.Integer
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "detail-section",
                    Name = "Detail Section",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "detail-text",
                            ValueExpression = "'Detail ' + Parameters.OrderId",
                            Bounds = new ReportItemBounds(0f, 0f, 200f, 20f)
                        }
                    }
                }
            }
        };

        var reportDefinition = new ReportDefinition
        {
            Id = "invoice",
            Name = "Invoice",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "OrderId",
                    DisplayName = "Order Id",
                    DataType = ReportParameterDataType.Integer
                }
            },
            SharedTemplates =
            {
                new ReportSharedTemplateDefinition
                {
                    Id = "invoice-template",
                    Format = ReportDocumentTemplateFormat.Markdown,
                    IsEmbedded = true,
                    Content = "Invoice {{OrderId}}"
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
                        new DocumentTemplateItem
                        {
                            Id = "template",
                            TemplateId = "invoice-template",
                            Bounds = new ReportItemBounds(0f, 0f, 300f, 80f),
                            Bindings =
                            {
                                ["OrderId"] = "Parameters.OrderId"
                            }
                        },
                        new SubreportItem
                        {
                            Id = "details-subreport",
                            ReportReferenceId = "details",
                            Bounds = new ReportItemBounds(0f, 90f, 300f, 60f),
                            Parameters =
                            {
                                new ReportParameterBinding
                                {
                                    ParameterId = "OrderId",
                                    ValueExpression = "Parameters.OrderId"
                                }
                            }
                        }
                    }
                }
            }
        };

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var request = new ReportMaterializationRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry()
        };
        request.ParameterValues["OrderId"] = ReportParameterValue.FromScalar(42);
        request.ReferencedReports["details"] = subreportDefinition;

        var result = await materializer.MaterializeAsync(request);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.MaterializedReport);

        var bodyItems = result.MaterializedReport!.Sections[0].BodyItems;
        var templateItem = Assert.IsType<MaterializedDocumentTemplateReportItem>(bodyItems[0]);
        Assert.Equal(ReportDocumentTemplateFormat.Markdown, templateItem.TemplateFormat);
        Assert.Equal("Invoice {{OrderId}}", templateItem.Content);
        Assert.Equal("42", templateItem.Bindings["OrderId"]);

        var subreportItem = Assert.IsType<MaterializedSubreportReportItem>(bodyItems[1]);
        Assert.NotNull(subreportItem.Report);
        Assert.Equal(42, Assert.IsType<int>(subreportItem.Report!.ResolvedParameters["OrderId"].GetScalarValue()));
        var nestedText = Assert.IsType<MaterializedTextReportItem>(subreportItem.Report.Sections[0].BodyItems[0]);
        Assert.Equal("Detail 42", nestedText.Text);
    }

    [Fact]
    public async Task MaterializeAsync_StopsWhenParameterResolutionFails()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "invalid",
            Name = "Invalid",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "HiddenRequired",
                    DisplayName = "Hidden Required",
                    DataType = ReportParameterDataType.Integer,
                    Visibility = ReportParameterVisibility.Hidden
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
                        new TextItem
                        {
                            Id = "text",
                            StaticText = "Should not materialize"
                        }
                    }
                }
            }
        };

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var result = await materializer.MaterializeAsync(new ReportMaterializationRequest
        {
            ReportDefinition = reportDefinition,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry()
        });

        Assert.True(result.HasErrors);
        Assert.Null(result.MaterializedReport);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ReportDiagnosticCodes.ParameterResolutionFailed);
    }

    [Fact]
    public async Task MaterializeAsync_ExpandsGroupedTablixHierarchy()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "grouped-sales",
            Name = "Grouped Sales",
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
                            DataSetId = "sales",
                            Bounds = new ReportItemBounds(0f, 0f, 480f, 180f),
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "region", Width = 140f },
                                new ReportTablixColumnDefinition { Id = "salesperson", Width = 180f },
                                new ReportTablixColumnDefinition { Id = "amount", Width = 120f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "header",
                                    IsHeader = true,
                                    Height = 18f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = "Region" },
                                        new ReportTablixCellDefinition { Text = "Salesperson" },
                                        new ReportTablixCellDefinition { Text = "Amount" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "group",
                                    IsHeader = true,
                                    Height = 20f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                                        new ReportTablixCellDefinition { Text = "Subtotal" },
                                        new ReportTablixCellDefinition { ValueExpression = "Sum(Fields.Amount)", FormatString = "0.00" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Height = 18f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = string.Empty },
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Salesperson" },
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Amount", FormatString = "0.00" }
                                    }
                                }
                            },
                            RowMembers =
                            {
                                new ReportTablixMemberDefinition
                                {
                                    Id = "header-member",
                                    Kind = ReportTablixMemberKind.Static,
                                    RepeatOnNewPage = true,
                                    RowDefinitionIndex = 0
                                },
                                new ReportTablixMemberDefinition
                                {
                                    Id = "region-group",
                                    Kind = ReportTablixMemberKind.Group,
                                    GroupName = "RegionGroup",
                                    GroupExpression = "Fields.Region",
                                    SortExpression = "Fields.Region",
                                    SortDirection = ReportSortDirection.Descending,
                                    Members =
                                    {
                                        new ReportTablixMemberDefinition
                                        {
                                            Id = "region-summary",
                                            Kind = ReportTablixMemberKind.Static,
                                            RowDefinitionIndex = 1
                                        },
                                        new ReportTablixMemberDefinition
                                        {
                                            Id = "detail-member",
                                            Kind = ReportTablixMemberKind.Details,
                                            SortExpression = "Fields.Amount",
                                            SortDirection = ReportSortDirection.Descending,
                                            RowDefinitionIndex = 2
                                        }
                                    }
                                }
                            }
                        }
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
                        ["Salesperson"] = "John",
                        ["Amount"] = 10m
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "West",
                        ["Salesperson"] = "Jane",
                        ["Amount"] = 12m
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "East",
                        ["Salesperson"] = "Bob",
                        ["Amount"] = 5m
                    }
                }));

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var result = await materializer.MaterializeAsync(
            new ReportMaterializationRequest
            {
                ReportDefinition = reportDefinition,
                ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
                HostDataRegistry = hostData
            });

        Assert.False(result.HasErrors);
        var tablix = Assert.IsType<MaterializedTablixReportItem>(Assert.Single(result.MaterializedReport!.Sections[0].BodyItems));
        Assert.Equal(6, tablix.Rows.Count);
        Assert.True(tablix.Rows[0].IsHeader);
        Assert.Equal(18f, tablix.Rows[0].Height, 3);
        Assert.Equal("West", tablix.Rows[1].Cells[0].Text);
        Assert.Equal("22.00", tablix.Rows[1].Cells[2].Text);
        Assert.Equal("Jane", tablix.Rows[2].Cells[1].Text);
        Assert.Equal("12.00", tablix.Rows[2].Cells[2].Text);
        Assert.Equal("John", tablix.Rows[3].Cells[1].Text);
        Assert.Equal("East", tablix.Rows[4].Cells[0].Text);
        Assert.Equal("5.00", tablix.Rows[5].Cells[2].Text);
    }

    [Fact]
    public async Task MaterializeAsync_PreservesDetailRowNumberInsideNestedDetailsLeaf()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "detail-scope-report",
            Name = "Detail Scope Report",
            Styles =
            {
                new ReportStyleDefinition
                {
                    Id = "detail-style",
                    BackgroundExpression = "IIf(RowNumber(null) % 2 = 0, 'Tan', 'White')"
                }
            },
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
                            DataSetId = "sales",
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "item", Width = 120f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "header",
                                    IsHeader = true,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = "Item" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition
                                        {
                                            ValueExpression = "Fields.Item",
                                            StyleName = "detail-style"
                                        }
                                    }
                                }
                            },
                            RowMembers =
                            {
                                new ReportTablixMemberDefinition
                                {
                                    Id = "header-member",
                                    Kind = ReportTablixMemberKind.Static,
                                    RowDefinitionIndex = 0
                                },
                                new ReportTablixMemberDefinition
                                {
                                    Id = "details-member",
                                    Kind = ReportTablixMemberKind.Details,
                                    Members =
                                    {
                                        new ReportTablixMemberDefinition
                                        {
                                            Id = "detail-leaf",
                                            Kind = ReportTablixMemberKind.Static,
                                            RowDefinitionIndex = 1
                                        }
                                    }
                                }
                            }
                        }
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
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Item"] = "A" },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Item"] = "B" },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Item"] = "C" }
                }));

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var result = await materializer.MaterializeAsync(
            new ReportMaterializationRequest
            {
                ReportDefinition = reportDefinition,
                ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
                HostDataRegistry = hostData
            });

        Assert.False(result.HasErrors);
        var tablix = Assert.IsType<MaterializedTablixReportItem>(Assert.Single(result.MaterializedReport!.Sections[0].BodyItems));
        Assert.Equal(4, tablix.Rows.Count);
        Assert.True(tablix.Rows[0].IsHeader);
        Assert.False(tablix.Rows[1].IsHeader);
        Assert.False(tablix.Rows[2].IsHeader);
        Assert.False(tablix.Rows[3].IsHeader);
        Assert.Equal("White", tablix.Rows[1].Cells[0].Style?.Background);
        Assert.Equal("Tan", tablix.Rows[2].Cells[0].Style?.Background);
        Assert.Equal("White", tablix.Rows[3].Cells[0].Style?.Background);
    }

    [Fact]
    public async Task MaterializeAsync_TreatsVisibilityExpressionAsRdlHiddenExpression()
    {
        var reportDefinition = new ReportDefinition
        {
            Id = "visibility-report",
            Name = "Visibility Report",
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new ContainerItem
                        {
                            Id = "container",
                            Bounds = new ReportItemBounds(0f, 0f, 200f, 80f),
                            Items =
                            {
                                new TextItem
                                {
                                    Id = "visible-text",
                                    StaticText = "Visible",
                                    VisibilityExpression = "false",
                                    Bounds = new ReportItemBounds(0f, 0f, 120f, 20f)
                                },
                                new TextItem
                                {
                                    Id = "hidden-text",
                                    StaticText = "Hidden",
                                    VisibilityExpression = "true",
                                    Bounds = new ReportItemBounds(0f, 24f, 120f, 20f)
                                }
                            }
                        }
                    }
                }
            }
        };

        var materializer = new ReportMaterializer(new ReportExpressionCompiler());
        var result = await materializer.MaterializeAsync(
            new ReportMaterializationRequest
            {
                ReportDefinition = reportDefinition,
                ProviderRegistry = ReportDataProviders.CreateDefaultRegistry()
            });

        Assert.False(result.HasErrors);
        var container = Assert.IsType<MaterializedContainerReportItem>(Assert.Single(result.MaterializedReport!.Sections[0].BodyItems));
        var child = Assert.IsType<MaterializedTextReportItem>(Assert.Single(container.Items));
        Assert.Equal("visible-text", child.SourceItemId);
        Assert.Equal("Visible", child.Text);
    }
}
