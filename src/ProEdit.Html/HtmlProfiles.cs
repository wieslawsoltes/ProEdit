using ProEdit.Documents.Formats;

namespace ProEdit.Html;

public static class HtmlProfiles
{
    public static readonly DocumentFormatProfile Html5 = new(
        "html5",
        "HTML (HTML5)",
        DocumentFormatCapability.Paragraphs
        | DocumentFormatCapability.Headings
        | DocumentFormatCapability.Lists
        | DocumentFormatCapability.BlockQuotes
        | DocumentFormatCapability.CodeBlocks
        | DocumentFormatCapability.ThematicBreaks
        | DocumentFormatCapability.Tables
        | DocumentFormatCapability.Images
        | DocumentFormatCapability.Links
        | DocumentFormatCapability.InlineCode
        | DocumentFormatCapability.Emphasis
        | DocumentFormatCapability.Strong
        | DocumentFormatCapability.Strikethrough
        | DocumentFormatCapability.HardLineBreaks
        | DocumentFormatCapability.HtmlBlocks
        | DocumentFormatCapability.HtmlInlines);
}
