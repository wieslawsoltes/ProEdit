using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorFormattingStateAdapter : IFormattingState
{
    private readonly IEditorSession _session;
    private readonly DocumentStyleResolver _resolver;
    private readonly TextStyle _defaultStyle;

    public EditorFormattingStateAdapter(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _resolver = new DocumentStyleResolver(_session.Document);
        _defaultStyle = _session.Document.DefaultTextStyle.Clone();
    }

    public EditorFormattingSnapshot GetSnapshot()
    {
        var selection = _session.Selection.Normalize();
        var fontFamily = new EditorValueAccumulator<string>();
        var fontSize = new EditorValueAccumulator<float>();
        var bold = new EditorValueAccumulator<bool>();
        var italic = new EditorValueAccumulator<bool>();
        var underline = new EditorValueAccumulator<bool>();
        var underlineStyle = new EditorValueAccumulator<DocUnderlineStyle>();
        var strikethrough = new EditorValueAccumulator<bool>();
        var fontColor = new EditorValueAccumulator<DocColor>();
        var highlightColor = new NullableEditorValueAccumulator<DocColor>();
        var underlineColor = new NullableEditorValueAccumulator<DocColor>();
        var smallCaps = new EditorValueAccumulator<bool>();
        var verticalPosition = new EditorValueAccumulator<DocVerticalPosition>();
        var textOutline = new EditorValueAccumulator<bool>();
        var textShadow = new EditorValueAccumulator<bool>();
        var textEmboss = new EditorValueAccumulator<bool>();
        var textImprint = new EditorValueAccumulator<bool>();

        if (_session.Document.ParagraphCount == 0)
        {
            return new EditorFormattingSnapshot(
                fontFamily.Build(),
                fontSize.Build(),
                bold.Build(),
                italic.Build(),
                underline.Build(),
                underlineStyle.Build(),
                strikethrough.Build(),
                fontColor.Build(),
                highlightColor.Build(),
                underlineColor.Build(),
                smallCaps.Build(),
                verticalPosition.Build(),
                textOutline.Build(),
                textShadow.Build(),
                textEmboss.Build(),
                textImprint.Build());
        }

        if (selection.IsEmpty)
        {
            var paragraphIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
            var paragraph = _session.Document.GetParagraph(paragraphIndex);
            var style = ResolveStyleAtCaret(paragraph, selection.Start.Offset);
            Accumulate(
                style,
                ref fontFamily,
                ref fontSize,
                ref bold,
                ref italic,
                ref underline,
                ref underlineStyle,
                ref strikethrough,
                ref fontColor,
                ref highlightColor,
                ref underlineColor,
                ref smallCaps,
                ref verticalPosition,
                ref textOutline,
                ref textShadow,
                ref textEmboss,
                ref textImprint);
        }
        else
        {
            var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
            var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
            if (startIndex > endIndex)
            {
                (startIndex, endIndex) = (endIndex, startIndex);
            }

            for (var i = startIndex; i <= endIndex; i++)
            {
                var paragraph = _session.Document.GetParagraph(i);
                var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
                var startOffset = i == startIndex ? selection.Start.Offset : 0;
                var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;
                AddStylesInRange(
                    paragraph,
                    Math.Clamp(startOffset, 0, paragraphLength),
                    Math.Clamp(endOffset, 0, paragraphLength),
                    ref fontFamily,
                    ref fontSize,
                    ref bold,
                    ref italic,
                    ref underline,
                    ref underlineStyle,
                    ref strikethrough,
                    ref fontColor,
                    ref highlightColor,
                    ref underlineColor,
                    ref smallCaps,
                    ref verticalPosition,
                    ref textOutline,
                    ref textShadow,
                    ref textEmboss,
                    ref textImprint);
            }
        }

        return new EditorFormattingSnapshot(
            fontFamily.Build(),
            fontSize.Build(),
            bold.Build(),
            italic.Build(),
            underline.Build(),
            underlineStyle.Build(),
            strikethrough.Build(),
            fontColor.Build(),
            highlightColor.Build(),
            underlineColor.Build(),
            smallCaps.Build(),
            verticalPosition.Build(),
            textOutline.Build(),
            textShadow.Build(),
            textEmboss.Build(),
            textImprint.Build());
    }

    private void AddStylesInRange(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        ref EditorValueAccumulator<string> fontFamily,
        ref EditorValueAccumulator<float> fontSize,
        ref EditorValueAccumulator<bool> bold,
        ref EditorValueAccumulator<bool> italic,
        ref EditorValueAccumulator<bool> underline,
        ref EditorValueAccumulator<DocUnderlineStyle> underlineStyle,
        ref EditorValueAccumulator<bool> strikethrough,
        ref EditorValueAccumulator<DocColor> fontColor,
        ref NullableEditorValueAccumulator<DocColor> highlightColor,
        ref NullableEditorValueAccumulator<DocColor> underlineColor,
        ref EditorValueAccumulator<bool> smallCaps,
        ref EditorValueAccumulator<DocVerticalPosition> verticalPosition,
        ref EditorValueAccumulator<bool> textOutline,
        ref EditorValueAccumulator<bool> textShadow,
        ref EditorValueAccumulator<bool> textEmboss,
        ref EditorValueAccumulator<bool> textImprint)
    {
        if (startOffset >= endOffset)
        {
            return;
        }

        var paragraphStyle = _resolver.ResolveParagraphTextStyle(paragraph, _defaultStyle);
        var position = 0;
        var foundRun = false;

        if (paragraph.Inlines.Count == 0)
        {
            Accumulate(
                paragraphStyle,
                ref fontFamily,
                ref fontSize,
                ref bold,
                ref italic,
                ref underline,
                ref underlineStyle,
                ref strikethrough,
                ref fontColor,
                ref highlightColor,
                ref underlineColor,
                ref smallCaps,
                ref verticalPosition,
                ref textOutline,
                ref textShadow,
                ref textEmboss,
                ref textImprint);
            return;
        }

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                continue;
            }

            if (inline is RunInline run)
            {
                var style = _resolver.ResolveRunStyle(paragraph, run, paragraphStyle);
                Accumulate(
                    style,
                    ref fontFamily,
                    ref fontSize,
                    ref bold,
                    ref italic,
                    ref underline,
                    ref underlineStyle,
                    ref strikethrough,
                    ref fontColor,
                    ref highlightColor,
                    ref underlineColor,
                    ref smallCaps,
                    ref verticalPosition,
                    ref textOutline,
                    ref textShadow,
                    ref textEmboss,
                    ref textImprint);
                foundRun = true;
            }
        }

        if (!foundRun)
        {
            Accumulate(
                paragraphStyle,
                ref fontFamily,
                ref fontSize,
                ref bold,
                ref italic,
                ref underline,
                ref underlineStyle,
                ref strikethrough,
                ref fontColor,
                ref highlightColor,
                ref underlineColor,
                ref smallCaps,
                ref verticalPosition,
                ref textOutline,
                ref textShadow,
                ref textEmboss,
                ref textImprint);
        }
    }

    private TextStyle ResolveStyleAtCaret(ParagraphBlock paragraph, int offset)
    {
        var paragraphStyle = _resolver.ResolveParagraphTextStyle(paragraph, _defaultStyle);
        if (paragraph.Inlines.Count == 0)
        {
            return paragraphStyle;
        }

        var position = 0;
        RunInline? lastRun = null;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            if (inline is RunInline run)
            {
                if (offset >= position && offset < position + length)
                {
                    return _resolver.ResolveRunStyle(paragraph, run, paragraphStyle);
                }

                lastRun = run;
            }

            position += length;
        }

        if (lastRun is not null)
        {
            return _resolver.ResolveRunStyle(paragraph, lastRun, paragraphStyle);
        }

        return paragraphStyle;
    }

    private static void Accumulate(
        TextStyle style,
        ref EditorValueAccumulator<string> fontFamily,
        ref EditorValueAccumulator<float> fontSize,
        ref EditorValueAccumulator<bool> bold,
        ref EditorValueAccumulator<bool> italic,
        ref EditorValueAccumulator<bool> underline,
        ref EditorValueAccumulator<DocUnderlineStyle> underlineStyle,
        ref EditorValueAccumulator<bool> strikethrough,
        ref EditorValueAccumulator<DocColor> fontColor,
        ref NullableEditorValueAccumulator<DocColor> highlightColor,
        ref NullableEditorValueAccumulator<DocColor> underlineColor,
        ref EditorValueAccumulator<bool> smallCaps,
        ref EditorValueAccumulator<DocVerticalPosition> verticalPosition,
        ref EditorValueAccumulator<bool> textOutline,
        ref EditorValueAccumulator<bool> textShadow,
        ref EditorValueAccumulator<bool> textEmboss,
        ref EditorValueAccumulator<bool> textImprint)
    {
        fontFamily.Add(ResolveFontFamily(style));
        fontSize.Add(style.FontSize);
        bold.Add(style.FontWeight == DocFontWeight.Bold);
        italic.Add(style.FontStyle == DocFontStyle.Italic);
        underline.Add(style.Underline);
        underlineStyle.Add(style.UnderlineStyle);
        strikethrough.Add(style.Strikethrough);
        fontColor.Add(style.Color);
        highlightColor.Add(style.HighlightColor);
        underlineColor.Add(style.UnderlineColor);
        smallCaps.Add(style.SmallCaps);
        verticalPosition.Add(style.VerticalPosition);
        var effects = style.Effects;
        textOutline.Add(effects?.Outline?.Enabled ?? false);
        textShadow.Add(effects?.Shadow?.Enabled ?? false);
        textEmboss.Add(effects?.Emboss ?? false);
        textImprint.Add(effects?.Imprint ?? false);
    }

    private static string ResolveFontFamily(TextStyle style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            return style.FontFamily;
        }

        return style.FontFamilyAscii
               ?? style.FontFamilyHighAnsi
               ?? style.FontFamilyEastAsia
               ?? style.FontFamilyComplexScript
               ?? string.Empty;
    }
}
