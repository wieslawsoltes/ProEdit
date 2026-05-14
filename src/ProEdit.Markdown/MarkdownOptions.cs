using ProEdit.Documents.Formats;

namespace ProEdit.Markdown;

public sealed class MarkdownOptions : DocumentFormatOptions
{
    public MarkdownDowngradeOptions Downgrade { get; } = new();
    public MarkdownFlavor Flavor { get; set; } = MarkdownFlavor.CommonMark;
    public bool AllowHtmlBlocks { get; set; }
    public bool AllowHtmlInlines { get; set; }
    public bool PreferFencedCode { get; set; } = true;
    public bool PreferAtxHeadings { get; set; } = true;
    public bool UseSetextForH1H2 { get; set; }
    public bool UseGfmTables { get; set; } = true;
    public bool UseTaskLists { get; set; } = true;
    public bool UseStrikethrough { get; set; } = true;
    public bool NormalizeLineEndings { get; set; } = true;
}
