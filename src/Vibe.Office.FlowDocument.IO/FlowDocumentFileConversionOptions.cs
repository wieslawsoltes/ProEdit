using Vibe.Office.FlowDocument.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Markdown;
using Vibe.Office.Pdf;

namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Provides options for converting between FlowDocument and file formats.
/// </summary>
public sealed class FlowDocumentFileConversionOptions
{
    /// <summary>
    /// Gets markdown conversion options used for markdown load/save.
    /// </summary>
    public MarkdownOptions MarkdownOptions { get; } = new()
    {
        Flavor = MarkdownFlavor.GitHub,
        UseGfmTables = true,
        UseTaskLists = true,
        UseStrikethrough = true
    };

    /// <summary>
    /// Gets PDF import options used when loading PDF/PDX files.
    /// </summary>
    public PdfImportOptions PdfImportOptions { get; } = new();

    /// <summary>
    /// Gets FlowDocument to Document conversion options used before saving.
    /// </summary>
    public FlowDocumentConverterOptions FlowToDocumentOptions { get; } = new();

    /// <summary>
    /// Gets Document to FlowDocument conversion options used after loading.
    /// </summary>
    public DocumentToFlowDocumentConverterOptions DocumentToFlowOptions { get; } = new();

    /// <summary>
    /// Gets or sets optional base layout settings used for PDF export.
    /// </summary>
    public LayoutSettings? PdfExportLayoutSettings { get; set; }

    /// <summary>
    /// Gets PostScript conversion options used for EPS/PS import and export.
    /// </summary>
    public PostScriptConversionOptions PostScriptOptions { get; } = new();

    /// <summary>
    /// Gets XPS conversion options used for XPS/OXPS import and export.
    /// </summary>
    public XpsConversionOptions XpsOptions { get; } = new();
}
