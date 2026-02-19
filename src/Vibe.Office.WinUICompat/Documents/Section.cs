namespace Vibe.Office.WinUICompat.Documents;

public sealed class Section : Block
{
    public BlockCollection Blocks { get; } = new();

    public bool? HasTrailingParagraphBreakOnPaste { get; set; }
}
