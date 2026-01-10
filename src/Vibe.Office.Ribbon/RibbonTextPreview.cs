using Vibe.Office.Primitives;

namespace Vibe.Office.Ribbon;

public readonly record struct RibbonTextPreview(
    string Text,
    string? FontFamily = null,
    float? FontSize = null,
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    DocColor? Foreground = null,
    DocColor? Highlight = null,
    DocColor? Background = null,
    RibbonParagraphSpacingPreview? ParagraphSpacing = null);
