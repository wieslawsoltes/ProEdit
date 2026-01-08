using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed class DocumentLayouter
{
    private readonly record struct LayoutBlockRule(
        Func<Block, bool> Matches,
        Action<Block, int> Apply)
    {
        public bool TryApply(Block block, int blockIndex)
        {
            if (!Matches(block))
            {
                return false;
            }

            Apply(block, blockIndex);
            return true;
        }

        public static LayoutBlockRule For<TBlock>(Action<TBlock, int> apply)
            where TBlock : Block
        {
            return new LayoutBlockRule(
                static block => block is TBlock,
                (block, blockIndex) => apply((TBlock)block, blockIndex));
        }
    }

    private readonly record struct LayoutPass(Action Apply)
    {
        public void Run()
        {
            Apply();
        }
    }

    private readonly record struct ParagraphLayoutPlan(
        ParagraphBlock Paragraph,
        ParagraphProperties Properties,
        TextStyle ParagraphStyle,
        string? Prefix,
        float ListIndent,
        float PrefixWidth,
        float SpacingBefore,
        float SpacingAfter,
        float IndentLeft,
        float IndentRight,
        float FirstLineIndent,
        bool KeepWithNext,
        bool KeepLinesTogether,
        bool WidowControl,
        float NextBlockMinHeight,
        bool CanReflow);

    private readonly record struct TableLayoutPlan(
        TableBlock Table,
        TableProperties ResolvedProperties,
        TableLayoutData Data,
        float TableX);

    public DocumentLayout Layout(Document document, LayoutSettings settings, ITextMeasurer measurer)
    {
        return LayoutInternal(document, settings, measurer, null, null);
    }

    public DocumentLayout Layout(Document document, LayoutSettings settings, ITextMeasurer measurer, DocumentLayout? previousLayout, int? dirtyParagraphIndex)
    {
        return LayoutInternal(document, settings, measurer, previousLayout, dirtyParagraphIndex);
    }

    private DocumentLayout LayoutInternal(
        Document document,
        LayoutSettings settings,
        ITextMeasurer measurer,
        DocumentLayout? previousLayout,
        int? dirtyParagraphIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(measurer);

        var style = document.DefaultTextStyle;
        var metrics = measurer.MeasureText("Mg", style);
        var lineHeight = MathF.Max(1f, metrics.Height);
        var ascent = metrics.Ascent;

        var lines = new List<LayoutLine>();
        var tables = new List<TableLayout>();
        var pages = new List<PageLayout>();
        var pageSections = new List<PageSectionSettings>();
        var headerFooters = new List<HeaderFooterLayout>();
        var wrapFloatingObjects = new List<FloatingLayoutObject>();
        var breakMarkers = new List<BreakMarker>();
        var linePageIndices = new List<int>();
        var paragraphLineRanges = new Dictionary<int, LineRange>();
        var paragraphSpacingBefore = new Dictionary<int, float>();
        var listState = new ListNumberingState(document);
        var styleResolver = new DocumentStyleResolver(document);
        var scanState = new ParagraphScanState();
        scanState.Scan(document);
        var paragraphSectionIndices = BuildParagraphSectionIndices(document);
        var sectionSettingsByIndex = BuildSectionSettingsByIndex(document, settings);
        var footnotesByPage = new Dictionary<int, HashSet<int>>();
        var endnotesByPage = new Dictionary<int, HashSet<int>>();

        var currentSectionIndex = 0;
        var sectionSettings = sectionSettingsByIndex.TryGetValue(currentSectionIndex, out var initialSection)
            ? initialSection
            : PageSectionSettings.FromSettings(settings, document.GetSection(0).Properties, currentSectionIndex, document.MirrorMargins, document.GutterAtTop);
        var pageWidth = 0f;
        var pageHeight = 0f;
        var pageX = 0f;
        var pageY = settings.UsePagination ? settings.PageGap : 0f;
        var pageIndex = 0;
        var pageSettings = sectionSettings.ResolveForPage(pageIndex);
        var pageContentWidth = 0f;
        var columnWidth = 0f;
        var columnIndex = 0;
        var columnCount = 1;
        var columnGap = 0f;
        var columnOffsets = Array.Empty<float>();
        var columnWidths = Array.Empty<float>();
        var columnX = 0f;
        var contentTop = 0f;
        var contentBottom = 0f;
        var columnTop = 0f;
        var cursorY = 0f;
        var marginLeft = 0f;
        var marginRight = 0f;
        var marginTop = 0f;
        var marginBottom = 0f;
        var paragraphIndex = 0;
        var blockStartIndex = 0;

        IReadOnlyList<Block> blocks = document.Blocks.Count == 0
            ? new Block[] { new ParagraphBlock() }
            : document.Blocks;

        void UpdateColumnMetrics()
        {
            if (columnWidths.Length == 0)
            {
                columnWidth = pageContentWidth;
                columnX = pageX + marginLeft;
                return;
            }

            var safeIndex = Math.Clamp(columnIndex, 0, columnWidths.Length - 1);
            columnWidth = columnWidths[safeIndex];
            columnX = pageX + marginLeft + columnOffsets[safeIndex];
        }

        void ApplySectionSettings(PageSectionSettings section, bool preserveColumn, float? resumeY = null)
        {
            pageSettings = section.ResolveForPage(pageIndex);
            marginLeft = pageSettings.MarginLeft;
            marginRight = pageSettings.MarginRight;
            marginTop = pageSettings.MarginTop;
            marginBottom = pageSettings.MarginBottom;
            pageWidth = settings.UsePagination ? pageSettings.PageWidth : settings.ViewportWidth;
            pageHeight = settings.UsePagination ? pageSettings.PageHeight : MathF.Max(settings.ViewportHeight, 1f);
            pageX = settings.UsePagination ? MathF.Max(0f, (settings.ViewportWidth - pageWidth) / 2f) : 0f;
            pageContentWidth = MathF.Max(1f, pageWidth - marginLeft - marginRight);
            contentTop = pageY + marginTop;
            contentBottom = pageY + pageHeight - marginBottom;

            columnCount = Math.Max(1, section.ColumnCount);
            columnGap = MathF.Max(0f, section.ColumnGap);
            columnWidths = ResolveSectionColumnWidths(section, pageContentWidth, columnGap);
            columnOffsets = BuildColumnOffsets(columnWidths, columnGap);
            columnIndex = preserveColumn ? Math.Clamp(columnIndex, 0, Math.Max(0, columnCount - 1)) : 0;
            UpdateColumnMetrics();
            var top = resumeY ?? contentTop;
            if (top < contentTop)
            {
                top = contentTop;
            }

            columnTop = top;
            cursorY = top;
        }

        void AddPage()
        {
            var bounds = new DocRect(pageX, pageY, pageWidth, pageHeight);
            var contentBounds = new DocRect(pageX + marginLeft, pageY + marginTop, pageContentWidth, pageHeight - marginTop - marginBottom);
            pages.Add(new PageLayout(pageIndex, bounds, contentBounds));
            pageSections.Add(pageSettings);
        }

        void StartNewPage(PageSectionSettings? newSection = null)
        {
            pageIndex++;
            pageY += pageHeight + settings.PageGap;
            if (newSection is not null)
            {
                sectionSettings = newSection;
                currentSectionIndex = newSection.SectionIndex;
            }

            ApplySectionSettings(sectionSettings, false);
            AddPage();
        }

        void AddBreakMarker(BreakMarkerKind kind, string label)
        {
            var padding = MathF.Max(4f, lineHeight * 0.3f);
            var minY = contentTop + padding;
            var maxY = contentBottom - padding;
            var markerY = Math.Clamp(cursorY, minY, maxY);
            var width = MathF.Max(1f, pageContentWidth);
            var x = pageX + marginLeft;
            breakMarkers.Add(new BreakMarker(kind, pageIndex, x, width, markerY, label));
        }

        static string FormatSectionBreakLabel(SectionBreakType breakType)
        {
            return breakType switch
            {
                SectionBreakType.Continuous => "Section Break (Continuous)",
                SectionBreakType.EvenPage => "Section Break (Even Page)",
                SectionBreakType.OddPage => "Section Break (Odd Page)",
                SectionBreakType.NextColumn => "Section Break (Next Column)",
                _ => "Section Break (Next Page)"
            };
        }

        void StartNewColumnOrPage()
        {
            if (columnCount > 1 && columnIndex + 1 < columnCount)
            {
                columnIndex++;
                UpdateColumnMetrics();
                cursorY = columnTop;
                return;
            }

            StartNewPage();
        }

        int ResolveColumnIndex(float lineX)
        {
            if (columnOffsets.Length == 0)
            {
                return 0;
            }

            var relativeX = lineX - (pageX + marginLeft);
            for (var i = columnOffsets.Length - 1; i >= 0; i--)
            {
                if (relativeX >= columnOffsets[i] - 0.5f)
                {
                    return i;
                }
            }

            return 0;
        }

        void ApplySectionBreak(SectionBreakType breakType, PageSectionSettings nextSection)
        {
            if (breakType == SectionBreakType.Continuous)
            {
                var resumeY = cursorY;
                sectionSettings = nextSection;
                currentSectionIndex = nextSection.SectionIndex;
                ApplySectionSettings(sectionSettings, true, MathF.Max(resumeY, contentTop));
                return;
            }

            if (breakType == SectionBreakType.NextColumn)
            {
                sectionSettings = nextSection;
                currentSectionIndex = nextSection.SectionIndex;
                ApplySectionSettings(sectionSettings, true, cursorY);
                StartNewColumnOrPage();
                return;
            }

            if (breakType is SectionBreakType.EvenPage or SectionBreakType.OddPage)
            {
                var nextPageIndex = pageIndex + 1;
                var nextIsOdd = (nextPageIndex + 1) % 2 == 1;
                var shouldBeOdd = breakType == SectionBreakType.OddPage;
                if (nextIsOdd != shouldBeOdd)
                {
                    StartNewPage();
                }
            }

            StartNewPage(nextSection);
        }

        void AddLine(LayoutLine line)
        {
            lines.Add(line);
            linePageIndices.Add(pageIndex);
            RegisterNoteReferences(line);
        }

        void RegisterNoteReferences(LayoutLine line)
        {
            if (line.ParagraphIndex < 0)
            {
                return;
            }

            if (!scanState.NoteReferencesByParagraph.TryGetValue(line.ParagraphIndex, out var references))
            {
                return;
            }

            var lineEnd = line.StartOffset + line.Length;
            foreach (var reference in references)
            {
                if (reference.Offset < line.StartOffset || reference.Offset >= lineEnd)
                {
                    continue;
                }

                var map = reference.Kind == NoteKind.Footnote ? footnotesByPage : endnotesByPage;
                if (!map.TryGetValue(pageIndex, out var set))
                {
                    set = new HashSet<int>();
                    map[pageIndex] = set;
                }

                set.Add(reference.Id);
            }
        }

        void AddTableLines(TableLayout tableLayout)
        {
            if (tableLayout.Cells.Count == 0)
            {
                return;
            }

            var tableLines = new List<TableCellLine>();
            foreach (var cell in tableLayout.Cells)
            {
                if (cell.Lines.Count == 0)
                {
                    continue;
                }

                tableLines.AddRange(cell.Lines);
            }

            if (tableLines.Count == 0)
            {
                return;
            }

            tableLines.Sort(static (left, right) =>
            {
                var paragraphCompare = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
                if (paragraphCompare != 0)
                {
                    return paragraphCompare;
                }

                var offsetCompare = left.StartOffset.CompareTo(right.StartOffset);
                if (offsetCompare != 0)
                {
                    return offsetCompare;
                }

                return left.Y.CompareTo(right.Y);
            });

            var currentParagraph = -1;
            var paragraphLineStart = 0;
            foreach (var line in tableLines)
            {
                if (line.ParagraphIndex != currentParagraph)
                {
                    if (currentParagraph >= 0)
                    {
                        paragraphLineRanges[currentParagraph] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                    }

                    currentParagraph = line.ParagraphIndex;
                    paragraphLineStart = lines.Count;
                }

                AddLine(new LayoutLine(
                    line.ParagraphIndex,
                    line.StartOffset,
                    line.Length,
                    line.X,
                    line.Y,
                    line.Width,
                    line.TextSlice,
                    line.Prefix,
                    line.PrefixWidth,
                    line.LineHeight,
                    line.Ascent,
                    line.Runs,
                    line.Images,
                    line.Shapes,
                    line.Charts,
                    line.Equations,
                    true,
                    line.IsRtl));
            }

            if (currentParagraph >= 0)
            {
                paragraphLineRanges[currentParagraph] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
            }
        }

        static bool AreSettingsCompatible(LayoutSettings current, LayoutSettings previous)
        {
            const float epsilon = 0.01f;
            return current.UsePagination == previous.UsePagination
                   && MathF.Abs(current.ViewportWidth - previous.ViewportWidth) < epsilon
                   && MathF.Abs(current.ViewportHeight - previous.ViewportHeight) < epsilon
                   && MathF.Abs(current.PageWidth - previous.PageWidth) < epsilon
                   && MathF.Abs(current.PageHeight - previous.PageHeight) < epsilon
                   && MathF.Abs(current.PageGap - previous.PageGap) < epsilon
                   && MathF.Abs(current.MarginLeft - previous.MarginLeft) < epsilon
                   && MathF.Abs(current.MarginTop - previous.MarginTop) < epsilon
                   && MathF.Abs(current.MarginRight - previous.MarginRight) < epsilon
                   && MathF.Abs(current.MarginBottom - previous.MarginBottom) < epsilon
                   && MathF.Abs(current.HeaderOffset - previous.HeaderOffset) < epsilon
                   && MathF.Abs(current.FooterOffset - previous.FooterOffset) < epsilon
                   && MathF.Abs(current.Gutter - previous.Gutter) < epsilon
                   && MathF.Abs(current.ParagraphSpacing - previous.ParagraphSpacing) < epsilon
                   && MathF.Abs(current.BlockSpacing - previous.BlockSpacing) < epsilon
                   && MathF.Abs(current.ListIndent - previous.ListIndent) < epsilon
                   && MathF.Abs(current.ListMarkerGap - previous.ListMarkerGap) < epsilon
                   && MathF.Abs(current.DefaultTabWidth - previous.DefaultTabWidth) < epsilon
                   && MathF.Abs(current.ColumnGap - previous.ColumnGap) < epsilon
                   && MathF.Abs(current.TableCellPadding - previous.TableCellPadding) < epsilon
                   && MathF.Abs(current.TableBorderThickness - previous.TableBorderThickness) < epsilon;
        }

        void WarmListState(int blockLimit)
        {
            for (var i = 0; i < blockLimit && i < blocks.Count; i++)
            {
                if (blocks[i] is not ParagraphBlock paragraph)
                {
                    continue;
                }

                var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
                listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            }
        }

        bool TryReusePrefix(DocumentLayout previous, int dirtyParagraph)
        {
            if (dirtyParagraph < 0 || document.ParagraphCount == 0)
            {
                return false;
            }

            if (dirtyParagraph >= document.ParagraphCount)
            {
                dirtyParagraph = document.ParagraphCount - 1;
            }

            if (previous.Lines.Count == 0 || previous.Pages.Count == 0)
            {
                return false;
            }

            if (previous.PageSections.Count != previous.Pages.Count)
            {
                return false;
            }

            if (!AreSettingsCompatible(settings, previous.Settings))
            {
                return false;
            }

            var dirtyLocation = document.GetParagraphLocation(dirtyParagraph);
            var startParagraphIndex = dirtyParagraph;
            var startBlockIndex = dirtyLocation.BlockIndex;

            if (dirtyLocation.IsInTable)
            {
                var paragraphsBefore = CountParagraphsBeforeInTable(dirtyLocation);
                startParagraphIndex = Math.Max(0, dirtyParagraph - paragraphsBefore);
            }

            if (startBlockIndex > 0 && blocks[startBlockIndex - 1] is ParagraphBlock previousParagraph)
            {
                var previousProperties = styleResolver.ResolveParagraphProperties(previousParagraph);
                if (previousProperties.KeepWithNext == true)
                {
                    startParagraphIndex = Math.Max(0, startParagraphIndex - 1);
                    startBlockIndex = startBlockIndex - 1;
                }
            }

            if (!previous.ParagraphLineRanges.TryGetValue(startParagraphIndex, out var range) || range.Count == 0)
            {
                return false;
            }

            var startLineIndex = range.Start;
            if (startLineIndex < 0 || startLineIndex >= previous.Lines.Count)
            {
                return false;
            }

            var startPageIndex = previous.LineIndex.GetPageForLine(startLineIndex);
            if (startPageIndex < 0 || startPageIndex >= previous.Pages.Count)
            {
                return false;
            }

            var startParagraph = document.GetParagraph(startParagraphIndex, out startBlockIndex);
            blockStartIndex = startBlockIndex;
            paragraphIndex = startParagraphIndex;

            for (var i = 0; i < startLineIndex; i++)
            {
                lines.Add(previous.Lines[i]);
                linePageIndices.Add(previous.LineIndex.GetPageForLine(i));
            }

            foreach (var pair in previous.ParagraphLineRanges)
            {
                if (pair.Key < startParagraphIndex)
                {
                    paragraphLineRanges[pair.Key] = pair.Value;
                }
            }

            for (var i = 0; i <= startPageIndex; i++)
            {
                pages.Add(previous.Pages[i]);
                pageSections.Add(previous.PageSections[i]);
            }

            sectionSettings = pageSections[startPageIndex];
            currentSectionIndex = sectionSettings.SectionIndex;
            pageIndex = startPageIndex;
            pageY = pages[startPageIndex].Bounds.Y;

            ApplySectionSettings(sectionSettings, true);
            columnIndex = ResolveColumnIndex(previous.Lines[startLineIndex].X);
            UpdateColumnMetrics();

            var startLine = previous.Lines[startLineIndex];
            if (startLine.IsInTable)
            {
                var tableLayout = FindContainingTable(previous.Tables, startLine);
                if (tableLayout is null)
                {
                    return false;
                }

                cursorY = MathF.Max(columnTop, tableLayout.Bounds.Top);
            }
            else
            {
                var paragraphProperties = styleResolver.ResolveParagraphProperties(startParagraph);
                var spacingBefore = paragraphProperties.SpacingBefore ?? settings.ParagraphSpacing;
                var resumeY = previous.Lines[startLineIndex].Y - spacingBefore;
                cursorY = MathF.Max(columnTop, resumeY);
            }

            WarmListState(blockStartIndex);

            if (previous.BreakMarkers.Count > 0)
            {
                foreach (var marker in previous.BreakMarkers)
                {
                    if (marker.PageIndex < startPageIndex)
                    {
                        breakMarkers.Add(marker);
                    }
                    else if (marker.PageIndex == startPageIndex && marker.Y + 0.5f < cursorY)
                    {
                        breakMarkers.Add(marker);
                    }
                }
            }

            var tableLimit = cursorY + 0.5f;
            foreach (var table in previous.Tables)
            {
                if (table.Bounds.Bottom <= tableLimit)
                {
                    tables.Add(table);
                }
            }

            return true;
        }

        static int CountParagraphsBeforeInTable(ParagraphLocation location)
        {
            if (location.Table is null)
            {
                return 0;
            }

            var count = 0;
            for (var rowIndex = 0; rowIndex < location.RowIndex; rowIndex++)
            {
                var row = location.Table.Rows[rowIndex];
                foreach (var cell in row.Cells)
                {
                    count += cell.Paragraphs.Count;
                }
            }

            var currentRow = location.Table.Rows[location.RowIndex];
            for (var columnIndex = 0; columnIndex < location.ColumnIndex; columnIndex++)
            {
                count += currentRow.Cells[columnIndex].Paragraphs.Count;
            }

            count += Math.Max(0, location.ParagraphIndexInCell);
            return count;
        }

        static TableLayout? FindContainingTable(IReadOnlyList<TableLayout> tableLayouts, LayoutLine line)
        {
            foreach (var tableLayout in tableLayouts)
            {
                if (tableLayout.Bounds.Contains(line.X, line.Y))
                {
                    return tableLayout;
                }
            }

            return null;
        }

        static int FindContainingTableIndex(IReadOnlyList<TableLayout> tableLayouts, LayoutLine line)
        {
            for (var i = 0; i < tableLayouts.Count; i++)
            {
                if (tableLayouts[i].Bounds.Contains(line.X, line.Y))
                {
                    return i;
                }
            }

            return -1;
        }

        var reusedPrefix = previousLayout is not null
            && dirtyParagraphIndex.HasValue
            && TryReusePrefix(previousLayout, dirtyParagraphIndex.Value);

        if (!reusedPrefix)
        {
            ApplySectionSettings(sectionSettings, false);
            AddPage();
        }
        else
        {
            foreach (var floating in previousLayout!.FloatingObjects)
            {
                if (floating.ParagraphIndex < paragraphIndex)
                {
                    wrapFloatingObjects.Add(floating);
                }
            }
        }

        WrapBounds ResolveWrapBounds(float lineTop, float lineHeight, float baseLeft, float baseRight)
        {
            if (wrapFloatingObjects.Count == 0 || baseRight <= baseLeft)
            {
                return new WrapBounds(baseLeft, baseRight, float.MinValue);
            }

            var left = baseLeft;
            var right = baseRight;
            var blockBottom = float.MinValue;
            var mid = (baseLeft + baseRight) / 2f;

            foreach (var floating in wrapFloatingObjects)
            {
                if (floating.PageIndex != pageIndex)
                {
                    continue;
                }

                var anchor = floating.Object.Anchor;
                if (anchor.BehindText || anchor.WrapStyle is FloatingWrapStyle.None or FloatingWrapStyle.TopBottom)
                {
                    continue;
                }

                var bounds = InflateBounds(floating.Bounds, anchor.Distance);
                if (!LineOverlaps(bounds, lineTop, lineHeight))
                {
                    continue;
                }

                if (bounds.Right <= baseLeft || bounds.Left >= baseRight)
                {
                    continue;
                }

                blockBottom = MathF.Max(blockBottom, bounds.Bottom);
                switch (anchor.WrapSide)
                {
                    case FloatingWrapSide.Left:
                        right = MathF.Min(right, bounds.Left);
                        break;
                    case FloatingWrapSide.Right:
                        left = MathF.Max(left, bounds.Right);
                        break;
                    case FloatingWrapSide.Largest:
                    {
                        var leftSpace = MathF.Max(0f, bounds.Left - baseLeft);
                        var rightSpace = MathF.Max(0f, baseRight - bounds.Right);
                        if (rightSpace > leftSpace)
                        {
                            left = MathF.Max(left, bounds.Right);
                        }
                        else
                        {
                            right = MathF.Min(right, bounds.Left);
                        }

                        break;
                    }
                    default:
                    {
                        var center = (bounds.Left + bounds.Right) / 2f;
                        if (center <= mid)
                        {
                            left = MathF.Max(left, bounds.Right);
                        }
                        else
                        {
                            right = MathF.Min(right, bounds.Left);
                        }

                        break;
                    }
                }
            }

            return new WrapBounds(left, right, blockBottom);
        }

        float ApplyTopBottomWrap(float lineTop, float lineHeight)
        {
            if (wrapFloatingObjects.Count == 0)
            {
                return lineTop;
            }

            var adjusted = lineTop;
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var floating in wrapFloatingObjects)
                {
                    if (floating.PageIndex != pageIndex)
                    {
                        continue;
                    }

                    var anchor = floating.Object.Anchor;
                    if (anchor.BehindText || anchor.WrapStyle != FloatingWrapStyle.TopBottom)
                    {
                        continue;
                    }

                    var bounds = InflateBounds(floating.Bounds, anchor.Distance);
                    if (!LineOverlaps(bounds, adjusted, lineHeight))
                    {
                        continue;
                    }

                    if (bounds.Bottom > adjusted + 0.5f)
                    {
                        adjusted = bounds.Bottom;
                        changed = true;
                    }
                }
            }

            return adjusted;
        }

        List<FloatingLayoutObject> CollectParagraphFloatingObjects(ParagraphBlock paragraph, int paragraphIndex, LineRange range)
        {
            var result = new List<FloatingLayoutObject>();
            if (paragraph.FloatingObjects.Count == 0 || range.Count == 0 || lines.Count == 0 || pages.Count == 0)
            {
                return result;
            }

            var rangeStart = Math.Clamp(range.Start, 0, lines.Count - 1);
            var rangeEnd = Math.Clamp(range.End, rangeStart + 1, lines.Count);

            foreach (var floating in paragraph.FloatingObjects)
            {
                var anchorLineIndex = ResolveAnchorLineIndex(lines, rangeStart, rangeEnd, paragraphIndex, floating.Anchor.AnchorOffset);
                anchorLineIndex = Math.Clamp(anchorLineIndex, 0, lines.Count - 1);
                var anchorLine = lines[anchorLineIndex];
                var anchorPageIndex = anchorLineIndex >= 0 && anchorLineIndex < linePageIndices.Count
                    ? linePageIndices[anchorLineIndex]
                    : pageIndex;
                if (anchorPageIndex < 0 || anchorPageIndex >= pages.Count)
                {
                    anchorPageIndex = pageIndex;
                }

                var anchorPage = pages[anchorPageIndex];
                var anchorSection = anchorPageIndex < pageSections.Count ? pageSections[anchorPageIndex] : sectionSettings;
                var (width, height) = ResolveFloatingSize(floating.Content);
                if (width <= 0f || height <= 0f)
                {
                    continue;
                }

                var baseX = ResolveAnchorX(floating.Anchor, anchorLine, anchorPage, anchorSection, width);
                var baseY = ResolveAnchorY(floating.Anchor, anchorLine, anchorPage, height);
                var x = baseX + floating.Anchor.OffsetX;
                var y = baseY + floating.Anchor.OffsetY;
                var bounds = new DocRect(x, y, width, height);
                result.Add(new FloatingLayoutObject(floating, paragraphIndex, anchorPageIndex, bounds));
            }

            return result;
        }

        LineRange LayoutParagraphLines(
            ParagraphBlock paragraph,
            ParagraphProperties properties,
            TextStyle paragraphStyle,
            string? prefix,
            float listIndent,
            float prefixWidth,
            float spacingBefore,
            float spacingAfter,
            float indentLeft,
            float indentRight,
            float firstLineIndent,
            bool keepWithNext,
            bool keepLinesTogether,
            bool widowControl,
            float nextBlockMinHeight)
        {
            var paragraphLineStart = lines.Count;
            WrapResolver? wrapResolver = wrapFloatingObjects.Count == 0 ? null : ResolveWrapBounds;
            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver);
            var lineStartY = cursorY + spacingBefore;
            var paragraphLines = BuildParagraphLines(
                text,
                spans,
                indentLeft,
                indentRight,
                firstLineIndent,
                listIndent,
                prefixWidth,
                properties,
                columnWidth,
                columnX,
                lineStartY,
                settings,
                measurer,
                lineHeight,
                ascent,
                wrapResolver);

            if (paragraphLines.Count == 0)
            {
                var lineX = columnX + indentLeft + listIndent + firstLineIndent + prefixWidth;
                var lineRight = columnX + columnWidth - indentRight;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(lineHeight, ascent, properties);
                var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                if (cursorY + spacingBefore + emptyLineHeight > contentBottom && cursorY > columnTop)
                {
                    StartNewColumnOrPage();
                }

                cursorY += spacingBefore;
                if (wrapResolver is not null)
                {
                    var adjustedY = ApplyTopBottomWrap(cursorY, emptyLineHeight);
                    if (adjustedY + emptyLineHeight > contentBottom && cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                        adjustedY = ApplyTopBottomWrap(columnTop, emptyLineHeight);
                    }

                    cursorY = adjustedY;
                    var wrap = ResolveWrapForLine(ref adjustedY, emptyLineHeight, lineX, lineRight, wrapResolver);
                    if (adjustedY + emptyLineHeight > contentBottom && cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                        adjustedY = ApplyTopBottomWrap(columnTop, emptyLineHeight);
                        wrap = ResolveWrapForLine(ref adjustedY, emptyLineHeight, lineX, lineRight, wrapResolver);
                    }

                    cursorY = adjustedY;
                    lineX = wrap.Left;
                    lineRight = wrap.Right;
                }

                var alignment = properties.Alignment;
                if (!alignment.HasValue && properties.Bidi == true)
                {
                    alignment = ParagraphAlignment.Right;
                }

                var alignedX = ApplyAlignment(lineX, 0f, MathF.Max(1f, lineRight - lineX), alignment);
                AddLine(new LayoutLine(paragraphIndex, 0, 0, alignedX, cursorY, 0, TextSlice.Empty, prefix, prefixWidth, emptyLineHeight, emptyAscent, Array.Empty<LayoutRun>(), Array.Empty<LayoutImage>(), Array.Empty<LayoutShape>(), Array.Empty<LayoutChart>(), Array.Empty<LayoutEquation>(), false, emptyIsRtl));
                cursorY += emptyLineHeight;
                cursorY += spacingAfter;
                return new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
            }

            var paragraphHeight = paragraphLines.Sum(line => line.Layout.LineHeight);
            var paragraphTotalHeight = spacingBefore + paragraphHeight + spacingAfter;
            var pageContentHeight = contentBottom - columnTop;

            if ((keepLinesTogether || keepWithNext)
                && paragraphTotalHeight + nextBlockMinHeight <= pageContentHeight
                && cursorY + paragraphTotalHeight + nextBlockMinHeight > contentBottom
                && cursorY > columnTop)
            {
                StartNewColumnOrPage();
            }

            var spacingBeforeApplied = false;
            var lineIndex = 0;
            while (lineIndex < paragraphLines.Count)
            {
                if (!spacingBeforeApplied)
                {
                    var firstLineHeight = paragraphLines[lineIndex].Layout.LineHeight;
                    if (cursorY + spacingBefore + firstLineHeight > contentBottom && cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                    }

                    cursorY += spacingBefore;
                    spacingBeforeApplied = true;
                }

                var availableHeight = contentBottom - cursorY;
                var linesToFit = CountParagraphLinesThatFit(paragraphLines, lineIndex, availableHeight);
                if (linesToFit == 0)
                {
                    if (cursorY <= columnTop + 0.5f)
                    {
                        linesToFit = 1;
                    }
                    else
                    {
                        StartNewColumnOrPage();
                        continue;
                    }
                }

                if (widowControl && paragraphLines.Count >= 2)
                {
                    var remaining = paragraphLines.Count - lineIndex;
                    var isAtPageTop = cursorY - spacingBefore <= columnTop + 0.5f;
                    if (linesToFit < 2 && remaining > 2 && !isAtPageTop)
                    {
                        StartNewColumnOrPage();
                        continue;
                    }

                    var remainingAfter = remaining - linesToFit;
                    if (remainingAfter > 0 && remainingAfter < 2)
                    {
                        var adjusted = remaining - 2;
                        if (adjusted >= 2)
                        {
                            linesToFit = adjusted;
                        }
                        else if (lineIndex > 0 && !isAtPageTop)
                        {
                            StartNewColumnOrPage();
                            continue;
                        }
                    }
                }

                var restartLineFlow = false;
                for (var i = 0; i < linesToFit; i++)
                {
                    var line = paragraphLines[lineIndex + i];
                    var lineIndent = indentLeft + listIndent + (line.IsFirstLine ? firstLineIndent : 0f);
                    var lineLeft = columnX + lineIndent + prefixWidth;
                    var lineRight = columnX + columnWidth - indentRight;
                    var currentY = cursorY;

                        if (wrapResolver is not null)
                        {
                            var adjustedY = ApplyTopBottomWrap(currentY, line.Layout.LineHeight);
                            if (adjustedY + line.Layout.LineHeight > contentBottom && currentY > columnTop)
                            {
                                StartNewColumnOrPage();
                                restartLineFlow = true;
                                break;
                            }

                            currentY = adjustedY;
                            var wrap = ResolveWrapForLine(ref adjustedY, line.Layout.LineHeight, lineLeft, lineRight, wrapResolver);
                            if (adjustedY + line.Layout.LineHeight > contentBottom && currentY > columnTop)
                            {
                                StartNewColumnOrPage();
                                restartLineFlow = true;
                                break;
                            }

                            currentY = adjustedY;
                            lineLeft = wrap.Left;
                            lineRight = wrap.Right;
                        }

                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var availableWidth = MathF.Max(1f, lineRight - lineLeft);
                    var lineLayout = line.Layout;
                    var isLastLine = IsLastParagraphLine(text, line.Start + line.Length);
                    if (alignment == ParagraphAlignment.Justify && !isLastLine)
                    {
                        lineLayout = JustifyLineLayout(lineLayout, availableWidth, measurer);
                    }

                    var alignedX = ApplyAlignment(lineLeft, lineLayout.Width, availableWidth, alignment);
                    var isRtl = ResolveLineIsRtl(properties, line.TextSlice);
                    AddLine(new LayoutLine(
                        paragraphIndex,
                        line.Start,
                        line.Length,
                        alignedX,
                        currentY,
                        lineLayout.Width,
                        line.TextSlice,
                        line.IsFirstLine ? prefix : null,
                        line.IsFirstLine ? prefixWidth : 0f,
                        lineLayout.LineHeight,
                        lineLayout.Ascent,
                        lineLayout.Runs,
                        lineLayout.Images,
                        lineLayout.Shapes,
                        lineLayout.Charts,
                        lineLayout.Equations,
                        false,
                        isRtl));
                    cursorY = currentY + lineLayout.LineHeight;
                }

                if (restartLineFlow)
                {
                    continue;
                }

                lineIndex += linesToFit;
            }

            cursorY += spacingAfter;
            return new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
        }

        bool RequiresParagraphReflow(IReadOnlyList<FloatingLayoutObject> floatingObjects)
        {
            foreach (var floating in floatingObjects)
            {
                var anchor = floating.Object.Anchor;
                if (anchor.BehindText || anchor.WrapStyle == FloatingWrapStyle.None)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        static bool AreFloatsEquivalent(
            IReadOnlyList<FloatingLayoutObject> left,
            IReadOnlyList<FloatingLayoutObject> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            const float epsilon = 0.25f;
            for (var i = 0; i < left.Count; i++)
            {
                var first = left[i];
                var second = right[i];
                if (first.Object.Id != second.Object.Id)
                {
                    return false;
                }

                if (!AreBoundsClose(first.Bounds, second.Bounds, epsilon))
                {
                    return false;
                }
            }

            return true;
        }

        void RestoreParagraphSnapshot(
            ParagraphLayoutSnapshot snapshot,
            Dictionary<int, HashSet<int>> footnotesSnapshot,
            Dictionary<int, HashSet<int>> endnotesSnapshot)
        {
            if (lines.Count > snapshot.LinesCount)
            {
                lines.RemoveRange(snapshot.LinesCount, lines.Count - snapshot.LinesCount);
            }

            if (linePageIndices.Count > snapshot.LinePageIndicesCount)
            {
                linePageIndices.RemoveRange(snapshot.LinePageIndicesCount, linePageIndices.Count - snapshot.LinePageIndicesCount);
            }

            if (pages.Count > snapshot.PagesCount)
            {
                pages.RemoveRange(snapshot.PagesCount, pages.Count - snapshot.PagesCount);
            }

            if (pageSections.Count > snapshot.PageSectionsCount)
            {
                pageSections.RemoveRange(snapshot.PageSectionsCount, pageSections.Count - snapshot.PageSectionsCount);
            }

            cursorY = snapshot.CursorY;
            pageIndex = snapshot.PageIndex;
            pageY = snapshot.PageY;
            columnIndex = snapshot.ColumnIndex;
            columnX = snapshot.ColumnX;
            columnWidth = snapshot.ColumnWidth;
            columnTop = snapshot.ColumnTop;
            contentTop = snapshot.ContentTop;
            contentBottom = snapshot.ContentBottom;
            footnotesByPage = footnotesSnapshot;
            endnotesByPage = endnotesSnapshot;
        }

        TableLayout OffsetTableLayout(TableLayout table, float dx, float dy)
        {
            if (MathF.Abs(dx) < 0.01f && MathF.Abs(dy) < 0.01f)
            {
                return table;
            }

            var bounds = table.Bounds;
            var updatedBounds = new DocRect(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height);
            var updatedCells = new List<TableCellLayout>(table.Cells.Count);
            foreach (var cell in table.Cells)
            {
                var cellBounds = cell.Bounds;
                var updatedCellBounds = new DocRect(cellBounds.X + dx, cellBounds.Y + dy, cellBounds.Width, cellBounds.Height);
                if (cell.Lines.Count == 0)
                {
                    updatedCells.Add(cell with { Bounds = updatedCellBounds });
                    continue;
                }

                var updatedLines = new List<TableCellLine>(cell.Lines.Count);
                foreach (var line in cell.Lines)
                {
                    updatedLines.Add(line with { X = line.X + dx, Y = line.Y + dy });
                }

                updatedCells.Add(cell with { Bounds = updatedCellBounds, Lines = updatedLines });
            }

            return table with { Bounds = updatedBounds, Cells = updatedCells };
        }

        void OffsetTableLines(int rangeStart, int rangeEnd, DocRect bounds, float dx, float dy)
        {
            if (MathF.Abs(dx) < 0.01f && MathF.Abs(dy) < 0.01f)
            {
                return;
            }

            var minX = bounds.Left - 0.5f;
            var maxX = bounds.Right + 0.5f;
            var minY = bounds.Top - 0.5f;
            var maxY = bounds.Bottom + 0.5f;
            var start = Math.Clamp(rangeStart, 0, lines.Count);
            var end = Math.Clamp(rangeEnd, start, lines.Count);
            for (var i = start; i < end; i++)
            {
                var line = lines[i];
                if (!line.IsInTable)
                {
                    continue;
                }

                if (line.X < minX || line.X > maxX || line.Y < minY || line.Y > maxY)
                {
                    continue;
                }

                lines[i] = line with { X = line.X + dx, Y = line.Y + dy };
            }
        }

        void BalanceSectionColumns()
        {
            if (pages.Count == 0 || (lines.Count == 0 && tables.Count == 0))
            {
                return;
            }

            var pageLineRanges = BuildPageLineRanges(linePageIndices, pages.Count);
            var lastPageBySection = new Dictionary<int, int>();
            for (var i = 0; i < lines.Count && i < linePageIndices.Count; i++)
            {
                var line = lines[i];
                if (line.ParagraphIndex < 0)
                {
                    continue;
                }

                if (!paragraphSectionIndices.TryGetValue(line.ParagraphIndex, out var sectionIndex))
                {
                    continue;
                }

                var pageIndex = linePageIndices[i];
                if (!lastPageBySection.TryGetValue(sectionIndex, out var lastPage) || pageIndex > lastPage)
                {
                    lastPageBySection[sectionIndex] = pageIndex;
                }
            }

            foreach (var entry in lastPageBySection)
            {
                if (!sectionSettingsByIndex.TryGetValue(entry.Key, out var section))
                {
                    continue;
                }

                BalancePageColumns(entry.Value, pageLineRanges[entry.Value], section, entry.Key);
            }
        }

        void BalancePageColumns(int pageIndex, LineRange range, PageSectionSettings section, int sectionIndex)
        {
            if (section.ColumnCount <= 1 || pageIndex < 0 || pageIndex >= pages.Count)
            {
                return;
            }

            var page = pages[pageIndex];
            var pageSection = section.ResolveForPage(pageIndex);
            var contentLeft = page.Bounds.X + pageSection.MarginLeft;
            var sectionTop = page.Bounds.Y + pageSection.MarginTop;
            var contentWidth = MathF.Max(1f, page.Bounds.Width - pageSection.MarginLeft - pageSection.MarginRight);
            var columnGap = MathF.Max(0f, pageSection.ColumnGap);
            var columnWidths = ResolveSectionColumnWidths(pageSection, contentWidth, columnGap);
            if (columnWidths.Length <= 1)
            {
                return;
            }

            var columnOffsets = BuildColumnOffsets(columnWidths, columnGap);
            if (columnOffsets.Length <= 1)
            {
                return;
            }

            var lineIndices = new List<int>(Math.Max(0, range.Count));
            var tableAnchorIndices = new Dictionary<int, int>();
            var tableIndices = new HashSet<int>();
            for (var i = range.Start; i < range.End && i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.ParagraphIndex < 0
                    || !paragraphSectionIndices.TryGetValue(line.ParagraphIndex, out var lineSection)
                    || lineSection != sectionIndex)
                {
                    continue;
                }

                if (line.IsInTable)
                {
                    var tableIndex = FindContainingTableIndex(tables, line);
                    if (tableIndex >= 0)
                    {
                        tableIndices.Add(tableIndex);
                        if (!tableAnchorIndices.TryGetValue(tableIndex, out var anchorIndex) || i < anchorIndex)
                        {
                            tableAnchorIndices[tableIndex] = i;
                        }
                    }

                    continue;
                }

                lineIndices.Add(i);
            }

            if (lineIndices.Count == 0 && tableIndices.Count == 0)
            {
                return;
            }

            var tableItems = new List<BalanceItem>(tableIndices.Count);
            foreach (var tableIndex in tableIndices)
            {
                var table = tables[tableIndex];
                var anchorIndex = tableAnchorIndices.TryGetValue(tableIndex, out var anchor) ? anchor : int.MaxValue;
                tableItems.Add(new BalanceItem(
                    BalanceItemKind.Table,
                    tableIndex,
                    anchorIndex,
                    table.Bounds.X,
                    table.Bounds.Y,
                    table.Bounds.Height,
                    -1,
                    -1));
            }

            tableItems.Sort((left, right) =>
            {
                var anchorCompare = left.AnchorIndex.CompareTo(right.AnchorIndex);
                if (anchorCompare != 0)
                {
                    return anchorCompare;
                }

                return left.Y.CompareTo(right.Y);
            });

            var items = new List<BalanceItem>(lineIndices.Count + tableItems.Count);
            var tablePointer = 0;
            for (var i = 0; i < lineIndices.Count; i++)
            {
                var lineIndex = lineIndices[i];
                while (tablePointer < tableItems.Count && tableItems[tablePointer].AnchorIndex <= lineIndex)
                {
                    items.Add(tableItems[tablePointer]);
                    tablePointer++;
                }

                var line = lines[lineIndex];
                items.Add(new BalanceItem(
                    BalanceItemKind.Line,
                    lineIndex,
                    lineIndex,
                    line.X,
                    line.Y,
                    line.LineHeight,
                    line.ParagraphIndex,
                    line.StartOffset));
            }

            while (tablePointer < tableItems.Count)
            {
                items.Add(tableItems[tablePointer]);
                tablePointer++;
            }

            if (items.Count == 0)
            {
                return;
            }

            var firstItem = items[0];
            var adjustedTop = firstItem.Y;
            if (firstItem.Kind == BalanceItemKind.Line
                && firstItem.StartOffset == 0
                && paragraphSpacingBefore.TryGetValue(firstItem.ParagraphIndex, out var firstSpacingBefore))
            {
                adjustedTop -= firstSpacingBefore;
            }

            sectionTop = MathF.Max(sectionTop, adjustedTop);

            var advances = new float[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                if (i == 0)
                {
                    advances[i] = MathF.Max(0f, items[i].Y - sectionTop);
                }
                else
                {
                    var prev = items[i - 1];
                    advances[i] = MathF.Max(0f, items[i].Y - (prev.Y + prev.Height));
                }
            }

            var totalHeight = 0f;
            for (var i = 0; i < items.Count; i++)
            {
                totalHeight += advances[i] + items[i].Height;
            }

            if (totalHeight <= 0f)
            {
                return;
            }

            var targetHeight = totalHeight / section.ColumnCount;
            var columnIndexBalance = 0;
            var currentY = sectionTop;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var isFirstInColumn = currentY <= sectionTop + 0.5f;
                var gap = advances[i];
                if (item.Kind == BalanceItemKind.Line && isFirstInColumn && item.StartOffset == 0
                    && paragraphSpacingBefore.TryGetValue(item.ParagraphIndex, out var spacingBefore))
                {
                    gap = spacingBefore;
                }
                else if (isFirstInColumn)
                {
                    gap = 0f;
                }

                if (columnIndexBalance < section.ColumnCount - 1
                    && !isFirstInColumn
                    && currentY + gap + item.Height > sectionTop + targetHeight)
                {
                    columnIndexBalance++;
                    currentY = sectionTop;
                    isFirstInColumn = true;
                    gap = 0f;
                    if (item.Kind == BalanceItemKind.Line && item.StartOffset == 0
                        && paragraphSpacingBefore.TryGetValue(item.ParagraphIndex, out spacingBefore))
                    {
                        gap = spacingBefore;
                    }
                }

                currentY += gap;

                var oldColumnIndex = ResolveColumnIndexFromX(item.X, contentLeft, columnOffsets);
                var oldColumnLeft = contentLeft + columnOffsets[Math.Clamp(oldColumnIndex, 0, columnOffsets.Length - 1)];
                var newColumnLeft = contentLeft + columnOffsets[Math.Clamp(columnIndexBalance, 0, columnOffsets.Length - 1)];
                var newX = item.X + (newColumnLeft - oldColumnLeft);

                if (item.Kind == BalanceItemKind.Line)
                {
                    var line = lines[item.Index];
                    lines[item.Index] = line with { X = newX, Y = currentY };
                }
                else
                {
                    var table = tables[item.Index];
                    var dx = newX - table.Bounds.X;
                    var dy = currentY - table.Bounds.Y;
                    if (MathF.Abs(dx) > 0.01f || MathF.Abs(dy) > 0.01f)
                    {
                        tables[item.Index] = OffsetTableLayout(table, dx, dy);
                        OffsetTableLines(range.Start, range.End, table.Bounds, dx, dy);
                    }
                }

                currentY += item.Height;
            }
        }

        static int ResolveColumnIndexFromX(float lineX, float contentX, float[] columnOffsets)
        {
            if (columnOffsets.Length == 0)
            {
                return 0;
            }

            var relativeX = lineX - contentX;
            for (var i = columnOffsets.Length - 1; i >= 0; i--)
            {
                if (relativeX >= columnOffsets[i] - 0.5f)
                {
                    return i;
                }
            }

            return 0;
        }

        static LineRange[] BuildPageLineRanges(IReadOnlyList<int> linePageIndices, int pageCount)
        {
            var ranges = new LineRange[pageCount];
            if (pageCount == 0)
            {
                return ranges;
            }

            var currentPage = 0;
            var start = 0;
            for (var i = 0; i < linePageIndices.Count; i++)
            {
                var page = linePageIndices[i];
                while (currentPage < page && currentPage < pageCount)
                {
                    ranges[currentPage] = new LineRange(start, i - start);
                    start = i;
                    currentPage++;
                }
            }

            while (currentPage < pageCount)
            {
                ranges[currentPage] = new LineRange(start, linePageIndices.Count - start);
                currentPage++;
            }

            return ranges;
        }

        void HandlePageBreak(PageBreakBlock _, int __)
        {
            AddBreakMarker(BreakMarkerKind.Page, "Page Break");
            StartNewPage();
        }

        void HandleColumnBreak(ColumnBreakBlock _, int __)
        {
            StartNewColumnOrPage();
        }

        void HandleSectionBreak(SectionBreakBlock sectionBreak, int __)
        {
            AddBreakMarker(BreakMarkerKind.Section, FormatSectionBreakLabel(sectionBreak.BreakType));
            var nextSectionIndex = sectionBreak.SectionIndex
                ?? (currentSectionIndex + 1 < document.SectionCount ? currentSectionIndex + 1 : currentSectionIndex);
            var nextSettings = sectionSettingsByIndex.TryGetValue(nextSectionIndex, out var section)
                ? section
                : PageSectionSettings.FromSettings(settings, document.GetSection(nextSectionIndex).Properties, nextSectionIndex, document.MirrorMargins, document.GutterAtTop);
            if (sectionBreak.SectionIndex is null && sectionBreak.Properties.HasValues)
            {
                nextSettings = nextSettings.ApplyOverrides(sectionBreak.Properties);
            }

            ApplySectionBreak(sectionBreak.BreakType, nextSettings);
        }

        void HandleAltChunk(AltChunkBlock altChunk, int __)
        {
            var label = ResolveAltChunkLabel(altChunk);
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            var spacingBefore = settings.ParagraphSpacing;
            var spacingAfter = settings.ParagraphSpacing;
            var textStyle = style.Clone();
            var width = measurer is ITextMeasurerSpan spanMeasurer
                ? spanMeasurer.MeasureText(label.AsSpan(), textStyle).Width
                : measurer.MeasureText(label, textStyle).Width;

            if (cursorY + spacingBefore + lineHeight > contentBottom && cursorY > columnTop)
            {
                StartNewColumnOrPage();
            }

            cursorY += spacingBefore;
            if (cursorY + lineHeight > contentBottom && cursorY > columnTop)
            {
                StartNewColumnOrPage();
            }

            var textSlice = new TextSlice(label, 0, label.Length);
            var runs = new[] { new LayoutRun(label, textStyle, 0f, width, label.Length, false, 0f) };
            var isRtl = TextBidi.ResolveBaseIsRtl(textSlice.Span, null);
            AddLine(new LayoutLine(-1, 0, label.Length, columnX, cursorY, width, textSlice, null, 0f, lineHeight, ascent,
                runs, Array.Empty<LayoutImage>(), Array.Empty<LayoutShape>(), Array.Empty<LayoutChart>(), Array.Empty<LayoutEquation>(), false, isRtl));

            cursorY += lineHeight + spacingAfter;
        }

        ParagraphLayoutPlan BuildParagraphPlan(ParagraphBlock paragraph, int blockIndex)
        {
            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var spacingBefore = properties.SpacingBefore ?? settings.ParagraphSpacing;
            if (properties.ContextualSpacing == true && blockIndex > 0 && blocks[blockIndex - 1] is ParagraphBlock previousParagraph)
            {
                var previousStyleId = previousParagraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                var currentStyleId = paragraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                if (!string.IsNullOrWhiteSpace(currentStyleId)
                    && string.Equals(previousStyleId, currentStyleId, StringComparison.OrdinalIgnoreCase))
                {
                    spacingBefore = 0f;
                }
            }
            paragraphSpacingBefore[paragraphIndex] = spacingBefore;
            var spacingAfter = properties.SpacingAfter ?? settings.ParagraphSpacing;
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;

            var keepWithNext = properties.KeepWithNext == true;
            var keepLinesTogether = properties.KeepLinesTogether == true;
            var widowControl = properties.WidowControl ?? true;
            var nextBlockMinHeight = keepWithNext
                ? EstimateNextBlockMinHeight(blockIndex, blocks, document, styleResolver, settings, measurer, style, columnWidth, lineHeight, ascent)
                : 0f;

            var canReflow = paragraph.FloatingObjects.Count > 0;
            return new ParagraphLayoutPlan(
                paragraph,
                properties,
                paragraphStyle,
                prefix,
                listIndent,
                prefixWidth,
                spacingBefore,
                spacingAfter,
                indentLeft,
                indentRight,
                firstLineIndent,
                keepWithNext,
                keepLinesTogether,
                widowControl,
                nextBlockMinHeight,
                canReflow);
        }

        LineRange LayoutParagraphWithReflow(ParagraphLayoutPlan plan)
        {
            var canReflow = plan.CanReflow;
            var wrapStartCount = wrapFloatingObjects.Count;
            ParagraphLayoutSnapshot? snapshot = null;
            Dictionary<int, HashSet<int>>? footnotesSnapshot = null;
            Dictionary<int, HashSet<int>>? endnotesSnapshot = null;

            if (canReflow)
            {
                snapshot = new ParagraphLayoutSnapshot(
                    lines.Count,
                    linePageIndices.Count,
                    pages.Count,
                    pageSections.Count,
                    cursorY,
                    pageIndex,
                    pageY,
                    columnIndex,
                    columnX,
                    columnWidth,
                    columnTop,
                    contentTop,
                    contentBottom);
                footnotesSnapshot = CloneNoteMap(footnotesByPage);
                endnotesSnapshot = CloneNoteMap(endnotesByPage);
            }

            var localFloats = Array.Empty<FloatingLayoutObject>();
            LineRange lineRange = default;
            var maxPasses = canReflow ? 3 : 1;

            for (var pass = 0; pass < maxPasses; pass++)
            {
                if (pass > 0 && canReflow)
                {
                    RestoreParagraphSnapshot(snapshot!.Value, footnotesSnapshot!, endnotesSnapshot!);
                    paragraphLineRanges.Remove(paragraphIndex);
                }

                if (wrapFloatingObjects.Count > wrapStartCount)
                {
                    wrapFloatingObjects.RemoveRange(wrapStartCount, wrapFloatingObjects.Count - wrapStartCount);
                }

                if (localFloats.Length > 0)
                {
                    wrapFloatingObjects.AddRange(localFloats);
                }

                lineRange = LayoutParagraphLines(
                    plan.Paragraph,
                    plan.Properties,
                    plan.ParagraphStyle,
                    plan.Prefix,
                    plan.ListIndent,
                    plan.PrefixWidth,
                    plan.SpacingBefore,
                    plan.SpacingAfter,
                    plan.IndentLeft,
                    plan.IndentRight,
                    plan.FirstLineIndent,
                    plan.KeepWithNext,
                    plan.KeepLinesTogether,
                    plan.WidowControl,
                    plan.NextBlockMinHeight);
                paragraphLineRanges[paragraphIndex] = lineRange;

                if (!canReflow)
                {
                    break;
                }

                var updatedFloats = CollectParagraphFloatingObjects(plan.Paragraph, paragraphIndex, lineRange);
                if (pass == 0 && !RequiresParagraphReflow(updatedFloats))
                {
                    localFloats = updatedFloats.ToArray();
                    break;
                }

                if (AreFloatsEquivalent(localFloats, updatedFloats))
                {
                    localFloats = updatedFloats.ToArray();
                    break;
                }

                localFloats = updatedFloats.ToArray();
            }

            if (canReflow)
            {
                if (wrapFloatingObjects.Count > wrapStartCount)
                {
                    wrapFloatingObjects.RemoveRange(wrapStartCount, wrapFloatingObjects.Count - wrapStartCount);
                }

                if (localFloats.Length > 0)
                {
                    wrapFloatingObjects.AddRange(localFloats);
                }
            }

            return lineRange;
        }

        void HandleParagraph(ParagraphBlock paragraph, int blockIndex)
        {
            var plan = BuildParagraphPlan(paragraph, blockIndex);
            if (plan.Properties.PageBreakBefore == true && cursorY > contentTop + 0.5f)
            {
                StartNewPage();
            }
            paragraphSpacingBefore[paragraphIndex] = plan.SpacingBefore;
            _ = LayoutParagraphWithReflow(plan);
            paragraphIndex++;
        }

        TableLayoutPlan BuildTablePlan(TableBlock table)
        {
            var tableX = columnX;
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, columnWidth, settings, measurer, style, styleResolver, lineHeight, ascent, ref paragraphIndex);
            return new TableLayoutPlan(table, resolvedTableProperties, data, tableX);
        }

        void LayoutTablePlan(TableLayoutPlan plan)
        {
            var tableX = plan.TableX;
            var data = plan.Data;
            var rowStart = 0;
            var totalRows = data.RowHeights.Length;
            if (totalRows == 0)
            {
                var emptyLayout = BuildTableLayout(plan.Table, plan.ResolvedProperties, data, tableX, cursorY, 0, 0, settings);
                tables.Add(emptyLayout);
                cursorY += emptyLayout.Bounds.Height + settings.BlockSpacing;
                return;
            }

            while (rowStart < totalRows)
            {
                var availableHeight = contentBottom - cursorY;
                if (availableHeight <= 0f && cursorY > columnTop)
                {
                    StartNewColumnOrPage();
                    tableX = columnX;
                    availableHeight = contentBottom - cursorY;
                }

                var rowsToFit = CountRowsToFit(data.RowHeights, rowStart, availableHeight);
                if (rowsToFit == 0)
                {
                    rowsToFit = Math.Min(1, totalRows - rowStart);
                }

                var tableLayout = BuildTableLayout(plan.Table, plan.ResolvedProperties, data, tableX, cursorY, rowStart, rowsToFit, settings);
                tables.Add(tableLayout);
                AddTableLines(tableLayout);
                cursorY += tableLayout.Bounds.Height + settings.BlockSpacing;
                rowStart += rowsToFit;

                if (rowStart < totalRows)
                {
                    StartNewColumnOrPage();
                    tableX = columnX;
                }
            }
        }

        void HandleTable(TableBlock table, int __)
        {
            var plan = BuildTablePlan(table);
            LayoutTablePlan(plan);
        }

        void ApplyBlockRules(LayoutBlockRule[] rules, Block block, int blockIndex)
        {
            foreach (var rule in rules)
            {
                if (rule.TryApply(block, blockIndex))
                {
                    return;
                }
            }
        }

        LayoutBlockRule[] BuildBlockRules()
        {
            return new[]
            {
                LayoutBlockRule.For<PageBreakBlock>(HandlePageBreak),
                LayoutBlockRule.For<ColumnBreakBlock>(HandleColumnBreak),
                LayoutBlockRule.For<SectionBreakBlock>(HandleSectionBreak),
                LayoutBlockRule.For<AltChunkBlock>(HandleAltChunk),
                LayoutBlockRule.For<ParagraphBlock>(HandleParagraph),
                LayoutBlockRule.For<TableBlock>(HandleTable)
            };
        }

        var blockRules = BuildBlockRules();

        for (var blockIndex = blockStartIndex; blockIndex < blocks.Count; blockIndex++)
        {
            ApplyBlockRules(blockRules, blocks[blockIndex], blockIndex);
        }

        IReadOnlyList<FootnoteLayout> footnotes = Array.Empty<FootnoteLayout>();

        static bool HasHeaderFooterContent(params HeaderFooter[] headers)
        {
            foreach (var headerFooter in headers)
            {
                if (headerFooter.Blocks.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        void LayoutHeaderFooters()
        {
            var hasHeaderFooter = document.Sections.Count == 0
                ? HasHeaderFooterContent(document.Header, document.Footer, document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter)
                : document.Sections.Any(section => HasHeaderFooterContent(section.Header, section.Footer, section.FirstHeader, section.FirstFooter, section.EvenHeader, section.EvenFooter));

            if (!hasHeaderFooter)
            {
                return;
            }

            var totalPages = Math.Max(1, pages.Count);
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var section = pageSections[i];
                var sectionInfo = document.GetSection(section.SectionIndex);
                var pageNumber = page.Index + 1;
                var isFirstPageOfSection = i == 0 || pageSections[i - 1].SectionIndex != section.SectionIndex;
                var isEvenPage = pageNumber % 2 == 0;
                var headerSource = sectionInfo.Header;
                var footerSource = sectionInfo.Footer;

                if (isFirstPageOfSection && sectionInfo.Properties.DifferentFirstPageHeaderFooter == true)
                {
                    headerSource = sectionInfo.FirstHeader;
                    footerSource = sectionInfo.FirstFooter;
                }
                else if (document.EvenAndOddHeaders && isEvenPage)
                {
                    headerSource = sectionInfo.EvenHeader;
                    footerSource = sectionInfo.EvenFooter;
                }

                if (headerSource.Blocks.Count == 0 && footerSource.Blocks.Count == 0)
                {
                    continue;
                }

                var headerFooterContentWidth = MathF.Max(1f, page.Bounds.Width - section.MarginLeft - section.MarginRight);
                var headerLayout = LayoutHeaderFooterBlocks(
                    headerSource.Blocks,
                    document,
                    settings,
                    measurer,
                    style,
                    styleResolver,
                    headerFooterContentWidth,
                    lineHeight,
                    ascent,
                    pageNumber,
                    totalPages);
                var footerLayout = LayoutHeaderFooterBlocks(
                    footerSource.Blocks,
                    document,
                    settings,
                    measurer,
                    style,
                    styleResolver,
                    headerFooterContentWidth,
                    lineHeight,
                    ascent,
                    pageNumber,
                    totalPages);

                var headerTop = page.Bounds.Y + section.HeaderOffset;
                var footerTop = page.Bounds.Bottom - section.FooterOffset - footerLayout.Height;
                var headerLines = OffsetHeaderFooterLines(headerLayout.Lines, page.Bounds.X + section.MarginLeft, headerTop);
                var footerLines = OffsetHeaderFooterLines(footerLayout.Lines, page.Bounds.X + section.MarginLeft, footerTop);
                headerFooters.Add(new HeaderFooterLayout(page.Index, headerLines, footerLines));
            }
        }

        void LayoutFootnotes()
        {
            footnotes = BuildFootnoteLayouts(
                document,
                pages,
                pageSections,
                headerFooters,
                footnotesByPage,
                endnotesByPage,
                settings,
                measurer,
                style,
                styleResolver,
                lineHeight,
                ascent);
        }

        void ApplyLayoutPasses(LayoutPass[] passes)
        {
            foreach (var pass in passes)
            {
                pass.Run();
            }
        }

        LayoutPass[] BuildPostLayoutPasses()
        {
            return new[]
            {
                new LayoutPass(BalanceSectionColumns),
                new LayoutPass(LayoutHeaderFooters),
                new LayoutPass(LayoutFootnotes)
            };
        }

        var postLayoutPasses = BuildPostLayoutPasses();

        ApplyLayoutPasses(postLayoutPasses);

        var commentHighlightsByParagraph = scanState.BuildCommentHighlights();
        var contentHeight = pages.Count == 0
            ? cursorY + marginBottom
            : pages.Last().Bounds.Bottom + settings.PageGap;

        var lineIndexMap = new LineIndex(lines, linePageIndices, pages);
        var floatingObjects = BuildFloatingObjects(document, lines, pages, pageSections, paragraphLineRanges, lineIndexMap);
        return new DocumentLayout(
            settings.Clone(),
            lines,
            tables,
            pages,
            headerFooters,
            footnotes,
            floatingObjects,
            pageSections,
            sectionSettingsByIndex,
            breakMarkers,
            lineIndexMap,
            paragraphLineRanges,
            paragraphSectionIndices,
            commentHighlightsByParagraph,
            lineHeight,
            ascent,
            contentHeight);
    }

    private static List<FloatingLayoutObject> BuildFloatingObjects(
        Document document,
        IReadOnlyList<LayoutLine> lines,
        IReadOnlyList<PageLayout> pages,
        IReadOnlyList<PageSectionSettings> pageSections,
        IReadOnlyDictionary<int, LineRange> paragraphLineRanges,
        LineIndex lineIndex)
    {
        var result = new List<FloatingLayoutObject>();
        if (pages.Count == 0 || lines.Count == 0)
        {
            return result;
        }

        var paragraphCount = document.ParagraphCount;
        for (var paragraphIndex = 0; paragraphIndex < paragraphCount; paragraphIndex++)
        {
            var paragraph = document.GetParagraph(paragraphIndex);
            if (paragraph.FloatingObjects.Count == 0)
            {
                continue;
            }

            if (!paragraphLineRanges.TryGetValue(paragraphIndex, out var range) || range.Count == 0)
            {
                continue;
            }

            var rangeStart = Math.Clamp(range.Start, 0, lines.Count - 1);
            var rangeEnd = Math.Clamp(range.End, rangeStart + 1, lines.Count);
            var pageIndex = lineIndex.GetPageForLine(rangeStart);
            if (pageIndex < 0 || pageIndex >= pages.Count)
            {
                pageIndex = 0;
            }

            var page = pages[pageIndex];
            var section = pageIndex < pageSections.Count ? pageSections[pageIndex] : PageSectionSettings.FromSettings(new LayoutSettings(), null, 0);

            foreach (var floating in paragraph.FloatingObjects)
            {
                var anchorLineIndex = ResolveAnchorLineIndex(lines, rangeStart, rangeEnd, paragraphIndex, floating.Anchor.AnchorOffset);
                var anchorLine = lines[Math.Clamp(anchorLineIndex, 0, lines.Count - 1)];
                var anchorPageIndex = lineIndex.GetPageForLine(anchorLineIndex);
                if (anchorPageIndex < 0 || anchorPageIndex >= pages.Count)
                {
                    anchorPageIndex = pageIndex;
                }

                var anchorPage = pages[anchorPageIndex];
                var anchorSection = anchorPageIndex < pageSections.Count ? pageSections[anchorPageIndex] : section;
                var (width, height) = ResolveFloatingSize(floating.Content);
                if (width <= 0f || height <= 0f)
                {
                    continue;
                }

                var baseX = ResolveAnchorX(floating.Anchor, anchorLine, anchorPage, anchorSection, width);
                var baseY = ResolveAnchorY(floating.Anchor, anchorLine, anchorPage, height);
                var x = baseX + floating.Anchor.OffsetX;
                var y = baseY + floating.Anchor.OffsetY;
                var bounds = new Vibe.Office.Primitives.DocRect(x, y, width, height);
                result.Add(new FloatingLayoutObject(floating, paragraphIndex, anchorPageIndex, bounds));
            }
        }

        return result;
    }

    private static int ResolveAnchorLineIndex(
        IReadOnlyList<LayoutLine> lines,
        int rangeStart,
        int rangeEnd,
        int paragraphIndex,
        int? anchorOffset)
    {
        if (!anchorOffset.HasValue)
        {
            return rangeStart;
        }

        var targetOffset = Math.Max(0, anchorOffset.Value);
        for (var i = rangeStart; i < rangeEnd; i++)
        {
            var line = lines[i];
            if (line.ParagraphIndex != paragraphIndex)
            {
                continue;
            }

            var lineEnd = line.StartOffset + line.Length;
            if (targetOffset >= line.StartOffset && targetOffset <= lineEnd)
            {
                return i;
            }
        }

        return rangeStart;
    }

    private static (float Width, float Height) ResolveFloatingSize(Inline content)
    {
        return content switch
        {
            ImageInline image => (image.Width, image.Height),
            ShapeInline shape => (shape.Width, shape.Height),
            ChartInline chart => (chart.Width, chart.Height),
            _ => (0f, 0f)
        };
    }

    private static WrapBounds ResolveWrapForLine(
        ref float lineTop,
        float lineHeight,
        float baseLeft,
        float baseRight,
        WrapResolver wrapResolver)
    {
        var wrap = wrapResolver(lineTop, lineHeight, baseLeft, baseRight);
        var attempts = 0;
        while (wrap.Right - wrap.Left < 1f
            && wrap.BlockBottom > lineTop + 0.5f
            && attempts < 4)
        {
            lineTop = wrap.BlockBottom;
            wrap = wrapResolver(lineTop, lineHeight, baseLeft, baseRight);
            attempts++;
        }

        return wrap;
    }

    private static DocRect InflateBounds(DocRect bounds, DocThickness distance)
    {
        var x = bounds.X - distance.Left;
        var y = bounds.Y - distance.Top;
        var width = bounds.Width + distance.Left + distance.Right;
        var height = bounds.Height + distance.Top + distance.Bottom;
        return new DocRect(x, y, MathF.Max(0f, width), MathF.Max(0f, height));
    }

    private static bool LineOverlaps(DocRect bounds, float lineTop, float lineHeight)
    {
        var lineBottom = lineTop + lineHeight;
        return lineTop < bounds.Bottom && lineBottom > bounds.Top;
    }

    private static bool AreBoundsClose(DocRect left, DocRect right, float epsilon)
    {
        return MathF.Abs(left.X - right.X) <= epsilon
               && MathF.Abs(left.Y - right.Y) <= epsilon
               && MathF.Abs(left.Width - right.Width) <= epsilon
               && MathF.Abs(left.Height - right.Height) <= epsilon;
    }

    private static Dictionary<int, HashSet<int>> CloneNoteMap(Dictionary<int, HashSet<int>> source)
    {
        var result = new Dictionary<int, HashSet<int>>(source.Count);
        foreach (var pair in source)
        {
            result[pair.Key] = new HashSet<int>(pair.Value);
        }

        return result;
    }

    private static float ResolveAnchorX(FloatingAnchor anchor, LayoutLine line, PageLayout page, PageSectionSettings section, float width)
    {
        var (referenceX, referenceWidth) = ResolveHorizontalReferenceBounds(anchor, line, page, section);
        var alignedX = anchor.HorizontalAlignment switch
        {
            FloatingHorizontalAlignment.Center => referenceX + (referenceWidth - width) / 2f,
            FloatingHorizontalAlignment.Right or FloatingHorizontalAlignment.Outside => referenceX + referenceWidth - width,
            FloatingHorizontalAlignment.Left or FloatingHorizontalAlignment.Inside => referenceX,
            _ => referenceX
        };

        return alignedX;
    }

    private static float ResolveAnchorY(FloatingAnchor anchor, LayoutLine line, PageLayout page, float height)
    {
        var (referenceY, referenceHeight) = ResolveVerticalReferenceBounds(anchor, line, page);
        var alignedY = anchor.VerticalAlignment switch
        {
            FloatingVerticalAlignment.Center => referenceY + (referenceHeight - height) / 2f,
            FloatingVerticalAlignment.Bottom or FloatingVerticalAlignment.Outside => referenceY + referenceHeight - height,
            FloatingVerticalAlignment.Top or FloatingVerticalAlignment.Inside => referenceY,
            _ => referenceY
        };

        return alignedY;
    }

    private static (float X, float Width) ResolveHorizontalReferenceBounds(
        FloatingAnchor anchor,
        LayoutLine line,
        PageLayout page,
        PageSectionSettings section)
    {
        switch (anchor.HorizontalReference)
        {
            case FloatingHorizontalReference.Page:
                return (page.Bounds.X, page.Bounds.Width);
            case FloatingHorizontalReference.Margin:
                return (page.ContentBounds.X, page.ContentBounds.Width);
            case FloatingHorizontalReference.Column:
            {
                var columnLeft = ResolveColumnLeft(line.X, page, section);
                var columnWidth = ResolveColumnWidth(line.X, page, section);
                return (columnLeft, columnWidth);
            }
            case FloatingHorizontalReference.Paragraph:
            case FloatingHorizontalReference.Character:
                return (line.X, MathF.Max(1f, line.Width));
            default:
                return (page.ContentBounds.X, page.ContentBounds.Width);
        }
    }

    private static (float Y, float Height) ResolveVerticalReferenceBounds(FloatingAnchor anchor, LayoutLine line, PageLayout page)
    {
        switch (anchor.VerticalReference)
        {
            case FloatingVerticalReference.Page:
                return (page.Bounds.Y, page.Bounds.Height);
            case FloatingVerticalReference.Margin:
                return (page.ContentBounds.Y, page.ContentBounds.Height);
            case FloatingVerticalReference.Line:
            case FloatingVerticalReference.Paragraph:
                return (line.Y, MathF.Max(1f, line.LineHeight));
            default:
                return (page.ContentBounds.Y, page.ContentBounds.Height);
        }
    }

    private static float ResolveColumnLeft(float lineX, PageLayout page, PageSectionSettings section)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var columnGap = MathF.Max(0f, section.ColumnGap);
        var columnWidths = ResolveSectionColumnWidths(section, page.ContentBounds.Width, columnGap);
        var offsets = BuildColumnOffsets(columnWidths, columnGap);
        if (offsets.Length == 0)
        {
            return page.ContentBounds.X;
        }

        var relativeX = lineX - page.ContentBounds.X;
        var columnIndex = 0;
        for (var i = offsets.Length - 1; i >= 0; i--)
        {
            if (relativeX >= offsets[i] - 0.5f)
            {
                columnIndex = i;
                break;
            }
        }

        columnIndex = Math.Clamp(columnIndex, 0, Math.Max(0, columnCount - 1));
        return page.ContentBounds.X + offsets[columnIndex];
    }

    private static float ResolveColumnWidth(float lineX, PageLayout page, PageSectionSettings section)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var columnGap = MathF.Max(0f, section.ColumnGap);
        var columnWidths = ResolveSectionColumnWidths(section, page.ContentBounds.Width, columnGap);
        if (columnWidths.Length == 0)
        {
            return page.ContentBounds.Width;
        }

        var offsets = BuildColumnOffsets(columnWidths, columnGap);
        var relativeX = lineX - page.ContentBounds.X;
        var columnIndex = 0;
        for (var i = offsets.Length - 1; i >= 0; i--)
        {
            if (relativeX >= offsets[i] - 0.5f)
            {
                columnIndex = i;
                break;
            }
        }

        columnIndex = Math.Clamp(columnIndex, 0, Math.Max(0, columnCount - 1));
        return columnWidths[Math.Clamp(columnIndex, 0, columnWidths.Length - 1)];
    }
    private static TableLayout LayoutTable(
        Document document,
        TableBlock table,
        float tableX,
        float tableY,
        float contentWidth,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        float lineHeight,
        float ascent)
    {
        var tableStyle = styleResolver.ResolveTableStyle(table);
        var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
        var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
        var paragraphIndex = 0;
        var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, contentWidth, settings, measurer, style, styleResolver, lineHeight, ascent, ref paragraphIndex);
        return BuildTableLayout(table, resolvedTableProperties, data, tableX, tableY, 0, data.RowHeights.Length, settings);
    }

    private sealed class TableCellPlacement
    {
        public TableCellPlacement(TableCell cell, int rowIndex, int columnIndex, int columnSpan)
        {
            Cell = cell;
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
            ColumnSpan = columnSpan;
        }

        public TableCell Cell { get; }
        public int RowIndex { get; }
        public int ColumnIndex { get; }
        public int ColumnSpan { get; }
        public int RowSpan { get; set; } = 1;
        public TableCellPlacement? MergeOrigin { get; set; }
        public TableCellProperties Properties { get; set; } = new TableCellProperties();
        public List<TableCellLine> Lines { get; set; } = new List<TableCellLine>();
        public DocThickness Padding { get; set; }
        public float ContentHeight { get; set; }
        public bool IsMergeContinuation => MergeOrigin is not null;
    }

    private enum BalanceItemKind
    {
        Line,
        Table
    }

    private readonly record struct BalanceItem(
        BalanceItemKind Kind,
        int Index,
        int AnchorIndex,
        float X,
        float Y,
        float Height,
        int ParagraphIndex,
        int StartOffset);

    private sealed record TableLayoutData(
        float[] ColumnWidths,
        float[] RowHeights,
        List<TableCellPlacement> Cells,
        int Columns,
        int Rows);

    private static TableLayoutData ComputeTableLayoutData(
        TableBlock table,
        Document document,
        TableProperties resolvedTableProperties,
        TableProperties directTableProperties,
        TableStyleDefinition? tableStyle,
        TableLook tableLook,
        float contentWidth,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        float lineHeight,
        float ascent,
        ref int paragraphIndex)
    {
        var rows = table.Rows;
        var rowCount = rows.Count;
        var columnCount = ResolveTableColumnCount(rows, resolvedTableProperties);
        var columnWidths = ResolveColumnWidths(resolvedTableProperties, columnCount, contentWidth);
        var rowHeights = new float[rowCount];
        var rowCanExpand = new bool[rowCount];
        var placements = new List<TableCellPlacement>();
        var grid = new TableCellPlacement?[rowCount, columnCount];
        var placementsByRow = new List<TableCellPlacement>[rowCount];
        var defaultPadding = DocThickness.Uniform(settings.TableCellPadding);
        var tablePadding = ResolveTablePadding(resolvedTableProperties.CellPadding, defaultPadding);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            placementsByRow[rowIndex] = new List<TableCellPlacement>();
            var row = rows[rowIndex];
            var columnIndex = 0;
            foreach (var cell in row.Cells)
            {
                while (columnIndex < columnCount && grid[rowIndex, columnIndex] is not null)
                {
                    columnIndex++;
                }

                if (columnIndex >= columnCount)
                {
                    break;
                }

                var span = Math.Clamp(cell.ColumnSpan, 1, columnCount - columnIndex);
                var placement = new TableCellPlacement(cell, rowIndex, columnIndex, span);
                placements.Add(placement);
                placementsByRow[rowIndex].Add(placement);
                for (var i = 0; i < span; i++)
                {
                    grid[rowIndex, columnIndex + i] = placement;
                }

                columnIndex += span;
            }
        }

        foreach (var placement in placements)
        {
            if (placement.Cell.VerticalMerge != TableCellVerticalMerge.Restart)
            {
                placement.RowSpan = 1;
                continue;
            }

            var rowSpan = 1;
            for (var nextRow = placement.RowIndex + 1; nextRow < rowCount; nextRow++)
            {
                var nextPlacement = grid[nextRow, placement.ColumnIndex];
                if (nextPlacement is null || nextPlacement.Cell.VerticalMerge != TableCellVerticalMerge.Continue)
                {
                    break;
                }

                if (nextPlacement.ColumnSpan != placement.ColumnSpan)
                {
                    break;
                }

                if (nextPlacement.MergeOrigin is null)
                {
                    nextPlacement.MergeOrigin = placement;
                }

                rowSpan++;
            }

            placement.RowSpan = rowSpan;
        }

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = rows[rowIndex];
            var maxHeight = lineHeight + defaultPadding.Vertical;
            foreach (var placement in placementsByRow[rowIndex])
            {
                var effectiveProperties = ResolveTableCellProperties(
                    placement.Cell,
                    directTableProperties,
                    tableStyle,
                    tableLook,
                    rowIndex,
                    placement.ColumnIndex,
                    rowCount,
                    columnCount);
                placement.Properties = effectiveProperties;
                placement.Padding = ResolvePadding(effectiveProperties.Padding, tablePadding);

                if (!placement.IsMergeContinuation)
                {
                    var spanWidth = SumColumns(columnWidths, placement.ColumnIndex, placement.ColumnSpan);
                    var cellLines = LayoutCellParagraphs(placement.Cell, document, spanWidth, placement.Padding, settings, measurer, style, styleResolver, lineHeight, ascent, ref paragraphIndex);
                    placement.Lines = cellLines;
                    placement.ContentHeight = cellLines.Count == 0 ? 0f : cellLines.Last().Y + cellLines.Last().LineHeight;

                    if (placement.RowSpan <= 1)
                    {
                        var cellHeight = placement.ContentHeight + placement.Padding.Vertical;
                        var minHeight = lineHeight + placement.Padding.Vertical;
                        maxHeight = MathF.Max(maxHeight, MathF.Max(minHeight, cellHeight));
                    }
                }
            }

            rowCanExpand[rowIndex] = true;
            if (row.Properties.Height.HasValue)
            {
                var height = MathF.Max(0f, row.Properties.Height.Value);
                var rule = row.Properties.HeightRule ?? TableRowHeightRule.AtLeast;
                if (rule == TableRowHeightRule.Exact)
                {
                    maxHeight = height;
                    rowCanExpand[rowIndex] = false;
                }
                else
                {
                    maxHeight = MathF.Max(maxHeight, height);
                }
            }

            rowHeights[rowIndex] = maxHeight;
        }

        foreach (var placement in placements)
        {
            if (placement.IsMergeContinuation || placement.RowSpan <= 1)
            {
                continue;
            }

            var requiredHeight = MathF.Max(lineHeight + placement.Padding.Vertical, placement.ContentHeight + placement.Padding.Vertical);
            var startRow = placement.RowIndex;
            var endRow = Math.Min(rowCount, startRow + placement.RowSpan);
            var spanHeight = 0f;
            for (var rowIndex = startRow; rowIndex < endRow; rowIndex++)
            {
                spanHeight += rowHeights[rowIndex];
            }

            if (requiredHeight > spanHeight)
            {
                var delta = requiredHeight - spanHeight;
                var targetRow = -1;
                for (var rowIndex = endRow - 1; rowIndex >= startRow; rowIndex--)
                {
                    if (rowCanExpand[rowIndex])
                    {
                        targetRow = rowIndex;
                        break;
                    }
                }

                if (targetRow < 0)
                {
                    targetRow = endRow - 1;
                }

                rowHeights[targetRow] += delta;
            }
        }

        return new TableLayoutData(columnWidths, rowHeights, placements, columnCount, rowCount);
    }

    private static TableLayout BuildTableLayout(
        TableBlock table,
        TableProperties tableProperties,
        TableLayoutData data,
        float tableX,
        float tableY,
        int rowStart,
        int rowCount,
        LayoutSettings settings)
    {
        var columnCount = data.Columns;
        var cellLayouts = new List<TableCellLayout>();
        var columnOffsets = new float[columnCount];
        var offset = 0f;
        for (var i = 0; i < columnCount; i++)
        {
            columnOffsets[i] = offset;
            offset += data.ColumnWidths[i];
        }

        var rowOffsets = new float[rowCount];
        var rowOffset = 0f;
        for (var i = 0; i < rowCount; i++)
        {
            rowOffsets[i] = rowOffset;
            rowOffset += data.RowHeights[rowStart + i];
        }

        var proxyOrigins = new HashSet<(int Row, int Column)>();
        foreach (var placement in data.Cells.OrderBy(cell => cell.RowIndex).ThenBy(cell => cell.ColumnIndex))
        {
            var localRowIndex = placement.RowIndex - rowStart;
            if (localRowIndex < 0 || localRowIndex >= rowCount)
            {
                continue;
            }

            if (placement.IsMergeContinuation && placement.MergeOrigin is { } origin && origin.RowIndex < rowStart)
            {
                var key = (origin.RowIndex, origin.ColumnIndex);
                if (!proxyOrigins.Add(key))
                {
                    continue;
                }

                var spanOffset = rowStart - origin.RowIndex;
                var remainingSpan = Math.Max(1, origin.RowSpan - spanOffset);
                var spanInChunk = Math.Min(remainingSpan, rowCount - localRowIndex);
                var cellX = tableX + columnOffsets[origin.ColumnIndex];
                var cellWidth = SumColumns(data.ColumnWidths, origin.ColumnIndex, origin.ColumnSpan);
                var cellY = tableY + rowOffsets[localRowIndex];
                var cellHeight = SumRows(data.RowHeights, rowStart + localRowIndex, spanInChunk);
                var cellBounds = new DocRect(cellX, cellY, cellWidth, cellHeight);
                cellLayouts.Add(new TableCellLayout(
                    placement.RowIndex,
                    origin.ColumnIndex,
                    origin.ColumnSpan,
                    spanInChunk,
                    cellBounds,
                    Array.Empty<TableCellLine>(),
                    origin.Properties,
                    origin.Padding,
                    true,
                    origin.RowIndex,
                    origin.ColumnIndex));
                continue;
            }

            if (placement.IsMergeContinuation)
            {
                continue;
            }

            var cellXOrigin = tableX + columnOffsets[placement.ColumnIndex];
            var cellWidthOrigin = SumColumns(data.ColumnWidths, placement.ColumnIndex, placement.ColumnSpan);
            var cellYOrigin = tableY + rowOffsets[localRowIndex];
            var spanInPage = Math.Min(placement.RowSpan, rowCount - localRowIndex);
            var cellHeightOrigin = SumRows(data.RowHeights, rowStart + localRowIndex, spanInPage);
            var cellBoundsOrigin = new DocRect(cellXOrigin, cellYOrigin, cellWidthOrigin, cellHeightOrigin);
            var lines = placement.Lines;
            var offsetLines = new List<TableCellLine>(lines.Count);
            var contentHeight = lines.Count == 0 ? 0f : lines.Last().Y + lines.Last().LineHeight;
            var availableHeight = MathF.Max(0f, cellHeightOrigin - placement.Padding.Vertical);
            var verticalOffset = 0f;
            if (contentHeight < availableHeight)
            {
                verticalOffset = (placement.Properties.VerticalAlignment ?? TableCellVerticalAlignment.Top) switch
                {
                    TableCellVerticalAlignment.Center => (availableHeight - contentHeight) / 2f,
                    TableCellVerticalAlignment.Bottom => availableHeight - contentHeight,
                    _ => 0f
                };
            }

            foreach (var line in lines)
            {
                offsetLines.Add(new TableCellLine(
                    line.ParagraphIndex,
                    line.StartOffset,
                    line.Length,
                    cellXOrigin + placement.Padding.Left + line.X,
                    cellYOrigin + placement.Padding.Top + verticalOffset + line.Y,
                    line.Width,
                    line.TextSlice,
                    line.Prefix,
                    line.PrefixWidth,
                    line.LineHeight,
                    line.Ascent,
                    line.Runs,
                    line.Images,
                    line.Shapes,
                    line.Charts,
                    line.Equations,
                    line.IsRtl));
            }

            cellLayouts.Add(new TableCellLayout(
                placement.RowIndex,
                placement.ColumnIndex,
                placement.ColumnSpan,
                spanInPage,
                cellBoundsOrigin,
                offsetLines,
                placement.Properties,
                placement.Padding,
                false,
                placement.RowIndex,
                placement.ColumnIndex));
        }

        var tableHeight = 0f;
        for (var i = 0; i < rowCount; i++)
        {
            tableHeight += data.RowHeights[rowStart + i];
        }

        var tableBounds = new DocRect(tableX, tableY, data.ColumnWidths.Sum(), tableHeight);
        var rowHeightsSlice = data.RowHeights.Skip(rowStart).Take(rowCount).ToArray();
        return new TableLayout(tableBounds, rowCount, columnCount, data.ColumnWidths, rowHeightsSlice, cellLayouts, tableProperties);
    }

    private static int CountRowsToFit(float[] rowHeights, int rowStart, float maxHeight)
    {
        if (maxHeight <= 0f)
        {
            return 0;
        }

        var height = 0f;
        var count = 0;
        for (var i = rowStart; i < rowHeights.Length; i++)
        {
            var rowHeight = rowHeights[i];
            if (count > 0 && height + rowHeight > maxHeight)
            {
                break;
            }

            if (count == 0 && rowHeight > maxHeight)
            {
                return 1;
            }

            height += rowHeight;
            count++;
        }

        return count;
    }

    private static List<TableCellLine> LayoutCellParagraphs(
        TableCell cell,
        Document document,
        float columnWidth,
        DocThickness padding,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        float lineHeight,
        float ascent,
        ref int paragraphIndex)
    {
        var availableWidth = MathF.Max(1f, columnWidth - padding.Horizontal);
        var lines = new List<TableCellLine>();
        var y = 0f;
        var listState = new ListNumberingState(document);
        ParagraphBlock? previousParagraph = null;

        foreach (var paragraph in cell.Paragraphs)
        {
            var currentParagraphIndex = paragraphIndex;
            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var spacingBefore = properties.SpacingBefore ?? settings.ParagraphSpacing;
            if (properties.ContextualSpacing == true && previousParagraph is not null)
            {
                var previousStyleId = previousParagraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                var currentStyleId = paragraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                if (!string.IsNullOrWhiteSpace(currentStyleId)
                    && string.Equals(previousStyleId, currentStyleId, StringComparison.OrdinalIgnoreCase))
                {
                    spacingBefore = 0f;
                }
            }
            var spacingAfter = properties.SpacingAfter ?? settings.ParagraphSpacing;
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;

            y += spacingBefore;

            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver);
            if (text.Length == 0)
            {
                var lineX = indentLeft + listIndent + firstLineIndent + prefixWidth;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(lineHeight, ascent, properties);
                var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                lines.Add(new TableCellLine(
                    currentParagraphIndex,
                    0,
                    0,
                    lineX,
                    y,
                    0f,
                    TextSlice.Empty,
                    prefix,
                    prefixWidth,
                    emptyLineHeight,
                    emptyAscent,
                    Array.Empty<LayoutRun>(),
                    Array.Empty<LayoutImage>(),
                    Array.Empty<LayoutShape>(),
                    Array.Empty<LayoutChart>(),
                    Array.Empty<LayoutEquation>(),
                    emptyIsRtl));
                y += emptyLineHeight;
                y += spacingAfter;
                paragraphIndex++;
                continue;
            }

            var baseWidth = MathF.Max(1f, availableWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var isFirstLine = true;
            foreach (var line in WrapParagraph(text, spans, firstLineWidth, otherLineWidth, properties, settings, measurer))
            {
                var lineIndent = indentLeft + listIndent + (isFirstLine ? firstLineIndent : 0f);
                var lineBaseX = lineIndent + prefixWidth;
                var lineLayout = BuildLineLayout(
                    spans,
                    line.Start,
                    line.Length,
                    properties.TabStops,
                    settings.DefaultTabWidth,
                    measurer,
                    lineHeight,
                    ascent);
                lineLayout = ApplyLineSpacing(lineLayout, properties);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer);
                }
                var alignment = properties.Alignment;
                if (!alignment.HasValue && properties.Bidi == true)
                {
                    alignment = ParagraphAlignment.Right;
                }
                var isLastLine = IsLastParagraphLine(text, line.Start + line.Length);
                var alignWidth = availableWidth - lineIndent - indentRight - prefixWidth;
                if (alignment == ParagraphAlignment.Justify && !isLastLine)
                {
                    lineLayout = JustifyLineLayout(lineLayout, alignWidth, measurer);
                }

                var alignedX = ApplyAlignment(lineBaseX, lineLayout.Width, alignWidth, alignment);
                var lineSlice = new TextSlice(text, line.Start, line.Length);
                var isRtl = ResolveLineIsRtl(properties, lineSlice);
                lines.Add(new TableCellLine(
                    currentParagraphIndex,
                    line.Start,
                    line.Length,
                    alignedX,
                    y,
                    lineLayout.Width,
                    lineSlice,
                    isFirstLine ? prefix : null,
                    isFirstLine ? prefixWidth : 0f,
                    lineLayout.LineHeight,
                    lineLayout.Ascent,
                    lineLayout.Runs,
                    lineLayout.Images,
                    lineLayout.Shapes,
                    lineLayout.Charts,
                    lineLayout.Equations,
                    isRtl));
                y += lineLayout.LineHeight;
                isFirstLine = false;
            }

            y += spacingAfter;
            paragraphIndex++;
            previousParagraph = paragraph;
        }

        return lines;
    }

    private static IEnumerable<ParagraphLineBreak> WrapParagraph(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth,
        ParagraphProperties properties,
        LayoutSettings settings,
        ITextMeasurer measurer)
    {
        return ParagraphLineBreaker.BreakParagraph(
            text,
            spans,
            firstLineWidth,
            otherLineWidth,
            measurer,
            (start, length) => MeasureInlineSpans(spans, start, length, properties.TabStops, settings.DefaultTabWidth, measurer));
    }

    private static int FindLineLength(string text, int start, float maxWidth, Func<int, int, float> measureWidth)
    {
        var length = 0;
        var lastBreak = -1;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ' || ch == '\t')
            {
                lastBreak = i;
            }

            var width = measureWidth(start, i - start + 1);
            if (width > maxWidth && i > start)
            {
                length = lastBreak >= start ? lastBreak - start : i - start;
                break;
            }

            length = i - start + 1;
        }

        if (length <= 0)
        {
            length = Math.Min(1, text.Length - start);
        }

        return length;
    }

    private static List<ParagraphLine> BuildParagraphLines(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float indentLeft,
        float indentRight,
        float firstLineIndent,
        float listIndent,
        float prefixWidth,
        ParagraphProperties properties,
        float contentWidth,
        float columnX,
        float startY,
        LayoutSettings settings,
        ITextMeasurer measurer,
        float lineHeight,
        float ascent,
        WrapResolver? wrapResolver)
    {
        var lines = new List<ParagraphLine>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var lineTop = startY;
        if (wrapResolver is null)
        {
            var baseLeft = columnX + indentLeft + listIndent + prefixWidth;
            var baseRight = columnX + contentWidth - indentRight;
            var firstLineWidth = MathF.Max(1f, baseRight - (columnX + indentLeft + listIndent + firstLineIndent + prefixWidth));
            var otherLineWidth = MathF.Max(1f, baseRight - baseLeft);
            var isFirstLineLocal = true;

            foreach (var line in WrapParagraph(text, spans, firstLineWidth, otherLineWidth, properties, settings, measurer))
            {
                var lineLayout = BuildLineLayout(
                    spans,
                    line.Start,
                    line.Length,
                    properties.TabStops,
                    settings.DefaultTabWidth,
                    measurer,
                    lineHeight,
                    ascent);
                lineLayout = ApplyLineSpacing(lineLayout, properties);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer);
                }

                lines.Add(new ParagraphLine(line.Start, line.Length, new TextSlice(text, line.Start, line.Length), lineLayout, isFirstLineLocal));
                lineTop += lineLayout.LineHeight;
                isFirstLineLocal = false;
            }

            return lines;
        }

        var start = 0;
        var isFirstLine = true;
        while (start < text.Length)
        {
            var lineIndent = indentLeft + listIndent + (isFirstLine ? firstLineIndent : 0f);
            var baseLeft = columnX + lineIndent + prefixWidth;
            var baseRight = columnX + contentWidth - indentRight;
            if (wrapResolver is not null)
            {
                var adjustedTop = lineTop;
                var wrap = ResolveWrapForLine(ref adjustedTop, lineHeight, baseLeft, baseRight, wrapResolver);
                baseLeft = wrap.Left;
                baseRight = wrap.Right;
                lineTop = adjustedTop;
            }

            var maxWidth = MathF.Max(1f, baseRight - baseLeft);
            var length = FindLineLength(text, start, maxWidth,
                (offset, runLength) => MeasureInlineSpans(spans, offset, runLength, properties.TabStops, settings.DefaultTabWidth, measurer));
            var textSlice = new TextSlice(text, start, length);

            var lineLayout = BuildLineLayout(
                spans,
                start,
                length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                lineHeight,
                ascent);
            lineLayout = ApplyLineSpacing(lineLayout, properties);
            lines.Add(new ParagraphLine(start, length, textSlice, lineLayout, isFirstLine));
            lineTop += lineLayout.LineHeight;
            start += length;
            isFirstLine = false;
            while (start < text.Length && text[start] == ' ')
            {
                start++;
            }
        }

        return lines;
    }

    private static int CountParagraphLinesThatFit(IReadOnlyList<ParagraphLine> lines, int startIndex, float availableHeight)
    {
        if (availableHeight <= 0f)
        {
            return 0;
        }

        var height = 0f;
        var count = 0;
        for (var i = startIndex; i < lines.Count; i++)
        {
            var lineHeight = lines[i].Layout.LineHeight;
            if (count > 0 && height + lineHeight > availableHeight)
            {
                break;
            }

            if (count == 0 && lineHeight > availableHeight)
            {
                return 0;
            }

            height += lineHeight;
            count++;
        }

        return count;
    }

    private static float EstimateNextBlockMinHeight(
        int blockIndex,
        IReadOnlyList<Block> blocks,
        Document document,
        DocumentStyleResolver styleResolver,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle defaultStyle,
        float contentWidth,
        float lineHeight,
        float ascent)
    {
        if (blockIndex + 1 >= blocks.Count)
        {
            return 0f;
        }

        var nextBlock = blocks[blockIndex + 1];
        if (nextBlock is PageBreakBlock || nextBlock is SectionBreakBlock || nextBlock is ColumnBreakBlock)
        {
            return 0f;
        }

        if (nextBlock is ParagraphBlock nextParagraph)
        {
            var properties = styleResolver.ResolveParagraphProperties(nextParagraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(nextParagraph, defaultStyle);
            var spacingBefore = properties.SpacingBefore ?? settings.ParagraphSpacing;
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;
            var listIndent = nextParagraph.ListInfo is null ? 0f : settings.ListIndent * (nextParagraph.ListInfo.Level + 1);
            var prefixWidth = 0f;

            var (text, spans) = BuildInlineSpans(nextParagraph, paragraphStyle, styleResolver);
            if (string.IsNullOrEmpty(text))
            {
                var (emptyLineHeight, _) = ApplyLineSpacing(lineHeight, ascent, properties);
                return spacingBefore + emptyLineHeight;
            }

            var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var firstLine = WrapParagraph(text, spans, firstLineWidth, otherLineWidth, properties, settings, measurer)
                .FirstOrDefault();
            if (firstLine.Length == 0)
            {
                var (emptyLineHeight, _) = ApplyLineSpacing(lineHeight, ascent, properties);
                return spacingBefore + emptyLineHeight;
            }

            var lineLayout = BuildLineLayout(
                spans,
                firstLine.Start,
                firstLine.Length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                lineHeight,
                ascent);
            lineLayout = ApplyLineSpacing(lineLayout, properties);
            return spacingBefore + lineLayout.LineHeight;
        }

        if (nextBlock is TableBlock table)
        {
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var paragraphIndex = 0;
            var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, contentWidth, settings, measurer, defaultStyle, styleResolver, lineHeight, ascent, ref paragraphIndex);
            return data.RowHeights.Length > 0 ? data.RowHeights[0] : lineHeight;
        }

        return 0f;
    }

    private static (string Text, List<InlineSpan> Spans) BuildInlineSpans(
        ParagraphBlock paragraph,
        TextStyle paragraphStyle,
        DocumentStyleResolver styleResolver,
        int? pageNumber = null,
        int? totalPages = null)
    {
        var spansList = new List<InlineSpan>();
        var builder = new System.Text.StringBuilder();
        var contentControls = new List<ContentControlState>();

        void AppendText(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var (effectiveStyle, baselineOffset) = PrepareRunStyle(style);
            AppendTextSpans(builder, spansList, text, effectiveStyle, baselineOffset);
        }

        void MarkContent()
        {
            if (contentControls.Count == 0)
            {
                return;
            }

            foreach (var state in contentControls)
            {
                state.HasContent = true;
            }
        }

        void AppendContent(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            AppendText(text, style);
            MarkContent();
        }

        if (paragraph.Inlines.Count == 0)
        {
            AppendText(paragraph.Text ?? string.Empty, paragraphStyle);
            return (builder.ToString(), spansList);
        }

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline runInline:
                {
                    var text = runInline.GetText();
                    if (text.Length == 0)
                    {
                        break;
                    }

                    var runStyle = styleResolver.ResolveRunStyle(paragraph, runInline, paragraphStyle);
                    AppendContent(text, runStyle);
                    break;
                }
                case ImageInline imageInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, imageInline, null, null, null, 0f));
                    MarkContent();
                    break;
                }
                case ShapeInline shapeInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, null, shapeInline, null, null, 0f));
                    MarkContent();
                    break;
                }
                case ChartInline chartInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, null, null, chartInline, null, 0f));
                    MarkContent();
                    break;
                }
                case EquationInline equationInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    var equationStyle = styleResolver.ResolveRunStyle(equationInline.StyleId, equationInline.Style, paragraphStyle);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, equationStyle, null, null, null, equationInline, 0f));
                    MarkContent();
                    break;
                }
                case PageNumberInline pageNumberInline:
                {
                    var text = pageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    if (text.Length == 0)
                    {
                        break;
                    }

                    AppendContent(text, pageNumberInline.Style?.Clone() ?? paragraphStyle);
                    break;
                }
                case TotalPagesInline totalPagesInline:
                {
                    var text = totalPages?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    if (text.Length == 0)
                    {
                        break;
                    }

                    AppendContent(text, totalPagesInline.Style?.Clone() ?? paragraphStyle);
                    break;
                }
                case FootnoteReferenceInline footnoteReference:
                {
                    var text = footnoteReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var refStyle = styleResolver.ResolveRunStyle(footnoteReference.StyleId, footnoteReference.Style, paragraphStyle);
                    refStyle.VerticalPosition = DocVerticalPosition.Superscript;
                    AppendContent(text, refStyle);
                    break;
                }
                case EndnoteReferenceInline endnoteReference:
                {
                    var text = endnoteReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var refStyle = styleResolver.ResolveRunStyle(endnoteReference.StyleId, endnoteReference.Style, paragraphStyle);
                    refStyle.VerticalPosition = DocVerticalPosition.Superscript;
                    AppendContent(text, refStyle);
                    break;
                }
                case CommentReferenceInline commentReference:
                {
                    var text = commentReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var refStyle = styleResolver.ResolveRunStyle(commentReference.StyleId, commentReference.Style, paragraphStyle);
                    refStyle.VerticalPosition = DocVerticalPosition.Superscript;
                    AppendContent(text, refStyle);
                    break;
                }
                case ContentControlStartInline controlStart:
                {
                    var placeholderText = ResolvePlaceholderText(controlStart.Properties);
                    contentControls.Add(new ContentControlState(controlStart.Properties, paragraphStyle, placeholderText));
                    break;
                }
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
                case ContentControlEndInline:
                {
                    if (contentControls.Count == 0)
                    {
                        break;
                    }

                    var state = contentControls[^1];
                    contentControls.RemoveAt(contentControls.Count - 1);
                    if (!state.HasContent && state.ShouldShowPlaceholder && !string.IsNullOrWhiteSpace(state.PlaceholderText))
                    {
                        AppendText(state.PlaceholderText, state.PlaceholderStyle);
                        MarkContent();
                    }

                    break;
                }
                case FieldStartInline:
                case FieldSeparatorInline:
                case FieldEndInline:
                case BookmarkStartInline:
                case BookmarkEndInline:
                case CommentRangeStartInline:
                case CommentRangeEndInline:
                    break;
            }
        }

        if (builder.Length == 0)
        {
            AppendText(paragraph.Text ?? string.Empty, paragraphStyle);
        }

        return (builder.ToString(), spansList);
    }

    private const float SuperscriptScale = 0.65f;
    private const float SubscriptScale = 0.65f;
    private const float SuperscriptOffsetRatio = 0.35f;
    private const float SubscriptOffsetRatio = 0.15f;
    private const float SmallCapsScale = 0.8f;
    private static readonly MathLayoutEngine SharedMathLayoutEngine = new MathLayoutEngine();
    private static readonly DocColor PlaceholderColor = new DocColor(150, 150, 150);

    private static (TextStyle Style, float BaselineOffset) PrepareRunStyle(TextStyle style)
    {
        var effective = style.Clone();
        var baselineOffset = 0f;
        if (style.VerticalPosition == DocVerticalPosition.Superscript)
        {
            baselineOffset = style.FontSize * SuperscriptOffsetRatio;
            effective.FontSize = MathF.Max(1f, style.FontSize * SuperscriptScale);
            if (style.FontSizeComplexScript.HasValue)
            {
                effective.FontSizeComplexScript = MathF.Max(1f, style.FontSizeComplexScript.Value * SuperscriptScale);
            }
        }
        else if (style.VerticalPosition == DocVerticalPosition.Subscript)
        {
            baselineOffset = -style.FontSize * SubscriptOffsetRatio;
            effective.FontSize = MathF.Max(1f, style.FontSize * SubscriptScale);
            if (style.FontSizeComplexScript.HasValue)
            {
                effective.FontSizeComplexScript = MathF.Max(1f, style.FontSizeComplexScript.Value * SubscriptScale);
            }
        }

        effective.VerticalPosition = DocVerticalPosition.Normal;
        return (effective, baselineOffset);
    }

    private static TextStyle CreatePlaceholderStyle(TextStyle paragraphStyle)
    {
        var style = paragraphStyle.Clone();
        style.Color = PlaceholderColor;
        style.FontStyle = DocFontStyle.Italic;
        return style;
    }

    private static string ResolvePlaceholderText(ContentControlProperties properties)
    {
        if (!string.IsNullOrWhiteSpace(properties.PlaceholderText))
        {
            return properties.PlaceholderText;
        }

        if (!string.IsNullOrWhiteSpace(properties.Placeholder))
        {
            return properties.Placeholder;
        }

        if (!string.IsNullOrWhiteSpace(properties.Alias))
        {
            return properties.Alias;
        }

        if (!string.IsNullOrWhiteSpace(properties.Tag))
        {
            return properties.Tag;
        }

        return "Content Control";
    }

    private static bool ResolveLineIsRtl(ParagraphProperties properties, TextSlice textSlice)
    {
        return TextBidi.ResolveBaseIsRtl(textSlice.Span, properties.Bidi);
    }

    private static string ResolveAltChunkLabel(AltChunkBlock altChunk)
    {
        if (!string.IsNullOrWhiteSpace(altChunk.Label))
        {
            return altChunk.Label;
        }

        if (!string.IsNullOrWhiteSpace(altChunk.ContentType))
        {
            return $"AltChunk ({altChunk.ContentType})";
        }

        if (!string.IsNullOrWhiteSpace(altChunk.TargetUri))
        {
            return $"AltChunk ({altChunk.TargetUri})";
        }

        return "AltChunk";
    }

    private static void AppendTextSpans(
        System.Text.StringBuilder builder,
        List<InlineSpan> spans,
        string text,
        TextStyle style,
        float baselineOffset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (RequiresScriptSegmentation(style))
        {
            AppendScriptSpans(builder, spans, text, style, baselineOffset);
            return;
        }

        AppendSegmentSpan(builder, spans, text, style, baselineOffset);
    }

    private static bool RequiresScriptSegmentation(TextStyle style)
    {
        return !string.IsNullOrWhiteSpace(style.FontFamilyAscii)
               || !string.IsNullOrWhiteSpace(style.FontFamilyHighAnsi)
               || !string.IsNullOrWhiteSpace(style.FontFamilyEastAsia)
               || !string.IsNullOrWhiteSpace(style.FontFamilyComplexScript)
               || style.FontSizeComplexScript.HasValue
               || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
               || !string.IsNullOrWhiteSpace(style.LanguageBidi);
    }

    private static void AppendScriptSpans(
        System.Text.StringBuilder builder,
        List<InlineSpan> spans,
        string text,
        TextStyle style,
        float baselineOffset)
    {
        var index = 0;
        var segmentStart = 0;
        var currentScript = TextScriptKind.Neutral;
        var lastStrong = TextScriptKind.Latin;

        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed))
            {
                rune = new System.Text.Rune(text[index]);
                consumed = 1;
            }

            var script = TextScript.ClassifyRune(rune);
            if (script == TextScriptKind.Neutral)
            {
                script = lastStrong;
            }
            else
            {
                lastStrong = script;
            }

            if (currentScript == TextScriptKind.Neutral)
            {
                currentScript = script;
                segmentStart = index;
            }
            else if (script != currentScript)
            {
                AppendScriptSegment(builder, spans, text, segmentStart, index - segmentStart, style, currentScript, baselineOffset);
                currentScript = script;
                segmentStart = index;
            }

            index += consumed;
        }

        if (segmentStart < text.Length)
        {
            AppendScriptSegment(builder, spans, text, segmentStart, text.Length - segmentStart, style, currentScript, baselineOffset);
        }
    }

    private static void AppendScriptSegment(
        System.Text.StringBuilder builder,
        List<InlineSpan> spans,
        string text,
        int start,
        int length,
        TextStyle style,
        TextScriptKind script,
        float baselineOffset)
    {
        if (length <= 0)
        {
            return;
        }

        var segmentText = text.Substring(start, length);
        var segmentStyle = ResolveScriptStyle(style, script);
        AppendSegmentSpan(builder, spans, segmentText, segmentStyle, baselineOffset);
    }

    private static TextStyle ResolveScriptStyle(TextStyle style, TextScriptKind script)
    {
        var family = style.FontFamily;
        var size = style.FontSize;
        var language = style.Language;

        switch (script)
        {
            case TextScriptKind.EastAsian:
                family = style.FontFamilyEastAsia ?? family;
                language = style.LanguageEastAsia ?? language;
                break;
            case TextScriptKind.Complex:
                family = style.FontFamilyComplexScript ?? family;
                if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0)
                {
                    size = style.FontSizeComplexScript.Value;
                }
                language = style.LanguageBidi ?? language;
                break;
            case TextScriptKind.Latin:
                family = style.FontFamilyAscii ?? style.FontFamilyHighAnsi ?? family;
                break;
        }

        if (string.Equals(family, style.FontFamily, StringComparison.Ordinal)
            && size.Equals(style.FontSize)
            && string.Equals(language, style.Language, StringComparison.Ordinal))
        {
            return style;
        }

        var resolved = style.Clone();
        resolved.FontFamily = family;
        resolved.FontSize = size;
        resolved.Language = language;
        return resolved;
    }

    private static void AppendSegmentSpan(
        System.Text.StringBuilder builder,
        List<InlineSpan> spans,
        string text,
        TextStyle style,
        float baselineOffset)
    {
        if (!style.SmallCaps)
        {
            var start = builder.Length;
            builder.Append(text);
            spans.Add(new InlineSpan(start, text.Length, text, style, null, null, null, null, baselineOffset));
            return;
        }

        var normalStyle = style.Clone();
        normalStyle.SmallCaps = false;
        var smallCapsStyle = style.Clone();
        smallCapsStyle.SmallCaps = false;
        smallCapsStyle.FontSize = MathF.Max(1f, style.FontSize * SmallCapsScale);

        var segmentBuilder = new System.Text.StringBuilder();
        bool? currentSmallCaps = null;
        var segmentStart = builder.Length;

        void FlushSegment()
        {
            if (segmentBuilder.Length == 0 || currentSmallCaps is null)
            {
                return;
            }

            var segmentText = segmentBuilder.ToString();
            var segmentStyle = currentSmallCaps.Value ? smallCapsStyle : normalStyle;
            spans.Add(new InlineSpan(segmentStart, segmentText.Length, segmentText, segmentStyle, null, null, null, null, baselineOffset));
            segmentBuilder.Clear();
        }

        foreach (var ch in text)
        {
            var isLower = char.IsLetter(ch) && char.IsLower(ch);
            if (currentSmallCaps.HasValue && currentSmallCaps.Value != isLower)
            {
                FlushSegment();
                currentSmallCaps = null;
            }

            if (!currentSmallCaps.HasValue)
            {
                currentSmallCaps = isLower;
                segmentStart = builder.Length;
            }

            var outputChar = isLower ? char.ToUpperInvariant(ch) : ch;
            segmentBuilder.Append(outputChar);
            builder.Append(outputChar);
        }

        FlushSegment();
    }

    private enum NoteKind
    {
        Footnote,
        Endnote
    }

    private sealed record NoteReference(int Offset, int Length, int Id, NoteKind Kind);

    private sealed record CommentMarker(int Offset, int Id, bool IsStart);

    private sealed record CommentRange(int Id, TextRange Range);

    private sealed record InlineScanResult(int TextLength, List<NoteReference> NoteReferences, List<CommentMarker> CommentMarkers);

    private static InlineScanResult ScanParagraphInlines(ParagraphBlock paragraph, int? pageNumber = null, int? totalPages = null)
    {
        var noteReferences = new List<NoteReference>();
        var commentMarkers = new List<CommentMarker>();
        var length = 0;

        void AppendLength(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            length += text.Length;
        }

        if (paragraph.Inlines.Count == 0)
        {
            length = paragraph.Text?.Length ?? 0;
            return new InlineScanResult(length, noteReferences, commentMarkers);
        }

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline runInline:
                    AppendLength(runInline.GetText());
                    break;
                case ImageInline:
                    length += 1;
                    break;
                case ChartInline:
                    length += 1;
                    break;
                case EquationInline:
                    length += 1;
                    break;
                case PageNumberInline:
                {
                    var text = pageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    AppendLength(text);
                    break;
                }
                case TotalPagesInline:
                {
                    var text = totalPages?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    AppendLength(text);
                    break;
                }
                case FootnoteReferenceInline footnoteReference:
                {
                    var text = footnoteReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    noteReferences.Add(new NoteReference(length, text.Length, footnoteReference.Id, NoteKind.Footnote));
                    AppendLength(text);
                    break;
                }
                case EndnoteReferenceInline endnoteReference:
                {
                    var text = endnoteReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    noteReferences.Add(new NoteReference(length, text.Length, endnoteReference.Id, NoteKind.Endnote));
                    AppendLength(text);
                    break;
                }
                case CommentReferenceInline commentReference:
                {
                    var text = commentReference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    AppendLength(text);
                    break;
                }
                case CommentRangeStartInline commentStart:
                    commentMarkers.Add(new CommentMarker(length, commentStart.Id, true));
                    break;
                case CommentRangeEndInline commentEnd:
                    commentMarkers.Add(new CommentMarker(length, commentEnd.Id, false));
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
            }
        }

        if (length == 0)
        {
            length = paragraph.Text?.Length ?? 0;
        }

        return new InlineScanResult(length, noteReferences, commentMarkers);
    }

    private static (List<HeaderFooterLine> Lines, float Height) LayoutHeaderFooterBlocks(
        IReadOnlyList<Block> blocks,
        Document document,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        float contentWidth,
        float lineHeight,
        float ascent,
        int pageNumber,
        int totalPages)
    {
        if (blocks.Count == 0)
        {
            return (new List<HeaderFooterLine>(), 0f);
        }

        var lines = new List<HeaderFooterLine>();
        var y = 0f;
        var listState = new ListNumberingState(document);
        ParagraphBlock? previousParagraph = null;

        foreach (var block in blocks)
        {
            if (block is not ParagraphBlock paragraph)
            {
                continue;
            }

            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var spacingBefore = properties.SpacingBefore ?? settings.ParagraphSpacing;
            if (properties.ContextualSpacing == true && previousParagraph is not null)
            {
                var previousStyleId = previousParagraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                var currentStyleId = paragraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
                if (!string.IsNullOrWhiteSpace(currentStyleId)
                    && string.Equals(previousStyleId, currentStyleId, StringComparison.OrdinalIgnoreCase))
                {
                    spacingBefore = 0f;
                }
            }
            var spacingAfter = properties.SpacingAfter ?? settings.ParagraphSpacing;
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;

            y += spacingBefore;

            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver, pageNumber, totalPages);
            if (text.Length == 0)
            {
                var lineX = indentLeft + listIndent + firstLineIndent + prefixWidth;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(lineHeight, ascent, properties);
                var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                lines.Add(new HeaderFooterLine(lineX, y, 0f, TextSlice.Empty, prefix, prefixWidth, emptyLineHeight, emptyAscent, Array.Empty<LayoutRun>(), Array.Empty<LayoutImage>(), Array.Empty<LayoutShape>(), Array.Empty<LayoutChart>(), Array.Empty<LayoutEquation>(), emptyIsRtl));
                y += emptyLineHeight;
                y += spacingAfter;
                continue;
            }

            var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var isFirstLine = true;
            foreach (var line in WrapParagraph(text, spans, firstLineWidth, otherLineWidth, properties, settings, measurer))
            {
                var lineIndent = indentLeft + listIndent + (isFirstLine ? firstLineIndent : 0f);
                var lineBaseX = lineIndent + prefixWidth;
                var lineLayout = BuildLineLayout(
                    spans,
                    line.Start,
                    line.Length,
                    properties.TabStops,
                    settings.DefaultTabWidth,
                    measurer,
                    lineHeight,
                    ascent);
                lineLayout = ApplyLineSpacing(lineLayout, properties);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer);
                }
                var alignment = properties.Alignment;
                if (!alignment.HasValue && properties.Bidi == true)
                {
                    alignment = ParagraphAlignment.Right;
                }
                var isLastLine = IsLastParagraphLine(text, line.Start + line.Length);
                var alignWidth = contentWidth - lineIndent - indentRight - prefixWidth;
                if (alignment == ParagraphAlignment.Justify && !isLastLine)
                {
                    lineLayout = JustifyLineLayout(lineLayout, alignWidth, measurer);
                }

                var alignedX = ApplyAlignment(lineBaseX, lineLayout.Width, alignWidth, alignment);
                var lineSlice = new TextSlice(text, line.Start, line.Length);
                var isRtl = ResolveLineIsRtl(properties, lineSlice);
                lines.Add(new HeaderFooterLine(
                    alignedX,
                    y,
                    lineLayout.Width,
                    lineSlice,
                    isFirstLine ? prefix : null,
                    isFirstLine ? prefixWidth : 0f,
                    lineLayout.LineHeight,
                    lineLayout.Ascent,
                    lineLayout.Runs,
                    lineLayout.Images,
                    lineLayout.Shapes,
                    lineLayout.Charts,
                    lineLayout.Equations,
                    isRtl));
                y += lineLayout.LineHeight;
                isFirstLine = false;
            }

            y += spacingAfter;
            previousParagraph = paragraph;
        }

        return (lines, y);
    }

    private static List<HeaderFooterLine> OffsetHeaderFooterLines(IReadOnlyList<HeaderFooterLine> lines, float offsetX, float offsetY)
    {
        if (lines.Count == 0)
        {
            return new List<HeaderFooterLine>();
        }

        var result = new List<HeaderFooterLine>(lines.Count);
        foreach (var line in lines)
        {
            result.Add(line with { X = line.X + offsetX, Y = line.Y + offsetY });
        }

        return result;
    }

    private static List<FootnoteLayout> BuildFootnoteLayouts(
        Document document,
        IReadOnlyList<PageLayout> pages,
        IReadOnlyList<PageSectionSettings> pageSections,
        IReadOnlyList<HeaderFooterLayout> headerFooters,
        IReadOnlyDictionary<int, HashSet<int>> footnotesByPage,
        IReadOnlyDictionary<int, HashSet<int>> endnotesByPage,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        float lineHeight,
        float ascent)
    {
        var footnoteLayouts = new List<FootnoteLayout>();
        if (pages.Count == 0 || (footnotesByPage.Count == 0 && endnotesByPage.Count == 0))
        {
            return footnoteLayouts;
        }

        var headerFooterMap = headerFooters.ToDictionary(layout => layout.PageIndex);
        var totalPages = Math.Max(1, pages.Count);

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            footnotesByPage.TryGetValue(pageIndex, out var footnoteIds);
            endnotesByPage.TryGetValue(pageIndex, out var endnoteIds);
            if ((footnoteIds is null || footnoteIds.Count == 0) && (endnoteIds is null || endnoteIds.Count == 0))
            {
                continue;
            }

            var blocks = new List<Block>();
            if (footnoteIds is not null)
            {
                foreach (var id in footnoteIds.OrderBy(id => id))
                {
                    if (document.Footnotes.TryGetValue(id, out var definition))
                    {
                        blocks.AddRange(definition.Blocks);
                    }
                }
            }

            if (endnoteIds is not null)
            {
                foreach (var id in endnoteIds.OrderBy(id => id))
                {
                    if (document.Endnotes.TryGetValue(id, out var definition))
                    {
                        blocks.AddRange(definition.Blocks);
                    }
                }
            }

            if (blocks.Count == 0)
            {
                continue;
            }

            var page = pages[pageIndex];
            var section = pageSections[Math.Clamp(pageIndex, 0, pageSections.Count - 1)];
            var contentWidth = MathF.Max(1f, page.Bounds.Width - section.MarginLeft - section.MarginRight);
            var pageNumber = page.Index + 1;
            var layout = LayoutHeaderFooterBlocks(
                blocks,
                document,
                settings,
                measurer,
                style,
                styleResolver,
                contentWidth,
                lineHeight,
                ascent,
                pageNumber,
                totalPages);

            if (layout.Lines.Count == 0)
            {
                continue;
            }

            var footerTop = page.Bounds.Bottom - section.MarginBottom;
            if (headerFooterMap.TryGetValue(pageIndex, out var headerFooter)
                && headerFooter.FooterLines.Count > 0)
            {
                footerTop = headerFooter.FooterLines.Min(line => line.Y);
            }

            var footnoteTop = footerTop - layout.Height;
            var offsetLines = OffsetHeaderFooterLines(layout.Lines, page.Bounds.X + section.MarginLeft, footnoteTop);
            var separatorWidth = MathF.Min(120f, contentWidth * 0.25f);
            var separatorX = page.Bounds.X + section.MarginLeft;
            var separatorY = footnoteTop - MathF.Max(4f, settings.ParagraphSpacing * 0.5f);
            var separatorBounds = separatorWidth > 0f
                ? new DocRect(separatorX, separatorY, separatorWidth, 1f)
                : new DocRect(separatorX, separatorY, 0f, 0f);

            footnoteLayouts.Add(new FootnoteLayout(page.Index, offsetLines, separatorBounds));
        }

        return footnoteLayouts;
    }

    private static float MeasureInlineSpans(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var items = CollectLineItems(spans, start, length, measurer);
        return MeasureLineItems(items, tabStops, defaultTabWidth, measurer);
    }

    private static LineLayout BuildLineLayout(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer,
        float defaultLineHeight,
        float defaultAscent)
    {
        var runs = new List<LayoutRun>();
        var images = new List<LayoutImage>();
        var shapes = new List<LayoutShape>();
        var charts = new List<LayoutChart>();
        var equations = new List<LayoutEquation>();
        var items = CollectLineItems(spans, start, length, measurer);
        var x = 0f;
        var maxAscent = defaultAscent;
        var maxDescent = MathF.Max(0f, defaultLineHeight - defaultAscent);
        var maxImageHeight = 0f;
        var metricsCache = new Dictionary<TextStyleKey, TextMetrics>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Kind)
            {
                case LineItemKind.Text:
                {
                    var metrics = GetMetrics(item.Style, measurer, metricsCache);
                    var ascent = MathF.Max(0f, metrics.Ascent + item.BaselineOffset);
                    var descent = MathF.Max(0f, metrics.Descent - item.BaselineOffset);
                    maxAscent = MathF.Max(maxAscent, ascent);
                    maxDescent = MathF.Max(maxDescent, descent);

                    runs.Add(new LayoutRun(item.Text, item.Style, x, item.Width, item.Text.Length, false, item.BaselineOffset));
                    x += item.Width;
                    break;
                }
                case LineItemKind.Image:
                {
                    var width = item.Width;
                    var height = item.Height;
                    images.Add(new LayoutImage(item.Image!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    break;
                }
                case LineItemKind.Shape:
                {
                    var width = item.Width;
                    var height = item.Height;
                    shapes.Add(new LayoutShape(item.Shape!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    break;
                }
                case LineItemKind.Chart:
                {
                    var width = item.Width;
                    var height = item.Height;
                    charts.Add(new LayoutChart(item.Chart!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    break;
                }
                case LineItemKind.Equation:
                {
                    var layout = item.EquationLayout!;
                    var width = layout.Width;
                    var height = layout.Height;
                    equations.Add(new LayoutEquation(item.Equation!, layout, x, width, height, 1));
                    x += width;
                    maxAscent = MathF.Max(maxAscent, layout.Baseline);
                    maxDescent = MathF.Max(maxDescent, MathF.Max(0f, height - layout.Baseline));
                    break;
                }
                case LineItemKind.Tab:
                {
                    var tabStop = ResolveNextTabStop(x, tabStops, defaultTabWidth);
                    var nextTabIndex = FindNextTabIndex(items, i + 1);
                    var (fieldWidth, widthBeforeDecimal) = MeasureTabField(items, i + 1, nextTabIndex, measurer);
                    var tabWidth = ComputeTabWidth(tabStop, x, fieldWidth, widthBeforeDecimal);
                    runs.Add(new LayoutRun(string.Empty, item.Style, x, tabWidth, 1, true, item.BaselineOffset, tabStop.Leader));
                    x += tabWidth;
                    break;
                }
            }
        }

        var lineHeight = MathF.Max(defaultLineHeight, maxAscent + maxDescent);
        lineHeight = MathF.Max(lineHeight, maxImageHeight);
        var lineAscent = MathF.Max(defaultAscent, maxAscent);
        lineAscent = MathF.Max(lineAscent, maxImageHeight);

        return new LineLayout(runs, images, shapes, charts, equations, x, lineHeight, lineAscent);
    }

    private static LineLayout ApplyLineSpacing(LineLayout layout, ParagraphProperties properties)
    {
        var targetHeight = ComputeLineHeight(layout.LineHeight, properties);
        if (MathF.Abs(targetHeight - layout.LineHeight) < 0.01f)
        {
            return layout;
        }

        var scale = layout.LineHeight > 0f ? targetHeight / layout.LineHeight : 1f;
        var ascent = layout.Ascent * scale;
        return layout with { LineHeight = targetHeight, Ascent = ascent };
    }

    private static LineLayout AppendHyphenRun(LineLayout layout, TextStyle style, float baselineOffset, ITextMeasurer measurer)
    {
        var width = measurer is ITextMeasurerSpan spanMeasurer
            ? spanMeasurer.MeasureText("-".AsSpan(), style).Width
            : measurer.MeasureText("-", style).Width;
        if (width <= 0f)
        {
            return layout;
        }

        var runs = new List<LayoutRun>(layout.Runs.Count + 1);
        runs.AddRange(layout.Runs);
        runs.Add(new LayoutRun("-", style, layout.Width, width, 0, false, baselineOffset));
        return layout with { Runs = runs, Width = layout.Width + width };
    }

    private static (float LineHeight, float Ascent) ApplyLineSpacing(float lineHeight, float ascent, ParagraphProperties properties)
    {
        var targetHeight = ComputeLineHeight(lineHeight, properties);
        if (MathF.Abs(targetHeight - lineHeight) < 0.01f)
        {
            return (lineHeight, ascent);
        }

        var scale = lineHeight > 0f ? targetHeight / lineHeight : 1f;
        return (targetHeight, ascent * scale);
    }

    private static float ComputeLineHeight(float baseHeight, ParagraphProperties properties)
    {
        if (!properties.LineSpacing.HasValue)
        {
            return baseHeight;
        }

        var lineValue = properties.LineSpacing.Value;
        if (lineValue <= 0)
        {
            return baseHeight;
        }

        var rule = properties.LineSpacingRule ?? DocLineSpacingRule.Auto;
        if (rule == DocLineSpacingRule.Auto)
        {
            var multiple = lineValue / 240f;
            return MathF.Max(1f, baseHeight * multiple);
        }

        var lineDip = TwipsToDip(lineValue);
        return rule == DocLineSpacingRule.AtLeast
            ? MathF.Max(baseHeight, lineDip)
            : MathF.Max(1f, lineDip);
    }

    private static float TwipsToDip(int twips)
    {
        return twips / 20f * 96f / 72f;
    }

    private enum LineItemKind
    {
        Text,
        Tab,
        Image,
        Shape,
        Chart,
        Equation
    }

    private readonly struct LineItem
    {
        public LineItemKind Kind { get; }
        public string Text { get; }
        public TextStyle Style { get; }
        public float BaselineOffset { get; }
        public ImageInline? Image { get; }
        public ShapeInline? Shape { get; }
        public ChartInline? Chart { get; }
        public EquationInline? Equation { get; }
        public MathLayout? EquationLayout { get; }
        public float Width { get; }
        public float Height { get; }

        private LineItem(
            LineItemKind kind,
            string text,
            TextStyle style,
            float baselineOffset,
            ImageInline? image,
            ShapeInline? shape,
            ChartInline? chart,
            EquationInline? equation,
            MathLayout? equationLayout,
            float width,
            float height)
        {
            Kind = kind;
            Text = text;
            Style = style;
            BaselineOffset = baselineOffset;
            Image = image;
            Shape = shape;
            Chart = chart;
            Equation = equation;
            EquationLayout = equationLayout;
            Width = width;
            Height = height;
        }

        public static LineItem TextSegment(string text, TextStyle style, float baselineOffset, float width)
        {
            return new LineItem(LineItemKind.Text, text, style, baselineOffset, null, null, null, null, null, width, 0f);
        }

        public static LineItem Tab(TextStyle style, float baselineOffset)
        {
            return new LineItem(LineItemKind.Tab, string.Empty, style, baselineOffset, null, null, null, null, null, 0f, 0f);
        }

        public static LineItem ImageSegment(ImageInline image, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Image, string.Empty, style, 0f, image, null, null, null, null, width, height);
        }

        public static LineItem ShapeSegment(ShapeInline shape, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Shape, string.Empty, style, 0f, null, shape, null, null, null, width, height);
        }

        public static LineItem ChartSegment(ChartInline chart, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Chart, string.Empty, style, 0f, null, null, chart, null, null, width, height);
        }

        public static LineItem EquationSegment(EquationInline equation, MathLayout layout, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Equation, string.Empty, style, 0f, null, null, null, equation, layout, width, height);
        }
    }

    private readonly record struct TabStopInfo(float Position, TabAlignment Alignment, TabLeader Leader);

    private static List<LineItem> CollectLineItems(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        ITextMeasurer measurer)
    {
        var items = new List<LineItem>();
        if (length <= 0)
        {
            return items;
        }

        var mathLayoutEngine = SharedMathLayoutEngine;
        var end = start + length;
        foreach (var span in spans)
        {
            var spanStart = span.Start;
            var spanEnd = span.Start + span.Length;
            if (spanEnd <= start || spanStart >= end)
            {
                continue;
            }

            var segmentStart = Math.Max(spanStart, start);
            var segmentEnd = Math.Min(spanEnd, end);
            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength <= 0)
            {
                continue;
            }

            if (span.Image is not null)
            {
                var width = span.Image.Width;
                var height = span.Image.Height;
                items.Add(LineItem.ImageSegment(span.Image, span.Style, width, height));
                continue;
            }

            if (span.Shape is not null)
            {
                var width = span.Shape.Width;
                var height = span.Shape.Height;
                items.Add(LineItem.ShapeSegment(span.Shape, span.Style, width, height));
                continue;
            }

            if (span.Chart is not null)
            {
                var width = span.Chart.Width;
                var height = span.Chart.Height;
                items.Add(LineItem.ChartSegment(span.Chart, span.Style, width, height));
                continue;
            }

            if (span.Equation is not null)
            {
                var baseStyle = span.Style;
                var layout = mathLayoutEngine.Layout(span.Equation.Root, baseStyle, measurer);
                items.Add(LineItem.EquationSegment(span.Equation, layout, baseStyle, layout.Width, layout.Height));
                continue;
            }

            var segmentText = span.Text.AsSpan(segmentStart - spanStart, segmentLength);
            AppendTextItems(items, segmentText, span.Style, span.BaselineOffset, measurer);
        }

        return items;
    }

    private static void AppendTextItems(
        List<LineItem> items,
        ReadOnlySpan<char> text,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var segmentStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\t')
            {
                continue;
            }

            if (i > segmentStart)
            {
                var segmentText = new string(text.Slice(segmentStart, i - segmentStart));
                var width = measurer.MeasureText(segmentText, style).Width;
                items.Add(LineItem.TextSegment(segmentText, style, baselineOffset, width));
            }

            items.Add(LineItem.Tab(style, baselineOffset));
            segmentStart = i + 1;
        }

        if (segmentStart < text.Length)
        {
            var segmentText = new string(text.Slice(segmentStart));
            var width = measurer.MeasureText(segmentText, style).Width;
            items.Add(LineItem.TextSegment(segmentText, style, baselineOffset, width));
        }
    }

    private static float MeasureLineItems(
        IReadOnlyList<LineItem> items,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer)
    {
        var x = 0f;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Kind)
            {
                case LineItemKind.Text:
                case LineItemKind.Image:
                case LineItemKind.Shape:
                case LineItemKind.Chart:
                case LineItemKind.Equation:
                    x += item.Width;
                    break;
                case LineItemKind.Tab:
                {
                    var tabStop = ResolveNextTabStop(x, tabStops, defaultTabWidth);
                    var nextTabIndex = FindNextTabIndex(items, i + 1);
                    var (fieldWidth, widthBeforeDecimal) = MeasureTabField(items, i + 1, nextTabIndex, measurer);
                    var tabWidth = ComputeTabWidth(tabStop, x, fieldWidth, widthBeforeDecimal);
                    x += tabWidth;
                    break;
                }
            }
        }

        return x;
    }

    private static int FindNextTabIndex(IReadOnlyList<LineItem> items, int startIndex)
    {
        for (var i = startIndex; i < items.Count; i++)
        {
            if (items[i].Kind == LineItemKind.Tab)
            {
                return i;
            }
        }

        return items.Count;
    }

    private static (float FieldWidth, float WidthBeforeDecimal) MeasureTabField(
        IReadOnlyList<LineItem> items,
        int startIndex,
        int endIndex,
        ITextMeasurer measurer)
    {
        var fieldWidth = 0f;
        var beforeDecimalWidth = 0f;
        var decimalFound = false;

        for (var i = startIndex; i < endIndex; i++)
        {
            var item = items[i];
            if (item.Kind == LineItemKind.Text)
            {
                if (!decimalFound)
                {
                    var decimalIndex = FindDecimalIndex(item.Text);
                    if (decimalIndex >= 0)
                    {
                        if (decimalIndex > 0)
                        {
                            var beforeSpan = item.Text.AsSpan(0, decimalIndex);
                            var beforeWidth = measurer is ITextMeasurerSpan spanMeasurer
                                ? spanMeasurer.MeasureText(beforeSpan, item.Style).Width
                                : measurer.MeasureText(new string(beforeSpan), item.Style).Width;
                            beforeDecimalWidth += beforeWidth;
                        }

                        decimalFound = true;
                    }
                    else
                    {
                        beforeDecimalWidth += item.Width;
                    }
                }

                fieldWidth += item.Width;
            }
            else if (item.Kind == LineItemKind.Image || item.Kind == LineItemKind.Shape || item.Kind == LineItemKind.Equation)
            {
                fieldWidth += item.Width;
                if (!decimalFound)
                {
                    beforeDecimalWidth += item.Width;
                }
            }
        }

        if (!decimalFound)
        {
            beforeDecimalWidth = fieldWidth;
        }

        return (fieldWidth, beforeDecimalWidth);
    }

    private static int FindDecimalIndex(string text)
    {
        var dot = text.IndexOf('.');
        var comma = text.IndexOf(',');
        if (dot < 0)
        {
            return comma;
        }

        if (comma < 0)
        {
            return dot;
        }

        return Math.Min(dot, comma);
    }

    private static TabStopInfo ResolveNextTabStop(
        float currentX,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth)
    {
        for (var i = 0; i < tabStops.Count; i++)
        {
            var stop = tabStops[i];
            if (stop.Position > currentX)
            {
                return new TabStopInfo(stop.Position, stop.Alignment, stop.Leader);
            }
        }

        var safeWidth = MathF.Max(1f, defaultTabWidth);
        var next = MathF.Floor(currentX / safeWidth) * safeWidth + safeWidth;
        return new TabStopInfo(next, TabAlignment.Left, TabLeader.None);
    }

    private static float ComputeTabWidth(TabStopInfo stop, float currentX, float fieldWidth, float widthBeforeDecimal)
    {
        var target = stop.Position;
        var width = stop.Alignment switch
        {
            TabAlignment.Center => target - currentX - fieldWidth / 2f,
            TabAlignment.Right => target - currentX - fieldWidth,
            TabAlignment.Decimal => target - currentX - widthBeforeDecimal,
            _ => target - currentX
        };

        return MathF.Max(0f, width);
    }

    private static float[] ResolveSectionColumnWidths(PageSectionSettings section, float contentWidth, float columnGap)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var availableWidth = MathF.Max(1f, contentWidth - columnGap * MathF.Max(0, columnCount - 1));

        if (section.ColumnEqualWidth || section.ColumnWidths.Count == 0)
        {
            var width = availableWidth / columnCount;
            var widths = new float[columnCount];
            Array.Fill(widths, width);
            return widths;
        }

        var resolved = new float[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            resolved[i] = i < section.ColumnWidths.Count ? section.ColumnWidths[i] : section.ColumnWidths.Last();
        }

        var total = resolved.Sum();
        if (total <= 0f)
        {
            var width = availableWidth / columnCount;
            Array.Fill(resolved, width);
            return resolved;
        }

        if (total > availableWidth && availableWidth > 0f)
        {
            var scale = availableWidth / total;
            for (var i = 0; i < resolved.Length; i++)
            {
                resolved[i] *= scale;
            }
        }

        return resolved;
    }

    private static float[] BuildColumnOffsets(float[] widths, float gap)
    {
        var offsets = new float[widths.Length];
        var current = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            offsets[i] = current;
            current += widths[i] + gap;
        }

        return offsets;
    }

    private static Dictionary<int, PageSectionSettings> BuildSectionSettingsByIndex(Document document, LayoutSettings settings)
    {
        var result = new Dictionary<int, PageSectionSettings>();
        var count = Math.Max(1, document.SectionCount);
        for (var i = 0; i < count; i++)
        {
            var section = document.GetSection(i);
            result[i] = PageSectionSettings.FromSettings(settings, section.Properties, i, document.MirrorMargins, document.GutterAtTop);
        }

        return result;
    }

    private static Dictionary<int, int> BuildParagraphSectionIndices(Document document)
    {
        var map = new Dictionary<int, int>();
        var currentSectionIndex = 0;
        var paragraphIndex = 0;

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case SectionBreakBlock sectionBreak:
                    currentSectionIndex = sectionBreak.SectionIndex ?? currentSectionIndex;
                    break;
                case ParagraphBlock:
                    map[paragraphIndex++] = currentSectionIndex;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var _ in cell.Paragraphs)
                            {
                                map[paragraphIndex++] = currentSectionIndex;
                            }
                        }
                    }

                    break;
            }
        }

        return map;
    }

    private static float[] ResolveColumnWidths(TableProperties properties, int columnCount, float contentWidth)
    {
        var widths = new float[columnCount];
        if (properties.ColumnWidths.Count == 0)
        {
            var defaultWidth = contentWidth / Math.Max(1, columnCount);
            Array.Fill(widths, defaultWidth);
            return widths;
        }

        for (var i = 0; i < columnCount; i++)
        {
            widths[i] = i < properties.ColumnWidths.Count ? properties.ColumnWidths[i] : properties.ColumnWidths.Last();
        }

        var total = widths.Sum();
        if (total <= 0f)
        {
            var defaultWidth = contentWidth / Math.Max(1, columnCount);
            Array.Fill(widths, defaultWidth);
            return widths;
        }

        if (total > contentWidth && contentWidth > 0f)
        {
            var scale = contentWidth / total;
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] *= scale;
            }
        }

        return widths;
    }

    private static int ResolveTableColumnCount(IReadOnlyList<TableRow> rows, TableProperties properties)
    {
        var columnCount = properties.ColumnWidths.Count;
        if (rows.Count == 0)
        {
            return Math.Max(1, columnCount);
        }

        var maxColumns = 0;
        foreach (var row in rows)
        {
            var rowColumns = 0;
            foreach (var cell in row.Cells)
            {
                rowColumns += Math.Max(1, cell.ColumnSpan);
            }

            maxColumns = Math.Max(maxColumns, rowColumns);
        }

        return Math.Max(1, Math.Max(columnCount, maxColumns));
    }

    private static float SumColumns(IReadOnlyList<float> widths, int start, int span)
    {
        if (widths.Count == 0 || span <= 0)
        {
            return 0f;
        }

        var end = Math.Min(widths.Count, start + span);
        var total = 0f;
        for (var i = Math.Max(0, start); i < end; i++)
        {
            total += widths[i];
        }

        return total;
    }

    private static float SumRows(IReadOnlyList<float> heights, int start, int span)
    {
        if (heights.Count == 0 || span <= 0)
        {
            return 0f;
        }

        var end = Math.Min(heights.Count, start + span);
        var total = 0f;
        for (var i = Math.Max(0, start); i < end; i++)
        {
            total += heights[i];
        }

        return total;
    }

    private static TableProperties MergeTableProperties(TableProperties? baseProperties, TableProperties? overrideProperties)
    {
        var merged = baseProperties?.Clone() ?? new TableProperties();
        if (overrideProperties is not null)
        {
            ApplyTableProperties(merged, overrideProperties);
        }

        return merged;
    }

    private static TableLook ResolveTableLook(TableLook? look)
    {
        return look?.Clone() ?? new TableLook();
    }

    private static TableCellProperties ResolveTableCellProperties(
        TableCell cell,
        TableProperties tableProperties,
        TableStyleDefinition? tableStyle,
        TableLook tableLook,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var resolved = new TableCellProperties();

        if (tableStyle is not null)
        {
            ApplyTablePropertiesToCell(resolved, tableStyle.TableProperties, rowIndex, columnIndex, rowCount, columnCount);
            ApplyTableCellProperties(resolved, tableStyle.CellProperties);

            foreach (var condition in GetApplicableTableStyleConditions(tableLook, rowIndex, columnIndex, rowCount, columnCount))
            {
                if (!tableStyle.Conditions.TryGetValue(condition, out var conditionProperties))
                {
                    continue;
                }

                ApplyTablePropertiesToCell(resolved, conditionProperties.TableProperties, rowIndex, columnIndex, rowCount, columnCount);
                ApplyTableCellProperties(resolved, conditionProperties.CellProperties);
            }
        }

        ApplyTablePropertiesToCell(resolved, tableProperties, rowIndex, columnIndex, rowCount, columnCount);
        ApplyTableCellProperties(resolved, cell.Properties);

        return resolved;
    }

    private static IEnumerable<TableStyleCondition> GetApplicableTableStyleConditions(
        TableLook look,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        if (look.BandedRows)
        {
            yield return rowIndex % 2 == 0 ? TableStyleCondition.Band1Horizontal : TableStyleCondition.Band2Horizontal;
        }

        if (look.BandedColumns)
        {
            yield return columnIndex % 2 == 0 ? TableStyleCondition.Band1Vertical : TableStyleCondition.Band2Vertical;
        }

        if (look.FirstRow && rowIndex == 0)
        {
            yield return TableStyleCondition.FirstRow;
        }

        if (look.LastRow && rowIndex == rowCount - 1)
        {
            yield return TableStyleCondition.LastRow;
        }

        if (look.FirstColumn && columnIndex == 0)
        {
            yield return TableStyleCondition.FirstColumn;
        }

        if (look.LastColumn && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.LastColumn;
        }
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties source)
    {
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        if (source.CellPadding.HasValue)
        {
            target.CellPadding = MergePadding(target.CellPadding, source.CellPadding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        if (source.Look is not null)
        {
            target.Look = source.Look.Clone();
        }

        ApplyTableBorders(target.Borders, source.Borders);
    }

    private static DocThickness ResolvePadding(DocThickness? padding, DocThickness fallback)
    {
        if (!padding.HasValue)
        {
            return fallback;
        }

        var value = padding.Value;
        return new DocThickness(
            ResolvePaddingSide(value.Left, fallback.Left),
            ResolvePaddingSide(value.Top, fallback.Top),
            ResolvePaddingSide(value.Right, fallback.Right),
            ResolvePaddingSide(value.Bottom, fallback.Bottom));
    }

    private static DocThickness ResolveTablePadding(DocThickness? padding, DocThickness fallback)
    {
        if (!padding.HasValue)
        {
            return fallback;
        }

        var value = padding.Value;
        return new DocThickness(
            float.IsNaN(value.Left) ? 0f : value.Left,
            float.IsNaN(value.Top) ? 0f : value.Top,
            float.IsNaN(value.Right) ? 0f : value.Right,
            float.IsNaN(value.Bottom) ? 0f : value.Bottom);
    }

    private static float ResolvePaddingSide(float value, float fallback)
    {
        return float.IsNaN(value) ? fallback : value;
    }

    private static DocThickness MergePadding(DocThickness? basePadding, DocThickness overridePadding)
    {
        if (!basePadding.HasValue)
        {
            return overridePadding;
        }

        var value = basePadding.Value;
        return new DocThickness(
            float.IsNaN(overridePadding.Left) ? value.Left : overridePadding.Left,
            float.IsNaN(overridePadding.Top) ? value.Top : overridePadding.Top,
            float.IsNaN(overridePadding.Right) ? value.Right : overridePadding.Right,
            float.IsNaN(overridePadding.Bottom) ? value.Bottom : overridePadding.Bottom);
    }

    private static void ApplyTablePropertiesToCell(
        TableCellProperties target,
        TableProperties source,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        if (source.CellPadding.HasValue)
        {
            target.Padding = MergePadding(target.Padding, source.CellPadding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        ApplyTableBordersToCell(target.Borders, source.Borders, rowIndex, columnIndex, rowCount, columnCount);
    }

    private static void ApplyTableBorders(TableBorders target, TableBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }

        if (source.InsideHorizontal is not null)
        {
            target.InsideHorizontal = source.InsideHorizontal.Clone();
        }

        if (source.InsideVertical is not null)
        {
            target.InsideVertical = source.InsideVertical.Clone();
        }
    }

    private static void ApplyTableBordersToCell(
        TableCellBorders target,
        TableBorders source,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var isFirstRow = rowIndex == 0;
        var isLastRow = rowIndex == rowCount - 1;
        var isFirstColumn = columnIndex == 0;
        var isLastColumn = columnIndex == columnCount - 1;

        if (isFirstRow && source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (isLastRow && source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (isFirstColumn && source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (isLastColumn && source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }

        if (!isLastRow && source.InsideHorizontal is not null)
        {
            target.Bottom = source.InsideHorizontal.Clone();
        }

        if (!isLastColumn && source.InsideVertical is not null)
        {
            target.Right = source.InsideVertical.Clone();
        }
    }

    private static void ApplyTableCellProperties(TableCellProperties target, TableCellProperties source)
    {
        if (source.Padding.HasValue)
        {
            target.Padding = MergePadding(target.Padding, source.Padding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        if (source.VerticalAlignment.HasValue)
        {
            target.VerticalAlignment = source.VerticalAlignment;
        }

        ApplyTableCellBorders(target.Borders, source.Borders);
    }

    private static void ApplyTableCellBorders(TableCellBorders target, TableCellBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }
    }

    private static float ApplyAlignment(float baseX, float lineWidth, float availableWidth, ParagraphAlignment? alignment)
    {
        if (availableWidth <= 0f)
        {
            return baseX;
        }

        var remaining = availableWidth - lineWidth;
        if (remaining <= 0f)
        {
            return baseX;
        }

        return (alignment ?? ParagraphAlignment.Left) switch
        {
            ParagraphAlignment.Center => baseX + remaining / 2f,
            ParagraphAlignment.Right => baseX + remaining,
            ParagraphAlignment.Justify => baseX,
            _ => baseX
        };
    }

    private static bool IsLastParagraphLine(string text, int lineEnd)
    {
        if (lineEnd >= text.Length)
        {
            return true;
        }

        for (var i = lineEnd; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != ' ' && ch != '\t')
            {
                return false;
            }
        }

        return true;
    }

    private static LineLayout JustifyLineLayout(LineLayout layout, float targetWidth, ITextMeasurer measurer)
    {
        return LineJustifier.Justify(layout, targetWidth, measurer);
    }

    private sealed record ListMarkerInfo(string Prefix, float Indent, float PrefixWidth);

    private sealed class ListNumberingState
    {
        private readonly Document _document;
        private readonly Dictionary<(int ListId, int Level), int> _counters = new();
        private int? _activeListId;
        private ListKind _activeKind = ListKind.None;

        public ListNumberingState(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public ListMarkerInfo? GetMarker(
            ListInfo? listInfo,
            LayoutSettings settings,
            ITextMeasurer measurer,
            TextStyle style)
        {
            if (listInfo is null || listInfo.Kind == ListKind.None)
            {
                Reset();
                return null;
            }

            var listId = listInfo.ListId ?? -1;
            if (_activeListId != listId || _activeKind != listInfo.Kind)
            {
                Reset();
                _activeListId = listId;
                _activeKind = listInfo.Kind;
            }

            var levelDefinition = ResolveLevelDefinition(listId, listInfo.Level);
            var prefix = BuildPrefix(listId, listInfo, levelDefinition);
            if (string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            var indent = ResolveListIndent(listInfo, levelDefinition, settings);
            var prefixWidth = ResolvePrefixWidth(prefix, indent, listInfo, levelDefinition, settings, measurer, style);
            return new ListMarkerInfo(prefix, indent, prefixWidth);
        }

        private ListLevelDefinition? ResolveLevelDefinition(int listId, int level)
        {
            if (listId >= 0
                && _document.ListDefinitions.TryGetValue(listId, out var listDefinition)
                && listDefinition.Levels.TryGetValue(level, out var levelDefinition))
            {
                return levelDefinition;
            }

            return null;
        }

        private string? BuildPrefix(int listId, ListInfo listInfo, ListLevelDefinition? levelDefinition)
        {
            var kind = ResolveKind(listInfo, levelDefinition);
            if (kind == ListKind.Bullet)
            {
                return ResolveBulletSymbol(listInfo, levelDefinition);
            }

            var levelText = listInfo.LevelText ?? levelDefinition?.LevelText;
            if (string.IsNullOrWhiteSpace(levelText))
            {
                levelText = "%1.";
            }

            NextNumber(listId, listInfo, levelDefinition);
            return FormatLevelText(levelText, listId, listInfo);
        }

        private static ListKind ResolveKind(ListInfo listInfo, ListLevelDefinition? levelDefinition)
        {
            if (listInfo.NumberFormat == ListNumberFormat.Bullet || levelDefinition?.Format == ListNumberFormat.Bullet)
            {
                return ListKind.Bullet;
            }

            return listInfo.Kind;
        }

        private static string ResolveBulletSymbol(ListInfo listInfo, ListLevelDefinition? levelDefinition)
        {
            return listInfo.BulletSymbol
                   ?? levelDefinition?.BulletSymbol
                   ?? levelDefinition?.LevelText
                   ?? "•";
        }

        private void NextNumber(int listId, ListInfo listInfo, ListLevelDefinition? levelDefinition)
        {
            var level = listInfo.Level;
            var startAt = listInfo.StartAt ?? levelDefinition?.StartAt ?? 1;
            if (!_counters.TryGetValue((listId, level), out var current))
            {
                current = startAt - 1;
            }

            current++;
            _counters[(listId, level)] = current;

            var keysToRemove = _counters.Keys
                .Where(key => key.ListId == listId && key.Level > level)
                .ToList();
            foreach (var key in keysToRemove)
            {
                _counters.Remove(key);
            }
        }

        private string FormatLevelText(string levelText, int listId, ListInfo listInfo)
        {
            if (levelText.IndexOf('%') < 0)
            {
                return levelText;
            }

            var builder = new System.Text.StringBuilder(levelText.Length);
            for (var i = 0; i < levelText.Length; i++)
            {
                var ch = levelText[i];
                if (ch != '%' || i == levelText.Length - 1)
                {
                    builder.Append(ch);
                    continue;
                }

                var digitStart = i + 1;
                var digitEnd = digitStart;
                while (digitEnd < levelText.Length && char.IsDigit(levelText[digitEnd]))
                {
                    digitEnd++;
                }

                if (digitEnd == digitStart)
                {
                    builder.Append(ch);
                    continue;
                }

                var tokenSpan = levelText.AsSpan(digitStart, digitEnd - digitStart);
                if (int.TryParse(tokenSpan, out var tokenIndex))
                {
                    var levelIndex = Math.Max(0, tokenIndex - 1);
                    var value = GetCounterValue(listId, levelIndex);
                    var format = ResolveNumberFormat(listId, levelIndex, listInfo);
                    builder.Append(FormatNumber(value, format));
                    i = digitEnd - 1;
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private int GetCounterValue(int listId, int level)
        {
            if (_counters.TryGetValue((listId, level), out var current))
            {
                return current;
            }

            var startAt = ResolveStartAt(listId, level);
            _counters[(listId, level)] = startAt;
            return startAt;
        }

        private int ResolveStartAt(int listId, int level)
        {
            if (listId >= 0
                && _document.ListDefinitions.TryGetValue(listId, out var definition)
                && definition.Levels.TryGetValue(level, out var levelDefinition))
            {
                return Math.Max(1, levelDefinition.StartAt);
            }

            return 1;
        }

        private ListNumberFormat ResolveNumberFormat(int listId, int level, ListInfo listInfo)
        {
            if (listId >= 0
                && _document.ListDefinitions.TryGetValue(listId, out var definition)
                && definition.Levels.TryGetValue(level, out var levelDefinition))
            {
                return levelDefinition.Format;
            }

            if (listInfo.NumberFormat.HasValue)
            {
                return listInfo.NumberFormat.Value;
            }

            return ListNumberFormat.Decimal;
        }

        private static float ResolveListIndent(ListInfo listInfo, ListLevelDefinition? levelDefinition, LayoutSettings settings)
        {
            var leftIndent = listInfo.LeftIndent ?? levelDefinition?.LeftIndent;
            var hangingIndent = listInfo.HangingIndent ?? levelDefinition?.HangingIndent;
            if (leftIndent.HasValue && hangingIndent.HasValue)
            {
                return MathF.Max(0f, leftIndent.Value - hangingIndent.Value);
            }

            return settings.ListIndent * (listInfo.Level + 1);
        }

        private static float ResolvePrefixWidth(
            string prefix,
            float listIndent,
            ListInfo listInfo,
            ListLevelDefinition? levelDefinition,
            LayoutSettings settings,
            ITextMeasurer measurer,
            TextStyle style)
        {
            var width = measurer.MeasureText(prefix, style).Width + settings.ListMarkerGap;
            var hangingIndent = listInfo.HangingIndent ?? levelDefinition?.HangingIndent;
            if (hangingIndent.HasValue)
            {
                width = MathF.Max(width, hangingIndent.Value);
            }

            var tabStop = listInfo.TabStop ?? levelDefinition?.TabStop;
            if (tabStop.HasValue)
            {
                width = MathF.Max(width, MathF.Max(0f, tabStop.Value - listIndent));
            }

            return width;
        }

        private static string FormatNumber(int value, ListNumberFormat format)
        {
            return format switch
            {
                ListNumberFormat.LowerLetter => ToAlphabetic(value, true),
                ListNumberFormat.UpperLetter => ToAlphabetic(value, false),
                ListNumberFormat.LowerRoman => ToRoman(value).ToLowerInvariant(),
                ListNumberFormat.UpperRoman => ToRoman(value),
                _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private static string ToAlphabetic(int value, bool lower)
        {
            if (value <= 0)
            {
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var result = new System.Text.StringBuilder();
            var number = value;
            while (number > 0)
            {
                number--;
                var remainder = number % 26;
                var ch = (char)((lower ? 'a' : 'A') + remainder);
                result.Insert(0, ch);
                number /= 26;
            }

            return result.ToString();
        }

        private static string ToRoman(int value)
        {
            if (value <= 0)
            {
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            var builder = new System.Text.StringBuilder();
            foreach (var (mapValue, mapSymbol) in map)
            {
                while (remaining >= mapValue)
                {
                    builder.Append(mapSymbol);
                    remaining -= mapValue;
                }
            }

            return builder.ToString();
        }

        private void Reset()
        {
            _activeKind = ListKind.None;
            _activeListId = null;
            _counters.Clear();
        }
    }

    private sealed record ParagraphLine(int Start, int Length, TextSlice TextSlice, LineLayout Layout, bool IsFirstLine);
    private readonly record struct WrapBounds(float Left, float Right, float BlockBottom);
    private delegate WrapBounds WrapResolver(float LineTop, float LineHeight, float BaseLeft, float BaseRight);

    private readonly struct ParagraphLayoutSnapshot
    {
        public int LinesCount { get; }
        public int LinePageIndicesCount { get; }
        public int PagesCount { get; }
        public int PageSectionsCount { get; }
        public float CursorY { get; }
        public int PageIndex { get; }
        public float PageY { get; }
        public int ColumnIndex { get; }
        public float ColumnX { get; }
        public float ColumnWidth { get; }
        public float ColumnTop { get; }
        public float ContentTop { get; }
        public float ContentBottom { get; }

        public ParagraphLayoutSnapshot(
            int linesCount,
            int linePageIndicesCount,
            int pagesCount,
            int pageSectionsCount,
            float cursorY,
            int pageIndex,
            float pageY,
            int columnIndex,
            float columnX,
            float columnWidth,
            float columnTop,
            float contentTop,
            float contentBottom)
        {
            LinesCount = linesCount;
            LinePageIndicesCount = linePageIndicesCount;
            PagesCount = pagesCount;
            PageSectionsCount = pageSectionsCount;
            CursorY = cursorY;
            PageIndex = pageIndex;
            PageY = pageY;
            ColumnIndex = columnIndex;
            ColumnX = columnX;
            ColumnWidth = columnWidth;
            ColumnTop = columnTop;
            ContentTop = contentTop;
            ContentBottom = contentBottom;
        }
    }

    private sealed class ContentControlState
    {
        public ContentControlProperties Properties { get; }
        public string PlaceholderText { get; }
        public TextStyle PlaceholderStyle { get; }
        public bool ShouldShowPlaceholder { get; }
        public bool HasContent { get; set; }

        public ContentControlState(ContentControlProperties properties, TextStyle paragraphStyle, string placeholderText)
        {
            Properties = properties;
            PlaceholderText = placeholderText;
            PlaceholderStyle = CreatePlaceholderStyle(paragraphStyle);
            ShouldShowPlaceholder = properties.ShowingPlaceholder != false;
        }
    }

    private sealed class ParagraphScanState
    {
        private readonly Dictionary<int, TextPosition> _openCommentStarts = new();
        private readonly List<CommentRange> _commentRanges = new();
        private readonly Dictionary<int, int> _paragraphLengths = new();

        public Dictionary<int, List<NoteReference>> NoteReferencesByParagraph { get; } = new();

        public void Scan(Document document)
        {
            var paragraphIndex = 0;
            foreach (var block in document.Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        RegisterParagraph(paragraphIndex++, paragraph);
                        break;
                    case TableBlock table:
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (var paragraph in cell.Paragraphs)
                                {
                                    RegisterParagraph(paragraphIndex++, paragraph);
                                }
                            }
                        }

                        break;
                }
            }
        }

        public IReadOnlyDictionary<int, IReadOnlyList<CommentHighlightSpan>> BuildCommentHighlights()
        {
            if (_commentRanges.Count == 0)
            {
                return new Dictionary<int, IReadOnlyList<CommentHighlightSpan>>();
            }

            var result = new Dictionary<int, List<CommentHighlightSpan>>();
            foreach (var range in _commentRanges)
            {
                var normalized = range.Range.Normalize();
                var start = normalized.Start;
                var end = normalized.End;
                if (start.ParagraphIndex > end.ParagraphIndex)
                {
                    continue;
                }

                for (var paragraphIndex = start.ParagraphIndex; paragraphIndex <= end.ParagraphIndex; paragraphIndex++)
                {
                    var paragraphLength = _paragraphLengths.TryGetValue(paragraphIndex, out var length) ? length : 0;
                    var startOffset = paragraphIndex == start.ParagraphIndex ? start.Offset : 0;
                    var endOffset = paragraphIndex == end.ParagraphIndex ? end.Offset : paragraphLength;
                    if (endOffset <= startOffset)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(paragraphIndex, out var spans))
                    {
                        spans = new List<CommentHighlightSpan>();
                        result[paragraphIndex] = spans;
                    }

                    spans.Add(new CommentHighlightSpan(range.Id, startOffset, endOffset));
                }
            }

            return result.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<CommentHighlightSpan>)item.Value);
        }

        private void RegisterParagraph(int paragraphIndex, ParagraphBlock paragraph)
        {
            var scan = ScanParagraphInlines(paragraph);
            if (scan.NoteReferences.Count > 0)
            {
                NoteReferencesByParagraph[paragraphIndex] = scan.NoteReferences;
            }

            _paragraphLengths[paragraphIndex] = scan.TextLength;
            if (scan.CommentMarkers.Count == 0)
            {
                return;
            }

            foreach (var marker in scan.CommentMarkers)
            {
                var position = new TextPosition(paragraphIndex, marker.Offset);
                if (marker.IsStart)
                {
                    _openCommentStarts[marker.Id] = position;
                    continue;
                }

                if (_openCommentStarts.TryGetValue(marker.Id, out var start))
                {
                    _commentRanges.Add(new CommentRange(marker.Id, new TextRange(start, position)));
                    _openCommentStarts.Remove(marker.Id);
                }
            }
        }
    }

    private static TextMetrics GetMetrics(TextStyle style, ITextMeasurer measurer, Dictionary<TextStyleKey, TextMetrics> cache)
    {
        var key = new TextStyleKey(style);
        if (cache.TryGetValue(key, out var metrics))
        {
            return metrics;
        }

        metrics = measurer.MeasureText("Mg", style);
        cache[key] = metrics;
        return metrics;
    }
}
