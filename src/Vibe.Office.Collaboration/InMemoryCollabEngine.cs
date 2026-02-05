using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Collaboration;

/// <summary>
/// In-memory collaboration engine for testing and scaffolding.
/// </summary>
public sealed class InMemoryCollabEngine : ICollabEngine
{
    private readonly Document _document;
    private readonly DocumentAnchorResolver _anchorResolver;
    private readonly CollabBlockOpApplier _blockApplier;
    private readonly CollabPropertyOpApplier _propertyApplier;

    /// <summary>
    /// Creates a new in-memory engine using a cloned document.
    /// </summary>
    public InMemoryCollabEngine(Document document, long initialVersion = 0)
    {
        _document = DocumentClone.Clone(document ?? throw new ArgumentNullException(nameof(document)));
        _anchorResolver = new DocumentAnchorResolver();
        _blockApplier = new CollabBlockOpApplier();
        _propertyApplier = new CollabPropertyOpApplier();
        Version = initialVersion;
    }

    public long Version { get; private set; }

    /// <summary>
    /// Gets the current document state held by the engine.
    /// </summary>
    public Document Document => _document;

    /// <summary>
    /// Applies a batch of operations.
    /// </summary>
    public CollabApplyResult Apply(CollabOpBatch batch, CollabApplyOrigin origin)
    {
        if (!CollabOpValidator.ValidateBatch(batch, out var error))
        {
            throw new InvalidOperationException(error);
        }

        foreach (var op in batch.Ops)
        {
            ApplyOp(op);
            Version++;
        }

        return new CollabApplyResult(Version, batch.Ops);
    }

    private void ApplyOp(ICollabOp op)
    {
        switch (op)
        {
            case InsertTextOp insert:
                ApplyInsertText(insert);
                break;
            case DeleteRangeOp delete:
                ApplyDeleteRange(delete);
                break;
            case InsertBlockOp:
            case DeleteBlockOp:
            case ReplaceBlockOp:
                if (!_blockApplier.Apply(_document, op))
                {
                    throw new InvalidOperationException($"Block op could not be applied: {op.Kind}");
                }

                break;
            case SetParagraphPropertiesOp setParagraph:
                _propertyApplier.ApplyParagraph(_document, setParagraph);
                break;
            case SetInlinePropertiesOp setInline:
                _propertyApplier.ApplyInline(_document, setInline);
                break;
            default:
                throw new NotSupportedException($"Operation not supported by in-memory engine: {op.Kind}");
        }
    }

    private void ApplyInsertText(InsertTextOp insert)
    {
        if (!_anchorResolver.TryResolveAnchor(_document, insert.Anchor, out var paragraph, out var offset))
        {
            throw new InvalidOperationException("InsertText anchor could not be resolved.");
        }

        CollabDocumentEdit.InsertText(paragraph, offset, insert.Text);
    }

    private void ApplyDeleteRange(DeleteRangeOp delete)
    {
        if (delete.Start.NodeId != delete.End.NodeId)
        {
            throw new NotSupportedException("Cross-paragraph delete is not supported in the in-memory engine.");
        }

        if (!_anchorResolver.TryResolveAnchor(_document, delete.Start, out var paragraph, out var startOffset))
        {
            throw new InvalidOperationException("DeleteRange start anchor could not be resolved.");
        }

        var endOffset = Math.Max(0, delete.End.Offset);
        if (endOffset < startOffset)
        {
            (startOffset, endOffset) = (endOffset, startOffset);
        }

        CollabDocumentEdit.DeleteRange(paragraph, startOffset, endOffset);
    }

    private static class CollabDocumentEdit
    {
        public static void InsertText(ParagraphBlock paragraph, int offset, string text)
        {
            EnsureParagraphInlines(paragraph);
            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new RunInline(text));
                UpdateParagraphText(paragraph);
                return;
            }

            var position = FindInlinePosition(paragraph, offset);
            var inline = paragraph.Inlines[position.Index];
            if (inline is RunInline run)
            {
                var insertAt = Math.Clamp(position.OffsetInInline, 0, run.Text.Length);
                run.Text.Insert(insertAt, text);
            }
            else
            {
                var insertIndex = position.OffsetInInline <= 0 ? position.Index : position.Index + 1;
                paragraph.Inlines.Insert(insertIndex, new RunInline(text));
            }

