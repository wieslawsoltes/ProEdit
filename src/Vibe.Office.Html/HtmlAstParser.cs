using System.Net;
using Vibe.Office.Html.Ast;

namespace Vibe.Office.Html;

public sealed class HtmlAstParser
{
    private readonly HtmlOptions _options;
    private readonly HtmlNodeIdProvider _idProvider;
    private string _source = string.Empty;
    private int _pos;
    private HtmlDocument? _document;
    private readonly Stack<HtmlElementNode> _elementStack = new();
    private string? _skipTag;

    public HtmlAstParser(HtmlOptions? options = null, HtmlNodeIdProvider? idProvider = null)
    {
        _options = options ?? new HtmlOptions();
        _idProvider = idProvider ?? new HtmlNodeIdProvider();
    }

    public HtmlDocument Parse(ReadOnlySpan<char> html)
    {
        _source = html.Length == 0 ? string.Empty : html.ToString();
        _pos = 0;
        _skipTag = null;
        _elementStack.Clear();

        _document = new HtmlDocument(_idProvider.NextId(), new HtmlTextSpan(0, _source.Length));

        while (_pos < Html.Length)
        {
            if (Html[_pos] == '<')
            {
                ReadTag();
                continue;
            }

            ReadTextNode();
        }

        CloseOpenElements();
        return _document;
    }

    private void ReadTextNode()
    {
        var start = _pos;
        while (_pos < Html.Length && Html[_pos] != '<')
        {
            _pos++;
        }

        if (_skipTag is not null || _pos <= start)
        {
            return;
        }

        var raw = Html.Slice(start, _pos - start);
        var decoded = WebUtility.HtmlDecode(raw.ToString());
        if (_options.NormalizeLineEndings)
        {
            decoded = NormalizeLineEndings(decoded);
        }

        if (decoded.Length == 0)
        {
            return;
        }

        var node = new HtmlTextNode(_idProvider.NextId(), new HtmlTextSpan(start, raw.Length), decoded);
        AddNode(node);
    }

