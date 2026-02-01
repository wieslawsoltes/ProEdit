using System.Globalization;
using System.Text;
using Vibe.Office.Markdown.Ast;

namespace Vibe.Office.Markdown;

public sealed class MarkdownSerializer
{
    private readonly MarkdownOptions _options;

    public MarkdownSerializer(MarkdownOptions? options = null)
    {
        _options = options ?? new MarkdownOptions();
    }

    public string Serialize(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        WriteBlocks(builder, document.Blocks, indentLevel: 0);
        return builder.ToString();
    }

    private void WriteBlocks(StringBuilder builder, IReadOnlyList<MarkdownBlock> blocks, int indentLevel)
    {
        var hasContent = false;
        var pendingEmptyParagraphs = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (IsEmptyParagraph(block))
            {
                pendingEmptyParagraphs++;
                continue;
            }

            if (hasContent)
            {
                AppendBlockSeparator(builder, pendingEmptyParagraphs);
            }

            WriteBlock(builder, block, indentLevel);
            hasContent = true;
            pendingEmptyParagraphs = 0;
        }

        if (hasContent && pendingEmptyParagraphs > 0)
        {
            builder.Append('\n', pendingEmptyParagraphs + 1);
        }
    }

    private static bool IsEmptyParagraph(MarkdownBlock block)
    {
        return block is MarkdownParagraphBlock paragraph && paragraph.Inlines.Count == 0;
    }

    private static void AppendBlockSeparator(StringBuilder builder, int emptyParagraphs)
    {
        var blankLines = 1 + emptyParagraphs;
        var newlineCount = blankLines + 1;
        builder.Append('\n', newlineCount);
    }

    private void WriteBlock(StringBuilder builder, MarkdownBlock block, int indentLevel)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                WriteHeading(builder, heading, indentLevel);
                break;
            case MarkdownParagraphBlock paragraph:
                WriteParagraph(builder, paragraph, indentLevel);
                break;
            case MarkdownBlockQuoteBlock quote:
                WriteBlockQuote(builder, quote, indentLevel);
                break;
            case MarkdownListBlock list:
                WriteList(builder, list, indentLevel);
                break;
            case MarkdownCodeBlock code:
                WriteCodeBlock(builder, code, indentLevel);
                break;
            case MarkdownThematicBreakBlock:
                WriteIndent(builder, indentLevel);
                builder.Append("---");
                break;
            case MarkdownHtmlBlock html:
                WriteIndent(builder, indentLevel);
                builder.Append(html.Html);
                break;
            case MarkdownTableBlock table:
                WriteTable(builder, table, indentLevel);
                break;
            default:
                WriteIndent(builder, indentLevel);
                builder.Append("[Unsupported]");
                break;
        }
    }

    private void WriteHeading(StringBuilder builder, MarkdownHeadingBlock heading, int indentLevel)
    {
        WriteIndent(builder, indentLevel);
        var level = Math.Clamp(heading.Level, 1, 6);
        builder.Append(new string('#', level)).Append(' ');
        WriteInlines(builder, heading.Inlines);
    }

    private void WriteParagraph(StringBuilder builder, MarkdownParagraphBlock paragraph, int indentLevel)
    {
        WriteIndent(builder, indentLevel);
        WriteInlines(builder, paragraph.Inlines);
    }

    private void WriteBlockQuote(StringBuilder builder, MarkdownBlockQuoteBlock quote, int indentLevel)
    {
        var inner = new StringBuilder();
        WriteBlocks(inner, quote.Blocks, indentLevel: 0);
        var lines = inner.ToString().Split('\n');
        foreach (var line in lines)
        {
            WriteIndent(builder, indentLevel);
            builder.Append('>').Append(' ').Append(line);
            builder.Append('\n');
        }

        if (builder.Length > 0 && builder[^1] == '\n')
        {
            builder.Length--;
        }
    }

    private void WriteList(StringBuilder builder, MarkdownListBlock list, int indentLevel)
    {
        var index = list.StartNumber.GetValueOrDefault(1);
        foreach (var item in list.Items)
        {
            WriteIndent(builder, indentLevel);
            if (list.Kind == MarkdownListKind.Ordered)
            {
                var marker = index.ToString(CultureInfo.InvariantCulture) + ". ";
                builder.Append(marker);
                WriteListItemContents(builder, item, indentLevel + marker.Length);
                index++;
            }
            else
            {
                const string marker = "- ";
                builder.Append(marker);
                WriteListItemContents(builder, item, indentLevel + marker.Length);
            }

            builder.Append('\n');
        }

        if (builder.Length > 0 && builder[^1] == '\n')
        {
            builder.Length--;
        }
    }

    private void WriteListItemContents(StringBuilder builder, MarkdownListItemBlock item, int indentLevel)
    {
        if (item.Blocks.Count == 0)
        {
            return;
        }

        if (item.Blocks.Count == 1 && item.Blocks[0] is MarkdownParagraphBlock paragraph)
        {
            if (item.IsTask == true)
            {
                builder.Append(item.TaskChecked == true ? "[x] " : "[ ] ");
            }
            WriteInlines(builder, paragraph.Inlines);
            return;
        }

        var inner = new StringBuilder();
        WriteBlocks(inner, item.Blocks, indentLevel: 0);
        var lines = inner.ToString().Split('\n');
        if (lines.Length > 0)
        {
            if (item.IsTask == true)
            {
                builder.Append(item.TaskChecked == true ? "[x] " : "[ ] ");
            }
            builder.Append(lines[0]);
        }

        for (var i = 1; i < lines.Length; i++)
        {
            builder.Append('\n');
            WriteIndent(builder, indentLevel);
            builder.Append(lines[i]);
        }
    }

    private void WriteCodeBlock(StringBuilder builder, MarkdownCodeBlock code, int indentLevel)
    {
        if (code.IsFenced || _options.PreferFencedCode)
        {
            WriteIndent(builder, indentLevel);
            builder.Append("```");
            if (!string.IsNullOrWhiteSpace(code.Info))
            {
                builder.Append(code.Info);
            }

            builder.Append('\n');
            if (!string.IsNullOrEmpty(code.Text))
            {
                builder.Append(code.Text);
                if (!code.Text.EndsWith("\n", StringComparison.Ordinal))
                {
                    builder.Append('\n');
                }
            }

            WriteIndent(builder, indentLevel);
            builder.Append("```");
            return;
        }

        var lines = code.Text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            WriteIndent(builder, indentLevel);
            builder.Append("    ");
            builder.Append(lines[i]);
            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }
    }

    private void WriteInlines(StringBuilder builder, IReadOnlyList<MarkdownInline> inlines)
    {
        WriteInlines(builder, inlines, escapePipe: false, tableMode: false);
    }

    private void WriteInlines(
        StringBuilder builder,
        IReadOnlyList<MarkdownInline> inlines,
        bool escapePipe,
        bool tableMode)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    builder.Append(EscapeText(text.Text, escapePipe));
                    break;
                case MarkdownEmphasisInline emphasis:
                    var marker = emphasis.IsStrong ? "**" : "*";
                    builder.Append(marker);
                    WriteInlines(builder, emphasis.Inlines, escapePipe, tableMode);
                    builder.Append(marker);
                    break;
                case MarkdownStrikethroughInline strike:
                    if (_options.Flavor == MarkdownFlavor.GitHub && _options.UseStrikethrough)
                    {
                        builder.Append("~~");
                        WriteInlines(builder, strike.Inlines, escapePipe, tableMode);
                        builder.Append("~~");
                    }
                    else
                    {
                        WriteInlines(builder, strike.Inlines, escapePipe, tableMode);
                    }
                    break;
                case MarkdownCodeInline code:
                    WriteCodeInline(builder, code.Code);
                    break;
                case MarkdownLinkInline link:
                    builder.Append('[');
                    WriteInlines(builder, link.Inlines, escapePipe, tableMode);
                    builder.Append(']');
                    builder.Append('(').Append(link.Url);
                    if (!string.IsNullOrWhiteSpace(link.Title))
                    {
                        builder.Append(' ').Append('"').Append(link.Title).Append('"');
                    }

                    builder.Append(')');
                    break;
                case MarkdownImageInline image:
                    builder.Append("![");
                    WriteInlines(builder, image.AltText, escapePipe, tableMode);
                    builder.Append(']');
                    builder.Append('(').Append(image.Url);
                    if (!string.IsNullOrWhiteSpace(image.Title))
                    {
                        builder.Append(' ').Append('"').Append(image.Title).Append('"');
                    }

                    builder.Append(')');
                    break;
                case MarkdownHardBreakInline:
                    if (tableMode)
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        builder.Append("  ");
                        builder.Append('\n');
                    }
                    break;
                case MarkdownSoftBreakInline:
                    builder.Append(tableMode ? ' ' : '\n');
                    break;
                case MarkdownHtmlInline html:
                    builder.Append(html.Html);
                    break;
            }
        }
    }

    private void WriteTable(StringBuilder builder, MarkdownTableBlock table, int indentLevel)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        var headerRow = table.Rows[0];
        WriteIndent(builder, indentLevel);
        WriteTableRow(builder, headerRow);
        builder.Append('\n');

        WriteIndent(builder, indentLevel);
        WriteAlignmentRow(builder, table.Alignments, headerRow.Cells.Count);
        builder.Append('\n');

        for (var i = 1; i < table.Rows.Count; i++)
        {
            WriteIndent(builder, indentLevel);
            WriteTableRow(builder, table.Rows[i]);
            if (i < table.Rows.Count - 1)
            {
                builder.Append('\n');
            }
        }
    }

    private void WriteTableRow(StringBuilder builder, MarkdownTableRow row)
    {
        builder.Append('|');
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var cell = row.Cells[i];
            builder.Append(' ');
            WriteInlines(builder, cell.Inlines, escapePipe: true, tableMode: true);
            builder.Append(' ').Append('|');
        }
    }

    private void WriteAlignmentRow(StringBuilder builder, IReadOnlyList<MarkdownTableAlignment> alignments, int columnCount)
    {
        builder.Append('|');
        for (var i = 0; i < columnCount; i++)
        {
            var alignment = i < alignments.Count ? alignments[i] : MarkdownTableAlignment.None;
            builder.Append(' ');
            switch (alignment)
            {
                case MarkdownTableAlignment.Left:
                    builder.Append(":---");
                    break;
                case MarkdownTableAlignment.Center:
                    builder.Append(":---:");
                    break;
                case MarkdownTableAlignment.Right:
                    builder.Append("---:");
                    break;
                default:
                    builder.Append("---");
                    break;
            }

            builder.Append(' ').Append('|');
        }
    }

    private static void WriteCodeInline(StringBuilder builder, string code)
    {
        var maxRun = 0;
        var currentRun = 0;
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '`')
            {
                currentRun++;
                if (currentRun > maxRun)
                {
                    maxRun = currentRun;
                }
            }
            else
            {
                currentRun = 0;
            }
        }

        var backtickCount = Math.Max(1, maxRun + 1);

        var fence = new string('`', backtickCount);
        var needsPadding = code.Length > 0
                           && (code[0] == ' '
                               || code[^1] == ' '
                               || code[0] == '`'
                               || code[^1] == '`')
                           && !IsAllSpaces(code);
        if (needsPadding)
        {
            builder.Append(fence).Append(' ').Append(code).Append(' ').Append(fence);
            return;
        }

        builder.Append(fence).Append(code).Append(fence);
    }

    private static string EscapeText(string text, bool escapePipe)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '*' or '_' or '[' or ']' or '(' or ')' or '`' or '\\'
                || (escapePipe && ch == '|'))
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsAllSpaces(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteIndent(StringBuilder builder, int indentLevel)
    {
        if (indentLevel <= 0)
        {
            return;
        }

        builder.Append(' ', indentLevel);
    }
}
