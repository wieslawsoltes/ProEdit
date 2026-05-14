using System.Text;
using ProEdit.Documents;
using ProEdit.FlowDocument;
using ProEdit.FlowDocument.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Layout;
using ProEdit.Pdf;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class FlowDocumentFileConversionServiceTests
{
    [Fact]
    public async Task SaveAndLoadMarkdown_RetainsContent()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.md");
        var original = CreateSimpleDocument("Markdown Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        var markdown = await File.ReadAllTextAsync(path);
        Assert.Contains("Markdown Roundtrip", markdown);
        Assert.Contains("Markdown Roundtrip", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveAndLoadDocx_RetainsContent()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.docx");
        var original = CreateSimpleDocument("Docx Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Contains("Docx Roundtrip", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveAndLoadDocx_PreservesFlowCompatibleFormatting()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "rich.docx");
        var original = CreateRichFormattingDocument();

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Contains("Bold Italic", ExtractText(loaded));

        var firstParagraph = Assert.IsType<Paragraph>(loaded.Blocks[0]);
        Assert.True(HasBoldItalicInline(firstParagraph.Inlines, hasBold: false, hasItalic: false));

        var table = Assert.IsType<Table>(loaded.Blocks[1]);
        var firstCell = table.RowGroups[0].Rows[0].Cells[0];
        Assert.True(firstCell.RowSpan >= 2);
        Assert.False(firstCell.Padding.IsEmpty);
        Assert.False(firstCell.BorderThickness.IsEmpty);
        Assert.NotNull(firstCell.BorderBrush);
        Assert.NotNull(firstCell.Background);
    }

    [Fact]
    public async Task SaveAndLoadRtf_RetainsContent()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.rtf");
        var original = CreateSimpleDocument("RTF Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        var rtf = await File.ReadAllTextAsync(path);
        Assert.Contains(@"{\rtf", rtf);
        Assert.Contains("RTF Roundtrip", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveAndLoadRtf_PreservesFormattingAndTableStructure()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "rich.rtf");
        var original = CreateRichFormattingDocument();

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.Contains("Bold Italic", ExtractText(loaded));

        var firstParagraph = Assert.IsType<Paragraph>(loaded.Blocks[0]);
        Assert.True(HasBoldItalicInline(firstParagraph.Inlines, hasBold: false, hasItalic: false));

        var table = Assert.IsType<Table>(loaded.Blocks[1]);
        Assert.True(table.RowGroups.Count > 0);
        Assert.True(table.RowGroups[0].Rows.Count >= 2);
        var firstCell = table.RowGroups[0].Rows[0].Cells[0];
        Assert.False(firstCell.Padding.IsEmpty);
        Assert.False(firstCell.BorderThickness.IsEmpty);
        Assert.NotNull(firstCell.BorderBrush);
        Assert.NotNull(firstCell.Background);
    }

    [Fact]
    public async Task SaveAndLoadRtf_PreservesPageSetup()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "pagesetup.rtf");
        var original = CreateSimpleDocument("Page setup");
        original.PageWidth = 900;
        original.PageHeight = 1300;
        original.PagePadding = new FlowThickness(40, 50, 60, 70);
        original.ColumnGap = 24;

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(loaded.PageWidth.HasValue);
        Assert.InRange(loaded.PageWidth!.Value, 899.5, 900.5);
        Assert.True(loaded.PageHeight.HasValue);
        Assert.InRange(loaded.PageHeight!.Value, 1299.5, 1300.5);
        Assert.InRange(loaded.PagePadding.Left, 39.5, 40.5);
        Assert.InRange(loaded.PagePadding.Top, 49.5, 50.5);
        Assert.InRange(loaded.PagePadding.Right, 59.5, 60.5);
        Assert.InRange(loaded.PagePadding.Bottom, 69.5, 70.5);
        Assert.True(loaded.ColumnGap.HasValue);
        Assert.InRange(loaded.ColumnGap!.Value, 23.5, 24.5);
    }

    [Fact]
    public async Task SaveAndLoadRtf_PreservesHyperlink()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "hyperlink.rtf");
        var original = new FlowDocument();
        var paragraph = new Paragraph();
        var hyperlink = new Hyperlink
        {
            NavigateUri = "https://example.com"
        };
        hyperlink.Inlines.Add(new Run("Visit"));
        paragraph.Inlines.Add(hyperlink);
        original.Blocks.Add(paragraph);

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        var loadedParagraph = Assert.IsType<Paragraph>(loaded.Blocks[0]);
        var loadedHyperlink = Assert.IsType<Hyperlink>(Assert.Single(loadedParagraph.Inlines));
        Assert.Equal("https://example.com", loadedHyperlink.NavigateUri);
        Assert.Equal("Visit", ExtractInlineText(loadedHyperlink).Trim());
    }

    [Fact]
    public async Task SaveAndLoadRtf_PreservesInlineImage()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "image.rtf");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+Y4QAAAAASUVORK5CYII=");

        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new InlineUIContainer
        {
            Child = new FlowInlineImageData(png, 18f, 12f, "image/png")
        });

        document.Blocks.Add(paragraph);
        await service.SaveAsync(document, path);
        var loaded = await service.LoadAsync(path);
        var rtf = await File.ReadAllTextAsync(path);

        var loadedParagraph = Assert.IsType<Paragraph>(loaded.Blocks[0]);
        var container = Assert.IsType<InlineUIContainer>(Assert.Single(loadedParagraph.Inlines));
        var payload = Assert.IsType<FlowInlineImageData>(container.Child);
        Assert.Equal(png, payload.Data);
        Assert.InRange(payload.Width, 17f, 19f);
        Assert.InRange(payload.Height, 11f, 13f);
        Assert.Contains(@"\pict", rtf);
    }

    [Fact]
    public void FlowInlineImageData_ClonesInputBuffer()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var expected = (byte[])source.Clone();

        var payload = new FlowInlineImageData(source, 18f, 12f, "image/png");
        source[0] = 255;

        Assert.NotSame(source, payload.Data);
        Assert.Equal(expected, payload.Data);
    }

    [Fact]
    public async Task SavePdfAndLoadPdf_Succeeds()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.pdf");
        var original = CreateSimpleDocument("Pdf Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadPs_UsesPostScriptBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestPostScriptBridge();
        var postScriptService = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var service = new FlowDocumentFileConversionService(options: null, pdfParser: null, postScriptDocumentConversionService: postScriptService);
        var path = Path.Combine(fixture.Path, "sample.ps");
        var original = CreateSimpleDocument("PS Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Ps, bridge.LastExportKind);
        Assert.Equal(PostScriptKind.Ps, bridge.LastImportKind);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadEps_UsesPostScriptBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestPostScriptBridge();
        var postScriptService = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var service = new FlowDocumentFileConversionService(options: null, pdfParser: null, postScriptDocumentConversionService: postScriptService);
        var path = Path.Combine(fixture.Path, "sample.eps");
        var original = CreateSimpleDocument("EPS Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Eps, bridge.LastExportKind);
        Assert.Equal(PostScriptKind.Eps, bridge.LastImportKind);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadPs_UsesInjectedPostScriptDocumentConversionService()
    {
        using var fixture = new TempDirectoryFixture();
        var postScriptService = new TestPostScriptDocumentConversionService();
        var service = new FlowDocumentFileConversionService(
            options: null,
            pdfParser: null,
            postScriptDocumentConversionService: postScriptService);
        var path = Path.Combine(fixture.Path, "injected.ps");
        var original = CreateSimpleDocument("Injected PS service");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(postScriptService.LoadPathCalls > 0);
        Assert.True(postScriptService.SavePathCalls > 0);
        Assert.Equal(PostScriptKind.Ps, postScriptService.LastLoadKind);
        Assert.Equal(PostScriptKind.Ps, postScriptService.LastSaveKind);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadPs_UsesCompatibilityBridgeConstructor()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestPostScriptBridge();
        var service = new FlowDocumentFileConversionService(options: null, postScriptBridge: bridge, pdfParser: null);
        var path = Path.Combine(fixture.Path, "compat.ps");
        var original = CreateSimpleDocument("Compatibility constructor");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Ps, bridge.LastExportKind);
        Assert.Equal(PostScriptKind.Ps, bridge.LastImportKind);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveToStreamEps_UsesPostScriptBridge()
    {
        var bridge = new TestPostScriptBridge();
        var postScriptService = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var service = new FlowDocumentFileConversionService(options: null, pdfParser: null, postScriptDocumentConversionService: postScriptService);
        var original = CreateSimpleDocument("EPS stream export");

        await using var stream = new MemoryStream();
        await service.SaveAsync(original, stream, ".eps");
        var output = Encoding.ASCII.GetString(stream.ToArray());

        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.Equal(PostScriptKind.Eps, bridge.LastExportKind);
        Assert.StartsWith("%!PS-Adobe-3.0 EPSF-3.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromStreamPs_UsesPostScriptBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var seedService = new FlowDocumentFileConversionService();
        var seedPdfPath = Path.Combine(fixture.Path, "seed.pdf");
        await seedService.SaveAsync(CreateSimpleDocument("PS stream import seed"), seedPdfPath);
        var seedPdf = await File.ReadAllBytesAsync(seedPdfPath);

        var bridge = new TestPostScriptBridge(seedPdf);
        var postScriptService = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var service = new FlowDocumentFileConversionService(options: null, pdfParser: null, postScriptDocumentConversionService: postScriptService);

        await using var source = new MemoryStream(Encoding.ASCII.GetBytes("%!PS-Adobe-3.0\nshowpage\n"));
        var loaded = await service.LoadAsync(source, ".ps");

        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Ps, bridge.LastImportKind);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task Save_UnsupportedExtension_Throws()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.txt");
        var original = CreateSimpleDocument("Unsupported");

        await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.SaveAsync(original, path));
    }

    [Fact]
    public async Task SavePs_WhenGhostscriptIsMissing_ThrowsFormatExceptionWithInnerError()
    {
        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.PostScriptOptions.GhostscriptPath = "__missing_gs_binary__";
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "missing.ps");
        var original = CreateSimpleDocument("PS Save Failure");

        var exception = await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.SaveAsync(original, path));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("Unable to export", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadPs_WhenGhostscriptIsMissing_ThrowsFormatExceptionWithInnerError()
    {
        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.PostScriptOptions.GhostscriptPath = "__missing_gs_binary__";
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "missing.ps");
        await File.WriteAllTextAsync(path, "%!PS-Adobe-3.0\n");

        var exception = await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.LoadAsync(path));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("Unable to import", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_UnsupportedExtension_Throws()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.txt");
        await File.WriteAllTextAsync(path, "hello");

        await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.LoadAsync(path));
    }

    [Fact]
    public async Task Load_FromNonReadableStream_ThrowsArgumentException()
    {
        var service = new FlowDocumentFileConversionService();
        await using var stream = new NonReadableMemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync(stream, ".rtf"));
    }

    [Fact]
    public async Task Save_ToNonWritableStream_ThrowsArgumentException()
    {
        var service = new FlowDocumentFileConversionService();
        var document = CreateSimpleDocument("Non writable stream");
        await using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(document, stream, ".rtf"));
    }

    [Fact]
    public void CanLoadAndCanSave_SupportConfiguredExtensions()
    {
        var service = new FlowDocumentFileConversionService();

        Assert.True(service.CanLoad(".docx"));
        Assert.True(service.CanLoad("sample.markdown"));
        Assert.True(service.CanLoad(".rtf"));
        Assert.True(service.CanLoad(".pdf"));
        Assert.True(service.CanLoad(".pdx"));
        Assert.True(service.CanLoad(".ps"));
        Assert.True(service.CanLoad(".eps"));
        Assert.True(service.CanSave(".docx"));
        Assert.True(service.CanSave(".md"));
        Assert.True(service.CanSave(".rtf"));
        Assert.True(service.CanSave(".pdf"));
        Assert.True(service.CanSave(".pdx"));
        Assert.True(service.CanSave(".ps"));
        Assert.True(service.CanSave(".eps"));
        Assert.False(service.CanLoad(".txt"));
        Assert.False(service.CanSave(".txt"));
    }

    private static FlowDocument CreateSimpleDocument(string text)
    {
        var document = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static FlowDocument CreateRichFormattingDocument()
    {
        var document = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };

        var paragraph = new Paragraph();
        var bold = new Bold();
        var italic = new Italic();
        italic.Inlines.Add(new Run("Bold Italic"));
        bold.Inlines.Add(italic);
        paragraph.Inlines.Add(bold);
        document.Blocks.Add(paragraph);

        var table = new Table();
        var group = new TableRowGroup();
        var row1 = new TableRow();
        var row1Cell1 = new TableCell
        {
            RowSpan = 2,
            Padding = new FlowThickness(6, 4, 6, 4),
            BorderThickness = new FlowThickness(1, 1, 1, 1),
            BorderBrush = "#334455",
            Background = "#EEF3FF"
        };
        row1Cell1.Blocks.Add(new Paragraph("R1C1"));
        row1.Cells.Add(row1Cell1);

        var row1Cell2 = new TableCell();
        row1Cell2.Blocks.Add(new Paragraph("R1C2"));
        row1.Cells.Add(row1Cell2);
        group.Rows.Add(row1);

        var row2 = new TableRow();
        var row2Cell = new TableCell();
        row2Cell.Blocks.Add(new Paragraph("R2C2"));
        row2.Cells.Add(row2Cell);
        group.Rows.Add(row2);

        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        return document;
    }

    private static bool HasBoldItalicInline(InlineCollection inlines, bool hasBold, bool hasItalic)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run && hasBold && hasItalic)
            {
                return true;
            }

            if (inline is Span span)
            {
                var nextBold = hasBold || span.FontWeight == FlowFontWeight.Bold;
                var nextItalic = hasItalic || span.FontStyle == FlowFontStyle.Italic;
                if (nextBold && nextItalic && span.Inlines.Count == 0)
                {
                    return true;
                }

                if (HasBoldItalicInline(span.Inlines, nextBold, nextItalic))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ExtractText(FlowDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlockText(block, builder);
        }

        return builder.ToString();
    }

    private static void AppendBlockText(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    AppendInlineText(inline, builder);
                }
                break;
            case Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlockText(child, builder);
                }
                break;
            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        AppendBlockText(child, builder);
                    }
                }
                break;
            case Table table:
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var child in cell.Blocks)
                            {
                                AppendBlockText(child, builder);
                            }
                        }
                    }
                }
                break;
        }
    }

    private static void AppendInlineText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case Run run:
                builder.Append(run.Text);
                break;
            case Span span:
                foreach (var child in span.Inlines)
                {
                    AppendInlineText(child, builder);
                }
                break;
            case LineBreak:
                builder.AppendLine();
                break;
            case AnchoredBlock anchored:
                foreach (var block in anchored.Blocks)
                {
                    AppendBlockText(block, builder);
                }
                break;
        }
    }

    private static string ExtractInlineText(Inline inline)
    {
        var builder = new StringBuilder();
        AppendInlineText(inline, builder);
        return builder.ToString();
    }

    private sealed class TestPostScriptBridge : IPostScriptBridge
    {
        private readonly Dictionary<string, byte[]> _postScriptPayloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[]? _fallbackPdfPayload;

        public TestPostScriptBridge(byte[]? fallbackPdfPayload = null)
        {
            _fallbackPdfPayload = fallbackPdfPayload is null
                ? null
                : (byte[])fallbackPdfPayload.Clone();
        }

        public int PdfToPostScriptCalls { get; private set; }

        public int PostScriptToPdfCalls { get; private set; }

        public PostScriptKind? LastExportKind { get; private set; }

        public PostScriptKind? LastImportKind { get; private set; }

        public Task ConvertPostScriptToPdfAsync(
            string sourcePath,
            string targetPdfPath,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PostScriptToPdfCalls++;
            LastImportKind = kind;

            if (!_postScriptPayloads.TryGetValue(sourcePath, out var payload))
            {
                if (_fallbackPdfPayload is null)
                {
                    throw new FileNotFoundException($"No test PostScript payload registered for '{sourcePath}'.", sourcePath);
                }

                payload = _fallbackPdfPayload;
            }

            File.WriteAllBytes(targetPdfPath, payload);
            return Task.CompletedTask;
        }

        public Task ConvertPdfToPostScriptAsync(
            string sourcePdfPath,
            string targetPath,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PdfToPostScriptCalls++;
            LastExportKind = kind;

            var payload = File.ReadAllBytes(sourcePdfPath);
            _postScriptPayloads[targetPath] = payload;
            var header = kind == PostScriptKind.Eps
                ? "%!PS-Adobe-3.0 EPSF-3.0"
                : "%!PS-Adobe-3.0";
            File.WriteAllText(targetPath, header + Environment.NewLine);
            return Task.CompletedTask;
        }
    }

    private sealed class TestPostScriptDocumentConversionService : IPostScriptDocumentConversionService
    {
        public int LoadPathCalls { get; private set; }

        public int LoadStreamCalls { get; private set; }

        public int SavePathCalls { get; private set; }

        public int SaveStreamCalls { get; private set; }

        public PostScriptKind? LastLoadKind { get; private set; }

        public PostScriptKind? LastSaveKind { get; private set; }

        public Task<Document> LoadAsync(
            string path,
            PostScriptKind kind,
            PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadPathCalls++;
            LastLoadKind = kind;
            var document = new Document();
            document.Blocks.Clear();
            document.Blocks.Add(new ParagraphBlock("Loaded from injected PS service"));
            return Task.FromResult(document);
        }

        public Task<Document> LoadAsync(
            Stream sourceStream,
            PostScriptKind kind,
            PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadStreamCalls++;
            LastLoadKind = kind;
            var document = new Document();
            document.Blocks.Clear();
            document.Blocks.Add(new ParagraphBlock("Loaded stream from injected PS service"));
            return Task.FromResult(document);
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            string path,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavePathCalls++;
            LastSaveKind = kind;
            var header = kind == PostScriptKind.Eps ? "%!PS-Adobe-3.0 EPSF-3.0" : "%!PS-Adobe-3.0";
            File.WriteAllText(path, header + Environment.NewLine);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            Stream targetStream,
            PostScriptKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveStreamCalls++;
            LastSaveKind = kind;
            using var writer = new StreamWriter(targetStream, Encoding.ASCII, leaveOpen: true);
            writer.Write(kind == PostScriptKind.Eps ? "%!PS-Adobe-3.0 EPSF-3.0\n" : "%!PS-Adobe-3.0\n");
            writer.Flush();
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectoryFixture : IDisposable
    {
        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best effort cleanup for test temp directory.
            }
        }
    }

    private sealed class NonReadableMemoryStream : MemoryStream
    {
        public override bool CanRead => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Read is not supported.");
        }

        public override int Read(Span<byte> buffer)
        {
            throw new NotSupportedException("Read is not supported.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Read is not supported.");
        }
    }
}
