namespace ProEdit.Markdown;

public enum MarkdownFallbackStrategy
{
    Drop,
    Placeholder,
    Html
}

public sealed class MarkdownDowngradeOptions
{
    public MarkdownFallbackStrategy FallbackStrategy { get; set; } = MarkdownFallbackStrategy.Placeholder;
    public string PlaceholderFormat { get; set; } = "[{0}]";
    public bool EmbedImagesAsDataUri { get; set; } = true;
    public bool ConvertPageBreaksToThematicBreak { get; set; } = true;
    public bool ConvertSectionBreaksToThematicBreak { get; set; } = true;
    public bool ConvertColumnBreaksToThematicBreak { get; set; }
}
