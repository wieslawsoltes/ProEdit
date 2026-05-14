using System.Threading.Tasks;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;

namespace ProEdit.Editing;

public readonly struct EditorValue<T>
{
    public bool HasValue { get; }
    public bool IsMixed { get; }
    public T? Value { get; }

    private EditorValue(T? value, bool hasValue, bool isMixed)
    {
        Value = value;
        HasValue = hasValue;
        IsMixed = isMixed;
    }

    public bool IsDefined => HasValue || IsMixed;

    public static EditorValue<T> Missing() => new EditorValue<T>(default, false, false);
    public static EditorValue<T> Mixed() => new EditorValue<T>(default, false, true);
    public static EditorValue<T> FromValue(T value) => new EditorValue<T>(value, true, false);
}

public enum EditorSelectionKind
{
    Caret,
    Range,
    FloatingObject
}

public readonly record struct EditorSelectionSnapshot(
    EditorSelectionKind Kind,
    bool IsCollapsed,
    bool IsMultiRange,
    bool IsBlockSelection,
    bool IsInTable,
    bool IsInList,
    TextPosition Caret,
    TextRange Selection,
    Guid? SelectedFloatingObjectId);

public interface ISelectionState
{
    EditorSelectionSnapshot GetSnapshot();
}

public readonly record struct EditorFormattingSnapshot(
    EditorValue<string> FontFamily,
    EditorValue<float> FontSize,
    EditorValue<bool> Bold,
    EditorValue<bool> Italic,
    EditorValue<bool> Underline,
    EditorValue<DocUnderlineStyle> UnderlineStyle,
    EditorValue<bool> Strikethrough,
    EditorValue<DocColor> FontColor,
    EditorValue<DocColor> HighlightColor,
    EditorValue<DocColor> UnderlineColor,
    EditorValue<bool> SmallCaps,
    EditorValue<bool> Caps,
    EditorValue<DocVerticalPosition> VerticalPosition,
    EditorValue<float> LetterSpacing,
    EditorValue<float> HorizontalScale,
    EditorValue<float> BaselineOffset,
    EditorValue<bool> TextOutline,
    EditorValue<bool> TextShadow,
    EditorValue<bool> TextEmboss,
    EditorValue<bool> TextImprint,
    EditorValue<DocLigatureOptions> Ligatures,
    EditorValue<bool> ContextualAlternates,
    EditorValue<DocNumberForm> NumberForm,
    EditorValue<DocNumberSpacing> NumberSpacing,
    EditorValue<uint> StylisticSets);

public interface IFormattingState
{
    EditorFormattingSnapshot GetSnapshot();
}

public readonly record struct EditorParagraphSnapshot(
    EditorValue<ParagraphAlignment> Alignment,
    EditorValue<float> IndentLeft,
    EditorValue<float> IndentRight,
    EditorValue<float> FirstLineIndent,
    EditorValue<float> SpacingBefore,
    EditorValue<float> SpacingAfter,
    EditorValue<int> LineSpacing,
    EditorValue<DocLineSpacingRule> LineSpacingRule,
    EditorValue<ListKind> ListKind,
    EditorValue<int> ListLevel,
    EditorValue<DocColor> ShadingColor,
    EditorValue<bool> KeepWithNext,
    EditorValue<bool> KeepLinesTogether,
    EditorValue<bool> WidowControl,
    EditorValue<bool> PageBreakBefore,
    EditorValue<bool> SuppressLineNumbers,
    EditorValue<bool> ContextualSpacing,
    EditorValue<bool> Bidi,
    EditorValue<DocTextDirection> TextDirection);

public interface IParagraphService
{
    EditorParagraphSnapshot GetSnapshot();
}

public readonly record struct EditorParagraphStyleInfo(string Id, string Name, bool IsDefault);

public readonly record struct EditorDirectFormattingInfo(bool HasParagraphFormatting, bool HasCharacterFormatting);

public interface IStyleService
{
    IReadOnlyList<EditorParagraphStyleInfo> GetParagraphStyles();
    EditorValue<string> GetCurrentParagraphStyleId();
    ParagraphStyleDefinition? GetParagraphStyle(string styleId);
    TextStyle? GetParagraphStylePreview(string styleId);
    IReadOnlyCollection<string> GetParagraphStylesInUse();
    EditorDirectFormattingInfo GetDirectFormattingInfo();
    bool ApplyParagraphStyle(string styleId);
    bool RenameParagraphStyle(string styleId, string name);
    bool SetParagraphStyleBasedOn(string styleId, string? basedOnId);
    bool SetParagraphStyleNext(string styleId, string? nextStyleId);
    bool SetDefaultParagraphStyle(string styleId);
}

public readonly record struct EditorTableStyleInfo(string Id, string Name, bool IsDefault);

