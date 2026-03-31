using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Reporting.Rdl;
using Xunit;

namespace Vibe.Office.Reporting.Rdl.Tests;

public sealed class ReportRdlSerializerTests
{
    private static readonly string SampleCorpusPath = ResolveSampleCorpusPath();
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
    public void ReadAndWrite_RoundTripsReportParameterLayoutGrid()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>parameter-layout-report</rd:ReportID>
              <ReportParameters>
                <ReportParameter Name="CalendarYear">
                  <DataType>String</DataType>
                  <Prompt>Calendar year</Prompt>
                </ReportParameter>
                <ReportParameter Name="SalesTerritoryGroup">
                  <DataType>String</DataType>
                  <Prompt>Sales territory</Prompt>
                </ReportParameter>
              </ReportParameters>
              <ReportParametersLayout>
                <GridLayoutDefinition>
                  <NumberOfColumns>4</NumberOfColumns>
                  <NumberOfRows>2</NumberOfRows>
                  <CellDefinitions>
                    <CellDefinition>
                      <ColumnIndex>0</ColumnIndex>
                      <RowIndex>0</RowIndex>
                      <ParameterName>CalendarYear</ParameterName>
                    </CellDefinition>
                    <CellDefinition>
                      <ColumnIndex>0</ColumnIndex>
                      <RowIndex>1</RowIndex>
                      <ParameterName>SalesTerritoryGroup</ParameterName>
                    </CellDefinition>
                  </CellDefinitions>
                </GridLayoutDefinition>
              </ReportParametersLayout>
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

        var readResult = _serializer.Read(xml);

        Assert.False(readResult.HasErrors);
        var report = Assert.IsType<ReportDefinition>(readResult.ReportDefinition);
        Assert.Equal(4, report.ParameterLayout.ColumnCount);
        Assert.Equal(2, report.ParameterLayout.RowCount);
        Assert.Collection(
            report.ParameterLayout.Cells.OrderBy(static cell => cell.RowIndex).ThenBy(static cell => cell.ColumnIndex),
            cell =>
            {
                Assert.Equal("CalendarYear", cell.ParameterId);
                Assert.Equal(0, cell.RowIndex);
                Assert.Equal(0, cell.ColumnIndex);
            },
            cell =>
            {
                Assert.Equal("SalesTerritoryGroup", cell.ParameterId);
                Assert.Equal(1, cell.RowIndex);
                Assert.Equal(0, cell.ColumnIndex);
            });

        var writeResult = _serializer.Write(report);

