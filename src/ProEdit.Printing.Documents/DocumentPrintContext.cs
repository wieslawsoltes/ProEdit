using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Printing;

namespace ProEdit.Printing.Documents;

public sealed class DocumentPrintContext : IPrintDocumentInfo
{
    public DocumentPrintContext(Document document, LayoutSettings layoutSettings)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        LayoutSettings = layoutSettings ?? throw new ArgumentNullException(nameof(layoutSettings));
    }

    public Document Document { get; }
    public LayoutSettings LayoutSettings { get; }
    public TextRange? Selection { get; set; }
    public int? CurrentPageIndex { get; set; }

    public bool HasSelection => Selection.HasValue && !Selection.Value.IsEmpty;

    public PrintPaperSize? DefaultPaperSize => new PrintPaperSize("Document", LayoutSettings.PageWidth, LayoutSettings.PageHeight);
}
