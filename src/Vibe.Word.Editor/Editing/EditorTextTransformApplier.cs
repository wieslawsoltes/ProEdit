using System;
using System.Collections.Generic;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorTextTransformApplier
{
    private readonly IEditorMutableSession _session;

    public EditorTextTransformApplier(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool Apply(TextRange range, Func<ReadOnlySpan<char>, string> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var selection = range.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var changed = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (startOffset >= endOffset)
            {
                continue;
            }

            if (paragraph.Inlines.Count == 0)
            {
                changed |= ApplyToPlainParagraph(paragraph, startOffset, endOffset, transform);
            }
            else
            {
                changed |= ApplyToInlineParagraph(paragraph, startOffset, endOffset, transform);
            }
        }

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }

    private static bool ApplyToPlainParagraph(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        Func<ReadOnlySpan<char>, string> transform)
    {
        var text = paragraph.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        startOffset = Math.Clamp(startOffset, 0, text.Length);
        endOffset = Math.Clamp(endOffset, 0, text.Length);
        if (startOffset >= endOffset)
        {
            return false;
        }

        var transformed = transform(text.AsSpan(startOffset, endOffset - startOffset));
        if (transformed.Length == 0 && endOffset - startOffset == 0)
        {
            return false;
        }

        var builder = new StringBuilder(text.Length - (endOffset - startOffset) + transformed.Length);
        builder.Append(text.AsSpan(0, startOffset));
        builder.Append(transformed);
        builder.Append(text.AsSpan(endOffset));
        paragraph.Text = builder.ToString();
        return true;
    }

    private static bool ApplyToInlineParagraph(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        Func<ReadOnlySpan<char>, string> transform)
    {
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

            if (selectionStart > 0)
            {
                newInlines.Add(CloneRunSlice(run, 0, selectionStart));
            }

            if (selectionEnd > selectionStart)
            {
                var original = run.Text.GetSlice(selectionStart, selectionEnd - selectionStart);
                var transformed = transform(original.AsSpan());
                newInlines.Add(CloneRunWithText(run, transformed));
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
            NormalizeInlines(paragraph);
        }

        return changed;
    }

    private static RunInline CloneRunSlice(RunInline run, int start, int length)
    {
        var buffer = run.Text.SliceBuffer(start, length);
        var clone = new RunInline(buffer, run.Style)
        {
            StyleId = run.StyleId,
            Hyperlink = run.Hyperlink
        };
        return clone;
    }

    private static RunInline CloneRunWithText(RunInline run, string text)
    {
        var clone = new RunInline(text, run.Style)
        {
            StyleId = run.StyleId,
            Hyperlink = run.Hyperlink
        };
        return clone;
    }

    private static void NormalizeInlines(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Text = string.Empty;
            return;
        }

        var normalized = new List<Inline>(paragraph.Inlines.Count);
        RunInline? lastRun = null;

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                if (lastRun is not null && AreRunsMergeable(lastRun, run))
                {
                    lastRun.Text.Append(run.Text);
                }
                else
                {
                    normalized.Add(run);
                    lastRun = run;
                }
            }
            else
            {
                normalized.Add(inline);
                lastRun = null;
            }
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(normalized);
        UpdateParagraphText(paragraph);
    }

    private static bool AreRunsMergeable(RunInline left, RunInline right)
    {
        if (!string.Equals(left.StyleId, right.StyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (left.Style is null && right.Style is null)
        {
            return true;
        }

        if (left.Style is null || right.Style is null)
        {
            return false;
        }

        return left.Style.IsEquivalentTo(right.Style);
    }

    private static void UpdateParagraphText(ParagraphBlock paragraph)
    {
        paragraph.Text = BuildInlineText(paragraph.Inlines);
    }

    private static string BuildInlineText(IEnumerable<Inline> inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    builder.Append(run.Text.GetText());
                    break;
                case ImageInline:
                case ShapeInline:
                case ChartInline:
                case EquationInline:
                case PageNumberInline:
                case TotalPagesInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
                case FootnoteReferenceInline footnote:
                    builder.Append(footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case FieldStartInline:
                case FieldSeparatorInline:
                case FieldEndInline:
                case BookmarkStartInline:
                case BookmarkEndInline:
                case CommentRangeStartInline:
                case CommentRangeEndInline:
                case ContentControlStartInline:
                case ContentControlEndInline:
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }
}
