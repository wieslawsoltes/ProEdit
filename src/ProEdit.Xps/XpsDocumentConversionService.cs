using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Pdf;
using ProEdit.Pdf.Documents;
using ProEdit.Pdf.PdfPig;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;

namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Converts XPS/OXPS content to and from the immediate <see cref="Document"/> model.
/// </summary>
public sealed class XpsDocumentConversionService : IXpsDocumentConversionService
{
    private readonly XpsConversionOptions _options;
    private readonly IXpsBridge _xpsBridge;
    private readonly IPdfParser _pdfParser;

    public XpsDocumentConversionService()
        : this(xpsBridge: null, pdfParser: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XpsDocumentConversionService"/> class.
    /// </summary>
    /// <param name="xpsBridge">Optional XPS bridge implementation.</param>
    /// <param name="pdfParser">Optional PDF parser implementation.</param>
    /// <param name="xpsOptions">Optional conversion options used when bridge is not provided.</param>
    public XpsDocumentConversionService(
        IXpsBridge? xpsBridge,
        IPdfParser? pdfParser,
        XpsConversionOptions? xpsOptions = null)
    {
        _options = xpsOptions ?? new XpsConversionOptions();
        _pdfParser = pdfParser ?? new PdfPigParser();
        if (xpsBridge is not null)
        {
            _xpsBridge = xpsBridge;
            return;
        }

        XpsRuntimeOptions.ApplyEnvironmentOverrides(_options);
        _xpsBridge = new GhostscriptXpsBridge(_options);
    }

    /// <summary>
    /// Loads a document from an XPS/OXPS file.
    /// </summary>
    public async Task<Document> LoadAsync(
        string path,
        XpsFlavor flavor,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file '{path}' does not exist.", path);
        }

        var tempPdfPath = CreateTempPath(".pdf");
        var flavorDisplay = GetFlavorDisplayName(flavor);
        Exception? nativeException = null;
        var bridgeAttempted = false;
        try
        {
            if (ShouldTryNativeFirst())
            {
                try
                {
                    await using var input = File.OpenRead(path);
                    return XpsNativePackageConverter.Load(input, flavor);
                }
                catch (Exception ex) when (ShouldFallbackToGhostscript(ex))
                {
                    nativeException = ex;
                }
            }

            bridgeAttempted = true;
            await _xpsBridge.ConvertXpsToPdfAsync(path, tempPdfPath, flavor, cancellationToken);
            return await LoadFromPdfFileAsync(tempPdfPath, path, importOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = nativeException is null ? ex : new AggregateException(nativeException, ex);
            throw new InvalidOperationException(
                BuildLoadErrorMessage(flavorDisplay, path, fromStream: false, bridgeAttempted),
                error);
        }
        finally
        {
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Loads a document from an XPS/OXPS stream.
    /// </summary>
    public async Task<Document> LoadAsync(
        Stream sourceStream,
        XpsFlavor flavor,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        if (!sourceStream.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(sourceStream));
        }

        var sourcePath = CreateTempPath(flavor == XpsFlavor.Oxps ? ".oxps" : ".xps");
        var tempPdfPath = CreateTempPath(".pdf");
        var flavorDisplay = GetFlavorDisplayName(flavor);
        Exception? nativeException = null;
        var bridgeAttempted = false;
        try
        {
            await using (var file = File.Create(sourcePath))
            {
                await sourceStream.CopyToAsync(file, cancellationToken);
            }

            if (ShouldTryNativeFirst())
            {
                try
                {
                    await using var input = File.OpenRead(sourcePath);
                    return XpsNativePackageConverter.Load(input, flavor);
                }
                catch (Exception ex) when (ShouldFallbackToGhostscript(ex))
                {
                    nativeException = ex;
                }
            }

            bridgeAttempted = true;
            await _xpsBridge.ConvertXpsToPdfAsync(sourcePath, tempPdfPath, flavor, cancellationToken);
            return await LoadFromPdfFileAsync(tempPdfPath, sourcePath: null, importOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = nativeException is null ? ex : new AggregateException(nativeException, ex);
            throw new InvalidOperationException(
                BuildLoadErrorMessage(flavorDisplay, pathOrDescriptor: null, fromStream: true, bridgeAttempted),
                error);
        }
        finally
        {
            TryDeleteFile(sourcePath);
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Saves a document as XPS/OXPS to a file.
    /// </summary>
    public async Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        string path,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layoutSettings);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var tempPdfPath = CreateTempPath(".pdf");
        var flavorDisplay = GetFlavorDisplayName(flavor);
        Exception? nativeException = null;
        var bridgeAttempted = false;
        try
        {
            if (ShouldTryNativeFirst())
            {
                try
                {
                    await using var output = File.Create(path);
                    XpsNativePackageConverter.Save(document, output, flavor);
                    return;
                }
                catch (Exception ex) when (ShouldFallbackToGhostscript(ex))
                {
                    nativeException = ex;
                }
            }

            await SaveToPdfFileAsync(document, layoutSettings, tempPdfPath, cancellationToken);
            bridgeAttempted = true;
            await _xpsBridge.ConvertPdfToXpsAsync(tempPdfPath, path, flavor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = nativeException is null ? ex : new AggregateException(nativeException, ex);
            throw new InvalidOperationException(
                BuildSaveErrorMessage(flavorDisplay, path, toStream: false, bridgeAttempted),
                error);
        }
        finally
        {
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Saves a document as XPS/OXPS to a stream.
    /// </summary>
    public async Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream targetStream,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layoutSettings);
        ArgumentNullException.ThrowIfNull(targetStream);
        if (!targetStream.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(targetStream));
        }

        var targetPath = CreateTempPath(flavor == XpsFlavor.Oxps ? ".oxps" : ".xps");
        try
        {
            await SaveAsync(document, layoutSettings, targetPath, flavor, cancellationToken);
            await using var output = File.OpenRead(targetPath);
            await output.CopyToAsync(targetStream, cancellationToken);
        }
        finally
        {
            TryDeleteFile(targetPath);
        }
    }

    private bool ShouldTryNativeFirst()
    {
        return _options.EnableNativeConversion && _options.PreferNativeConversion;
    }

    private bool ShouldFallbackToGhostscript(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        return _options.FallbackToGhostscript;
    }

    private static string BuildLoadErrorMessage(
        string flavorDisplay,
        string? pathOrDescriptor,
        bool fromStream,
        bool bridgeAttempted)
    {
        if (fromStream)
        {
            return bridgeAttempted
                ? $"Unable to import {flavorDisplay} stream. Ensure Ghostscript is installed and configured."
                : $"Unable to import {flavorDisplay} stream using native package conversion.";
        }

        return bridgeAttempted
            ? $"Unable to import {flavorDisplay} file '{pathOrDescriptor}'. Ensure Ghostscript is installed and configured."
            : $"Unable to import {flavorDisplay} file '{pathOrDescriptor}' using native package conversion.";
    }

    private static string BuildSaveErrorMessage(
        string flavorDisplay,
        string pathOrDescriptor,
        bool toStream,
        bool bridgeAttempted)
    {
        if (toStream)
        {
            return bridgeAttempted
                ? $"Unable to export {flavorDisplay} stream. Ensure Ghostscript is installed and configured."
                : $"Unable to export {flavorDisplay} stream using native package conversion.";
        }

        return bridgeAttempted
            ? $"Unable to export {flavorDisplay} file '{pathOrDescriptor}'. Ensure Ghostscript is installed and configured."
            : $"Unable to export {flavorDisplay} file '{pathOrDescriptor}' using native package conversion.";
    }

    private async Task<Document> LoadFromPdfFileAsync(
        string path,
        string? sourcePath,
        PdfImportOptions? importOptions,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var options = importOptions ?? new PdfImportOptions();
        var pdfDocument = await Task.Run(
                () => _pdfParser.Parse(stream, options.ParserOptions),
                cancellationToken);
        pdfDocument.SourcePath = sourcePath;
        return PdfDocumentConverter.FromPdf(pdfDocument, options);
    }

    private static async Task SaveToPdfFileAsync(
        Document document,
        LayoutSettings layoutSettings,
        string path,
        CancellationToken cancellationToken)
    {
        var effectiveLayoutSettings = layoutSettings.Clone();
        effectiveLayoutSettings.UsePagination = true;
        effectiveLayoutSettings.PageFlow = PageFlowDirection.Vertical;
        effectiveLayoutSettings.ViewportWidth = effectiveLayoutSettings.PageWidth;
        effectiveLayoutSettings.ViewportHeight = effectiveLayoutSettings.PageHeight;

        var context = new DocumentPrintContext(document, effectiveLayoutSettings);
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
        var result = await printService.PrintAsync(context, settings, cancellationToken);
        if (!result.Succeeded)
        {
            throw new IOException($"PDF export failed: {result.Message}");
        }
    }

    private static string GetFlavorDisplayName(XpsFlavor flavor)
    {
        return flavor == XpsFlavor.Oxps ? "OXPS" : "XPS";
    }

    private static string CreateTempPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
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
            // best effort cleanup.
        }
    }
}
