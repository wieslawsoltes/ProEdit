namespace Vibe.Office.Ribbon;

public readonly record struct RibbonParagraphSpacingPreview(
    float? Before = null,
    float? After = null,
    float? LineSpacing = null);
