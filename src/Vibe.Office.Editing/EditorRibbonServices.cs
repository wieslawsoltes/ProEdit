using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

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
    EditorValue<bool> TextImprint);

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

public interface IStyleService
{
    IReadOnlyList<EditorParagraphStyleInfo> GetParagraphStyles();
    EditorValue<string> GetCurrentParagraphStyleId();
    ParagraphStyleDefinition? GetParagraphStyle(string styleId);
    bool ApplyParagraphStyle(string styleId);
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
}

public interface IStylePaneService
{
    void OpenStylesPane();
    void OpenStylesManager();
}

public interface IEditorCommandRouter
{
    bool CanExecute(string commandId, object? payload = null, RibbonContextSnapshot? context = null);
    ValueTask<bool> ExecuteAsync(string commandId, object? payload = null, RibbonContextSnapshot? context = null);
}
