using ProEdit.Documents;
using ProEdit.Documents.Formats;
using ProEdit.FlowDocument.Documents;
using ProEdit.FlowDocument.IO.Formats;
using ProEdit.Layout;
using ProEdit.Pdf;
using ProEdit.Pdf.Documents;
using ProEdit.Pdf.PdfPig;
using ProEdit.Printing;
using ProEdit.Printing.Documents;
using ProEdit.Printing.Skia;
using ProEdit.Printing.System;
using FlowDocumentModel = ProEdit.FlowDocument.FlowDocument;

namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Converts FlowDocument files through the shared ProEdit document model.
/// </summary>
public sealed class FlowDocumentFileConversionService : IFlowDocumentFileConversionService
{
    private static readonly string[] LoadExtensions = [".docx", ".md", ".markdown", ".rtf", ".pdf", ".pdx", ".ps", ".eps", ".xps", ".oxps"];
    private static readonly string[] SaveExtensions = [".docx", ".md", ".markdown", ".rtf", ".pdf", ".pdx", ".ps", ".eps", ".xps", ".oxps"];

    private readonly FlowDocumentFileConversionOptions _options;
    private readonly IPdfParser _pdfParser;
    private readonly IPostScriptDocumentConversionService _postScriptDocumentConversionService;
    private readonly IXpsDocumentConversionService _xpsDocumentConversionService;
    private readonly DocumentFormatRegistry _registry;

