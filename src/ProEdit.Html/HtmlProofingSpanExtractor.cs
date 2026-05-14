using ProEdit.Editing;
using ProEdit.Html.Ast;

namespace ProEdit.Html;

public static class HtmlProofingSpanExtractor
{
    public static IReadOnlyList<ProofingSourceSpan> ExtractTextSpans(string html, HtmlOptions? options = null)
    {
        var parser = new HtmlAstParser(options);
        var document = parser.Parse(html.AsSpan());
        return ExtractTextSpans(document);
    }

    public static IReadOnlyList<ProofingSourceSpan> ExtractTextSpans(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var spans = new List<ProofingSourceSpan>();
        Collect(document, spans);
        return spans;
    }

    private static void Collect(HtmlNode node, List<ProofingSourceSpan> spans)
    {
        switch (node)
        {
            case HtmlTextNode textNode:
                AddSpan(textNode, spans);
                break;
            case HtmlDocument document:
                foreach (var child in document.Children)
                {
                    Collect(child, spans);
                }
                break;
            case HtmlElementNode element:
                foreach (var child in element.Children)
                {
                    Collect(child, spans);
                }
                break;
        }
    }

    private static void AddSpan(HtmlTextNode textNode, List<ProofingSourceSpan> spans)
    {
        var span = textNode.Span;
        if (span.Start < 0 || span.Length <= 0)
        {
            return;
        }

        if (textNode.Text.Length == 0)
        {
            return;
        }

        spans.Add(new ProofingSourceSpan(span.Start, span.Length, textNode.Text));
    }
}
