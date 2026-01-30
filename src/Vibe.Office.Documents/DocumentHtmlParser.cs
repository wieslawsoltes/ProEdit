using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public static class DocumentHtmlParser
{
    public static bool TryParse(string html, out Document document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var fragment = ExtractHtmlFragment(html);
        var parser = new HtmlParser(fragment);
        document = parser.Parse();
        return true;
    }

    private static string ExtractHtmlFragment(string html)
    {
        var startMarkerIndex = html.IndexOf("StartFragment:", StringComparison.OrdinalIgnoreCase);
        var endMarkerIndex = html.IndexOf("EndFragment:", StringComparison.OrdinalIgnoreCase);
        if (startMarkerIndex >= 0 && endMarkerIndex >= 0)
        {
            var start = ReadOffset(html, startMarkerIndex + "StartFragment:".Length);
            var end = ReadOffset(html, endMarkerIndex + "EndFragment:".Length);
            if (start >= 0 && end > start && end <= html.Length)
            {
                return html.Substring(start, end - start);
            }
        }

        const string startTag = "<!--StartFragment-->";
        const string endTag = "<!--EndFragment-->";
        var startTagIndex = html.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var endTagIndex = html.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (startTagIndex >= 0 && endTagIndex > startTagIndex)
        {
            startTagIndex += startTag.Length;
            return html.Substring(startTagIndex, endTagIndex - startTagIndex);
        }

        return html;
    }

    private static int ReadOffset(string text, int startIndex)
    {
        var index = startIndex;
        while (index < text.Length && text[index] == ' ')
        {
            index++;
        }

        var end = index;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        if (end == index)
        {
            return -1;
        }

        if (int.TryParse(text.Substring(index, end - index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return -1;
    }

    private sealed class HtmlParser
    {
        private readonly string _html;
        private int _pos;
        private readonly Document _document;
        private readonly Stack<InlineState> _inlineStack = new();
        private readonly Stack<string> _inlineTagStack = new();
        private readonly Stack<ListContext> _listStack = new();
        private readonly Stack<TableContext> _tableStack = new();
        private readonly Stack<IList<Block>> _blockStack = new();
        private ParagraphBlock? _currentParagraph;
        private bool _pendingWhitespace;
        private bool _paragraphHasContent;
        private bool _inListItem;
        private int _listIdCounter;
        private string? _skipTag;

        public HtmlParser(string html)
        {
            _html = html ?? string.Empty;
            _document = DocumentTextFactory.CreateEmptyDocument();
            _blockStack.Push(_document.Blocks);
            _inlineStack.Push(new InlineState(new TextStyleProperties(), null));
        }

        public Document Parse()
        {
            while (_pos < _html.Length)
            {
                if (_html[_pos] == '<')
                {
                    var tag = ReadTag();
                    if (!string.IsNullOrEmpty(tag.Name))
                    {
                        HandleTag(tag);
                    }
                    continue;
                }

                var text = ReadText();
                if (_skipTag is null)
                {
                    AppendText(text);
                }
            }

            CloseParagraph();
            if (_document.Blocks.Count == 0)
            {
                _document.Blocks.Add(new ParagraphBlock());
            }

            return _document;
        }

        private string ReadText()
        {
            var start = _pos;
            while (_pos < _html.Length && _html[_pos] != '<')
            {
                _pos++;
            }

            return _pos > start ? _html.Substring(start, _pos - start) : string.Empty;
        }

        private HtmlTag ReadTag()
        {
            _pos++;
            if (_pos >= _html.Length)
            {
                return default;
            }

            if (StartsWith(_pos, "!--"))
            {
                var end = _html.IndexOf("-->", _pos + 3, StringComparison.Ordinal);
                _pos = end >= 0 ? end + 3 : _html.Length;
                return default;
            }

            if (_html[_pos] == '!')
            {
                var end = _html.IndexOf('>', _pos);
                _pos = end >= 0 ? end + 1 : _html.Length;
                return default;
            }

            if (_html[_pos] == '?')
            {
                var end = _html.IndexOf('>', _pos);
                _pos = end >= 0 ? end + 1 : _html.Length;
                return default;
            }

            var isEnd = false;
            if (_html[_pos] == '/')
            {
                isEnd = true;
                _pos++;
            }

            var name = ReadName();
            if (string.IsNullOrEmpty(name))
            {
                SkipToTagEnd();
                return default;
            }

            var tag = new HtmlTag(name.ToLowerInvariant(), isEnd);
            if (isEnd)
            {
                SkipToTagEnd();
                return tag;
            }

            while (_pos < _html.Length)
            {
                SkipWhitespace();
                if (_pos >= _html.Length)
                {
                    break;
                }

                if (_html[_pos] == '>')
                {
                    _pos++;
                    break;
                }

                if (_html[_pos] == '/')
                {
                    tag.IsSelfClosing = true;
                    _pos++;
                    continue;
                }

                var attrName = ReadName();
                if (string.IsNullOrEmpty(attrName))
                {
                    _pos++;
                    continue;
                }

                SkipWhitespace();
                string? attrValue = null;
                if (_pos < _html.Length && _html[_pos] == '=')
                {
                    _pos++;
                    SkipWhitespace();
                    attrValue = ReadAttributeValue();
                }

                tag.ApplyAttribute(attrName, attrValue);
            }

            if (IsVoidTag(tag.Name))
            {
                tag.IsSelfClosing = true;
            }

            return tag;
        }

        private void HandleTag(HtmlTag tag)
        {
            if (_skipTag is not null)
            {
                if (tag.IsEnd && string.Equals(tag.Name, _skipTag, StringComparison.Ordinal))
                {
                    _skipTag = null;
                }
                return;
            }

            if (tag.IsEnd)
            {
                HandleEndTag(tag.Name);
                return;
            }

            HandleStartTag(tag);
            if (tag.IsSelfClosing)
            {
                HandleEndTag(tag.Name);
            }
        }

        private void HandleStartTag(HtmlTag tag)
        {
            switch (tag.Name)
            {
                case "script":
                case "style":
                    _skipTag = tag.Name;
                    return;
                case "p":
                case "div":
                    StartParagraph(tag, isListItem: false);
                    return;
                case "br":
                    AppendLineBreak();
                    return;
                case "ul":
                    StartList(ListKind.Bullet);
                    return;
                case "ol":
                    StartList(ListKind.Numbered);
                    return;
                case "li":
                    StartParagraph(tag, isListItem: true);
                    return;
                case "table":
                    StartTable();
                    return;
                case "tr":
                    StartTableRow();
                    return;
                case "td":
                case "th":
                    StartTableCell(tag);
                    return;
                case "span":
                    PushInlineStyle(tag.Name, tag.Style, null, null);
                    return;
                case "b":
                case "strong":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.FontWeight = DocFontWeight.Bold, null);
                    return;
                case "i":
                case "em":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.FontStyle = DocFontStyle.Italic, null);
                    return;
                case "u":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.Underline = true, null);
                    return;
                case "s":
                case "strike":
                case "del":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.Strikethrough = true, null);
                    return;
                case "sup":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.VerticalPosition = DocVerticalPosition.Superscript, null);
                    return;
                case "sub":
                    PushInlineStyle(tag.Name, tag.Style, static style => style.VerticalPosition = DocVerticalPosition.Subscript, null);
                    return;
                case "a":
                {
                    var hyperlink = BuildHyperlink(tag.Href);
                    PushInlineStyle(tag.Name, tag.Style, null, hyperlink);
                    return;
                }
                case "img":
                    AppendImage(tag);
                    return;
                case "font":
                    PushInlineStyle(tag.Name, tag.Style, style => ApplyFontTag(style, tag), null);
                    return;
                case "pre":
                    StartParagraph(tag, isListItem: _inListItem);
                    return;
            }

            if (!string.IsNullOrWhiteSpace(tag.Style))
            {
                PushInlineStyle(tag.Name, tag.Style, null, null);
            }
        }

        private void HandleEndTag(string name)
        {
            switch (name)
            {
                case "p":
                case "div":
                    CloseParagraph();
                    PopInlineStyle(name);
                    return;
                case "li":
                    CloseParagraph();
                    _inListItem = false;
                    PopInlineStyle(name);
                    return;
                case "ul":
                case "ol":
                    CloseParagraph();
                    _inListItem = false;
                    if (_listStack.Count > 0)
                    {
                        _listStack.Pop();
                    }
                    return;
                case "table":
                    CloseParagraph();
                    if (_tableStack.Count > 0)
                    {
                        _tableStack.Pop();
                    }
                    return;
                case "tr":
                    if (_tableStack.Count > 0)
                    {
                        _tableStack.Peek().CurrentRow = null;
                    }
                    return;
                case "td":
                case "th":
                    CloseParagraph();
                    if (_blockStack.Count > 1)
                    {
                        _blockStack.Pop();
                    }
                    return;
                case "span":
                case "b":
                case "strong":
                case "i":
                case "em":
                case "u":
                case "s":
                case "strike":
                case "del":
                case "sup":
                case "sub":
                case "a":
                case "font":
                    PopInlineStyle(name);
                    return;
            }

            PopInlineStyle(name);
        }

        private void StartParagraph(HtmlTag tag, bool isListItem)
        {
            CloseParagraph();
            _inListItem = isListItem || _inListItem;
            var listInfo = isListItem ? BuildListInfo() : null;
            var paragraph = new ParagraphBlock(string.Empty, listInfo);
            if (!string.IsNullOrWhiteSpace(tag.Style))
            {
                ApplyParagraphStyle(paragraph.Properties, tag.Style);
                PushInlineStyle(tag.Name, tag.Style, null, null);
            }

            if (!string.IsNullOrWhiteSpace(tag.Align))
            {
                if (TryParseAlignment(tag.Align, out var alignment))
                {
                    paragraph.Properties.Alignment = alignment;
                }
            }

            AddBlock(paragraph);
            _currentParagraph = paragraph;
            _paragraphHasContent = false;
            _pendingWhitespace = false;
        }

        private void StartList(ListKind kind)
        {
            CloseParagraph();
            var level = _listStack.Count;
            var listId = _listStack.Count > 0 ? _listStack.Peek().ListId : ++_listIdCounter;
            _listStack.Push(new ListContext(kind, level, listId));
        }

        private void StartTable()
        {
            CloseParagraph();
            var table = new TableBlock();
            AddBlock(table);
            _tableStack.Push(new TableContext(table));
        }

        private void StartTableRow()
        {
            if (_tableStack.Count == 0)
            {
                return;
            }

            var row = new TableRow();
            var context = _tableStack.Peek();
            context.Table.Rows.Add(row);
            context.CurrentRow = row;
        }

        private void StartTableCell(HtmlTag tag)
        {
            if (_tableStack.Count == 0)
            {
                return;
            }

            var context = _tableStack.Peek();
            if (context.CurrentRow is null)
            {
                StartTableRow();
            }

            var row = context.CurrentRow;
            if (row is null)
            {
                return;
            }

            var cell = new TableCell();
            if (TryParseInteger(tag.ColSpan, out var colSpan) && colSpan > 1)
            {
                cell.ColumnSpan = colSpan;
            }

            row.Cells.Add(cell);
            _blockStack.Push(cell.Blocks);
            _currentParagraph = null;
            _paragraphHasContent = false;
            _pendingWhitespace = false;
        }

        private void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var decoded = WebUtility.HtmlDecode(text);
            if (string.IsNullOrEmpty(decoded))
            {
                return;
            }

            var builder = new StringBuilder();
            var span = decoded.AsSpan();
            for (var i = 0; i < span.Length; i++)
            {
                var ch = span[i];
                if (char.IsWhiteSpace(ch))
                {
                    _pendingWhitespace = true;
                    continue;
                }

                if (_pendingWhitespace && _paragraphHasContent)
                {
                    builder.Append(' ');
                }

                _pendingWhitespace = false;
                builder.Append(ch);
            }

            if (builder.Length == 0)
            {
                return;
            }

            AppendRun(builder.ToString());
        }

        private void AppendLineBreak()
        {
            EnsureParagraph();
            AppendRun("\n");
            _pendingWhitespace = false;
        }

        private void AppendRun(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paragraph = EnsureParagraph();
            var state = _inlineStack.Peek();
            var style = state.Style.HasValues ? state.Style : null;
            var run = new RunInline(text, style);
            if (state.Hyperlink is not null)
            {
                run.Hyperlink = state.Hyperlink;
            }

            paragraph.Inlines.Add(run);
            _paragraphHasContent = true;
        }

        private void AppendImage(HtmlTag tag)
        {
            if (string.IsNullOrWhiteSpace(tag.Src))
            {
                return;
            }

            if (!TryParseDataUri(tag.Src, out var contentType, out var data))
            {
                return;
            }

            var width = 0f;
            var height = 0f;
            if (TryParseCssLength(tag.Width, out var parsedWidth))
            {
                width = parsedWidth;
            }
            else if (TryParseStyleLength(tag.Style, "width", out var styleWidth))
            {
                width = styleWidth;
            }

            if (TryParseCssLength(tag.Height, out var parsedHeight))
            {
                height = parsedHeight;
            }
            else if (TryParseStyleLength(tag.Style, "height", out var styleHeight))
            {
                height = styleHeight;
            }

            if ((width <= 0f || height <= 0f) && TryGetImageSize(data, out var imageWidth, out var imageHeight))
            {
                if (width <= 0f)
                {
                    width = imageWidth;
                }

                if (height <= 0f)
                {
                    height = imageHeight;
                }
            }

            if (width <= 0f || height <= 0f)
            {
                return;
            }

            var image = new ImageInline(data, width, height, contentType);
            var state = _inlineStack.Peek();
            if (state.Hyperlink is not null)
            {
                image.Hyperlink = state.Hyperlink;
            }

            var paragraph = EnsureParagraph();
            paragraph.Inlines.Add(image);
            _paragraphHasContent = true;
        }

        private ParagraphBlock EnsureParagraph()
        {
            if (_currentParagraph is not null)
            {
                return _currentParagraph;
            }

            var paragraph = new ParagraphBlock(string.Empty, BuildListInfo());
            AddBlock(paragraph);
            _currentParagraph = paragraph;
            _paragraphHasContent = false;
            _pendingWhitespace = false;
            return paragraph;
        }

        private void CloseParagraph()
        {
            _currentParagraph = null;
            _paragraphHasContent = false;
            _pendingWhitespace = false;
        }

        private void AddBlock(Block block)
        {
            _blockStack.Peek().Add(block);
        }

        private ListInfo? BuildListInfo()
        {
            if (!_inListItem || _listStack.Count == 0)
            {
                return null;
            }

            var context = _listStack.Peek();
            return new ListInfo(context.Kind, context.Level, context.ListId);
        }

        private void PushInlineStyle(
            string tagName,
            string? styleText,
            Action<TextStyleProperties>? apply,
            HyperlinkInfo? hyperlink)
        {
            var current = _inlineStack.Peek();
            var nextStyle = current.Style;
            var hasStyle = false;

            if (!string.IsNullOrWhiteSpace(styleText))
            {
                nextStyle = current.Style.Clone();
                ApplyInlineStyle(nextStyle, styleText);
                hasStyle = true;
            }

            if (apply is not null)
            {
                if (!hasStyle)
                {
                    nextStyle = current.Style.Clone();
                    hasStyle = true;
                }

                apply(nextStyle);
            }

            var nextHyperlink = hyperlink ?? current.Hyperlink;
            if (!hasStyle && ReferenceEquals(nextHyperlink, current.Hyperlink))
            {
                return;
            }

            _inlineStack.Push(new InlineState(nextStyle, nextHyperlink));
            _inlineTagStack.Push(tagName);
        }

        private void PopInlineStyle(string tagName)
        {
            if (_inlineTagStack.Count == 0)
            {
                return;
            }

            if (!string.Equals(_inlineTagStack.Peek(), tagName, StringComparison.Ordinal))
            {
                return;
            }

            _inlineTagStack.Pop();
            if (_inlineStack.Count > 1)
            {
                _inlineStack.Pop();
            }
        }

        private static void ApplyParagraphStyle(ParagraphProperties properties, string styleText)
        {
            ParseStyleDeclarations(styleText, (name, value) =>
            {
                if (name.Equals("text-align".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseAlignment(value, out var alignment))
                    {
                        properties.Alignment = alignment;
                    }

                    return;
                }

                if (name.Equals("margin-left".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssLength(value, out var length))
                    {
                        properties.IndentLeft = length;
                    }

                    return;
                }

                if (name.Equals("margin-right".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssLength(value, out var length))
                    {
                        properties.IndentRight = length;
                    }

                    return;
                }

                if (name.Equals("text-indent".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssLength(value, out var length))
                    {
                        properties.FirstLineIndent = length;
                    }

                    return;
                }

                if (name.Equals("background-color".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssColor(value, out var color))
                    {
                        properties.ShadingColor = color;
                    }
                }
            });
        }

        private static void ApplyInlineStyle(TextStyleProperties style, string styleText)
        {
            ParseStyleDeclarations(styleText, (name, value) =>
            {
                if (name.Equals("font-weight".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Equals("bold".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.FontWeight = DocFontWeight.Bold;
                    }
                    else if (value.Equals("normal".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.FontWeight = DocFontWeight.Normal;
                    }
                    else if (TryParseInteger(value, out var weightValue))
                    {
                        style.FontWeight = weightValue >= 600 ? DocFontWeight.Bold : DocFontWeight.Normal;
                    }

                    return;
                }

                if (name.Equals("font-style".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Equals("italic".AsSpan(), StringComparison.OrdinalIgnoreCase)
                        || value.Equals("oblique".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.FontStyle = DocFontStyle.Italic;
                    }
                    else if (value.Equals("normal".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.FontStyle = DocFontStyle.Normal;
                    }

                    return;
                }

                if (name.Equals("text-decoration".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    var lowered = value.ToString().ToLowerInvariant();
                    if (lowered.Contains("underline", StringComparison.Ordinal))
                    {
                        style.Underline = true;
                    }

                    if (lowered.Contains("line-through", StringComparison.Ordinal))
                    {
                        style.Strikethrough = true;
                    }

                    if (lowered.Contains("none", StringComparison.Ordinal))
                    {
                        style.Underline = false;
                        style.Strikethrough = false;
                    }

                    return;
                }

                if (name.Equals("font-size".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssLength(value, out var length))
                    {
                        style.FontSize = length;
                    }

                    return;
                }

                if (name.Equals("font-family".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    var family = NormalizeFontFamily(value);
                    if (!string.IsNullOrWhiteSpace(family))
                    {
                        style.FontFamily = family;
                    }

                    return;
                }

                if (name.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssColor(value, out var color))
                    {
                        style.Color = color;
                    }

                    return;
                }

                if (name.Equals("background-color".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssColor(value, out var color))
                    {
                        style.HighlightColor = color;
                    }

                    return;
                }

                if (name.Equals("vertical-align".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Equals("super".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.VerticalPosition = DocVerticalPosition.Superscript;
                    }
                    else if (value.Equals("sub".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.VerticalPosition = DocVerticalPosition.Subscript;
                    }

                    return;
                }

                if (name.Equals("letter-spacing".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseCssLength(value, out var length))
                    {
                        style.LetterSpacing = length;
                    }

                    return;
                }

                if (name.Equals("font-variant".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Equals("small-caps".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        style.SmallCaps = true;
                    }
                }
            });
        }

        private static void ApplyFontTag(TextStyleProperties style, HtmlTag tag)
        {
            if (!string.IsNullOrWhiteSpace(tag.Color) && TryParseCssColor(tag.Color.AsSpan(), out var color))
            {
                style.Color = color;
            }

            if (!string.IsNullOrWhiteSpace(tag.Face))
            {
                style.FontFamily = tag.Face;
            }

            if (TryParseCssLength(tag.Size, out var length))
            {
                style.FontSize = length;
            }
            else if (TryParseInteger(tag.Size, out var sizeValue))
            {
                style.FontSize = MathF.Max(1f, sizeValue * 4f);
            }
        }

        private static HyperlinkInfo? BuildHyperlink(string? href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return null;
            }

            var anchorIndex = href.IndexOf('#', StringComparison.Ordinal);
            if (anchorIndex >= 0)
            {
                var uri = anchorIndex > 0 ? href.Substring(0, anchorIndex) : null;
                var anchor = anchorIndex + 1 < href.Length ? href.Substring(anchorIndex + 1) : null;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    uri = null;
                }

                if (string.IsNullOrWhiteSpace(anchor))
                {
                    anchor = null;
                }

                return new HyperlinkInfo(uri, anchor, null);
            }

            return new HyperlinkInfo(href, null, null);
        }

        private static void ParseStyleDeclarations(string styleText, Action<ReadOnlySpan<char>, ReadOnlySpan<char>> handler)
        {
            var span = styleText.AsSpan();
            var index = 0;
            while (index < span.Length)
            {
                while (index < span.Length && (span[index] == ';' || char.IsWhiteSpace(span[index])))
                {
                    index++;
                }

                if (index >= span.Length)
                {
                    break;
                }

                var nameStart = index;
                while (index < span.Length && span[index] != ':' && span[index] != ';')
                {
                    index++;
                }

                var name = TrimSpan(span.Slice(nameStart, index - nameStart));
                if (index >= span.Length || span[index] != ':')
                {
                    while (index < span.Length && span[index] != ';')
                    {
                        index++;
                    }
                    continue;
                }

                index++;
                var valueStart = index;
                while (index < span.Length && span[index] != ';')
                {
                    index++;
                }

                var value = TrimSpan(span.Slice(valueStart, index - valueStart));
                if (!name.IsEmpty && !value.IsEmpty)
                {
                    handler(name, value);
                }
            }
        }

        private static bool TryParseAlignment(ReadOnlySpan<char> value, out ParagraphAlignment alignment)
        {
            if (value.Equals("center".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                alignment = ParagraphAlignment.Center;
                return true;
            }

            if (value.Equals("right".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                alignment = ParagraphAlignment.Right;
                return true;
            }

            if (value.Equals("justify".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                alignment = ParagraphAlignment.Justify;
                return true;
            }

            alignment = ParagraphAlignment.Left;
            return value.Equals("left".AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeFontFamily(ReadOnlySpan<char> value)
        {
            var span = TrimSpan(value);
            if (span.IsEmpty)
            {
                return null;
            }

            var commaIndex = span.IndexOf(',');
            if (commaIndex >= 0)
            {
                span = span.Slice(0, commaIndex);
            }

            span = TrimSpan(span);
            if (span.Length >= 2 && ((span[0] == '"' && span[^1] == '"') || (span[0] == '\'' && span[^1] == '\'')))
            {
                span = span.Slice(1, span.Length - 2);
            }

            return span.IsEmpty ? null : span.ToString();
        }

        private static ReadOnlySpan<char> TrimSpan(ReadOnlySpan<char> value)
        {
            var start = 0;
            var end = value.Length - 1;
            while (start <= end && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            return start > end ? ReadOnlySpan<char>.Empty : value.Slice(start, end - start + 1);
        }

        private static bool TryParseCssLength(ReadOnlySpan<char> value, out float length)
        {
            length = 0f;
            var span = TrimSpan(value);
            if (span.IsEmpty)
            {
                return false;
            }

            if (span.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                span = span[..^2];
            }
            else if (span.EndsWith("pt".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(span[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var points))
                {
                    length = points * 96f / 72f;
                    return true;
                }

                return false;
            }

            return float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out length);
        }

        private static bool TryParseCssLength(string? value, out float length)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                length = 0f;
                return false;
            }

            return TryParseCssLength(value.AsSpan(), out length);
        }

        private static bool TryParseStyleLength(string? styleText, string propertyName, out float length)
        {
            length = 0f;
            if (string.IsNullOrWhiteSpace(styleText))
            {
                return false;
            }

            var found = false;
            var parsedLength = 0f;
            ParseStyleDeclarations(styleText, (name, value) =>
            {
                if (found)
                {
                    return;
                }

                if (name.Equals(propertyName.AsSpan(), StringComparison.OrdinalIgnoreCase)
                    && TryParseCssLength(value, out var parsed))
                {
                    parsedLength = parsed;
                    found = true;
                }
            });

            if (found)
            {
                length = parsedLength;
            }

            return found;
        }

        private static bool TryParseCssColor(ReadOnlySpan<char> value, out DocColor color)
        {
            color = default;
            var span = TrimSpan(value);
            if (span.IsEmpty)
            {
                return false;
            }

            if (span.Equals("transparent".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = DocColor.Transparent;
                return true;
            }

            if (span[0] == '#')
            {
                return TryParseHexColor(span, out color);
            }

            if (span.StartsWith("rgb".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryParseRgbColor(span, out color);
            }

            return TryParseNamedColor(span, out color);
        }

        private static bool TryParseNamedColor(ReadOnlySpan<char> value, out DocColor color)
        {
            if (value.Equals("black".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = DocColor.Black;
                return true;
            }

            if (value.Equals("white".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = DocColor.White;
                return true;
            }

            if (value.Equals("red".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = new DocColor(255, 0, 0);
                return true;
            }

            if (value.Equals("green".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = new DocColor(0, 128, 0);
                return true;
            }

            if (value.Equals("blue".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = new DocColor(0, 0, 255);
                return true;
            }

            if (value.Equals("gray".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || value.Equals("grey".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                color = new DocColor(128, 128, 128);
                return true;
            }

            color = default;
            return false;
        }

        private static bool TryParseHexColor(ReadOnlySpan<char> value, out DocColor color)
        {
            color = default;
            if (value.Length == 4)
            {
                if (TryParseHexDigit(value[1], out var r)
                    && TryParseHexDigit(value[2], out var g)
                    && TryParseHexDigit(value[3], out var b))
                {
                    color = new DocColor((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                    return true;
                }

                return false;
            }

            if (value.Length == 7)
            {
                if (TryParseHexByte(value[1], value[2], out var r)
                    && TryParseHexByte(value[3], value[4], out var g)
                    && TryParseHexByte(value[5], value[6], out var b))
                {
                    color = new DocColor(r, g, b);
                    return true;
                }

                return false;
            }

            if (value.Length == 9)
            {
                if (TryParseHexByte(value[1], value[2], out var a)
                    && TryParseHexByte(value[3], value[4], out var r)
                    && TryParseHexByte(value[5], value[6], out var g)
                    && TryParseHexByte(value[7], value[8], out var b))
                {
                    color = DocColor.FromArgb(a, r, g, b);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseRgbColor(ReadOnlySpan<char> value, out DocColor color)
        {
            color = default;
            var open = value.IndexOf('(');
            var close = value.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return false;
            }

            var content = value.Slice(open + 1, close - open - 1);
            Span<int> components = stackalloc int[4];
            var count = 0;
            var index = 0;
            while (index < content.Length && count < components.Length)
            {
                while (index < content.Length && (content[index] == ' ' || content[index] == ','))
                {
                    index++;
                }

                if (index >= content.Length)
                {
                    break;
                }

                var start = index;
                while (index < content.Length && content[index] != ',' && content[index] != ' ')
                {
                    index++;
                }

                var token = TrimSpan(content.Slice(start, index - start));
                if (token.IsEmpty)
                {
                    continue;
                }

                if (!TryParseCssComponent(token, out var valueComponent))
                {
                    return false;
                }

                components[count++] = valueComponent;
            }

            if (count < 3)
            {
                return false;
            }

            var r = ClampByte(components[0]);
            var g = ClampByte(components[1]);
            var b = ClampByte(components[2]);
            if (count >= 4)
            {
                var alpha = ClampByte(components[3]);
                color = DocColor.FromArgb(alpha, r, g, b);
            }
            else
            {
                color = new DocColor(r, g, b);
            }

            return true;
        }

        private static bool TryParseCssComponent(ReadOnlySpan<char> token, out int value)
        {
            value = 0;
            if (token.EndsWith("%".AsSpan(), StringComparison.Ordinal))
            {
                if (float.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    value = (int)MathF.Round(percent / 100f * 255f);
                    return true;
                }

                return false;
            }

            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static byte ClampByte(int value)
        {
            return (byte)Math.Clamp(value, 0, 255);
        }

        private static bool TryParseHexDigit(char ch, out int value)
        {
            value = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'f' => ch - 'a' + 10,
                >= 'A' and <= 'F' => ch - 'A' + 10,
                _ => -1
            };

            return value >= 0;
        }

        private static bool TryParseHexByte(char high, char low, out byte value)
        {
            value = 0;
            if (!TryParseHexDigit(high, out var highValue) || !TryParseHexDigit(low, out var lowValue))
            {
                return false;
            }

            value = (byte)((highValue << 4) + lowValue);
            return true;
        }

        private static bool TryParseInteger(string? value, out int result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseInteger(ReadOnlySpan<char> value, out int result)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseDataUri(string src, out string contentType, out byte[] data)
        {
            contentType = "image/png";
            data = Array.Empty<byte>();
            if (!src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var commaIndex = src.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 5 || commaIndex + 1 >= src.Length)
            {
                return false;
            }

            var header = src.Substring(5, commaIndex - 5);
            var payload = src.Substring(commaIndex + 1);
            var base64Index = header.IndexOf(";base64", StringComparison.OrdinalIgnoreCase);
            if (base64Index >= 0)
            {
                contentType = header.Substring(0, base64Index);
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    contentType = "image/png";
                }

                data = Convert.FromBase64String(payload);
                return true;
            }

            return false;
        }

        private static bool TryGetImageSize(byte[] data, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (data is null || data.Length < 10)
            {
                return false;
            }

            if (TryGetPngSize(data, out width, out height))
            {
                return true;
            }

            if (TryGetGifSize(data, out width, out height))
            {
                return true;
            }

            if (TryGetJpegSize(data, out width, out height))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetPngSize(byte[] data, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (data.Length < 24)
            {
                return false;
            }

            if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            {
                return false;
            }

            width = ReadBigEndianUInt(data, 16);
            height = ReadBigEndianUInt(data, 20);
            return width > 0f && height > 0f;
        }

        private static bool TryGetGifSize(byte[] data, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (data.Length < 10)
            {
                return false;
            }

            if (data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
            {
                return false;
            }

            width = data[6] | (data[7] << 8);
            height = data[8] | (data[9] << 8);
            return width > 0f && height > 0f;
        }

        private static bool TryGetJpegSize(byte[] data, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                return false;
            }

            var index = 2;
            while (index + 1 < data.Length)
            {
                if (data[index] != 0xFF)
                {
                    index++;
                    continue;
                }

                var marker = data[index + 1];
                if (marker == 0xD9 || marker == 0xDA)
                {
                    break;
                }

                if (index + 3 >= data.Length)
                {
                    break;
                }

                var length = (data[index + 2] << 8) + data[index + 3];
                if (length < 2 || index + 1 + length >= data.Length)
                {
                    break;
                }

                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    if (index + 8 >= data.Length)
                    {
                        break;
                    }

                    height = (data[index + 5] << 8) + data[index + 6];
                    width = (data[index + 7] << 8) + data[index + 8];
                    return width > 0f && height > 0f;
                }

                index += 2 + length;
            }

            return false;
        }

        private static float ReadBigEndianUInt(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private bool StartsWith(int index, string value)
        {
            if (index < 0 || index + value.Length > _html.Length)
            {
                return false;
            }

            return _html.AsSpan(index, value.Length).SequenceEqual(value.AsSpan());
        }

        private void SkipWhitespace()
        {
            while (_pos < _html.Length && char.IsWhiteSpace(_html[_pos]))
            {
                _pos++;
            }
        }

        private string ReadName()
        {
            var start = _pos;
            while (_pos < _html.Length)
            {
                var ch = _html[_pos];
                if (!char.IsLetterOrDigit(ch) && ch != ':' && ch != '_' && ch != '-')
                {
                    break;
                }

                _pos++;
            }

            return _pos > start ? _html.Substring(start, _pos - start) : string.Empty;
        }

        private string? ReadAttributeValue()
        {
            if (_pos >= _html.Length)
            {
                return null;
            }

            var quote = _html[_pos];
            if (quote == '\'' || quote == '"')
            {
                _pos++;
                var start = _pos;
                while (_pos < _html.Length && _html[_pos] != quote)
                {
                    _pos++;
                }

                var value = _pos > start ? _html.Substring(start, _pos - start) : string.Empty;
                if (_pos < _html.Length)
                {
                    _pos++;
                }

                return value;
            }

            var valueStart = _pos;
            while (_pos < _html.Length && !char.IsWhiteSpace(_html[_pos]) && _html[_pos] != '>')
            {
                _pos++;
            }

            return _pos > valueStart ? _html.Substring(valueStart, _pos - valueStart) : string.Empty;
        }

        private void SkipToTagEnd()
        {
            while (_pos < _html.Length && _html[_pos] != '>')
            {
                _pos++;
            }

            if (_pos < _html.Length)
            {
                _pos++;
            }
        }

        private static bool IsVoidTag(string name)
        {
            return name is "br" or "img" or "hr" or "meta" or "link" or "input";
        }

        private readonly struct InlineState
        {
            public InlineState(TextStyleProperties style, HyperlinkInfo? hyperlink)
            {
                Style = style;
                Hyperlink = hyperlink;
            }

            public TextStyleProperties Style { get; }
            public HyperlinkInfo? Hyperlink { get; }
        }

        private readonly struct ListContext
        {
            public ListContext(ListKind kind, int level, int listId)
            {
                Kind = kind;
                Level = level;
                ListId = listId;
            }

            public ListKind Kind { get; }
            public int Level { get; }
            public int ListId { get; }
        }

        private sealed class TableContext
        {
            public TableContext(TableBlock table)
            {
                Table = table;
            }

            public TableBlock Table { get; }
            public TableRow? CurrentRow { get; set; }
        }

        private struct HtmlTag
        {
            public HtmlTag(string name, bool isEnd)
            {
                Name = name;
                IsEnd = isEnd;
                IsSelfClosing = false;
                Style = null;
                Href = null;
                Src = null;
                Width = null;
                Height = null;
                ColSpan = null;
                RowSpan = null;
                Align = null;
                Color = null;
                Face = null;
                Size = null;
            }

            public string Name { get; }
            public bool IsEnd { get; }
            public bool IsSelfClosing { get; set; }
            public string? Style { get; set; }
            public string? Href { get; set; }
            public string? Src { get; set; }
            public string? Width { get; set; }
            public string? Height { get; set; }
            public string? ColSpan { get; set; }
            public string? RowSpan { get; set; }
            public string? Align { get; set; }
            public string? Color { get; set; }
            public string? Face { get; set; }
            public string? Size { get; set; }

            public void ApplyAttribute(string name, string? value)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    Style = value;
                }
                else if (name.Equals("href", StringComparison.OrdinalIgnoreCase))
                {
                    Href = value;
                }
                else if (name.Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    Src = value;
                }
                else if (name.Equals("width", StringComparison.OrdinalIgnoreCase))
                {
                    Width = value;
                }
                else if (name.Equals("height", StringComparison.OrdinalIgnoreCase))
                {
                    Height = value;
                }
                else if (name.Equals("colspan", StringComparison.OrdinalIgnoreCase))
                {
                    ColSpan = value;
                }
                else if (name.Equals("rowspan", StringComparison.OrdinalIgnoreCase))
                {
                    RowSpan = value;
                }
                else if (name.Equals("align", StringComparison.OrdinalIgnoreCase))
                {
                    Align = value;
                }
                else if (name.Equals("color", StringComparison.OrdinalIgnoreCase))
                {
                    Color = value;
                }
                else if (name.Equals("face", StringComparison.OrdinalIgnoreCase))
                {
                    Face = value;
                }
                else if (name.Equals("size", StringComparison.OrdinalIgnoreCase))
                {
                    Size = value;
                }
            }
        }
    }
}
