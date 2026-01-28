using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorTextFormattingApplier
{
    private readonly IEditorMutableSession _session;
    private readonly ITextContainerNormalizer? _textNormalizer;

    public EditorTextFormattingApplier(IEditorMutableSession session, ITextContainerNormalizer? textNormalizer = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _textNormalizer = textNormalizer;
    }

    public bool Apply(Action<TextStyleProperties> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return ApplyInternal(apply, clearFormatting: false);
    }

    public bool ClearFormatting()
    {
        return ApplyInternal(static style => ClearStyle(style), clearFormatting: true);
    }

    private bool ApplyInternal(Action<TextStyleProperties> apply, bool clearFormatting)
    {
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var changed = selection.IsEmpty
            ? ApplyToCaret(selection.Start, apply, clearFormatting)
            : ApplyToRange(selection, apply, clearFormatting);

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }

    private bool ApplyToCaret(TextPosition caret, Action<TextStyleProperties> apply, bool clearFormatting)
    {
        var paragraphCount = _session.GetParagraphCountFast();
        var paragraphIndex = Math.Clamp(caret.ParagraphIndex, 0, paragraphCount - 1);
        var paragraph = _session.GetParagraphFast(paragraphIndex);
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var offset = Math.Clamp(caret.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        var position = 0;
        RunInline? target = null;

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inline is not RunInline run)
            {
                continue;
            }

            if (offset >= inlineStart && offset <= inlineEnd)
            {
                target = run;
                break;
            }

            target = run;
        }

        if (target is null)
        {
            return false;
        }

        if (clearFormatting)
        {
            target.Style = null;
            return true;
        }

        ApplyToRun(target, apply);
        return true;
    }

    private bool ApplyToRange(TextRange range, Action<TextStyleProperties> apply, bool clearFormatting)
    {
        var paragraphCount = _session.GetParagraphCountFast();
        var startIndex = Math.Clamp(range.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(range.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var changed = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? range.Start.Offset : 0;
            var endOffset = i == endIndex ? range.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (startOffset >= endOffset)
            {
                continue;
            }

            changed |= ApplyToParagraphRange(paragraph, startOffset, endOffset, apply, clearFormatting);
        }

        return changed;
    }

    private bool ApplyToParagraphRange(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        Action<TextStyleProperties> apply,
        bool clearFormatting)
    {
        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count + 2);
        var changed = false;
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inline is not RunInline run)
            {
                newInlines.Add(inline);
                continue;
            }

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                newInlines.Add(run);
                continue;
            }

            var selectionStart = Math.Max(startOffset, inlineStart) - inlineStart;
            var selectionEnd = Math.Min(endOffset, inlineEnd) - inlineStart;

            if (selectionStart <= 0 && selectionEnd >= length)
            {
                if (clearFormatting)
                {
                    run.Style = null;
                }
                else
                {
                    ApplyToRun(run, apply);
                }

                newInlines.Add(run);
                changed = true;
                continue;
            }

            if (selectionStart > 0)
            {
                newInlines.Add(CloneRunSlice(run, 0, selectionStart));
            }

            if (selectionEnd > selectionStart)
            {
                var selected = CloneRunSlice(run, selectionStart, selectionEnd - selectionStart, apply, clearFormatting);
                newInlines.Add(selected);
                changed = true;
            }

            if (selectionEnd < length)
            {
                newInlines.Add(CloneRunSlice(run, selectionEnd, length - selectionEnd));
            }
        }

        if (changed)
        {
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(newInlines);
        }

        return changed;
    }

    private void EnsureParagraphInlines(ParagraphBlock paragraph)
    {
        if (_textNormalizer is not null)
        {
            _textNormalizer.EnsureParagraphInlines(paragraph);
        }
        else
        {
            DocumentEditHelpers.EnsureParagraphInlines(paragraph);
        }
    }

    private static void ApplyToRun(RunInline run, Action<TextStyleProperties> apply)
    {
        var style = run.Style?.Clone() ?? new TextStyleProperties();
        apply(style);
        run.Style = style;
    }

    private static RunInline CloneRunSlice(
        RunInline run,
        int start,
        int length,
        Action<TextStyleProperties>? apply = null,
        bool clearFormatting = false)
    {
        var buffer = run.Text.SliceBuffer(start, length);
        TextStyleProperties? style = run.Style;
        if (clearFormatting)
        {
            style = null;
        }
        else if (apply is not null)
        {
            style = style?.Clone() ?? new TextStyleProperties();
            apply(style);
        }

        var clone = new RunInline(buffer, style)
        {
            StyleId = run.StyleId,
            Hyperlink = run.Hyperlink
        };
        return clone;
    }

    private static void ClearStyle(TextStyleProperties style)
    {
        style.FontFamily = null;
        style.FontFamilyAscii = null;
        style.FontFamilyHighAnsi = null;
        style.FontFamilyEastAsia = null;
        style.FontFamilyComplexScript = null;
        style.FontSize = null;
        style.FontSizeComplexScript = null;
        style.FontWeight = null;
        style.FontStyle = null;
        style.Color = null;
        style.VerticalPosition = null;
        style.BaselineOffset = null;
        style.LetterSpacing = null;
        style.HorizontalScale = null;
        style.Kerning = null;
        style.Caps = null;
        style.SmallCaps = null;
        style.Underline = null;
        style.UnderlineStyle = null;
        style.UnderlineColor = null;
        style.Strikethrough = null;
        style.HighlightColor = null;
        style.Hidden = null;
        style.ThemeFontAscii = null;
        style.ThemeFontHighAnsi = null;
        style.ThemeFontEastAsia = null;
        style.ThemeFontComplexScript = null;
        style.Language = null;
        style.LanguageEastAsia = null;
        style.LanguageBidi = null;
        style.EastAsianLayout = null;
        style.OpenTypeFeatures = null;
        style.Effects = null;
    }
}
