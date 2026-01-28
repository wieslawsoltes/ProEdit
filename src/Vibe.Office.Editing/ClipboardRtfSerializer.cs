using System.Globalization;
using System.Linq;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public static class ClipboardRtfSerializer
{
    public static string ToRtf(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolver = new DocumentStyleResolver(document);
        var fonts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var colors = new Dictionary<DocColor, int>();
        CollectResources(document, resolver, fonts, colors);

        var builder = new StringBuilder(2048);
        builder.Append("{\\rtf1\\ansi\\deff0");
        AppendFontTable(builder, fonts);
        AppendColorTable(builder, colors);
        builder.Append("\\viewkind4\\uc1 ");

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    WriteParagraph(builder, document, resolver, paragraph, fonts, colors);
                    break;
                case TableBlock table:
                    WriteTable(builder, table, fonts, colors);
                    break;
            }
        }

        builder.Append('}');
        return builder.ToString();
    }

    public static bool TryParse(string rtf, out Document document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(rtf))
        {
            return false;
        }

        var parser = new RtfParser(rtf);
        document = parser.Parse();
        return true;
    }

    private static void CollectResources(
        Document document,
        DocumentStyleResolver resolver,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        AddFont(fonts, document.DefaultTextStyle.FontFamily);
        AddColor(colors, document.DefaultTextStyle.Color);

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    CollectParagraphResources(document, resolver, paragraph, fonts, colors);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                CollectParagraphResources(document, resolver, paragraph, fonts, colors);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private static void CollectParagraphResources(
        Document document,
        DocumentStyleResolver resolver,
        ParagraphBlock paragraph,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        var paragraphStyle = resolver.ResolveParagraphTextStyle(paragraph, document.DefaultTextStyle);
        AddStyleResources(paragraphStyle, fonts, colors);

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                var runStyle = resolver.ResolveRunStyle(paragraph, run, paragraphStyle);
                AddStyleResources(runStyle, fonts, colors);
            }
        }
    }

    private static void AddStyleResources(TextStyle style, Dictionary<string, int> fonts, Dictionary<DocColor, int> colors)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            AddFont(fonts, style.FontFamily);
        }

        AddColor(colors, style.Color);
        if (style.HighlightColor.HasValue)
        {
            AddColor(colors, style.HighlightColor.Value);
        }
    }

    private static void AddFont(Dictionary<string, int> fonts, string font)
    {
        if (!fonts.ContainsKey(font))
        {
            fonts[font] = fonts.Count;
        }
    }

    private static void AddColor(Dictionary<DocColor, int> colors, DocColor color)
    {
        if (!colors.ContainsKey(color))
        {
            colors[color] = colors.Count + 1;
        }
    }

    private static void AppendFontTable(StringBuilder builder, Dictionary<string, int> fonts)
    {
        builder.Append("{\\fonttbl");
        foreach (var pair in fonts.OrderBy(static pair => pair.Value))
        {
            builder.Append("{\\f").Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(' ');
            builder.Append(EscapeRtfText(pair.Key));
            builder.Append(";}");
        }
        builder.Append('}');
    }

    private static void AppendColorTable(StringBuilder builder, Dictionary<DocColor, int> colors)
    {
        builder.Append("{\\colortbl;");
        foreach (var pair in colors.OrderBy(static pair => pair.Value))
        {
            var color = pair.Key;
            builder.Append("\\red").Append(color.R.ToString(CultureInfo.InvariantCulture));
            builder.Append("\\green").Append(color.G.ToString(CultureInfo.InvariantCulture));
            builder.Append("\\blue").Append(color.B.ToString(CultureInfo.InvariantCulture));
            builder.Append(';');
        }
        builder.Append('}');
    }

    private static void WriteParagraph(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        ParagraphBlock paragraph,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        var paragraphProps = resolver.ResolveParagraphProperties(paragraph);
        builder.Append("\\pard");
        AppendAlignment(builder, paragraphProps.Alignment);

        if (paragraph.Inlines.Count == 0)
        {
            var style = resolver.ResolveParagraphTextStyle(paragraph, document.DefaultTextStyle);
            AppendRun(builder, style, paragraph.Text ?? string.Empty, fonts, colors);
        }
        else
        {
            var paragraphStyle = resolver.ResolveParagraphTextStyle(paragraph, document.DefaultTextStyle);
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is RunInline run)
                {
                    var runStyle = resolver.ResolveRunStyle(paragraph, run, paragraphStyle);
                    AppendRun(builder, runStyle, run.Text.GetText(), fonts, colors);
                }
                else
                {
                    AppendRun(builder, paragraphStyle, DocumentConstants.ObjectReplacementChar.ToString(), fonts, colors);
                }
            }
        }

        builder.Append("\\par ");
    }

    private static void WriteTable(
        StringBuilder builder,
        TableBlock table,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        var firstRow = true;
        foreach (var row in table.Rows)
        {
            if (!firstRow)
            {
                builder.Append("\\par ");
            }

            firstRow = false;
            var firstCell = true;
            foreach (var cell in row.Cells)
            {
                if (!firstCell)
                {
                    builder.Append("\\tab ");
                }

                firstCell = false;
                var cellText = BuildCellText(cell);
                AppendRun(builder, null, cellText, fonts, colors);
            }
        }

        builder.Append("\\par ");
    }

    private static string BuildCellText(TableCell cell)
    {
        if (cell.Paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < cell.Paragraphs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(DocumentEditHelpers.GetParagraphText(cell.Paragraphs[i]));
        }

        return builder.ToString();
    }

    private static void AppendAlignment(StringBuilder builder, ParagraphAlignment? alignment)
    {
        if (!alignment.HasValue)
        {
            return;
        }

        var control = alignment.Value switch
        {
            ParagraphAlignment.Center => "\\qc",
            ParagraphAlignment.Right => "\\qr",
            ParagraphAlignment.Justify => "\\qj",
            _ => "\\ql"
        };

        builder.Append(control);
    }

    private static void AppendRun(
        StringBuilder builder,
        TextStyle? style,
        string text,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (style is not null)
        {
            AppendRunStyle(builder, style, fonts, colors);
        }

        AppendRtfText(builder, text);
    }

    private static void AppendRunStyle(
        StringBuilder builder,
        TextStyle style,
        Dictionary<string, int> fonts,
        Dictionary<DocColor, int> colors)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily) && fonts.TryGetValue(style.FontFamily, out var fontIndex))
        {
            builder.Append("\\f").Append(fontIndex.ToString(CultureInfo.InvariantCulture));
        }

        var sizeHalfPoints = DipToHalfPoints(style.FontSize);
        builder.Append("\\fs").Append(sizeHalfPoints.ToString(CultureInfo.InvariantCulture));
        builder.Append(style.FontWeight == DocFontWeight.Bold ? "\\b" : "\\b0");
        builder.Append(style.FontStyle == DocFontStyle.Italic ? "\\i" : "\\i0");
        builder.Append(style.Underline ? "\\ul" : "\\ul0");
        builder.Append(style.Strikethrough ? "\\strike" : "\\strike0");

        if (colors.TryGetValue(style.Color, out var colorIndex))
        {
            builder.Append("\\cf").Append(colorIndex.ToString(CultureInfo.InvariantCulture));
        }

        if (style.HighlightColor.HasValue && colors.TryGetValue(style.HighlightColor.Value, out var highlightIndex))
        {
            builder.Append("\\highlight").Append(highlightIndex.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append("\\highlight0");
        }

        if (style.VerticalPosition == DocVerticalPosition.Superscript)
        {
            builder.Append("\\super");
        }
        else if (style.VerticalPosition == DocVerticalPosition.Subscript)
        {
            builder.Append("\\sub");
        }
        else
        {
            builder.Append("\\nosupersub");
        }

        builder.Append(' ');
    }

    private static void AppendRtfText(StringBuilder builder, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                case '{':
                case '}':
                    builder.Append('\\').Append(ch);
                    break;
                case '\n':
                    builder.Append("\\line ");
                    break;
                case '\r':
                    break;
                default:
                    if (ch <= 0x7f)
                    {
                        builder.Append(ch);
                    }
                    else
                    {
                        var value = (short)ch;
                        builder.Append("\\u").Append(value.ToString(CultureInfo.InvariantCulture)).Append('?');
                    }
                    break;
            }
        }
    }

    private static string EscapeRtfText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                case '{':
                case '}':
                    builder.Append('\\').Append(ch);
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }
        return builder.ToString();
    }

    private static int DipToHalfPoints(float dip)
    {
        var points = dip * 72f / 96f;
        return (int)MathF.Round(points * 2f);
    }

    private sealed class RtfParser
    {
        private readonly string _rtf;
        private readonly Dictionary<int, string> _fonts;
        private readonly List<DocColor> _colors;
        private readonly Stack<StyleState> _styleStack = new();
        private readonly Stack<GroupState> _groupStack = new();
        private StyleState _currentStyle;
        private bool _skipText;
        private int _pos;
        private int _unicodeSkip = 1;
        private int _pendingUnicodeSkip;
        private ParagraphBlock _paragraph = new();
        private readonly List<ParagraphBlock> _paragraphs = new();
        private StringBuilder? _runText;
        private StyleState _runStyle;

        public RtfParser(string rtf)
        {
            _rtf = rtf;
            _fonts = ParseFontTable(rtf);
            _colors = ParseColorTable(rtf);
            _currentStyle = new StyleState();
            _runStyle = new StyleState();
            _paragraphs.Add(_paragraph);
        }

        public Document Parse()
        {
            _currentStyle = new StyleState();
            _styleStack.Push(_currentStyle.Clone());

            while (_pos < _rtf.Length)
            {
                if (_pendingUnicodeSkip > 0 && SkipUnicodeFallback())
                {
                    continue;
                }

                var ch = _rtf[_pos];
                if (ch == '{')
                {
                    PushGroup();
                    _pos++;
                    continue;
                }

                if (ch == '}')
                {
                    PopGroup();
                    _pos++;
                    continue;
                }

                if (ch == '\\')
                {
                    _pos++;
                    ParseControl();
                    continue;
                }

                if (!_skipText)
                {
                    AppendText(ch.ToString());
                }

                _pos++;
            }

            FlushRun();

            var document = new Document();
            document.Blocks.Clear();
            document.Sections.Clear();
            document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer, document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));
            DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
            foreach (var paragraph in _paragraphs)
            {
                document.Blocks.Add(paragraph);
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new ParagraphBlock());
            }

            return document;
        }

        private void PushGroup()
        {
            _groupStack.Push(new GroupState(_skipText));
            _styleStack.Push(_currentStyle.Clone());
        }

        private void PopGroup()
        {
            FlushRun();
            if (_styleStack.Count > 0)
            {
                _currentStyle = _styleStack.Pop();
            }

            if (_groupStack.Count > 0)
            {
                _skipText = _groupStack.Pop().SkipText;
            }
        }

        private void ParseControl()
        {
            if (_pos >= _rtf.Length)
            {
                return;
            }

            var ch = _rtf[_pos];
            if (ch == '\\' || ch == '{' || ch == '}')
            {
                if (!_skipText)
                {
                    AppendText(ch.ToString());
                }
                _pos++;
                return;
            }

            if (ch == '\'')
            {
                if (_pos + 2 < _rtf.Length)
                {
                    var hex = _rtf.Substring(_pos + 1, 2);
                    if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && !_skipText)
                    {
                        AppendText(((char)value).ToString());
                    }
                }
                _pos += 3;
                return;
            }

            if (!char.IsLetter(ch))
            {
                _pos++;
                if (!_skipText)
                {
                    switch (ch)
                    {
                        case '~':
                            AppendText(" ");
                            break;
                        case '_':
                        case '-':
                            AppendText("-");
                            break;
                    }
                }

                return;
            }

            var word = ReadControlWord(out var param, out var hasParam);
            if (_skipText)
            {
                if (word == "fonttbl" || word == "colortbl" || word == "stylesheet")
                {
                    _skipText = true;
                }

                return;
            }

            switch (word)
            {
                case "par":
                    FlushRun();
                    _paragraph = new ParagraphBlock();
                    _paragraphs.Add(_paragraph);
                    break;
                case "line":
                    AppendText("\n");
                    break;
                case "tab":
                    AppendText("\t");
                    break;
                case "b":
                    SetStyle(ref _currentStyle.Bold, !hasParam || param != 0);
                    break;
                case "i":
                    SetStyle(ref _currentStyle.Italic, !hasParam || param != 0);
                    break;
                case "ul":
                    SetStyle(ref _currentStyle.Underline, !hasParam || param != 0);
                    break;
                case "ulnone":
                    SetStyle(ref _currentStyle.Underline, false);
                    break;
                case "strike":
                    SetStyle(ref _currentStyle.Strikethrough, !hasParam || param != 0);
                    break;
                case "fs":
                    if (hasParam)
                    {
                        SetFontSize(param);
                    }
                    break;
                case "f":
                    if (hasParam)
                    {
                        SetFont(param);
                    }
                    break;
                case "cf":
                    if (hasParam)
                    {
                        SetColor(param);
                    }
                    break;
                case "highlight":
                    if (hasParam)
                    {
                        SetHighlight(param);
                    }
                    break;
                case "super":
                    SetVerticalPosition(DocVerticalPosition.Superscript);
                    break;
                case "sub":
                    SetVerticalPosition(DocVerticalPosition.Subscript);
                    break;
                case "nosupersub":
                    SetVerticalPosition(DocVerticalPosition.Normal);
                    break;
                case "uc":
                    if (hasParam)
                    {
                        _unicodeSkip = Math.Max(0, param);
                    }
                    break;
                case "u":
                    if (hasParam)
                    {
                        var codePoint = param < 0 ? param + 65536 : param;
                        AppendText(char.ConvertFromUtf32(codePoint));
                        _pendingUnicodeSkip = _unicodeSkip;
                    }
                    break;
                case "fonttbl":
                case "colortbl":
                case "stylesheet":
                    _skipText = true;
                    break;
            }
        }

        private string ReadControlWord(out int param, out bool hasParam)
        {
            param = 0;
            hasParam = false;

            var start = _pos;
            while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
            {
                _pos++;
            }

            var word = _rtf.Substring(start, _pos - start);
            if (_pos < _rtf.Length && (_rtf[_pos] == '-' || char.IsDigit(_rtf[_pos])))
            {
                var sign = 1;
                if (_rtf[_pos] == '-')
                {
                    sign = -1;
                    _pos++;
                }

                var valueStart = _pos;
                while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                {
                    _pos++;
                }

                if (int.TryParse(_rtf.Substring(valueStart, _pos - valueStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    param = value * sign;
                    hasParam = true;
                }
            }

            if (_pos < _rtf.Length && _rtf[_pos] == ' ')
            {
                _pos++;
            }

            return word;
        }

        private void SetStyle(ref bool target, bool value)
        {
            if (target != value)
            {
                FlushRun();
                target = value;
            }
        }

        private void SetFontSize(int halfPoints)
        {
            var dip = halfPoints / 2f * 96f / 72f;
            if (!_currentStyle.FontSize.HasValue || Math.Abs(_currentStyle.FontSize.Value - dip) > 0.01f)
            {
                FlushRun();
                _currentStyle.FontSize = dip;
            }
        }

        private void SetFont(int index)
        {
            if (_currentStyle.FontIndex != index)
            {
                FlushRun();
                _currentStyle.FontIndex = index;
            }
        }

        private void SetColor(int index)
        {
            var color = index > 0 && index <= _colors.Count ? _colors[index - 1] : (DocColor?)null;
            if (_currentStyle.Color != color)
            {
                FlushRun();
                _currentStyle.Color = color;
            }
        }

        private void SetHighlight(int index)
        {
            var color = index > 0 && index <= _colors.Count ? _colors[index - 1] : (DocColor?)null;
            if (_currentStyle.Highlight != color)
            {
                FlushRun();
                _currentStyle.Highlight = color;
            }
        }

        private void SetVerticalPosition(DocVerticalPosition position)
        {
            if (_currentStyle.VerticalPosition != position)
            {
                FlushRun();
                _currentStyle.VerticalPosition = position;
            }
        }

        private void AppendText(string text)
        {
            if (_runText is null)
            {
                _runText = new StringBuilder();
                _runStyle = _currentStyle.Clone();
            }
            _runText.Append(text);
        }

        private void FlushRun()
        {
            if (_runText is null || _runText.Length == 0)
            {
                _runText = null;
                return;
            }

            var style = new TextStyleProperties();
            if (_runStyle.FontIndex >= 0 && _fonts.TryGetValue(_runStyle.FontIndex, out var font))
            {
                style.FontFamily = font;
            }

            if (_runStyle.FontSize.HasValue)
            {
                style.FontSize = _runStyle.FontSize;
            }

            if (_runStyle.Bold)
            {
                style.FontWeight = DocFontWeight.Bold;
            }

            if (_runStyle.Italic)
            {
                style.FontStyle = DocFontStyle.Italic;
            }

            if (_runStyle.Underline)
            {
                style.Underline = true;
            }

            if (_runStyle.Strikethrough)
            {
                style.Strikethrough = true;
            }

            if (_runStyle.Color.HasValue)
            {
                style.Color = _runStyle.Color;
            }

            if (_runStyle.Highlight.HasValue)
            {
                style.HighlightColor = _runStyle.Highlight;
            }

            if (_runStyle.VerticalPosition != DocVerticalPosition.Normal)
            {
                style.VerticalPosition = _runStyle.VerticalPosition;
            }

            _paragraph.Inlines.Add(new RunInline(_runText.ToString(), style));
            _runText = null;
        }

        private bool SkipUnicodeFallback()
        {
            if (_pendingUnicodeSkip <= 0)
            {
                return false;
            }

            var ch = _rtf[_pos];
            if (ch == '\\' || ch == '{' || ch == '}')
            {
                _pendingUnicodeSkip = 0;
                return false;
            }

            _pos++;
            _pendingUnicodeSkip--;
            return true;
        }

        private static Dictionary<int, string> ParseFontTable(string rtf)
        {
            var fonts = new Dictionary<int, string>();
            var index = rtf.IndexOf("{\\fonttbl", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return fonts;
            }

            var group = ExtractGroup(rtf, index);
            if (group is null)
            {
                return fonts;
            }

            var pos = 0;
            while (pos < group.Length)
            {
                if (group[pos] == '\\' && pos + 1 < group.Length && group[pos + 1] == 'f')
                {
                    pos += 2;
                    var start = pos;
                    while (pos < group.Length && char.IsDigit(group[pos]))
                    {
                        pos++;
                    }

                    if (pos == start)
                    {
                        pos++;
                        continue;
                    }

                    if (!int.TryParse(group.Substring(start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fontIndex))
                    {
                        continue;
                    }

                    var nameBuilder = new StringBuilder();
                    while (pos < group.Length && group[pos] != ';')
                    {
                        if (group[pos] == '\\')
                        {
                            pos++;
                            while (pos < group.Length && char.IsLetter(group[pos]))
                            {
                                pos++;
                            }

                            while (pos < group.Length && (group[pos] == '-' || char.IsDigit(group[pos])))
                            {
                                pos++;
                            }
                        }
                        else if (group[pos] != '{' && group[pos] != '}')
                        {
                            nameBuilder.Append(group[pos]);
                            pos++;
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if (pos < group.Length && group[pos] == ';')
                    {
                        pos++;
                    }

                    var name = nameBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !fonts.ContainsKey(fontIndex))
                    {
                        fonts[fontIndex] = name;
                    }

                    continue;
                }

                pos++;
            }

            return fonts;
        }

        private static List<DocColor> ParseColorTable(string rtf)
        {
            var colors = new List<DocColor>();
            var index = rtf.IndexOf("{\\colortbl", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return colors;
            }

            var group = ExtractGroup(rtf, index);
            if (group is null)
            {
                return colors;
            }

            var red = 0;
            var green = 0;
            var blue = 0;
            var pos = 0;
            while (pos < group.Length)
            {
                if (group[pos] == '\\')
                {
                    pos++;
                    var start = pos;
                    while (pos < group.Length && char.IsLetter(group[pos]))
                    {
                        pos++;
                    }
                    var word = group.Substring(start, pos - start);

                    var sign = 1;
                    if (pos < group.Length && group[pos] == '-')
                    {
                        sign = -1;
                        pos++;
                    }

                    var numberStart = pos;
                    while (pos < group.Length && char.IsDigit(group[pos]))
                    {
                        pos++;
                    }

                    if (int.TryParse(group.Substring(numberStart, pos - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        value *= sign;
                        switch (word)
                        {
                            case "red":
                                red = value;
                                break;
                            case "green":
                                green = value;
                                break;
                            case "blue":
                                blue = value;
                                break;
                        }
                    }
                }
                else if (group[pos] == ';')
                {
                    colors.Add(new DocColor(ClampColor(red), ClampColor(green), ClampColor(blue)));
                    red = 0;
                    green = 0;
                    blue = 0;
                    pos++;
                }
                else
                {
                    pos++;
                }
            }

            return colors;
        }

        private static string? ExtractGroup(string text, int startIndex)
        {
            var braceIndex = text.IndexOf('{', startIndex);
            if (braceIndex < 0)
            {
                return null;
            }

            var depth = 0;
            for (var i = braceIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(braceIndex, i - braceIndex + 1);
                    }
                }
            }

            return null;
        }

        private static byte ClampColor(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return (byte)value;
        }

        private sealed record GroupState(bool SkipText);
    }

    private sealed class StyleState
    {
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
        public int FontIndex = -1;
        public float? FontSize;
        public DocColor? Color;
        public DocColor? Highlight;
        public DocVerticalPosition VerticalPosition = DocVerticalPosition.Normal;

        public StyleState Clone()
        {
            return new StyleState
            {
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                Strikethrough = Strikethrough,
                FontIndex = FontIndex,
                FontSize = FontSize,
                Color = Color,
                Highlight = Highlight,
                VerticalPosition = VerticalPosition
            };
        }
    }
}