    public FlowDocumentFileConversionService()
        : this(
            options: null,
            pdfParser: null,
            postScriptDocumentConversionService: null,
            xpsDocumentConversionService: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileConversionService"/> class.
    /// </summary>
    /// <param name="options">Conversion options.</param>
    public FlowDocumentFileConversionService(FlowDocumentFileConversionOptions? options)
        : this(
            options,
            pdfParser: null,
            postScriptDocumentConversionService: null,
            xpsDocumentConversionService: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileConversionService"/> class.
    /// </summary>
    /// <param name="options">Conversion options.</param>
    /// <param name="postScriptBridge">Optional PostScript bridge implementation.</param>
    /// <param name="pdfParser">Optional PDF parser implementation.</param>
    /// <param name="postScriptDocumentConversionService">Optional PostScript document conversion service.</param>
    /// <param name="xpsBridge">Optional XPS bridge implementation.</param>
    /// <param name="xpsDocumentConversionService">Optional XPS document conversion service.</param>
    public FlowDocumentFileConversionService(
        FlowDocumentFileConversionOptions? options,
        IPostScriptBridge? postScriptBridge,
        IPdfParser? pdfParser,
        IPostScriptDocumentConversionService? postScriptDocumentConversionService = null,
        IXpsBridge? xpsBridge = null,
        IXpsDocumentConversionService? xpsDocumentConversionService = null)
        : this(
            options,
            pdfParser,
            postScriptDocumentConversionService
            ?? (postScriptBridge is null
                ? null
                : new PostScriptDocumentConversionService(postScriptBridge, pdfParser)),
            xpsDocumentConversionService
            ?? (xpsBridge is null
                ? null
                : new XpsDocumentConversionService(xpsBridge, pdfParser)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileConversionService"/> class.
    /// </summary>
    /// <param name="options">Conversion options.</param>
    /// <param name="pdfParser">Optional PDF parser implementation.</param>
    /// <param name="postScriptDocumentConversionService">Optional PostScript document conversion service.</param>
    /// <param name="xpsDocumentConversionService">Optional XPS document conversion service.</param>
    public FlowDocumentFileConversionService(
        FlowDocumentFileConversionOptions? options,
        IPdfParser? pdfParser,
        IPostScriptDocumentConversionService? postScriptDocumentConversionService = null,
        IXpsDocumentConversionService? xpsDocumentConversionService = null)
    {
        _options = options ?? new FlowDocumentFileConversionOptions();
        _pdfParser = pdfParser ?? new PdfPigParser();
        _postScriptDocumentConversionService = postScriptDocumentConversionService
            ?? CreateDefaultPostScriptDocumentConversionService(_options.PostScriptOptions, _pdfParser);
        _xpsDocumentConversionService = xpsDocumentConversionService
            ?? CreateDefaultXpsDocumentConversionService(_options.XpsOptions, _pdfParser);
        _registry = new DocumentFormatRegistry();
        _registry.Register(new DocxDocumentFormat());
        _registry.Register(new MarkdownDocumentFormat(_options.MarkdownOptions));
        _registry.Register(new RtfDocumentFormat());
    }

    public IReadOnlyList<string> SupportedLoadExtensions => LoadExtensions;
    public IReadOnlyList<string> SupportedSaveExtensions => SaveExtensions;

    public bool CanLoad(string pathOrExtension)
    {
        var extension = NormalizeExtension(pathOrExtension);
        return IsInSet(extension, LoadExtensions);
    }

    public bool CanSave(string pathOrExtension)
    {
        var extension = NormalizeExtension(pathOrExtension);
        return IsInSet(extension, SaveExtensions);
    }

    public async Task<FlowDocumentModel> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var extension = NormalizeExtension(path);
        if (!IsInSet(extension, LoadExtensions))
        {
            throw new FlowDocumentFileFormatException($"Unsupported load extension '{extension}'.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file '{path}' does not exist.", path);
        }

        if (IsXpsExtension(extension))
        {
            var flavor = extension == ".oxps" ? XpsFlavor.Oxps : XpsFlavor.Xps;
            Document document;
            try
            {
                document = await _xpsDocumentConversionService.LoadAsync(
                    path,
                    flavor,
                    _options.PdfImportOptions,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
            {
                throw CreateXpsLoadException(flavor, path, fromStream: false, ex);
            }

            var converter = new DocumentToFlowDocumentConverter(_options.DocumentToFlowOptions);
            return converter.Convert(document);
        }

        if (IsPostScriptExtension(extension))
        {
            var kind = string.Equals(extension, ".eps", StringComparison.OrdinalIgnoreCase)
                ? PostScriptKind.Eps
                : PostScriptKind.Ps;
            Document document;
            try
            {
                document = await _postScriptDocumentConversionService.LoadAsync(
                    path,
                    kind,
                    _options.PdfImportOptions,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
            {
                throw CreatePostScriptLoadException(kind, path, fromStream: false, ex);
            }

            var converter = new DocumentToFlowDocumentConverter(_options.DocumentToFlowOptions);
            return converter.Convert(document);
        }

        await using var stream = File.OpenRead(path);
        return await LoadCoreAsync(stream, extension, path, cancellationToken);
    }

    public Task<FlowDocumentModel> LoadAsync(Stream stream, string pathOrExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(stream));
        }

        var extension = NormalizeExtension(pathOrExtension);
        return LoadCoreAsync(stream, extension, sourcePath: null, cancellationToken);
    }

    public async Task SaveAsync(FlowDocumentModel flowDocument, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flowDocument);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var extension = NormalizeExtension(path);
        if (!IsInSet(extension, SaveExtensions))
        {
            throw new FlowDocumentFileFormatException($"Unsupported save extension '{extension}'.");
        }

        var converter = new FlowDocumentConverter(_options.FlowToDocumentOptions);
        var document = converter.Convert(flowDocument);

        switch (extension)
        {
            case ".pdf":
            case ".pdx":
                await SavePdfAsync(document, flowDocument, path, cancellationToken);
                return;
            case ".xps":
            case ".oxps":
            {
                var flavor = extension == ".oxps" ? XpsFlavor.Oxps : XpsFlavor.Xps;
                try
                {
                    await _xpsDocumentConversionService.SaveAsync(
                        document,
                        BuildPdfLayoutSettings(flowDocument),
                        path,
                        flavor,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreateXpsSaveException(flavor, path, toStream: false, ex);
                }

                return;
            }
            case ".ps":
            case ".eps":
            {
                var kind = extension == ".eps" ? PostScriptKind.Eps : PostScriptKind.Ps;
                try
                {
                    await _postScriptDocumentConversionService.SaveAsync(
                        document,
                        BuildPdfLayoutSettings(flowDocument),
                        path,
                        kind,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreatePostScriptSaveException(kind, path, toStream: false, ex);
                }

                return;
            }
            default:
            {
                if (!_registry.TryGetByExtension(extension, out var format) || !format.CanSave)
                {
                    throw new FlowDocumentFileFormatException($"Unsupported save extension '{extension}'.");
                }

                await using var stream = File.Create(path);
                await Task.Run(() => format.Save(document, stream), cancellationToken);
                return;
            }
        }
    }

    public async Task SaveAsync(
        FlowDocumentModel flowDocument,
        Stream stream,
        string pathOrExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flowDocument);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(stream));
        }

        var extension = NormalizeExtension(pathOrExtension);
        if (!IsInSet(extension, SaveExtensions))
        {
            throw new FlowDocumentFileFormatException($"Unsupported save extension '{extension}'.");
        }

        var converter = new FlowDocumentConverter(_options.FlowToDocumentOptions);
        var document = converter.Convert(flowDocument);
        switch (extension)
        {
            case ".pdf":
            case ".pdx":
                await SavePdfToStreamAsync(document, flowDocument, stream, cancellationToken);
                return;
            case ".xps":
            case ".oxps":
            {
                var flavor = extension == ".oxps" ? XpsFlavor.Oxps : XpsFlavor.Xps;
                try
                {
                    await _xpsDocumentConversionService.SaveAsync(
                        document,
                        BuildPdfLayoutSettings(flowDocument),
                        stream,
                        flavor,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreateXpsSaveException(flavor, pathOrExtension, toStream: true, ex);
                }

                return;
            }
            case ".ps":
            case ".eps":
            {
                var kind = extension == ".eps" ? PostScriptKind.Eps : PostScriptKind.Ps;
                try
                {
                    await _postScriptDocumentConversionService.SaveAsync(
                        document,
                        BuildPdfLayoutSettings(flowDocument),
                        stream,
                        kind,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreatePostScriptSaveException(kind, pathOrExtension, toStream: true, ex);
                }

                return;
            }
            default:
            {
                if (!_registry.TryGetByExtension(extension, out var format) || !format.CanSave)
                {
                    throw new FlowDocumentFileFormatException($"Unsupported save extension '{extension}'.");
                }

                await Task.Run(() => format.Save(document, stream), cancellationToken);
                return;
            }
        }
    }

    private async Task<FlowDocumentModel> LoadCoreAsync(
        Stream stream,
        string extension,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        if (!IsInSet(extension, LoadExtensions))
        {
            throw new FlowDocumentFileFormatException($"Unsupported load extension '{extension}'.");
        }

        Document document;
        switch (extension)
        {
            case ".pdf":
            case ".pdx":
                document = await LoadPdfAsync(stream, sourcePath, cancellationToken);
                break;
            case ".xps":
            case ".oxps":
            {
                var flavor = extension == ".oxps" ? XpsFlavor.Oxps : XpsFlavor.Xps;
                try
                {
                    document = sourcePath is null
                        ? await _xpsDocumentConversionService.LoadAsync(stream, flavor, _options.PdfImportOptions, cancellationToken)
                        : await _xpsDocumentConversionService.LoadAsync(sourcePath, flavor, _options.PdfImportOptions, cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreateXpsLoadException(flavor, sourcePath ?? extension, fromStream: sourcePath is null, ex);
                }

                break;
            }
            case ".ps":
            case ".eps":
            {
                var kind = extension == ".eps" ? PostScriptKind.Eps : PostScriptKind.Ps;
                try
                {
                    document = sourcePath is null
                        ? await _postScriptDocumentConversionService.LoadAsync(stream, kind, _options.PdfImportOptions, cancellationToken)
                        : await _postScriptDocumentConversionService.LoadAsync(sourcePath, kind, _options.PdfImportOptions, cancellationToken);
                }
                catch (Exception ex) when (ex is not FlowDocumentFileFormatException and not OperationCanceledException)
                {
                    throw CreatePostScriptLoadException(kind, sourcePath ?? extension, fromStream: sourcePath is null, ex);
                }

                break;
            }
            default:
            {
                if (!_registry.TryGetByExtension(extension, out var format) || !format.CanLoad)
                {
                    throw new FlowDocumentFileFormatException($"Unsupported load extension '{extension}'.");
                }

                document = await Task.Run(() => format.Load(stream), cancellationToken);
                break;
            }
        }

        var converter = new DocumentToFlowDocumentConverter(_options.DocumentToFlowOptions);
        return converter.Convert(document);
    }

    private async Task<Document> LoadPdfAsync(Stream stream, string? sourcePath, CancellationToken cancellationToken)
    {
        var pdfDocument = await Task.Run(
                () => _pdfParser.Parse(stream, _options.PdfImportOptions.ParserOptions),
                cancellationToken);
        pdfDocument.SourcePath = sourcePath;
        return PdfDocumentConverter.FromPdf(pdfDocument, _options.PdfImportOptions);
    }

    private async Task SavePdfAsync(
        Document document,
        FlowDocumentModel flowDocument,
        string path,
        CancellationToken cancellationToken)
    {
        var layoutSettings = BuildPdfLayoutSettings(flowDocument);
        var context = new DocumentPrintContext(document, layoutSettings);
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

    private async Task SavePdfToStreamAsync(
        Document document,
        FlowDocumentModel flowDocument,
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var tempPdfPath = CreateTempPath(".pdf");
        try
        {
            await SavePdfAsync(document, flowDocument, tempPdfPath, cancellationToken);
            await using var input = File.OpenRead(tempPdfPath);
            await input.CopyToAsync(stream, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempPdfPath);
        }
    }

    private LayoutSettings BuildPdfLayoutSettings(FlowDocumentModel source)
    {
        var settings = _options.PdfExportLayoutSettings?.Clone() ?? new LayoutSettings();
        settings.UsePagination = true;
        settings.PageFlow = PageFlowDirection.Vertical;

        if (source.PageWidth.HasValue && source.PageWidth.Value > 0d)
        {
            settings.PageWidth = (float)source.PageWidth.Value;
        }

        if (source.PageHeight.HasValue && source.PageHeight.Value > 0d)
        {
            settings.PageHeight = (float)source.PageHeight.Value;
        }

        if (!source.PagePadding.IsEmpty)
        {
            settings.MarginLeft = (float)source.PagePadding.Left;
            settings.MarginTop = (float)source.PagePadding.Top;
            settings.MarginRight = (float)source.PagePadding.Right;
            settings.MarginBottom = (float)source.PagePadding.Bottom;
        }

        if (source.ColumnGap.HasValue && source.ColumnGap.Value >= 0d)
        {
            settings.ColumnGap = (float)source.ColumnGap.Value;
        }

        settings.ViewportWidth = settings.PageWidth;
        settings.ViewportHeight = settings.PageHeight;
        return settings;
    }

    private static string NormalizeExtension(string pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = pathOrExtension;
        }

        return DocumentFormatRegistry.NormalizeExtension(extension);
    }

    private static bool IsInSet(string extension, IReadOnlyList<string> set)
    {
        for (var i = 0; i < set.Count; i++)
        {
            if (string.Equals(set[i], extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostScriptExtension(string extension)
    {
        return string.Equals(extension, ".ps", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".eps", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXpsExtension(string extension)
    {
        return string.Equals(extension, ".xps", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".oxps", StringComparison.OrdinalIgnoreCase);
    }

    private static IPostScriptDocumentConversionService CreateDefaultPostScriptDocumentConversionService(
        PostScriptConversionOptions options,
        IPdfParser pdfParser)
    {
        PostScriptRuntimeOptions.ApplyEnvironmentOverrides(options);
        var bridge = new GhostscriptPostScriptBridge(options);
        return new PostScriptDocumentConversionService(bridge, pdfParser);
    }

    private static IXpsDocumentConversionService CreateDefaultXpsDocumentConversionService(
        XpsConversionOptions options,
        IPdfParser pdfParser)
    {
        XpsRuntimeOptions.ApplyEnvironmentOverrides(options);
        var bridge = new GhostscriptXpsBridge(options);
        return new XpsDocumentConversionService(bridge, pdfParser, options);
    }

    private static FlowDocumentFileFormatException CreatePostScriptLoadException(
        PostScriptKind kind,
        string pathOrExtension,
        bool fromStream,
        Exception innerException)
    {
        if (fromStream)
        {
            return new FlowDocumentFileFormatException(
                $"Unable to import {kind} stream. Ensure Ghostscript is installed and configured.",
                innerException);
        }

        return new FlowDocumentFileFormatException(
            $"Unable to import {kind} file '{pathOrExtension}'. Ensure Ghostscript is installed and configured.",
            innerException);
    }

    private static FlowDocumentFileFormatException CreatePostScriptSaveException(
        PostScriptKind kind,
        string pathOrExtension,
        bool toStream,
        Exception innerException)
    {
        if (toStream)
        {
            return new FlowDocumentFileFormatException(
                $"Unable to export {kind} stream. Ensure Ghostscript is installed and configured.",
                innerException);
        }

        return new FlowDocumentFileFormatException(
            $"Unable to export {kind} file '{pathOrExtension}'. Ensure Ghostscript is installed and configured.",
            innerException);
    }

    private static FlowDocumentFileFormatException CreateXpsLoadException(
        XpsFlavor flavor,
        string pathOrExtension,
        bool fromStream,
        Exception innerException)
    {
        var displayName = flavor == XpsFlavor.Oxps ? "OXPS" : "XPS";
        if (fromStream)
        {
            return new FlowDocumentFileFormatException(
                $"Unable to import {displayName} stream. Ensure Ghostscript is installed and configured.",
                innerException);
        }

        return new FlowDocumentFileFormatException(
            $"Unable to import {displayName} file '{pathOrExtension}'. Ensure Ghostscript is installed and configured.",
            innerException);
    }

    private static FlowDocumentFileFormatException CreateXpsSaveException(
        XpsFlavor flavor,
        string pathOrExtension,
        bool toStream,
        Exception innerException)
    {
        var displayName = flavor == XpsFlavor.Oxps ? "OXPS" : "XPS";
        if (toStream)
        {
            return new FlowDocumentFileFormatException(
                $"Unable to export {displayName} stream. Ensure Ghostscript is installed and configured.",
                innerException);
        }

        return new FlowDocumentFileFormatException(
            $"Unable to export {displayName} file '{pathOrExtension}'. Ensure Ghostscript is installed and configured.",
            innerException);
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
