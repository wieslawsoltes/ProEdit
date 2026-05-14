namespace ProEdit.Editing;

public sealed class RibbonContextSnapshotBuilder : IRibbonContextSnapshotProvider
{
    private readonly IEditorSession? _session;
    private readonly ISelectionState _selectionState;
    private readonly IFormattingState _formattingState;
    private readonly IParagraphService _paragraphService;
    private readonly IStyleService? _styleService;
    private readonly IFontService? _fontService;
    private readonly IClipboardService? _clipboardService;
    private readonly IUndoRedoService? _undoRedoService;
    private readonly IFindReplaceService? _findReplaceService;

    public RibbonContextSnapshotBuilder(
        ISelectionState selectionState,
        IFormattingState formattingState,
        IParagraphService paragraphService,
        IStyleService? styleService = null,
        IFontService? fontService = null,
        IClipboardService? clipboardService = null,
        IUndoRedoService? undoRedoService = null,
        IFindReplaceService? findReplaceService = null,
        IEditorSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(selectionState);
        ArgumentNullException.ThrowIfNull(formattingState);
        ArgumentNullException.ThrowIfNull(paragraphService);

        _selectionState = selectionState;
        _formattingState = formattingState;
        _paragraphService = paragraphService;
        _styleService = styleService;
        _fontService = fontService;
        _clipboardService = clipboardService;
        _undoRedoService = undoRedoService;
        _findReplaceService = findReplaceService;
        _session = session;
    }

    public RibbonContextSnapshot GetSnapshot()
    {
        var selection = _selectionState.GetSnapshot();
        var formatting = _formattingState.GetSnapshot();
        var paragraph = _paragraphService.GetSnapshot();
        var currentStyleId = _styleService?.GetCurrentParagraphStyleId() ?? EditorValue<string>.Missing();
        var paragraphStyles = _styleService?.GetParagraphStyles() ?? Array.Empty<EditorParagraphStyleInfo>();
        var fontFamilies = _fontService?.GetFontFamilies() ?? Array.Empty<EditorFontFamilyInfo>();
        var canUndo = _undoRedoService?.CanUndo ?? false;
        var canRedo = _undoRedoService?.CanRedo ?? false;
        var canCopy = _clipboardService?.CanCopy ?? false;
        var canCut = _clipboardService?.CanCut ?? false;
        var canPaste = _clipboardService?.CanPaste ?? false;
        var isFindAvailable = _findReplaceService?.IsAvailable ?? false;
        var version = _session?.DirtyVersion ?? 0;

        return new RibbonContextSnapshot(
            version,
            selection,
            formatting,
            paragraph,
            currentStyleId,
            paragraphStyles,
            fontFamilies,
            canUndo,
            canRedo,
            canCopy,
            canCut,
            canPaste,
            isFindAvailable);
    }
}
