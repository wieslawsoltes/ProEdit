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
/// Converts PostScript/EPS content to and from the immediate <see cref="Document"/> model.
/// </summary>
public sealed class PostScriptDocumentConversionService : IPostScriptDocumentConversionService
{
    private readonly IPostScriptBridge _postScriptBridge;
    private readonly IPdfParser _pdfParser;

    public PostScriptDocumentConversionService()
        : this(postScriptBridge: null, pdfParser: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostScriptDocumentConversionService"/> class.
    /// </summary>
    /// <param name="postScriptBridge">Optional PostScript bridge implementation.</param>
    /// <param name="pdfParser">Optional PDF parser implementation.</param>
    /// <param name="postScriptOptions">Optional conversion options used when bridge is not provided.</param>
    public PostScriptDocumentConversionService(
        IPostScriptBridge? postScriptBridge,
        IPdfParser? pdfParser,
        PostScriptConversionOptions? postScriptOptions = null)
    {
        _pdfParser = pdfParser ?? new PdfPigParser();
        if (postScriptBridge is not null)
        {
            _postScriptBridge = postScriptBridge;
            return;
        }

        var options = postScriptOptions ?? new PostScriptConversionOptions();
        PostScriptRuntimeOptions.ApplyEnvironmentOverrides(options);
        _postScriptBridge = new GhostscriptPostScriptBridge(options);
    }

    /// <summary>
    /// Loads a document from a PostScript/EPS file.
    /// </summary>
    public async Task<Document> LoadAsync(
        string path,
        PostScriptKind kind,
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
        try
        {
            await _postScriptBridge.ConvertPostScriptToPdfAsync(path, tempPdfPath, kind, cancellationToken);
            return await LoadFromPdfFileAsync(tempPdfPath, path, importOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to import {kind} file '{path}'. Ensure Ghostscript is installed and configured.",
                ex);
        }
        finally
        {
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Loads a document from a PostScript/EPS stream.
    /// </summary>
    public async Task<Document> LoadAsync(
        Stream sourceStream,
        PostScriptKind kind,
        PdfImportOptions? importOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        if (!sourceStream.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(sourceStream));
        }

        var sourcePath = CreateTempPath(kind == PostScriptKind.Eps ? ".eps" : ".ps");
        var tempPdfPath = CreateTempPath(".pdf");
        try
        {
            await using (var file = File.Create(sourcePath))
            {
                await sourceStream.CopyToAsync(file, cancellationToken);
            }

            await _postScriptBridge.ConvertPostScriptToPdfAsync(sourcePath, tempPdfPath, kind, cancellationToken);
            return await LoadFromPdfFileAsync(tempPdfPath, sourcePath: null, importOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to import {kind} stream. Ensure Ghostscript is installed and configured.",
                ex);
        }
        finally
        {
            TryDeleteFile(sourcePath);
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Saves a document as PostScript/EPS to a file.
    /// </summary>
    public async Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        string path,
        PostScriptKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layoutSettings);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var tempPdfPath = CreateTempPath(".pdf");
        try
        {
            await SaveToPdfFileAsync(document, layoutSettings, tempPdfPath, cancellationToken);
            await _postScriptBridge.ConvertPdfToPostScriptAsync(tempPdfPath, path, kind, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to export {kind} file '{path}'. Ensure Ghostscript is installed and configured.",
                ex);
        }
        finally
        {
            TryDeleteFile(tempPdfPath);
        }
    }

    /// <summary>
    /// Saves a document as PostScript/EPS to a stream.
    /// </summary>
    public async Task SaveAsync(
        Document document,
        LayoutSettings layoutSettings,
        Stream targetStream,
        PostScriptKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layoutSettings);
        ArgumentNullException.ThrowIfNull(targetStream);
        if (!targetStream.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(targetStream));
        }

        var targetPath = CreateTempPath(kind == PostScriptKind.Eps ? ".eps" : ".ps");
        try
        {
            await SaveAsync(document, layoutSettings, targetPath, kind, cancellationToken);
            await using var output = File.OpenRead(targetPath);
            await output.CopyToAsync(targetStream, cancellationToken);
        }
        finally
        {
            TryDeleteFile(targetPath);
        }
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
