using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProEdit.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Layout;
using ProEdit.Reporting.DocumentComposition;
using ProEdit.Reporting.Export;
using Xunit;
using SimpleField = DocumentFormat.OpenXml.Wordprocessing.SimpleField;
using WordprocessingDocument = DocumentFormat.OpenXml.Packaging.WordprocessingDocument;

namespace ProEdit.Reporting.Export.Tests;

public sealed class ReportExporterTests
{
    [Fact]
    public async Task ExportAsync_ExportsComposedDocxWithHeaderFieldsAndChart()
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = ReportExportFormat.Docx,
            ExecutionResult = new ReportExecutionResult
            {
                MaterializedReport = CreateMaterializedReport(includeSecondTablix: false)
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.False(result.HasErrors);
        Assert.Equal(".docx", result.FileExtension);
        Assert.True(result.BytesWritten > 0);

        stream.Position = 0;
        using var wordDocument = WordprocessingDocument.Open(stream, false);
        var headerFields = wordDocument.MainDocumentPart!
            .HeaderParts
            .SelectMany(static part => part.Header!.Descendants<SimpleField>())
            .Select(field => field.Instruction?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(headerFields, static instruction => instruction.Contains("PAGE", StringComparison.Ordinal));
        Assert.Contains(headerFields, static instruction => instruction.Contains("NUMPAGES", StringComparison.Ordinal));
        Assert.True(wordDocument.MainDocumentPart.ChartParts.Any());
        Assert.Contains(
            "Overview",
            wordDocument.MainDocumentPart.Document!.Body!.InnerText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReportExportFormat.Html, "<html")]
    [InlineData(ReportExportFormat.Rtf, "{\\rtf")]
    [InlineData(ReportExportFormat.Markdown, "Overview")]
    public async Task ExportAsync_ExportsTextDocumentFormats(
        ReportExportFormat format,
        string expectedMarker)
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = format,
            ExecutionResult = new ReportExecutionResult
            {
                MaterializedReport = CreateMaterializedReport(includeSecondTablix: false)
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.False(result.HasErrors);
        stream.Position = 0;
        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains(expectedMarker, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Overview", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_ExportsPdfFromDocument()
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = ReportExportFormat.Pdf,
            ExecutionResult = new ReportExecutionResult
            {
                Document = CreateDocument()
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.False(result.HasErrors);
        Assert.Equal("application/pdf", result.MediaType);
        Assert.True(stream.Length > 32);

        var header = Encoding.ASCII.GetString(stream.ToArray(), 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public async Task ExportAsync_UsesInjectedXpsAndPostScriptServices()
    {
        var xpsService = new TestXpsDocumentConversionService();
        var postScriptService = new TestPostScriptDocumentConversionService();
        var exporter = new ReportExporter(new ReportDocumentComposer(), xpsService, postScriptService);
        var profile = new PaginatedReportExportProfile
        {
            LayoutSettings = new LayoutSettings
            {
                PageWidth = 900f,
                PageHeight = 1200f
            }
        };

        using var xpsStream = new MemoryStream();
        var xpsResult = await exporter.ExportAsync(
            new ReportExportRequest
            {
                Format = ReportExportFormat.Xps,
                Profile = profile,
                ExecutionResult = new ReportExecutionResult
                {
                    Document = CreateDocument(includeSectionPageSettings: false)
                }
            },
            xpsStream);

        using var psStream = new MemoryStream();
        var psResult = await exporter.ExportAsync(
            new ReportExportRequest
            {
                Format = ReportExportFormat.Ps,
                Profile = profile,
                ExecutionResult = new ReportExecutionResult
                {
                    Document = CreateDocument(includeSectionPageSettings: false)
                }
            },
            psStream);

        Assert.False(xpsResult.HasErrors);
        Assert.False(psResult.HasErrors);
        Assert.Equal(1, xpsService.SaveCalls);
        Assert.Equal(1, postScriptService.SaveCalls);
        Assert.Equal(XpsFlavor.Xps, xpsService.LastFlavor);
        Assert.Equal(PostScriptKind.Ps, postScriptService.LastKind);
        Assert.Equal(900f, xpsService.LastLayoutSettings!.PageWidth);
        Assert.Equal(1200f, postScriptService.LastLayoutSettings!.PageHeight);
        Assert.Equal("XPS", Encoding.ASCII.GetString(xpsStream.ToArray()));
        Assert.Equal("PS", Encoding.ASCII.GetString(psStream.ToArray()));
    }

    [Fact]
    public async Task ExportAsync_ExportsCsvForSelectedTablix()
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = ReportExportFormat.Csv,
            Profile = new CsvReportExportProfile
            {
                TablixItemId = "orders-table"
            },
            ExecutionResult = new ReportExecutionResult
            {
                MaterializedReport = CreateMaterializedReport(includeSecondTablix: true)
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.False(result.HasErrors);
        var csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("Order", csv, StringComparison.Ordinal);
        Assert.Contains("\"Widget, \"\"Deluxe\"\"\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Region", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_RequiresCsvTablixSelectionWhenMultipleTablixesExist()
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = ReportExportFormat.Csv,
            ExecutionResult = new ReportExecutionResult
            {
                MaterializedReport = CreateMaterializedReport(includeSecondTablix: true)
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.ExportTablixSelectionRequired);
    }

    [Fact]
    public async Task ExportAsync_CsvPreservesTablixSpansAsPaddedColumns()
    {
        var exporter = CreateExporter();
        var report = CreateSpanAwareReport();

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(
            new ReportExportRequest
            {
                Format = ReportExportFormat.Csv,
                Profile = new CsvReportExportProfile
                {
                    TablixItemId = "spans-table"
                },
                ExecutionResult = new ReportExecutionResult
                {
                    MaterializedReport = report
                }
            },
            stream);

        Assert.False(result.HasErrors);
        var csv = Encoding.UTF8.GetString(stream.ToArray());
        var lines = csv.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Merged,", lines[0]);
        Assert.Equal("A,B1", lines[1]);
        Assert.Equal(",B2", lines[2]);
    }

    [Fact]
    public async Task ExportAsync_ExportsAllTablixesToXlsx()
    {
        var exporter = CreateExporter();
        var request = new ReportExportRequest
        {
            Format = ReportExportFormat.Xlsx,
            ExecutionResult = new ReportExecutionResult
            {
                MaterializedReport = CreateMaterializedReport(includeSecondTablix: true)
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(request, stream);

        Assert.False(result.HasErrors);
        Assert.Equal(".xlsx", result.FileExtension);

        stream.Position = 0;
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);
        var workbookPart = spreadsheet.WorkbookPart!;
        var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheets = workbook.Sheets!.Elements<Sheet>().ToList();

        Assert.Equal(2, sheets.Count);
        Assert.Contains(sheets, static sheet => sheet.Name!.Value == "Sales Table");
        Assert.Contains(sheets, static sheet => sheet.Name!.Value == "Orders Table");

        var firstSheetId = sheets[0].Id?.Value ?? throw new InvalidOperationException("First worksheet id is missing.");
        var secondSheetId = sheets[1].Id?.Value ?? throw new InvalidOperationException("Second worksheet id is missing.");
        var firstWorksheetPart = (WorksheetPart)workbookPart.GetPartById(firstSheetId);
        var secondWorksheetPart = (WorksheetPart)workbookPart.GetPartById(secondSheetId);
        Assert.Equal("Region", GetCellText(firstWorksheetPart.Worksheet!, "A1"));
        Assert.Equal("West", GetCellText(firstWorksheetPart.Worksheet!, "A2"));
        Assert.Equal("Widget, \"Deluxe\"", GetCellText(secondWorksheetPart.Worksheet!, "B2"));
    }

    [Fact]
    public async Task ExportAsync_XlsxPreservesTablixSpansWithMergedCells()
    {
        var exporter = CreateExporter();
        var report = CreateSpanAwareReport();

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(
            new ReportExportRequest
            {
                Format = ReportExportFormat.Xlsx,
                Profile = new XlsxReportExportProfile
                {
                    TablixItemId = "spans-table"
                },
                ExecutionResult = new ReportExecutionResult
                {
                    MaterializedReport = report
                }
            },
            stream);

        Assert.False(result.HasErrors);

        stream.Position = 0;
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);
        var workbookPart = spreadsheet.WorkbookPart!;
        var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheet = workbook.Sheets!.Elements<Sheet>().Single();
        var sheetId = sheet.Id?.Value ?? throw new InvalidOperationException("Worksheet id is missing.");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);
        var worksheet = worksheetPart.Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
        var mergeReferences = worksheet.Elements<MergeCells>()
            .SelectMany(static group => group.Elements<MergeCell>())
            .Select(merge => merge.Reference?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        Assert.Equal("Merged", GetCellText(worksheet, "A1"));
        Assert.Equal("A", GetCellText(worksheet, "A2"));
        Assert.Equal("B2", GetCellText(worksheet, "B3"));
        Assert.Contains("A1:B1", mergeReferences);
        Assert.Contains("A2:A3", mergeReferences);
    }

    [Fact]
    public async Task ExportAsync_GeneratesUniqueWorksheetNamesForLongDuplicateTablixNames()
    {
        var exporter = CreateExporter();
        var report = new MaterializedReport
        {
            Id = "duplicate-sheets",
            Name = "Duplicate Sheets",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        CreateNamedTablix("tables-1", "Very Long Tablix Name For Quarterly Sales Detail"),
                        CreateNamedTablix("tables-2", "Very Long Tablix Name For Quarterly Sales Detail")
                    }
                }
            }
        };

        using var stream = new MemoryStream();
        var result = await exporter.ExportAsync(
            new ReportExportRequest
            {
                Format = ReportExportFormat.Xlsx,
                ExecutionResult = new ReportExecutionResult
                {
                    MaterializedReport = report
                }
            },
            stream);

        Assert.False(result.HasErrors);

        stream.Position = 0;
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);
        var workbook = spreadsheet.WorkbookPart!.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheetNames = workbook.Sheets!.Elements<Sheet>().Select(sheet => sheet.Name!.Value).ToList();

        Assert.Equal(2, sheetNames.Count);
        Assert.Equal(2, sheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(sheetNames, static name => Assert.True(name!.Length <= 31));
    }

    private static ReportExporter CreateExporter()
    {
        return new ReportExporter(
            new ReportDocumentComposer(),
            new TestXpsDocumentConversionService(),
            new TestPostScriptDocumentConversionService());
    }

    private static MaterializedReport CreateMaterializedReport(bool includeSecondTablix)
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
            PageSettings = new ReportPageSettings
            {
                Width = 816f,
                Height = 1056f,
                MarginLeft = 72f,
                MarginTop = 72f,
                MarginRight = 72f,
                MarginBottom = 72f
            }
        };

        section.HeaderItems.Add(new MaterializedTextReportItem
        {
            Text = "Page"
        });
        section.HeaderItems.Add(new MaterializedTextReportItem
        {
            ValueKind = MaterializedTextValueKind.PageNumber
        });
        section.HeaderItems.Add(new MaterializedTextReportItem
        {
            Text = "of"
        });
        section.HeaderItems.Add(new MaterializedTextReportItem
        {
            ValueKind = MaterializedTextValueKind.TotalPages
        });

        section.BodyItems.Add(new MaterializedTextReportItem
        {
            SourceItemId = "title",
            Name = "Title",
            Text = "Overview",
            Style = new MaterializedReportStyle
            {
                Bold = true,
                FontSize = 18f
            }
        });

        var salesTable = new MaterializedTablixReportItem
        {
            SourceItemId = "sales-table",
            Name = "Sales Table",
            RepeatHeaderRows = true
        };
        salesTable.Columns.Add(new MaterializedTablixColumn { Id = "region", Width = 120f });
        salesTable.Columns.Add(new MaterializedTablixColumn { Id = "amount", Width = 100f });
        salesTable.Rows.Add(new MaterializedTablixRow
        {
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell { Text = "Region" },
                new MaterializedTablixCell { Text = "Amount" }
            }
        });
        salesTable.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell { Text = "West" },
                new MaterializedTablixCell { Text = "10.00" }
            }
        });
        section.BodyItems.Add(salesTable);

        var chart = new MaterializedChartReportItem
        {
            SourceItemId = "sales-chart",
            Name = "Sales Chart",
            Bounds = new ReportItemBounds(0f, 0f, 240f, 140f),
            Model = new ChartModel
            {
                Title = "Sales by Region",
                Series =
                {
                    new ChartSeries
                    {
                        Name = "Amount",
                        Points =
                        {
                            new ChartPoint { Category = "West", Value = 10d }
                        }
                    }
                }
            }
        };
        section.BodyItems.Add(chart);

        if (includeSecondTablix)
        {
            var ordersTable = new MaterializedTablixReportItem
            {
                SourceItemId = "orders-table",
                Name = "Orders Table"
            };
            ordersTable.Columns.Add(new MaterializedTablixColumn { Id = "order", Width = 80f });
            ordersTable.Columns.Add(new MaterializedTablixColumn { Id = "product", Width = 180f });
            ordersTable.Rows.Add(new MaterializedTablixRow
            {
                IsHeader = true,
                Cells =
                {
                    new MaterializedTablixCell { Text = "Order" },
                    new MaterializedTablixCell { Text = "Product" }
                }
            });
            ordersTable.Rows.Add(new MaterializedTablixRow
            {
                Cells =
                {
                    new MaterializedTablixCell { Text = "1001" },
                    new MaterializedTablixCell { Text = "Widget, \"Deluxe\"" }
                }
            });
            section.BodyItems.Add(ordersTable);
        }

        report.Sections.Add(section);
        return report;
    }

    private static MaterializedReport CreateSpanAwareReport()
    {
        var report = new MaterializedReport
        {
            Id = "spans",
            Name = "Spans"
        };

        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = "spans-table",
            Name = "Spans Table"
        };
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col1", Width = 100f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col2", Width = 100f });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell
                {
                    Text = "Merged",
                    ColumnSpan = 2
                }
            }
        });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell
                {
                    Text = "A",
                    RowSpan = 2
                },
                new MaterializedTablixCell
                {
                    Text = "B1"
                }
            }
        });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell
                {
                    Text = "B2"
                }
            }
        });

        report.Sections.Add(new MaterializedReportSection
        {
            Id = "main",
            Name = "Main",
            BodyItems =
            {
                tablix
            }
        });

        return report;
    }

    private static Document CreateDocument(bool includeSectionPageSettings = true)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Export"));

        if (includeSectionPageSettings)
        {
            document.SectionProperties.PageWidth = 816f;
            document.SectionProperties.PageHeight = 1056f;
            document.SectionProperties.MarginLeft = 72f;
            document.SectionProperties.MarginTop = 72f;
            document.SectionProperties.MarginRight = 72f;
            document.SectionProperties.MarginBottom = 72f;
        }

        return document;
    }

    private static MaterializedTablixReportItem CreateNamedTablix(string sourceItemId, string name)
    {
        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = sourceItemId,
            Name = name
        };
        tablix.Rows.Add(new MaterializedTablixRow
        {
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell { Text = "Value" }
            }
        });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell { Text = sourceItemId }
            }
        });
        return tablix;
    }

    private static string GetCellText(Worksheet worksheet, string cellReference)
    {
        var cell = worksheet.Descendants<Cell>()
            .First(cell => string.Equals(cell.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
        return cell.InlineString?.Text?.Text ?? string.Empty;
    }

    private sealed class TestXpsDocumentConversionService : IXpsDocumentConversionService
    {
        public int SaveCalls { get; private set; }
        public LayoutSettings? LastLayoutSettings { get; private set; }
        public XpsFlavor LastFlavor { get; private set; }

        public Task<Document> LoadAsync(
            string path,
            XpsFlavor flavor,
            ProEdit.Pdf.PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Document> LoadAsync(
            Stream sourceStream,
            XpsFlavor flavor,
            ProEdit.Pdf.PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            string path,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            Stream targetStream,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastLayoutSettings = layoutSettings.Clone();
            LastFlavor = flavor;
            await targetStream.WriteAsync(Encoding.ASCII.GetBytes("XPS"), cancellationToken);
        }
    }

    private sealed class TestPostScriptDocumentConversionService : IPostScriptDocumentConversionService
    {
        public int SaveCalls { get; private set; }
        public LayoutSettings? LastLayoutSettings { get; private set; }
        public PostScriptKind LastKind { get; private set; }

        public Task<Document> LoadAsync(
            string path,
            PostScriptKind kind,
            ProEdit.Pdf.PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Document> LoadAsync(
            Stream sourceStream,
            PostScriptKind kind,
            ProEdit.Pdf.PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            string path,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            Stream targetStream,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastLayoutSettings = layoutSettings.Clone();
            LastKind = kind;
            await targetStream.WriteAsync(Encoding.ASCII.GetBytes("PS"), cancellationToken);
        }
    }
}
