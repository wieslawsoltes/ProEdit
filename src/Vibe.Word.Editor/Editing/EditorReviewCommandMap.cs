using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorReviewCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorServices _services;
    private int _commentCounter;

    public EditorReviewCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _commentCounter = FindNextCommentId(session.Document);
    }

    public void Register()
    {
        _router.RegisterAction(EditorReviewCommandIds.Proofing.SpellingGrammar, (_, __) => ShowSpellingGrammar(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Proofing.Thesaurus, (_, __) => ShowThesaurus(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Proofing.WordCount, (_, __) => ShowWordCount(), (context, _) => HasParagraphs(context), isUndoable: false);

        _router.RegisterAction(EditorReviewCommandIds.Proofing.ApplySuggestion, (_, payload) => ApplySuggestion(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReviewCommandIds.Proofing.IgnoreWord, (_, __) => IgnoreWord(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReviewCommandIds.Proofing.AddToDictionary, (_, __) => AddWordToDictionary(), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorReviewCommandIds.Speech.ReadAloud, (_, __) => ShowNotImplemented("Read Aloud", "Read Aloud is not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Accessibility.CheckAccessibility, (_, __) => ShowNotImplemented("Accessibility", "Accessibility checks are not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);

        _router.RegisterAction(EditorReviewCommandIds.Language.Translate, (_, __) => ShowNotImplemented("Translate", "Translation is not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Language.SetLanguage, (_, payload) => SetLanguage(payload), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorReviewCommandIds.Comments.NewComment, (_, __) => InsertComment(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReviewCommandIds.Comments.ReplyComment, (_, payload) => ReplyToComment(payload), (context, _) => HasComments(context));
        _router.RegisterAction(EditorReviewCommandIds.Comments.DeleteComment, (_, payload) => DeleteComment(payload), (context, _) => HasComments(context));
        _router.RegisterAction(EditorReviewCommandIds.Comments.ResolveComment, (_, payload) => ToggleCommentResolved(payload), (context, _) => HasComments(context));
        _router.RegisterAction(EditorReviewCommandIds.Comments.PreviousComment, (_, __) => NavigateComment(-1), (context, _) => HasComments(context));
        _router.RegisterAction(EditorReviewCommandIds.Comments.NextComment, (_, __) => NavigateComment(1), (context, _) => HasComments(context));

        _router.RegisterAction(EditorReviewCommandIds.Tracking.TrackChangesToggle, (_, payload) => ToggleTrackChanges(payload));
        _router.RegisterAction(EditorReviewCommandIds.Tracking.ShowMarkup, (_, payload) => SetMarkupMode(payload), (context, _) => CanToggleReviewPane(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Tracking.ReviewingPane, (_, __) => ToggleReviewingPane(), (context, _) => CanToggleReviewPane(context), isUndoable: false);

        _router.RegisterAction(EditorReviewCommandIds.Changes.Accept, (_, __) => ApplyRevision(true), (context, _) => HasRevisions(context));
        _router.RegisterAction(EditorReviewCommandIds.Changes.Reject, (_, __) => ApplyRevision(false), (context, _) => HasRevisions(context));
        _router.RegisterAction(EditorReviewCommandIds.Changes.AcceptAll, (_, __) => ApplyAllRevisions(true), (context, _) => HasRevisions(context));
        _router.RegisterAction(EditorReviewCommandIds.Changes.RejectAll, (_, __) => ApplyAllRevisions(false), (context, _) => HasRevisions(context));
        _router.RegisterAction(EditorReviewCommandIds.Changes.PreviousChange, (_, __) => NavigateRevision(-1), (context, _) => HasRevisions(context));
        _router.RegisterAction(EditorReviewCommandIds.Changes.NextChange, (_, __) => NavigateRevision(1), (context, _) => HasRevisions(context));

        _router.RegisterAction(EditorReviewCommandIds.Compare.CompareDocuments, (_, __) => ShowNotImplemented("Compare Documents", "Document comparison is not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorReviewCommandIds.Compare.Combine, (_, __) => ShowNotImplemented("Combine Documents", "Document combine is not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);

        _router.RegisterAction(EditorReviewCommandIds.Protect.RestrictEditing, (_, __) => ShowNotImplemented("Restrict Editing", "Restrict editing is not available yet."), (context, _) => HasParagraphs(context), isUndoable: false);
    }

    private bool HasParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private bool HasComments(RibbonContextSnapshot? context)
    {
        if (!HasParagraphs(context))
        {
            return false;
        }

        return _session.Document.Comments.Count > 0 || HasCommentMarkers();
    }

    private bool HasRevisions(RibbonContextSnapshot? context)
    {
        if (!HasParagraphs(context))
        {
            return false;
        }

        return HasRevisionMarkers();
    }

    private void ToggleTrackChanges(object? payload)
    {
        var enabled = payload is bool value ? value : !_session.Document.TrackChangesEnabled;
        _session.Document.TrackChangesEnabled = enabled;
    }

    private bool CanToggleReviewPane(RibbonContextSnapshot? context)
    {
        return TryGetReviewPaneService(out _);
    }

    private void ToggleReviewingPane()
    {
        if (TryGetReviewPaneService(out var service))
        {
            service.ToggleReviewingPane();
        }
    }

    private void SetMarkupMode(object? payload)
    {
        if (!TryGetReviewPaneService(out var service))
        {
            return;
        }

        if (payload is ReviewMarkupMode mode)
        {
            service.MarkupMode = mode;
            return;
        }

        if (payload is string label && TryParseMarkupMode(label, out mode))
        {
            service.MarkupMode = mode;
        }
    }

    private bool TryGetReviewPaneService(out IReviewPaneService service)
    {
        return _services.TryGet(out service);
    }

    private bool TryGetProofingService(out IProofingService service)
    {
        return _services.TryGet(out service);
    }

    private static bool TryParseMarkupMode(string label, out ReviewMarkupMode mode)
    {
        mode = ReviewMarkupMode.All;
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (string.Equals(label, "All", StringComparison.OrdinalIgnoreCase))
        {
            mode = ReviewMarkupMode.All;
            return true;
        }

        if (string.Equals(label, "Simple", StringComparison.OrdinalIgnoreCase))
        {
            mode = ReviewMarkupMode.Simple;
            return true;
        }

        if (string.Equals(label, "None", StringComparison.OrdinalIgnoreCase))
        {
            mode = ReviewMarkupMode.None;
            return true;
        }

        if (string.Equals(label, "Balloons", StringComparison.OrdinalIgnoreCase))
        {
            mode = ReviewMarkupMode.Balloons;
            return true;
        }

        return false;
    }

    private void ShowNotImplemented(string title, string message)
    {
        if (!TryGetDialogService(out var dialog))
        {
            return;
        }

        _ = dialog.ShowMessageAsync(title, message);
    }

    private void ShowWordCount()
    {
        if (!TryGetDialogService(out var dialog))
        {
            return;
        }

        var totals = new WordCountTotals();
        AccumulateDocument(_session.Document, ref totals);
        var message = BuildWordCountMessage(totals);
        _ = dialog.ShowMessageAsync("Word Count", message);
    }

    private void ShowSpellingGrammar()
    {
        if (_services.TryGet<IProofingToggleService>(out var toggle))
        {
            toggle.SetEnabled(true);
            toggle.SetSpellingEnabled(true);
            toggle.SetGrammarEnabled(true);
        }

        if (_services.TryGet<IProofingDialogService>(out var proofingDialog))
        {
            _ = proofingDialog.ShowSpellingGrammarAsync();
            return;
        }

        if (!TryGetProofingService(out var proofing))
        {
            ShowNotImplemented("Spelling & Grammar", "Spelling and grammar checking is not available yet.");
            return;
        }

        proofing.RefreshAll();
        var total = proofing.GetTotalDiagnostics();
        if (!TryGetDialogService(out var dialog))
        {
            return;
        }

        var message = total == 0
            ? "No spelling issues found."
            : $"Found {total} spelling issue{(total == 1 ? string.Empty : "s")}.";
        _ = dialog.ShowMessageAsync("Spelling & Grammar", message);
    }

    private void ShowThesaurus()
    {
        if (_services.TryGet<IProofingDialogService>(out var proofingDialog))
        {
            _ = proofingDialog.ShowThesaurusAsync();
            return;
        }

        ShowNotImplemented("Thesaurus", "Thesaurus lookup is not available yet.");
    }

    private void ApplySuggestion(object? payload)
    {
        if (payload is not string suggestion || string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        if (!TryGetActiveWord(out _, out var range))
        {
            return;
        }

        _session.SetSelection(range);
        _session.InsertText(suggestion.AsSpan());
    }

    private void IgnoreWord()
    {
        if (!TryGetProofingService(out var proofing))
        {
            return;
        }

        if (!TryGetActiveWord(out var word, out _))
        {
            return;
        }

        proofing.IgnoreWord(word);
    }

    private void AddWordToDictionary()
    {
        if (!TryGetProofingService(out var proofing))
        {
            return;
        }

        if (!TryGetActiveWord(out var word, out _))
        {
            return;
        }

        proofing.AddToUserDictionary(word);
    }

    private bool TryGetActiveWord(out string word, out TextRange range)
    {
        word = string.Empty;
        range = default;

        var selection = _session.Selection;
        if (selection.Start.ParagraphIndex != selection.End.ParagraphIndex)
        {
            return false;
        }

        var paragraphIndex = selection.Start.ParagraphIndex;
        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var start = Math.Clamp(selection.Start.Offset, 0, text.Length);
        var end = Math.Clamp(selection.End.Offset, 0, text.Length);
        if (end > start)
        {
            var selectionSpan = text.AsSpan(start, end - start);
            var trimmedStartOffset = 0;
            while (trimmedStartOffset < selectionSpan.Length && char.IsWhiteSpace(selectionSpan[trimmedStartOffset]))
            {
                trimmedStartOffset++;
            }

            var trimmedEndOffset = selectionSpan.Length;
            while (trimmedEndOffset > trimmedStartOffset && char.IsWhiteSpace(selectionSpan[trimmedEndOffset - 1]))
            {
                trimmedEndOffset--;
            }

            if (trimmedEndOffset > trimmedStartOffset)
            {
                var wordStart = start + trimmedStartOffset;
                var wordEnd = start + trimmedEndOffset;
                word = text.Substring(wordStart, wordEnd - wordStart);
                range = new TextRange(new TextPosition(paragraphIndex, wordStart), new TextPosition(paragraphIndex, wordEnd));
                return true;
            }
        }

        var caretOffset = Math.Clamp(_session.Caret.Offset, 0, text.Length - 1);
        if (!ProofingTokenizer.TryGetWordAtOffset(text.AsSpan(), caretOffset, out var span))
        {
            return false;
        }

        word = text.Substring(span.Start, span.Length);
        range = new TextRange(new TextPosition(paragraphIndex, span.Start), new TextPosition(paragraphIndex, span.Start + span.Length));
        return true;
    }

    private void SetLanguage(object? payload)
    {
        var language = payload as string;
        if (string.IsNullOrWhiteSpace(language))
        {
            ShowNotImplemented("Language", "Provide a language code to update the document language.");
            return;
        }

        _session.Document.DefaultTextStyle.Language = language.Trim();
        _session.RefreshLayout();
    }

    private bool TryGetDialogService(out IEditorDialogService dialogService)
    {
        return _services.TryGet(out dialogService);
    }

    private static void AccumulateDocument(Document document, ref WordCountTotals totals)
    {
        AccumulateBlocks(document.Blocks, ref totals);

        foreach (var footnote in document.Footnotes.Values)
        {
            AccumulateBlocks(footnote.Blocks, ref totals);
        }

        foreach (var endnote in document.Endnotes.Values)
        {
            AccumulateBlocks(endnote.Blocks, ref totals);
        }
    }

    private static void AccumulateBlocks(IReadOnlyList<Block> blocks, ref WordCountTotals totals)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    AccumulateParagraph(paragraph, ref totals);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                AccumulateParagraph(paragraph, ref totals);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static void AccumulateParagraph(ParagraphBlock paragraph, ref WordCountTotals totals)
    {
        DocumentEditHelpers.EnsureParagraphInlines(paragraph);
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        AddText(text, ref totals);
        totals.Paragraphs++;

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is ShapeInline shape && shape.TextBox is { } textBox)
            {
                AccumulateBlocks(textBox.Blocks, ref totals);
            }
        }

        foreach (var floating in paragraph.FloatingObjects)
        {
            if (floating.Content is ShapeInline shape && shape.TextBox is { } textBox)
            {
                AccumulateBlocks(textBox.Blocks, ref totals);
            }
        }
    }

    private static void AddText(string text, ref WordCountTotals totals)
    {
        totals.Characters += text.Length;
        totals.Words += CountWords(text.AsSpan());

        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                totals.CharactersNoSpaces++;
            }
        }
    }

    private static int CountWords(ReadOnlySpan<char> text)
    {
        var count = 0;
        var inWord = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }

    private static string BuildWordCountMessage(WordCountTotals totals)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Words: {totals.Words}");
        builder.AppendLine($"Characters (with spaces): {totals.Characters}");
        builder.AppendLine($"Characters (no spaces): {totals.CharactersNoSpaces}");
        builder.AppendLine($"Paragraphs: {totals.Paragraphs}");
        return builder.ToString().TrimEnd();
    }

    private struct WordCountTotals
    {
        public int Words;
        public int Characters;
        public int CharactersNoSpaces;
        public int Paragraphs;
    }

    private void InsertComment()
    {
        var range = ResolveCommentRange();
        var commentId = _commentCounter++;
        var definition = new CommentDefinition(commentId)
        {
            Author = Environment.UserName,
            Date = DateTime.UtcNow,
            ThreadId = commentId
        };
        definition.Blocks.Add(new ParagraphBlock("Comment"));
        _session.Document.Comments[commentId] = definition;

        InsertCommentMarkers(range, commentId);
        _session.RefreshLayout();
    }

    private void DeleteComment(object? payload)
    {
        if (!TryResolveCommentId(payload, out var commentId))
        {
            return;
        }

        var comments = _session.Document.Comments;
        if (!comments.TryGetValue(commentId, out var comment))
        {
            return;
        }

        var threadId = CommentThreading.ResolveThreadId(comment, comments);
        if (!comment.ParentId.HasValue || commentId == threadId)
        {
            var threadComments = CollectThreadCommentIds(threadId, comments);
            foreach (var id in threadComments)
            {
                comments.Remove(id);
            }

            RemoveCommentMarkers(threadId);
        }
        else
        {
            comments.Remove(commentId);
        }

        _session.RefreshLayout();
    }

    private void ReplyToComment(object? payload)
    {
        if (!TryResolveCommentId(payload, out var commentId))
        {
            return;
        }

        var comments = _session.Document.Comments;
        if (!comments.TryGetValue(commentId, out var comment))
        {
            return;
        }

        var threadId = CommentThreading.ResolveThreadId(comment, comments);
        var replyId = _commentCounter++;
        var definition = new CommentDefinition(replyId)
        {
            Author = Environment.UserName,
            Date = DateTime.UtcNow,
            ParentId = commentId,
            ThreadId = threadId
        };

        definition.Blocks.Add(new ParagraphBlock("Reply"));
        comments[replyId] = definition;

        if (comments.TryGetValue(threadId, out var root) && root.IsResolved)
        {
            root.IsResolved = false;
            root.ResolvedBy = null;
            root.ResolvedDate = null;
        }

        _session.RefreshLayout();
    }

    private void ToggleCommentResolved(object? payload)
    {
        if (!TryResolveCommentId(payload, out var commentId))
        {
            return;
        }

        var comments = _session.Document.Comments;
        if (!comments.TryGetValue(commentId, out var comment))
        {
            return;
        }

        var root = CommentThreading.ResolveRootComment(comment, comments);
        var resolved = !root.IsResolved;
        root.IsResolved = resolved;
        root.ResolvedBy = resolved ? Environment.UserName : null;
        root.ResolvedDate = resolved ? DateTime.UtcNow : null;
        _session.RefreshLayout();
    }

    private void NavigateComment(int direction)
    {
        var anchors = BuildCommentAnchors();
        if (anchors.Count == 0)
        {
            return;
        }

        anchors.Sort(CompareAnchors);
        var caret = _session.Caret;
        var nextIndex = FindAnchorIndex(anchors, caret, direction);
        if (nextIndex < 0)
        {
            return;
        }

        var anchor = anchors[nextIndex];
        _session.SetSelection(new TextRange(anchor.Position, anchor.Position));
    }

    private void ApplyRevision(bool accept)
    {
        if (!TryGetRevisionSpan(out var span))
        {
            return;
        }

        if (span.IsBlock)
        {
            ApplyBlockRevision(span.BlockSpan, accept);
            _session.RefreshLayout();
            return;
        }

        ApplyInlineRevision(span.InlineSpan, accept);
        _session.RefreshLayout();
    }

    private void ApplyAllRevisions(bool accept)
    {
        var inlineSpans = CollectInlineRevisionSpans();
        var blockSpans = CollectBlockRevisionSpans();
        if (inlineSpans.Count == 0 && blockSpans.Count == 0)
        {
            return;
        }

        inlineSpans.Sort((left, right) =>
        {
            var paragraphCompare = right.ParagraphIndex.CompareTo(left.ParagraphIndex);
            if (paragraphCompare != 0)
            {
                return paragraphCompare;
            }

            return right.StartOffset.CompareTo(left.StartOffset);
        });

        foreach (var span in inlineSpans)
        {
            ApplyInlineRevision(span, accept);
        }

        blockSpans.Sort((left, right) => right.StartBlockIndex.CompareTo(left.StartBlockIndex));
        foreach (var span in blockSpans)
        {
            ApplyBlockRevision(span, accept);
        }

        _session.RefreshLayout();
    }

    private void NavigateRevision(int direction)
    {
        var anchors = BuildRevisionAnchors();
        if (anchors.Count == 0)
        {
            return;
        }

        anchors.Sort(CompareAnchors);
        var caret = _session.Caret;
        var nextIndex = FindAnchorIndex(anchors, caret, direction);
        if (nextIndex < 0)
        {
            return;
        }

        var anchor = anchors[nextIndex];
        _session.SetSelection(new TextRange(anchor.Position, anchor.Position));
    }

    private TextRange ResolveCommentRange()
    {
        var selection = _session.Selection.Normalize();
        return selection.IsEmpty
            ? new TextRange(_session.Caret, _session.Caret)
            : selection;
    }

    private void InsertCommentMarkers(TextRange range, int commentId)
    {
        var normalized = range.Normalize();
        var endPosition = normalized.End;
        var startPosition = normalized.Start;

        InsertInlinesAtPosition(endPosition, new Inline[]
        {
            new CommentRangeEndInline(commentId),
            new CommentReferenceInline(commentId)
        });

        InsertInlinesAtPosition(startPosition, new Inline[]
        {
            new CommentRangeStartInline(commentId)
        });
    }

    private void InsertInlinesAtPosition(TextPosition position, IReadOnlyList<Inline> inlines)
    {
        _session.SetSelection(new TextRange(position, position));
        _session.InsertInlines(inlines);
    }

    private bool TryResolveCommentId(object? payload, out int commentId)
    {
        if (payload is int id)
        {
            commentId = id;
            return true;
        }

        return TryResolveCommentId(out commentId);
    }

    private bool TryResolveCommentId(out int commentId)
    {
        commentId = 0;
        var caret = _session.Caret;
        if (TryGetCommentIdAtPosition(caret, out commentId))
        {
            return true;
        }

        var selection = _session.Selection.Normalize();
        if (!selection.IsEmpty && TryGetCommentIdAtPosition(selection.Start, out commentId))
        {
            return true;
        }

        return false;
    }

    private bool TryGetCommentIdAtPosition(TextPosition position, out int commentId)
    {
        commentId = 0;
        if (_session.Layout.CommentHighlightsByParagraph.TryGetValue(position.ParagraphIndex, out var spans))
        {
            foreach (var span in spans)
            {
                if (position.Offset >= span.StartOffset && position.Offset <= span.EndOffset)
                {
                    commentId = span.Id;
                    return true;
                }
            }
        }

        if (TryGetInlineAtPosition(position, out var inline) && inline is CommentReferenceInline comment)
        {
            commentId = comment.Id;
            return true;
        }

        return false;
    }

    private void RemoveCommentMarkers(int commentId)
    {
        foreach (var (paragraph, _) in EnumerateParagraphs())
        {
            if (paragraph.Inlines.Count == 0)
            {
                continue;
            }

            var removed = false;
            for (var i = paragraph.Inlines.Count - 1; i >= 0; i--)
            {
                var inline = paragraph.Inlines[i];
                if (inline is CommentRangeStartInline start && start.Id == commentId)
                {
                    paragraph.Inlines.RemoveAt(i);
                    removed = true;
                }
                else if (inline is CommentRangeEndInline end && end.Id == commentId)
                {
                    paragraph.Inlines.RemoveAt(i);
                    removed = true;
                }
                else if (inline is CommentReferenceInline reference && reference.Id == commentId)
                {
                    paragraph.Inlines.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                NormalizeParagraphInlines(paragraph);
            }
        }
    }

    private static List<int> CollectThreadCommentIds(int threadId, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        var ids = new List<int>();
        foreach (var pair in comments)
        {
            if (CommentThreading.ResolveThreadId(pair.Value, comments) == threadId)
            {
                ids.Add(pair.Key);
            }
        }

        return ids;
    }

    private bool HasCommentMarkers()
    {
        foreach (var (paragraph, _) in EnumerateParagraphs())
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is CommentRangeStartInline or CommentRangeEndInline or CommentReferenceInline)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasRevisionMarkers()
    {
        foreach (var block in _session.Document.Blocks)
        {
            if (block is RevisionStartBlock or RevisionEndBlock)
            {
                return true;
            }
        }

        foreach (var (paragraph, _) in EnumerateParagraphs())
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is RevisionStartInline or RevisionEndInline)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<CommentAnchor> BuildCommentAnchors()
    {
        var anchors = new List<CommentAnchor>();
        foreach (var (paragraph, paragraphIndex) in EnumerateParagraphs())
        {
            var position = 0;
            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case CommentRangeStartInline start:
                        anchors.Add(new CommentAnchor(start.Id, new TextPosition(paragraphIndex, position)));
                        break;
                    case CommentReferenceInline reference:
                        anchors.Add(new CommentAnchor(reference.Id, new TextPosition(paragraphIndex, position)));
                        break;
                }

                position += DocumentEditHelpers.GetInlineLength(inline);
            }
        }

        return anchors;
    }

    private List<CommentAnchor> BuildRevisionAnchors()
    {
        var anchors = new List<CommentAnchor>();
        foreach (var span in CollectInlineRevisionSpans())
        {
            anchors.Add(new CommentAnchor(span.RevisionId, new TextPosition(span.ParagraphIndex, span.StartOffset)));
        }

        foreach (var span in CollectBlockRevisionSpans())
        {
            anchors.Add(new CommentAnchor(span.RevisionId, new TextPosition(span.StartParagraphIndex, 0)));
        }

        return anchors;
    }

    private bool TryGetRevisionSpan(out RevisionSpan span)
    {
        span = default;
        var caret = _session.Caret;
        var inlineSpans = CollectInlineRevisionSpans();
        foreach (var candidate in inlineSpans)
        {
            if (candidate.ParagraphIndex == caret.ParagraphIndex
                && caret.Offset >= candidate.StartOffset
                && caret.Offset <= candidate.EndOffset)
            {
                span = RevisionSpan.FromInline(candidate);
                return true;
            }
        }

        foreach (var candidate in CollectBlockRevisionSpans())
        {
            if (caret.ParagraphIndex >= candidate.StartParagraphIndex && caret.ParagraphIndex <= candidate.EndParagraphIndex)
            {
                span = RevisionSpan.FromBlock(candidate);
                return true;
            }
        }

        var anchors = BuildRevisionAnchors();
        if (anchors.Count == 0)
        {
            return false;
        }

        anchors.Sort(CompareAnchors);
        var nextIndex = FindAnchorIndex(anchors, caret, 1);
        if (nextIndex < 0)
        {
            return false;
        }

        var anchor = anchors[nextIndex];
        foreach (var candidate in inlineSpans)
        {
            if (candidate.RevisionId == anchor.Id && candidate.ParagraphIndex == anchor.Position.ParagraphIndex)
            {
                span = RevisionSpan.FromInline(candidate);
                return true;
            }
        }

        foreach (var candidate in CollectBlockRevisionSpans())
        {
            if (candidate.RevisionId == anchor.Id)
            {
                span = RevisionSpan.FromBlock(candidate);
                return true;
            }
        }

        return false;
    }

    private void ApplyInlineRevision(InlineRevisionSpan span, bool accept)
    {
        if (_session.Document.ParagraphCount <= 0)
        {
            return;
        }

        var paragraph = _session.Document.GetParagraph(span.ParagraphIndex);
        var removeContent = ShouldRemoveContent(span.Kind, accept);
        if (removeContent)
        {
            DeleteRangeInParagraph(paragraph, span.StartOffset, span.EndOffset);
        }

        RemoveInlineRevisionMarkers(paragraph, span.Kind, span.RevisionId);
    }

    private void ApplyBlockRevision(BlockRevisionSpan span, bool accept)
    {
        var removeBlocks = ShouldRemoveContent(span.Kind, accept);
        var blocks = _session.Document.Blocks;

        for (var i = span.EndBlockIndex; i >= span.StartBlockIndex; i--)
        {
            var block = blocks[i];
            if (removeBlocks)
            {
                blocks.RemoveAt(i);
                continue;
            }

            if (block is RevisionStartBlock or RevisionEndBlock)
            {
                blocks.RemoveAt(i);
            }
        }
    }

    private static bool ShouldRemoveContent(RevisionKind kind, bool accept)
    {
        var isInsert = kind is RevisionKind.Insert or RevisionKind.MoveTo;
        if (accept)
        {
            return !isInsert;
        }

        return isInsert;
    }

    private void RemoveInlineRevisionMarkers(ParagraphBlock paragraph, RevisionKind kind, int? id)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return;
        }

        var removed = false;
        for (var i = paragraph.Inlines.Count - 1; i >= 0; i--)
        {
            var inline = paragraph.Inlines[i];
            if (inline is RevisionStartInline start && RevisionMatches(start.Revision, kind, id))
            {
                paragraph.Inlines.RemoveAt(i);
                removed = true;
            }
            else if (inline is RevisionEndInline end && end.Kind == kind && end.Id == id)
            {
                paragraph.Inlines.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            NormalizeParagraphInlines(paragraph);
        }
    }

    private static bool RevisionMatches(RevisionInfo revision, RevisionKind kind, int? id)
    {
        if (revision.Kind != kind)
        {
            return false;
        }

        if (!id.HasValue)
        {
            return revision.Id is null;
        }

        return revision.Id == id;
    }

    private void DeleteRangeInParagraph(ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var start = Math.Clamp(startOffset, 0, length);
        var end = Math.Clamp(endOffset, 0, length);
        if (end <= start)
        {
            return;
        }

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            paragraph.Text = text.Remove(start, end - start);
            return;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count);
        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = DocumentEditHelpers.GetInlineLength(inline);
            var inlineEnd = position + inlineLength;
            if (inlineEnd <= start || position >= end)
            {
                newInlines.Add(inline);
            }
            else if (inline is RunInline run)
            {
                var runLength = run.Text.Length;
                var deleteStart = Math.Clamp(start - position, 0, runLength);
                var deleteEnd = Math.Clamp(end - position, 0, runLength);
                if (deleteStart > 0)
                {
                    newInlines.Add(new RunInline(run.Text.SliceBuffer(0, deleteStart), run.Style) { StyleId = run.StyleId });
                }

                var afterLength = runLength - deleteEnd;
                if (afterLength > 0)
                {
                    newInlines.Add(new RunInline(run.Text.SliceBuffer(deleteEnd, afterLength), run.Style) { StyleId = run.StyleId });
                }
            }

            position = inlineEnd;
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(newInlines);
        NormalizeParagraphInlines(paragraph);
    }

    private List<InlineRevisionSpan> CollectInlineRevisionSpans()
    {
        var spans = new List<InlineRevisionSpan>();
        foreach (var (paragraph, paragraphIndex) in EnumerateParagraphs())
        {
            if (paragraph.Inlines.Count == 0)
            {
                continue;
            }

            var open = new Dictionary<RevisionKey, int>();
            var position = 0;
            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case RevisionStartInline start:
                        if (!open.ContainsKey(new RevisionKey(start.Revision.Kind, start.Revision.Id)))
                        {
                            open[new RevisionKey(start.Revision.Kind, start.Revision.Id)] = position;
                        }

                        break;
                    case RevisionEndInline end:
                    {
                        var key = new RevisionKey(end.Kind, end.Id);
                        if (open.TryGetValue(key, out var startOffset))
                        {
                            spans.Add(new InlineRevisionSpan(end.Kind, end.Id, paragraphIndex, startOffset, position));
                            open.Remove(key);
                        }

                        break;
                    }
                }

                position += DocumentEditHelpers.GetInlineLength(inline);
            }
        }

        return spans;
    }

    private List<BlockRevisionSpan> CollectBlockRevisionSpans()
    {
        var spans = new List<BlockRevisionSpan>();
        var open = new Dictionary<RevisionKey, OpenBlockRevision>();
        var paragraphIndex = 0;
        var blocks = _session.Document.Blocks;

        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            switch (block)
            {
                case RevisionStartBlock start:
                {
                    var key = new RevisionKey(start.Revision.Kind, start.Revision.Id);
                    if (!open.ContainsKey(key))
                    {
                        open[key] = new OpenBlockRevision(start.Revision, blockIndex, paragraphIndex);
                    }

                    break;
                }
                case RevisionEndBlock end:
                {
                    var key = new RevisionKey(end.Kind, end.Id);
                    if (open.TryGetValue(key, out var openRevision))
                    {
                        spans.Add(new BlockRevisionSpan(openRevision.Revision, openRevision.StartBlockIndex, blockIndex, openRevision.StartParagraphIndex, paragraphIndex));
                        open.Remove(key);
                    }

                    break;
                }
                case ParagraphBlock:
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    paragraphIndex += CountParagraphs(table);
                    break;
            }
        }

        return spans;
    }

    private static int CountParagraphs(TableBlock table)
    {
        var count = 0;
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                count += cell.Paragraphs.Count;
            }
        }

        return count;
    }

    private IEnumerable<(ParagraphBlock Paragraph, int ParagraphIndex)> EnumerateParagraphs()
    {
        var paragraphIndex = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    yield return (paragraph, paragraphIndex++);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                yield return (paragraph, paragraphIndex++);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private bool TryGetInlineAtPosition(TextPosition position, out Inline inline)
    {
        inline = null!;
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= _session.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _session.Document.GetParagraph(position.ParagraphIndex);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var offset = Math.Clamp(position.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        var current = 0;
        foreach (var item in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(item);
            var end = current + length;
            if (length == 0 && offset == current)
            {
                inline = item;
                return true;
            }

            if (offset < end)
            {
                inline = item;
                return true;
            }

            current = end;
        }

        return false;
    }

    private static int CompareAnchors(CommentAnchor left, CommentAnchor right)
    {
        var paragraphComparison = left.Position.ParagraphIndex.CompareTo(right.Position.ParagraphIndex);
        if (paragraphComparison != 0)
        {
            return paragraphComparison;
        }

        return left.Position.Offset.CompareTo(right.Position.Offset);
    }

    private static int FindAnchorIndex(List<CommentAnchor> anchors, TextPosition caret, int direction)
    {
        if (anchors.Count == 0)
        {
            return -1;
        }

        if (direction >= 0)
        {
            for (var i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (ComparePosition(anchor.Position, caret) > 0)
                {
                    return i;
                }
            }

            return 0;
        }

        for (var i = anchors.Count - 1; i >= 0; i--)
        {
            var anchor = anchors[i];
            if (ComparePosition(anchor.Position, caret) < 0)
            {
                return i;
            }
        }

        return anchors.Count - 1;
    }

    private static int ComparePosition(TextPosition left, TextPosition right)
    {
        if (left.ParagraphIndex != right.ParagraphIndex)
        {
            return left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        }

        return left.Offset.CompareTo(right.Offset);
    }

    private static int FindNextCommentId(Document document)
    {
        var nextId = 1;
        foreach (var id in document.Comments.Keys)
        {
            if (id >= nextId)
            {
                nextId = id + 1;
            }
        }

        foreach (var block in document.Blocks)
        {
            if (block is ParagraphBlock paragraphBlock)
            {
                UpdateCommentIdFromInlines(paragraphBlock.Inlines, ref nextId);
            }
            else if (block is TableBlock table)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraph in cell.Paragraphs)
                        {
                            UpdateCommentIdFromInlines(paragraph.Inlines, ref nextId);
                        }
                    }
                }
            }
        }

        return nextId;
    }

    private static void UpdateCommentIdFromInlines(IReadOnlyList<Inline> inlines, ref int nextId)
    {
        foreach (var inline in inlines)
        {
            var id = inline switch
            {
                CommentRangeStartInline start => start.Id,
                CommentRangeEndInline end => end.Id,
                CommentReferenceInline reference => reference.Id,
                _ => 0
            };

            if (id >= nextId)
            {
                nextId = id + 1;
            }
        }
    }

    private static void NormalizeParagraphInlines(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Text = paragraph.Text ?? string.Empty;
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
        paragraph.Text = DocumentEditHelpers.GetParagraphText(paragraph);
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

    private readonly record struct CommentAnchor(int? Id, TextPosition Position);

    private readonly record struct RevisionKey(RevisionKind Kind, int? Id);

    private readonly record struct InlineRevisionSpan(
        RevisionKind Kind,
        int? RevisionId,
        int ParagraphIndex,
        int StartOffset,
        int EndOffset);

    private readonly record struct OpenBlockRevision(RevisionInfo Revision, int StartBlockIndex, int StartParagraphIndex);

    private readonly record struct BlockRevisionSpan(
        RevisionInfo Revision,
        int StartBlockIndex,
        int EndBlockIndex,
        int StartParagraphIndex,
        int EndParagraphIndex)
    {
        public RevisionKind Kind => Revision.Kind;
        public int? RevisionId => Revision.Id;
    }

    private readonly record struct RevisionSpan(bool IsBlock, InlineRevisionSpan InlineSpan, BlockRevisionSpan BlockSpan)
    {
        public static RevisionSpan FromInline(InlineRevisionSpan span)
            => new RevisionSpan(false, span, default);

        public static RevisionSpan FromBlock(BlockRevisionSpan span)
            => new RevisionSpan(true, default, span);
    }
}
