using System.Globalization;
using System.Text;
using ProEdit.Documents;
using ProEdit.Layout;

namespace ProEdit.Editing;

public static class DocumentFieldUpdater
{
    public static bool UpdateFields(Document document, DocumentLayout layout, TextRange? range, bool pageNumbersOnly)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);

        var context = new FieldUpdateContext(document, layout);
        var updated = false;
        for (var paragraphIndex = 0; paragraphIndex < context.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = context.Paragraphs[paragraphIndex];
            var spans = BuildFieldSpans(paragraph);
            if (spans.Count == 0)
            {
                continue;
            }

            for (var i = spans.Count - 1; i >= 0; i--)
            {
                var span = spans[i];
                if (range.HasValue && !IntersectsRange(paragraphIndex, span, range.Value))
                {
                    continue;
                }

                if (TryBuildFieldResult(context, span, paragraph, paragraphIndex, pageNumbersOnly, out var resultInline))
                {
                    ReplaceFieldResult(paragraph, span, resultInline);
                    updated = true;
                }
            }
        }

        return updated;
    }

    public static bool UpdateFieldAtPosition(Document document, DocumentLayout layout, TextPosition position, bool pageNumbersOnly)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);

        var context = new FieldUpdateContext(document, layout);
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= context.Paragraphs.Count)
        {
            return false;
        }

        var paragraph = context.Paragraphs[position.ParagraphIndex];
        var spans = BuildFieldSpans(paragraph);
        if (spans.Count == 0)
        {
            return false;
        }

        foreach (var span in spans)
        {
            if (position.Offset < span.StartOffset || position.Offset > span.EndOffset)
            {
                continue;
            }

            if (TryBuildFieldResult(context, span, paragraph, position.ParagraphIndex, pageNumbersOnly, out var resultInline))
            {
                ReplaceFieldResult(paragraph, span, resultInline);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryBuildFieldResult(
        FieldUpdateContext context,
        FieldSpan span,
        ParagraphBlock paragraph,
        int paragraphIndex,
        bool pageNumbersOnly,
        out Inline? resultInline)
    {
        resultInline = null;
        var fieldStart = span.Start;
        if (fieldStart.IsLocked)
        {
            return false;
        }

        var definition = fieldStart.Definition ??= FieldInstructionParser.Parse(fieldStart.Instruction);
        if (definition is null)
        {
            return false;
        }

        if (pageNumbersOnly && !IsPageNumberField(definition))
        {
            return false;
        }

        if (!TryResolveFieldText(context, definition, span, paragraph, paragraphIndex, out var text, out var hyperlink))
        {
            return false;
        }

        var (style, styleId) = ResolveFieldResultStyle(paragraph, span);
        var run = new RunInline(text, style)
        {
            StyleId = styleId,
            Hyperlink = hyperlink
        };

        fieldStart.IsDirty = false;
        resultInline = run;
        return true;
    }

    private static bool TryResolveFieldText(
        FieldUpdateContext context,
        FieldDefinition definition,
        FieldSpan span,
        ParagraphBlock paragraph,
        int paragraphIndex,
        out string text,
        out HyperlinkInfo? hyperlink)
    {
        text = string.Empty;
        hyperlink = null;

        switch (definition.Kind)
        {
            case FieldKind.Page:
                return context.TryGetPageNumberText(new TextPosition(paragraphIndex, span.StartOffset), out text);
            case FieldKind.NumPages:
                return context.TryGetTotalPagesText(new TextPosition(paragraphIndex, span.StartOffset), out text);
            case FieldKind.Date:
                return TryFormatDateTimeField(definition, context.Now, false, out text);
            case FieldKind.Time:
                return TryFormatDateTimeField(definition, context.Now, true, out text);
            case FieldKind.DocProperty:
            {
                var name = GetFirstFieldArgument(definition);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                return context.TryGetDocProperty(name, out text);
            }
            case FieldKind.Ref:
            {
                var target = GetFirstFieldArgument(definition);
                if (string.IsNullOrWhiteSpace(target))
                {
                    return false;
                }

                if (definition.Name.Equals("PAGEREF", StringComparison.OrdinalIgnoreCase))
                {
                    return context.TryGetBookmarkPageText(target, out text);
                }

                return context.TryGetBookmarkText(target, out text);
            }
            case FieldKind.StyleRef:
                return context.TryGetStyleRefText(definition, new TextPosition(paragraphIndex, span.StartOffset), out text);
            case FieldKind.Seq:
                return context.TryGetSequenceText(span.Start, definition, out text);
            case FieldKind.Citation:
                return context.TryGetCitationText(definition, out text);
            case FieldKind.Bibliography:
                return context.TryGetBibliographyText(out text);
            case FieldKind.Index:
                return context.TryGetIndexText(span.Start, definition, out text);
            case FieldKind.Hyperlink:
            {
                text = GetFieldResultText(paragraph, span);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = ResolveHyperlinkDisplay(definition);
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                if (TryResolveHyperlinkInfo(definition, out var resolved))
                {
                    hyperlink = resolved;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsPageNumberField(FieldDefinition definition)
    {
        if (definition.Kind is FieldKind.Page or FieldKind.NumPages)
        {
            return true;
        }

        return definition.Kind == FieldKind.Ref
               && definition.Name.Equals("PAGEREF", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFieldResultText(ParagraphBlock paragraph, FieldSpan span)
    {
        var inlines = paragraph.Inlines;
        var resultStart = span.SeparatorIndex >= 0 ? span.SeparatorIndex + 1 : span.StartIndex + 1;
        var resultEnd = span.EndIndex;
        if (resultStart >= resultEnd || resultStart < 0 || resultStart >= inlines.Count)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = resultStart; i < resultEnd && i < inlines.Count; i++)
        {
            if (inlines[i] is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString();
    }

    private static string ResolveHyperlinkDisplay(FieldDefinition definition)
    {
        var anchor = GetFieldSwitch(definition, "\\l");
        if (!string.IsNullOrWhiteSpace(anchor))
        {
            return anchor.Trim();
        }

        var target = GetFirstFieldArgument(definition);
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        target = target.Trim();
        if (target.Length > 0 && target[0] == '#')
        {
            target = target.TrimStart('#');
        }

        return target;
    }

    private static bool TryResolveHyperlinkInfo(FieldDefinition definition, out HyperlinkInfo? hyperlink)
    {
        hyperlink = null;
        var target = GetFirstFieldArgument(definition);
        var anchor = GetFieldSwitch(definition, "\\l");
        var tooltip = GetFieldSwitch(definition, "\\o");

        if (!string.IsNullOrWhiteSpace(anchor))
        {
            anchor = anchor.Trim();
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            target = target.Trim();
        }

        if (!string.IsNullOrWhiteSpace(target) && target.StartsWith("#", StringComparison.Ordinal))
        {
            anchor = target.TrimStart('#');
            target = string.Empty;
        }

        string? uri = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                uri = target;
            }
            else if (string.IsNullOrWhiteSpace(anchor))
            {
                anchor = target;
            }
        }

        if (string.IsNullOrWhiteSpace(uri) && string.IsNullOrWhiteSpace(anchor))
        {
            return false;
        }

        hyperlink = new HyperlinkInfo(uri, anchor, tooltip);
        return !hyperlink.IsEmpty;
    }

    private static bool TryFormatDateTimeField(FieldDefinition definition, DateTimeOffset now, bool isTime, out string text)
    {
        text = string.Empty;
        var format = GetFieldSwitch(definition, "\\@");
        var culture = CultureInfo.CurrentCulture;
        var value = now.LocalDateTime;

        if (!string.IsNullOrWhiteSpace(format))
        {
            try
            {
                text = value.ToString(format, culture);
                return true;
            }
            catch
            {
            }
        }

        text = isTime ? value.ToString("T", culture) : value.ToString("d", culture);
        return true;
    }

    private static (TextStyleProperties? Style, string? StyleId) ResolveFieldResultStyle(ParagraphBlock paragraph, FieldSpan span)
    {
        var inlines = paragraph.Inlines;
        var resultStart = span.SeparatorIndex >= 0 ? span.SeparatorIndex + 1 : span.StartIndex + 1;
        var resultEnd = span.EndIndex;
        for (var i = resultStart; i < resultEnd && i < inlines.Count; i++)
        {
            if (inlines[i] is RunInline run)
            {
                return (run.Style?.Clone(), run.StyleId);
            }
        }

        return (null, null);
    }

    private static void ReplaceFieldResult(ParagraphBlock paragraph, FieldSpan span, Inline? resultInline)
    {
        var inlines = paragraph.Inlines;
        var separatorIndex = span.SeparatorIndex;
        var endIndex = span.EndIndex;
        if (separatorIndex < 0)
        {
            separatorIndex = span.StartIndex + 1;
            inlines.Insert(separatorIndex, new FieldSeparatorInline());
            endIndex++;
        }

        var resultStart = separatorIndex + 1;
        var removeCount = Math.Max(0, endIndex - resultStart);
        if (removeCount > 0 && resultStart < inlines.Count)
        {
            inlines.RemoveRange(resultStart, Math.Min(removeCount, inlines.Count - resultStart));
        }

        if (resultInline is not null)
        {
            inlines.Insert(resultStart, resultInline);
        }
    }

    private static bool IntersectsRange(int paragraphIndex, FieldSpan span, TextRange selection)
    {
        var fieldStart = new TextPosition(paragraphIndex, span.StartOffset);
        var fieldEnd = new TextPosition(paragraphIndex, span.EndOffset);
        var normalized = selection.Normalize();
        var start = normalized.Start;
        var end = normalized.End;

        if (ComparePositions(fieldEnd, start) < 0)
        {
            return false;
        }

        if (ComparePositions(fieldStart, end) > 0)
        {
            return false;
        }

        return true;
    }

    private static int ComparePositions(TextPosition left, TextPosition right)
    {
        var paragraphCompare = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return left.Offset.CompareTo(right.Offset);
    }

    private static List<FieldSpan> BuildFieldSpans(ParagraphBlock paragraph)
    {
        var spans = new List<FieldSpan>();
        if (paragraph.Inlines.Count == 0)
        {
            return spans;
        }

        var inlines = paragraph.Inlines;
        var offsets = new int[inlines.Count];
        var offset = 0;
        for (var i = 0; i < inlines.Count; i++)
        {
            offsets[i] = offset;
            offset += DocumentEditHelpers.GetInlineLength(inlines[i]);
        }

        for (var i = 0; i < inlines.Count; i++)
        {
            if (inlines[i] is not FieldStartInline start)
            {
                continue;
            }

            var separatorIndex = -1;
            var endIndex = -1;
            for (var j = i + 1; j < inlines.Count; j++)
            {
                if (inlines[j] is FieldSeparatorInline && separatorIndex < 0)
                {
                    separatorIndex = j;
                    continue;
                }

                if (inlines[j] is FieldEndInline)
                {
                    endIndex = j;
                    break;
                }
            }

            if (endIndex <= i)
            {
                continue;
            }

            spans.Add(new FieldSpan(start, i, separatorIndex, endIndex, offsets[i], offsets[endIndex]));
            i = endIndex;
        }

        return spans;
    }

    private static string? GetFirstFieldArgument(FieldDefinition definition)
    {
        return definition.Arguments.Count > 0 ? definition.Arguments[0].Value : null;
    }

    private static string? GetFieldSwitch(FieldDefinition definition, string name)
    {
        foreach (var fieldSwitch in definition.Switches)
        {
            var switchName = fieldSwitch.Name;
            if (switchName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return fieldSwitch.Value;
            }

            if (switchName.Length > 0 && switchName[0] == '\\' && name.Length > 0 && name[0] != '\\')
            {
                if (switchName.Substring(1).Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return fieldSwitch.Value;
                }
            }
        }

        return null;
    }

    private sealed class FieldUpdateContext
    {
        private readonly Document _document;
        private readonly DocumentLayout _layout;
        private readonly Dictionary<string, TextPosition> _bookmarkStartPositions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, BookmarkRangeState> _openBookmarkRanges = new();
        private readonly Dictionary<string, TextRange> _bookmarkRanges = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<FieldStartInline, string> _sequenceTexts = new();
        private readonly List<IndexEntry> _indexEntries = new();
        private readonly string[] _pageNumberTexts;
        private readonly string[] _totalPagesTexts;

        public FieldUpdateContext(Document document, DocumentLayout layout)
        {
            _document = document;
            _layout = layout;
            Paragraphs = DocumentEditHelpers.BuildParagraphList(document);
            _pageNumberTexts = BuildPageNumberTexts(layout.Pages, layout.PageSections);
            _totalPagesTexts = BuildTotalPagesTexts(layout.Pages, layout.PageSections, Math.Max(1, layout.Pages.Count));
            Now = DateTimeOffset.Now;
            ScanParagraphs();
        }

        public List<ParagraphBlock> Paragraphs { get; }

        public DateTimeOffset Now { get; }

        public bool TryGetPageNumberText(TextPosition position, out string text)
        {
            if (TryGetPageIndex(position, out var pageIndex)
                && pageIndex >= 0
                && pageIndex < _pageNumberTexts.Length)
            {
                text = _pageNumberTexts[pageIndex];
                return !string.IsNullOrEmpty(text);
            }

            text = string.Empty;
            return false;
        }

        public bool TryGetTotalPagesText(TextPosition position, out string text)
        {
            if (TryGetPageIndex(position, out var pageIndex)
                && pageIndex >= 0
                && pageIndex < _totalPagesTexts.Length)
            {
                text = _totalPagesTexts[pageIndex];
                return !string.IsNullOrEmpty(text);
            }

            text = string.Empty;
            return false;
        }

        public bool TryGetBookmarkPageText(string name, out string text)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                text = string.Empty;
                return false;
            }

            if (!_bookmarkStartPositions.TryGetValue(name, out var position))
            {
                text = string.Empty;
                return false;
            }

            return TryGetPageNumberText(position, out text);
        }

        public bool TryGetBookmarkText(string name, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!_bookmarkRanges.TryGetValue(name, out var range))
            {
                return false;
            }

            range = range.Normalize();
            if (range.Start.ParagraphIndex < 0 || range.End.ParagraphIndex < 0)
            {
                return false;
            }

            var builder = new StringBuilder();
            for (var paragraphIndex = range.Start.ParagraphIndex; paragraphIndex <= range.End.ParagraphIndex; paragraphIndex++)
            {
                if (!TryGetParagraphText(paragraphIndex, out var paragraphText))
                {
                    continue;
                }

                var startOffset = paragraphIndex == range.Start.ParagraphIndex ? range.Start.Offset : 0;
                var endOffset = paragraphIndex == range.End.ParagraphIndex ? range.End.Offset : paragraphText.Length;
                startOffset = Math.Clamp(startOffset, 0, paragraphText.Length);
                endOffset = Math.Clamp(endOffset, startOffset, paragraphText.Length);
                if (endOffset <= startOffset)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(paragraphText, startOffset, endOffset - startOffset);
            }

            text = builder.ToString();
            return text.Length > 0;
        }

        public bool TryGetDocProperty(string name, out string text)
        {
            if (_document.Properties.TryGetValue(name, out text))
            {
                return !string.IsNullOrEmpty(text);
            }

            text = string.Empty;
            return false;
        }

        public bool TryGetSequenceText(FieldStartInline start, FieldDefinition definition, out string text)
        {
            if (_sequenceTexts.TryGetValue(start, out var cached) && !string.IsNullOrEmpty(cached))
            {
                text = cached;
                return true;
            }

            text = string.Empty;
            return false;
        }

        public bool TryGetStyleRefText(FieldDefinition definition, TextPosition position, out string text)
        {
            text = string.Empty;
            var styleName = GetFirstFieldArgument(definition);
            if (string.IsNullOrWhiteSpace(styleName))
            {
                return false;
            }

            styleName = styleName.Trim();
            if (styleName.Length == 0)
            {
                return false;
            }

            var startIndex = Math.Clamp(position.ParagraphIndex, 0, Math.Max(0, Paragraphs.Count - 1));
            if (TryResolveStyleRefText(styleName, startIndex, -1, out text))
            {
                return true;
            }

            if (TryResolveStyleRefText(styleName, startIndex + 1, 1, out text))
            {
                return true;
            }

            text = string.Empty;
            return false;
        }

        public bool TryGetCitationText(FieldDefinition definition, out string text)
        {
            text = string.Empty;
            var tag = GetFirstFieldArgument(definition);
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            tag = tag.Trim();
            text = BuildCitationDisplay(tag);
            return !string.IsNullOrEmpty(text);
        }

        public bool TryGetBibliographyText(out string text)
        {
            text = BuildBibliographyDisplay();
            return !string.IsNullOrEmpty(text);
        }

        public bool TryGetIndexText(FieldStartInline start, FieldDefinition definition, out string text)
        {
            text = string.Empty;
            if (_indexEntries.Count == 0)
            {
                return false;
            }

            var entrySeparator = GetFieldSwitch(definition, "\\e");
            if (string.IsNullOrEmpty(entrySeparator))
            {
                entrySeparator = "\t";
            }

            var pageSeparator = GetFieldSwitch(definition, "\\p");
            if (string.IsNullOrEmpty(pageSeparator))
            {
                pageSeparator = ", ";
            }

            var grouped = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _indexEntries)
            {
                if (!grouped.TryGetValue(entry.Text, out var list))
                {
                    list = new List<int>();
                    grouped[entry.Text] = list;
                }

                if (TryGetPageIndex(entry.Position, out var pageIndex))
                {
                    list.Add(pageIndex);
                }
            }

            var keys = grouped.Keys.ToList();
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(key);
                var pages = grouped[key];
                if (pages.Count == 0)
                {
                    continue;
                }

                pages.Sort();
                var first = true;
                var lastPage = -1;
                for (var i = 0; i < pages.Count; i++)
                {
                    var pageIndex = pages[i];
                    if (pageIndex == lastPage)
                    {
                        continue;
                    }

                    if (pageIndex < 0 || pageIndex >= _pageNumberTexts.Length)
                    {
                        continue;
                    }

                    if (first)
                    {
                        builder.Append(entrySeparator);
                        first = false;
                    }
                    else
                    {
                        builder.Append(pageSeparator);
                    }

                    builder.Append(_pageNumberTexts[pageIndex]);
                    lastPage = pageIndex;
                }
            }

            text = builder.ToString();
            return !string.IsNullOrEmpty(text);
        }

        private void ScanParagraphs()
        {
            var sequenceCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var paragraphIndex = 0; paragraphIndex < Paragraphs.Count; paragraphIndex++)
            {
                var paragraph = Paragraphs[paragraphIndex];
                ScanParagraph(paragraph, paragraphIndex, sequenceCounters);
            }
        }

        private void ScanParagraph(ParagraphBlock paragraph, int paragraphIndex, Dictionary<string, int> sequenceCounters)
        {
            if (paragraph.Inlines.Count == 0)
            {
                return;
            }

            var offset = 0;
            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case FieldStartInline fieldStart:
                    {
                        var definition = fieldStart.Definition ?? FieldInstructionParser.Parse(fieldStart.Instruction);
                        fieldStart.Definition = definition;
                        if (definition is not null)
                        {
                            if (definition.Kind == FieldKind.Seq)
                            {
                                if (TryBuildSequenceText(definition, sequenceCounters, out var sequenceText))
                                {
                                    _sequenceTexts[fieldStart] = sequenceText;
                                }
                            }
                            else if (definition.Kind == FieldKind.IndexEntry)
                            {
                                if (TryParseIndexEntry(definition, out var indexText))
                                {
                                    var position = new TextPosition(paragraphIndex, offset);
                                    _indexEntries.Add(new IndexEntry(position, indexText));
                                }
                            }
                        }

                        break;
                    }
                    case BookmarkStartInline bookmarkStart:
                    {
                        if (!string.IsNullOrWhiteSpace(bookmarkStart.Name))
                        {
                            var position = new TextPosition(paragraphIndex, offset);
                            if (!_bookmarkStartPositions.ContainsKey(bookmarkStart.Name))
                            {
                                _bookmarkStartPositions[bookmarkStart.Name] = position;
                            }

                            _openBookmarkRanges[bookmarkStart.Id] = new BookmarkRangeState(bookmarkStart.Name, position);
                        }

                        break;
                    }
                    case BookmarkEndInline bookmarkEnd:
                    {
                        if (_openBookmarkRanges.TryGetValue(bookmarkEnd.Id, out var state))
                        {
                            _openBookmarkRanges.Remove(bookmarkEnd.Id);
                            if (!string.IsNullOrWhiteSpace(state.Name) && !_bookmarkRanges.ContainsKey(state.Name))
                            {
                                _bookmarkRanges[state.Name] = new TextRange(state.Start, new TextPosition(paragraphIndex, offset));
                            }
                        }

                        break;
                    }
                }

                offset += DocumentEditHelpers.GetInlineLength(inline);
            }
        }

        private bool TryGetParagraphText(int paragraphIndex, out string text)
        {
            if (paragraphIndex < 0 || paragraphIndex >= Paragraphs.Count)
            {
                text = string.Empty;
                return false;
            }

            text = DocumentEditHelpers.GetParagraphText(Paragraphs[paragraphIndex]);
            return !string.IsNullOrEmpty(text);
        }

        private bool TryResolveStyleRefText(string styleName, int startIndex, int step, out string text)
        {
            text = string.Empty;
            if (step == 0)
            {
                return false;
            }

            var index = startIndex;
            while (index >= 0 && index < Paragraphs.Count)
            {
                var paragraph = Paragraphs[index];
                if (IsParagraphStyleMatch(paragraph, styleName))
                {
                    if (TryGetParagraphText(index, out var paragraphText))
                    {
                        paragraphText = paragraphText.Trim();
                        if (paragraphText.Length > 0)
                        {
                            text = paragraphText;
                            return true;
                        }
                    }
                }

                index += step;
            }

            return false;
        }

        private bool IsParagraphStyleMatch(ParagraphBlock paragraph, string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(paragraph.StyleId)
                && string.Equals(paragraph.StyleId, styleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(paragraph.StyleId)
                && _document.Styles.ParagraphStyles.TryGetValue(paragraph.StyleId, out var style))
            {
                if (string.Equals(style.Name, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPageIndex(TextPosition position, out int pageIndex)
        {
            pageIndex = -1;
            if (position.ParagraphIndex < 0)
            {
                return false;
            }

            if (!_layout.ParagraphLineRanges.TryGetValue(position.ParagraphIndex, out var range))
            {
                return false;
            }

            if (_layout.Lines.Count == 0)
            {
                return false;
            }

            var start = Math.Clamp(range.Start, 0, _layout.Lines.Count - 1);
            var end = Math.Clamp(range.End, start, _layout.Lines.Count);
            for (var i = start; i < end; i++)
            {
                var line = _layout.Lines[i];
                var lineEnd = line.StartOffset + line.Length;
                if (position.Offset >= line.StartOffset && position.Offset <= lineEnd)
                {
                    pageIndex = _layout.LineIndex.GetPageForLine(i);
                    return pageIndex >= 0;
                }
            }

            if (start < end)
            {
                pageIndex = _layout.LineIndex.GetPageForLine(start);
                return pageIndex >= 0;
            }

            return false;
        }

        private string BuildCitationDisplay(string tag)
        {
            var source = _document.CitationSources.FindByTag(tag);
            if (source is null)
            {
                return tag;
            }

            var author = source.GetField("Author");
            var year = source.GetField("Year");
            var title = source.GetField("Title");
            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(year))
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}, {1}", author, year);
            }

            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}, {1}", author, title);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            return tag;
        }

        private string BuildBibliographyDisplay()
        {
            var sources = _document.CitationSources.Sources;
            if (sources.Count == 0)
            {
                return "Bibliography";
            }

            var entries = new List<string>(sources.Count);
            foreach (var source in sources)
            {
                var author = source.GetField("Author");
                var year = source.GetField("Year");
                var title = source.GetField("Title");
                if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(year))
                {
                    entries.Add(string.Format(CultureInfo.CurrentCulture, "{0} ({1})", author, year));
                }
                else if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
                {
                    entries.Add(string.Format(CultureInfo.CurrentCulture, "{0}. {1}.", author, title));
                }
                else if (!string.IsNullOrWhiteSpace(title))
                {
                    entries.Add(title);
                }
                else if (!string.IsNullOrWhiteSpace(source.Tag))
                {
                    entries.Add(source.Tag);
                }
            }

            return entries.Count == 0 ? "Bibliography" : string.Join("; ", entries);
        }
    }

    private static string[] BuildPageNumberTexts(
        IReadOnlyList<PageLayout> pages,
        IReadOnlyList<PageSectionSettings> pageSections)
    {
        if (pages.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new string[pages.Count];
        var currentNumber = 1;
        var currentSectionIndex = -1;
        var currentFormat = PageNumberFormat.Decimal;

        for (var i = 0; i < pages.Count; i++)
        {
            var section = pageSections[Math.Clamp(i, 0, pageSections.Count - 1)];
            var isFirstPageOfSection = currentSectionIndex != section.SectionIndex;
            if (isFirstPageOfSection)
            {
                currentSectionIndex = section.SectionIndex;
                var numbering = section.PageNumbering;
                if (numbering?.Start.HasValue == true)
                {
                    currentNumber = Math.Max(1, numbering.Start.Value);
                }

                if (numbering?.Format.HasValue == true)
                {
                    currentFormat = numbering.Format.Value;
                }
            }

            result[i] = FormatPageNumber(currentNumber, currentFormat);
            currentNumber++;
        }

        return result;
    }

    private static string[] BuildTotalPagesTexts(
        IReadOnlyList<PageLayout> pages,
        IReadOnlyList<PageSectionSettings> pageSections,
        int totalPages)
    {
        if (pages.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new string[pages.Count];
        var currentSectionIndex = -1;
        var currentFormat = PageNumberFormat.Decimal;

        for (var i = 0; i < pages.Count; i++)
        {
            var section = pageSections[Math.Clamp(i, 0, pageSections.Count - 1)];
            var isFirstPageOfSection = currentSectionIndex != section.SectionIndex;
            if (isFirstPageOfSection)
            {
                currentSectionIndex = section.SectionIndex;
                if (section.PageNumbering?.Format.HasValue == true)
                {
                    currentFormat = section.PageNumbering.Format.Value;
                }
            }

            result[i] = FormatPageNumber(totalPages, currentFormat);
        }

        return result;
    }

    private static string FormatPageNumber(int value, PageNumberFormat format)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return format switch
        {
            PageNumberFormat.UpperRoman => ToRoman(value, true),
            PageNumberFormat.LowerRoman => ToRoman(value, false),
            PageNumberFormat.UpperLetter => ToAlpha(value, true),
            PageNumberFormat.LowerLetter => ToAlpha(value, false),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string ToAlpha(int value, bool upper)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var builder = new StringBuilder();
        var remaining = value - 1;
        while (remaining >= 0)
        {
            var rem = remaining % 26;
            var ch = (char)('A' + rem);
            builder.Insert(0, ch);
            remaining = remaining / 26 - 1;
        }

        var result = builder.ToString();
        return upper ? result : result.ToLowerInvariant();
    }

    private static string ToRoman(int value, bool upper)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var map = new (int Value, string Symbol)[]
        {
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I")
        };

        var remaining = value;
        var builder = new StringBuilder();
        foreach (var (mapValue, mapSymbol) in map)
        {
            while (remaining >= mapValue)
            {
                builder.Append(mapSymbol);
                remaining -= mapValue;
            }
        }

        var result = builder.ToString();
        return upper ? result : result.ToLowerInvariant();
    }

    private static bool TryBuildSequenceText(
        FieldDefinition definition,
        Dictionary<string, int> sequenceCounters,
        out string text)
    {
        text = string.Empty;
        var label = GetFirstFieldArgument(definition);
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        label = label.Trim();
        if (TryGetFieldSwitchInt(definition, "\\r", out var resetValue))
        {
            sequenceCounters[label] = resetValue;
            text = resetValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (HasFieldSwitch(definition, "\\c"))
        {
            if (!sequenceCounters.TryGetValue(label, out var current))
            {
                current = 0;
            }

            text = current.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var nextValue = sequenceCounters.TryGetValue(label, out var value) ? value + 1 : 1;
        sequenceCounters[label] = nextValue;
        text = nextValue.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryParseIndexEntry(FieldDefinition definition, out string text)
    {
        text = GetFirstFieldArgument(definition) ?? string.Empty;
        text = text.Trim();
        return text.Length > 0;
    }

    private static bool TryGetFieldSwitchInt(FieldDefinition definition, string name, out int value)
    {
        value = 0;
        var raw = GetFieldSwitch(definition, name);
        return !string.IsNullOrWhiteSpace(raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool HasFieldSwitch(FieldDefinition definition, string name)
    {
        foreach (var fieldSwitch in definition.Switches)
        {
            var switchName = fieldSwitch.Name;
            if (switchName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (switchName.Length > 0 && switchName[0] == '\\' && name.Length > 0 && name[0] != '\\')
            {
                if (switchName.AsSpan(1).Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (name.Length > 0 && name[0] == '\\' && switchName.Length > 0 && switchName[0] != '\\')
            {
                if (name.AsSpan(1).Equals(switchName.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly record struct FieldSpan(
        FieldStartInline Start,
        int StartIndex,
        int SeparatorIndex,
        int EndIndex,
        int StartOffset,
        int EndOffset);

    private readonly record struct IndexEntry(TextPosition Position, string Text);

    private readonly record struct BookmarkRangeState(string Name, TextPosition Start);
}