public interface ITableStyleService
{
    IReadOnlyList<EditorTableStyleInfo> GetTableStyles();
    string? GetCurrentTableStyleId();
    string? GetDefaultTableStyleId();
}

public readonly record struct EditorTableSelectionSnapshot(
    TableBlock Table,
    int RowIndex,
    int ColumnIndex,
    int RowStart,
    int RowEnd,
    int ColumnStart,
    int ColumnEnd,
    TableLayout? Layout);

public interface ITableSelectionSnapshotProvider
{
    bool TryGetSnapshot(out EditorTableSelectionSnapshot snapshot);
}

public readonly record struct EditorFontFamilyInfo(string Name, bool IsEmbedded);

public interface IFontService
{
    IReadOnlyList<EditorFontFamilyInfo> GetFontFamilies();
    bool IsFontAvailable(string family);
    bool HasEmbeddedFont(string family);
}

public interface IClipboardService
{
    bool CanCopy { get; }
    bool CanCut { get; }
    bool CanPaste { get; }
    IReadOnlyList<string> SupportedFormats { get; }
    bool TryGetText(out string text);
    void SetText(string text);
    bool TryGetContent(out ClipboardContent content);
    void SetContent(ClipboardContent content);
}

public interface IFormatPainterService
{
    bool IsActive { get; }
    void Toggle();
}

public interface IUndoRedoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    ValueTask UndoAsync();
    ValueTask RedoAsync();
}

public readonly record struct EditorFindQuery(string Text, bool MatchCase = false, bool WholeWord = false, bool Wrap = true);

public readonly record struct EditorReplaceQuery(
    string Text,
    string Replacement,
    bool MatchCase = false,
    bool WholeWord = false,
    bool Wrap = true);

public readonly record struct EditorFindResult(bool Found, TextRange Range);

public interface IFindReplaceService
{
    bool IsAvailable { get; }
    bool TryFindNext(EditorFindQuery query, out EditorFindResult result);
    bool TryReplaceNext(EditorReplaceQuery query, out EditorFindResult result);
    int ReplaceAll(EditorReplaceQuery query);
}

public interface ISelectionTextService
{
    bool TryGetSelectionText(out string text, int maxLength = 0);
}

public interface IEditorViewOptionsService
{
    bool ShowInvisibles { get; set; }
    bool ShowRuler { get; set; }
    bool ShowGridlines { get; set; }
    bool ShowNavigationPane { get; set; }
    PageFlowDirection PageMovement { get; set; }
    EditorViewMode ViewMode { get; set; }
}

public interface IEditorZoomService
{
    ValueTask OpenZoomDialogAsync();
    void ZoomToPercent(float percent);
    void ZoomToPageWidth();
    void ZoomToWholePage();
    void ZoomToMultiplePages(int pagesPerRow);
    void ZoomToDefault();
}

public interface IInkReplayService
{
    ValueTask ReplaySelectedInkAsync();
}

public enum ReviewMarkupMode
{
    All,
    Simple,
    None,
    Balloons
}

public interface IReviewPaneService
{
    ReviewMarkupMode MarkupMode { get; set; }
    void ToggleReviewingPane();
}

public interface IMailMergeSourceManager
{
    ValueTask<MailMergeData?> EditRecipientsAsync(MailMergeData? currentData);
}

public interface ICitationSourceManager
{
    ValueTask<CitationSourceCatalog?> EditSourcesAsync(CitationSourceCatalog? currentCatalog);
    ValueTask<string?> PickSourceAsync(CitationSourceCatalog catalog);
}

public interface IEditorDialogService
{
    ValueTask ShowMessageAsync(string title, string message);
    ValueTask<string?> PromptAsync(string title, string prompt, string? initialValue = null);
}

public interface IProofingDialogService
{
    ValueTask ShowSpellingGrammarAsync();
    ValueTask ShowThesaurusAsync();
}

public interface IMacroManagerService
{
    ValueTask OpenMacroManagerAsync();
    ValueTask ToggleRecordMacroAsync();
    ValueTask OpenVbaEditorAsync();
    ValueTask StartDebugAsync();
}

public enum EditorWindowCommand
{
    NewWindow,
    ArrangeAll,
    Split,
    ViewSideBySide,
    SynchronousScrolling,
    ResetPosition,
    SwitchWindows
}

public interface IEditorWindowService
{
    ValueTask ExecuteAsync(EditorWindowCommand command);
}

public interface IStylePaneService
{
    void OpenStylesPane();
    void OpenStylesManager();
}

public interface IEditorCommandRouter
{
    bool CanExecute(string commandId, object? payload = null, RibbonContextSnapshot? context = null);
    ValueTask<bool> ExecuteAsync(string commandId, object? payload = null, RibbonContextSnapshot? context = null, bool recordHistory = true);
}
