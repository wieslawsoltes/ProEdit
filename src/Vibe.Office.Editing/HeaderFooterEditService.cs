namespace Vibe.Office.Editing;

public enum HeaderFooterVariant
{
    Default,
    First,
    Even
}

public interface IHeaderFooterEditService
{
    bool IsEditing { get; }
    bool IsHeaderEditing { get; }
    bool IsFooterEditing { get; }
    int SectionIndex { get; }
    int SectionCount { get; }
    HeaderFooterVariant Variant { get; }
    bool DifferentFirstPage { get; set; }
    bool DifferentOddEven { get; set; }
    void BeginHeader();
    void BeginFooter();
    void Close();
    void GoToPreviousSection();
    void GoToNextSection();
    void SetVariant(HeaderFooterVariant variant);
}
