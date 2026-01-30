using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public static class ClipboardHtmlSerializer
{

    public static string ToHtml(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder(2048);
        builder.Append("<html><head><meta charset=\"utf-8\"></head><body>");

        var resolver = new DocumentStyleResolver(document);
        var listStack = new List<ListState>();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    WriteParagraph(builder, document, resolver, paragraph, listStack);
                    break;
                case TableBlock table:
                    CloseLists(builder, listStack);
                    WriteTable(builder, document, resolver, table);
                    break;
            }
        }

        CloseLists(builder, listStack);
        builder.Append("</body></html>");
        return builder.ToString();
    }

    public static string ToClipboardHtml(Document document)
    {
        return ToClipboardHtml(ToHtml(document));
    }

    public static string ToClipboardHtml(string html)
    {
        var fragmentStart = "<!--StartFragment-->";
        var fragmentEnd = "<!--EndFragment-->";

        var insertIndex = html.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
        if (insertIndex >= 0)
        {
            insertIndex += "<body>".Length;
        }
        else
        {
            insertIndex = 0;
        }

        var fragmentBuilder = new StringBuilder(html.Length + fragmentStart.Length + fragmentEnd.Length);
        fragmentBuilder.Append(html.AsSpan(0, insertIndex));
        fragmentBuilder.Append(fragmentStart);
        fragmentBuilder.Append(html.AsSpan(insertIndex));
        fragmentBuilder.Append(fragmentEnd);

        var htmlWithFragment = fragmentBuilder.ToString();
        return BuildClipboardHtmlHeader(htmlWithFragment, fragmentStart, fragmentEnd);
    }

    public static bool TryParse(string html, out Document document)
    {
        return DocumentHtmlParser.TryParse(html, out document);
    }

    private static void WriteParagraph(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        ParagraphBlock paragraph,
        List<ListState> listStack)
    {
        var listInfo = paragraph.ListInfo;
        if (listInfo is null || listInfo.Kind == ListKind.None)
        {
            CloseLists(builder, listStack);
            WriteParagraphContainer(builder, document, resolver, paragraph, tag: "p");
            return;
        }

        EnsureListState(builder, listStack, listInfo);
        WriteParagraphContainer(builder, document, resolver, paragraph, tag: "li");
    }

    private static void WriteParagraphContainer(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        ParagraphBlock paragraph,
        string tag)
    {
        var paragraphProps = resolver.ResolveParagraphProperties(paragraph);
        var paragraphStyle = BuildParagraphStyle(paragraphProps);

        builder.Append('<').Append(tag);
        if (!string.IsNullOrEmpty(paragraphStyle))
        {
            builder.Append(" style=\"").Append(paragraphStyle).Append('"');
        }
        builder.Append('>');

        WriteParagraphContent(builder, document, resolver, paragraph);

        builder.Append("</").Append(tag).Append('>');
    }

    private static void WriteParagraphContent(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var style = resolver.ResolveParagraphTextStyle(paragraph, document.DefaultTextStyle);
            WriteStyledText(builder, style, paragraph.Text ?? string.Empty, null);
            return;
        }

        var paragraphStyle = resolver.ResolveParagraphTextStyle(paragraph, document.DefaultTextStyle);
        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                {
                    var runStyle = resolver.ResolveRunStyle(paragraph, run, paragraphStyle);
                    WriteStyledText(builder, runStyle, run.Text.GetText(), run.Hyperlink);
                    break;
                }
                case RubyInline ruby:
                    WriteRuby(builder, document, resolver, paragraphStyle, ruby);
                    break;
                case ImageInline image:
                    WriteImage(builder, image);
                    break;
                case PageNumberInline:
                    WriteStyledText(builder, paragraphStyle, "{PAGE}", inline.Hyperlink);
                    break;
                case TotalPagesInline:
                    WriteStyledText(builder, paragraphStyle, "{NUMPAGES}", inline.Hyperlink);
                    break;
                case FootnoteReferenceInline footnote:
                    WriteStyledText(builder, paragraphStyle, $"[{footnote.Id}]", inline.Hyperlink);
                    break;
                case EndnoteReferenceInline endnote:
                    WriteStyledText(builder, paragraphStyle, $"[{endnote.Id}]", inline.Hyperlink);
                    break;
                case CommentReferenceInline comment:
                    WriteStyledText(builder, paragraphStyle, $"[{comment.Id}]", inline.Hyperlink);
                    break;
                case EquationInline:
                    WriteStyledText(builder, paragraphStyle, "[Equation]", inline.Hyperlink);
                    break;
                case ShapeInline:
                    WriteStyledText(builder, paragraphStyle, "[Shape]", inline.Hyperlink);
                    break;
                case ChartInline:
                    WriteStyledText(builder, paragraphStyle, "[Chart]", inline.Hyperlink);
                    break;
                case TableInline:
                    WriteStyledText(builder, paragraphStyle, "[Table]", inline.Hyperlink);
                    break;
            }
        }

        if (paragraph.FloatingObjects.Count > 0)
        {
            foreach (var floating in paragraph.FloatingObjects)
            {
                switch (floating.Content)
                {
                    case ImageInline image:
                        WriteImage(builder, image);
                        break;
                    case ShapeInline:
                        WriteStyledText(builder, paragraphStyle, "[Shape]", null);
                        break;
                    case ChartInline:
                        WriteStyledText(builder, paragraphStyle, "[Chart]", null);
                        break;
                }
            }
        }
    }

    private static void WriteRuby(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        TextStyle paragraphStyle,
        RubyInline ruby)
    {
        builder.Append("<ruby>");

        var baseStyle = resolver.ResolveRunStyle(ruby.BaseStyleId, ruby.BaseStyle, paragraphStyle);
        WriteStyledText(builder, baseStyle, ruby.BaseText, ruby.Hyperlink);

        builder.Append("<rt>");
        var rubyStyle = resolver.ResolveRunStyle(ruby.RubyStyleId, ruby.RubyStyle, paragraphStyle);
        WriteStyledText(builder, rubyStyle, ruby.RubyText, null);
        builder.Append("</rt></ruby>");
    }

    private static void WriteImage(StringBuilder builder, ImageInline image)
    {
        var data = Convert.ToBase64String(image.Data);
        var width = Math.Max(0, image.Width);
        var height = Math.Max(0, image.Height);

        builder.Append("<img src=\"data:");
        builder.Append(EscapeHtmlAttribute(image.ContentType));
        builder.Append(";base64,");
        builder.Append(data);
        builder.Append('"');

        if (width > 0f)
        {
            builder.Append(" width=\"").Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append('"');
        }

        if (height > 0f)
        {
            builder.Append(" height=\"").Append(height.ToString("0.##", CultureInfo.InvariantCulture)).Append('"');
        }

        builder.Append(" />");
    }

    private static void WriteStyledText(StringBuilder builder, TextStyle style, string text, HyperlinkInfo? hyperlink)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var runStyle = BuildRunStyle(style);
        var hasLink = hyperlink is { IsEmpty: false };

        if (hasLink)
        {
            var href = BuildHyperlinkHref(hyperlink!);
            builder.Append("<a href=\"").Append(EscapeHtmlAttribute(href)).Append("\">");
        }

        if (!string.IsNullOrEmpty(runStyle))
        {
            builder.Append("<span style=\"").Append(runStyle).Append("\">");
        }

        AppendHtmlText(builder, text);

        if (!string.IsNullOrEmpty(runStyle))
        {
            builder.Append("</span>");
        }

        if (hasLink)
        {
            builder.Append("</a>");
        }
    }

    private static void WriteTable(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        TableBlock table)
    {
        var tableStyle = BuildTableStyle(table.Properties);
        builder.Append("<table");
        if (!string.IsNullOrEmpty(tableStyle))
        {
            builder.Append(" style=\"").Append(tableStyle).Append('"');
        }
        builder.Append('>');

        foreach (var row in table.Rows)
        {
            builder.Append("<tr>");
            foreach (var cell in row.Cells)
            {
                WriteTableCell(builder, document, resolver, table, cell);
            }
            builder.Append("</tr>");
        }

        builder.Append("</table>");
    }

    private static void WriteTableCell(
        StringBuilder builder,
        Document document,
        DocumentStyleResolver resolver,
        TableBlock table,
        TableCell cell)
    {
        var cellStyle = BuildTableCellStyle(table, cell);
        builder.Append("<td");
        if (cell.ColumnSpan > 1)
        {
            builder.Append(" colspan=\"").Append(cell.ColumnSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
        }
        if (!string.IsNullOrEmpty(cellStyle))
        {
            builder.Append(" style=\"").Append(cellStyle).Append('"');
        }
        builder.Append('>');

        if (cell.Paragraphs.Count == 0)
        {
            builder.Append("&nbsp;");
        }
        else
        {
            for (var i = 0; i < cell.Paragraphs.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("<br />");
                }

                WriteParagraphContainer(builder, document, resolver, cell.Paragraphs[i], "span");
            }
        }

        builder.Append("</td>");
    }

    private static void EnsureListState(StringBuilder builder, List<ListState> listStack, ListInfo info)
    {
        if (listStack.Count > 0)
        {
            var root = listStack[0];
            if (root.ListId != info.ListId || root.Kind != info.Kind)
            {
                CloseLists(builder, listStack);
            }
        }

        var targetLevel = info.Level;
        while (listStack.Count > targetLevel + 1)
        {
            CloseLastList(builder, listStack);
        }

        while (listStack.Count <= targetLevel)
        {
            var tag = info.Kind == ListKind.Numbered ? "ol" : "ul";
            builder.Append('<').Append(tag).Append('>');
            listStack.Add(new ListState(info.ListId, info.Kind));
        }
    }

    private static void CloseLists(StringBuilder builder, List<ListState> listStack)
    {
        while (listStack.Count > 0)
        {
            CloseLastList(builder, listStack);
        }
    }

    private static void CloseLastList(StringBuilder builder, List<ListState> listStack)
    {
        var tag = listStack[^1].Kind == ListKind.Numbered ? "ol" : "ul";
        builder.Append("</").Append(tag).Append('>');
        listStack.RemoveAt(listStack.Count - 1);
    }

    private static string BuildParagraphStyle(ParagraphProperties properties)
    {
        var builder = new StringBuilder();
        AppendParagraphSpacing(builder, properties);
        AppendParagraphIndent(builder, properties);
        AppendParagraphAlignment(builder, properties);
        builder.Append("white-space:pre-wrap;");
        return builder.ToString();
    }

    private static void AppendParagraphSpacing(StringBuilder builder, ParagraphProperties properties)
    {
        if (properties.SpacingBefore.HasValue)
        {
            AppendCss(builder, "margin-top", ToCssPx(properties.SpacingBefore.Value));
        }

        if (properties.SpacingAfter.HasValue)
        {
            AppendCss(builder, "margin-bottom", ToCssPx(properties.SpacingAfter.Value));
        }
    }

    private static void AppendParagraphIndent(StringBuilder builder, ParagraphProperties properties)
    {
        if (properties.IndentLeft.HasValue)
        {
            AppendCss(builder, "margin-left", ToCssPx(properties.IndentLeft.Value));
        }

        if (properties.IndentRight.HasValue)
        {
            AppendCss(builder, "margin-right", ToCssPx(properties.IndentRight.Value));
        }

        if (properties.FirstLineIndent.HasValue)
        {
            AppendCss(builder, "text-indent", ToCssPx(properties.FirstLineIndent.Value));
        }
    }

    private static void AppendParagraphAlignment(StringBuilder builder, ParagraphProperties properties)
    {
        if (!properties.Alignment.HasValue)
        {
            return;
        }

        var value = properties.Alignment.Value switch
        {
            ParagraphAlignment.Center => "center",
            ParagraphAlignment.Right => "right",
            ParagraphAlignment.Justify => "justify",
            _ => "left"
        };
        AppendCss(builder, "text-align", value);
    }

    private static string BuildRunStyle(TextStyle style)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            AppendCss(builder, "font-family", $"\"{EscapeHtmlAttribute(style.FontFamily)}\"");
        }

        if (style.FontSize > 0f)
        {
            AppendCss(builder, "font-size", ToCssPx(style.FontSize));
        }

        if (style.FontWeight != DocFontWeight.Normal)
        {
            AppendCss(builder, "font-weight", ((int)style.FontWeight).ToString(CultureInfo.InvariantCulture));
        }

        if (style.FontStyle == DocFontStyle.Italic)
        {
            AppendCss(builder, "font-style", "italic");
        }

        var decorations = new List<string>();
        if (style.Underline)
        {
            decorations.Add("underline");
        }

        if (style.Strikethrough)
        {
            decorations.Add("line-through");
        }

        if (decorations.Count > 0)
        {
            AppendCss(builder, "text-decoration", string.Join(' ', decorations));
        }

        if (style.Color.A > 0)
        {
            AppendCss(builder, "color", ToCssColor(style.Color));
        }

        if (style.HighlightColor.HasValue)
        {
            AppendCss(builder, "background-color", ToCssColor(style.HighlightColor.Value));
        }

        if (style.VerticalPosition == DocVerticalPosition.Superscript)
        {
            AppendCss(builder, "vertical-align", "super");
        }
        else if (style.VerticalPosition == DocVerticalPosition.Subscript)
        {
            AppendCss(builder, "vertical-align", "sub");
        }

        if (style.SmallCaps)
        {
            AppendCss(builder, "font-variant", "small-caps");
        }
        else if (style.Caps)
        {
            AppendCss(builder, "text-transform", "uppercase");
        }

        if (style.LetterSpacing != 0f)
        {
            AppendCss(builder, "letter-spacing", ToCssPx(style.LetterSpacing));
        }

        return builder.ToString();
    }

    private static string BuildTableStyle(TableProperties properties)
    {
        var builder = new StringBuilder();
        AppendCss(builder, "border-collapse", "collapse");

        var border = ResolveBorder(properties.Borders);
        if (border is not null)
        {
            AppendCss(builder, "border", border);
        }

        if (properties.Width.HasValue)
        {
            var width = properties.Width.Value;
            switch (properties.WidthUnit)
            {
                case TableWidthUnit.Pct:
                    AppendCss(builder, "width", $"{width.ToString("0.##", CultureInfo.InvariantCulture)}%");
                    break;
                case TableWidthUnit.Dxa:
                case TableWidthUnit.Auto:
                default:
                    AppendCss(builder, "width", ToCssPx(width));
                    break;
            }
        }

        if (properties.CellSpacing.HasValue)
        {
            AppendCss(builder, "border-spacing", ToCssPx(properties.CellSpacing.Value));
        }

        if (properties.ShadingColor.HasValue)
        {
            AppendCss(builder, "background-color", ToCssColor(properties.ShadingColor.Value));
        }

        if (properties.Alignment.HasValue)
        {
            switch (properties.Alignment.Value)
            {
                case TableAlignment.Center:
                    AppendCss(builder, "margin-left", "auto");
                    AppendCss(builder, "margin-right", "auto");
                    break;
                case TableAlignment.Right:
                    AppendCss(builder, "margin-left", "auto");
                    AppendCss(builder, "margin-right", "0");
                    break;
            }
        }

        return builder.ToString();
    }

    private static string BuildTableCellStyle(TableBlock table, TableCell cell)
    {
        var builder = new StringBuilder();
        var border = ResolveBorder(cell.Properties.Borders) ?? ResolveBorder(table.Properties.Borders);
        if (border is not null)
        {
            AppendCss(builder, "border", border);
        }

        if (cell.Properties.ShadingColor.HasValue)
        {
            AppendCss(builder, "background-color", ToCssColor(cell.Properties.ShadingColor.Value));
        }

        if (cell.Properties.Padding.HasValue)
        {
            var padding = cell.Properties.Padding.Value;
            var value = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2} {3}",
                ToCssPx(padding.Top),
                ToCssPx(padding.Right),
                ToCssPx(padding.Bottom),
                ToCssPx(padding.Left));
            AppendCss(builder, "padding", value);
        }

        if (cell.Properties.PreferredWidth.HasValue)
        {
            var width = cell.Properties.PreferredWidth.Value;
            var value = cell.Properties.PreferredWidthUnit == TableWidthUnit.Pct
                ? $"{width.ToString("0.##", CultureInfo.InvariantCulture)}%"
                : ToCssPx(width);
            AppendCss(builder, "width", value);
        }

        return builder.ToString();
    }

    private static string? ResolveBorder(TableCellBorders borders)
    {
        return ResolveBorder(borders.Top ?? borders.Bottom ?? borders.Left ?? borders.Right);
    }

    private static string? ResolveBorder(TableBorders borders)
    {
        return ResolveBorder(borders.Top ?? borders.Bottom ?? borders.Left ?? borders.Right ?? borders.InsideHorizontal ?? borders.InsideVertical);
    }

    private static string? ResolveBorder(BorderLine? border)
    {
        if (border is null || !border.IsVisible)
        {
            return null;
        }

        var style = border.Style switch
        {
            DocBorderStyle.Dashed => "dashed",
            DocBorderStyle.Dotted => "dotted",
            DocBorderStyle.Double => "double",
            DocBorderStyle.Triple => "double",
            DocBorderStyle.ThickThin => "double",
            DocBorderStyle.ThinThick => "double",
            DocBorderStyle.ThinThickThin => "double",
            DocBorderStyle.DotDash => "dashed",
            DocBorderStyle.DotDotDash => "dashed",
            _ => "solid"
        };
        if (border.Compound != DocCompoundLine.Single)
        {
            style = "double";
        }

        var thickness = Math.Max(1f, border.Thickness);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2}",
            ToCssPx(thickness),
            style,
            ToCssColor(border.Color));
    }

    private static string BuildHyperlinkHref(HyperlinkInfo hyperlink)
    {
        if (!string.IsNullOrWhiteSpace(hyperlink.Uri))
        {
            return hyperlink.Uri!;
        }

        if (!string.IsNullOrWhiteSpace(hyperlink.Anchor))
        {
            return "#" + hyperlink.Anchor;
        }

        return "#";
    }

    private static string ToCssColor(DocColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string ToCssPx(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture) + "px";
    }

    private static void AppendCss(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append(':').Append(value).Append(';');
    }

    private static void AppendHtmlText(StringBuilder builder, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '&':
                    builder.Append("&amp;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\n':
                    builder.Append("<br />");
                    break;
                case '\r':
                    break;
                case '\t':
                    builder.Append("&emsp;");
                    break;
                default:
                    if (ch <= 0x7f)
                    {
                        builder.Append(ch);
                    }
                    else
                    {
                        builder.Append("&#").Append(((int)ch).ToString(CultureInfo.InvariantCulture)).Append(';');
                    }
                    break;
            }
        }
    }

    private static string EscapeHtmlAttribute(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                default:
                    if (ch <= 0x7f)
                    {
                        builder.Append(ch);
                    }
                    else
                    {
                        builder.Append("&#").Append(((int)ch).ToString(CultureInfo.InvariantCulture)).Append(';');
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static string BuildClipboardHtmlHeader(string html, string fragmentStart, string fragmentEnd)
    {
        var startHtml = "00000000";
        var endHtml = "00000000";
        var startFragment = "00000000";
        var endFragment = "00000000";

        var header = new StringBuilder();
        header.Append("Version:0.9\r\n");
        header.Append("StartHTML:").Append(startHtml).Append("\r\n");
        header.Append("EndHTML:").Append(endHtml).Append("\r\n");
        header.Append("StartFragment:").Append(startFragment).Append("\r\n");
        header.Append("EndFragment:").Append(endFragment).Append("\r\n");

        var headerLength = header.Length;
        var startHtmlIndex = headerLength;
        var startFragmentIndex = html.IndexOf(fragmentStart, StringComparison.Ordinal);
        var endFragmentIndex = html.IndexOf(fragmentEnd, StringComparison.Ordinal);

        if (startFragmentIndex >= 0)
        {
            startFragmentIndex += startHtmlIndex + fragmentStart.Length;
        }
        else
        {
            startFragmentIndex = startHtmlIndex;
        }

        if (endFragmentIndex >= 0)
        {
            endFragmentIndex += startHtmlIndex;
        }
        else
        {
            endFragmentIndex = startHtmlIndex + html.Length;
        }

        var endHtmlIndex = startHtmlIndex + html.Length;

        var formattedHeader = new StringBuilder();
        formattedHeader.Append("Version:0.9\r\n");
        formattedHeader.Append("StartHTML:").Append(startHtmlIndex.ToString("D8", CultureInfo.InvariantCulture)).Append("\r\n");
        formattedHeader.Append("EndHTML:").Append(endHtmlIndex.ToString("D8", CultureInfo.InvariantCulture)).Append("\r\n");
        formattedHeader.Append("StartFragment:").Append(startFragmentIndex.ToString("D8", CultureInfo.InvariantCulture)).Append("\r\n");
        formattedHeader.Append("EndFragment:").Append(endFragmentIndex.ToString("D8", CultureInfo.InvariantCulture)).Append("\r\n");

        formattedHeader.Append(html);
        return formattedHeader.ToString();
    }

    private sealed record ListState(int? ListId, ListKind Kind);
}
