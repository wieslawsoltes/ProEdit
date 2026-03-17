using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Reporting.Rdl;
using Xunit;

namespace Vibe.Office.Reporting.Rdl.Tests;

public sealed class ReportRdlSerializerTests
{
    private readonly ReportRdlSerializer _serializer = new();

    [Fact]
    public void WriteAndRead_RoundTripsSupportedSubset()
    {
        var report = CreateSampleReport(includeUnsupportedItems: false);

        var writeResult = _serializer.Write(report);

        Assert.False(writeResult.HasErrors);
        Assert.Contains("2016/01/reportdefinition", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<ReportSections>", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<Tablix ", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<Chart ", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedImages>", writeResult.Xml, StringComparison.Ordinal);

        var readResult = _serializer.Read(writeResult.Xml);

        Assert.False(readResult.HasErrors);
        var roundTripped = Assert.IsType<ReportDefinition>(readResult.ReportDefinition);
        Assert.Equal("sales-report", roundTripped.Id);
        Assert.Equal("Sales Report", roundTripped.Name);
        Assert.Single(roundTripped.Parameters);
        Assert.Single(roundTripped.DataSources);
        Assert.Single(roundTripped.DataSets);
        Assert.Single(roundTripped.Sections);
        Assert.NotEmpty(roundTripped.Styles);

        var section = roundTripped.Sections[0];
        Assert.Single(section.HeaderItems);
        Assert.Single(section.FooterItems);
        Assert.Equal(7, section.BodyItems.Count);
        Assert.IsType<TextItem>(section.BodyItems[0]);
        Assert.IsType<ImageItem>(section.BodyItems[1]);
        Assert.IsType<ChartItem>(section.BodyItems[2]);
        Assert.IsType<TablixItem>(section.BodyItems[3]);
        Assert.IsType<SubreportItem>(section.BodyItems[4]);
        Assert.IsType<LineItem>(section.BodyItems[5]);
        Assert.IsType<ShapeItem>(section.BodyItems[6]);

        var title = Assert.IsType<TextItem>(section.BodyItems[0]);
        Assert.Equal("Parameters.Region", title.ValueExpression);
        Assert.Equal("'sales-title'", title.BookmarkExpression);
        Assert.Equal("C2", title.FormatString);

        var image = Assert.IsType<ImageItem>(section.BodyItems[1]);
        Assert.Equal(ReportImageSourceKind.Embedded, image.SourceKind);
        Assert.NotNull(image.EmbeddedData);

        var chart = Assert.IsType<ChartItem>(section.BodyItems[2]);
        Assert.Equal("SalesData", chart.DataSetId);
        Assert.Equal("Fields.Region", chart.CategoryExpression);
        Assert.Equal("'Sales by Region'", chart.TitleExpression);
        Assert.Single(chart.Series);
        Assert.Equal("Fields.Amount", chart.Series[0].ValueExpression);

        var tablix = Assert.IsType<TablixItem>(section.BodyItems[3]);
        Assert.Equal("SalesData", tablix.DataSetId);
        Assert.True(tablix.RepeatHeaderRows);
        Assert.Equal(2, tablix.Columns.Count);
        Assert.Equal(2, tablix.Rows.Count);
        Assert.True(tablix.Rows[0].IsHeader);
        Assert.False(tablix.Rows[1].IsHeader);
        Assert.Equal("Fields.Region", tablix.Rows[1].Cells[0].ValueExpression);

        var subreport = Assert.IsType<SubreportItem>(section.BodyItems[4]);
        Assert.Equal("sales-detail", subreport.ReportReferenceId);
        Assert.Single(subreport.Parameters);
        Assert.Equal("Parameters.Region", subreport.Parameters[0].ValueExpression);
    }

