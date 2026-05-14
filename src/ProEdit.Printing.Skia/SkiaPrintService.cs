using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Primitives;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;

namespace ProEdit.Printing.Skia;

public sealed class SkiaPrintService : IPrintService
{
    private const float DipsPerInch = 96f;
    private const float PdfPointsPerInch = 72f;
    private const float DefaultPreviewDpi = 120f;
    private static readonly float[] GrayScaleMatrix =
    {
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0
    };

    private readonly DocumentLayouter _layouter = new();
    private readonly IPrinterDiscovery _printerDiscovery;
    private readonly IPrintTransport _printTransport;

    public SkiaPrintService(IPrinterDiscovery printerDiscovery, IPrintTransport printTransport)
    {
        _printerDiscovery = printerDiscovery ?? throw new ArgumentNullException(nameof(printerDiscovery));
        _printTransport = printTransport ?? throw new ArgumentNullException(nameof(printTransport));
    }

    public ValueTask<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        return _printerDiscovery.GetPrintersAsync(cancellationToken);
    }

    public ValueTask<PrinterInfo?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
    {
        return _printerDiscovery.GetDefaultPrinterAsync(cancellationToken);
    }

    public ValueTask<PrintPreviewResult> BuildPreviewAsync(PrintPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ValueTask<PrintPreviewResult>(Task.Run(() => BuildPreviewInternal(request, cancellationToken), cancellationToken));
    }

    public ValueTask<PrintJobResult> PrintAsync(IPrintDocumentInfo documentInfo, PrintSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentInfo);
        ArgumentNullException.ThrowIfNull(settings);
        return new ValueTask<PrintJobResult>(Task.Run(() => PrintInternal(RequireDocumentInfo(documentInfo), settings, cancellationToken), cancellationToken));
    }

    private PrintPreviewResult BuildPreviewInternal(PrintPreviewRequest request, CancellationToken cancellationToken)
    {
        var documentInfo = RequireDocumentInfo(request.DocumentInfo);
        var settings = request.Settings.Clone();
        settings.Normalize();

        using var resolver = new SkiaDocumentFontResolver(documentInfo.Document.Fonts);
        var measurer = new SkiaTextMeasurer
        {
            TypefaceResolver = resolver
        };

        var layoutSettings = BuildLayoutSettings(documentInfo.LayoutSettings, settings);
        var layout = _layouter.Layout(documentInfo.Document, layoutSettings, measurer);
        var printablePages = ResolvePrintablePageIndices(layout, documentInfo, settings);
        var requestedPages = request.PageIndices is { Count: > 0 }
            ? request.PageIndices.Where(page => printablePages.Contains(page)).ToArray()
            : printablePages;

        if (requestedPages.Count == 0 && printablePages.Count > 0)
        {
            requestedPages = new[] { printablePages[0] };
        }

        var renderer = new SkiaDocumentRenderer
        {
            TypefaceResolver = resolver
        };

        var renderOptions = BuildRenderOptions(settings);
        var dpi = request.Dpi > 0f ? request.Dpi : DefaultPreviewDpi;
        var previews = new List<PrintPreviewPage>(requestedPages.Count);

        foreach (var pageIndex in requestedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((uint)pageIndex >= (uint)layout.Pages.Count)
            {
                continue;
            }

            var preview = RenderPreviewPage(documentInfo.Document, layout, renderer, renderOptions, pageIndex, dpi, settings);
            if (preview is not null)
            {
                previews.Add(preview);
            }
        }

        return new PrintPreviewResult(layout.Pages.Count, printablePages, previews);
    }

    private PrintJobResult PrintInternal(DocumentPrintContext documentInfo, PrintSettings settings, CancellationToken cancellationToken)
    {
        settings.Normalize();
        using var resolver = new SkiaDocumentFontResolver(documentInfo.Document.Fonts);
        var measurer = new SkiaTextMeasurer
        {
            TypefaceResolver = resolver
        };

        var layoutSettings = BuildLayoutSettings(documentInfo.LayoutSettings, settings);
        var layout = _layouter.Layout(documentInfo.Document, layoutSettings, measurer);
        var printablePages = ResolvePrintablePageIndices(layout, documentInfo, settings);
        if (printablePages.Count == 0)
        {
            return PrintJobResult.Failed("No pages to print.");
        }

        var renderOptions = BuildRenderOptions(settings);
        var outputPath = settings.OutputKind == PrintOutputKind.Pdf
            ? settings.OutputPath
            : null;

        if (settings.OutputKind == PrintOutputKind.Pdf && string.IsNullOrWhiteSpace(outputPath))
        {
            return PrintJobResult.Failed("No output path specified for PDF.");
        }

        var tempPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"proedit-print-{Guid.NewGuid():N}.pdf")
            : outputPath!;

        var renderer = new SkiaDocumentRenderer
        {
            TypefaceResolver = resolver
        };

        try
        {
            RenderPdf(documentInfo.Document, layout, renderer, renderOptions, tempPath, printablePages, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            return PrintJobResult.Failed($"Failed to render print output: {ex.Message}");
        }

        if (settings.OutputKind == PrintOutputKind.Pdf)
        {
            return PrintJobResult.Success(tempPath);
        }

        var printResult = _printTransport.SendToPrinterAsync(tempPath, settings, cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (printResult.Succeeded)
        {
            TryDeleteFile(tempPath);
        }

        return printResult;
    }

    private static LayoutSettings BuildLayoutSettings(LayoutSettings baseSettings, PrintSettings settings)
    {
        var layoutSettings = baseSettings.Clone();
        layoutSettings.UsePagination = true;
        layoutSettings.PageFlow = PageFlowDirection.Vertical;
        if (settings.PaperSize is not null)
        {
            layoutSettings.PageWidth = settings.PaperSize.Width;
            layoutSettings.PageHeight = settings.PaperSize.Height;
        }

        layoutSettings.ViewportWidth = layoutSettings.PageWidth;
        layoutSettings.ViewportHeight = layoutSettings.PageHeight;

        if (settings.Orientation != PrintOrientationMode.Auto)
        {
            var isLandscape = layoutSettings.PageWidth > layoutSettings.PageHeight;
            var shouldLandscape = settings.Orientation == PrintOrientationMode.Landscape;
            if (isLandscape != shouldLandscape)
            {
                (layoutSettings.PageWidth, layoutSettings.PageHeight) = (layoutSettings.PageHeight, layoutSettings.PageWidth);
            }
        }

        return layoutSettings;
    }

    private static RenderOptions BuildRenderOptions(PrintSettings settings)
    {
        return new RenderOptions
        {
            BackgroundColor = DocColor.White,
            PageColor = DocColor.White,
            PageBorderColor = DocColor.Transparent,
            PageBorderThickness = 0f,
            ShowCaret = false,
            ShowInvisibles = false,
            ShowLayout = false,
            ShowGridlines = false,
            UsePictureCache = false,
            Selection = null,
            SelectionRanges = null,
            HeaderFooterMode = HeaderFooterEditMode.None
        };
    }

    private static IReadOnlyList<int> ResolvePrintablePageIndices(DocumentLayout layout, DocumentPrintContext documentInfo, PrintSettings settings)
    {
        var pageCount = layout.Pages.Count;
        if (pageCount == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new List<int>();
        void AddRange(int start, int end)
        {
            if (start > end)
            {
                (start, end) = (end, start);
            }

            start = Math.Clamp(start, 0, pageCount - 1);
            end = Math.Clamp(end, 0, pageCount - 1);
            for (var i = start; i <= end; i++)
            {
                indices.Add(i);
            }
        }

        switch (settings.RangeKind)
        {
            case PrintRangeKind.All:
                AddRange(0, pageCount - 1);
                break;
            case PrintRangeKind.CurrentPage:
                AddRange(documentInfo.CurrentPageIndex ?? 0, documentInfo.CurrentPageIndex ?? 0);
                break;
            case PrintRangeKind.Selection:
                if (settings.CustomRanges.Count > 0)
                {
                    foreach (var range in settings.CustomRanges)
                    {
                        AddRange(range.Start - 1, range.End - 1);
                    }
                    break;
                }
                if (documentInfo.Selection is { } selection && !selection.IsEmpty)
                {
                    if (DocumentPrintSelection.TryResolveSelectionPageRange(layout, selection, out var range))
                    {
                        AddRange(range.start, range.end);
                        break;
                    }
                }

                AddRange(0, pageCount - 1);
                break;
            case PrintRangeKind.CustomPages:
                if (settings.CustomRanges.Count == 0)
                {
                    AddRange(0, pageCount - 1);
                    break;
                }

                foreach (var range in settings.CustomRanges)
                {
                    AddRange(range.Start - 1, range.End - 1);
                }
                break;
            default:
                AddRange(0, pageCount - 1);
                break;
        }

        return indices.Distinct().OrderBy(value => value).ToArray();
    }

    private static PrintPreviewPage? RenderPreviewPage(
        Document document,
        DocumentLayout layout,
        SkiaDocumentRenderer renderer,
        RenderOptions options,
        int pageIndex,
        float dpi,
        PrintSettings settings)
    {
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return null;
        }

        var page = layout.Pages[pageIndex];
        var scale = ResolveScale(settings, dpi);
        var widthPx = (int)MathF.Ceiling(layout.Settings.PageWidth * dpi / DipsPerInch);
        var heightPx = (int)MathF.Ceiling(layout.Settings.PageHeight * dpi / DipsPerInch);
        using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.Translate(-page.Bounds.X, -page.Bounds.Y);

        using var grayscaleLayer = settings.ColorMode == PrintColorMode.Grayscale
            ? new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(GrayScaleMatrix) }
            : null;

        if (grayscaleLayer is not null)
        {
            canvas.SaveLayer(grayscaleLayer);
        }

        options.VisibleBounds = page.Bounds;
        renderer.Render(canvas, document, layout, options);

        if (grayscaleLayer is not null)
        {
            canvas.Restore();
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data is null
            ? null
            : new PrintPreviewPage(pageIndex + 1, data.ToArray(), widthPx, heightPx);
    }

    private static void RenderPdf(
        Document document,
        DocumentLayout layout,
        SkiaDocumentRenderer renderer,
        RenderOptions options,
        string outputPath,
        IReadOnlyList<int> pageIndices,
        PrintSettings settings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var pdf = SKDocument.CreatePdf(stream);
        if (pdf is null)
        {
            throw new InvalidOperationException("Unable to create PDF document.");
        }

        var scale = ResolveScale(settings, PdfPointsPerInch);
        var pageWidth = layout.Settings.PageWidth * PdfPointsPerInch / DipsPerInch;
        var pageHeight = layout.Settings.PageHeight * PdfPointsPerInch / DipsPerInch;

        foreach (var pageIndex in pageIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((uint)pageIndex >= (uint)layout.Pages.Count)
            {
                continue;
            }

            var page = layout.Pages[pageIndex];
            using var canvas = pdf.BeginPage(pageWidth, pageHeight);
            canvas.Clear(SKColors.White);
            canvas.Scale(scale);
            canvas.Translate(-page.Bounds.X, -page.Bounds.Y);

            using var grayscaleLayer = settings.ColorMode == PrintColorMode.Grayscale
                ? new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(GrayScaleMatrix) }
                : null;

            if (grayscaleLayer is not null)
            {
                canvas.SaveLayer(grayscaleLayer);
            }

            options.VisibleBounds = page.Bounds;
            renderer.Render(canvas, document, layout, options);

            if (grayscaleLayer is not null)
            {
                canvas.Restore();
            }

            pdf.EndPage();
        }

        pdf.Close();
    }

    private static float ResolveScale(PrintSettings settings, float targetDpi)
    {
        var scale = targetDpi / DipsPerInch;
        if (settings.Scaling == PrintScalingMode.Custom)
        {
            scale *= MathF.Max(0.1f, settings.CustomScale);
        }

        return scale;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static DocumentPrintContext RequireDocumentInfo(IPrintDocumentInfo documentInfo)
    {
        if (documentInfo is DocumentPrintContext info)
        {
            return info;
        }

        throw new InvalidOperationException("SkiaPrintService requires a DocumentPrintContext instance.");
    }
}