        Assert.False(writeResult.HasErrors);
        Assert.Contains("<ReportParametersLayout>", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<NumberOfColumns>4</NumberOfColumns>", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<NumberOfRows>2</NumberOfRows>", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<ParameterName>CalendarYear</ParameterName>", writeResult.Xml, StringComparison.Ordinal);
        Assert.Contains("<ParameterName>SalesTerritoryGroup</ParameterName>", writeResult.Xml, StringComparison.Ordinal);
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
    public void Read_InvoiceInfersImplicitContainerAndCellBounds_WhenSampleAvailable()
    {
        var invoicePath = Path.Combine(ResolveSampleCorpusPath(), "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(invoicePath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        Assert.Equal("Segoe UI", report.DefaultFontFamily);
        var section = Assert.Single(report.Sections);
        Assert.Equal(0f, section.PageSettings.MarginTop);
        Assert.Equal(0f, section.PageSettings.MarginBottom);
        Assert.Equal(0f, section.PageSettings.MarginLeft);
        Assert.Equal(0f, section.PageSettings.MarginRight);
        Assert.True(section.PageSettings.HeaderHeight > 0f);
        Assert.True(section.PageSettings.FooterHeight > 0f);
        var outerTablix = Assert.IsType<TablixItem>(Assert.Single(section.BodyItems));
        var headerContainer = Assert.IsType<ContainerItem>(outerTablix.Rows[0].Cells[0].ContentItem);
        var detailContainer = Assert.IsType<ContainerItem>(outerTablix.Rows[1].Cells[0].ContentItem);
        var footerContainer = Assert.IsType<ContainerItem>(Assert.Single(section.FooterItems));

        Assert.True(headerContainer.Bounds.Width > 0f);
        Assert.True(headerContainer.Bounds.Height > 0f);
        Assert.True(detailContainer.Bounds.Width > 0f);
        Assert.True(detailContainer.Bounds.Height > 0f);
        Assert.True(footerContainer.Bounds.Width > 0f);
        Assert.True(footerContainer.Bounds.Height > 0f);

        var lineItemsTablix = Assert.IsType<TablixItem>(Assert.Single(detailContainer.Items.OfType<TablixItem>()));
        Assert.True(lineItemsTablix.Bounds.Width > 0f);
        Assert.True(lineItemsTablix.Bounds.Height > 0f);
        Assert.NotEmpty(lineItemsTablix.Columns);
        Assert.NotEmpty(lineItemsTablix.Rows);
        Assert.True(lineItemsTablix.Rows[0].IsHeader);
        Assert.False(lineItemsTablix.Rows[1].IsHeader);
        Assert.Equal(
            lineItemsTablix.Columns.Count,
            lineItemsTablix.Rows[0].Cells.Sum(static cell => Math.Max(1, cell.ColumnSpan)));
        Assert.Contains(lineItemsTablix.Rows[0].Cells, static cell => cell.ColumnSpan == 2);
        var detailCellStyle = Assert.Single(
            report.Styles,
            candidate => string.Equals(candidate.Id, lineItemsTablix.Rows[1].Cells[0].StyleName, StringComparison.Ordinal));
        Assert.Equal(
            "IIf(RowNumber(null) % 2 = 0, First(Fields.SecondaryColor, 'Company'), 'White')",
            detailCellStyle.BackgroundExpression);

        var salesInvoiceDataSet = Assert.Single(report.DataSets, static dataSet => string.Equals(dataSet.Id, "SalesInvoiceDS", StringComparison.Ordinal));
        Assert.Equal(
            ReportParameterDataType.String,
            Assert.Single(salesInvoiceDataSet.ExpectedFields, static field => string.Equals(field.Name, "InvoiceId", StringComparison.Ordinal)).DataType);
        Assert.Equal(
            ReportParameterDataType.Number,
            Assert.Single(salesInvoiceDataSet.ExpectedFields, static field => string.Equals(field.Name, "Amount", StringComparison.Ordinal)).DataType);

        var headerFooterDataSet = Assert.Single(report.DataSets, static dataSet => string.Equals(dataSet.Id, "SalesInvoiceHeaderFooterDS", StringComparison.Ordinal));
        Assert.Equal(
            ReportParameterDataType.String,
            Assert.Single(headerFooterDataSet.ExpectedFields, static field => string.Equals(field.Name, "InvoiceDate", StringComparison.Ordinal)).DataType);
    }

    [Fact]
    public void Read_ImportsOrganizationExpendituresShapeCharts_WhenSampleAvailable()
    {
        var organizationExpendituresPath = Path.Combine(ResolveSampleCorpusPath(), "OrganizationExpenditures.rdl");
        if (!File.Exists(organizationExpendituresPath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(organizationExpendituresPath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var section = Assert.Single(report.Sections);
        var charts = section.BodyItems
            .OfType<ChartItem>()
            .OrderBy(static chart => chart.Bounds.X)
            .ToList();

        Assert.Equal(2, charts.Count);

        var treemap = charts[0];
        Assert.Equal(ChartType.Treemap, treemap.Type);
        Assert.Equal("EarthTones", treemap.PaletteName);
        Assert.Single(treemap.CategoryLevels);
        Assert.True(treemap.Series[0].DataLabels?.ShowValue);
        Assert.Equal("#,0,;(#,0,)", treemap.Series[0].DataLabels?.NumberFormat);

        var sunburst = charts[1];
        Assert.Equal(ChartType.Sunburst, sunburst.Type);
        Assert.Equal("EarthTones", sunburst.PaletteName);
        Assert.Equal(3, sunburst.CategoryLevels.Count);
        Assert.True(sunburst.Series[0].DataLabels?.ShowValue);
        Assert.Equal("#,0,;(#,0,)", sunburst.Series[0].DataLabels?.NumberFormat);
    }

    [Fact]
    public void Read_CountrySalesPerformanceImportsEmbeddedMicrochartAxesAsHidden_WhenSampleAvailable()
    {
        var reportPath = Path.Combine(ResolveSampleCorpusPath(), "CountrySalesPerformance.rdl");
        if (!File.Exists(reportPath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(reportPath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var charts = EnumerateItems(report.Sections[0].BodyItems)
            .OfType<ChartItem>()
            .Where(static chart => chart.Id is "DataBar1" or "DataBar2" or "Sparkline1" or "Sparkline2" or "Sparkline3")
            .OrderBy(static chart => chart.Id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(5, charts.Count);
        Assert.All(charts, static chart =>
        {
            Assert.NotEmpty(chart.Axes);
            Assert.All(chart.Axes, static axis => Assert.False(axis.IsVisible));
        });

        var dataBar = Assert.Single(charts, static chart => chart.Id == "DataBar1");
        var valueAxis = Assert.Single(dataBar.Axes, static axis =>
            axis.Kind == ChartAxisKind.Value
            && axis.SyncMaximum
            && string.Equals(axis.SyncScopeName, "Tablix1", StringComparison.Ordinal));
        Assert.Equal(0d, valueAxis.Minimum);
        Assert.NotNull(valueAxis.MaximumExpression);
        Assert.Equal("Tablix1", valueAxis.SyncScopeName);
        Assert.True(valueAxis.SyncMaximum);
    }

    [Fact]
    public void Write_EmitsDefaultFontFamilyExtension_WhenPresent()
    {
        var report = new ReportDefinition
        {
            Id = "invoice",
            Name = "Invoice",
            DefaultFontFamily = "Segoe UI"
        };

        report.Sections.Add(new ReportSection
        {
            Id = "main",
            Name = "Main"
        });

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains("MustUnderstand=\"df\"", result.Xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:df=\"http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition/defaultfontfamily\"", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<df:DefaultFontFamily>Segoe UI</df:DefaultFontFamily>", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_PreservesWhitespaceOnlyTextboxRunsInsideCombinedExpressions()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Textbox Name="InvoiceReference">
                        <CanGrow>true</CanGrow>
                        <Paragraphs>
                          <Paragraph>
                            <TextRuns>
                              <TextRun><Value>Write reference</Value></TextRun>
                              <TextRun><Value xml:space="preserve"> </Value></TextRun>
                              <TextRun><Value>=Fields!InvoiceAccount.Value</Value></TextRun>
                              <TextRun><Value xml:space="preserve"> </Value></TextRun>
                              <TextRun><Value>on the check.</Value></TextRun>
                            </TextRuns>
                          </Paragraph>
                        </Paragraphs>
                        <Top>0in</Top>
                        <Left>0in</Left>
                        <Height>0.25in</Height>
                        <Width>4in</Width>
                      </Textbox>
                    </ReportItems>
                  </Body>
                  <Width>6.5in</Width>
                </ReportSection>
              </ReportSections>
            </Report>
            """;

        var result = _serializer.Read(xml);

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var textItem = Assert.IsType<TextItem>(Assert.Single(report.Sections[0].BodyItems));
        Assert.Equal("'Write reference' + ' ' + Fields.InvoiceAccount + ' ' + 'on the check.'", textItem.ValueExpression);
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
    public void Read_ImportsTablixFilters()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>tablix-filters</rd:ReportID>
              <DataSources>
                <DataSource Name="Donors">
                  <ConnectionProperties>
                    <DataProvider>ENTERDATA</DataProvider>
                    <ConnectString />
                  </ConnectionProperties>
                </DataSource>
              </DataSources>
              <DataSets>
                <DataSet Name="Donors">
                  <Query>
                    <DataSourceName>Donors</DataSourceName>
                    <CommandText>&lt;Query&gt;&lt;XmlData&gt;&lt;Data /&gt;&lt;/XmlData&gt;&lt;/Query&gt;</CommandText>
                  </Query>
                  <Fields>
                    <Field Name="Name">
                      <DataField>Name</DataField>
                    </Field>
                  </Fields>
                </DataSet>
              </DataSets>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Tablix Name="DonorTable">
                        <TablixBody>
                          <TablixColumns>
                            <TablixColumn>
                              <Width>1in</Width>
                            </TablixColumn>
                          </TablixColumns>
                          <TablixRows>
                            <TablixRow>
                              <Height>0.25in</Height>
                              <TablixCells>
                                <TablixCell>
                                  <CellContents>
                                    <Textbox Name="DonorName">
                                      <CanGrow>true</CanGrow>
                                      <Paragraphs>
                                        <Paragraph>
                                          <TextRuns>
                                            <TextRun>
                                              <Value>=Fields!Name.Value</Value>
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
                              <Group Name="Details" />
                            </TablixMember>
                          </TablixMembers>
                        </TablixRowHierarchy>
                        <DataSetName>Donors</DataSetName>
                        <Filters>
                          <Filter>
                            <FilterExpression>=Fields!Name.Value</FilterExpression>
                            <Operator>Equal</Operator>
                            <FilterValues>
                              <FilterValue>=Parameters!Donor.Value</FilterValue>
                            </FilterValues>
                          </Filter>
                        </Filters>
                      </Tablix>
                    </ReportItems>
                    <Height>1in</Height>
                  </Body>
                  <Width>2in</Width>
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
        var filter = Assert.Single(tablix.Filters);
        Assert.Equal("Fields.Name", filter.Expression);
        Assert.Equal(ReportFilterOperator.Equal, filter.Operator);
        Assert.Equal("Parameters.Donor", filter.ValueExpression);
    }

    [Fact]
    public void Write_EmitsTablixFilters()
    {
        var report = new ReportDefinition
        {
            Id = "tablix-filters-export",
            Name = "Tablix Filters Export",
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
                            Id = "DonorTable",
                            DataSetId = "Donors",
                            Columns =
                            {
                                new ReportTablixColumnDefinition { Id = "col1", Width = 96f }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "detail",
                                    Height = 24f,
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition { ValueExpression = "Fields.Name" }
                                    }
                                }
                            },
                            Filters =
                            {
                                new ReportFilterDefinition
                                {
                                    Expression = "Fields.Name",
                                    Operator = ReportFilterOperator.Equal,
                                    ValueExpression = "Parameters.Donor"
                                }
                            }
                        }
                    }
                }
            }
        };

        var result = _serializer.Write(report);

        Assert.False(result.HasErrors);
        Assert.Contains("<Filters>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<FilterExpression>=Fields!Name.Value</FilterExpression>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<Operator>Equal</Operator>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<FilterValue>=Parameters!Donor.Value</FilterValue>", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ImportsRichStylesZIndexAndPageBreaks()
    {
        const string xml =
            """
            <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
                    xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
              <rd:ReportID>styled-report</rd:ReportID>
              <ConsumeContainerWhitespace>true</ConsumeContainerWhitespace>
              <ReportSections>
                <ReportSection>
                  <Body>
                    <ReportItems>
                      <Textbox Name="StyledText">
                        <KeepTogether>true</KeepTogether>
                        <CanGrow>true</CanGrow>
                        <Paragraphs>
                          <Paragraph>
                            <TextRuns>
                              <TextRun>
                                <Value>Styled</Value>
                              </TextRun>
                            </TextRuns>
                          </Paragraph>
                        </Paragraphs>
                        <Top>0in</Top>
                        <Left>0in</Left>
                        <Height>0.4in</Height>
                        <Width>2in</Width>
                        <ZIndex>3</ZIndex>
                        <PageBreak>
                          <BreakLocation>End</BreakLocation>
                        </PageBreak>
                        <Style>
                          <BackgroundColor>White</BackgroundColor>
                          <BackgroundGradientType>DiagonalLeft</BackgroundGradientType>
                          <BackgroundGradientEndColor>Gray</BackgroundGradientEndColor>
                          <Border>
                            <Style>None</Style>
                          </Border>
                          <TopBorder>
                            <Color>Black</Color>
                            <Width>1pt</Width>
                          </TopBorder>
                          <PaddingLeft>4pt</PaddingLeft>
                          <PaddingRight>8pt</PaddingRight>
                          <PaddingTop>4pt</PaddingTop>
                          <PaddingBottom>2pt</PaddingBottom>
                          <VerticalAlign>Middle</VerticalAlign>
                          <TextDecoration>Underline</TextDecoration>
                        </Style>
                      </Textbox>
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
                                    <Textbox Name="RegionCell">
                                      <Paragraphs>
                                        <Paragraph>
                                          <TextRuns>
                                            <TextRun>
                                              <Value>=Fields!Region.Value</Value>
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
                              <Group Name="Details">
                                <GroupExpressions>
                                  <GroupExpression>=Fields!Region.Value</GroupExpression>
                                </GroupExpressions>
                                <PageBreak>
                                  <BreakLocation>Between</BreakLocation>
                                </PageBreak>
                              </Group>
                            </TablixMember>
                          </TablixMembers>
                        </TablixRowHierarchy>
                        <DataSetName>Sales</DataSetName>
                        <Top>1in</Top>
                        <Left>0in</Left>
                        <Height>1in</Height>
                        <Width>2in</Width>
                      </Tablix>
                    </ReportItems>
                    <Height>2in</Height>
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
        Assert.True(report.ConsumeContainerWhitespace);
        var styledText = Assert.IsType<TextItem>(report.Sections[0].BodyItems[0]);
        Assert.True(styledText.KeepTogether);
        Assert.Equal(3, styledText.ZIndex);
        Assert.Equal(ReportPageBreakLocation.End, styledText.PageBreak?.Location);
        var style = Assert.Single(report.Styles, candidate => candidate.Id == styledText.StyleName);
        Assert.Equal(ReportBackgroundGradientType.DiagonalLeft, style.BackgroundGradientType);
        Assert.Equal("Gray", style.BackgroundGradientEndColor);
        Assert.Equal(ReportBorderLineStyle.None, style.Border?.Style);
        Assert.Equal("Black", style.TopBorder?.Color);
        Assert.InRange(style.TopBorder?.Width ?? 0f, 1.32f, 1.35f);
        Assert.InRange(style.PaddingLeft ?? 0f, 5.32f, 5.35f);
        Assert.InRange(style.PaddingRight ?? 0f, 10.65f, 10.68f);
        Assert.Equal(ReportVerticalAlignment.Middle, style.VerticalAlign);
        Assert.Equal(ReportTextDecoration.Underline, style.TextDecoration);

        var tablix = Assert.IsType<TablixItem>(report.Sections[0].BodyItems[1]);
        Assert.Equal(ReportPageBreakLocation.Between, Assert.Single(tablix.RowMembers).PageBreak?.Location);
    }

    [Fact]
    public void Write_EmitsRichStylesZIndexAndPageBreaks()
    {
        var report = new ReportDefinition
        {
            Id = "styled-export",
            Name = "Styled Export",
            ConsumeContainerWhitespace = true,
            Styles =
            {
                new ReportStyleDefinition
                {
                    Id = "styled",
                    Background = "White",
                    BackgroundGradientType = ReportBackgroundGradientType.DiagonalLeft,
                    BackgroundGradientEndColor = "Gray",
                    Border = new ReportBorderDefinition
                    {
                        Style = ReportBorderLineStyle.None
                    },
                    TopBorder = new ReportBorderDefinition
                    {
                        Color = "Black",
                        Width = 1f
                    },
                    PaddingLeft = 4f,
                    PaddingRight = 8f,
                    PaddingTop = 4f,
                    PaddingBottom = 2f,
                    VerticalAlign = ReportVerticalAlignment.Middle,
                    TextDecoration = ReportTextDecoration.Underline
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
                            Id = "styled",
                            Name = "Styled",
                            Bounds = new ReportItemBounds(0f, 0f, 144f, 24f),
                            StaticText = "Styled",
                            StyleName = "styled",
                            KeepTogether = true,
                            ZIndex = 3,
                            PageBreak = new ReportPageBreakDefinition
                            {
                                Location = ReportPageBreakLocation.End
                            }
                        },
                        new TablixItem
                        {
                            Id = "table",
                            Name = "Table",
                            Bounds = new ReportItemBounds(0f, 72f, 144f, 24f),
                            Columns =
                            {
                                new ReportTablixColumnDefinition
                                {
                                    Id = "col1",
                                    Width = 144f
                                }
                            },
                            Rows =
                            {
                                new ReportTablixRowDefinition
                                {
                                    Id = "row1",
                                    Cells =
                                    {
                                        new ReportTablixCellDefinition
                                        {
                                            Text = "Region"
                                        }
                                    }
                                }
                            },
                            RowMembers =
                            {
                                new ReportTablixMemberDefinition
                                {
                                    Id = "group",
                                    Kind = ReportTablixMemberKind.Group,
                                    GroupName = "Details",
                                    GroupExpression = "Fields.Region",
                                    RowDefinitionIndex = 0,
                                    PageBreak = new ReportPageBreakDefinition
                                    {
                                        Location = ReportPageBreakLocation.Between
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
        Assert.Contains("<ConsumeContainerWhitespace>true</ConsumeContainerWhitespace>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<KeepTogether>true</KeepTogether>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<ZIndex>3</ZIndex>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<PageBreak>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<BreakLocation>End</BreakLocation>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<BackgroundGradientType>DiagonalLeft</BackgroundGradientType>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<BackgroundGradientEndColor>Gray</BackgroundGradientEndColor>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<TopBorder>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<PaddingLeft>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<VerticalAlign>Middle</VerticalAlign>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<TextDecoration>Underline</TextDecoration>", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<BreakLocation>Between</BreakLocation>", result.Xml, StringComparison.Ordinal);
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

    private static string ResolveSampleCorpusPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VibeOffice.slnx")))
            {
                return Path.Combine(directory.FullName, "external", "Reporting-Services", "PaginatedReportSamples");
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "external", "Reporting-Services", "PaginatedReportSamples");
    }

    [Fact]
    public void Read_InvoiceMergesCommonRunStyleAcrossMultipleTextRuns_WhenSampleAvailable()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(invoicePath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var invoiceTitle = Assert.IsType<TextItem>(
            EnumerateItems(report.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Textbox7", StringComparison.Ordinal)));
        Assert.False(string.IsNullOrWhiteSpace(invoiceTitle.StyleName));

        var style = Assert.Single(report.Styles, candidate => string.Equals(candidate.Id, invoiceTitle.StyleName, StringComparison.Ordinal));
        Assert.True(style.Bold);
        Assert.Equal(16f, style.FontSize);
        Assert.Equal("White", style.Foreground);
        Assert.Equal(ParagraphAlignment.Right, style.TextAlign);
    }

    [Fact]
    public void Read_InvoiceUsesRepresentativeRunStyleForMixedRuns_WhenSampleAvailable()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(invoicePath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var paymentInstructions = Assert.IsType<TextItem>(
            EnumerateItems(report.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Textbox40", StringComparison.Ordinal)));
        Assert.False(string.IsNullOrWhiteSpace(paymentInstructions.StyleName));

        var style = Assert.Single(report.Styles, candidate => string.Equals(candidate.Id, paymentInstructions.StyleName, StringComparison.Ordinal));
        Assert.True(style.FontSize.HasValue);
        Assert.InRange(style.FontSize.Value, 11f, 11.5f);
    }

    [Fact]
    public void Read_InvoicePreservesContainerBackgroundExpressions_WhenSampleAvailable()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var result = _serializer.Read(File.ReadAllText(invoicePath));

        Assert.False(result.HasErrors);
        var report = Assert.IsType<ReportDefinition>(result.ReportDefinition);
        var invoiceBanner = Assert.IsType<ContainerItem>(
            EnumerateItems(report.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Rectangle3", StringComparison.Ordinal)));
        var paymentPanel = Assert.IsType<ContainerItem>(
            EnumerateItems(report.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Rectangle5", StringComparison.Ordinal)));

        var bannerStyle = Assert.Single(report.Styles, candidate => string.Equals(candidate.Id, invoiceBanner.StyleName, StringComparison.Ordinal));
        var paymentStyle = Assert.Single(report.Styles, candidate => string.Equals(candidate.Id, paymentPanel.StyleName, StringComparison.Ordinal));

        Assert.Equal("First(Fields.PrimaryColor, 'Company')", bannerStyle.BackgroundExpression);
        Assert.Equal("First(Fields.SecondaryColor, 'Company')", paymentStyle.BackgroundExpression);
    }

    private static IEnumerable<ReportItem> EnumerateItems(IEnumerable<ReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;

            if (item is ContainerItem container)
            {
                foreach (var child in EnumerateItems(container.Items))
                {
                    yield return child;
                }
            }

            if (item is TablixItem tablix)
            {
                for (var rowIndex = 0; rowIndex < tablix.Rows.Count; rowIndex++)
                {
                    var row = tablix.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var content = row.Cells[cellIndex].ContentItem;
                        if (content is null)
                        {
                            continue;
                        }

                        foreach (var child in EnumerateItems([content]))
                        {
                            yield return child;
                        }
                    }
                }
            }
        }
    }

}
