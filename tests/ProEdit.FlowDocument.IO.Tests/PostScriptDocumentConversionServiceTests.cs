using System.Text;
using ProEdit.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Layout;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class PostScriptDocumentConversionServiceTests
{
    [Fact]
    public async Task SaveAndLoadPs_Path_UsesPostScriptBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestPostScriptBridge();
        var service = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var path = Path.Combine(fixture.Path, "sample.ps");
        var original = CreateSimpleDocument("PostScript document service");

        await service.SaveAsync(original, CreateLayoutSettings(), path, PostScriptKind.Ps);
        var loaded = await service.LoadAsync(path, PostScriptKind.Ps);

        Assert.True(File.Exists(path));
        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Ps, bridge.LastExportKind);
        Assert.Equal(PostScriptKind.Ps, bridge.LastImportKind);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveToStreamEps_UsesPostScriptBridge()
    {
        var bridge = new TestPostScriptBridge();
        var service = new PostScriptDocumentConversionService(bridge, pdfParser: null);
        var document = CreateSimpleDocument("EPS stream");

        await using var stream = new MemoryStream();
        await service.SaveAsync(document, CreateLayoutSettings(), stream, PostScriptKind.Eps);
        var output = Encoding.ASCII.GetString(stream.ToArray());

        Assert.True(bridge.PdfToPostScriptCalls > 0);
        Assert.Equal(PostScriptKind.Eps, bridge.LastExportKind);
        Assert.StartsWith("%!PS-Adobe-3.0 EPSF-3.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromStreamPs_UsesPostScriptBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var seedPdf = await CreateSeedPdfPayloadAsync(CreateSimpleDocument("PS stream import seed"), fixture.Path);
        var bridge = new TestPostScriptBridge(seedPdf);
        var service = new PostScriptDocumentConversionService(bridge, pdfParser: null);

        await using var source = new MemoryStream(Encoding.ASCII.GetBytes("%!PS-Adobe-3.0\nshowpage\n"));
        var loaded = await service.LoadAsync(source, PostScriptKind.Ps);

        Assert.True(bridge.PostScriptToPdfCalls > 0);
        Assert.Equal(PostScriptKind.Ps, bridge.LastImportKind);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task Load_FromNonReadableStream_ThrowsArgumentException()
    {
        var service = new PostScriptDocumentConversionService();
        await using var stream = new NonReadableMemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync(stream, PostScriptKind.Ps));
    }

    [Fact]
    public async Task Save_ToNonWritableStream_ThrowsArgumentException()
    {
        var service = new PostScriptDocumentConversionService();
        await using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(CreateSimpleDocument("No write"), CreateLayoutSettings(), stream, PostScriptKind.Ps));
    }

    private static Document CreateSimpleDocument(string text)
    {
        var document = new Document();
        document.Blocks.Clear();

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new RunInline(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static LayoutSettings CreateLayoutSettings()
    {
        return new LayoutSettings
        {
            PageWidth = 816f,
            PageHeight = 1056f,
            ViewportWidth = 816f,
            ViewportHeight = 1056f,
            UsePagination = true,
            PageFlow = PageFlowDirection.Vertical
        };
    }

    private static async Task<byte[]> CreateSeedPdfPayloadAsync(Document document, string directory)
    {
        var path = Path.Combine(directory, "seed.pdf");
        var context = new DocumentPrintContext(document, CreateLayoutSettings());
        var settings = new PrintSettings
        {
            OutputKind = PrintOutputKind.Pdf,
            OutputPath = path,
            RangeKind = PrintRangeKind.All,
            Copies = 1,
            Collate = true
        };

        var systemPrintService = new SystemPrintService();
        var printService = new SkiaPrintService(systemPrintService, systemPrintService);
        var result = await printService.PrintAsync(context, settings);
        Assert.True(result.Succeeded, result.Message);
        return await File.ReadAllBytesAsync(path);
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
