using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown;

internal static class MarkdownInlineParser
{
    public static List<MarkdownInline> Parse(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        MarkdownOptions options)
    {
        var result = new List<MarkdownInline>();
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '<')
            {
                if (TryParseAutoLink(text, baseOffset, idProvider, ref index, out var autoLink))
                {
                    result.Add(autoLink);
                    continue;
                }
            }

            if (ch == '`')
            {
                if (TryParseCodeSpan(text, baseOffset, idProvider, ref index, out var codeInline))
                {
                    result.Add(codeInline);
                    continue;
                }
            }

            if (ch == '!' && index + 1 < text.Length && text[index + 1] == '[')
            {
                if (TryParseImage(text, baseOffset, idProvider, ref index, out var imageInline, options))
                {
                    result.Add(imageInline);
                    continue;
                }
            }

            if (ch == '[')
            {
                if (TryParseLink(text, baseOffset, idProvider, ref index, out var linkInline, options))
                {
                    result.Add(linkInline);
                    continue;
                }
            }

            if (options.Flavor == MarkdownFlavor.GitHub && options.UseStrikethrough && ch == '~')
            {
                if (TryParseStrikethrough(text, baseOffset, idProvider, ref index, out var strikeInline, options))
                {
                    result.Add(strikeInline);
                    continue;
                }
            }

            if (ch == '*' || ch == '_')
            {
                if (TryParseEmphasis(text, baseOffset, idProvider, ref index, out var emphasisInline, options))
                {
                    result.Add(emphasisInline);
                    continue;
                }
            }

            if (ch == '\\')
            {
                if (index + 1 < text.Length)
                {
                    var next = text[index + 1];
                    if (next == '\n')
                    {
                        var span = new MarkdownTextSpan(baseOffset + index, 2);
                        result.Add(new MarkdownHardBreakInline(idProvider.NextId(), span));
                        index += 2;
                        continue;
                    }

                    var escapedSpan = new MarkdownTextSpan(baseOffset + index + 1, 1);
                    result.Add(new MarkdownTextInline(idProvider.NextId(), escapedSpan, next.ToString()));
                    index += 2;
                    continue;
                }
            }

            if (ch == '\n')
            {
                var span = new MarkdownTextSpan(baseOffset + index, 1);
                result.Add(new MarkdownSoftBreakInline(idProvider.NextId(), span));
                index++;
                continue;
            }

            var textStart = index;
            while (index < text.Length && !IsSpecial(text[index], options))
            {
                index++;
            }

            if (index > textStart)
            {
                var slice = text.Slice(textStart, index - textStart);
                var span = new MarkdownTextSpan(baseOffset + textStart, slice.Length);
                result.Add(new MarkdownTextInline(idProvider.NextId(), span, slice.ToString()));
                continue;
            }

