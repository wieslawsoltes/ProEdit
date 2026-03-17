using Vibe.Office.Documents;
using Vibe.Office.OpenXml;
using Vibe.Office.Reporting.DocumentComposition;
using Xunit;

namespace Vibe.Office.Reporting.DocumentComposition.Tests;

public sealed class ReportDocumentComposerTests
{
    [Fact]
    public async Task ComposeAsync_CreatesHeadersPageFieldsTablesAndCharts()
    {
        var report = new MaterializedReport
        {
            Id = "sales",
            Name = "Sales"
        };

        var section = new MaterializedReportSection
        {
            Id = "main",
            Name = "Main",
            Bookmark = "main-section",
            PageSettings = new ReportPageSettings
            {
                Width = 800f,
                Height = 1100f,
                MarginLeft = 50f,
                MarginTop = 60f,
                MarginRight = 50f,
                MarginBottom = 60f
            }
        };

        section.HeaderItems.Add(new MaterializedTextReportItem { Text = "Page", ValueKind = MaterializedTextValueKind.Static });
        section.HeaderItems.Add(new MaterializedTextReportItem { ValueKind = MaterializedTextValueKind.PageNumber });
        section.HeaderItems.Add(new MaterializedTextReportItem { Text = "of", ValueKind = MaterializedTextValueKind.Static });
        section.HeaderItems.Add(new MaterializedTextReportItem { ValueKind = MaterializedTextValueKind.TotalPages });

        section.BodyItems.Add(new MaterializedTextReportItem
        {
            SourceItemId = "overview",
            Text = "Overview",
            Bookmark = "overview",
            Style = new MaterializedReportStyle
            {
                Bold = true,
                FontSize = 18f
            }
        });

        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = "table",
            RepeatHeaderRows = true
        };
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col1", Width = 100f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col2", Width = 100f });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell { Text = "Region" },
                new MaterializedTablixCell { Text = "Amount" }
            }
        });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell { Text = "West" },
                new MaterializedTablixCell { Text = "10.00" }
            }
        });
        section.BodyItems.Add(tablix);

        var chart = new MaterializedChartReportItem
        {
            SourceItemId = "chart",
            Bounds = new ReportItemBounds(0f, 0f, 200f, 120f),
            Model = new ChartModel
            {
                Title = "Sales"
            }
        };
        chart.Model.Series.Add(new ChartSeries
        {
            Name = "Amount",
            Points =
            {
                new ChartPoint { Category = "West", Value = 10d }
            }
        });
        section.BodyItems.Add(chart);

        report.Sections.Add(section);

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Sections);
        Assert.Equal(4, result.Document.Header.Blocks.Count);
        Assert.Contains(result.Document.Header.Blocks.OfType<ParagraphBlock>().SelectMany(static paragraph => paragraph.Inlines), inline => inline is PageNumberInline);
        Assert.Contains(result.Document.Header.Blocks.OfType<ParagraphBlock>().SelectMany(static paragraph => paragraph.Inlines), inline => inline is TotalPagesInline);
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && paragraph.Inlines.OfType<BookmarkStartInline>().Any(static bookmark => bookmark.Name == "main-section"));
        Assert.Contains(result.Document.Blocks, block => block is TableBlock);
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && paragraph.Inlines.Any(static inline => inline is BookmarkStartInline));
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && paragraph.Inlines.Any(static inline => inline is ChartInline));
    }

    [Fact]
    public async Task ComposeAsync_BindsTemplateContentAndAddsSectionBreaks()
    {
        var nestedReport = new MaterializedReport
        {
            Id = "nested",
            Name = "Nested",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "nested-section",
                    Name = "Nested Section",
                    BodyItems =
                    {
                        new MaterializedTextReportItem
                        {
                            Text = "Nested"
                        }
                    }
                }
            }
        };

        var report = new MaterializedReport
        {
            Id = "main",
            Name = "Main",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "first",
                    Name = "First",
                    BodyItems =
                    {
                        new MaterializedDocumentTemplateReportItem
                        {
                            SourceItemId = "template",
                            TemplateFormat = ReportDocumentTemplateFormat.Vibe,
                            Content = "Hello {{Name}}",
                            Bindings =
                            {
                                ["Name"] = "Contoso"
                            }
                        }
                    }
                },
                new MaterializedReportSection
                {
                    Id = "second",
                    Name = "Second",
                    BodyItems =
                    {
                        new MaterializedSubreportReportItem
                        {
                            SourceItemId = "subreport",
                            Report = nestedReport
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Document!.Sections.Count);
        Assert.Contains(result.Document.Blocks, block => block is SectionBreakBlock);

        var text = GetDocumentText(result.Document);
        Assert.Contains("Hello Contoso", text);
        Assert.Contains("Nested", text);
    }

    [Fact]
    public async Task ComposeAsync_ImportsTemplateCustomXmlBindingsIntoTargetDocument()
    {
        var sourceTemplate = new Document();
        sourceTemplate.Blocks.Clear();
        sourceTemplate.CustomXmlParts["template-store"] = System.Xml.Linq.XDocument.Parse("<root><Name>Original</Name></root>");

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ContentControlStartInline(new ContentControlProperties
        {
            Alias = "Name",
            DataBinding = new ContentControlDataBinding
            {
                StoreItemId = "template-store",
                XPath = "/root/Name"
            }
        }));
        paragraph.Inlines.Add(new RunInline("Placeholder"));
        paragraph.Inlines.Add(new ContentControlEndInline(id: null));
        sourceTemplate.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        new DocxExporter().Save(sourceTemplate, stream);

        var report = new MaterializedReport
        {
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedDocumentTemplateReportItem
                        {
                            SourceItemId = "template",
                            TemplateFormat = ReportDocumentTemplateFormat.Docx,
                            Content = Convert.ToBase64String(stream.ToArray()),
                            Bindings =
                            {
                                ["Name"] = "Alice"
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Contains("template-store", result.Document!.CustomXmlParts.Keys);

        var importedParagraph = Assert.IsType<ParagraphBlock>(result.Document.Blocks[0]);
        var controlStart = Assert.IsType<ContentControlStartInline>(importedParagraph.Inlines[0]);
        Assert.Equal("Alice", ContentControlValueResolver.ResolveContentControlValue(controlStart.Properties, result.Document));
    }

    private static string GetDocumentText(Document document)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlockText(block, builder);
        }

        return builder.ToString();
    }

    private static void AppendBlockText(Block block, System.Text.StringBuilder builder)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    builder.AppendLine(paragraph.Text);
                }

                foreach (var run in paragraph.Inlines.OfType<RunInline>())
                {
                    builder.AppendLine(run.GetText());
                }

                break;
            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var cellBlock in cell.Blocks)
                        {
                            AppendBlockText(cellBlock, builder);
                        }
                    }
                }

                break;
        }
    }
}
