using ProEdit.Documents.Formats;

namespace ProEdit.Markdown;

public static class MarkdownProfiles
{
    public static readonly DocumentFormatProfile CommonMark = new(
        "markdown.commonmark",
        "Markdown (CommonMark)",
        DocumentFormatCapability.Paragraphs
        | DocumentFormatCapability.Headings
        | DocumentFormatCapability.Lists
        | DocumentFormatCapability.BlockQuotes
        | DocumentFormatCapability.CodeBlocks
        | DocumentFormatCapability.ThematicBreaks
        | DocumentFormatCapability.Links
        | DocumentFormatCapability.Images
        | DocumentFormatCapability.InlineCode
        | DocumentFormatCapability.Emphasis
        | DocumentFormatCapability.Strong
        | DocumentFormatCapability.HardLineBreaks);

    public static readonly DocumentFormatProfile GitHub = new(
        "markdown.github",
        "Markdown (GitHub)",
        CommonMark.Capabilities
        | DocumentFormatCapability.Tables
        | DocumentFormatCapability.TaskLists
        | DocumentFormatCapability.Strikethrough);
}
