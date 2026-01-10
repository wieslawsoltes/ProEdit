using Vibe.Office.Primitives;

namespace Vibe.Office.Ribbon;

public sealed class RibbonGalleryItem : RibbonStateNode
{
    public RibbonGalleryItem(
        string id,
        string label,
        RibbonTextPreview? preview = null,
        string? description = null,
        string? iconKey = null,
        object? tag = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Preview = preview;
        Description = description;
        IconKey = iconKey;
        Tag = tag;
    }

    public string Id { get; }
    public string Label { get; }
    public RibbonTextPreview? Preview { get; }
    public string? Description { get; }
    public string? IconKey { get; }
    public object? Tag { get; }

    public bool HasPreview => Preview.HasValue;
    public string PreviewText => Preview?.Text ?? Label;
    public string? PreviewFontFamily => Preview?.FontFamily;
    public float? PreviewFontSize => Preview?.FontSize;
    public bool? PreviewBold => Preview?.Bold;
    public bool? PreviewItalic => Preview?.Italic;
    public bool? PreviewUnderline => Preview?.Underline;
    public DocColor? PreviewForeground => Preview?.Foreground;
    public DocColor? PreviewHighlight => Preview?.Highlight;
    public DocColor? PreviewBackground => Preview?.Background;
    public RibbonParagraphSpacingPreview? PreviewParagraphSpacing => Preview?.ParagraphSpacing;

    public double? PreviewLineHeight
    {
        get
        {
            if (PreviewParagraphSpacing is not { } spacing || !spacing.LineSpacing.HasValue)
            {
                return null;
            }

            var baseFontSize = PreviewFontSize ?? 14f;
            var multiple = spacing.LineSpacing.Value;
            if (multiple <= 0)
            {
                return null;
            }

            return baseFontSize * multiple;
        }
    }
}
