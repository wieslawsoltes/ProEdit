namespace ProEdit.Editing;

public readonly record struct RibbonContextSnapshot(
    long Version,
    EditorSelectionSnapshot Selection,
    EditorFormattingSnapshot Formatting,
    EditorParagraphSnapshot Paragraph,
    EditorValue<string> CurrentParagraphStyleId,
    IReadOnlyList<EditorParagraphStyleInfo> ParagraphStyles,
    IReadOnlyList<EditorFontFamilyInfo> FontFamilies,
    bool CanUndo,
    bool CanRedo,
    bool CanCopy,
    bool CanCut,
    bool CanPaste,
    bool IsFindAvailable);

public interface IRibbonContextSnapshotProvider
{
    RibbonContextSnapshot GetSnapshot();
}