    [Fact]
    public void Read_ImportsLegacy2008SingleSectionReport()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>legacy-report</rd:ReportID>
              <Body>
                <ReportItems>
                  <Textbox Name="LegacyTitle">
                    <CanGrow>true</CanGrow>
                    <Paragraphs>
                      <Paragraph>
                        <TextRuns>
                          <TextRun>
                            <Value>=Parameters!Region.Value</Value>
                          </TextRun>
                        </TextRuns>
                      </Paragraph>
                    </Paragraphs>
                    <Top>0in</Top>
                    <Left>0in</Left>
                    <Height>0.25in</Height>
                    <Width>2in</Width>
                  </Textbox>
                </ReportItems>
                <Height>1in</Height>
              </Body>
              <Width>6.5in</Width>
              <Page>
                <PageHeight>11in</PageHeight>
                <PageWidth>8.5in</PageWidth>
                <LeftMargin>1in</LeftMargin>
                <RightMargin>1in</RightMargin>
                <TopMargin>1in</TopMargin>
                <BottomMargin>1in</BottomMargin>
              </Page>
              <ReportParameters>
                <ReportParameter Name="Region">
                  <DataType>String</DataType>
                  <Prompt>Region</Prompt>
                </ReportParameter>
              </ReportParameters>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        Assert.Equal("legacy-report", report.Id);
        Assert.Single(report.Sections);
        Assert.Single(report.Parameters);
        Assert.Single(report.Sections[0].BodyItems);
        var title = Assert.IsType<TextItem>(report.Sections[0].BodyItems[0]);
        Assert.Equal("Parameters.Region", title.ValueExpression);
    }

    [Fact]
    public void Read_ConvertsScalarLiteralValuesToExecutableNativeExpressions()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>literal-report</rd:ReportID>
              <DataSources>
                <DataSource Name="Source">
                  <ConnectionProperties>
                    <DataProvider>SQL</DataProvider>
                    <ConnectString>Server=(local)</ConnectString>
                  </ConnectionProperties>
                </DataSource>
              </DataSources>
              <DataSets>
                <DataSet Name="Data">
                  <Query>
                    <DataSourceName>Source</DataSourceName>
                    <CommandText>select 1</CommandText>
                    <QueryParameters>
                      <QueryParameter Name="@Region">
                        <Value>West</Value>
                      </QueryParameter>
                    </QueryParameters>
                  </Query>
                  <Filters>
                    <Filter>
                      <FilterExpression>=Fields!Region.Value</FilterExpression>
                      <Operator>Equal</Operator>
                      <FilterValues>
                        <FilterValue>West</FilterValue>
                      </FilterValues>
                    </Filter>
                  </Filters>
                </DataSet>
              </DataSets>
              <ReportParameters>
                <ReportParameter Name="Region">
                  <DataType>String</DataType>
                  <DefaultValue>
                    <Values>
                      <Value>West</Value>
                    </Values>
                  </DefaultValue>
                </ReportParameter>
              </ReportParameters>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems />
                    <Height>1in</Height>
                  </Body>
                  <Width>6.5in</Width>
                  <Page>
                    <PageHeight>11in</PageHeight>
                    <PageWidth>8.5in</PageWidth>
                    <LeftMargin>1in</LeftMargin>
                    <RightMargin>1in</RightMargin>
                    <TopMargin>1in</TopMargin>
                    <BottomMargin>1in</BottomMargin>
                  </Page>
                </ReportSection>
              </ReportSections>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var dataSet = Assert.Single(report.DataSets);
        Assert.Equal("'West'", Assert.Single(report.Parameters).DefaultValueExpression);
        Assert.Equal("'West'", Assert.Single(dataSet.Parameters).ValueExpression);
        Assert.Equal("'West'", Assert.Single(dataSet.Filters).ValueExpression);
    }

    [Fact]
    public void Read_TranslatesParameterLabelsAndCurrentValueExpressions()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>parameter-label-report</rd:ReportID>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Textbox Name="Title">
                        <Paragraphs>
                          <Paragraph>
                            <TextRuns>
                              <TextRun>
                                <Value>="Organization: " &amp; Parameters!OrganizationKey.Label &amp; " | Department: " &amp; Parameters!DepartmentGroupKey.Label</Value>
                              </TextRun>
                            </TextRuns>
                          </Paragraph>
                        </Paragraphs>
                        <Top>0in</Top>
                        <Left>0in</Left>
                        <Height>0.25in</Height>
                        <Width>5in</Width>
                      </Textbox>
                      <Textbox Name="Variance">
                        <Paragraphs>
                          <Paragraph>
                            <TextRuns>
                              <TextRun>
                                <Value>=Code.SalesVariancePct(Sum(Fields!Sales.Value), Sum(Fields!Quota.Value))</Value>
                                <Style>
                                  <Color>=Iif(Me.Value &lt; 0, "#c0433a", "Black")</Color>
                                </Style>
                              </TextRun>
                            </TextRuns>
                          </Paragraph>
                        </Paragraphs>
                        <Top>0.3in</Top>
                        <Left>0in</Left>
                        <Height>0.25in</Height>
                        <Width>2in</Width>
                      </Textbox>
                    </ReportItems>
                    <Height>1in</Height>
                  </Body>
                  <Width>6.5in</Width>
                  <Page>
                    <PageHeight>11in</PageHeight>
                    <PageWidth>8.5in</PageWidth>
                    <LeftMargin>1in</LeftMargin>
                    <RightMargin>1in</RightMargin>
                    <TopMargin>1in</TopMargin>
                    <BottomMargin>1in</BottomMargin>
                  </Page>
                </ReportSection>
              </ReportSections>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var title = Assert.IsType<TextItem>(report.Sections[0].BodyItems[0]);
        Assert.Equal("'Organization: ' + ParameterLabel('OrganizationKey') + ' | Department: ' + ParameterLabel('DepartmentGroupKey')", title.ValueExpression);

        var variance = Assert.IsType<TextItem>(report.Sections[0].BodyItems[1]);
        Assert.NotNull(variance.StyleName);
        var style = Assert.Single(report.Styles, candidate => candidate.Id == variance.StyleName);
        Assert.Equal("Iif(CurrentValue() < 0, '#c0433a', 'Black')", style.ForegroundExpression);
    }

    [Fact]
    public void Read_ImportsGroupedTablixHierarchy()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>grouped-report</rd:ReportID>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Tablix Name="SalesTable">
                        <TablixBody>
                          <TablixColumns>
                            <TablixColumn><Width>2in</Width></TablixColumn>
                          </TablixColumns>
                          <TablixRows>
                            <TablixRow>
                              <Height>0.25in</Height>
                              <TablixCells>
                                <TablixCell>
                                  <CellContents>
                                    <Textbox Name="CategoryCell">
                                      <Paragraphs>
                                        <Paragraph>
                                          <TextRuns>
                                            <TextRun>
                                              <Value>=Fields!Category.Value</Value>
                                            </TextRun>
                                          </TextRuns>
                                        </Paragraph>
                                      </Paragraphs>
                                    </Textbox>
                                  </CellContents>
                                </TablixCell>
                              </TablixCells>
                            </TablixRow>
                          </TablixRows>
                        </TablixBody>
                        <TablixColumnHierarchy>
                          <TablixMembers>
                            <TablixMember />
                          </TablixMembers>
                        </TablixColumnHierarchy>
                        <TablixRowHierarchy>
                          <TablixMembers>
                            <TablixMember>
                              <Group Name="CategoryGroup">
                                <GroupExpressions>
                                  <GroupExpression>=Fields!Category.Value</GroupExpression>
                                </GroupExpressions>
                              </Group>
                            </TablixMember>
                          </TablixMembers>
                        </TablixRowHierarchy>
                        <DataSetName>SalesData</DataSetName>
                        <Top>0in</Top>
                        <Left>0in</Left>
                        <Height>1in</Height>
                        <Width>2in</Width>
                      </Tablix>
                    </ReportItems>
                    <Height>1in</Height>
                  </Body>
                  <Width>6.5in</Width>
                  <Page>
                    <PageHeight>11in</PageHeight>
                    <PageWidth>8.5in</PageWidth>
                    <LeftMargin>1in</LeftMargin>
                    <RightMargin>1in</RightMargin>
                    <TopMargin>1in</TopMargin>
                    <BottomMargin>1in</BottomMargin>
                  </Page>
                </ReportSection>
              </ReportSections>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var tablix = Assert.IsType<TablixItem>(Assert.Single(report.Sections[0].BodyItems));
        var rowMember = Assert.Single(tablix.RowMembers);
        Assert.Equal(ReportTablixMemberKind.Group, rowMember.Kind);
        Assert.Equal("CategoryGroup", rowMember.GroupName);
        Assert.Equal("Fields.Category", rowMember.GroupExpression);
        Assert.Equal(0, rowMember.RowDefinitionIndex);
        Assert.Single(tablix.Rows);
        Assert.Equal(24f, tablix.Rows[0].Height, 3);
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains("Grouped tablix hierarchies", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_EmitsGroupedTablixHierarchy()
    {
        var report = new ReportDefinition
        {
            Id = "grouped-export",
            Name = "Grouped Export",
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
                            Id = "SalesTable",
                            DataSetId = "Sales",
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "col1", Width = 144f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "header",
                                    IsHeader = true,
                                    Height = 24f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { Text = "Category" }
                                    }
                                },
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Height = 20f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Category" }
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
                                    KeepWithGroup = "After",
                                    RowDefinitionIndex = 0
                                },
                                new ReportTablixMemberDefinition
                                {
                                    Id = "category-group",
                                    Kind = ReportTablixMemberKind.Group,
                                    GroupName = "CategoryGroup",
                                    GroupExpression = "Fields.Category",
                                    SortExpression = "Fields.Category",
                                    VisibilityExpression = "Parameters.ShowGroups == false",
                                    ToggleItemId = "CategoryCell",
                                    Members =
                                    {
                                        new ReportTablixMemberDefinition
                                        {
                                            Id = "detail-member",
                                            Kind = ReportTablixMemberKind.Details,
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

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains("<Group Name=\"CategoryGroup\">", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<GroupExpression>=Fields!Category.Value</GroupExpression>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<RepeatOnNewPage>true</RepeatOnNewPage>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<KeepWithGroup>After</KeepWithGroup>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<ToggleItem>CategoryCell</ToggleItem>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<Height>0.25in</Height>", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ReportsUnsupportedItemsAsDiagnostics()
    {
        var report = CreateSampleReport(includeUnsupportedItems: true);

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains("Document template item", StringComparison.Ordinal));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains("Internal visibility", StringComparison.Ordinal));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains("Shape 'Ellipse'", StringComparison.Ordinal));
        Assert.DoesNotContain("<DocumentTemplateItem", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsValidVbStringLiteralsAndEscapesStaticEqualsText()
    {
        var report = new ReportDefinition
        {
            Id = "expression-report",
            Name = "Expression Report",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'West'"
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
                            Id = "expr",
                            Name = "Expression",
                            Bounds = new ReportItemBounds(0f, 0f, 120f, 24f),
                            ValueExpression = "Parameters.Region != 'West' && true"
                        },
                        new TextItem
                        {
                            Id = "static-equals",
                            Name = "Static Equals",
                            Bounds = new ReportItemBounds(0f, 30f, 120f, 24f),
                            StaticText = "=literal"
                        }
                    }
                }
            }
        };

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains("<Value>=\"West\"</Value>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Parameters!Region.Value", result.Xml, StringComparison.Ordinal);
        Assert.Contains("&lt;&gt;", result.Xml, StringComparison.Ordinal);
        Assert.Contains("\"West\"", result.Xml, StringComparison.Ordinal);
        Assert.Contains("AndAlso", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<Value>=\"=literal\"</Value>", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("='West'", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ReexportsParameterLabelsAndCurrentValueExpressions()
    {
        var report = new ReportDefinition
        {
            Id = "parameter-label-export",
            Name = "Parameter Label Export",
            Styles =
            {
                new ReportStyleDefinition
                {
                    Id = "variance-style",
                    ForegroundExpression = "Iif(CurrentValue() < 0, '#c0433a', 'Black')"
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
                            Bounds = new ReportItemBounds(0f, 0f, 200f, 20f),
                            ValueExpression = "'Organization: ' + ParameterLabel('OrganizationKey') + ' | Department: ' + ParameterLabel('DepartmentGroupKey')"
                        },
                        new TextItem
                        {
                            Id = "variance",
                            Name = "Variance",
                            Bounds = new ReportItemBounds(0f, 24f, 120f, 20f),
                            ValueExpression = "Code.SalesVariancePct(Sum(Fields.Sales), Sum(Fields.Quota))",
                            StyleName = "variance-style"
                        }
                    }
                }
            }
        };

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains("Parameters!OrganizationKey.Label", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Parameters!DepartmentGroupKey.Label", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Me.Value", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_UsesUtf8Payload()
    {
        var report = CreateSampleReport(includeUnsupportedItems: false);
        await using var stream = new MemoryStream();

        var result = await _serializer.WriteAsync(report, stream);

        Assert.False(result.HasErrors);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = await reader.ReadToEndAsync();
        Assert.Equal(result.Xml, xml);
    }

    [Fact]
    public void Read_ImportsParagraphTextAlignmentIntoStyle()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>alignment-report</rd:ReportID>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Textbox Name="Aligned">
                        <Paragraphs>
                          <Paragraph>
                            <TextRuns>
                              <TextRun>
                                <Value>Right aligned</Value>
                              </TextRun>
                            </TextRuns>
                            <Style>
                              <TextAlign>Right</TextAlign>
                            </Style>
                          </Paragraph>
                        </Paragraphs>
                        <Top>0in</Top>
                        <Left>0in</Left>
                        <Height>0.25in</Height>
                        <Width>2in</Width>
                      </Textbox>
                    </ReportItems>
                    <Height>1in</Height>
                  </Body>
                  <Width>6.5in</Width>
                  <Page>
                    <PageHeight>11in</PageHeight>
                    <PageWidth>8.5in</PageWidth>
                    <LeftMargin>1in</LeftMargin>
                    <RightMargin>1in</RightMargin>
                    <TopMargin>1in</TopMargin>
                    <BottomMargin>1in</BottomMargin>
                  </Page>
                </ReportSection>
              </ReportSections>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var textbox = Assert.IsType<TextItem>(Assert.Single(report.Sections[0].BodyItems));
        var style = Assert.Single(report.Styles);
        Assert.Equal(style.Id, textbox.StyleName);
        Assert.Equal(ParagraphAlignment.Right, style.TextAlign);
    }

    private static ReportDefinition CreateSampleReport(bool includeUnsupportedItems)
    {
        var report = new ReportDefinition
        {
            Id = "sales-report",
            Name = "Sales Report"
        };

        report.Parameters.Add(new ReportParameterDefinition
        {
            Id = "Region",
            DisplayName = "Region",
            DataType = ReportParameterDataType.String,
            Prompt = "Region",
            DefaultValueExpression = "'West'",
            Visibility = includeUnsupportedItems ? ReportParameterVisibility.Internal : ReportParameterVisibility.Visible
        });

        var dataSource = new ReportDataSourceDefinition
        {
            Id = "SalesSource",
            ProviderId = "SQL"
        };
        dataSource.Options["connectionString"] = "Server=(local);Database=Sales";
        report.DataSources.Add(dataSource);

        var dataSet = new ReportDataSetDefinition
        {
            Id = "SalesData",
            DataSourceId = dataSource.Id,
            Query = "select Region, Amount from Sales where Region = @Region"
        };
        dataSet.Parameters.Add(new ReportDataSetParameterDefinition
        {
            Name = "@Region",
            ValueExpression = "Parameters.Region"
        });
        dataSet.ExpectedFields.Add(new ReportFieldDefinition { Name = "Region" });
        dataSet.ExpectedFields.Add(new ReportFieldDefinition { Name = "Amount", DataType = ReportParameterDataType.Decimal });
        report.DataSets.Add(dataSet);

        report.Styles.Add(new ReportStyleDefinition
        {
            Id = "title-style",
            FontFamily = "Aptos",
            FontSize = 20f,
            Foreground = "#204060",
            Bold = true
        });

        var section = new ReportSection
        {
            Id = "main",
            Name = "Main"
        };
        section.PageSettings.Width = 816f;
        section.PageSettings.Height = 1056f;
        section.PageSettings.MarginLeft = 96f;
        section.PageSettings.MarginRight = 96f;
        section.PageSettings.MarginTop = 72f;
        section.PageSettings.MarginBottom = 72f;

        section.HeaderItems.Add(new TextItem
        {
            Id = "page-number",
            Name = "Page Number",
            Bounds = new ReportItemBounds(0f, 0f, 80f, 24f),
            ValueExpression = "Globals.PageNumber"
        });
        section.FooterItems.Add(new TextItem
        {
            Id = "total-pages",
            Name = "Total Pages",
            Bounds = new ReportItemBounds(0f, 0f, 80f, 24f),
            ValueExpression = "Globals.TotalPages"
        });

        section.BodyItems.Add(new TextItem
        {
            Id = "title",
            Name = "Title",
            Bounds = new ReportItemBounds(36f, 24f, 280f, 32f),
            ValueExpression = "Parameters.Region",
            StyleName = "title-style",
            FormatString = "C2",
            BookmarkExpression = "'sales-title'"
        });

        section.BodyItems.Add(new ImageItem
        {
            Id = "logo",
            Name = "Logo",
            Bounds = new ReportItemBounds(340f, 18f, 96f, 48f),
            SourceKind = ReportImageSourceKind.Embedded,
            EmbeddedData = [0x01, 0x02, 0x03, 0x04],
            MimeType = "image/png",
            SizingMode = ReportSizingMode.FitProportional
        });

        section.BodyItems.Add(new ChartItem
        {
            Id = "sales-chart",
            Name = "Sales Chart",
            Bounds = new ReportItemBounds(36f, 84f, 360f, 180f),
            DataSetId = "SalesData",
            CategoryExpression = "Fields.Region",
            TitleExpression = "'Sales by Region'",
            Series =
            {
                new ReportChartSeriesDefinition
                {
                    NameExpression = "'Revenue'",
                    ValueExpression = "Fields.Amount",
                    ColorExpression = "'#336699'"
                }
            }
        });

        section.BodyItems.Add(new TablixItem
        {
            Id = "sales-table",
            Name = "Sales Table",
            Bounds = new ReportItemBounds(36f, 282f, 400f, 120f),
            DataSetId = "SalesData",
            RepeatHeaderRows = true,
            Columns =
            {
                new ReportTablixColumnDefinition { Id = "region", Width = 200f },
                new ReportTablixColumnDefinition { Id = "amount", Width = 200f }
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
                    IsHeader = false,
                    Cells =
                    {
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Amount", FormatString = "C2" }
                    }
                }
            }
        });

        section.BodyItems.Add(new SubreportItem
        {
            Id = "detail-report",
            Name = "Detail Report",
            Bounds = new ReportItemBounds(36f, 420f, 320f, 80f),
            ReportReferenceId = "sales-detail",
            Parameters =
            {
                new ReportParameterBinding
                {
                    ParameterId = "Region",
                    ValueExpression = "Parameters.Region"
                }
            }
        });

        section.BodyItems.Add(new LineItem
        {
            Id = "divider",
            Name = "Divider",
            Bounds = new ReportItemBounds(36f, 520f, 360f, 0f),
            X2 = 396f,
            Y2 = 520f
        });

        section.BodyItems.Add(new ShapeItem
        {
            Id = "summary-box",
            Name = "Summary Box",
            Bounds = new ReportItemBounds(36f, 540f, 360f, 72f),
            Shape = includeUnsupportedItems ? ReportShapeKind.Ellipse : ReportShapeKind.Rectangle
        });

        if (includeUnsupportedItems)
        {
            section.BodyItems.Add(new DocumentTemplateItem
            {
                Id = "narrative",
                Name = "Narrative",
                Bounds = new ReportItemBounds(36f, 624f, 320f, 96f),
                EmbeddedContent = "# Not supported in RDL"
            });
        }

        report.Sections.Add(section);
        return report;
    }
}
