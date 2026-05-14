using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Editing;

public readonly record struct EditorLineSpacingRequest(
    float? Multiple = null,
    int? Twips = null,
    DocLineSpacingRule? Rule = null)
{
    public static EditorLineSpacingRequest FromMultiple(float multiple) =>
        new EditorLineSpacingRequest(multiple, null, DocLineSpacingRule.Auto);

    public static EditorLineSpacingRequest FromTwips(int twips, DocLineSpacingRule rule) =>
        new EditorLineSpacingRequest(null, twips, rule);
}

public readonly record struct EditorParagraphSpacingOptions(
    float? SpacingBefore,
    float? SpacingAfter,
    int? LineSpacing,
    DocLineSpacingRule? LineSpacingRule);

public enum EditorParagraphBorderKind
{
    None,
    All,
    Outside,
    Top,
    Bottom,
    Left,
    Right
}

public readonly record struct EditorParagraphBorderRequest(EditorParagraphBorderKind Kind);

public readonly record struct EditorFontDialogOptions(
    string? FontFamily,
    float? FontSize,
    DocFontWeight? FontWeight,
    DocFontStyle? FontStyle,
    DocUnderlineStyle? UnderlineStyle,
    DocColor? UnderlineColor,
    DocColor? FontColor,
    bool? Strikethrough,
    bool? SmallCaps,
    bool? Caps,
    DocVerticalPosition? VerticalPosition,
    bool? TextOutline,
    bool? TextShadow,
    bool? TextEmboss,
    bool? TextImprint,
    float? LetterSpacing,
    float? HorizontalScale,
    float? BaselineOffset,
    TextOpenTypeFeatures? OpenTypeFeatures);

public readonly record struct EditorParagraphDialogOptions(
    ParagraphAlignment? Alignment,
    float? IndentLeft,
    float? IndentRight,
    float? FirstLineIndent,
    float? SpacingBefore,
    float? SpacingAfter,
    int? LineSpacing,
    DocLineSpacingRule? LineSpacingRule,
    bool? ContextualSpacing,
    bool? KeepWithNext,
    bool? KeepLinesTogether,
    bool? WidowControl,
    bool? PageBreakBefore,
    bool? SuppressLineNumbers,
    bool? Bidi,
    DocTextDirection? TextDirection);

public readonly record struct EditorParagraphTabStopRequest(
    float Position,
    TabAlignment Alignment,
    TabLeader Leader);

public readonly record struct EditorParagraphTabStopUpdateRequest(
    float OriginalPosition,
    float Position,
    TabAlignment Alignment,
    TabLeader Leader);

public readonly record struct EditorParagraphTabStopRemoveRequest(float Position);
