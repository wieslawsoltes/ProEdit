using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Vibe.Office.Documents;

public static class DocumentHtmlParser
{
    private static readonly Regex BreakRegex = new("(?i)<br\\s*/?>", RegexOptions.Compiled);
    private static readonly Regex ParagraphEndRegex = new("(?i)</p\\s*>", RegexOptions.Compiled);
    private static readonly Regex DivEndRegex = new("(?i)</div\\s*>", RegexOptions.Compiled);
    private static readonly Regex ListItemEndRegex = new("(?i)</li\\s*>", RegexOptions.Compiled);
    private static readonly Regex TableRowEndRegex = new("(?i)</tr\\s*>", RegexOptions.Compiled);
    private static readonly Regex TableCellEndRegex = new("(?i)</t[dh]\\s*>", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static bool TryParse(string html, out Document document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var fragment = ExtractHtmlFragment(html);
        var plainText = StripHtml(fragment);
        document = DocumentPlainTextParser.FromPlainText(plainText.AsSpan());
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

        var startTag = "<!--StartFragment-->";
        var endTag = "<!--EndFragment-->";
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

    private static string StripHtml(string html)
    {
        var text = BreakRegex.Replace(html, "\n");
        text = ParagraphEndRegex.Replace(text, "\n");
        text = DivEndRegex.Replace(text, "\n");
        text = ListItemEndRegex.Replace(text, "\n");
        text = TableRowEndRegex.Replace(text, "\n");
        text = TableCellEndRegex.Replace(text, "\t");
        text = TagRegex.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return text;
    }
}
