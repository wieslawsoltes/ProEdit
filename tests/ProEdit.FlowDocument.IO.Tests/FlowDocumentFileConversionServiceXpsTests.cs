using ProEdit.Documents;
using ProEdit.FlowDocument;
using ProEdit.FlowDocument.IO;
using ProEdit.Layout;
using ProEdit.Pdf;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class FlowDocumentFileConversionServiceXpsTests
{
    [Fact]
    public async Task SaveAndLoadXps_UsesInjectedXpsDocumentConversionService()
    {
        using var fixture = new TempDirectoryFixture();
        var xpsService = new TestXpsDocumentConversionService();
        var service = new FlowDocumentFileConversionService(
            options: null,
            pdfParser: null,
            postScriptDocumentConversionService: null,
            xpsDocumentConversionService: xpsService);
        var path = Path.Combine(fixture.Path, "sample.xps");
        var original = CreateSimpleDocument("XPS Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(xpsService.SavePathCalls > 0);
        Assert.True(xpsService.LoadPathCalls > 0);
        Assert.Equal(XpsFlavor.Xps, xpsService.LastSaveFlavor);
        Assert.Equal(XpsFlavor.Xps, xpsService.LastLoadFlavor);
        Assert.Contains("Loaded from injected XPS service", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveToStreamOxps_UsesInjectedXpsDocumentConversionService()
    {
        var xpsService = new TestXpsDocumentConversionService();
        var service = new FlowDocumentFileConversionService(
            options: null,
            pdfParser: null,
            postScriptDocumentConversionService: null,
            xpsDocumentConversionService: xpsService);

        await using var stream = new MemoryStream();
        await service.SaveAsync(CreateSimpleDocument("OXPS stream"), stream, ".oxps");

        Assert.True(xpsService.SaveStreamCalls > 0);
        Assert.Equal(XpsFlavor.Oxps, xpsService.LastSaveFlavor);
    }

    [Fact]
    public async Task LoadFromStreamOxps_UsesInjectedXpsDocumentConversionService()
    {
        var xpsService = new TestXpsDocumentConversionService();
        var service = new FlowDocumentFileConversionService(
            options: null,
            pdfParser: null,
            postScriptDocumentConversionService: null,
            xpsDocumentConversionService: xpsService);

        await using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var loaded = await service.LoadAsync(source, ".oxps");

        Assert.True(xpsService.LoadStreamCalls > 0);
        Assert.Equal(XpsFlavor.Oxps, xpsService.LastLoadFlavor);
        Assert.Contains("Loaded stream from injected XPS service", ExtractText(loaded));
    }

    [Fact]
    public void CanLoadAndCanSave_SupportsXpsExtensions()
    {
        var service = new FlowDocumentFileConversionService();

        Assert.True(service.CanLoad(".xps"));
        Assert.True(service.CanLoad(".oxps"));
        Assert.True(service.CanSave(".xps"));
        Assert.True(service.CanSave(".oxps"));
    }

    [Fact]
    public async Task SaveXps_WhenGhostscriptIsMissing_ThrowsFormatExceptionWithInnerError()
    {
        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.XpsOptions.GhostscriptPath = "__missing_gs_binary__";
        options.XpsOptions.EnableNativeConversion = false;
        options.XpsOptions.PreferNativeConversion = false;
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "missing.xps");

        var exception = await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.SaveAsync(CreateSimpleDocument("XPS save"), path));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("Unable to export XPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadOxps_WhenGhostscriptIsMissing_ThrowsFormatExceptionWithInnerError()
    {
        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.XpsOptions.GhostscriptPath = "__missing_gs_binary__";
        options.XpsOptions.EnableNativeConversion = false;
        options.XpsOptions.PreferNativeConversion = false;
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "missing.oxps");
        await File.WriteAllTextAsync(path, "<?xml version=\"1.0\"?><FixedDocumentSequence/>");

        var exception = await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.LoadAsync(path));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("Unable to import OXPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FlowDocument CreateSimpleDocument(string text)
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static string ExtractText(FlowDocument document)
    {
        var lines = new List<string>();
        for (var i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is Paragraph paragraph)
            {
                var line = string.Concat(paragraph.Inlines.OfType<Run>().Select(static run => run.Text));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }
        }

        return string.Join("\n", lines);
    }

    private sealed class TestXpsDocumentConversionService : IXpsDocumentConversionService
    {
        public int LoadPathCalls { get; private set; }
        public int LoadStreamCalls { get; private set; }
        public int SavePathCalls { get; private set; }
        public int SaveStreamCalls { get; private set; }
        public XpsFlavor? LastLoadFlavor { get; private set; }
        public XpsFlavor? LastSaveFlavor { get; private set; }

        public Task<Document> LoadAsync(
            string path,
            XpsFlavor flavor,
            PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadPathCalls++;
            LastLoadFlavor = flavor;
            var document = new Document();
            document.Blocks.Clear();
            document.Blocks.Add(new ParagraphBlock("Loaded from injected XPS service"));
            return Task.FromResult(document);
        }

        public Task<Document> LoadAsync(
            Stream sourceStream,
            XpsFlavor flavor,
            PdfImportOptions? importOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadStreamCalls++;
            LastLoadFlavor = flavor;
            var document = new Document();
            document.Blocks.Clear();
            document.Blocks.Add(new ParagraphBlock("Loaded stream from injected XPS service"));
            return Task.FromResult(document);
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            string path,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavePathCalls++;
            LastSaveFlavor = flavor;
            File.WriteAllText(path, "<?xml version=\"1.0\"?><FixedDocumentSequence/>");
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Document document,
            LayoutSettings layoutSettings,
            Stream targetStream,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveStreamCalls++;
            LastSaveFlavor = flavor;
            using var writer = new StreamWriter(targetStream, leaveOpen: true);
            writer.Write("<?xml version=\"1.0\"?><FixedDocumentSequence/>");
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
}
