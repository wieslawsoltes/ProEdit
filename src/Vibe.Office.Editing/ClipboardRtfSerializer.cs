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
        return DocumentRtfParser.TryParse(rtf, out document);
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

}