            NormalizeRuns(paragraph);
            UpdateParagraphText(paragraph);
        }

        public static void DeleteRange(ParagraphBlock paragraph, int startOffset, int endOffset)
        {
            EnsureParagraphInlines(paragraph);
            var length = GetParagraphLength(paragraph);
            var start = Math.Clamp(startOffset, 0, length);
            var end = Math.Clamp(endOffset, 0, length);
            if (end <= start)
            {
                return;
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Text = paragraph.Text.Remove(start, end - start);
                return;
            }

            var newInlines = new List<Inline>(paragraph.Inlines.Count);
            var position = 0;

            foreach (var inline in paragraph.Inlines)
            {
                var inlineLength = GetInlineLength(inline);
                if (inlineLength == 0)
                {
                    newInlines.Add(inline);
                    continue;
                }

                var inlineStart = position;
                var inlineEnd = position + inlineLength;
                position = inlineEnd;

                if (inlineEnd <= start || inlineStart >= end)
                {
                    newInlines.Add(inline);
                    continue;
                }

                if (inline is RunInline run)
                {
                    var selectionStart = Math.Max(start, inlineStart) - inlineStart;
                    var selectionEnd = Math.Min(end, inlineEnd) - inlineStart;

                    if (selectionStart > 0)
                    {
                        newInlines.Add(CloneRunSlice(run, 0, selectionStart));
                    }

                    if (selectionEnd < inlineLength)
                    {
                        newInlines.Add(CloneRunSlice(run, selectionEnd, inlineLength - selectionEnd));
                    }
                }
                else
                {
                    // Non-run inline: drop if it falls within the delete range.
                }
            }

            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(newInlines);
            NormalizeRuns(paragraph);
            UpdateParagraphText(paragraph);
        }

        private static void EnsureParagraphInlines(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count > 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(paragraph.Text))
            {
                paragraph.Inlines.Add(new RunInline(paragraph.Text));
            }
        }

        private static void UpdateParagraphText(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (var inline in paragraph.Inlines)
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
                    case FootnoteReferenceInline footnote:
                        builder.Append(footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case EndnoteReferenceInline endnote:
                        builder.Append(endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case CommentReferenceInline comment:
                        builder.Append(comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }
            }

            paragraph.Text = builder.ToString();
        }

        private static int GetParagraphLength(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count == 0)
            {
                return (paragraph.Text ?? string.Empty).Length;
            }

            var length = 0;
            foreach (var inline in paragraph.Inlines)
            {
                length += GetInlineLength(inline);
            }

            return length;
        }

        private static int GetInlineLength(Inline inline)
        {
            return inline switch
            {
                RunInline run => run.Text.Length,
                ImageInline => 1,
                ShapeInline => 1,
                ChartInline => 1,
                EquationInline => 1,
                PageNumberInline => 1,
                TotalPagesInline => 1,
                FootnoteReferenceInline footnote => footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
                EndnoteReferenceInline endnote => endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
                CommentReferenceInline comment => comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
                MetadataStartInline => 0,
                MetadataEndInline => 0,
                FieldStartInline => 0,
                FieldSeparatorInline => 0,
                FieldEndInline => 0,
                BookmarkStartInline => 0,
                BookmarkEndInline => 0,
                CommentRangeStartInline => 0,
                CommentRangeEndInline => 0,
                ContentControlStartInline => 0,
                ContentControlEndInline => 0,
                RevisionStartInline => 0,
                RevisionEndInline => 0,
                RevisionRangeStartInline => 0,
                RevisionRangeEndInline => 0,
                _ => 1
            };
        }

        private static InlinePosition FindInlinePosition(ParagraphBlock paragraph, int offset)
        {
            var position = 0;
            for (var i = 0; i < paragraph.Inlines.Count; i++)
            {
                var inlineLength = GetInlineLength(paragraph.Inlines[i]);
                var inlineStart = position;
                var inlineEnd = position + inlineLength;
                if (offset <= inlineEnd)
                {
                    return new InlinePosition(i, Math.Max(0, offset - inlineStart));
                }

                position = inlineEnd;
            }

            return new InlinePosition(paragraph.Inlines.Count - 1, 0);
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

        private static void NormalizeRuns(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count <= 1)
            {
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

        private readonly record struct InlinePosition(int Index, int OffsetInInline);
    }
}