            // Fallback for unmatched special characters to avoid infinite loops.
            var fallbackSpan = new MarkdownTextSpan(baseOffset + index, 1);
            result.Add(new MarkdownTextInline(idProvider.NextId(), fallbackSpan, text.Slice(index, 1).ToString()));
            index++;
            continue;
        }

        return result;
    }

    private static bool TryParseCodeSpan(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownCodeInline inline)
    {
        inline = null!;
        var start = index;
        var tickCount = CountRun(text, index, '`');
        if (tickCount <= 0)
        {
            return false;
        }

        var searchIndex = index + tickCount;
        while (searchIndex < text.Length)
        {
            var nextTick = text.Slice(searchIndex).IndexOf('`');
            if (nextTick < 0)
            {
                return false;
            }

            searchIndex += nextTick;
            var runLength = CountRun(text, searchIndex, '`');
            if (runLength == tickCount)
            {
                var contentStart = index + tickCount;
                var contentLength = searchIndex - contentStart;
                var code = text.Slice(contentStart, contentLength).ToString();
                var span = new MarkdownTextSpan(baseOffset + start, searchIndex + tickCount - start);
                inline = new MarkdownCodeInline(idProvider.NextId(), span, NormalizeCodeSpan(code));
                index = searchIndex + tickCount;
                return true;
            }

            searchIndex += runLength;
        }

        return false;
    }

    private static string NormalizeCodeSpan(string code)
    {
        if (code.Length == 0)
        {
            return string.Empty;
        }

        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ')
        {
            var hasNonSpace = false;
            for (var i = 0; i < code.Length; i++)
            {
                if (code[i] != ' ')
                {
                    hasNonSpace = true;
                    break;
                }
            }

            if (hasNonSpace)
            {
                return code.Substring(1, code.Length - 2);
            }
        }

        return code;
    }

    private static bool TryParseImage(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownImageInline inline,
        MarkdownOptions options)
    {
        inline = null!;
        if (index + 2 >= text.Length || text[index + 1] != '[')
        {
            return false;
        }

        var labelStart = index + 2;
        if (!TryFindClosingBracket(text, labelStart, out var labelEnd))
        {
            return false;
        }

        var linkStart = labelEnd + 1;
        if (linkStart >= text.Length || text[linkStart] != '(')
        {
            return false;
        }

        if (!TryParseLinkDestination(text, linkStart + 1, out var linkEnd, out var destination, out var title))
        {
            return false;
        }

        var span = new MarkdownTextSpan(baseOffset + index, linkEnd - index + 1);
        var image = new MarkdownImageInline(idProvider.NextId(), span, destination);
        if (!string.IsNullOrEmpty(title))
        {
            image.Title = title;
        }

        var label = text.Slice(labelStart, labelEnd - labelStart);
        image.AltText.AddRange(Parse(label, baseOffset + labelStart, idProvider, options));

        inline = image;
        index = linkEnd + 1;
        return true;
    }

    private static bool TryParseLink(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownLinkInline inline,
        MarkdownOptions options)
    {
        inline = null!;
        var labelStart = index + 1;
        if (!TryFindClosingBracket(text, labelStart, out var labelEnd))
        {
            return false;
        }

        var linkStart = labelEnd + 1;
        if (linkStart >= text.Length || text[linkStart] != '(')
        {
            return false;
        }

        if (!TryParseLinkDestination(text, linkStart + 1, out var linkEnd, out var destination, out var title))
        {
            return false;
        }

        var span = new MarkdownTextSpan(baseOffset + index, linkEnd - index + 1);
        var link = new MarkdownLinkInline(idProvider.NextId(), span, destination);
        if (!string.IsNullOrEmpty(title))
        {
            link.Title = title;
        }

        var label = text.Slice(labelStart, labelEnd - labelStart);
        link.Inlines.AddRange(Parse(label, baseOffset + labelStart, idProvider, options));
        inline = link;
        index = linkEnd + 1;
        return true;
    }

    private static bool TryParseLinkDestination(
        ReadOnlySpan<char> text,
        int start,
        out int end,
        out string destination,
        out string? title)
    {
        destination = string.Empty;
        title = null;
        end = -1;

        var index = start;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length)
        {
            return false;
        }

        if (text[index] == '<')
        {
            var close = text.Slice(index + 1).IndexOf('>');
            if (close < 0)
            {
                return false;
            }

            destination = text.Slice(index + 1, close).ToString();
            index += close + 2;
        }
        else
        {
            var destStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ')')
            {
                index++;
            }

            destination = text.Slice(destStart, index - destStart).ToString();
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index < text.Length && (text[index] == '"' || text[index] == '\''))
        {
            var quote = text[index];
            var titleStart = index + 1;
            var titleEnd = text.Slice(titleStart).IndexOf(quote);
            if (titleEnd >= 0)
            {
                title = text.Slice(titleStart, titleEnd).ToString();
                index = titleStart + titleEnd + 1;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
            }
        }

        if (index >= text.Length || text[index] != ')')
        {
            return false;
        }

        end = index;
        return true;
    }

    private static bool TryParseEmphasis(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownInline inline,
        MarkdownOptions options)
    {
        inline = null!;
        var marker = text[index];
        var runLength = CountRun(text, index, marker);
        if (runLength <= 0)
        {
            return false;
        }

        var strongCount = runLength >= 2 ? 2 : 1;
        var closing = FindClosingDelimiter(text, index + strongCount, marker, strongCount);
        if (closing < 0)
        {
            if (runLength >= 1)
            {
                closing = FindClosingDelimiter(text, index + 1, marker, 1);
                if (closing < 0)
                {
                    return false;
                }

                strongCount = 1;
            }
            else
            {
                return false;
            }
        }

        var contentStart = index + strongCount;
        var contentLength = closing - contentStart;
        var content = text.Slice(contentStart, contentLength);
        var span = new MarkdownTextSpan(baseOffset + index, closing + strongCount - index);
        var emphasis = new MarkdownEmphasisInline(idProvider.NextId(), span)
        {
            IsStrong = strongCount == 2
        };
        emphasis.Inlines.AddRange(Parse(content, baseOffset + contentStart, idProvider, options));
        inline = emphasis;
        index = closing + strongCount;
        return true;
    }

    private static bool TryParseStrikethrough(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownInline inline,
        MarkdownOptions options)
    {
        inline = null!;
        var runLength = CountRun(text, index, '~');
        if (runLength < 2)
        {
            return false;
        }

        var closing = FindClosingDelimiter(text, index + 2, '~', 2);
        if (closing < 0)
        {
            return false;
        }

        var contentStart = index + 2;
        var contentLength = closing - contentStart;
        var content = text.Slice(contentStart, contentLength);
        var span = new MarkdownTextSpan(baseOffset + index, closing + 2 - index);
        var strike = new MarkdownStrikethroughInline(idProvider.NextId(), span);
        strike.Inlines.AddRange(Parse(content, baseOffset + contentStart, idProvider, options));
        inline = strike;
        index = closing + 2;
        return true;
    }

    private static bool TryParseAutoLink(
        ReadOnlySpan<char> text,
        int baseOffset,
        MarkdownNodeIdProvider idProvider,
        ref int index,
        out MarkdownInline inline)
    {
        inline = null!;
        var end = text.Slice(index + 1).IndexOf('>');
        if (end < 0)
        {
            return false;
        }

        var contentStart = index + 1;
        var contentLength = end;
        var content = text.Slice(contentStart, contentLength);
        var value = content.ToString();
        if (!IsAutoLink(value))
        {
            return false;
        }

        var span = new MarkdownTextSpan(baseOffset + index, contentLength + 2);
        var link = new MarkdownLinkInline(idProvider.NextId(), span, value);
        link.Inlines.Add(new MarkdownTextInline(idProvider.NextId(), new MarkdownTextSpan(baseOffset + contentStart, contentLength), value));
        inline = link;
        index = contentStart + contentLength + 1;
        return true;
    }

    private static bool IsAutoLink(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindClosingBracket(ReadOnlySpan<char> text, int start, out int end)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '[')
            {
                depth++;
                continue;
            }

            if (ch == ']')
            {
                if (depth == 0)
                {
                    end = i;
                    return true;
                }

                depth--;
            }
        }

        end = -1;
        return false;
    }

    private static int FindClosingDelimiter(ReadOnlySpan<char> text, int start, char marker, int count)
    {
        var index = start;
        while (index < text.Length)
        {
            var next = text.Slice(index).IndexOf(marker);
            if (next < 0)
            {
                return -1;
            }

            index += next;
            var runLength = CountRun(text, index, marker);
            if (runLength >= count)
            {
                return index;
            }

            index += runLength;
        }

        return -1;
    }

    private static int CountRun(ReadOnlySpan<char> text, int index, char marker)
    {
        var count = 0;
        while (index + count < text.Length && text[index + count] == marker)
        {
            count++;
        }

        return count;
    }

    private static bool IsSpecial(char ch, MarkdownOptions options)
    {
        return ch == '`'
               || ch == '['
               || ch == '!'
               || ch == '*'
               || ch == '_'
               || (options.Flavor == MarkdownFlavor.GitHub && options.UseStrikethrough && ch == '~')
               || ch == '\\'
               || ch == '<'
               || ch == '\n';
    }
}
