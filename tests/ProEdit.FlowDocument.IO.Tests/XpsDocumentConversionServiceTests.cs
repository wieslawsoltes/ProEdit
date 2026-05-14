using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ProEdit.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Layout;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;
using ProEdit.Primitives;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class XpsDocumentConversionServiceTests
{
    [Fact]
    public async Task SaveAndLoadXps_Path_UsesXpsBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateBridgeOnlyXpsOptions());
        var path = Path.Combine(fixture.Path, "sample.xps");
        var original = CreateSimpleDocument("XPS document service");

        await service.SaveAsync(original, CreateLayoutSettings(), path, XpsFlavor.Xps);
        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.True(File.Exists(path));
        Assert.True(bridge.PdfToXpsCalls > 0);
        Assert.True(bridge.XpsToPdfCalls > 0);
        Assert.Equal(XpsFlavor.Xps, bridge.LastExportFlavor);
        Assert.Equal(XpsFlavor.Xps, bridge.LastImportFlavor);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveToStreamOxps_UsesXpsBridge()
    {
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateBridgeOnlyXpsOptions());
        var document = CreateSimpleDocument("OXPS stream");

        await using var stream = new MemoryStream();
        await service.SaveAsync(document, CreateLayoutSettings(), stream, XpsFlavor.Oxps);
        var output = Encoding.ASCII.GetString(stream.ToArray());

        Assert.True(bridge.PdfToXpsCalls > 0);
        Assert.Equal(XpsFlavor.Oxps, bridge.LastExportFlavor);
        Assert.StartsWith("<?xml", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromStreamXps_UsesXpsBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var seedPdf = await CreateSeedPdfPayloadAsync(CreateSimpleDocument("XPS stream import seed"), fixture.Path);
        var bridge = new TestXpsBridge(seedPdf);
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateBridgeOnlyXpsOptions());

        await using var source = new MemoryStream(Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?><FixedDocumentSequence/>"));
        var loaded = await service.LoadAsync(source, XpsFlavor.Xps);

        Assert.True(bridge.XpsToPdfCalls > 0);
        Assert.Equal(XpsFlavor.Xps, bridge.LastImportFlavor);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadXps_Path_NativePreferred_DoesNotUseBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));
        var path = Path.Combine(fixture.Path, "native.xps");
        var original = CreateSimpleDocument("Native XPS pipeline");

        await service.SaveAsync(original, CreateLayoutSettings(), path, XpsFlavor.Xps);
        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.True(File.Exists(path));
        Assert.Equal(0, bridge.PdfToXpsCalls);
        Assert.Equal(0, bridge.XpsToPdfCalls);
        Assert.Contains("Native XPS pipeline", ExtractDocumentText(loaded), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_NativeXps_WithResourceDictionaryLinksAndVectors_MapsExpectedInlines()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "resource-linked.xps");
        await WriteResourceLinkedXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var textRun = FindRunInlineByText(loaded, "Linked text");
        Assert.NotNull(textRun);
        Assert.Equal("https://example.com/text", textRun!.Hyperlink?.Uri);
        Assert.Equal(new DocColor(0x22, 0x88, 0x33), textRun.Style?.Color);

        var image = FindFirstImageInline(loaded);
        Assert.NotNull(image);
        Assert.Equal(80f, image!.Width, 3);
        Assert.Equal(60f, image.Height, 3);

        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.Equal("https://example.com/vector", shape!.Hyperlink?.Uri);
        Assert.Equal(new DocColor(0x33, 0x66, 0x99), shape.Properties.FillColor);
    }

    [Fact]
    public async Task Load_NativeXps_PathFillStaticResourceElementImageBrush_ImportsImage()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "resource-image-fill-element.xps");
        await WriteImageFillResourceElementXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var image = FindFirstImageInline(loaded);
        Assert.NotNull(image);
        Assert.Equal(80f, image!.Width, 3);
        Assert.Equal(60f, image.Height, 3);
    }

    [Fact]
    public async Task SaveAndLoadXps_NativePreferred_PreservesShapeAndRunHyperlinks()
    {
        using var fixture = new TempDirectoryFixture();
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));
        var path = Path.Combine(fixture.Path, "native-vector-links.xps");
        var original = CreateDocumentWithShapeAndHyperlinks();

        await service.SaveAsync(original, CreateLayoutSettings(), path, XpsFlavor.Xps);
        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.PdfToXpsCalls);
        Assert.Equal(0, bridge.XpsToPdfCalls);
        var run = FindRunInlineByText(loaded, "Vector linked text");
        Assert.NotNull(run);
        Assert.Equal("https://example.com/text-link", run!.Hyperlink?.Uri);

        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.Equal("https://example.com/shape-link", shape!.Hyperlink?.Uri);
        Assert.True(shape.Width > 0f);
        Assert.True(shape.Height > 0f);
    }

    [Fact]
    public async Task Load_NativeXps_PathDataWithFillRulePrefix_ImportsShape()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "fillrule-prefix.xps");
        await WriteFillRulePrefixedShapeXpsAsync(path, "F1");
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.True(shape!.Width > 0f);
        Assert.True(shape.Height > 0f);
    }

    [Fact]
    public async Task Load_NativeXps_PathDataF0Prefix_SetsEvenOddFillRule()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "fillrule-f0.xps");
        await WriteFillRulePrefixedShapeXpsAsync(path, "F0");
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        var fillRule = shape!.Properties.CustomGeometry?.Paths.FirstOrDefault()?.FillRule;
        Assert.Equal(ShapePathFillRule.EvenOdd, fillRule);
    }

    [Fact]
    public async Task LoadThenSave_NativeXps_F0FillRule_PreservesEvenOddOnWrite()
    {
        using var fixture = new TempDirectoryFixture();
        var sourcePath = Path.Combine(fixture.Path, "source-f0.xps");
        var targetPath = Path.Combine(fixture.Path, "target-f0.xps");
        await WriteFillRulePrefixedShapeXpsAsync(sourcePath, "F0");
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(sourcePath, XpsFlavor.Xps);
        await service.SaveAsync(loaded, CreateLayoutSettings(), targetPath, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        Assert.Equal(0, bridge.PdfToXpsCalls);
        var fillRule = ReadFirstPathFillRule(targetPath);
        Assert.Equal("EvenOdd", fillRule);
    }

    [Fact]
    public async Task Load_NativeXps_GlyphRenderTransformTranslation_AffectsOrdering()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "glyph-transform-order.xps");
        await WriteGlyphTransformOrderingXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var texts = ExtractRunTextsInOrder(loaded);
        Assert.True(texts.Count >= 2);
        Assert.Equal("First", texts[0]);
        Assert.Equal("Second shifted", texts[1]);
    }

    [Fact]
    public async Task Load_NativeXps_PathRenderTransformMatrixElement_AffectsShapeOrdering()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "shape-transform-order.xps");
        await WriteShapeTransformOrderingXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var uris = ExtractShapeHyperlinkUrisInOrder(loaded);
        Assert.True(uris.Count >= 2);
        Assert.Equal("https://example.com/static", uris[0]);
        Assert.Equal("https://example.com/translated", uris[1]);
    }

    [Fact]
    public async Task Load_NativeXps_PathRenderTransformScale_ScalesStrokeThickness()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "shape-transform-scale.xps");
        await WriteShapeScaleTransformXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.NotNull(shape!.Properties.Outline);
        Assert.Equal(4f, shape.Properties.Outline!.Thickness, 3);
    }

    [Fact]
    public async Task Load_NativeXps_PathStrokeSolidColorBrushElement_ImportsStroke()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "shape-stroke-brush-element.xps");
        await WritePathStrokeBrushElementXpsAsync(path);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.NotNull(shape!.Properties.Outline);
        Assert.Equal(new DocColor(0x11, 0x22, 0x33), shape.Properties.Outline!.Color);
        Assert.Equal(3f, shape.Properties.Outline.Thickness, 3);
    }

    [Fact]
    public async Task Load_NativeXps_PathFillAndStrokeStaticResourceElements_ImportsBrushes()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "shape-static-resource-brush-elements.xps");
        await WritePathFillStrokeResourceElementXpsAsync(path, useDynamicResources: false);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.Equal(new DocColor(0x66, 0x88, 0xAA), shape!.Properties.FillColor);
        Assert.NotNull(shape.Properties.Outline);
        Assert.Equal(new DocColor(0x22, 0x44, 0x66), shape.Properties.Outline!.Color);
        Assert.Equal(2f, shape.Properties.Outline.Thickness, 3);
    }

    [Fact]
    public async Task Load_NativeXps_PathFillAndStrokeDynamicResourceElements_ImportsBrushes()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "shape-dynamic-resource-brush-elements.xps");
        await WritePathFillStrokeResourceElementXpsAsync(path, useDynamicResources: true);
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(0, bridge.XpsToPdfCalls);
        var shape = FindFirstShapeInline(loaded);
        Assert.NotNull(shape);
        Assert.Equal(new DocColor(0x66, 0x88, 0xAA), shape!.Properties.FillColor);
        Assert.NotNull(shape.Properties.Outline);
        Assert.Equal(new DocColor(0x22, 0x44, 0x66), shape.Properties.Outline!.Color);
        Assert.Equal(2f, shape.Properties.Outline.Thickness, 3);
    }

    [Fact]
    public async Task Load_InvalidXps_WhenFallbackEnabled_UsesBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "invalid.xps");
        await File.WriteAllTextAsync(path, "not-an-xps-package");
        var seedPdf = await CreateSeedPdfPayloadAsync(CreateSimpleDocument("Fallback PDF seed"), fixture.Path);
        var bridge = new TestXpsBridge(seedPdf);
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: true));

        var loaded = await service.LoadAsync(path, XpsFlavor.Xps);

        Assert.Equal(1, bridge.XpsToPdfCalls);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task Load_InvalidXps_WhenFallbackDisabled_ThrowsAndSkipsBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "invalid.xps");
        await File.WriteAllTextAsync(path, "not-an-xps-package");
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadAsync(path, XpsFlavor.Xps));

        Assert.Equal(0, bridge.XpsToPdfCalls);
        Assert.Contains("Unable to import XPS", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghostscript", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_InvalidTargetPath_WhenFallbackDisabled_ThrowsAndSkipsBridge()
    {
        using var fixture = new TempDirectoryFixture();
        var missingDirectory = Path.Combine(fixture.Path, "missing");
        var path = Path.Combine(missingDirectory, "native.xps");
        var bridge = new TestXpsBridge();
        var service = new XpsDocumentConversionService(bridge, pdfParser: null, xpsOptions: CreateNativePreferredXpsOptions(fallbackToGhostscript: false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(CreateSimpleDocument("Native save"), CreateLayoutSettings(), path, XpsFlavor.Xps));

        Assert.Equal(0, bridge.PdfToXpsCalls);
        Assert.Contains("Unable to export XPS", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghostscript", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_FromNonReadableStream_ThrowsArgumentException()
    {
        var service = new XpsDocumentConversionService();
        await using var stream = new NonReadableMemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync(stream, XpsFlavor.Xps));
    }

    [Fact]
    public async Task Save_ToNonWritableStream_ThrowsArgumentException()
    {
        var service = new XpsDocumentConversionService();
        await using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(CreateSimpleDocument("No write"), CreateLayoutSettings(), stream, XpsFlavor.Xps));
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

    private static XpsConversionOptions CreateBridgeOnlyXpsOptions()
    {
        return new XpsConversionOptions
        {
            EnableNativeConversion = false,
            PreferNativeConversion = false,
            FallbackToGhostscript = true
        };
    }

    private static XpsConversionOptions CreateNativePreferredXpsOptions(bool fallbackToGhostscript)
    {
        return new XpsConversionOptions
        {
            EnableNativeConversion = true,
            PreferNativeConversion = true,
            FallbackToGhostscript = fallbackToGhostscript
        };
    }

    private static string ExtractDocumentText(Document document)
    {
        var parts = new List<string>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            if (paragraph.Inlines.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    parts.Add(paragraph.Text);
                }

                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is RunInline run)
                {
                    var text = run.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                }
            }
        }

        return string.Join(" ", parts);
    }

    private static RunInline? FindRunInlineByText(Document document, string text)
    {
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is RunInline run
                    && string.Equals(run.GetText(), text, StringComparison.Ordinal))
                {
                    return run;
                }
            }
        }

        return null;
    }

    private static ImageInline? FindFirstImageInline(Document document)
    {
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is ImageInline image)
                {
                    return image;
                }
            }
        }

        return null;
    }

    private static ShapeInline? FindFirstShapeInline(Document document)
    {
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is ShapeInline shape)
                {
                    return shape;
                }
            }
        }

        return null;
    }

    private static Document CreateDocumentWithShapeAndHyperlinks()
    {
        var document = new Document();
        document.Blocks.Clear();

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new RunInline("Vector linked text")
        {
            Hyperlink = new HyperlinkInfo("https://example.com/text-link", null, null)
        });
        paragraph.Inlines.Add(CreateTriangleShapeInline());
        document.Blocks.Add(paragraph);
        return document;
    }

    private static ShapeInline CreateTriangleShapeInline()
    {
        var shape = new ShapeInline(64f, 48f);
        var geometry = new ShapeGeometry();
        var path = new ShapePath
        {
            Width = 64,
            Height = 48,
            FillMode = ShapePathFillMode.Normal,
            IsStroked = true
        };
        path.Commands.Add(new ShapeMoveToCommand(new ShapeAdjustPoint("0", "48")));
        path.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint("32", "0")));
        path.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint("64", "48")));
        path.Commands.Add(new ShapeClosePathCommand());
        geometry.Paths.Add(path);
        geometry.TextRectangle = new ShapeTextRectangle("l", "t", "r", "b");
        shape.Properties.CustomGeometry = geometry;
        shape.Properties.FillColor = new DocColor(0x55, 0x77, 0xAA);
        shape.Properties.Outline = new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1.5f,
            Color = new DocColor(0x22, 0x33, 0x44)
        };
        shape.Hyperlink = new HyperlinkInfo("https://example.com/shape-link", null, null);
        return shape;
    }

    private static Task WriteResourceLinkedXpsAsync(string path)
    {
        const string packageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string fixedRepresentationType = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        const string xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        const string contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var contentTypes = new XDocument(
            new XElement(
                XName.Get("Types", contentTypesNamespace),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/FixedDoc.fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/Pages/1.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml"))));

        var rootRelationships = new XDocument(
            new XElement(
                XName.Get("Relationships", packageRelationshipsNamespace),
                new XElement(
                    XName.Get("Relationship", packageRelationshipsNamespace),
                    new XAttribute("Id", "R1"),
                    new XAttribute("Type", fixedRepresentationType),
                    new XAttribute("Target", "/FixedDocSeq.fdseq"))));

        var fixedDocumentSequence = new XDocument(
            new XElement(
                XName.Get("FixedDocumentSequence", xpsNamespace),
                new XElement(XName.Get("DocumentReference", xpsNamespace), new XAttribute("Source", "/Documents/1/FixedDoc.fdoc"))));

        var fixedDocument = new XDocument(
            new XElement(
                XName.Get("FixedDocument", xpsNamespace),
                new XElement(XName.Get("PageContent", xpsNamespace), new XAttribute("Source", "/Documents/1/Pages/1.fpage"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                new XAttribute(XNamespace.Xmlns + "x", xamlNamespace),
                new XElement(
                    XName.Get("FixedPage.Resources", xpsNamespace),
                    new XElement(
                        XName.Get("ResourceDictionary", xpsNamespace),
                        new XAttribute("Source", "/Resources/dictionaries/brushes.xaml"))),
                new XElement(
                    XName.Get("Glyphs", xpsNamespace),
                    new XAttribute("FontUri", "/Resources/Fonts/Arial.odttf"),
                    new XAttribute("FontRenderingEmSize", "16"),
                    new XAttribute("Fill", "{StaticResource AccentBrush}"),
                    new XAttribute("OriginX", "40"),
                    new XAttribute("OriginY", "72"),
                    new XAttribute("UnicodeString", "Linked text"),
                    new XAttribute("FixedPage.NavigateUri", "https://example.com/text")),
                new XElement(
                    XName.Get("Path", xpsNamespace),
                    new XAttribute("Data", "M 100,120 L 180,120 180,180 100,180 Z"),
                    new XAttribute("Fill", "{StaticResource PhotoBrush}")),
                new XElement(
                    XName.Get("Path", xpsNamespace),
                    new XAttribute("Data", "M 200,200 L 260,200 260,250 200,250 Z"),
                    new XAttribute("Fill", "#FF336699"),
                    new XAttribute("Stroke", "#FF112233"),
                    new XAttribute("StrokeThickness", "2"),
                    new XAttribute("FixedPage.NavigateUri", "https://example.com/vector"))));

        var dictionaryPart = new XDocument(
            new XElement(
                XName.Get("ResourceDictionary", xpsNamespace),
                new XAttribute(XNamespace.Xmlns + "x", xamlNamespace),
                new XElement(
                    XName.Get("SolidColorBrush", xpsNamespace),
                    new XAttribute(XName.Get("Key", xamlNamespace), "AccentBrush"),
                    new XAttribute("Color", "#FF228833")),
                new XElement(
                    XName.Get("ImageBrush", xpsNamespace),
                    new XAttribute(XName.Get("Key", xamlNamespace), "PhotoBrush"),
                    new XAttribute("ImageSource", "/Resources/Images/pic.png"),
                    new XAttribute("Viewport", "100,120,80,60"),
                    new XAttribute("ViewportUnits", "Absolute"))));

        WriteXmlEntry(archive, "[Content_Types].xml", contentTypes);
        WriteXmlEntry(archive, "_rels/.rels", rootRelationships);
        WriteXmlEntry(archive, "FixedDocSeq.fdseq", fixedDocumentSequence);
        WriteXmlEntry(archive, "Documents/1/FixedDoc.fdoc", fixedDocument);
        WriteXmlEntry(archive, "Documents/1/Pages/1.fpage", fixedPage);
        WriteXmlEntry(archive, "Resources/dictionaries/brushes.xaml", dictionaryPart);
        WriteBinaryEntry(archive, "Resources/Images/pic.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+Wn0AAAAASUVORK5CYII="));

        return Task.CompletedTask;
    }

    private static Task WriteFillRulePrefixedShapeXpsAsync(string path, string fillRulePrefix)
    {
        const string packageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string fixedRepresentationType = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        const string contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var contentTypes = new XDocument(
            new XElement(
                XName.Get("Types", contentTypesNamespace),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/FixedDoc.fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/Pages/1.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml"))));

        var rootRelationships = new XDocument(
            new XElement(
                XName.Get("Relationships", packageRelationshipsNamespace),
                new XElement(
                    XName.Get("Relationship", packageRelationshipsNamespace),
                    new XAttribute("Id", "R1"),
                    new XAttribute("Type", fixedRepresentationType),
                    new XAttribute("Target", "/FixedDocSeq.fdseq"))));

        var fixedDocumentSequence = new XDocument(
            new XElement(
                XName.Get("FixedDocumentSequence", xpsNamespace),
                new XElement(XName.Get("DocumentReference", xpsNamespace), new XAttribute("Source", "/Documents/1/FixedDoc.fdoc"))));

        var fixedDocument = new XDocument(
            new XElement(
                XName.Get("FixedDocument", xpsNamespace),
                new XElement(XName.Get("PageContent", xpsNamespace), new XAttribute("Source", "/Documents/1/Pages/1.fpage"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                new XElement(
                    XName.Get("Path", xpsNamespace),
                    new XAttribute("Data", fillRulePrefix + " M 40,100 L 140,100 140,160 40,160 Z"),
                    new XAttribute("Fill", "#FF336699"),
                    new XAttribute("Stroke", "#FF112233"),
                    new XAttribute("StrokeThickness", "2"))));

        WriteXmlEntry(archive, "[Content_Types].xml", contentTypes);
        WriteXmlEntry(archive, "_rels/.rels", rootRelationships);
        WriteXmlEntry(archive, "FixedDocSeq.fdseq", fixedDocumentSequence);
        WriteXmlEntry(archive, "Documents/1/FixedDoc.fdoc", fixedDocument);
        WriteXmlEntry(archive, "Documents/1/Pages/1.fpage", fixedPage);

        return Task.CompletedTask;
    }

    private static Task WriteImageFillResourceElementXpsAsync(string path)
    {
        const string packageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string fixedRepresentationType = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        const string xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        const string contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var contentTypes = new XDocument(
            new XElement(
                XName.Get("Types", contentTypesNamespace),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/FixedDoc.fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/Pages/1.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml"))));

        var rootRelationships = new XDocument(
            new XElement(
                XName.Get("Relationships", packageRelationshipsNamespace),
                new XElement(
                    XName.Get("Relationship", packageRelationshipsNamespace),
                    new XAttribute("Id", "R1"),
                    new XAttribute("Type", fixedRepresentationType),
                    new XAttribute("Target", "/FixedDocSeq.fdseq"))));

        var fixedDocumentSequence = new XDocument(
            new XElement(
                XName.Get("FixedDocumentSequence", xpsNamespace),
                new XElement(XName.Get("DocumentReference", xpsNamespace), new XAttribute("Source", "/Documents/1/FixedDoc.fdoc"))));

        var fixedDocument = new XDocument(
            new XElement(
                XName.Get("FixedDocument", xpsNamespace),
                new XElement(XName.Get("PageContent", xpsNamespace), new XAttribute("Source", "/Documents/1/Pages/1.fpage"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                new XAttribute(XNamespace.Xmlns + "x", xamlNamespace),
                new XElement(
                    XName.Get("FixedPage.Resources", xpsNamespace),
                    new XElement(
                        XName.Get("ResourceDictionary", xpsNamespace),
                        new XElement(
                            XName.Get("ImageBrush", xpsNamespace),
                            new XAttribute(XName.Get("Key", xamlNamespace), "PhotoBrush"),
                            new XAttribute("ImageSource", "/Resources/Images/pic.png"),
                            new XAttribute("Viewport", "100,120,80,60"),
                            new XAttribute("ViewportUnits", "Absolute")))),
                new XElement(
                    XName.Get("Path", xpsNamespace),
                    new XAttribute("Data", "M 100,120 L 180,120 180,180 100,180 Z"),
                    new XElement(
                        XName.Get("Path.Fill", xpsNamespace),
                        new XElement(
                            XName.Get("StaticResource", xamlNamespace),
                            new XAttribute("ResourceKey", "PhotoBrush"))))));

        WriteXmlEntry(archive, "[Content_Types].xml", contentTypes);
        WriteXmlEntry(archive, "_rels/.rels", rootRelationships);
        WriteXmlEntry(archive, "FixedDocSeq.fdseq", fixedDocumentSequence);
        WriteXmlEntry(archive, "Documents/1/FixedDoc.fdoc", fixedDocument);
        WriteXmlEntry(archive, "Documents/1/Pages/1.fpage", fixedPage);
        WriteBinaryEntry(archive, "Resources/Images/pic.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+Wn0AAAAASUVORK5CYII="));

        return Task.CompletedTask;
    }

    private static Task WriteGlyphTransformOrderingXpsAsync(string path)
    {
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                new XElement(
                    XName.Get("Glyphs", xpsNamespace),
                    new XAttribute("FontUri", "/Resources/Fonts/Arial.odttf"),
                    new XAttribute("FontRenderingEmSize", "16"),
                    new XAttribute("Fill", "#FF000000"),
                    new XAttribute("OriginX", "20"),
                    new XAttribute("OriginY", "100"),
                    new XAttribute("UnicodeString", "First")),
                new XElement(
                    XName.Get("Glyphs", xpsNamespace),
                    new XAttribute("FontUri", "/Resources/Fonts/Arial.odttf"),
                    new XAttribute("FontRenderingEmSize", "16"),
                    new XAttribute("Fill", "#FF000000"),
                    new XAttribute("OriginX", "20"),
                    new XAttribute("OriginY", "10"),
                    new XAttribute("RenderTransform", "1,0,0,1,0,220"),
                    new XAttribute("UnicodeString", "Second shifted"))));

        WriteSinglePageXpsPackage(archive, fixedPage);
        return Task.CompletedTask;
    }

    private static Task WriteShapeTransformOrderingXpsAsync(string path)
    {
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var translated = new XElement(
            XName.Get("Path", xpsNamespace),
            new XAttribute("Data", "M 20,10 L 80,10 80,40 20,40 Z"),
            new XAttribute("Fill", "#FF336699"),
            new XAttribute("Stroke", "#FF112233"),
            new XAttribute("StrokeThickness", "2"),
            new XAttribute("FixedPage.NavigateUri", "https://example.com/translated"),
            new XElement(
                XName.Get("Path.RenderTransform", xpsNamespace),
                new XElement(
                    XName.Get("MatrixTransform", xpsNamespace),
                    new XAttribute("Matrix", "1,0,0,1,0,280"))));

        var staticPath = new XElement(
            XName.Get("Path", xpsNamespace),
            new XAttribute("Data", "M 20,100 L 80,100 80,130 20,130 Z"),
            new XAttribute("Fill", "#FF669933"),
            new XAttribute("Stroke", "#FF112233"),
            new XAttribute("StrokeThickness", "2"),
            new XAttribute("FixedPage.NavigateUri", "https://example.com/static"));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                translated,
                staticPath));

        WriteSinglePageXpsPackage(archive, fixedPage);
        return Task.CompletedTask;
    }

    private static Task WriteShapeScaleTransformXpsAsync(string path)
    {
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var scaled = new XElement(
            XName.Get("Path", xpsNamespace),
            new XAttribute("Data", "M 20,20 L 80,20 80,50 20,50 Z"),
            new XAttribute("Fill", "#FF336699"),
            new XAttribute("Stroke", "#FF112233"),
            new XAttribute("StrokeThickness", "2"),
            new XElement(
                XName.Get("Path.RenderTransform", xpsNamespace),
                new XElement(
                    XName.Get("MatrixTransform", xpsNamespace),
                    new XAttribute("Matrix", "2,0,0,2,0,0"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                scaled));

        WriteSinglePageXpsPackage(archive, fixedPage);
        return Task.CompletedTask;
    }

    private static Task WritePathStrokeBrushElementXpsAsync(string path)
    {
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var strokePath = new XElement(
            XName.Get("Path", xpsNamespace),
            new XAttribute("Data", "M 20,20 L 80,20 80,50 20,50 Z"),
            new XAttribute("Fill", "#FF336699"),
            new XAttribute("StrokeThickness", "3"),
            new XElement(
                XName.Get("Path.Stroke", xpsNamespace),
                new XElement(
                    XName.Get("SolidColorBrush", xpsNamespace),
                    new XAttribute("Color", "#FF112233"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                strokePath));

        WriteSinglePageXpsPackage(archive, fixedPage);
        return Task.CompletedTask;
    }

    private static Task WritePathFillStrokeResourceElementXpsAsync(string path, bool useDynamicResources)
    {
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        const string xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        var resourceElementName = useDynamicResources ? "DynamicResource" : "StaticResource";

        var shapePath = new XElement(
            XName.Get("Path", xpsNamespace),
            new XAttribute("Data", "M 20,20 L 80,20 80,50 20,50 Z"),
            new XAttribute("StrokeThickness", "2"),
            new XElement(
                XName.Get("Path.Fill", xpsNamespace),
                new XElement(
                    XName.Get(resourceElementName, xpsNamespace),
                    new XAttribute("ResourceKey", "FillBrush"))),
            new XElement(
                XName.Get("Path.Stroke", xpsNamespace),
                new XElement(
                    XName.Get(resourceElementName, xpsNamespace),
                    new XAttribute("ResourceKey", "StrokeBrush"))));

        var fixedPage = new XDocument(
            new XElement(
                XName.Get("FixedPage", xpsNamespace),
                new XAttribute("Width", "816"),
                new XAttribute("Height", "1056"),
                new XAttribute(XNamespace.Xml + "lang", "en-US"),
                new XAttribute(XNamespace.Xmlns + "x", xamlNamespace),
                new XElement(
                    XName.Get("FixedPage.Resources", xpsNamespace),
                    new XElement(
                        XName.Get("ResourceDictionary", xpsNamespace),
                        new XElement(
                            XName.Get("SolidColorBrush", xpsNamespace),
                            new XAttribute(XName.Get("Key", xamlNamespace), "FillBrush"),
                            new XAttribute("Color", "#FF6688AA")),
                        new XElement(
                            XName.Get("SolidColorBrush", xpsNamespace),
                            new XAttribute(XName.Get("Key", xamlNamespace), "StrokeBrush"),
                            new XAttribute("Color", "#FF224466")))),
                shapePath));

        WriteSinglePageXpsPackage(archive, fixedPage);
        return Task.CompletedTask;
    }

    private static void WriteSinglePageXpsPackage(ZipArchive archive, XDocument fixedPage)
    {
        const string packageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string fixedRepresentationType = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
        const string xpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        const string contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypes = new XDocument(
            new XElement(
                XName.Get("Types", contentTypesNamespace),
                new XElement(XName.Get("Default", contentTypesNamespace), new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/FixedDoc.fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
                new XElement(XName.Get("Override", contentTypesNamespace), new XAttribute("PartName", "/Documents/1/Pages/1.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml"))));

        var rootRelationships = new XDocument(
            new XElement(
                XName.Get("Relationships", packageRelationshipsNamespace),
                new XElement(
                    XName.Get("Relationship", packageRelationshipsNamespace),
                    new XAttribute("Id", "R1"),
                    new XAttribute("Type", fixedRepresentationType),
                    new XAttribute("Target", "/FixedDocSeq.fdseq"))));

        var fixedDocumentSequence = new XDocument(
            new XElement(
                XName.Get("FixedDocumentSequence", xpsNamespace),
                new XElement(XName.Get("DocumentReference", xpsNamespace), new XAttribute("Source", "/Documents/1/FixedDoc.fdoc"))));

        var fixedDocument = new XDocument(
            new XElement(
                XName.Get("FixedDocument", xpsNamespace),
                new XElement(XName.Get("PageContent", xpsNamespace), new XAttribute("Source", "/Documents/1/Pages/1.fpage"))));

        WriteXmlEntry(archive, "[Content_Types].xml", contentTypes);
        WriteXmlEntry(archive, "_rels/.rels", rootRelationships);
        WriteXmlEntry(archive, "FixedDocSeq.fdseq", fixedDocumentSequence);
        WriteXmlEntry(archive, "Documents/1/FixedDoc.fdoc", fixedDocument);
        WriteXmlEntry(archive, "Documents/1/Pages/1.fpage", fixedPage);
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string entryPath, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string? ReadFirstPathFillRule(string xpsPath)
    {
        using var stream = File.OpenRead(xpsPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var pageEntry = archive.GetEntry("Documents/1/Pages/1.fpage");
        Assert.NotNull(pageEntry);
        using var pageStream = pageEntry!.Open();
        var page = XDocument.Load(pageStream);
        var path = page
            .Descendants()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal));
        Assert.NotNull(path);
        return path!.Attribute("FillRule")?.Value;
    }

    private static List<string> ExtractRunTextsInOrder(Document document)
    {
        var list = new List<string>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is RunInline run)
                {
                    var text = run.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text);
                    }
                }
            }
        }

        return list;
    }

    private static List<string> ExtractShapeHyperlinkUrisInOrder(Document document)
    {
        var list = new List<string>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ParagraphBlock paragraph)
            {
                continue;
            }

            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is ShapeInline shape
                    && !string.IsNullOrWhiteSpace(shape.Hyperlink?.Uri))
                {
                    list.Add(shape.Hyperlink!.Uri!);
                }
            }
        }

        return list;
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

    private sealed class TestXpsBridge : IXpsBridge
    {
        private readonly Dictionary<string, byte[]> _xpsPayloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[]? _fallbackPdfPayload;

        public TestXpsBridge(byte[]? fallbackPdfPayload = null)
        {
            _fallbackPdfPayload = fallbackPdfPayload is null
                ? null
                : (byte[])fallbackPdfPayload.Clone();
        }

        public int PdfToXpsCalls { get; private set; }

        public int XpsToPdfCalls { get; private set; }

        public XpsFlavor? LastExportFlavor { get; private set; }

        public XpsFlavor? LastImportFlavor { get; private set; }

        public Task ConvertXpsToPdfAsync(
            string sourcePath,
            string targetPdfPath,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XpsToPdfCalls++;
            LastImportFlavor = flavor;

            if (!_xpsPayloads.TryGetValue(sourcePath, out var payload))
            {
                if (_fallbackPdfPayload is null)
                {
                    throw new FileNotFoundException($"No test XPS payload registered for '{sourcePath}'.", sourcePath);
                }

                payload = _fallbackPdfPayload;
            }

            File.WriteAllBytes(targetPdfPath, payload);
            return Task.CompletedTask;
        }

        public Task ConvertPdfToXpsAsync(
            string sourcePdfPath,
            string targetPath,
            XpsFlavor flavor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PdfToXpsCalls++;
            LastExportFlavor = flavor;

            var payload = File.ReadAllBytes(sourcePdfPath);
            _xpsPayloads[targetPath] = payload;
            File.WriteAllText(
                targetPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><FixedDocumentSequence xmlns=\"http://schemas.microsoft.com/xps/2005/06\"/>");
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
