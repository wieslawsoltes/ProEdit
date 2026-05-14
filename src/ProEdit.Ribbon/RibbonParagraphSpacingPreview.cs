namespace ProEdit.Ribbon;

public readonly record struct RibbonParagraphSpacingPreview(
    float? Before = null,
    float? After = null,
    float? LineSpacing = null);
