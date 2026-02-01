namespace Vibe.Office.Documents.Formats;

[Flags]
public enum DocumentFormatCapability : ulong
{
    None = 0,
    Paragraphs = 1UL << 0,
    Headings = 1UL << 1,
    Lists = 1UL << 2,
    BlockQuotes = 1UL << 3,
    CodeBlocks = 1UL << 4,
    ThematicBreaks = 1UL << 5,
    Tables = 1UL << 6,
    TaskLists = 1UL << 7,
    Images = 1UL << 8,
    Links = 1UL << 9,
    InlineCode = 1UL << 10,
    Emphasis = 1UL << 11,
    Strong = 1UL << 12,
    Strikethrough = 1UL << 13,
    HardLineBreaks = 1UL << 14,
    HtmlBlocks = 1UL << 15,
    HtmlInlines = 1UL << 16
}
