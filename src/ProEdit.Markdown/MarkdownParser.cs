using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown;

public sealed class MarkdownParser
{
    private readonly MarkdownOptions _options;
    private readonly MarkdownNodeIdProvider _idProvider;
    private string _text = string.Empty;
    private List<MarkdownLine> _lines = new();

    public MarkdownParser(MarkdownOptions? options = null, MarkdownNodeIdProvider? idProvider = null)
    {
        _options = options ?? new MarkdownOptions();
        _idProvider = idProvider ?? new MarkdownNodeIdProvider();
    }

    public MarkdownDocument Parse(ReadOnlySpan<char> text)
    {
        _text = text.Length == 0 ? string.Empty : text.ToString();
        _lines = SplitLines(_text.AsSpan());
        var span = _text.Length == 0 ? MarkdownTextSpan.Unknown : new MarkdownTextSpan(0, _text.Length);
        var document = new MarkdownDocument(_idProvider.NextId(), span);
        var index = 0;
        ParseBlocks(document.Blocks, ref index, untilEnd: true, stopOnIndentLessThan: -1);
        AppendTrailingBlankLines(document);
        return document;
    }

    private void AppendTrailingBlankLines(MarkdownDocument document)
    {
        if (document.Blocks.Count == 0)
        {
            return;
        }

        var count = 0;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (!_lines[i].IsBlank)
            {
                break;
            }

            count++;
        }

        if (count == 0)
        {
            return;
        }

        if (_text.Length > 0 && (_text[^1] == '\n' || _text[^1] == '\r'))
        {
            count = Math.Max(0, count - 1);
        }

