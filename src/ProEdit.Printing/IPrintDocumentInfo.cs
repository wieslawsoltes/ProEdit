namespace ProEdit.Printing;

public interface IPrintDocumentInfo
{
    int? CurrentPageIndex { get; }
    bool HasSelection { get; }
    PrintPaperSize? DefaultPaperSize { get; }
}