    private void ReadTag()
    {
        var tagStart = _pos;
        _pos++;
        if (_pos >= Html.Length)
        {
            return;
        }

        if (StartsWith("!--"))
        {
            ReadComment(tagStart);
            return;
        }

        var ch = Html[_pos];
        if (ch == '!' || ch == '?')
        {
            SkipToTagEnd();
            return;
        }

        var isEnd = false;
        if (ch == '/')
        {
            isEnd = true;
            _pos++;
        }

        var name = ReadName();
        if (string.IsNullOrEmpty(name))
        {
            SkipToTagEnd();
            return;
        }

        if (isEnd)
        {
            SkipToTagEnd();
            CloseElement(name, _pos);
            return;
        }

        var element = new HtmlElementNode(_idProvider.NextId(), new HtmlTextSpan(tagStart, 0), name);
        var selfClosing = false;

        while (_pos < Html.Length)
        {
            SkipWhitespace();
            if (_pos >= Html.Length)
            {
                break;
            }

            var current = Html[_pos];
            if (current == '>')
            {
                _pos++;
                break;
            }

            if (current == '/')
            {
                selfClosing = true;
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
            if (_pos < Html.Length && Html[_pos] == '=')
            {
                _pos++;
                SkipWhitespace();
                attrValue = ReadAttributeValue();
            }

            if (!string.IsNullOrWhiteSpace(attrName))
            {
                element.Attributes.Add(new HtmlAttribute(attrName, attrValue));
            }
        }

        element.IsVoidElement = selfClosing || HtmlVoidElements.IsVoid(element.Name);
        element.Span = new HtmlTextSpan(tagStart, _pos - tagStart);
        AddNode(element);

        if (!element.IsVoidElement)
        {
            _elementStack.Push(element);
            if (!_options.AllowScripts && element.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                _skipTag = element.Name;
            }
            else if (!_options.AllowStyles && element.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                _skipTag = element.Name;
            }
        }
    }

    private void ReadComment(int tagStart)
    {
        var commentStart = _pos + 3;
        var end = IndexOf("-->", commentStart);
        if (end < 0)
        {
            _pos = Html.Length;
            return;
        }

        var commentText = Html.Slice(commentStart, end - commentStart).ToString();
        if (_options.NormalizeLineEndings)
        {
            commentText = NormalizeLineEndings(commentText);
        }

        var comment = new HtmlCommentNode(_idProvider.NextId(), new HtmlTextSpan(tagStart, end + 3 - tagStart), commentText);
        if (_skipTag is null)
        {
            AddNode(comment);
        }

        _pos = end + 3;
    }

    private void CloseElement(string name, int endPosition)
    {
        if (_elementStack.Count == 0)
        {
            return;
        }

        HtmlElementNode? matched = null;
        while (_elementStack.Count > 0)
        {
            var element = _elementStack.Pop();
            if (element.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                matched = element;
                break;
            }
        }

        if (matched is null)
        {
            return;
        }

        matched.Span = new HtmlTextSpan(matched.Span.Start, endPosition - matched.Span.Start);
        if (_skipTag is not null && matched.Name.Equals(_skipTag, StringComparison.OrdinalIgnoreCase))
        {
            _skipTag = null;
        }
    }

    private void CloseOpenElements()
    {
        if (_document is null)
        {
            return;
        }

        var end = Html.Length;
        while (_elementStack.Count > 0)
        {
            var element = _elementStack.Pop();
            if (element.Span.IsKnown)
            {
                element.Span = new HtmlTextSpan(element.Span.Start, end - element.Span.Start);
            }
        }
    }

    private void AddNode(HtmlNode node)
    {
        if (_document is null)
        {
            return;
        }

        if (_elementStack.Count == 0)
        {
            _document.Children.Add(node);
        }
        else
        {
            _elementStack.Peek().Children.Add(node);
        }
    }

    private string ReadName()
    {
        var start = _pos;
        while (_pos < Html.Length && IsNameChar(Html[_pos]))
        {
            _pos++;
        }

        if (_pos <= start)
        {
            return string.Empty;
        }

        return Html.Slice(start, _pos - start).ToString();
    }

    private string ReadAttributeValue()
    {
        if (_pos >= Html.Length)
        {
            return string.Empty;
        }

        var quote = Html[_pos];
        if (quote == '"' || quote == '\'')
        {
            _pos++;
            var start = _pos;
            while (_pos < Html.Length && Html[_pos] != quote)
            {
                _pos++;
            }

            var value = Html.Slice(start, _pos - start).ToString();
            if (_pos < Html.Length)
            {
                _pos++;
            }

            return WebUtility.HtmlDecode(value);
        }

        var rawStart = _pos;
        while (_pos < Html.Length && !char.IsWhiteSpace(Html[_pos]) && Html[_pos] != '>' && Html[_pos] != '/')
        {
            _pos++;
        }

        return WebUtility.HtmlDecode(Html.Slice(rawStart, _pos - rawStart).ToString());
    }

    private void SkipWhitespace()
    {
        while (_pos < Html.Length && char.IsWhiteSpace(Html[_pos]))
        {
            _pos++;
        }
    }

    private void SkipToTagEnd()
    {
        while (_pos < Html.Length)
        {
            if (Html[_pos] == '>')
            {
                _pos++;
                return;
            }

            _pos++;
        }
    }

    private bool StartsWith(string value)
    {
        return _pos + value.Length <= Html.Length
               && Html.Slice(_pos, value.Length).Equals(value.AsSpan(), StringComparison.Ordinal);
    }

    private int IndexOf(string value, int start)
    {
        if (start < 0)
        {
            start = 0;
        }

        var span = Html.Slice(start);
        var index = span.IndexOf(value.AsSpan(), StringComparison.Ordinal);
        return index < 0 ? -1 : start + index;
    }

    private ReadOnlySpan<char> Html => _source.AsSpan();

    private static bool IsNameChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':' or '.';
    }

    private static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