        if (count == 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            document.Blocks.Add(new MarkdownParagraphBlock(_idProvider.NextId(), MarkdownTextSpan.Unknown));
        }
    }

    private void ParseBlocks(List<MarkdownBlock> target, ref int lineIndex, bool untilEnd, int stopOnIndentLessThan)
    {
        while (lineIndex < _lines.Count)
        {
            var line = _lines[lineIndex];
            var lineSpan = GetLineSpan(line);
            if (line.IsBlank)
            {
                lineIndex++;
                if (!untilEnd)
                {
                    return;
                }

                continue;
            }

            var indent = CountIndent(lineSpan, out _);
            if (stopOnIndentLessThan >= 0 && indent < stopOnIndentLessThan)
            {
                return;
            }

            if (TryParseAtxHeading(lineSpan, out var headingLevel, out var headingContentStart))
            {
                var contentSpan = lineSpan.Slice(headingContentStart);
                var blockSpan = new MarkdownTextSpan(line.Start, line.Length);
                var heading = new MarkdownHeadingBlock(_idProvider.NextId(), blockSpan, headingLevel);
                heading.Inlines.AddRange(ParseLineInlines(contentSpan, line.Start + headingContentStart));
                target.Add(heading);
                lineIndex++;
                continue;
            }

            if (TryParseThematicBreak(lineSpan))
            {
                var blockSpan = new MarkdownTextSpan(line.Start, line.Length);
                target.Add(new MarkdownThematicBreakBlock(_idProvider.NextId(), blockSpan));
                lineIndex++;
                continue;
            }

            if (TryParseFencedCodeBlock(target, ref lineIndex))
            {
                continue;
            }

            if (TryParseBlockQuote(target, ref lineIndex))
            {
                continue;
            }

            if (TryParseTable(target, ref lineIndex))
            {
                continue;
            }

            if (TryParseList(target, ref lineIndex))
            {
                continue;
            }

            if (TryParseIndentedCodeBlock(target, ref lineIndex))
            {
                continue;
            }

            ParseParagraph(target, ref lineIndex);
        }
    }

    private void ParseParagraph(List<MarkdownBlock> target, ref int lineIndex)
    {
        var startLine = lineIndex;
        var lines = new List<int>();
        while (lineIndex < _lines.Count)
        {
            var line = _lines[lineIndex];
            var lineSpan = GetLineSpan(line);
            if (line.IsBlank)
            {
                break;
            }

            if (lineIndex > startLine && IsBlockStart(lineSpan))
            {
                break;
            }

            lines.Add(lineIndex);
            lineIndex++;
        }

        if (lines.Count == 0)
        {
            lineIndex++;
            return;
        }

        if (lines.Count == 1 && lineIndex < _lines.Count)
        {
            var nextLine = _lines[lineIndex];
            var nextSpan = GetLineSpan(nextLine);
            if (TryParseSetextUnderline(nextSpan, out var level))
            {
                var line = _lines[lines[0]];
                var lineSpan = GetLineSpan(line);
                var blockSpan = new MarkdownTextSpan(line.Start, nextLine.Start + nextLine.Length - line.Start);
                var heading = new MarkdownHeadingBlock(_idProvider.NextId(), blockSpan, level);
                heading.Inlines.AddRange(ParseLineInlines(lineSpan, line.Start));
                target.Add(heading);
                lineIndex++;
                return;
            }
        }

        var firstLine = _lines[lines[0]];
        var lastLine = _lines[lines[^1]];
        var paragraphSpan = new MarkdownTextSpan(firstLine.Start, lastLine.Start + lastLine.Length - firstLine.Start);
        var paragraph = new MarkdownParagraphBlock(_idProvider.NextId(), paragraphSpan);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = _lines[lines[i]];
            var lineSpan = GetLineSpan(line);
            var parsed = ParseLineWithBreak(lineSpan, line.Start);
            paragraph.Inlines.AddRange(parsed.Inlines);
            if (i < lines.Count - 1)
            {
                var breakSpan = new MarkdownTextSpan(line.Start + parsed.ContentLength, 1);
                paragraph.Inlines.Add(parsed.HardBreak
                    ? new MarkdownHardBreakInline(_idProvider.NextId(), breakSpan)
                    : new MarkdownSoftBreakInline(_idProvider.NextId(), breakSpan));
            }
        }

        target.Add(paragraph);
    }

    private bool TryParseBlockQuote(List<MarkdownBlock> target, ref int lineIndex)
    {
        var line = _lines[lineIndex];
        var lineSpan = GetLineSpan(line);
        if (!TryStripBlockQuoteMarker(lineSpan, out _))
        {
            return false;
        }

        var quoteLines = new List<MarkdownLine>();
        var startIndex = lineIndex;
        while (lineIndex < _lines.Count)
        {
            var current = _lines[lineIndex];
            var currentSpan = GetLineSpan(current);
            if (current.IsBlank)
            {
                quoteLines.Add(new MarkdownLine(current.Start, current.Length, true));
                lineIndex++;
                continue;
            }

            if (!TryStripBlockQuoteMarker(currentSpan, out var currentStart))
            {
                break;
            }

            var trimmed = currentSpan.Slice(currentStart);
            quoteLines.Add(new MarkdownLine(current.Start + currentStart, trimmed.Length, IsBlank(trimmed)));
            lineIndex++;
        }

        var quoteSpan = new MarkdownTextSpan(_lines[startIndex].Start, _lines[lineIndex - 1].Start + _lines[lineIndex - 1].Length - _lines[startIndex].Start);
        var quote = new MarkdownBlockQuoteBlock(_idProvider.NextId(), quoteSpan);
        var nestedParser = new MarkdownParser(_options, _idProvider)
        {
            _text = _text,
            _lines = quoteLines
        };
        var nestedIndex = 0;
        nestedParser.ParseBlocks(quote.Blocks, ref nestedIndex, untilEnd: true, stopOnIndentLessThan: -1);
        target.Add(quote);
        return true;
    }

    private bool TryParseFencedCodeBlock(List<MarkdownBlock> target, ref int lineIndex)
    {
        var line = _lines[lineIndex];
        var lineSpan = GetLineSpan(line);
        if (!TryParseFenceStart(lineSpan, out var fenceChar, out var fenceLength, out var info, out var indent))
        {
            return false;
        }

        var startLine = lineIndex;
        var builder = new List<MarkdownLine>();
        lineIndex++;
        while (lineIndex < _lines.Count)
        {
            var current = _lines[lineIndex];
            var currentSpan = GetLineSpan(current);
            if (TryParseFenceEnd(currentSpan, fenceChar, fenceLength, indent))
            {
                lineIndex++;
                break;
            }

            builder.Add(current);
            lineIndex++;
        }

        var endLineIndex = Math.Max(lineIndex - 1, startLine);
        var blockSpan = new MarkdownTextSpan(_lines[startLine].Start, _lines[endLineIndex].Start + _lines[endLineIndex].Length - _lines[startLine].Start);
        var code = new MarkdownCodeBlock(_idProvider.NextId(), blockSpan)
        {
            Info = info,
            IsFenced = true,
            Text = JoinLines(builder)
        };
        target.Add(code);
        return true;
    }

    private bool TryParseIndentedCodeBlock(List<MarkdownBlock> target, ref int lineIndex)
    {
        var line = _lines[lineIndex];
        var lineSpan = GetLineSpan(line);
        var indent = CountIndent(lineSpan, out _);
        if (indent < 4)
        {
            return false;
        }

        var startLine = lineIndex;
        var lines = new List<(MarkdownLine Line, int Strip)>(4);
        while (lineIndex < _lines.Count)
        {
            var current = _lines[lineIndex];
            var currentSpan = GetLineSpan(current);
            if (!current.IsBlank)
            {
                var currentIndent = CountIndent(currentSpan, out _);
                if (currentIndent < 4)
                {
                    break;
                }

                lines.Add((current, 4));
            }
            else
            {
                lines.Add((current, 0));
            }

            lineIndex++;
        }

        var lastLine = _lines[Math.Max(lineIndex - 1, startLine)];
        var blockSpan = new MarkdownTextSpan(_lines[startLine].Start, lastLine.Start + lastLine.Length - _lines[startLine].Start);
        var code = new MarkdownCodeBlock(_idProvider.NextId(), blockSpan)
        {
            IsFenced = false,
            Text = JoinLines(lines)
        };
        target.Add(code);
        return true;
    }

    private bool TryParseList(List<MarkdownBlock> target, ref int lineIndex)
    {
        var line = _lines[lineIndex];
        var lineSpan = GetLineSpan(line);
        var indent = CountIndent(lineSpan, out var contentStartOffset);
        if (!TryParseListMarker(lineSpan.Slice(contentStartOffset), out var kind, out var markerLength, out var startNumber))
        {
            return false;
        }

        var startLine = lineIndex;
        var list = new MarkdownListBlock(_idProvider.NextId(), MarkdownTextSpan.Unknown, kind)
        {
            StartNumber = startNumber
        };

        while (lineIndex < _lines.Count)
        {
            var current = _lines[lineIndex];
            var currentSpan = GetLineSpan(current);
            if (current.IsBlank)
            {
                lineIndex++;
                continue;
            }

            var currentIndent = CountIndent(currentSpan, out var currentContentStartOffset);
            if (currentIndent < indent)
            {
                break;
            }

            if (!TryParseListMarker(currentSpan.Slice(currentContentStartOffset), out var currentKind, out var currentMarkerLength, out _))
            {
                break;
            }

            if (currentKind != kind)
            {
                break;
            }

            var itemLines = new List<(MarkdownLine Line, int ContentStart)>();
            var markerContentStart = current.Start + currentContentStartOffset + currentMarkerLength;
            itemLines.Add((current, markerContentStart));
            lineIndex++;

            while (lineIndex < _lines.Count)
            {
                var continuation = _lines[lineIndex];
                var continuationSpan = GetLineSpan(continuation);
                if (continuation.IsBlank)
                {
                    itemLines.Add((continuation, continuation.Start + continuation.Length));
                    lineIndex++;
                    continue;
                }

                var continuationIndent = CountIndent(continuationSpan, out var continuationContentStartOffset);
                if (continuationIndent <= indent)
                {
                    break;
                }

                itemLines.Add((continuation, continuation.Start + continuationContentStartOffset));
                lineIndex++;
            }

            var itemBlock = new MarkdownListItemBlock(_idProvider.NextId(), BuildSpan(itemLines));
            if (_options.Flavor == MarkdownFlavor.GitHub && _options.UseTaskLists)
            {
                var firstLine = itemLines[0];
                var lineText = _text.AsSpan(firstLine.ContentStart, firstLine.Line.Start + firstLine.Line.Length - firstLine.ContentStart);
                if (TryParseTaskMarker(lineText, out var isChecked, out var consumed))
                {
                    itemBlock.IsTask = true;
                    itemBlock.TaskChecked = isChecked;
                    itemLines[0] = (firstLine.Line, firstLine.ContentStart + consumed);
                }
            }
            var paragraph = new MarkdownParagraphBlock(_idProvider.NextId(), itemBlock.Span);
            for (var i = 0; i < itemLines.Count; i++)
            {
                var entry = itemLines[i];
                if (entry.ContentStart < entry.Line.Start + entry.Line.Length)
                {
                    var content = _text.AsSpan(entry.ContentStart, entry.Line.Start + entry.Line.Length - entry.ContentStart);
                    var parsed = ParseLineWithBreak(content, entry.ContentStart);
                    paragraph.Inlines.AddRange(parsed.Inlines);
                    if (i < itemLines.Count - 1)
                    {
                        var breakSpan = new MarkdownTextSpan(entry.Line.Start + parsed.ContentLength, 1);
                        paragraph.Inlines.Add(parsed.HardBreak
                            ? new MarkdownHardBreakInline(_idProvider.NextId(), breakSpan)
                            : new MarkdownSoftBreakInline(_idProvider.NextId(), breakSpan));
                    }
                }
                else if (i < itemLines.Count - 1)
                {
                    var breakSpan = new MarkdownTextSpan(entry.Line.Start + entry.Line.Length, 1);
                    paragraph.Inlines.Add(new MarkdownSoftBreakInline(_idProvider.NextId(), breakSpan));
                }
            }

            itemBlock.Blocks.Add(paragraph);
            list.Items.Add(itemBlock);
        }

        var endLine = _lines[Math.Max(lineIndex - 1, startLine)];
        list.Span = new MarkdownTextSpan(_lines[startLine].Start, endLine.Start + endLine.Length - _lines[startLine].Start);
        target.Add(list);
        return true;
    }

    private bool TryParseTable(List<MarkdownBlock> target, ref int lineIndex)
    {
        if (_options.Flavor != MarkdownFlavor.GitHub || !_options.UseGfmTables)
        {
            return false;
        }

        if (lineIndex + 1 >= _lines.Count)
        {
            return false;
        }

        var headerLine = _lines[lineIndex];
        var separatorLine = _lines[lineIndex + 1];
        var headerSpan = GetLineSpan(headerLine);
        var separatorSpan = GetLineSpan(separatorLine);
        if (headerLine.IsBlank || separatorLine.IsBlank)
        {
            return false;
        }

        if (headerSpan.IndexOf('|') < 0)
        {
            return false;
        }

        if (!TryParseAlignmentRow(separatorSpan, out var alignments))
        {
            return false;
        }

        var headerCells = SplitTableRow(headerSpan);
        if (headerCells.Count == 0)
        {
            return false;
        }

        var table = new MarkdownTableBlock(_idProvider.NextId(), MarkdownTextSpan.Unknown)
        {
            HasHeader = true
        };
        table.Alignments.AddRange(alignments);

        var headerRow = new MarkdownTableRow(_idProvider.NextId(), MarkdownTextSpan.Unknown) { IsHeader = true };
        AddTableCells(headerRow, headerCells, headerSpan, headerLine.Start);
        table.Rows.Add(headerRow);

        lineIndex += 2;
        while (lineIndex < _lines.Count)
        {
            var rowLine = _lines[lineIndex];
            if (rowLine.IsBlank)
            {
                break;
            }

            var rowSpan = GetLineSpan(rowLine);
            if (rowSpan.IndexOf('|') < 0)
            {
                break;
            }

            var cells = SplitTableRow(rowSpan);
            var row = new MarkdownTableRow(_idProvider.NextId(), MarkdownTextSpan.Unknown);
            AddTableCells(row, cells, rowSpan, rowLine.Start);
            table.Rows.Add(row);
            lineIndex++;
        }

        var lastIndex = Math.Clamp(lineIndex - 1, 0, _lines.Count - 1);
        var lastLine = _lines[lastIndex];
        table.Span = new MarkdownTextSpan(headerLine.Start, lastLine.Start + lastLine.Length - headerLine.Start);
        target.Add(table);
        return true;
    }

    private void AddTableCells(MarkdownTableRow row, IReadOnlyList<TableCellSlice> cells, ReadOnlySpan<char> line, int baseOffset)
    {
        foreach (var cell in cells)
        {
            var cellSpan = line.Slice(cell.Start, cell.Length);
            var cellText = TrimSpan(cellSpan, out var trimStart);
            var cellNode = new MarkdownTableCell(_idProvider.NextId(), MarkdownTextSpan.Unknown);
            if (!cellText.IsEmpty)
            {
                cellNode.Inlines.AddRange(ParseLineInlines(cellText, baseOffset + cell.Start + trimStart));
            }

            row.Cells.Add(cellNode);
        }
    }

    private static bool TryParseAlignmentRow(ReadOnlySpan<char> line, out List<MarkdownTableAlignment> alignments)
    {
        alignments = new List<MarkdownTableAlignment>();
        var cells = SplitTableRow(line);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            var trimmed = TrimSpan(line.Slice(cell.Start, cell.Length), out _);
            if (trimmed.IsEmpty)
            {
                return false;
            }

            var start = trimmed[0] == ':'; 
            var end = trimmed[^1] == ':'; 
            var dashCount = 0;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (ch == '-')
                {
                    dashCount++;
                    continue;
                }

                if (ch == ':' && (i == 0 || i == trimmed.Length - 1))
                {
                    continue;
                }

                return false;
            }

            if (dashCount < 1)
            {
                return false;
            }

            var alignment = MarkdownTableAlignment.None;
            if (start && end)
            {
                alignment = MarkdownTableAlignment.Center;
            }
            else if (start)
            {
                alignment = MarkdownTableAlignment.Left;
            }
            else if (end)
            {
                alignment = MarkdownTableAlignment.Right;
            }

            alignments.Add(alignment);
        }

        return true;
    }

    private static List<TableCellSlice> SplitTableRow(ReadOnlySpan<char> line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(line[end - 1]))
        {
            end--;
        }

        if (end <= start)
        {
            return new List<TableCellSlice>();
        }

        if (line[start] == '|')
        {
            start++;
        }

        if (end > start && line[end - 1] == '|')
        {
            end--;
        }

        var cells = new List<TableCellSlice>();
        var cellStart = start;
        for (var i = start; i < end; i++)
        {
            if (line[i] == '|')
            {
                var backslashCount = 0;
                var backslashIndex = i - 1;
                while (backslashIndex >= start && line[backslashIndex] == '\\')
                {
                    backslashCount++;
                    backslashIndex--;
                }

                if (backslashCount % 2 == 1)
                {
                    continue;
                }

                cells.Add(new TableCellSlice(cellStart, i - cellStart));
                cellStart = i + 1;
            }
        }

        cells.Add(new TableCellSlice(cellStart, end - cellStart));
        return cells;
    }

    private static bool TryParseTaskMarker(ReadOnlySpan<char> line, out bool isChecked, out int consumed)
    {
        isChecked = false;
        consumed = 0;
        var index = 0;
        while (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        if (index + 2 >= line.Length)
        {
            return false;
        }

        if (line[index] != '[' || line[index + 2] != ']')
        {
            return false;
        }

        var marker = line[index + 1];
        if (marker != ' ' && marker != 'x' && marker != 'X')
        {
            return false;
        }

        var next = index + 3;
        if (next < line.Length && !char.IsWhiteSpace(line[next]))
        {
            return false;
        }

        isChecked = marker == 'x' || marker == 'X';
        consumed = next < line.Length ? next + 1 : next;
        return true;
    }

    private static MarkdownTextSpan BuildSpan(List<(MarkdownLine Line, int ContentStart)> lines)
    {
        if (lines.Count == 0)
        {
            return MarkdownTextSpan.Unknown;
        }

        var first = lines[0].Line;
        var last = lines[^1].Line;
        return new MarkdownTextSpan(first.Start, last.Start + last.Length - first.Start);
    }

    private bool IsBlockStart(ReadOnlySpan<char> lineSpan)
    {
        var indent = CountIndent(lineSpan, out var contentStart);
        return TryParseAtxHeading(lineSpan, out _, out _)
               || TryParseThematicBreak(lineSpan)
               || TryParseFenceStart(lineSpan, out _, out _, out _, out _)
               || TryStripBlockQuoteMarker(lineSpan, out _)
               || TryParseListMarker(lineSpan.Slice(contentStart), out _, out _, out _)
               || (indent >= 4);
    }

    private List<MarkdownInline> ParseLineInlines(ReadOnlySpan<char> lineSpan, int baseOffset)
    {
        return MarkdownInlineParser.Parse(lineSpan, baseOffset, _idProvider, _options);
    }

    private LineParseResult ParseLineWithBreak(ReadOnlySpan<char> lineSpan, int baseOffset)
    {
        var end = lineSpan.Length;
        var hardBreak = false;

        var trimmedEnd = end;
        while (trimmedEnd > 0 && lineSpan[trimmedEnd - 1] == ' ')
        {
            trimmedEnd--;
        }

        var trailingSpaces = end - trimmedEnd;
        if (trailingSpaces >= 2)
        {
            hardBreak = true;
        }

        if (trimmedEnd > 0 && lineSpan[trimmedEnd - 1] == '\\')
        {
            hardBreak = true;
            trimmedEnd--;
        }

        var content = trimmedEnd <= 0 ? ReadOnlySpan<char>.Empty : lineSpan.Slice(0, trimmedEnd);
        var inlines = ParseLineInlines(content, baseOffset);
        return new LineParseResult(inlines, hardBreak, trimmedEnd);
    }

    private ReadOnlySpan<char> GetLineSpan(MarkdownLine line)
    {
        return _text.AsSpan(line.Start, line.Length);
    }

    private static List<MarkdownLine> SplitLines(ReadOnlySpan<char> text)
    {
        var lines = new List<MarkdownLine>();
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            while (index < text.Length && text[index] != '\n' && text[index] != '\r')
            {
                index++;
            }

            var length = index - start;
            var lineSpan = text.Slice(start, length);
            var isBlank = IsBlank(lineSpan);

            if (index < text.Length && text[index] == '\r')
            {
                index++;
                if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }
            }
            else if (index < text.Length && text[index] == '\n')
            {
                index++;
            }

            lines.Add(new MarkdownLine(start, length, isBlank));
        }

        if (text.Length == 0)
        {
            lines.Add(new MarkdownLine(0, 0, true));
            return lines;
        }

        var last = text[^1];
        if (last == '\n' || last == '\r')
        {
            lines.Add(new MarkdownLine(text.Length, 0, true));
        }

        return lines;
    }

    private string JoinLines(List<MarkdownLine> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = lines[i];
            if (line.Length > 0)
            {
                builder.Append(_text.AsSpan(line.Start, line.Length));
            }
        }

        return builder.ToString();
    }

    private string JoinLines(List<(MarkdownLine Line, int Strip)> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = lines[i];
            if (line.Line.Length == 0)
            {
                continue;
            }

            var span = _text.AsSpan(line.Line.Start, line.Line.Length);
            var start = Math.Min(line.Strip, span.Length);
            builder.Append(span.Slice(start));
        }

        return builder.ToString();
    }

    private static bool TryParseAtxHeading(ReadOnlySpan<char> line, out int level, out int contentStart)
    {
        level = 0;
        contentStart = 0;
        var index = 0;
        var indent = CountIndent(line, out index);
        if (indent > 3)
        {
            return false;
        }

        while (index < line.Length && line[index] == '#')
        {
            level++;
            index++;
        }

        if (level == 0 || level > 6)
        {
            return false;
        }

        if (index < line.Length && !char.IsWhiteSpace(line[index]))
        {
            return false;
        }

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        contentStart = index;
        return true;
    }

    private static bool TryParseSetextUnderline(ReadOnlySpan<char> line, out int level)
    {
        level = 0;
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        if (index >= line.Length)
        {
            return false;
        }

        var marker = line[index];
        if (marker != '=' && marker != '-')
        {
            return false;
        }

        var count = 0;
        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == marker)
            {
                count++;
            }
            else if (!char.IsWhiteSpace(ch))
            {
                return false;
            }

            index++;
        }

        if (count < 1)
        {
            return false;
        }

        level = marker == '=' ? 1 : 2;
        return true;
    }

    private static bool TryParseThematicBreak(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        if (index >= line.Length)
        {
            return false;
        }

        var marker = line[index];
        if (marker != '-' && marker != '*' && marker != '_')
        {
            return false;
        }

        var count = 0;
        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == marker)
            {
                count++;
            }
            else if (!char.IsWhiteSpace(ch))
            {
                return false;
            }

            index++;
        }

        return count >= 3;
    }

    private static bool TryParseFenceStart(ReadOnlySpan<char> line, out char fenceChar, out int fenceLength, out string? info, out int indent)
    {
        fenceChar = '\0';
        fenceLength = 0;
        info = null;
        indent = CountIndent(line, out var index);
        if (indent > 3)
        {
            return false;
        }

        if (index >= line.Length)
        {
            return false;
        }

        var ch = line[index];
        if (ch != '`' && ch != '~')
        {
            return false;
        }

        var count = 0;
        while (index < line.Length && line[index] == ch)
        {
            count++;
            index++;
        }

        if (count < 3)
        {
            return false;
        }

        fenceChar = ch;
        fenceLength = count;
        if (index < line.Length)
        {
            info = line.Slice(index).ToString().Trim();
        }

        return true;
    }

    private static bool TryParseFenceEnd(ReadOnlySpan<char> line, char fenceChar, int fenceLength, int indent)
    {
        var currentIndent = CountIndent(line, out var index);
        if (currentIndent > 3)
        {
            return false;
        }

        if (index >= line.Length || line[index] != fenceChar)
        {
            return false;
        }

        var count = 0;
        while (index < line.Length && line[index] == fenceChar)
        {
            count++;
            index++;
        }

        if (count < fenceLength)
        {
            return false;
        }

        while (index < line.Length)
        {
            if (!char.IsWhiteSpace(line[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool TryStripBlockQuoteMarker(ReadOnlySpan<char> line, out int contentStart)
    {
        contentStart = 0;
        var index = 0;
        var indent = CountIndent(line, out index);
        if (indent > 3)
        {
            return false;
        }

        if (index >= line.Length || line[index] != '>')
        {
            return false;
        }

        index++;
        if (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        contentStart = index;
        return true;
    }

    private static bool TryParseListMarker(ReadOnlySpan<char> line, out MarkdownListKind kind, out int markerLength, out int? startNumber)
    {
        kind = MarkdownListKind.Bullet;
        markerLength = 0;
        startNumber = null;

        if (line.Length == 0)
        {
            return false;
        }

        var ch = line[0];
        if (ch == '-' || ch == '*' || ch == '+')
        {
            if (line.Length >= 2 && !char.IsWhiteSpace(line[1]))
            {
                return false;
            }

            kind = MarkdownListKind.Bullet;
            markerLength = 1;
            if (line.Length >= 2)
            {
                markerLength = 2;
            }

            return true;
        }

        var index = 0;
        var number = 0;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            number = (number * 10) + (line[index] - '0');
            index++;
        }

        if (index == 0 || index >= line.Length)
        {
            return false;
        }

        if (line[index] != '.' && line[index] != ')')
        {
            return false;
        }

        index++;
        if (index >= line.Length || !char.IsWhiteSpace(line[index]))
        {
            return false;
        }

        kind = MarkdownListKind.Ordered;
        startNumber = number;
        markerLength = index + 1;
        return true;
    }

    private static bool IsBlank(ReadOnlySpan<char> line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountIndent(ReadOnlySpan<char> line, out int contentStart)
    {
        var indent = 0;
        var index = 0;
        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == ' ')
            {
                indent++;
            }
            else if (ch == '\t')
            {
                indent += 4;
            }
            else
            {
                break;
            }

            index++;
        }

        contentStart = index;
        return indent;
    }

    private static ReadOnlySpan<char> TrimSpan(ReadOnlySpan<char> span, out int trimStart)
    {
        var start = 0;
        var end = span.Length;
        while (start < end && span[start] == ' ')
        {
            start++;
        }

        while (end > start && span[end - 1] == ' ')
        {
            end--;
        }

        trimStart = start;
        return span.Slice(start, end - start);
    }

    private readonly record struct MarkdownLine(int Start, int Length, bool IsBlank);

    private readonly record struct LineParseResult(
        List<MarkdownInline> Inlines,
        bool HardBreak,
        int ContentLength);

    private readonly record struct TableCellSlice(int Start, int Length);
}
