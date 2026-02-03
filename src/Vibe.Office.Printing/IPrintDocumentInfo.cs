namespace Vibe.Office.Printing;

public interface IPrintDocumentInfo
{
    int? CurrentPageIndex { get; }
    bool HasSelection { get; }
    PrintPaperSize? DefaultPaperSize { get; }
}
