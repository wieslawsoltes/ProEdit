using Vibe.Office.Documents;
using Vibe.Office.Documents.Formats;
using Vibe.Office.FlowDocument.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Markdown;
using Vibe.Office.OpenXml;
using Vibe.Office.Pdf;
using Vibe.Office.Pdf.Documents;
using Vibe.Office.Pdf.PdfPig;
using Vibe.Office.Printing;
using Vibe.Office.Printing.Documents;
using Vibe.Office.Printing.Skia;
using Vibe.Office.Printing.System;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Converts FlowDocument files through the shared VibeOffice document model.
/// </summary>
public sealed class FlowDocumentFileConversionService : IFlowDocumentFileConversionService
{
    private static readonly string[] LoadExtensions = [".docx", ".md", ".markdown", ".pdf", ".pdx"];
    private static readonly string[] SaveExtensions = [".docx", ".md", ".markdown", ".pdf", ".pdx"];

    private readonly FlowDocumentFileConversionOptions _options;
    private readonly IPdfParser _pdfParser;

    public FlowDocumentFileConversionService()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentFileConversionService"/> class.
    /// </summary>
    /// <param name="options">Conversion options.</param>
    public FlowDocumentFileConversionService(FlowDocumentFileConversionOptions? options)
    {
        _options = options ?? new FlowDocumentFileConversionOptions();
        _pdfParser = new PdfPigParser();
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

        Document document;
        switch (extension)
        {
            case ".docx":
                document = await Task.Run(() => new DocxImporter().Load(path), cancellationToken);
                break;
            case ".md":
            case ".markdown":
            {
                var markdown = await File.ReadAllTextAsync(path, cancellationToken);
                document = MarkdownDocumentConverter.FromMarkdown(markdown.AsSpan(), _options.MarkdownOptions);
                break;
            }
            case ".pdf":
            case ".pdx":
            {
                await using var stream = File.OpenRead(path);
                var pdfDocument = await Task.Run(
                        () => _pdfParser.Parse(stream, _options.PdfImportOptions.ParserOptions),
                        cancellationToken);
                pdfDocument.SourcePath = path;
                document = PdfDocumentConverter.FromPdf(pdfDocument, _options.PdfImportOptions);
                break;
            }
            default:
                throw new FlowDocumentFileFormatException($"Unsupported load extension '{extension}'.");
        }

        var converter = new DocumentToFlowDocumentConverter(_options.DocumentToFlowOptions);
        return converter.Convert(document);
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
            case ".docx":
                await Task.Run(() => new DocxExporter().Save(document, path), cancellationToken);
                return;
            case ".md":
            case ".markdown":
            {
                var markdown = MarkdownDocumentConverter.ToMarkdown(document, _options.MarkdownOptions);
                await File.WriteAllTextAsync(path, markdown, cancellationToken);
                return;
            }
            case ".pdf":
            case ".pdx":
                await SavePdfAsync(document, flowDocument, path, cancellationToken);
                return;
            default:
                throw new FlowDocumentFileFormatException($"Unsupported save extension '{extension}'.");
        }
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
}
