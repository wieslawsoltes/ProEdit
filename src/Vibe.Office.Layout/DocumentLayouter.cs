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
        TableLayoutData Data);

    private readonly record struct HeaderFooterLayoutResult(
        IReadOnlyList<HeaderFooterLine> Lines,
        IReadOnlyList<TableLayout> Tables,
        float Height,
        IReadOnlyDictionary<int, LineRange> ParagraphLineRanges,
        IReadOnlyList<HeaderFooterFrameLayout> FrameLayouts);

    private readonly record struct HeaderFooterFrameLayout(
        FloatingObject Floating,
        HeaderFooterLine AnchorLine);

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
        var extraFloatingObjects = new List<FloatingLayoutObject>();
        var breakMarkers = new List<BreakMarker>();
        var linePageIndices = new List<int>();
        var paragraphLineRanges = new Dictionary<int, LineRange>();
        var paragraphSpacingBefore = new Dictionary<int, float>();
        var listState = new ListNumberingState(document);
        var styleResolver = new DocumentStyleResolver(document);
        var spacingMetricsCache = new Dictionary<TextStyleKey, TextMetrics>();
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
        var pageOriginX = settings.UsePagination ? settings.PageGap : 0f;
        var pageOriginY = settings.UsePagination ? settings.PageGap : 0f;
        var pageIndex = 0;
        var pageSettings = sectionSettings.ResolveForPage(pageIndex);
        var pageContentWidth = 0f;
        var columnWidth = 0f;
        var columnIndex = 0;
        var columnCount = 1;
        var columnGap = 0f;
        var columnGaps = Array.Empty<float>();
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
            if (!settings.UsePagination)
            {
                pageX = 0f;
                pageY = 0f;
            }
            else if (settings.PageFlow == PageFlowDirection.Horizontal)
            {
                pageX = pageOriginX;
                pageY = pageOriginY;
            }
            else
            {
                pageX = MathF.Max(0f, (settings.ViewportWidth - pageWidth) / 2f);
                pageY = pageOriginY;
            }
            pageContentWidth = MathF.Max(1f, pageWidth - marginLeft - marginRight);
            contentTop = pageY + marginTop;
            contentBottom = pageY + pageHeight - marginBottom;

            columnCount = Math.Max(1, section.ColumnCount);
            columnGap = MathF.Max(0f, section.ColumnGap);
            columnGaps = ResolveSectionColumnGaps(section, columnCount, columnGap);
            columnWidths = ResolveSectionColumnWidths(section, pageContentWidth, columnGaps);
            columnOffsets = BuildColumnOffsets(columnWidths, columnGaps);
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
            if (settings.UsePagination)
            {
                if (settings.PageFlow == PageFlowDirection.Horizontal)
                {
                    pageOriginX += pageWidth + settings.PageGap;
                }
                else
                {
                    pageOriginY += pageHeight + settings.PageGap;
                }
            }
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
                        if (!paragraphLineRanges.ContainsKey(currentParagraph))
                        {
                            paragraphLineRanges[currentParagraph] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                        }
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
                    line.Rubies,
                    line.TextDirection,
                    true,
                    line.IsRtl));
            }

            if (currentParagraph >= 0)
            {
                if (!paragraphLineRanges.ContainsKey(currentParagraph))
                {
                    paragraphLineRanges[currentParagraph] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                }
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
                var paragraphStyle = styleResolver.ResolveParagraphTextStyle(startParagraph, style);
                var lineHeightAdjusted = ResolveParagraphLineHeight(
                    paragraphStyle,
                    measurer,
                    spacingMetricsCache,
                    paragraphProperties,
                    pageSettings.DocGrid);
                var spacingBefore = ResolveParagraphSpacing(
                    paragraphProperties.SpacingBefore,
                    paragraphProperties.SpacingBeforeLines,
                    paragraphProperties.AutoSpacingBefore,
                    settings.ParagraphSpacing,
                    lineHeightAdjusted);
                if (paragraphProperties.ContextualSpacing == true
                    && blockStartIndex > 0
                    && blocks[blockStartIndex - 1] is ParagraphBlock previousBlockParagraph
                    && IsSameParagraphStyle(document, previousBlockParagraph, startParagraph))
                {
                    spacingBefore = 0f;
                }
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

                float boundsLeft;
                float boundsRight;
                float boundsBottom;
                if (!TryResolveWrapContourBounds(floating, lineTop, lineHeight, out boundsLeft, out boundsRight, out boundsBottom))
                {
                    var bounds = InflateBounds(floating.Bounds, anchor.Distance);
                    if (!LineOverlaps(bounds, lineTop, lineHeight))
                    {
                        continue;
                    }

                    if (bounds.Right <= baseLeft || bounds.Left >= baseRight)
                    {
                        continue;
                    }

                    boundsLeft = bounds.Left;
                    boundsRight = bounds.Right;
                    boundsBottom = bounds.Bottom;
                }
                else if (boundsRight <= baseLeft || boundsLeft >= baseRight)
                {
                    continue;
                }

                blockBottom = MathF.Max(blockBottom, boundsBottom);
                switch (anchor.WrapSide)
                {
                    case FloatingWrapSide.Left:
                        right = MathF.Min(right, boundsLeft);
                        break;
                    case FloatingWrapSide.Right:
                        left = MathF.Max(left, boundsRight);
                        break;
                    case FloatingWrapSide.Largest:
                    {
                        var leftSpace = MathF.Max(0f, boundsLeft - baseLeft);
                        var rightSpace = MathF.Max(0f, baseRight - boundsRight);
                        if (rightSpace > leftSpace)
                        {
                            left = MathF.Max(left, boundsRight);
                        }
                        else
                        {
                            right = MathF.Min(right, boundsLeft);
                        }

                        break;
                    }
                    default:
                    {
                        var center = (boundsLeft + boundsRight) / 2f;
                        if (center <= mid)
                        {
                            left = MathF.Max(left, boundsRight);
                        }
                        else
                        {
                            right = MathF.Min(right, boundsLeft);
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
                var wrapContour = CreateWrapContour(floating.Anchor, bounds);
                result.Add(new FloatingLayoutObject(floating, paragraphIndex, anchorPageIndex, bounds, wrapContour));
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
            var charGridSpacing = TextGridSnapping.GetCharacterSpacing(pageSettings.DocGrid);
            var (paragraphLineHeight, paragraphAscent) = ResolveParagraphLineMetrics(paragraphStyle, measurer, spacingMetricsCache);
            var dropCapActive = false;
            DropCapInfo dropCap = default;
            if (DocTextDirectionHelpers.IsVertical(properties.TextDirection))
            {
                return LayoutVerticalParagraphLines(text, spans);
            }

            if (wrapResolver is null
                && properties.DropCap?.HasValues == true
                && TryPrepareDropCap(text, spans, properties, paragraphLineHeight, paragraphAscent, pageSettings.DocGrid, measurer, out dropCap, out var dropCapSpans))
            {
                spans = dropCapSpans;
                dropCapActive = true;
            }

            var lineStartY = cursorY + spacingBefore;
            var paragraphLines = dropCapActive
                ? BuildParagraphLinesWithDropCap(
                    text,
                    spans,
                    dropCap,
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
                    paragraphLineHeight,
                    paragraphAscent,
                    pageSettings.DocGrid)
                : BuildParagraphLines(
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
                    paragraphLineHeight,
                    paragraphAscent,
                    pageSettings.DocGrid,
                    wrapResolver);

            if (paragraphLines.Count == 0)
            {
                var lineX = columnX + indentLeft + listIndent + firstLineIndent + prefixWidth;
                var lineRight = columnX + columnWidth - indentRight;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, pageSettings.DocGrid);
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
                (alignedX, cursorY) = ApplyDocGridSnapping(
                    alignedX,
                    cursorY,
                    emptyAscent,
                    properties.TextDirection,
                    pageSettings.DocGrid,
                    lineX,
                    columnTop);
                AddLine(new LayoutLine(paragraphIndex, 0, 0, alignedX, cursorY, 0, TextSlice.Empty, prefix, prefixWidth, emptyLineHeight, emptyAscent, Array.Empty<LayoutRun>(), Array.Empty<LayoutImage>(), Array.Empty<LayoutShape>(), Array.Empty<LayoutChart>(), Array.Empty<LayoutEquation>(), Array.Empty<LayoutRuby>(), properties.TextDirection, false, emptyIsRtl));
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
            var dropCapOffset = dropCapActive ? MathF.Max(0f, dropCap.Width + dropCap.Distance) : 0f;
            var dropCapLines = dropCapActive ? Math.Max(1, dropCap.Lines) : 0;
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

                    if (dropCapActive && dropCap.Kind == DropCapKind.Drop && lineIndex + i < dropCapLines && lineIndex + i > 0)
                    {
                        lineLeft += dropCapOffset;
                        if (lineRight - lineLeft < 1f)
                        {
                            lineLeft = MathF.Max(lineLeft, lineRight - 1f);
                        }
                    }

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
                        lineLayout = JustifyLineLayout(lineLayout, availableWidth, measurer, charGridSpacing);
                    }

                    var alignedX = ApplyAlignment(lineLeft, lineLayout.Width, availableWidth, alignment);
                    (alignedX, currentY) = ApplyDocGridSnapping(
                        alignedX,
                        currentY,
                        lineLayout.Ascent,
                        properties.TextDirection,
                        pageSettings.DocGrid,
                        lineLeft,
                        columnTop);
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
                    lineLayout.Rubies,
                    properties.TextDirection,
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

            LineRange LayoutVerticalParagraphLines(string paragraphText, IReadOnlyList<InlineSpan> paragraphSpans)
            {
                var verticalLineStart = lines.Count;
                if (string.IsNullOrEmpty(paragraphText))
                {
                    var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, pageSettings.DocGrid);
                    if (cursorY + spacingBefore + emptyLineHeight > contentBottom && cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                    }

                    cursorY += spacingBefore;
                    var emptyAxisOrigin = cursorY;
                    var lineAxisStart = emptyAxisOrigin + indentLeft + listIndent + firstLineIndent + prefixWidth;
                    var lineAxisLength = MathF.Max(1f, contentBottom - lineAxisStart - indentRight);
                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var lineX = columnX;
                    if (wrapResolver is not null)
                    {
                        var adjustedAxisStart = ApplyTopBottomWrap(lineAxisStart, emptyLineHeight);
                        var wrap = ResolveWrapForLine(ref adjustedAxisStart, emptyLineHeight, columnX, columnX + columnWidth, wrapResolver);
                        var axisShift = adjustedAxisStart - lineAxisStart;
                        if (axisShift > 0f)
                        {
                            emptyAxisOrigin += axisShift;
                            lineAxisStart = adjustedAxisStart;
                            lineAxisLength = MathF.Max(1f, contentBottom - lineAxisStart - indentRight);
                        }

                        var maxX = wrap.Right - emptyLineHeight;
                        if (maxX < wrap.Left)
                        {
                            maxX = wrap.Left;
                        }

                        lineX = Math.Clamp(lineX, wrap.Left, maxX);
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, 0f, lineAxisLength, alignment);
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        emptyAscent,
                        properties.TextDirection,
                        pageSettings.DocGrid,
                        lineAxisStart,
                        columnX);
                    var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                    AddLine(new LayoutLine(
                        paragraphIndex,
                        0,
                        0,
                        lineX,
                        alignedY,
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
                        Array.Empty<LayoutRuby>(),
                        properties.TextDirection,
                        false,
                        emptyIsRtl));
                    cursorY = alignedY + emptyLineHeight + spacingAfter;
                    return new LineRange(verticalLineStart, lines.Count - verticalLineStart);
                }

                var availableLength = MathF.Max(1f, contentBottom - columnTop);
                var verticalLines = BuildParagraphLines(
                    paragraphText,
                    paragraphSpans,
                    indentLeft,
                    indentRight,
                    firstLineIndent,
                    listIndent,
                    prefixWidth,
                    properties,
                    availableLength,
                    0f,
                    0f,
                    settings,
                    measurer,
                    lineHeight,
                    ascent,
                    pageSettings.DocGrid,
                    null);

                if (verticalLines.Count == 0)
                {
                    return new LineRange(verticalLineStart, 0);
                }

                var maxLineLength = 0f;
                foreach (var line in verticalLines)
                {
                    maxLineLength = MathF.Max(maxLineLength, line.Layout.Width);
                }

                var paragraphAxisHeight = spacingBefore + maxLineLength + spacingAfter;
                var pageContentHeight = contentBottom - columnTop;
                if ((keepLinesTogether || keepWithNext)
                    && paragraphAxisHeight + nextBlockMinHeight <= pageContentHeight
                    && cursorY + paragraphAxisHeight + nextBlockMinHeight > contentBottom
                    && cursorY > columnTop)
                {
                    StartNewColumnOrPage();
                }

                var spacingBeforeApplied = false;
                var lineAxisOrigin = cursorY;
                var currentX = columnX;
                var maxLineAxisEnd = cursorY;
                for (var i = 0; i < verticalLines.Count; i++)
                {
                    var line = verticalLines[i];
                    var lineLayout = line.Layout;

                    if (!spacingBeforeApplied)
                    {
                        if (cursorY + spacingBefore + lineLayout.Width > contentBottom && cursorY > columnTop)
                        {
                            StartNewColumnOrPage();
                        }

                        cursorY += spacingBefore;
                        lineAxisOrigin = cursorY;
                        maxLineAxisEnd = lineAxisOrigin;
                        spacingBeforeApplied = true;
                    }
                    else if (lineAxisOrigin + lineLayout.Width > contentBottom && lineAxisOrigin > columnTop)
                    {
                        StartNewColumnOrPage();
                        currentX = columnX;
                        lineAxisOrigin = columnTop;
                        maxLineAxisEnd = lineAxisOrigin;
                    }

                    if (currentX + lineLayout.LineHeight > columnX + columnWidth && currentX > columnX)
                    {
                        StartNewColumnOrPage();
                        currentX = columnX;
                        lineAxisOrigin = columnTop;
                        maxLineAxisEnd = lineAxisOrigin;
                    }

                    var baseAxisStart = lineAxisOrigin + indentLeft + listIndent + (line.IsFirstLine ? firstLineIndent : 0f) + prefixWidth;
                    var lineAxisStart = baseAxisStart;
                    var lineAxisLength = MathF.Max(1f, contentBottom - lineAxisStart - indentRight);
                    var lineX = currentX;

                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var isLastLine = IsLastParagraphLine(paragraphText, line.Start + line.Length);
                    if (alignment == ParagraphAlignment.Justify && !isLastLine)
                    {
                        lineLayout = JustifyLineLayout(lineLayout, lineAxisLength, measurer, charGridSpacing);
                    }

                    if (wrapResolver is not null)
                    {
                        var adjustedAxisStart = ApplyTopBottomWrap(lineAxisStart, lineLayout.Width);
                        var wrap = ResolveWrapForLine(ref adjustedAxisStart, lineLayout.Width, columnX, columnX + columnWidth, wrapResolver);
                        var axisShift = adjustedAxisStart - baseAxisStart;
                        if (axisShift > 0f)
                        {
                            lineAxisOrigin += axisShift;
                            lineAxisStart = adjustedAxisStart;
                            lineAxisLength = MathF.Max(1f, contentBottom - lineAxisStart - indentRight);
                        }

                        var maxX = wrap.Right - lineLayout.LineHeight;
                        if (maxX < wrap.Left)
                        {
                            maxX = wrap.Left;
                        }

                        lineX = Math.Clamp(lineX, wrap.Left, maxX);
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, lineLayout.Width, lineAxisLength, alignment);
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        lineLayout.Ascent,
                        properties.TextDirection,
                        pageSettings.DocGrid,
                        lineAxisStart,
                        columnX);
                    var isRtl = ResolveLineIsRtl(properties, line.TextSlice);
                    AddLine(new LayoutLine(
                        paragraphIndex,
                        line.Start,
                        line.Length,
                        lineX,
                        alignedY,
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
                        lineLayout.Rubies,
                        properties.TextDirection,
                        false,
                        isRtl));

                    maxLineAxisEnd = MathF.Max(maxLineAxisEnd, alignedY + lineLayout.Width);
                    currentX = lineX + lineLayout.LineHeight;
                }

                cursorY = maxLineAxisEnd + spacingAfter;
                return new LineRange(verticalLineStart, lines.Count - verticalLineStart);
            }
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

                if (!AreWrapContoursClose(first.WrapContour, second.WrapContour, epsilon))
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
            var columnGaps = ResolveSectionColumnGaps(pageSection, Math.Max(1, pageSection.ColumnCount), columnGap);
            var columnWidths = ResolveSectionColumnWidths(pageSection, contentWidth, columnGaps);
            if (columnWidths.Length <= 1)
            {
                return;
            }

            var columnOffsets = BuildColumnOffsets(columnWidths, columnGaps);
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
                    GetLineBlockHeight(line),
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
            var runs = new[] { new LayoutRun(label, textStyle, 0f, width, label.Length, false, 0f, LetterSpacing: textStyle.LetterSpacing) };
            var isRtl = TextBidi.ResolveBaseIsRtl(textSlice.Span, null);
            AddLine(new LayoutLine(-1, 0, label.Length, columnX, cursorY, width, textSlice, null, 0f, lineHeight, ascent,
                runs, Array.Empty<LayoutImage>(), Array.Empty<LayoutShape>(), Array.Empty<LayoutChart>(), Array.Empty<LayoutEquation>(), Array.Empty<LayoutRuby>(), null, false, isRtl));

            cursorY += lineHeight + spacingAfter;
        }

        ParagraphLayoutPlan BuildParagraphPlan(ParagraphBlock paragraph, int blockIndex)
        {
            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var lineHeightAdjusted = ResolveParagraphLineHeight(
                paragraphStyle,
                measurer,
                spacingMetricsCache,
                properties,
                pageSettings.DocGrid);
            var spacingBefore = ResolveParagraphSpacing(
                properties.SpacingBefore,
                properties.SpacingBeforeLines,
                properties.AutoSpacingBefore,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            var spacingAfter = ResolveParagraphSpacing(
                properties.SpacingAfter,
                properties.SpacingAfterLines,
                properties.AutoSpacingAfter,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            if (properties.ContextualSpacing == true)
            {
                if (blockIndex > 0 && blocks[blockIndex - 1] is ParagraphBlock previousParagraph
                    && IsSameParagraphStyle(document, previousParagraph, paragraph))
                {
                    spacingBefore = 0f;
                }

                if (blockIndex + 1 < blocks.Count && blocks[blockIndex + 1] is ParagraphBlock nextParagraph
                    && IsSameParagraphStyle(document, paragraph, nextParagraph))
                {
                    spacingAfter = 0f;
                }
            }
            paragraphSpacingBefore[paragraphIndex] = spacingBefore;
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;
            var paragraphBorders = properties.Borders.HasAny ? properties.Borders.Clone() : null;
            var paragraphShading = properties.ShadingColor;

            var keepWithNext = properties.KeepWithNext == true;
            var keepLinesTogether = properties.KeepLinesTogether == true;
            var widowControl = properties.WidowControl ?? true;
            var nextBlockMinHeight = keepWithNext
                ? EstimateNextBlockMinHeight(blockIndex, blocks, document, styleResolver, settings, measurer, style, spacingMetricsCache, columnWidth, lineHeight, ascent, pageSettings.DocGrid)
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

        bool TryHandleFrameParagraph(ParagraphLayoutPlan plan)
        {
            var frame = plan.Properties.Frame;
            if (frame is null || !frame.HasValues)
            {
                return false;
            }

            var (text, spans) = BuildInlineSpans(plan.Paragraph, plan.ParagraphStyle, styleResolver);
            var charGridSpacing = TextGridSnapping.GetCharacterSpacing(pageSettings.DocGrid);
            var baseWidth = MathF.Max(1f, columnWidth - plan.IndentLeft - plan.IndentRight - plan.ListIndent - plan.PrefixWidth);
            var measuredWidth = text.Length == 0
                ? baseWidth
                : MeasureInlineSpans(spans, 0, text.Length, plan.Properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, plan.IndentLeft + plan.ListIndent + plan.FirstLineIndent + plan.PrefixWidth);
            var isDropCapFrame = plan.Properties.DropCap?.HasValues == true;
            var frameWidth = frame.Width ?? MathF.Min(columnWidth, MathF.Max(isDropCapFrame ? 1f : 120f, measuredWidth));
            if (frameWidth <= 0f)
            {
                frameWidth = MathF.Max(isDropCapFrame ? 1f : 120f, baseWidth);
            }

            var (paragraphLineHeight, paragraphAscent) = ResolveParagraphLineMetrics(plan.ParagraphStyle, measurer, spacingMetricsCache);
            var (lineHeightAdjusted, lineAscentAdjusted) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, plan.Properties, pageSettings.DocGrid);
            var lineCount = 1;
            if (text.Length > 0)
            {
                var firstLineTabOffset = plan.IndentLeft + plan.ListIndent + plan.FirstLineIndent + plan.PrefixWidth;
                var otherLineTabOffset = plan.IndentLeft + plan.ListIndent + plan.PrefixWidth;
                lineCount = WrapParagraph(
                        text,
                        spans,
                        frameWidth,
                        frameWidth,
                        plan.Properties,
                        settings,
                        measurer,
                        charGridSpacing,
                        firstLineTabOffset,
                        otherLineTabOffset)
                    .Count();
                lineCount = Math.Max(1, lineCount);
            }

            var frameHeight = frame.Height ?? MathF.Max(lineHeightAdjusted, lineCount * lineHeightAdjusted);
            if (frameHeight <= 0f)
            {
                frameHeight = MathF.Max(lineHeightAdjusted, paragraphLineHeight);
            }

            var shapeParagraph = BuildFrameParagraph(plan.Paragraph, plan.Properties);
            var shape = BuildFrameShape(shapeParagraph, frameWidth, frameHeight);
            var floating = new FloatingObject(shape);
            ApplyFrameAnchor(frame, floating.Anchor);

            var requiredHeight = MathF.Max(lineHeightAdjusted, frameHeight);
            if (plan.KeepWithNext
                && plan.NextBlockMinHeight > 0f
                && cursorY + plan.SpacingBefore + requiredHeight + plan.NextBlockMinHeight > contentBottom
                && cursorY > columnTop)
            {
                StartNewColumnOrPage();
            }

            var anchorY = cursorY + plan.SpacingBefore;
            if (anchorY + requiredHeight > contentBottom && cursorY > columnTop)
            {
                StartNewColumnOrPage();
                anchorY = cursorY + plan.SpacingBefore;
            }

            var anchorX = columnX + plan.IndentLeft + plan.ListIndent + plan.PrefixWidth;
            var anchorLine = new LayoutLine(
                paragraphIndex,
                0,
                0,
                anchorX,
                anchorY,
                0f,
                TextSlice.Empty,
                null,
                0f,
                lineHeightAdjusted,
                lineAscentAdjusted,
                Array.Empty<LayoutRun>(),
                Array.Empty<LayoutImage>(),
                Array.Empty<LayoutShape>(),
                Array.Empty<LayoutChart>(),
                Array.Empty<LayoutEquation>(),
                Array.Empty<LayoutRuby>(),
                plan.Properties.TextDirection,
                false,
                false);

            var anchorPageIndex = Math.Clamp(pageIndex, 0, pages.Count - 1);
            var anchorPage = pages[anchorPageIndex];
            var anchorSection = anchorPageIndex < pageSections.Count ? pageSections[anchorPageIndex] : sectionSettings;
            var baseX = ResolveAnchorX(floating.Anchor, anchorLine, anchorPage, anchorSection, frameWidth);
            var baseY = ResolveAnchorY(floating.Anchor, anchorLine, anchorPage, frameHeight);
            var bounds = new DocRect(baseX + floating.Anchor.OffsetX, baseY + floating.Anchor.OffsetY, frameWidth, frameHeight);
            var wrapContour = CreateWrapContour(floating.Anchor, bounds);
            var layoutObject = new FloatingLayoutObject(floating, paragraphIndex, anchorPageIndex, bounds, wrapContour);

            extraFloatingObjects.Add(layoutObject);
            if (floating.Anchor.WrapStyle != FloatingWrapStyle.None)
            {
                wrapFloatingObjects.Add(layoutObject);
            }

            paragraphLineRanges[paragraphIndex] = new LineRange(lines.Count, 0);
            return true;
        }

        bool TryHandleFloatingTable(TableBlock table)
        {
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var floatingAnchor = resolvedTableProperties.FloatingAnchor;
            if (floatingAnchor is null)
            {
                return false;
            }

            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var anchorParagraphIndex = paragraphIndex;
            var data = ComputeTableLayoutData(
                table,
                document,
                resolvedTableProperties,
                table.Properties,
                tableStyle,
                tableLook,
                columnWidth,
                settings,
                measurer,
                style,
                styleResolver,
                spacingMetricsCache,
                lineHeight,
                ascent,
                pageSettings.DocGrid,
                ref paragraphIndex);

            var slices = new List<TableRowSlice>(data.RowHeights.Length);
            for (var i = 0; i < data.RowHeights.Length; i++)
            {
                slices.Add(new TableRowSlice(i, 0f, data.RowHeights[i]));
            }

            var baseLayout = BuildTableLayout(
                table,
                resolvedTableProperties,
                data,
                0f,
                0f,
                slices,
                settings,
                includeTopSpacing: true,
                includeBottomSpacing: true,
                continuesFromPrevious: false,
                continuesOnNext: false);

            var tableWidth = baseLayout.Bounds.Width;
            var tableHeight = baseLayout.Bounds.Height;
            var anchorPageIndex = Math.Clamp(pageIndex, 0, pages.Count - 1);
            var anchorPage = pages[anchorPageIndex];
            var anchorSection = anchorPageIndex < pageSections.Count ? pageSections[anchorPageIndex] : sectionSettings;
            var anchorLine = new LayoutLine(
                anchorParagraphIndex,
                0,
                0,
                columnX,
                cursorY,
                columnWidth,
                TextSlice.Empty,
                null,
                0f,
                lineHeight,
                ascent,
                Array.Empty<LayoutRun>(),
                Array.Empty<LayoutImage>(),
                Array.Empty<LayoutShape>(),
                Array.Empty<LayoutChart>(),
                Array.Empty<LayoutEquation>(),
                Array.Empty<LayoutRuby>(),
                null,
                false,
                false);

            var baseX = ResolveAnchorX(floatingAnchor, anchorLine, anchorPage, anchorSection, tableWidth);
            var baseY = ResolveAnchorY(floatingAnchor, anchorLine, anchorPage, tableHeight);
            var x = baseX + floatingAnchor.OffsetX;
            var y = baseY + floatingAnchor.OffsetY;

            if (y + tableHeight > contentBottom && cursorY > columnTop)
            {
                StartNewColumnOrPage();
                anchorPageIndex = Math.Clamp(pageIndex, 0, pages.Count - 1);
                anchorPage = pages[anchorPageIndex];
                anchorSection = anchorPageIndex < pageSections.Count ? pageSections[anchorPageIndex] : sectionSettings;
                anchorLine = anchorLine with { X = columnX, Y = cursorY, Width = columnWidth };
                baseX = ResolveAnchorX(floatingAnchor, anchorLine, anchorPage, anchorSection, tableWidth);
                baseY = ResolveAnchorY(floatingAnchor, anchorLine, anchorPage, tableHeight);
                x = baseX + floatingAnchor.OffsetX;
                y = baseY + floatingAnchor.OffsetY;
            }

            var tableLayout = OffsetTableLayout(baseLayout, x - baseLayout.Bounds.X, y - baseLayout.Bounds.Y);
            tables.Add(tableLayout);
            AddTableLines(tableLayout);

            var floating = new FloatingObject(new TableInline());
            ApplyFloatingAnchor(floatingAnchor, floating.Anchor);
            var wrapContour = CreateWrapContour(floating.Anchor, tableLayout.Bounds);
            var layoutObject = new FloatingLayoutObject(floating, anchorParagraphIndex, anchorPageIndex, tableLayout.Bounds, wrapContour);
            extraFloatingObjects.Add(layoutObject);
            if (floating.Anchor.WrapStyle != FloatingWrapStyle.None)
            {
                wrapFloatingObjects.Add(layoutObject);
            }

            return true;
        }

        void HandleParagraph(ParagraphBlock paragraph, int blockIndex)
        {
            var plan = BuildParagraphPlan(paragraph, blockIndex);
            if (plan.Properties.PageBreakBefore == true && cursorY > contentTop + 0.5f)
            {
                StartNewPage();
            }
            paragraphSpacingBefore[paragraphIndex] = plan.SpacingBefore;
            if (TryHandleFrameParagraph(plan))
            {
                paragraphIndex++;
                return;
            }
            _ = LayoutParagraphWithReflow(plan);
            paragraphIndex++;
        }

        TableLayoutPlan BuildTablePlan(TableBlock table)
        {
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, columnWidth, settings, measurer, style, styleResolver, spacingMetricsCache, lineHeight, ascent, pageSettings.DocGrid, ref paragraphIndex);
            return new TableLayoutPlan(table, resolvedTableProperties, data);
        }

        void LayoutTablePlan(TableLayoutPlan plan)
        {
            var data = plan.Data;
            var tableX = ResolveTableX(columnX, columnWidth, plan.ResolvedProperties, data.TableWidth);
            var rowStart = 0;
            var rowOffset = 0f;
            var totalRows = data.RowHeights.Length;
            var headerRowCount = GetRepeatHeaderRowCount(plan.Table);
            if (headerRowCount > 0 && HasHeaderRowSpanCrossing(data, headerRowCount))
            {
                headerRowCount = 0;
            }
            var rowSplittable = BuildRowSplitMap(plan.Table, data);
            if (totalRows == 0)
            {
                var emptyLayout = BuildTableLayout(
                    plan.Table,
                    plan.ResolvedProperties,
                    data,
                    tableX,
                    cursorY,
                    Array.Empty<TableRowSlice>(),
                    settings,
                    includeTopSpacing: true,
                    includeBottomSpacing: true,
                    continuesFromPrevious: false,
                    continuesOnNext: false);
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
                    tableX = ResolveTableX(columnX, columnWidth, plan.ResolvedProperties, data.TableWidth);
                    availableHeight = contentBottom - cursorY;
                }

                var includeHeader = headerRowCount > 0 && rowStart >= headerRowCount;
                var headerHeight = 0f;
                if (includeHeader)
                {
                    headerHeight = ComputeHeaderBlockHeight(data.RowHeights, headerRowCount, data.CellSpacing, includeGapToBody: true);
                    if (availableHeight - headerHeight <= 0f && cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                        tableX = ResolveTableX(columnX, columnWidth, plan.ResolvedProperties, data.TableWidth);
                        availableHeight = contentBottom - cursorY;
                    }
                }

                var bodyAvailableHeight = MathF.Max(0f, availableHeight - headerHeight);
                var slices = BuildRowSlices(plan.Table, data, rowStart, rowOffset, bodyAvailableHeight, rowSplittable, out var nextRow, out var nextOffset);
                if (slices.Count == 0)
                {
                    if (cursorY > columnTop)
                    {
                        StartNewColumnOrPage();
                        tableX = ResolveTableX(columnX, columnWidth, plan.ResolvedProperties, data.TableWidth);
                        continue;
                    }

                    var rowHeight = data.RowHeights[rowStart];
                    var remaining = MathF.Max(0f, rowHeight - rowOffset);
                    slices.Add(new TableRowSlice(rowStart, rowOffset, remaining));
                    nextRow = rowStart + 1;
                    nextOffset = 0f;
                }

                var continuesFromPrevious = rowStart > 0 || rowOffset > 0f;
                var continuesOnNext = nextRow < totalRows || nextOffset > 0f;
                TableLayout tableLayout;
                if (includeHeader)
                {
                    tableLayout = BuildTableLayoutWithHeader(
                        plan.Table,
                        plan.ResolvedProperties,
                        data,
                        tableX,
                        cursorY,
                        headerRowCount,
                        slices,
                        settings,
                        includeTopSpacing: !continuesFromPrevious,
                        includeBottomSpacing: !continuesOnNext,
                        continuesFromPrevious: continuesFromPrevious,
                        continuesOnNext: continuesOnNext);
                }
                else
                {
                    tableLayout = BuildTableLayout(
                        plan.Table,
                        plan.ResolvedProperties,
                        data,
                        tableX,
                        cursorY,
                        slices,
                        settings,
                        includeTopSpacing: !continuesFromPrevious,
                        includeBottomSpacing: !continuesOnNext,
                        continuesFromPrevious: continuesFromPrevious,
                        continuesOnNext: continuesOnNext);
                }
                tables.Add(tableLayout);
                AddTableLines(tableLayout);
                cursorY += tableLayout.Bounds.Height + settings.BlockSpacing;
                rowStart = nextRow;
                rowOffset = nextOffset;

                if (rowStart < totalRows)
                {
                    StartNewColumnOrPage();
                    tableX = ResolveTableX(columnX, columnWidth, plan.ResolvedProperties, data.TableWidth);
                }
            }
        }

        void HandleTable(TableBlock table, int __)
        {
            if (TryHandleFloatingTable(table))
            {
                return;
            }

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

            static HeaderFooter? ResolveEffectiveHeaderFooter(HeaderFooter current, HeaderFooter? previous)
            {
                if (current.IsDefined || current.Blocks.Count > 0)
                {
                    return current;
                }

                return previous;
            }

            var totalPages = Math.Max(1, pages.Count);
            var pageNumberTexts = BuildPageNumberTexts(pages, pageSections);
            var totalPagesTexts = BuildTotalPagesTexts(pages, pageSections, totalPages);
            var currentSectionIndex = -1;
            HeaderFooter? effectiveDefaultHeader = null;
            HeaderFooter? effectiveDefaultFooter = null;
            HeaderFooter? effectiveFirstHeader = null;
            HeaderFooter? effectiveFirstFooter = null;
            HeaderFooter? effectiveEvenHeader = null;
            HeaderFooter? effectiveEvenFooter = null;
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var section = pageSections[i];
                var sectionInfo = document.GetSection(section.SectionIndex);
                if (section.SectionIndex != currentSectionIndex)
                {
                    currentSectionIndex = section.SectionIndex;
                    effectiveDefaultHeader = ResolveEffectiveHeaderFooter(sectionInfo.Header, effectiveDefaultHeader);
                    effectiveDefaultFooter = ResolveEffectiveHeaderFooter(sectionInfo.Footer, effectiveDefaultFooter);
                    effectiveFirstHeader = ResolveEffectiveHeaderFooter(sectionInfo.FirstHeader, effectiveFirstHeader);
                    effectiveFirstFooter = ResolveEffectiveHeaderFooter(sectionInfo.FirstFooter, effectiveFirstFooter);
                    effectiveEvenHeader = ResolveEffectiveHeaderFooter(sectionInfo.EvenHeader, effectiveEvenHeader);
                    effectiveEvenFooter = ResolveEffectiveHeaderFooter(sectionInfo.EvenFooter, effectiveEvenFooter);
                }

                var pageNumber = page.Index + 1;
                var pageNumberText = i < pageNumberTexts.Length
                    ? pageNumberTexts[i]
                    : pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var totalPagesText = i < totalPagesTexts.Length
                    ? totalPagesTexts[i]
                    : totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var isFirstPageOfSection = i == 0 || pageSections[i - 1].SectionIndex != section.SectionIndex;
                var isEvenPage = pageNumber % 2 == 0;
                var headerSource = effectiveDefaultHeader;
                var footerSource = effectiveDefaultFooter;

                if (isFirstPageOfSection && sectionInfo.Properties.DifferentFirstPageHeaderFooter == true)
                {
                    headerSource = effectiveFirstHeader;
                    footerSource = effectiveFirstFooter;
                }
                else if (document.EvenAndOddHeaders && isEvenPage)
                {
                    headerSource = effectiveEvenHeader;
                    footerSource = effectiveEvenFooter;
                }

                if ((headerSource?.Blocks.Count ?? 0) == 0 && (footerSource?.Blocks.Count ?? 0) == 0)
                {
                    continue;
                }

                var headerFooterContentWidth = MathF.Max(1f, page.Bounds.Width - section.MarginLeft - section.MarginRight);
                IReadOnlyList<Block> headerBlocks = headerSource is null ? Array.Empty<Block>() : headerSource.Blocks;
                IReadOnlyList<Block> footerBlocks = footerSource is null ? Array.Empty<Block>() : footerSource.Blocks;
                var headerLayout = LayoutHeaderFooterBlocks(
                    headerBlocks,
                    document,
                    settings,
                    measurer,
                    style,
                    styleResolver,
                    spacingMetricsCache,
                    headerFooterContentWidth,
                    lineHeight,
                    ascent,
                    section.DocGrid,
                    pageNumberText,
                    totalPagesText,
                    includeTables: true,
                    handleFrameParagraphs: true);
                var footerLayout = LayoutHeaderFooterBlocks(
                    footerBlocks,
                    document,
                    settings,
                    measurer,
                    style,
                    styleResolver,
                    spacingMetricsCache,
                    headerFooterContentWidth,
                    lineHeight,
                    ascent,
                    section.DocGrid,
                    pageNumberText,
                    totalPagesText,
                    includeTables: true,
                    handleFrameParagraphs: true);

                var headerTop = page.Bounds.Y + section.HeaderOffset;
                var footerTop = page.Bounds.Bottom - section.FooterOffset - footerLayout.Height;
                var headerLines = OffsetHeaderFooterLines(headerLayout.Lines, page.Bounds.X + section.MarginLeft, headerTop);
                var footerLines = OffsetHeaderFooterLines(footerLayout.Lines, page.Bounds.X + section.MarginLeft, footerTop);
                var headerTables = OffsetHeaderFooterTables(headerLayout.Tables, page.Bounds.X + section.MarginLeft, headerTop);
                var footerTables = OffsetHeaderFooterTables(footerLayout.Tables, page.Bounds.X + section.MarginLeft, footerTop);
                var headerFloatingObjects = BuildHeaderFooterFloatingObjects(
                    headerBlocks,
                    headerLines,
                    headerLayout.ParagraphLineRanges,
                    page,
                    section);
                var footerFloatingObjects = BuildHeaderFooterFloatingObjects(
                    footerBlocks,
                    footerLines,
                    footerLayout.ParagraphLineRanges,
                    page,
                    section);
                var headerFrameObjects = BuildHeaderFooterFrameObjects(
                    headerLayout.FrameLayouts,
                    page,
                    section,
                    page.Bounds.X + section.MarginLeft,
                    headerTop);
                var footerFrameObjects = BuildHeaderFooterFrameObjects(
                    footerLayout.FrameLayouts,
                    page,
                    section,
                    page.Bounds.X + section.MarginLeft,
                    footerTop);
                var floatingObjects = new List<FloatingLayoutObject>(
                    headerFloatingObjects.Count
                    + footerFloatingObjects.Count
                    + headerFrameObjects.Count
                    + footerFrameObjects.Count);
                if (headerFloatingObjects.Count > 0)
                {
                    floatingObjects.AddRange(headerFloatingObjects);
                }

                if (headerFrameObjects.Count > 0)
                {
                    floatingObjects.AddRange(headerFrameObjects);
                }

                if (footerFloatingObjects.Count > 0)
                {
                    floatingObjects.AddRange(footerFloatingObjects);
                }

                if (footerFrameObjects.Count > 0)
                {
                    floatingObjects.AddRange(footerFrameObjects);
                }

                headerFooters.Add(new HeaderFooterLayout(page.Index, headerLines, footerLines, headerTables, footerTables, floatingObjects));
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
                spacingMetricsCache,
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
        if (extraFloatingObjects.Count > 0)
        {
            floatingObjects.AddRange(extraFloatingObjects);
        }
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
                var wrapContour = CreateWrapContour(floating.Anchor, bounds);
                result.Add(new FloatingLayoutObject(floating, paragraphIndex, anchorPageIndex, bounds, wrapContour));
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

    private static int ResolveAnchorLineIndex(
        IReadOnlyList<HeaderFooterLine> lines,
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

    private static FloatingWrapContour? CreateWrapContour(FloatingAnchor anchor, DocRect bounds)
    {
        if (anchor.WrapStyle is not (FloatingWrapStyle.Tight or FloatingWrapStyle.Through))
        {
            return null;
        }

        var polygon = anchor.WrapPolygon;
        if (polygon is null)
        {
            return null;
        }

        var points = polygon.Points;
        if (points.Length < 3)
        {
            return null;
        }

        var absolute = new DocPoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            absolute[i] = new DocPoint(point.X + bounds.X, point.Y + bounds.Y);
        }

        return new FloatingWrapContour(absolute);
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

    private static bool TryResolveWrapContourBounds(
        FloatingLayoutObject floating,
        float lineTop,
        float lineHeight,
        out float left,
        out float right,
        out float blockBottom)
    {
        left = 0f;
        right = 0f;
        blockBottom = 0f;

        var contour = floating.WrapContour;
        if (contour is null)
        {
            return false;
        }

        var anchor = floating.Object.Anchor;
        if (anchor.WrapStyle is not (FloatingWrapStyle.Tight or FloatingWrapStyle.Through))
        {
            return false;
        }

        var top = contour.Bounds.Y - anchor.Distance.Top;
        var bottom = contour.Bounds.Bottom + anchor.Distance.Bottom;
        if (lineTop >= bottom || lineTop + lineHeight <= top)
        {
            return false;
        }

        var sampleY = lineTop + lineHeight * 0.5f;
        if (sampleY < contour.Bounds.Y || sampleY > contour.Bounds.Bottom)
        {
            sampleY = Math.Clamp(sampleY, contour.Bounds.Y, contour.Bounds.Bottom);
        }

        if (!contour.TryGetHorizontalSpan(sampleY, out var spanLeft, out var spanRight))
        {
            return false;
        }

        left = spanLeft - anchor.Distance.Left;
        right = spanRight + anchor.Distance.Right;
        blockBottom = bottom;
        return true;
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

    private static bool AreWrapContoursClose(FloatingWrapContour? left, FloatingWrapContour? right, float epsilon)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (!AreBoundsClose(left.Bounds, right.Bounds, epsilon))
        {
            return false;
        }

        var leftPoints = left.Points;
        var rightPoints = right.Points;
        if (leftPoints.Length != rightPoints.Length)
        {
            return false;
        }

        for (var i = 0; i < leftPoints.Length; i++)
        {
            if (MathF.Abs(leftPoints[i].X - rightPoints[i].X) > epsilon
                || MathF.Abs(leftPoints[i].Y - rightPoints[i].Y) > epsilon)
            {
                return false;
            }
        }

        return true;
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

    private static float ResolveAnchorX(FloatingAnchor anchor, HeaderFooterLine line, PageLayout page, PageSectionSettings section, float width)
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

    private static float ResolveAnchorY(FloatingAnchor anchor, HeaderFooterLine line, PageLayout page, float height)
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

    private static (float X, float Width) ResolveHorizontalReferenceBounds(
        FloatingAnchor anchor,
        HeaderFooterLine line,
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

    private static (float Y, float Height) ResolveVerticalReferenceBounds(FloatingAnchor anchor, HeaderFooterLine line, PageLayout page)
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
        var columnGaps = ResolveSectionColumnGaps(section, columnCount, columnGap);
        var columnWidths = ResolveSectionColumnWidths(section, page.ContentBounds.Width, columnGaps);
        var offsets = BuildColumnOffsets(columnWidths, columnGaps);
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
        var columnGaps = ResolveSectionColumnGaps(section, columnCount, columnGap);
        var columnWidths = ResolveSectionColumnWidths(section, page.ContentBounds.Width, columnGaps);
        if (columnWidths.Length == 0)
        {
            return page.ContentBounds.Width;
        }

        var offsets = BuildColumnOffsets(columnWidths, columnGaps);
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
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid)
    {
        var tableStyle = styleResolver.ResolveTableStyle(table);
        var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
        var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
        var paragraphIndex = 0;
        var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, contentWidth, settings, measurer, style, styleResolver, spacingMetricsCache, lineHeight, ascent, docGrid, ref paragraphIndex);
        var slices = new List<TableRowSlice>(data.RowHeights.Length);
        for (var i = 0; i < data.RowHeights.Length; i++)
        {
            slices.Add(new TableRowSlice(i, 0f, data.RowHeights[i]));
        }

        return BuildTableLayout(
            table,
            resolvedTableProperties,
            data,
            tableX,
            tableY,
            slices,
            settings,
            includeTopSpacing: true,
            includeBottomSpacing: true,
            continuesFromPrevious: false,
            continuesOnNext: false);
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

    private readonly record struct TableRowSlice(int RowIndex, float Offset, float Height);

    private sealed record TableLayoutData(
        float[] ColumnWidths,
        float[] RowHeights,
        List<TableCellPlacement> Cells,
        List<TableCellPlacement>[] PlacementsByRow,
        int Columns,
        int Rows,
        float TableWidth,
        float CellSpacing);

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
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid,
        ref int paragraphIndex)
    {
        var rows = table.Rows;
        var rowCount = rows.Count;
        var columnCount = ResolveTableColumnCount(rows, resolvedTableProperties);
        var tableWidth = ResolveTableWidth(resolvedTableProperties, contentWidth);
        var spacingBaseWidth = tableWidth ?? ResolveTableSpacingBaseWidth(resolvedTableProperties, contentWidth);
        var cellSpacing = ResolveTableCellSpacing(resolvedTableProperties, spacingBaseWidth);
        var separateBorders = cellSpacing > 0f;
        var columnWidths = ResolveColumnWidths(resolvedTableProperties, columnCount, contentWidth, tableWidth, cellSpacing);
        var effectiveTableWidth = MathF.Max(0f, columnWidths.Sum() + cellSpacing * (columnCount + 1));
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
            var columnIndex = Math.Clamp(row.Properties.GridBefore ?? 0, 0, columnCount);
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
            var maxHeight = 0f;
            foreach (var placement in placementsByRow[rowIndex])
            {
                var effectiveProperties = ResolveTableCellProperties(
                    placement.Cell,
                    row.Properties,
                    directTableProperties,
                    tableStyle,
                    tableLook,
                    separateBorders,
                    rowIndex,
                    placement.ColumnIndex,
                    rowCount,
                    columnCount);
                placement.Properties = effectiveProperties;
                placement.Padding = ResolvePadding(effectiveProperties.Padding, tablePadding);

                if (!placement.IsMergeContinuation)
                {
                    var spanWidth = SumColumnsWithSpacing(columnWidths, placement.ColumnIndex, placement.ColumnSpan, cellSpacing);
                    var cellLines = LayoutCellParagraphs(placement.Cell, document, spanWidth, placement.Padding, settings, measurer, style, styleResolver, spacingMetricsCache, lineHeight, ascent, docGrid, ref paragraphIndex);
                    placement.Lines = cellLines;
                    placement.ContentHeight = cellLines.Count == 0 ? 0f : cellLines.Last().Y + GetLineBlockHeight(cellLines.Last());

                    if (placement.RowSpan <= 1)
                    {
                        var cellHeight = placement.ContentHeight + placement.Padding.Vertical;
                        if (cellHeight <= 0f)
                        {
                            cellHeight = lineHeight + placement.Padding.Vertical;
                        }

                        maxHeight = MathF.Max(maxHeight, cellHeight);
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
                else if (rule != TableRowHeightRule.Auto)
                {
                    maxHeight = MathF.Max(maxHeight, height);
                }
            }

            if (maxHeight <= 0f)
            {
                maxHeight = lineHeight + defaultPadding.Vertical;
            }

            rowHeights[rowIndex] = maxHeight;
        }

        foreach (var placement in placements)
        {
            if (placement.IsMergeContinuation || placement.RowSpan <= 1)
            {
                continue;
            }

            var requiredHeight = placement.ContentHeight + placement.Padding.Vertical;
            if (requiredHeight <= 0f)
            {
                requiredHeight = lineHeight + placement.Padding.Vertical;
            }
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

                if (targetRow >= 0)
                {
                    rowHeights[targetRow] += delta;
                }
            }
        }

        return new TableLayoutData(columnWidths, rowHeights, placements, placementsByRow, columnCount, rowCount, effectiveTableWidth, cellSpacing);
    }

    private static TableLayout BuildTableLayout(
        TableBlock table,
        TableProperties tableProperties,
        TableLayoutData data,
        float tableX,
        float tableY,
        IReadOnlyList<TableRowSlice> slices,
        LayoutSettings settings,
        bool includeTopSpacing,
        bool includeBottomSpacing,
        bool continuesFromPrevious,
        bool continuesOnNext)
    {
        var columnCount = data.Columns;
        var rowCount = slices.Count;
        var cellSpacing = data.CellSpacing;
        var topSpacing = includeTopSpacing ? cellSpacing : 0f;
        var bottomSpacing = includeBottomSpacing ? cellSpacing : 0f;
        var cellLayouts = new List<TableCellLayout>();
        var columnOffsets = new float[columnCount];
        var offset = cellSpacing;
        for (var i = 0; i < columnCount; i++)
        {
            columnOffsets[i] = offset;
            offset += data.ColumnWidths[i] + cellSpacing;
        }

        var rowOffsets = new float[rowCount];
        var rowOffset = topSpacing;
        for (var i = 0; i < rowCount; i++)
        {
            rowOffsets[i] = rowOffset;
            rowOffset += slices[i].Height;
            if (i < rowCount - 1)
            {
                rowOffset += cellSpacing;
            }
        }

        var rowIndexMap = BuildRowIndexMap(data.Rows, slices);
        var proxyOrigins = new HashSet<(int Row, int Column)>();
        foreach (var placement in data.Cells.OrderBy(cell => cell.RowIndex).ThenBy(cell => cell.ColumnIndex))
        {
            var localRowIndex = placement.RowIndex >= 0 && placement.RowIndex < rowIndexMap.Length
                ? rowIndexMap[placement.RowIndex]
                : -1;

            if (placement.IsMergeContinuation)
            {
                if (localRowIndex < 0)
                {
                    continue;
                }

                if (placement.MergeOrigin is not { } origin || origin.RowIndex < 0 || origin.RowIndex >= rowIndexMap.Length)
                {
                    continue;
                }

                if (rowIndexMap[origin.RowIndex] >= 0)
                {
                    continue;
                }

                var key = (origin.RowIndex, origin.ColumnIndex);
                if (!proxyOrigins.Add(key))
                {
                    continue;
                }

                var spanInChunk = CountSpanInLayout(rowIndexMap, placement.RowIndex, origin.RowSpan - (placement.RowIndex - origin.RowIndex));
                if (spanInChunk <= 0)
                {
                    continue;
                }

                var cellX = tableX + columnOffsets[origin.ColumnIndex];
                var cellWidth = SumColumnsWithSpacing(data.ColumnWidths, origin.ColumnIndex, origin.ColumnSpan, cellSpacing);
                var cellY = tableY + rowOffsets[localRowIndex];
                var cellHeight = SumSliceHeights(rowOffsets, slices, localRowIndex, spanInChunk, cellSpacing);
                var cellBounds = new DocRect(cellX, cellY, cellWidth, cellHeight);
                cellLayouts.Add(new TableCellLayout(
                    localRowIndex,
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

            if (localRowIndex < 0)
            {
                continue;
            }

            var spanInPage = CountSpanInLayout(rowIndexMap, placement.RowIndex, placement.RowSpan);
            if (spanInPage <= 0)
            {
                continue;
            }

            var cellXOrigin = tableX + columnOffsets[placement.ColumnIndex];
            var cellWidthOrigin = SumColumnsWithSpacing(data.ColumnWidths, placement.ColumnIndex, placement.ColumnSpan, cellSpacing);
            var cellYOrigin = tableY + rowOffsets[localRowIndex];
            var cellHeightOrigin = SumSliceHeights(rowOffsets, slices, localRowIndex, spanInPage, cellSpacing);
            var cellBoundsOrigin = new DocRect(cellXOrigin, cellYOrigin, cellWidthOrigin, cellHeightOrigin);
            IReadOnlyList<TableCellLine> offsetLines = Array.Empty<TableCellLine>();
            if (placement.Lines.Count > 0)
            {
                if (placement.RowSpan == 1 && localRowIndex < slices.Count)
                {
                    var slice = slices[localRowIndex];
                    var rowHeight = data.RowHeights[Math.Clamp(placement.RowIndex, 0, data.RowHeights.Length - 1)];
                    var isPartial = slice.Offset > 0f || slice.Height < rowHeight - 0.01f;
                    offsetLines = isPartial
                        ? BuildCellLinesForSlice(placement, cellXOrigin, cellYOrigin, rowHeight, slice.Offset, slice.Height)
                        : BuildCellLines(placement, cellXOrigin, cellYOrigin, rowHeight);
                }
                else
                {
                    offsetLines = BuildCellLines(placement, cellXOrigin, cellYOrigin, cellHeightOrigin);
                }
            }

            cellLayouts.Add(new TableCellLayout(
                localRowIndex,
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
        if (rowCount > 0)
        {
            for (var i = 0; i < rowCount; i++)
            {
                tableHeight += slices[i].Height;
            }

            if (rowCount > 1)
            {
                tableHeight += cellSpacing * (rowCount - 1);
            }

            tableHeight += topSpacing + bottomSpacing;
        }

        var tableBounds = new DocRect(tableX, tableY, data.TableWidth, tableHeight);
        var rowHeightsSlice = new float[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            rowHeightsSlice[i] = slices[i].Height;
        }

        return new TableLayout(tableBounds, rowCount, columnCount, data.ColumnWidths, rowHeightsSlice, cellLayouts, tableProperties, cellSpacing, continuesFromPrevious, continuesOnNext);
    }

    private static TableLayout BuildTableLayoutWithHeader(
        TableBlock table,
        TableProperties tableProperties,
        TableLayoutData data,
        float tableX,
        float tableY,
        int headerRowCount,
        IReadOnlyList<TableRowSlice> bodySlices,
        LayoutSettings settings,
        bool includeTopSpacing,
        bool includeBottomSpacing,
        bool continuesFromPrevious,
        bool continuesOnNext)
    {
        if (headerRowCount <= 0 || bodySlices.Count == 0)
        {
            return BuildTableLayout(table, tableProperties, data, tableX, tableY, bodySlices, settings, includeTopSpacing, includeBottomSpacing, continuesFromPrevious, continuesOnNext);
        }

        var slices = new List<TableRowSlice>(headerRowCount + bodySlices.Count);
        for (var i = 0; i < headerRowCount && i < data.RowHeights.Length; i++)
        {
            slices.Add(new TableRowSlice(i, 0f, data.RowHeights[i]));
        }

        slices.AddRange(bodySlices);
        return BuildTableLayout(table, tableProperties, data, tableX, tableY, slices, settings, includeTopSpacing, includeBottomSpacing, continuesFromPrevious, continuesOnNext);
    }

    private static int[] BuildRowIndexMap(int rowCount, IReadOnlyList<TableRowSlice> slices)
    {
        var map = new int[rowCount];
        Array.Fill(map, -1);
        for (var i = 0; i < slices.Count; i++)
        {
            var rowIndex = slices[i].RowIndex;
            if (rowIndex >= 0 && rowIndex < map.Length)
            {
                map[rowIndex] = i;
            }
        }

        return map;
    }

    private static int CountSpanInLayout(int[] rowIndexMap, int rowIndex, int span)
    {
        if (span <= 0 || rowIndex < 0 || rowIndex >= rowIndexMap.Length)
        {
            return 0;
        }

        var localStart = rowIndexMap[rowIndex];
        if (localStart < 0)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < span && rowIndex + i < rowIndexMap.Length; i++)
        {
            var local = rowIndexMap[rowIndex + i];
            if (local != localStart + i)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static float SumSliceHeights(float[] rowOffsets, IReadOnlyList<TableRowSlice> slices, int start, int count, float spacing)
    {
        if (count <= 0 || start < 0 || start >= slices.Count)
        {
            return 0f;
        }

        var lastIndex = Math.Min(slices.Count - 1, start + count - 1);
        var startOffset = rowOffsets[start];
        var endOffset = rowOffsets[lastIndex] + slices[lastIndex].Height;
        var height = endOffset - startOffset;
        if (height <= 0f && count > 0)
        {
            for (var i = start; i < start + count && i < slices.Count; i++)
            {
                height += slices[i].Height;
                if (i < start + count - 1)
                {
                    height += spacing;
                }
            }
        }

        return height;
    }

    private static float ResolveCellVerticalOffset(TableCellPlacement placement, float alignmentHeight)
    {
        var availableHeight = MathF.Max(0f, alignmentHeight - placement.Padding.Vertical);
        var contentHeight = placement.ContentHeight;
        if (contentHeight >= availableHeight)
        {
            return 0f;
        }

        return (placement.Properties.VerticalAlignment ?? TableCellVerticalAlignment.Top) switch
        {
            TableCellVerticalAlignment.Center => (availableHeight - contentHeight) / 2f,
            TableCellVerticalAlignment.Bottom => availableHeight - contentHeight,
            _ => 0f
        };
    }

    private static IReadOnlyList<TableCellLine> BuildCellLines(
        TableCellPlacement placement,
        float cellX,
        float cellY,
        float alignmentHeight)
    {
        if (placement.Lines.Count == 0)
        {
            return Array.Empty<TableCellLine>();
        }

        var verticalOffset = ResolveCellVerticalOffset(placement, alignmentHeight);
        var offsetLines = new List<TableCellLine>(placement.Lines.Count);
        foreach (var line in placement.Lines)
        {
            offsetLines.Add(line with
            {
                X = cellX + placement.Padding.Left + line.X,
                Y = cellY + placement.Padding.Top + verticalOffset + line.Y
            });
        }

        return offsetLines;
    }

    private static IReadOnlyList<TableCellLine> BuildCellLinesForSlice(
        TableCellPlacement placement,
        float cellX,
        float cellY,
        float alignmentHeight,
        float sliceOffset,
        float sliceHeight)
    {
        if (placement.Lines.Count == 0)
        {
            return Array.Empty<TableCellLine>();
        }

        var verticalOffset = ResolveCellVerticalOffset(placement, alignmentHeight);
        var sliceEnd = sliceOffset + sliceHeight;
        var offsetLines = new List<TableCellLine>(placement.Lines.Count);
        foreach (var line in placement.Lines)
        {
            var lineHeight = GetLineBlockHeight(line);
            var lineTop = placement.Padding.Top + verticalOffset + line.Y;
            var lineBottom = lineTop + lineHeight;
            if (lineTop + 0.01f < sliceOffset || lineBottom - 0.01f > sliceEnd)
            {
                continue;
            }

            offsetLines.Add(line with
            {
                X = cellX + placement.Padding.Left + line.X,
                Y = cellY + (lineTop - sliceOffset)
            });
        }

        return offsetLines.Count == 0 ? Array.Empty<TableCellLine>() : offsetLines;
    }

    private static bool[] BuildRowSplitMap(TableBlock table, TableLayoutData data)
    {
        var rowCount = data.RowHeights.Length;
        var canSplit = new bool[rowCount];
        for (var i = 0; i < rowCount && i < table.Rows.Count; i++)
        {
            canSplit[i] = table.Rows[i].Properties.CantSplit != true;
        }

        if (rowCount == 0)
        {
            return canSplit;
        }

        var hasMerge = new bool[rowCount];
        foreach (var placement in data.Cells)
        {
            if (placement.IsMergeContinuation)
            {
                if (placement.RowIndex >= 0 && placement.RowIndex < rowCount)
                {
                    hasMerge[placement.RowIndex] = true;
                }

                continue;
            }

            if (placement.RowSpan > 1)
            {
                var endRow = Math.Min(rowCount, placement.RowIndex + placement.RowSpan);
                for (var row = Math.Max(0, placement.RowIndex); row < endRow; row++)
                {
                    hasMerge[row] = true;
                }
            }
        }

        for (var i = 0; i < rowCount; i++)
        {
            if (hasMerge[i])
            {
                canSplit[i] = false;
            }
        }

        return canSplit;
    }

    private static List<TableRowSlice> BuildRowSlices(
        TableBlock table,
        TableLayoutData data,
        int rowStart,
        float rowOffset,
        float availableHeight,
        bool[] rowSplittable,
        out int nextRow,
        out float nextOffset)
    {
        var slices = new List<TableRowSlice>();
        nextRow = rowStart;
        nextOffset = rowOffset;
        if (rowStart < 0 || rowStart >= data.RowHeights.Length || availableHeight <= 0f)
        {
            return slices;
        }

        var remainingHeight = availableHeight;
        var currentRow = rowStart;
        var currentOffset = MathF.Max(0f, rowOffset);
        while (currentRow < data.RowHeights.Length)
        {
            var rowHeight = data.RowHeights[currentRow];
            var rowRemaining = MathF.Max(0f, rowHeight - currentOffset);
            var spacing = slices.Count > 0 ? data.CellSpacing : 0f;
            if (remainingHeight <= spacing + 0.01f)
            {
                break;
            }

            var availableForRow = remainingHeight - spacing;
            if (availableForRow <= 0f)
            {
                break;
            }

            if (rowRemaining <= availableForRow)
            {
                slices.Add(new TableRowSlice(currentRow, currentOffset, rowRemaining));
                remainingHeight -= spacing + rowRemaining;
                currentRow++;
                currentOffset = 0f;
                continue;
            }

            var canSplit = currentRow < rowSplittable.Length && rowSplittable[currentRow];
            if (!canSplit)
            {
                if (slices.Count == 0)
                {
                    slices.Add(new TableRowSlice(currentRow, currentOffset, rowRemaining));
                    currentRow++;
                    currentOffset = 0f;
                }

                break;
            }

            var sliceHeight = ComputeRowSliceHeight(data, currentRow, currentOffset, availableForRow);
            if (sliceHeight <= 0f)
            {
                if (slices.Count > 0)
                {
                    break;
                }

                sliceHeight = MathF.Min(rowRemaining, MathF.Max(availableForRow, 1f));
                if (sliceHeight <= 0f)
                {
                    break;
                }
            }

            slices.Add(new TableRowSlice(currentRow, currentOffset, sliceHeight));
            remainingHeight -= spacing + sliceHeight;
            currentOffset += sliceHeight;
            if (currentOffset >= rowHeight - 0.01f)
            {
                currentRow++;
                currentOffset = 0f;
                continue;
            }

            break;
        }

        nextRow = currentRow;
        nextOffset = currentOffset;
        return slices;
    }

    private static float ComputeRowSliceHeight(
        TableLayoutData data,
        int rowIndex,
        float rowOffset,
        float maxHeight)
    {
        if (rowIndex < 0 || rowIndex >= data.RowHeights.Length || maxHeight <= 0f)
        {
            return 0f;
        }

        var rowHeight = data.RowHeights[rowIndex];
        var maxEnd = MathF.Min(rowHeight, rowOffset + maxHeight);
        if (maxEnd <= rowOffset)
        {
            return 0f;
        }

        var placements = data.PlacementsByRow[rowIndex];
        if (placements.Count == 0)
        {
            return maxEnd - rowOffset;
        }

        var sliceEnd = maxEnd;
        foreach (var placement in placements)
        {
            if (placement.IsMergeContinuation || placement.RowSpan > 1)
            {
                continue;
            }

            var candidate = ComputeCellSliceBoundary(placement, rowHeight, rowOffset, maxEnd);
            sliceEnd = MathF.Min(sliceEnd, candidate);
            if (sliceEnd <= rowOffset)
            {
                return 0f;
            }
        }

        return MathF.Max(0f, sliceEnd - rowOffset);
    }

    private static float ComputeCellSliceBoundary(
        TableCellPlacement placement,
        float rowHeight,
        float rowOffset,
        float maxEnd)
    {
        var verticalOffset = ResolveCellVerticalOffset(placement, rowHeight);
        var candidate = rowOffset;
        var insideLine = false;
        foreach (var line in placement.Lines)
        {
            var lineHeight = GetLineBlockHeight(line);
            var lineTop = placement.Padding.Top + verticalOffset + line.Y;
            var lineBottom = lineTop + lineHeight;
            if (lineBottom <= rowOffset + 0.01f)
            {
                continue;
            }

            if (lineBottom <= maxEnd + 0.01f)
            {
                candidate = MathF.Max(candidate, lineBottom);
            }

            if (lineTop < maxEnd && lineBottom > maxEnd)
            {
                insideLine = true;
            }
        }

        if (!insideLine)
        {
            candidate = MathF.Max(candidate, maxEnd);
        }

        return candidate;
    }

    private static int CountRowsToFit(float[] rowHeights, int rowStart, float maxHeight, float cellSpacing)
    {
        if (maxHeight <= 0f)
        {
            return 0;
        }

        var height = rowStart == 0 ? cellSpacing : 0f;
        var count = 0;
        for (var i = rowStart; i < rowHeights.Length; i++)
        {
            var rowHeight = rowHeights[i];
            var spacing = count > 0 ? cellSpacing : 0f;
            var candidate = height + spacing + rowHeight;
            if (i == rowHeights.Length - 1)
            {
                candidate += cellSpacing;
            }

            if (count > 0 && candidate > maxHeight)
            {
                break;
            }

            if (count == 0 && candidate > maxHeight)
            {
                return 0;
            }

            height = candidate;
            count++;
        }

        return count;
    }

    private static float ResolveParagraphLineHeight(
        TextStyle paragraphStyle,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, TextMetrics> metricsCache,
        ParagraphProperties properties,
        DocGridSettings? docGrid)
    {
        var (baseHeight, _) = ResolveParagraphLineMetrics(paragraphStyle, measurer, metricsCache);
        return ComputeLineHeight(baseHeight, properties, docGrid);
    }

    private static (float LineHeight, float Ascent) ResolveParagraphLineMetrics(
        TextStyle paragraphStyle,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, TextMetrics> metricsCache)
    {
        var metrics = GetMetrics(paragraphStyle, measurer, metricsCache);
        var lineHeight = MathF.Max(1f, metrics.Height);
        var ascent = MathF.Max(0f, metrics.Ascent);
        return (lineHeight, ascent);
    }

    private static float ResolveParagraphSpacing(
        float? spacingDip,
        int? spacingLines,
        bool? autoSpacing,
        float fallbackSpacing,
        float lineHeight)
    {
        if (autoSpacing == true)
        {
            return 0f;
        }

        if (spacingLines.HasValue)
        {
            var lineMultiplier = spacingLines.Value / 100f;
            return MathF.Max(0f, lineHeight * lineMultiplier);
        }

        if (spacingDip.HasValue)
        {
            return MathF.Max(0f, spacingDip.Value);
        }

        return MathF.Max(0f, fallbackSpacing);
    }

    private static string? ResolveParagraphStyleId(Document document, ParagraphBlock paragraph)
    {
        return paragraph.StyleId ?? document.Styles.DefaultParagraphStyleId;
    }

    private static bool IsSameParagraphStyle(Document document, ParagraphBlock current, ParagraphBlock other)
    {
        var currentId = ResolveParagraphStyleId(document, current);
        var otherId = ResolveParagraphStyleId(document, other);

        if (string.IsNullOrWhiteSpace(currentId) && string.IsNullOrWhiteSpace(otherId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(currentId) || string.IsNullOrWhiteSpace(otherId))
        {
            return false;
        }

        return string.Equals(currentId, otherId, StringComparison.OrdinalIgnoreCase);
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
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid,
        ref int paragraphIndex)
    {
        var availableWidth = MathF.Max(1f, columnWidth - padding.Horizontal);
        var lines = new List<TableCellLine>();
        var y = 0f;
        var listState = new ListNumberingState(document);
        var charGridSpacing = TextGridSnapping.GetCharacterSpacing(docGrid);

        for (var paragraphOffset = 0; paragraphOffset < cell.Paragraphs.Count; paragraphOffset++)
        {
            var paragraph = cell.Paragraphs[paragraphOffset];
            var previousParagraph = paragraphOffset > 0 ? cell.Paragraphs[paragraphOffset - 1] : null;
            var nextParagraph = paragraphOffset + 1 < cell.Paragraphs.Count ? cell.Paragraphs[paragraphOffset + 1] : null;
            var currentParagraphIndex = paragraphIndex;
            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var textDirection = properties.TextDirection ?? cell.Properties.TextDirection;
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var (paragraphLineHeight, paragraphAscent) = ResolveParagraphLineMetrics(paragraphStyle, measurer, spacingMetricsCache);
            var lineHeightAdjusted = ResolveParagraphLineHeight(
                paragraphStyle,
                measurer,
                spacingMetricsCache,
                properties,
                docGrid);
            var spacingBefore = ResolveParagraphSpacing(
                properties.SpacingBefore,
                properties.SpacingBeforeLines,
                properties.AutoSpacingBefore,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            var spacingAfter = ResolveParagraphSpacing(
                properties.SpacingAfter,
                properties.SpacingAfterLines,
                properties.AutoSpacingAfter,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            if (properties.ContextualSpacing == true)
            {
                if (previousParagraph is not null && IsSameParagraphStyle(document, previousParagraph, paragraph))
                {
                    spacingBefore = 0f;
                }

                if (nextParagraph is not null && IsSameParagraphStyle(document, paragraph, nextParagraph))
                {
                    spacingAfter = 0f;
                }
            }
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;
            var paragraphBorders = properties.Borders.HasAny ? properties.Borders.Clone() : null;
            var paragraphShading = properties.ShadingColor;

            if (DocTextDirectionHelpers.IsVertical(textDirection))
            {
                var (verticalText, verticalSpans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver);
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                if (string.IsNullOrEmpty(verticalText))
                {
                    y += spacingBefore;
                    var emptyAxisOrigin = y;
                    var lineAxisStart = emptyAxisOrigin + indentLeft + listIndent + firstLineIndent + prefixWidth;
                    var lineAxisLength = 1f;
                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, 0f, lineAxisLength, alignment);
                    var lineX = 0f;
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        emptyAscent,
                        textDirection,
                        docGrid,
                        lineAxisStart,
                        0f);
                    var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                    lines.Add(new TableCellLine(
                        currentParagraphIndex,
                        0,
                        0,
                        lineX,
                        alignedY,
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
                        Array.Empty<LayoutRuby>(),
                        textDirection,
                        emptyIsRtl,
                        paragraphBorders,
                        paragraphShading,
                        true,
                        true));
                    y = emptyAxisOrigin + emptyLineHeight + spacingAfter;
                    paragraphIndex++;
                    continue;
                }

                var availableLength = MathF.Max(1f, float.MaxValue / 4f);
                var verticalLines = BuildParagraphLines(
                    verticalText,
                    verticalSpans,
                    indentLeft,
                    indentRight,
                    firstLineIndent,
                    listIndent,
                    prefixWidth,
                    properties,
                    availableLength,
                    0f,
                    0f,
                    settings,
                    measurer,
                    paragraphLineHeight,
                    paragraphAscent,
                    docGrid,
                    null);

                y += spacingBefore;
                var verticalAxisOrigin = y;
                var currentX = 0f;
                var maxLineAxisEnd = verticalAxisOrigin;
                foreach (var line in verticalLines)
                {
                    var lineLayout = line.Layout;
                    var lineAxisStart = verticalAxisOrigin + indentLeft + listIndent + (line.IsFirstLine ? firstLineIndent : 0f) + prefixWidth;
                    var lineAxisLength = MathF.Max(1f, lineLayout.Width);
                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var isLastLine = IsLastParagraphLine(verticalText, line.Start + line.Length);
                    if (alignment == ParagraphAlignment.Justify && !isLastLine)
                    {
                        lineLayout = JustifyLineLayout(lineLayout, lineAxisLength, measurer, charGridSpacing);
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, lineLayout.Width, lineAxisLength, alignment);
                    var lineX = currentX;
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        lineLayout.Ascent,
                        textDirection,
                        docGrid,
                        lineAxisStart,
                        0f);
                    var lineSlice = new TextSlice(verticalText, line.Start, line.Length);
                    var isRtl = ResolveLineIsRtl(properties, lineSlice);
                    lines.Add(new TableCellLine(
                        currentParagraphIndex,
                        line.Start,
                        line.Length,
                        lineX,
                        alignedY,
                        lineLayout.Width,
                        lineSlice,
                        line.IsFirstLine ? prefix : null,
                        line.IsFirstLine ? prefixWidth : 0f,
                        lineLayout.LineHeight,
                        lineLayout.Ascent,
                        lineLayout.Runs,
                        lineLayout.Images,
                        lineLayout.Shapes,
                        lineLayout.Charts,
                        lineLayout.Equations,
                        lineLayout.Rubies,
                        textDirection,
                        isRtl,
                        paragraphBorders,
                        paragraphShading,
                        line.IsFirstLine,
                        isLastLine));
                    maxLineAxisEnd = MathF.Max(maxLineAxisEnd, alignedY + lineLayout.Width);
                    currentX = lineX + lineLayout.LineHeight;
                }

                y = maxLineAxisEnd + spacingAfter;
                paragraphIndex++;
                continue;
            }

            y += spacingBefore;

            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver);
            if (text.Length == 0)
            {
                var lineX = indentLeft + listIndent + firstLineIndent + prefixWidth;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                (lineX, y) = ApplyDocGridSnapping(
                    lineX,
                    y,
                    emptyAscent,
                    textDirection,
                    docGrid,
                    lineX,
                    0f);
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
                    Array.Empty<LayoutRuby>(),
                    textDirection,
                    emptyIsRtl,
                    paragraphBorders,
                    paragraphShading,
                    true,
                    true));
                y += emptyLineHeight;
                y += spacingAfter;
                paragraphIndex++;
                continue;
            }

            var baseWidth = MathF.Max(1f, availableWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
            var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
            var isFirstLine = true;
            foreach (var line in WrapParagraph(
                         text,
                         spans,
                         firstLineWidth,
                         otherLineWidth,
                         properties,
                         settings,
                         measurer,
                         charGridSpacing,
                         firstLineTabOffset,
                         otherLineTabOffset))
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
                    paragraphLineHeight,
                    paragraphAscent,
                    charGridSpacing,
                    lineBaseX);
                lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer, charGridSpacing);
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
                    lineLayout = JustifyLineLayout(lineLayout, alignWidth, measurer, charGridSpacing);
                }

                var alignedX = ApplyAlignment(lineBaseX, lineLayout.Width, alignWidth, alignment);
                (alignedX, y) = ApplyDocGridSnapping(
                    alignedX,
                    y,
                    lineLayout.Ascent,
                    textDirection,
                    docGrid,
                    lineBaseX,
                    0f);
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
                    lineLayout.Rubies,
                    textDirection,
                    isRtl,
                    paragraphBorders,
                    paragraphShading,
                    isFirstLine,
                    isLastLine));
                y += lineLayout.LineHeight;
                isFirstLine = false;
            }

            y += spacingAfter;
            paragraphIndex++;
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
        ITextMeasurer measurer,
        float charGridSpacing,
        float firstLineTabOffset,
        float otherLineTabOffset)
    {
        return ParagraphLineBreaker.BreakParagraph(
            text,
            spans,
            firstLineWidth,
            otherLineWidth,
            measurer,
            charGridSpacing,
            (start, length) => MeasureInlineSpans(spans, start, length, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, firstLineTabOffset),
            (start, length) => MeasureInlineSpans(spans, start, length, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, otherLineTabOffset));
    }

    private static int FindLineLength(string text, int start, float maxWidth, Func<int, int, float> measureWidth)
    {
        var length = 0;
        var lastBreak = -1;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ')
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

    private static List<ParagraphLine> BuildParagraphLinesWithDropCap(
        string text,
        IReadOnlyList<InlineSpan> spans,
        DropCapInfo dropCap,
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
        DocGridSettings? docGrid)
    {
        var lines = new List<ParagraphLine>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var charGridSpacing = TextGridSnapping.GetCharacterSpacing(docGrid);
        var (lineHeightAdjusted, lineAscentAdjusted) = ApplyLineSpacing(lineHeight, ascent, properties, docGrid);
        var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
        var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
        var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
        var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
        var dropCapOffset = dropCap.Kind == DropCapKind.Drop
            ? MathF.Max(0f, dropCap.Width + dropCap.Distance)
            : 0f;
        var firstLineWidthForBreak = dropCap.Kind == DropCapKind.Drop
            ? MathF.Max(1f, firstLineWidth - dropCap.Distance)
            : firstLineWidth;
        var dropCapLineWidth = MathF.Max(1f, baseWidth - dropCapOffset);

        var dropCapBreaks = new List<ParagraphLineBreak>(dropCap.Lines);
        foreach (var line in ParagraphLineBreaker.BreakParagraph(
                     text,
                     spans,
                     firstLineWidthForBreak,
                     dropCapLineWidth,
                     measurer,
                     charGridSpacing,
                     (start, length) => MeasureInlineSpans(spans, start, length, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, firstLineTabOffset),
                     (start, length) => MeasureInlineSpans(spans, start, length, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, otherLineTabOffset)))
        {
            dropCapBreaks.Add(line);
            if (dropCapBreaks.Count >= dropCap.Lines)
            {
                break;
            }
        }

        var isFirstLine = true;
        for (var i = 0; i < dropCapBreaks.Count; i++)
        {
            var line = dropCapBreaks[i];
            var lineLayout = BuildLineLayout(
                spans,
                line.Start,
                line.Length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                lineHeight,
                ascent,
                charGridSpacing,
                i == 0 ? firstLineTabOffset : otherLineTabOffset);
            lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
            if (line.HasHyphen && line.HyphenStyle is not null)
            {
                lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer, charGridSpacing);
            }

            if (i == 0)
            {
                lineLayout = lineLayout with { LineHeight = lineHeightAdjusted, Ascent = lineAscentAdjusted };
            }

            if (i == 0 && dropCap.Distance > 0f)
            {
                lineLayout = ApplyDropCapGap(lineLayout, dropCap.Distance);
            }

            lines.Add(new ParagraphLine(line.Start, line.Length, new TextSlice(text, line.Start, line.Length), lineLayout, isFirstLine));
            isFirstLine = false;
        }

        if (dropCapBreaks.Count == 0)
        {
            return lines;
        }

        var lastBreak = dropCapBreaks[^1];
        var remainingStart = lastBreak.Start + lastBreak.Length;
        while (remainingStart < text.Length && text[remainingStart] == ' ')
        {
            remainingStart++;
        }

        if (remainingStart >= text.Length)
        {
            return lines;
        }

        var remainingText = text.Substring(remainingStart);
        var remainingSpans = SliceInlineSpans(spans, remainingStart, remainingText.Length);
        if (remainingText.Length == 0 || remainingSpans.Count == 0)
        {
            return lines;
        }

        foreach (var line in WrapParagraph(
                     remainingText,
                     remainingSpans,
                     baseWidth,
                     baseWidth,
                     properties,
                     settings,
                     measurer,
                     charGridSpacing,
                     otherLineTabOffset,
                     otherLineTabOffset))
        {
            var lineLayout = BuildLineLayout(
                remainingSpans,
                line.Start,
                line.Length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                lineHeight,
                ascent,
                charGridSpacing,
                otherLineTabOffset);
            lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
            if (line.HasHyphen && line.HyphenStyle is not null)
            {
                lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer, charGridSpacing);
            }

            var start = remainingStart + line.Start;
            lines.Add(new ParagraphLine(start, line.Length, new TextSlice(text, start, line.Length), lineLayout, false));
        }

        return lines;
    }

    private static List<InlineSpan> SliceInlineSpans(IReadOnlyList<InlineSpan> spans, int start, int length)
    {
        var result = new List<InlineSpan>();
        if (length <= 0)
        {
            return result;
        }

        var end = start + length;
        foreach (var span in spans)
        {
            var spanStart = span.Start;
            var spanEnd = span.Start + span.Length;
            if (spanEnd <= start || spanStart >= end)
            {
                continue;
            }

            var sliceStart = Math.Max(spanStart, start);
            var sliceEnd = Math.Min(spanEnd, end);
            var sliceLength = sliceEnd - sliceStart;
            if (sliceLength <= 0)
            {
                continue;
            }

            var text = span.Text;
            var relativeStart = sliceStart - spanStart;
            if (relativeStart > 0 || sliceLength < span.Length)
            {
                if (relativeStart < text.Length)
                {
                    var takeLength = Math.Min(sliceLength, text.Length - relativeStart);
                    text = text.Substring(relativeStart, takeLength);
                }
                else
                {
                    text = string.Empty;
                }
            }

            result.Add(span with { Start = sliceStart - start, Length = sliceLength, Text = text });
        }

        return result;
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
        DocGridSettings? docGrid,
        WrapResolver? wrapResolver)
    {
        var lines = new List<ParagraphLine>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var charGridSpacing = TextGridSnapping.GetCharacterSpacing(docGrid);
        var lineTop = startY;
        if (wrapResolver is null)
        {
            var baseLeft = columnX + indentLeft + listIndent + prefixWidth;
            var baseRight = columnX + contentWidth - indentRight;
            var firstLineWidth = MathF.Max(1f, baseRight - (columnX + indentLeft + listIndent + firstLineIndent + prefixWidth));
            var otherLineWidth = MathF.Max(1f, baseRight - baseLeft);
            var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
            var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
            var isFirstLineLocal = true;

            foreach (var line in WrapParagraph(
                         text,
                         spans,
                         firstLineWidth,
                         otherLineWidth,
                         properties,
                         settings,
                         measurer,
                         charGridSpacing,
                         firstLineTabOffset,
                         otherLineTabOffset))
            {
                var tabStopOffset = isFirstLineLocal ? firstLineTabOffset : otherLineTabOffset;
                var lineLayout = BuildLineLayout(
                    spans,
                    line.Start,
                    line.Length,
                    properties.TabStops,
                    settings.DefaultTabWidth,
                    measurer,
                    lineHeight,
                    ascent,
                    charGridSpacing,
                    tabStopOffset);
                lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer, charGridSpacing);
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
            var tabStopOffset = lineIndent + prefixWidth;
            var length = FindLineLength(text, start, maxWidth,
                (offset, runLength) => MeasureInlineSpans(spans, offset, runLength, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, tabStopOffset));
            var textSlice = new TextSlice(text, start, length);

            var lineLayout = BuildLineLayout(
                spans,
                start,
                length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                lineHeight,
                ascent,
                charGridSpacing,
                tabStopOffset);
            lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
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
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
        float contentWidth,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid)
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
            var isVertical = DocTextDirectionHelpers.IsVertical(properties.TextDirection);
            var charGridSpacing = TextGridSnapping.GetCharacterSpacing(docGrid);
            var (paragraphLineHeight, paragraphAscent) = ResolveParagraphLineMetrics(paragraphStyle, measurer, spacingMetricsCache);
            var lineHeightAdjusted = ResolveParagraphLineHeight(
                paragraphStyle,
                measurer,
                spacingMetricsCache,
                properties,
                docGrid);
            var spacingBefore = ResolveParagraphSpacing(
                properties.SpacingBefore,
                properties.SpacingBeforeLines,
                properties.AutoSpacingBefore,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            if (properties.ContextualSpacing == true && blocks[blockIndex] is ParagraphBlock currentParagraph
                && IsSameParagraphStyle(document, currentParagraph, nextParagraph))
            {
                spacingBefore = 0f;
            }
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;
            var listIndent = nextParagraph.ListInfo is null ? 0f : settings.ListIndent * (nextParagraph.ListInfo.Level + 1);
            var prefixWidth = 0f;

            var (text, spans) = BuildInlineSpans(nextParagraph, paragraphStyle, styleResolver);
            if (string.IsNullOrEmpty(text))
            {
                var (emptyLineHeight, _) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                return spacingBefore + emptyLineHeight;
            }

            var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
            var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
            var firstLine = WrapParagraph(
                    text,
                    spans,
                    firstLineWidth,
                    otherLineWidth,
                    properties,
                    settings,
                    measurer,
                    charGridSpacing,
                    firstLineTabOffset,
                    otherLineTabOffset)
                .FirstOrDefault();
            if (firstLine.Length == 0)
            {
                var (emptyLineHeight, _) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                return spacingBefore + emptyLineHeight;
            }

            var lineLayout = BuildLineLayout(
                spans,
                firstLine.Start,
                firstLine.Length,
                properties.TabStops,
                settings.DefaultTabWidth,
                measurer,
                paragraphLineHeight,
                paragraphAscent,
                charGridSpacing,
                firstLineTabOffset);
            lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
            var lineExtent = isVertical ? lineLayout.Width : lineLayout.LineHeight;
            return spacingBefore + lineExtent;
        }

        if (nextBlock is TableBlock table)
        {
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var paragraphIndex = 0;
            var data = ComputeTableLayoutData(table, document, resolvedTableProperties, table.Properties, tableStyle, tableLook, contentWidth, settings, measurer, defaultStyle, styleResolver, spacingMetricsCache, lineHeight, ascent, docGrid, ref paragraphIndex);
            if (data.RowHeights.Length == 0)
            {
                return lineHeight;
            }

            var estimated = data.RowHeights[0];
            if (data.CellSpacing > 0f)
            {
                estimated += data.CellSpacing;
                if (data.RowHeights.Length == 1)
                {
                    estimated += data.CellSpacing;
                }
            }

            return estimated;
        }

        return 0f;
    }

    private static (string Text, List<InlineSpan> Spans) BuildInlineSpans(
        ParagraphBlock paragraph,
        TextStyle paragraphStyle,
        DocumentStyleResolver styleResolver,
        string? pageNumberText = null,
        string? totalPagesText = null)
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
                case RubyInline rubyInline:
                {
                    var baseText = rubyInline.BaseText;
                    if (string.IsNullOrEmpty(baseText))
                    {
                        break;
                    }

                    var baseStyle = styleResolver.ResolveRunStyle(rubyInline.BaseStyleId, rubyInline.BaseStyle, paragraphStyle);
                    var rubyStyle = styleResolver.ResolveRunStyle(rubyInline.RubyStyleId, rubyInline.RubyStyle, baseStyle);
                    var rubyScale = rubyInline.RubyScale > 0f ? rubyInline.RubyScale : 0.5f;
                    if (rubyScale != 1f)
                    {
                        rubyStyle.FontSize = MathF.Max(1f, rubyStyle.FontSize * rubyScale);
                        if (rubyStyle.FontSizeComplexScript.HasValue)
                        {
                            rubyStyle.FontSizeComplexScript = MathF.Max(1f, rubyStyle.FontSizeComplexScript.Value * rubyScale);
                        }
                    }

                    var (effectiveBaseStyle, baselineOffset) = PrepareRunStyle(baseStyle);
                    var (effectiveRubyStyle, _) = PrepareRunStyle(rubyStyle);
                    var start = builder.Length;
                    builder.Append(baseText);
                    spansList.Add(new InlineSpan(start, baseText.Length, baseText, effectiveBaseStyle, null, null, null, null, rubyInline, effectiveRubyStyle, baselineOffset));
                    MarkContent();
                    break;
                }
                case ImageInline imageInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, imageInline, null, null, null, null, null, 0f));
                    MarkContent();
                    break;
                }
                case ShapeInline shapeInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, null, shapeInline, null, null, null, null, 0f));
                    MarkContent();
                    break;
                }
                case ChartInline chartInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, paragraphStyle, null, null, chartInline, null, null, null, 0f));
                    MarkContent();
                    break;
                }
                case EquationInline equationInline:
                {
                    var start = builder.Length;
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    var equationStyle = styleResolver.ResolveRunStyle(equationInline.StyleId, equationInline.Style, paragraphStyle);
                    spansList.Add(new InlineSpan(start, 1, string.Empty, equationStyle, null, null, null, equationInline, null, null, 0f));
                    MarkContent();
                    break;
                }
                case PageNumberInline pageNumberInline:
                {
                    var text = pageNumberText ?? string.Empty;
                    if (text.Length == 0)
                    {
                        break;
                    }

                    AppendContent(text, pageNumberInline.Style?.Clone() ?? paragraphStyle);
                    break;
                }
                case TotalPagesInline totalPagesInline:
                {
                    var text = totalPagesText ?? string.Empty;
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

        if (MathF.Abs(style.BaselineOffset) > 0.01f)
        {
            baselineOffset += style.BaselineOffset;
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
            if (style.Caps)
            {
                var upperText = text.ToUpperInvariant();
                var start = builder.Length;
                builder.Append(upperText);
                spans.Add(new InlineSpan(start, upperText.Length, upperText, style, null, null, null, null, null, null, baselineOffset));
                return;
            }

            var defaultStart = builder.Length;
            builder.Append(text);
            spans.Add(new InlineSpan(defaultStart, text.Length, text, style, null, null, null, null, null, null, baselineOffset));
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
            spans.Add(new InlineSpan(segmentStart, segmentText.Length, segmentText, segmentStyle, null, null, null, null, null, null, baselineOffset));
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

    private static bool TryPrepareDropCap(
        string text,
        IReadOnlyList<InlineSpan> spans,
        ParagraphProperties properties,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid,
        ITextMeasurer measurer,
        out DropCapInfo dropCap,
        out List<InlineSpan> dropCapSpans)
    {
        dropCap = default;
        dropCapSpans = new List<InlineSpan>();
        var settings = properties.DropCap;
        if (settings is null || settings.Lines <= 1)
        {
            return false;
        }

        if (string.IsNullOrEmpty(text) || spans.Count == 0)
        {
            return false;
        }

        var firstChar = text[0];
        if (char.IsWhiteSpace(firstChar) || firstChar == DocumentConstants.ObjectReplacementChar)
        {
            return false;
        }

        var clusterLength = TextCluster.GetNextClusterLength(text.AsSpan(), 0);
        if (clusterLength <= 0 || clusterLength > text.Length)
        {
            return false;
        }

        var dropText = text.Substring(0, clusterLength);
        var spanIndex = -1;
        InlineSpan? dropSpan = null;
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.Start <= 0 && span.Start + span.Length > 0 && !string.IsNullOrEmpty(span.Text))
            {
                spanIndex = i;
                dropSpan = span;
                break;
            }
        }

        if (dropSpan is null)
        {
            return false;
        }

        var baseStyle = dropSpan.Style;
        var dropStyle = baseStyle.Clone();
        var (lineHeightAdjusted, lineAscentAdjusted) = ApplyLineSpacing(lineHeight, ascent, properties, docGrid);
        var targetHeight = MathF.Max(1f, lineHeightAdjusted * Math.Max(1, settings.Lines));
        var baseMetrics = measurer.MeasureText(dropText, baseStyle);
        if (baseMetrics.Height > 0.5f)
        {
            var scale = targetHeight / baseMetrics.Height;
            scale = Math.Clamp(scale, 0.5f, 10f);
            dropStyle.FontSize = MathF.Max(1f, baseStyle.FontSize * scale);
            if (dropStyle.FontSizeComplexScript.HasValue)
            {
                dropStyle.FontSizeComplexScript = MathF.Max(1f, dropStyle.FontSizeComplexScript.Value * scale);
            }
        }

        var dropMetrics = measurer.MeasureText(dropText, dropStyle);
        var baselineOffset = dropMetrics.Ascent > 0f ? lineAscentAdjusted - dropMetrics.Ascent : 0f;
        var width = TextGridSnapping.MeasureText(dropText, dropStyle, measurer, TextGridSnapping.GetCharacterSpacing(docGrid));
        dropCap = new DropCapInfo(clusterLength, settings.Lines, width, settings.Distance ?? 0f, settings.Kind);

        dropCapSpans.Capacity = spans.Count + 1;
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (i == spanIndex)
            {
                dropCapSpans.Add(span with { Length = clusterLength, Text = dropText, Style = dropStyle, BaselineOffset = baselineOffset });
                var remainingLength = span.Length - clusterLength;
                if (remainingLength > 0)
                {
                    var remainingText = span.Text.Length > clusterLength
                        ? span.Text.Substring(clusterLength)
                        : string.Empty;
                    dropCapSpans.Add(span with
                    {
                        Start = span.Start + clusterLength,
                        Length = remainingLength,
                        Text = remainingText
                    });
                }

                continue;
            }

            dropCapSpans.Add(span);
        }

        return true;
    }

    private static LineLayout ApplyDropCapGap(LineLayout layout, float gap)
    {
        if (gap <= 0f || layout.Runs.Count == 0)
        {
            return layout;
        }

        var runs = new List<LayoutRun>(layout.Runs.Count);
        var first = layout.Runs[0];
        runs.Add(first with { Width = first.Width + gap });
        for (var i = 1; i < layout.Runs.Count; i++)
        {
            var run = layout.Runs[i];
            runs.Add(run with { X = run.X + gap });
        }

        return layout with { Runs = runs, Width = layout.Width + gap };
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

    private static InlineScanResult ScanParagraphInlines(ParagraphBlock paragraph, string? pageNumberText = null, string? totalPagesText = null)
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
                case RubyInline rubyInline:
                    AppendLength(rubyInline.BaseText);
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
                    var text = pageNumberText ?? string.Empty;
                    AppendLength(text);
                    break;
                }
                case TotalPagesInline:
                {
                    var text = totalPagesText ?? string.Empty;
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

    private static HeaderFooterLayoutResult LayoutHeaderFooterBlocks(
        IReadOnlyList<Block> blocks,
        Document document,
        LayoutSettings settings,
        ITextMeasurer measurer,
        TextStyle style,
        DocumentStyleResolver styleResolver,
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
        float contentWidth,
        float lineHeight,
        float ascent,
        DocGridSettings? docGrid,
        string? pageNumberText,
        string? totalPagesText,
        bool includeTables,
        bool handleFrameParagraphs)
    {
        if (blocks.Count == 0)
        {
            return new HeaderFooterLayoutResult(
                new List<HeaderFooterLine>(),
                new List<TableLayout>(),
                0f,
                new Dictionary<int, LineRange>(),
                new List<HeaderFooterFrameLayout>());
        }

        var lines = new List<HeaderFooterLine>();
        var tables = new List<TableLayout>();
        var frameLayouts = new List<HeaderFooterFrameLayout>();
        var paragraphLineRanges = new Dictionary<int, LineRange>();
        var charGridSpacing = TextGridSnapping.GetCharacterSpacing(docGrid);
        var y = 0f;
        var listState = new ListNumberingState(document);
        var paragraphIndex = 0;

        TableLayout LayoutHeaderFooterTable(TableBlock table, float tableY, ref int index)
        {
            var tableStyle = styleResolver.ResolveTableStyle(table);
            var resolvedTableProperties = MergeTableProperties(tableStyle?.TableProperties, table.Properties);
            var tableLook = ResolveTableLook(table.Properties.Look ?? tableStyle?.TableProperties.Look);
            var data = ComputeTableLayoutData(
                table,
                document,
                resolvedTableProperties,
                table.Properties,
                tableStyle,
                tableLook,
                contentWidth,
                settings,
                measurer,
                style,
                styleResolver,
                spacingMetricsCache,
                lineHeight,
                ascent,
                docGrid,
                ref index);

            var slices = new List<TableRowSlice>(data.RowHeights.Length);
            for (var i = 0; i < data.RowHeights.Length; i++)
            {
                slices.Add(new TableRowSlice(i, 0f, data.RowHeights[i]));
            }

            var tableX = ResolveTableX(0f, contentWidth, resolvedTableProperties, data.TableWidth);
            return BuildTableLayout(
                table,
                resolvedTableProperties,
                data,
                tableX,
                tableY,
                slices,
                settings,
                includeTopSpacing: true,
                includeBottomSpacing: true,
                continuesFromPrevious: false,
                continuesOnNext: false);
        }

        bool TryHandleFrameParagraph(
            ParagraphBlock paragraph,
            ParagraphProperties properties,
            TextStyle paragraphStyle,
            float spacingBefore,
            float indentLeft,
            float indentRight,
            float listIndent,
            float prefixWidth,
            float paragraphLineHeight,
            float paragraphAscent,
            int paragraphIndex,
            float currentY)
        {
            if (!handleFrameParagraphs)
            {
                return false;
            }

            var frame = properties.Frame;
            if (frame is null || !frame.HasValues)
            {
                return false;
            }

            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver, pageNumberText, totalPagesText);
            var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineIndent = properties.FirstLineIndent ?? 0f;
            var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
            var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
            var measuredWidth = text.Length == 0
                ? baseWidth
                : MeasureInlineSpans(spans, 0, text.Length, properties.TabStops, settings.DefaultTabWidth, measurer, charGridSpacing, firstLineTabOffset);
            var isDropCapFrame = properties.DropCap?.HasValues == true;
            var frameWidth = frame.Width ?? MathF.Max(isDropCapFrame ? 1f : 120f, measuredWidth);
            if (frameWidth <= 0f)
            {
                frameWidth = MathF.Max(isDropCapFrame ? 1f : 120f, baseWidth);
            }

            var (lineHeightAdjusted, lineAscentAdjusted) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
            var lineCount = 1;
            if (text.Length > 0)
            {
                lineCount = WrapParagraph(
                        text,
                        spans,
                        frameWidth,
                        frameWidth,
                        properties,
                        settings,
                        measurer,
                        charGridSpacing,
                        firstLineTabOffset,
                        otherLineTabOffset)
                    .Count();
                lineCount = Math.Max(1, lineCount);
            }

            var frameHeight = frame.Height ?? MathF.Max(lineHeightAdjusted, lineCount * lineHeightAdjusted);
            if (frameHeight <= 0f)
            {
                frameHeight = MathF.Max(lineHeightAdjusted, paragraphLineHeight);
            }

            var shapeParagraph = BuildFrameParagraph(paragraph, properties);
            var shape = BuildFrameShape(shapeParagraph, frameWidth, frameHeight);
            var floating = new FloatingObject(shape);
            ApplyFrameAnchor(frame, floating.Anchor);

            var anchorLine = new HeaderFooterLine(
                paragraphIndex,
                0,
                0,
                indentLeft + listIndent + prefixWidth,
                currentY + spacingBefore,
                0f,
                TextSlice.Empty,
                null,
                0f,
                lineHeightAdjusted,
                lineAscentAdjusted,
                Array.Empty<LayoutRun>(),
                Array.Empty<LayoutImage>(),
                Array.Empty<LayoutShape>(),
                Array.Empty<LayoutChart>(),
                Array.Empty<LayoutEquation>(),
                Array.Empty<LayoutRuby>(),
                properties.TextDirection,
                false);

            frameLayouts.Add(new HeaderFooterFrameLayout(floating, anchorLine));
            return true;
        }

        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            if (includeTables && block is TableBlock table)
            {
                var tableLayout = LayoutHeaderFooterTable(table, y, ref paragraphIndex);
                tables.Add(tableLayout);
                y = tableLayout.Bounds.Bottom + settings.BlockSpacing;
                continue;
            }

            if (block is not ParagraphBlock paragraph)
            {
                continue;
            }

            var previousParagraph = blockIndex > 0 && blocks[blockIndex - 1] is ParagraphBlock previous
                ? previous
                : null;
            var nextParagraph = blockIndex + 1 < blocks.Count && blocks[blockIndex + 1] is ParagraphBlock next
                ? next
                : null;

            var paragraphLineStart = lines.Count;
            var properties = styleResolver.ResolveParagraphProperties(paragraph);
            var paragraphStyle = styleResolver.ResolveParagraphTextStyle(paragraph, style);
            var (paragraphLineHeight, paragraphAscent) = ResolveParagraphLineMetrics(paragraphStyle, measurer, spacingMetricsCache);
            var lineHeightAdjusted = ResolveParagraphLineHeight(
                paragraphStyle,
                measurer,
                spacingMetricsCache,
                properties,
                docGrid);
            var spacingBefore = ResolveParagraphSpacing(
                properties.SpacingBefore,
                properties.SpacingBeforeLines,
                properties.AutoSpacingBefore,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            var spacingAfter = ResolveParagraphSpacing(
                properties.SpacingAfter,
                properties.SpacingAfterLines,
                properties.AutoSpacingAfter,
                settings.ParagraphSpacing,
                lineHeightAdjusted);
            if (properties.ContextualSpacing == true)
            {
                if (previousParagraph is not null && IsSameParagraphStyle(document, previousParagraph, paragraph))
                {
                    spacingBefore = 0f;
                }

                if (nextParagraph is not null && IsSameParagraphStyle(document, paragraph, nextParagraph))
                {
                    spacingAfter = 0f;
                }
            }
            var indentLeft = properties.IndentLeft ?? 0f;
            var indentRight = properties.IndentRight ?? 0f;
            var firstLineIndent = properties.FirstLineIndent ?? 0f;

            var listMarker = listState.GetMarker(paragraph.ListInfo, settings, measurer, paragraphStyle);
            var prefix = listMarker?.Prefix;
            var listIndent = listMarker?.Indent ?? 0f;
            var prefixWidth = listMarker?.PrefixWidth ?? 0f;

            if (TryHandleFrameParagraph(
                    paragraph,
                    properties,
                    paragraphStyle,
                    spacingBefore,
                    indentLeft,
                    indentRight,
                    listIndent,
                    prefixWidth,
                    paragraphLineHeight,
                    paragraphAscent,
                    paragraphIndex,
                    y))
            {
                paragraphIndex++;
                continue;
            }

            if (DocTextDirectionHelpers.IsVertical(properties.TextDirection))
            {
                var (verticalText, verticalSpans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver, pageNumberText, totalPagesText);
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                if (string.IsNullOrEmpty(verticalText))
                {
                    y += spacingBefore;
                    var emptyAxisOrigin = y;
                    var lineAxisStart = emptyAxisOrigin + indentLeft + listIndent + firstLineIndent + prefixWidth;
                    var lineAxisLength = 1f;
                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, 0f, lineAxisLength, alignment);
                    var lineX = 0f;
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        emptyAscent,
                        properties.TextDirection,
                        docGrid,
                        lineAxisStart,
                        0f);
                    var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                    lines.Add(new HeaderFooterLine(
                        paragraphIndex,
                        0,
                        0,
                        lineX,
                        alignedY,
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
                        Array.Empty<LayoutRuby>(),
                        properties.TextDirection,
                        emptyIsRtl));
                    y = emptyAxisOrigin + emptyLineHeight + spacingAfter;
                    paragraphLineRanges[paragraphIndex] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                    paragraphIndex++;
                    continue;
                }

                var availableLength = MathF.Max(1f, float.MaxValue / 4f);
                var verticalLines = BuildParagraphLines(
                    verticalText,
                    verticalSpans,
                    indentLeft,
                    indentRight,
                    firstLineIndent,
                    listIndent,
                    prefixWidth,
                    properties,
                    availableLength,
                    0f,
                    0f,
                    settings,
                    measurer,
                    paragraphLineHeight,
                    paragraphAscent,
                    docGrid,
                    null);

                y += spacingBefore;
                var verticalAxisOrigin = y;
                var currentX = 0f;
                var maxLineAxisEnd = verticalAxisOrigin;
                foreach (var line in verticalLines)
                {
                    var lineLayout = line.Layout;
                    var lineAxisStart = verticalAxisOrigin + indentLeft + listIndent + (line.IsFirstLine ? firstLineIndent : 0f) + prefixWidth;
                    var lineAxisLength = MathF.Max(1f, lineLayout.Width);
                    var alignment = properties.Alignment;
                    if (!alignment.HasValue && properties.Bidi == true)
                    {
                        alignment = ParagraphAlignment.Right;
                    }

                    var isLastLine = IsLastParagraphLine(verticalText, line.Start + line.Length);
                    if (alignment == ParagraphAlignment.Justify && !isLastLine)
                    {
                        lineLayout = JustifyLineLayout(lineLayout, lineAxisLength, measurer, charGridSpacing);
                    }

                    var alignedY = ApplyAlignment(lineAxisStart, lineLayout.Width, lineAxisLength, alignment);
                    var lineX = currentX;
                    (lineX, alignedY) = ApplyDocGridSnapping(
                        lineX,
                        alignedY,
                        lineLayout.Ascent,
                        properties.TextDirection,
                        docGrid,
                        lineAxisStart,
                        0f);
                    var lineSlice = new TextSlice(verticalText, line.Start, line.Length);
                    var isRtl = ResolveLineIsRtl(properties, lineSlice);
                    lines.Add(new HeaderFooterLine(
                        paragraphIndex,
                        line.Start,
                        line.Length,
                        lineX,
                        alignedY,
                        lineLayout.Width,
                        lineSlice,
                        line.IsFirstLine ? prefix : null,
                        line.IsFirstLine ? prefixWidth : 0f,
                        lineLayout.LineHeight,
                        lineLayout.Ascent,
                        lineLayout.Runs,
                        lineLayout.Images,
                        lineLayout.Shapes,
                        lineLayout.Charts,
                        lineLayout.Equations,
                        lineLayout.Rubies,
                        properties.TextDirection,
                        isRtl));
                    maxLineAxisEnd = MathF.Max(maxLineAxisEnd, alignedY + lineLayout.Width);
                    currentX = lineX + lineLayout.LineHeight;
                }

                y = maxLineAxisEnd + spacingAfter;
                paragraphLineRanges[paragraphIndex] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                paragraphIndex++;
                continue;
            }

            y += spacingBefore;

            var (text, spans) = BuildInlineSpans(paragraph, paragraphStyle, styleResolver, pageNumberText, totalPagesText);
            if (text.Length == 0)
            {
                var lineX = indentLeft + listIndent + firstLineIndent + prefixWidth;
                var (emptyLineHeight, emptyAscent) = ApplyLineSpacing(paragraphLineHeight, paragraphAscent, properties, docGrid);
                var emptyIsRtl = ResolveLineIsRtl(properties, TextSlice.Empty);
                (lineX, y) = ApplyDocGridSnapping(
                    lineX,
                    y,
                    emptyAscent,
                    properties.TextDirection,
                    docGrid,
                    lineX,
                    0f);
                lines.Add(new HeaderFooterLine(
                    paragraphIndex,
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
                    Array.Empty<LayoutRuby>(),
                    properties.TextDirection,
                    emptyIsRtl));
                y += emptyLineHeight;
                y += spacingAfter;
                paragraphLineRanges[paragraphIndex] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
                paragraphIndex++;
                continue;
            }

            var baseWidth = MathF.Max(1f, contentWidth - indentLeft - indentRight - listIndent - prefixWidth);
            var firstLineWidth = MathF.Max(1f, baseWidth - firstLineIndent);
            var otherLineWidth = MathF.Max(1f, baseWidth);
            var firstLineTabOffset = indentLeft + listIndent + firstLineIndent + prefixWidth;
            var otherLineTabOffset = indentLeft + listIndent + prefixWidth;
            var isFirstLine = true;
            foreach (var line in WrapParagraph(
                         text,
                         spans,
                         firstLineWidth,
                         otherLineWidth,
                         properties,
                         settings,
                         measurer,
                         charGridSpacing,
                         firstLineTabOffset,
                         otherLineTabOffset))
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
                    paragraphLineHeight,
                    paragraphAscent,
                    charGridSpacing,
                    lineBaseX);
                lineLayout = ApplyLineSpacing(lineLayout, properties, docGrid);
                if (line.HasHyphen && line.HyphenStyle is not null)
                {
                    lineLayout = AppendHyphenRun(lineLayout, line.HyphenStyle, line.HyphenBaselineOffset, measurer, charGridSpacing);
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
                    lineLayout = JustifyLineLayout(lineLayout, alignWidth, measurer, charGridSpacing);
                }

                var alignedX = ApplyAlignment(lineBaseX, lineLayout.Width, alignWidth, alignment);
                (alignedX, y) = ApplyDocGridSnapping(
                    alignedX,
                    y,
                    lineLayout.Ascent,
                    properties.TextDirection,
                    docGrid,
                    lineBaseX,
                    0f);
                var lineSlice = new TextSlice(text, line.Start, line.Length);
                var isRtl = ResolveLineIsRtl(properties, lineSlice);
                lines.Add(new HeaderFooterLine(
                    paragraphIndex,
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
                    lineLayout.Rubies,
                    properties.TextDirection,
                    isRtl));
                y += lineLayout.LineHeight;
                isFirstLine = false;
            }

            y += spacingAfter;
            paragraphLineRanges[paragraphIndex] = new LineRange(paragraphLineStart, lines.Count - paragraphLineStart);
            paragraphIndex++;
        }

        return new HeaderFooterLayoutResult(lines, tables, y, paragraphLineRanges, frameLayouts);
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

    private static List<TableLayout> OffsetHeaderFooterTables(IReadOnlyList<TableLayout> tables, float offsetX, float offsetY)
    {
        if (tables.Count == 0)
        {
            return new List<TableLayout>();
        }

        var result = new List<TableLayout>(tables.Count);
        foreach (var table in tables)
        {
            result.Add(OffsetTableLayout(table, offsetX, offsetY));
        }

        return result;
    }

    private static List<FloatingLayoutObject> BuildHeaderFooterFrameObjects(
        IReadOnlyList<HeaderFooterFrameLayout> frames,
        PageLayout page,
        PageSectionSettings section,
        float offsetX,
        float offsetY)
    {
        if (frames.Count == 0)
        {
            return new List<FloatingLayoutObject>();
        }

        var result = new List<FloatingLayoutObject>(frames.Count);
        foreach (var frame in frames)
        {
            var anchorLine = frame.AnchorLine with { X = frame.AnchorLine.X + offsetX, Y = frame.AnchorLine.Y + offsetY };
            var (width, height) = ResolveFloatingSize(frame.Floating.Content);
            if (width <= 0f || height <= 0f)
            {
                continue;
            }

            var baseX = ResolveAnchorX(frame.Floating.Anchor, anchorLine, page, section, width);
            var baseY = ResolveAnchorY(frame.Floating.Anchor, anchorLine, page, height);
            var x = baseX + frame.Floating.Anchor.OffsetX;
            var y = baseY + frame.Floating.Anchor.OffsetY;
            var bounds = new DocRect(x, y, width, height);
            var wrapContour = CreateWrapContour(frame.Floating.Anchor, bounds);
            result.Add(new FloatingLayoutObject(frame.Floating, anchorLine.ParagraphIndex, page.Index, bounds, wrapContour));
        }

        return result;
    }

    private static TableLayout OffsetTableLayout(TableLayout table, float dx, float dy)
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

    private static int CountParagraphsInTable(TableBlock table)
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

    private static List<FloatingLayoutObject> BuildHeaderFooterFloatingObjects(
        IReadOnlyList<Block> blocks,
        IReadOnlyList<HeaderFooterLine> lines,
        IReadOnlyDictionary<int, LineRange> paragraphLineRanges,
        PageLayout page,
        PageSectionSettings section)
    {
        var result = new List<FloatingLayoutObject>();
        if (blocks.Count == 0 || lines.Count == 0 || paragraphLineRanges.Count == 0)
        {
            return result;
        }

        var paragraphIndex = 0;
        foreach (var block in blocks)
        {
            if (block is not ParagraphBlock paragraph)
            {
                if (block is TableBlock table)
                {
                    paragraphIndex += CountParagraphsInTable(table);
                }

                continue;
            }

            if (paragraph.FloatingObjects.Count == 0)
            {
                paragraphIndex++;
                continue;
            }

            if (!paragraphLineRanges.TryGetValue(paragraphIndex, out var range) || range.Count == 0)
            {
                paragraphIndex++;
                continue;
            }

            var rangeStart = Math.Clamp(range.Start, 0, lines.Count - 1);
            var rangeEnd = Math.Clamp(range.End, rangeStart + 1, lines.Count);
            foreach (var floating in paragraph.FloatingObjects)
            {
                var anchorLineIndex = ResolveAnchorLineIndex(lines, rangeStart, rangeEnd, paragraphIndex, floating.Anchor.AnchorOffset);
                anchorLineIndex = Math.Clamp(anchorLineIndex, 0, lines.Count - 1);
                var anchorLine = lines[anchorLineIndex];
                var (width, height) = ResolveFloatingSize(floating.Content);
                if (width <= 0f || height <= 0f)
                {
                    continue;
                }

                var baseX = ResolveAnchorX(floating.Anchor, anchorLine, page, section, width);
                var baseY = ResolveAnchorY(floating.Anchor, anchorLine, page, height);
                var x = baseX + floating.Anchor.OffsetX;
                var y = baseY + floating.Anchor.OffsetY;
                var bounds = new DocRect(x, y, width, height);
                var wrapContour = CreateWrapContour(floating.Anchor, bounds);
                result.Add(new FloatingLayoutObject(floating, paragraphIndex, page.Index, bounds, wrapContour));
            }

            paragraphIndex++;
        }

        return result;
    }

    private static void AppendNoteBlocks(List<Block> target, IReadOnlyList<Block> source, int id, NoteKind kind)
    {
        if (source.Count == 0)
        {
            target.Add(BuildNotePrefixParagraph(id, kind));
            return;
        }

        if (source[0] is ParagraphBlock paragraph)
        {
            var cloned = CloneParagraphWithNotePrefix(paragraph, id, kind);
            target.Add(cloned);
            for (var i = 1; i < source.Count; i++)
            {
                target.Add(source[i]);
            }

            return;
        }

        target.Add(BuildNotePrefixParagraph(id, kind));
        for (var i = 0; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }

    private static ParagraphBlock CloneParagraphWithNotePrefix(ParagraphBlock source, int id, NoteKind kind)
    {
        var clone = (ParagraphBlock)DocumentClone.CloneBlock(source);
        if (clone.Inlines.Count == 0)
        {
            var text = clone.Text ?? string.Empty;
            if (text.Length > 0)
            {
                clone.Inlines.Add(new RunInline(text));
            }
        }

        clone.Inlines.Insert(0, new RunInline(" "));
        clone.Inlines.Insert(0, CreateNoteReferenceInline(kind, id));
        return clone;
    }

    private static ParagraphBlock BuildNotePrefixParagraph(int id, NoteKind kind)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(CreateNoteReferenceInline(kind, id));
        paragraph.Inlines.Add(new RunInline(" "));
        return paragraph;
    }

    private static Inline CreateNoteReferenceInline(NoteKind kind, int id)
    {
        return kind == NoteKind.Endnote
            ? new EndnoteReferenceInline(id)
            : new FootnoteReferenceInline(id);
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
        Dictionary<TextStyleKey, TextMetrics> spacingMetricsCache,
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
        var pageNumberTexts = BuildPageNumberTexts(pages, pageSections);
        var totalPagesTexts = BuildTotalPagesTexts(pages, pageSections, totalPages);

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
                        AppendNoteBlocks(blocks, definition.Blocks, id, NoteKind.Footnote);
                    }
                }
            }

            if (endnoteIds is not null)
            {
                foreach (var id in endnoteIds.OrderBy(id => id))
                {
                    if (document.Endnotes.TryGetValue(id, out var definition))
                    {
                        AppendNoteBlocks(blocks, definition.Blocks, id, NoteKind.Endnote);
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
            var pageNumberText = pageIndex < pageNumberTexts.Length
                ? pageNumberTexts[pageIndex]
                : pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var totalPagesText = pageIndex < totalPagesTexts.Length
                ? totalPagesTexts[pageIndex]
                : totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var layout = LayoutHeaderFooterBlocks(
                blocks,
                document,
                settings,
                measurer,
                style,
                styleResolver,
                spacingMetricsCache,
                contentWidth,
                lineHeight,
                ascent,
                section.DocGrid,
                pageNumberText,
                totalPagesText,
                includeTables: true,
                handleFrameParagraphs: false);

            if (layout.Lines.Count == 0 && layout.Tables.Count == 0)
            {
                continue;
            }

            var footerTop = page.Bounds.Bottom - section.MarginBottom;
            if (headerFooterMap.TryGetValue(pageIndex, out var headerFooter))
            {
                var minFooterY = float.MaxValue;
                var hasFooterContent = false;
                foreach (var line in headerFooter.FooterLines)
                {
                    if (line.Y < minFooterY)
                    {
                        minFooterY = line.Y;
                    }

                    hasFooterContent = true;
                }

                foreach (var table in headerFooter.FooterTables)
                {
                    var tableTop = table.Bounds.Y;
                    if (table.Bounds.Height <= 0f)
                    {
                        continue;
                    }

                    if (tableTop < minFooterY)
                    {
                        minFooterY = tableTop;
                    }

                    hasFooterContent = true;
                }

                if (hasFooterContent)
                {
                    footerTop = minFooterY;
                }
            }

            var footnoteTop = footerTop - layout.Height;
            var offsetLines = OffsetHeaderFooterLines(layout.Lines, page.Bounds.X + section.MarginLeft, footnoteTop);
            var separatorWidth = MathF.Min(120f, contentWidth * 0.25f);
            var separatorX = page.Bounds.X + section.MarginLeft;
            var separatorY = footnoteTop - MathF.Max(4f, settings.ParagraphSpacing * 0.5f);
            var separatorBounds = separatorWidth > 0f
                ? new DocRect(separatorX, separatorY, separatorWidth, 1f)
                : new DocRect(separatorX, separatorY, 0f, 0f);

            var offsetTables = OffsetHeaderFooterTables(layout.Tables, page.Bounds.X + section.MarginLeft, footnoteTop);
            footnoteLayouts.Add(new FootnoteLayout(page.Index, offsetLines, offsetTables, separatorBounds));
        }

        return footnoteLayouts;
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
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return format switch
        {
            PageNumberFormat.UpperRoman => ToRoman(value, true),
            PageNumberFormat.LowerRoman => ToRoman(value, false),
            PageNumberFormat.UpperLetter => ToAlpha(value, true),
            PageNumberFormat.LowerLetter => ToAlpha(value, false),
            _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string ToAlpha(int value, bool upper)
    {
        if (value <= 0)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var builder = new System.Text.StringBuilder();
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

        var result = builder.ToString();
        return upper ? result : result.ToLowerInvariant();
    }

    private static ParagraphBlock BuildFrameParagraph(ParagraphBlock source, ParagraphProperties resolvedProperties)
    {
        var clone = (ParagraphBlock)DocumentClone.CloneBlock(source);
        clone.Properties.DropCap = null;
        clone.Properties.Frame = null;
        clone.FloatingObjects.Clear();
        if (resolvedProperties.Alignment.HasValue)
        {
            clone.Properties.Alignment = resolvedProperties.Alignment;
        }

        return clone;
    }

    private static ShapeInline BuildFrameShape(ParagraphBlock paragraph, float width, float height)
    {
        var textBox = new ShapeTextBox();
        textBox.Blocks.Add(paragraph);

        var shapeProperties = new ShapeProperties
        {
            PresetGeometry = "rect"
        };

        return new ShapeInline(width, height, shapeProperties, textBox, "Frame");
    }

    private static void ApplyFrameAnchor(ParagraphFrameProperties frame, FloatingAnchor anchor)
    {
        anchor.HorizontalReference = frame.HorizontalReference ?? FloatingHorizontalReference.Margin;
        anchor.VerticalReference = frame.VerticalReference ?? FloatingVerticalReference.Paragraph;
        anchor.HorizontalAlignment = frame.HorizontalAlignment ?? FloatingHorizontalAlignment.None;
        anchor.VerticalAlignment = frame.VerticalAlignment ?? FloatingVerticalAlignment.None;
        anchor.OffsetX = frame.X ?? 0f;
        anchor.OffsetY = frame.Y ?? 0f;
        anchor.WrapStyle = frame.WrapStyle ?? FloatingWrapStyle.None;
        anchor.WrapSide = frame.WrapSide ?? FloatingWrapSide.Both;
        var hSpace = frame.HorizontalSpace ?? 0f;
        var vSpace = frame.VerticalSpace ?? 0f;
        anchor.Distance = new DocThickness(hSpace, vSpace, hSpace, vSpace);
    }

    private static void ApplyFloatingAnchor(FloatingAnchor source, FloatingAnchor target)
    {
        target.HorizontalReference = source.HorizontalReference;
        target.VerticalReference = source.VerticalReference;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.OffsetX = source.OffsetX;
        target.OffsetY = source.OffsetY;
        target.WrapStyle = source.WrapStyle;
        target.WrapSide = source.WrapSide;
        target.WrapPolygon = CloneWrapPolygon(source.WrapPolygon);
        target.BehindText = source.BehindText;
        target.Distance = source.Distance;
        target.AnchorOffset = source.AnchorOffset;
    }

    private static float MeasureInlineSpans(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer,
        float charGridSpacing,
        float tabStopOffset)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var items = CollectLineItems(spans, start, length, measurer, charGridSpacing);
        return MeasureLineItems(items, tabStops, defaultTabWidth, measurer, charGridSpacing, tabStopOffset);
    }

    private static LineLayout BuildLineLayout(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer,
        float defaultLineHeight,
        float defaultAscent,
        float charGridSpacing,
        float tabStopOffset)
    {
        var runs = new List<LayoutRun>();
        var images = new List<LayoutImage>();
        var shapes = new List<LayoutShape>();
        var charts = new List<LayoutChart>();
        var equations = new List<LayoutEquation>();
        var rubies = new List<LayoutRuby>();
        var items = CollectLineItems(spans, start, length, measurer, charGridSpacing);
        var x = 0f;
        var maxAscent = defaultAscent;
        var maxDescent = MathF.Max(0f, defaultLineHeight - defaultAscent);
        var maxImageHeight = 0f;
        var metricsCache = new Dictionary<TextStyleKey, TextMetrics>();
        var offset = 0;

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

                    runs.Add(new LayoutRun(item.Text, item.Style, x, item.Width, item.Text.Length, false, item.BaselineOffset, LetterSpacing: item.Style.LetterSpacing));
                    x += item.Width;
                    offset += item.Text.Length;
                    break;
                }
                case LineItemKind.Ruby:
                {
                    var baseMetrics = GetMetrics(item.Style, measurer, metricsCache);
                    var ascent = MathF.Max(0f, baseMetrics.Ascent + item.BaselineOffset);
                    var descent = MathF.Max(0f, baseMetrics.Descent - item.BaselineOffset);
                    maxAscent = MathF.Max(maxAscent, ascent);
                    maxDescent = MathF.Max(maxDescent, descent);

                    var baseWidth = TextGridSnapping.MeasureText(item.Text, item.Style, measurer, charGridSpacing);
                    runs.Add(new LayoutRun(item.Text, item.Style, x, baseWidth, item.Text.Length, false, item.BaselineOffset, LetterSpacing: item.Style.LetterSpacing));

                    var rubyStyle = item.RubyStyle ?? item.Style;
                    if (!string.IsNullOrEmpty(item.RubyText))
                    {
                        var rubyMetrics = GetMetrics(rubyStyle, measurer, metricsCache);
                        var gap = MathF.Max(0f, item.Style.FontSize * 0.1f);
                        var rubyBaselineOffset = -(ascent + gap + rubyMetrics.Descent);
                        var rubyAscent = ascent + gap + rubyMetrics.Height;
                        maxAscent = MathF.Max(maxAscent, rubyAscent);
                        rubies.Add(new LayoutRuby(offset, item.Text.Length, item.RubyText, rubyStyle, rubyBaselineOffset));
                    }

                    x += item.Width;
                    offset += item.Text.Length;
                    break;
                }
                case LineItemKind.Image:
                {
                    var width = item.Width;
                    var height = item.Height;
                    images.Add(new LayoutImage(item.Image!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    offset += 1;
                    break;
                }
                case LineItemKind.Shape:
                {
                    var width = item.Width;
                    var height = item.Height;
                    shapes.Add(new LayoutShape(item.Shape!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    offset += 1;
                    break;
                }
                case LineItemKind.Chart:
                {
                    var width = item.Width;
                    var height = item.Height;
                    charts.Add(new LayoutChart(item.Chart!, x, width, height, 1));
                    x += width;
                    maxImageHeight = MathF.Max(maxImageHeight, height);
                    offset += 1;
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
                    offset += 1;
                    break;
                }
                case LineItemKind.Tab:
                {
                    var tabStop = ResolveNextTabStop(x, tabStops, defaultTabWidth, tabStopOffset);
                    var nextTabIndex = FindNextTabIndex(items, i + 1);
                    var (fieldWidth, widthBeforeDecimal) = MeasureTabField(items, i + 1, nextTabIndex, measurer, charGridSpacing);
                    var tabWidth = ComputeTabWidth(tabStop, x, fieldWidth, widthBeforeDecimal);
                    runs.Add(new LayoutRun(string.Empty, item.Style, x, tabWidth, 1, true, item.BaselineOffset, tabStop.Leader, item.Style.LetterSpacing));
                    x += tabWidth;
                    offset += 1;
                    break;
                }
            }
        }

        var lineHeight = MathF.Max(defaultLineHeight, maxAscent + maxDescent);
        lineHeight = MathF.Max(lineHeight, maxImageHeight);
        var lineAscent = MathF.Max(defaultAscent, maxAscent);
        lineAscent = MathF.Max(lineAscent, maxImageHeight);

        return new LineLayout(runs, images, shapes, charts, equations, rubies, x, lineHeight, lineAscent);
    }

    private static LineLayout ApplyLineSpacing(LineLayout layout, ParagraphProperties properties, DocGridSettings? docGrid)
    {
        var targetHeight = ComputeLineHeight(layout.LineHeight, properties, docGrid);
        if (MathF.Abs(targetHeight - layout.LineHeight) < 0.01f)
        {
            return layout;
        }

        var scale = layout.LineHeight > 0f ? targetHeight / layout.LineHeight : 1f;
        var ascent = layout.Ascent * scale;
        return layout with { LineHeight = targetHeight, Ascent = ascent };
    }

    private static LineLayout AppendHyphenRun(LineLayout layout, TextStyle style, float baselineOffset, ITextMeasurer measurer, float charGridSpacing)
    {
        var width = TextGridSnapping.MeasureText("-".AsSpan(), style, measurer, charGridSpacing);
        if (width <= 0f)
        {
            return layout;
        }

        var runs = new List<LayoutRun>(layout.Runs.Count + 1);
        runs.AddRange(layout.Runs);
        runs.Add(new LayoutRun("-", style, layout.Width, width, 0, false, baselineOffset, LetterSpacing: style.LetterSpacing));
        return layout with { Runs = runs, Width = layout.Width + width };
    }

    private static (float LineHeight, float Ascent) ApplyLineSpacing(float lineHeight, float ascent, ParagraphProperties properties, DocGridSettings? docGrid)
    {
        var targetHeight = ComputeLineHeight(lineHeight, properties, docGrid);
        if (MathF.Abs(targetHeight - lineHeight) < 0.01f)
        {
            return (lineHeight, ascent);
        }

        var scale = lineHeight > 0f ? targetHeight / lineHeight : 1f;
        return (targetHeight, ascent * scale);
    }

    private static float ComputeLineHeight(float baseHeight, ParagraphProperties properties, DocGridSettings? docGrid)
    {
        if (!properties.LineSpacing.HasValue)
        {
            if (docGrid?.LinePitch is > 0f
                && docGrid.Type is DocGridType.Lines or DocGridType.LinesAndChars or DocGridType.SnapToChars)
            {
                return MathF.Max(1f, docGrid.LinePitch.Value);
            }

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

    private static float GetLineBlockHeight(TableCellLine line)
    {
        return DocTextDirectionHelpers.IsVertical(line.TextDirection) ? line.Width : line.LineHeight;
    }

    private static float GetLineBlockHeight(LayoutLine line)
    {
        return DocTextDirectionHelpers.IsVertical(line.TextDirection) ? line.Width : line.LineHeight;
    }

    private static (float X, float Y) ApplyDocGridSnapping(
        float x,
        float y,
        float ascent,
        DocTextDirection? textDirection,
        DocGridSettings? docGrid,
        float lineAxisOrigin,
        float stackAxisOrigin)
    {
        if (docGrid is null || !docGrid.HasValues)
        {
            return (x, y);
        }

        var isVertical = DocTextDirectionHelpers.IsVertical(textDirection);
        if (ShouldSnapLinePitch(docGrid))
        {
            var pitch = docGrid.LinePitch!.Value;
            if (!isVertical)
            {
                y = SnapBaselineToGrid(y, ascent, stackAxisOrigin, pitch);
            }
            else if (textDirection.HasValue)
            {
                var baselineOffset = DocTextDirectionHelpers.GetVerticalBaselineOffset(ascent, textDirection.Value);
                var baseline = x + baselineOffset;
                var snappedBaseline = SnapToGridForward(baseline, stackAxisOrigin, pitch);
                x = snappedBaseline - baselineOffset;
            }
        }

        if (ShouldSnapCharacters(docGrid))
        {
            var charSpace = docGrid.CharacterSpace!.Value;
            if (!isVertical)
            {
                x = SnapToGridForward(x, lineAxisOrigin, charSpace);
            }
            else
            {
                y = SnapToGridForward(y, lineAxisOrigin, charSpace);
            }
        }

        return (x, y);
    }

    private static bool ShouldSnapLinePitch(DocGridSettings docGrid)
    {
        return docGrid.LinePitch is > 0f
               && (!docGrid.Type.HasValue || docGrid.Type.Value != DocGridType.Default);
    }

    private static bool ShouldSnapCharacters(DocGridSettings docGrid)
    {
        return TextGridSnapping.GetCharacterSpacing(docGrid) > 0f;
    }

    private static float SnapBaselineToGrid(float lineTop, float ascent, float origin, float spacing)
    {
        var baseline = lineTop + ascent;
        var snappedBaseline = SnapToGridForward(baseline, origin + ascent, spacing);
        return snappedBaseline - ascent;
    }

    private static float SnapToGridForward(float value, float origin, float spacing)
    {
        if (spacing <= 0f)
        {
            return value;
        }

        var offset = value - origin;
        var snapped = MathF.Ceiling(offset / spacing) * spacing + origin;
        return snapped;
    }

    private static float TwipsToDip(int twips)
    {
        return twips / 20f * 96f / 72f;
    }

    private enum LineItemKind
    {
        Text,
        Ruby,
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
        public string RubyText { get; }
        public TextStyle? RubyStyle { get; }
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
            string rubyText,
            TextStyle? rubyStyle,
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
            RubyText = rubyText;
            RubyStyle = rubyStyle;
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
            return new LineItem(LineItemKind.Text, text, style, baselineOffset, string.Empty, null, null, null, null, null, null, width, 0f);
        }

        public static LineItem RubySegment(string text, TextStyle style, float baselineOffset, string rubyText, TextStyle rubyStyle, float width)
        {
            return new LineItem(LineItemKind.Ruby, text, style, baselineOffset, rubyText, rubyStyle, null, null, null, null, null, width, 0f);
        }

        public static LineItem Tab(TextStyle style, float baselineOffset)
        {
            return new LineItem(LineItemKind.Tab, string.Empty, style, baselineOffset, string.Empty, null, null, null, null, null, null, 0f, 0f);
        }

        public static LineItem ImageSegment(ImageInline image, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Image, string.Empty, style, 0f, string.Empty, null, image, null, null, null, null, width, height);
        }

        public static LineItem ShapeSegment(ShapeInline shape, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Shape, string.Empty, style, 0f, string.Empty, null, null, shape, null, null, null, width, height);
        }

        public static LineItem ChartSegment(ChartInline chart, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Chart, string.Empty, style, 0f, string.Empty, null, null, null, chart, null, null, width, height);
        }

        public static LineItem EquationSegment(EquationInline equation, MathLayout layout, TextStyle style, float width, float height)
        {
            return new LineItem(LineItemKind.Equation, string.Empty, style, 0f, string.Empty, null, null, null, null, equation, layout, width, height);
        }
    }

    private readonly record struct TabStopInfo(float Position, TabAlignment Alignment, TabLeader Leader);

    private static List<LineItem> CollectLineItems(
        IReadOnlyList<InlineSpan> spans,
        int start,
        int length,
        ITextMeasurer measurer,
        float charGridSpacing)
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

            if (span.Ruby is not null && span.RubyStyle is not null && segmentStart == spanStart && segmentLength == span.Length)
            {
                var baseText = span.Text;
                var baseWidth = TextGridSnapping.MeasureText(baseText, span.Style, measurer, charGridSpacing);
                var rubyText = span.Ruby.RubyText;
                var rubyWidth = string.IsNullOrEmpty(rubyText)
                    ? 0f
                    : TextGridSnapping.MeasureText(rubyText, span.RubyStyle, measurer, charGridSpacing);
                var width = MathF.Max(baseWidth, rubyWidth);
                var rubyStyle = span.RubyStyle ?? span.Style;
                items.Add(LineItem.RubySegment(baseText, span.Style, span.BaselineOffset, rubyText, rubyStyle, width));
                continue;
            }

            var segmentText = span.Text.AsSpan(segmentStart - spanStart, segmentLength);
            AppendTextItems(items, segmentText, span.Style, span.BaselineOffset, measurer, charGridSpacing);
        }

        return items;
    }

    private static void AppendTextItems(
        List<LineItem> items,
        ReadOnlySpan<char> text,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer,
        float charGridSpacing)
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
                var width = TextGridSnapping.MeasureText(segmentText, style, measurer, charGridSpacing);
                items.Add(LineItem.TextSegment(segmentText, style, baselineOffset, width));
            }

            items.Add(LineItem.Tab(style, baselineOffset));
            segmentStart = i + 1;
        }

        if (segmentStart < text.Length)
        {
            var segmentText = new string(text.Slice(segmentStart));
            var width = TextGridSnapping.MeasureText(segmentText, style, measurer, charGridSpacing);
            items.Add(LineItem.TextSegment(segmentText, style, baselineOffset, width));
        }
    }

    private static float MeasureLineItems(
        IReadOnlyList<LineItem> items,
        IReadOnlyList<TabStopDefinition> tabStops,
        float defaultTabWidth,
        ITextMeasurer measurer,
        float charGridSpacing,
        float tabStopOffset)
    {
        var x = 0f;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Kind)
            {
                case LineItemKind.Text:
                case LineItemKind.Ruby:
                case LineItemKind.Image:
                case LineItemKind.Shape:
                case LineItemKind.Chart:
                case LineItemKind.Equation:
                    x += item.Width;
                    break;
                case LineItemKind.Tab:
                {
                    var tabStop = ResolveNextTabStop(x, tabStops, defaultTabWidth, tabStopOffset);
                    var nextTabIndex = FindNextTabIndex(items, i + 1);
                    var (fieldWidth, widthBeforeDecimal) = MeasureTabField(items, i + 1, nextTabIndex, measurer, charGridSpacing);
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
        ITextMeasurer measurer,
        float charGridSpacing)
    {
        var fieldWidth = 0f;
        var beforeDecimalWidth = 0f;
        var decimalFound = false;

        for (var i = startIndex; i < endIndex; i++)
        {
            var item = items[i];
            if (item.Kind == LineItemKind.Text || item.Kind == LineItemKind.Ruby)
            {
                if (!decimalFound)
                {
                    var decimalIndex = FindDecimalIndex(item.Text);
                    if (decimalIndex >= 0)
                    {
                        if (decimalIndex > 0)
                        {
                            var beforeSpan = item.Text.AsSpan(0, decimalIndex);
                            var beforeWidth = TextGridSnapping.MeasureText(beforeSpan, item.Style, measurer, charGridSpacing);
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
        float defaultTabWidth,
        float tabStopOffset)
    {
        var absoluteX = currentX + tabStopOffset;
        for (var i = 0; i < tabStops.Count; i++)
        {
            var stop = tabStops[i];
            if (stop.Position > absoluteX)
            {
                return new TabStopInfo(stop.Position - tabStopOffset, stop.Alignment, stop.Leader);
            }
        }

        var safeWidth = MathF.Max(1f, defaultTabWidth);
        var next = MathF.Floor(absoluteX / safeWidth) * safeWidth + safeWidth;
        return new TabStopInfo(next - tabStopOffset, TabAlignment.Left, TabLeader.None);
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

    private static float[] ResolveSectionColumnWidths(PageSectionSettings section, float contentWidth, IReadOnlyList<float> columnGaps)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var gapTotal = 0f;
        for (var i = 0; i < columnGaps.Count; i++)
        {
            gapTotal += columnGaps[i];
        }

        var availableWidth = MathF.Max(1f, contentWidth - gapTotal);

        if (section.ColumnEqualWidth || section.ColumnWidths.Count == 0)
        {
            var width = availableWidth / columnCount;
            var widths = new float[columnCount];
            Array.Fill(widths, width);
            return widths;
        }

        var resolved = new float[columnCount];
        var explicitCount = Math.Min(section.ColumnWidths.Count, columnCount);
        var explicitTotal = 0f;
        for (var i = 0; i < explicitCount; i++)
        {
            resolved[i] = section.ColumnWidths[i];
            explicitTotal += resolved[i];
        }

        if (explicitCount < columnCount)
        {
            var remaining = MathF.Max(0f, availableWidth - explicitTotal);
            var autoWidth = remaining / Math.Max(1, columnCount - explicitCount);
            for (var i = explicitCount; i < columnCount; i++)
            {
                resolved[i] = autoWidth;
            }
        }

        var total = resolved.Sum();
        if (total <= 0f)
        {
            var width = availableWidth / columnCount;
            Array.Fill(resolved, width);
            return resolved;
        }

        var delta = availableWidth - total;
        if (MathF.Abs(delta) > 0.01f)
        {
            var lastIndex = resolved.Length - 1;
            if (resolved[lastIndex] + delta > 0f)
            {
                resolved[lastIndex] += delta;
            }
            else if (availableWidth > 0f)
            {
                var scale = availableWidth / total;
                for (var i = 0; i < resolved.Length; i++)
                {
                    resolved[i] *= scale;
                }
            }
        }

        return resolved;
    }

    private static float[] ResolveSectionColumnGaps(PageSectionSettings section, int columnCount, float defaultGap)
    {
        if (columnCount <= 1)
        {
            return Array.Empty<float>();
        }

        var gaps = new float[columnCount - 1];
        if (section.ColumnGaps.Count == 0)
        {
            Array.Fill(gaps, MathF.Max(0f, defaultGap));
            return gaps;
        }

        for (var i = 0; i < gaps.Length; i++)
        {
            var gap = i < section.ColumnGaps.Count ? section.ColumnGaps[i] : float.NaN;
            if (float.IsNaN(gap))
            {
                gap = defaultGap;
            }

            gaps[i] = MathF.Max(0f, gap);
        }

        return gaps;
    }

    private static float[] BuildColumnOffsets(float[] widths, IReadOnlyList<float> gaps)
    {
        var offsets = new float[widths.Length];
        var current = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            offsets[i] = current;
            current += widths[i];
            if (i < widths.Length - 1)
            {
                current += gaps[i];
            }
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

    private static float[] ResolveColumnWidths(
        TableProperties properties,
        int columnCount,
        float contentWidth,
        float? tableWidth,
        float cellSpacing)
    {
        var widths = new float[columnCount];
        var hasExplicitWidths = properties.ColumnWidths.Count > 0;
        var spacingTotal = cellSpacing * (columnCount + 1);

        if (!hasExplicitWidths)
        {
            var targetWidth = tableWidth ?? contentWidth;
            var availableWidth = MathF.Max(1f, targetWidth - spacingTotal);
            var defaultWidth = availableWidth / Math.Max(1, columnCount);
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
            var targetWidth = tableWidth ?? contentWidth;
            var availableWidth = MathF.Max(1f, targetWidth - spacingTotal);
            var defaultWidth = availableWidth / Math.Max(1, columnCount);
            Array.Fill(widths, defaultWidth);
            return widths;
        }

        if (tableWidth.HasValue)
        {
            var availableWidth = MathF.Max(1f, tableWidth.Value - spacingTotal);
            var scale = availableWidth / total;
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] *= scale;
            }
        }
        else if (total > contentWidth && contentWidth > 0f && properties.LayoutMode != TableLayoutMode.Fixed)
        {
            var availableWidth = MathF.Max(1f, contentWidth - spacingTotal);
            if (total > availableWidth)
            {
                var scale = availableWidth / total;
                for (var i = 0; i < widths.Length; i++)
                {
                    widths[i] *= scale;
                }
            }
        }

        return widths;
    }

    private static float? ResolveTableWidth(TableProperties properties, float contentWidth)
    {
        if (!properties.Width.HasValue && !properties.WidthUnit.HasValue)
        {
            return null;
        }

        var unit = properties.WidthUnit ?? TableWidthUnit.Dxa;
        if (unit == TableWidthUnit.Auto)
        {
            return null;
        }

        var resolved = ResolveTableMeasurement(properties.Width, unit, contentWidth);
        return resolved is > 0f ? resolved : null;
    }

    private static float ResolveTableSpacingBaseWidth(TableProperties properties, float contentWidth)
    {
        if (properties.ColumnWidths.Count == 0)
        {
            return contentWidth;
        }

        var total = properties.ColumnWidths.Sum();
        if (total <= 0f)
        {
            return contentWidth;
        }

        if (properties.LayoutMode == TableLayoutMode.Fixed)
        {
            return total;
        }

        return MathF.Min(total, contentWidth);
    }

    private static float ResolveTableIndent(TableProperties properties, float contentWidth)
    {
        var resolved = ResolveTableMeasurement(properties.Indent, properties.IndentUnit ?? TableWidthUnit.Dxa, contentWidth);
        return resolved ?? 0f;
    }

    private static float ResolveTableCellSpacing(TableProperties properties, float referenceWidth)
    {
        var resolved = ResolveTableMeasurement(properties.CellSpacing, properties.CellSpacingUnit ?? TableWidthUnit.Dxa, referenceWidth);
        return resolved is > 0f ? resolved.Value : 0f;
    }

    private static float? ResolveTableMeasurement(float? value, TableWidthUnit unit, float referenceWidth)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return unit switch
        {
            TableWidthUnit.Dxa => value.Value,
            TableWidthUnit.Pct => referenceWidth * (value.Value / 100f),
            _ => null
        };
    }

    private static float ResolveTableX(float columnX, float columnWidth, TableProperties properties, float tableWidth)
    {
        var alignment = properties.Alignment ?? TableAlignment.Left;
        var indent = ResolveTableIndent(properties, columnWidth);
        var offset = alignment switch
        {
            TableAlignment.Center => (columnWidth - tableWidth) / 2f,
            TableAlignment.Right => columnWidth - tableWidth,
            _ => 0f
        };

        if (offset < 0f)
        {
            offset = 0f;
        }

        var x = columnX + indent + offset;
        return x < columnX ? columnX : x;
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
            var rowColumns = Math.Max(0, row.Properties.GridBefore ?? 0);
            foreach (var cell in row.Cells)
            {
                rowColumns += Math.Max(1, cell.ColumnSpan);
            }

            rowColumns += Math.Max(0, row.Properties.GridAfter ?? 0);
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

    private static float SumColumnsWithSpacing(IReadOnlyList<float> widths, int start, int span, float spacing)
    {
        if (widths.Count == 0 || span <= 0)
        {
            return 0f;
        }

        var total = SumColumns(widths, start, span);
        if (span > 1 && spacing > 0f)
        {
            total += spacing * (span - 1);
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

    private static float SumRowsWithSpacing(IReadOnlyList<float> heights, int start, int span, float spacing)
    {
        if (heights.Count == 0 || span <= 0)
        {
            return 0f;
        }

        var total = SumRows(heights, start, span);
        if (span > 1 && spacing > 0f)
        {
            total += spacing * (span - 1);
        }

        return total;
    }

    private static int GetRepeatHeaderRowCount(TableBlock table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < table.Rows.Count; i++)
        {
            if (table.Rows[i].Properties.RepeatOnEachPage == true)
            {
                count++;
                continue;
            }

            break;
        }

        return count;
    }

    private static bool HasHeaderRowSpanCrossing(TableLayoutData data, int headerRowCount)
    {
        if (headerRowCount <= 0)
        {
            return false;
        }

        foreach (var placement in data.Cells)
        {
            if (placement.IsMergeContinuation)
            {
                continue;
            }

            if (placement.RowIndex < headerRowCount
                && placement.RowSpan > headerRowCount - placement.RowIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static float ComputeHeaderBlockHeight(float[] rowHeights, int headerRowCount, float cellSpacing, bool includeGapToBody)
    {
        if (headerRowCount <= 0)
        {
            return 0f;
        }

        var height = SumRows(rowHeights, 0, headerRowCount);
        if (headerRowCount > 1)
        {
            height += cellSpacing * (headerRowCount - 1);
        }

        if (includeGapToBody)
        {
            height += cellSpacing;
        }

        return height;
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
        TableRowProperties rowProperties,
        TableProperties tableProperties,
        TableStyleDefinition? tableStyle,
        TableLook tableLook,
        bool separateBorders,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var resolved = new TableCellProperties();

        if (tableStyle is not null)
        {
            ApplyTablePropertiesToCell(resolved, tableStyle.TableProperties, separateBorders, rowIndex, columnIndex, rowCount, columnCount);
            ApplyTableCellProperties(resolved, tableStyle.CellProperties);

            foreach (var condition in GetApplicableTableStyleConditions(tableLook, rowIndex, columnIndex, rowCount, columnCount))
            {
                if (!tableStyle.Conditions.TryGetValue(condition, out var conditionProperties))
                {
                    continue;
                }

                ApplyTablePropertiesToCell(resolved, conditionProperties.TableProperties, separateBorders, rowIndex, columnIndex, rowCount, columnCount);
                ApplyTableCellProperties(resolved, conditionProperties.CellProperties);
            }
        }

        ApplyTablePropertiesToCell(resolved, tableProperties, separateBorders, rowIndex, columnIndex, rowCount, columnCount);
        ApplyTableRowPropertiesToCell(resolved, rowProperties);
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

        if (look.FirstRow && look.FirstColumn && rowIndex == 0 && columnIndex == 0)
        {
            yield return TableStyleCondition.NorthWestCell;
        }

        if (look.FirstRow && look.LastColumn && rowIndex == 0 && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.NorthEastCell;
        }

        if (look.LastRow && look.FirstColumn && rowIndex == rowCount - 1 && columnIndex == 0)
        {
            yield return TableStyleCondition.SouthWestCell;
        }

        if (look.LastRow && look.LastColumn && rowIndex == rowCount - 1 && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.SouthEastCell;
        }
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties source)
    {
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        if (source.Width.HasValue)
        {
            target.Width = source.Width;
        }

        if (source.WidthUnit.HasValue)
        {
            target.WidthUnit = source.WidthUnit;
        }

        if (source.Indent.HasValue)
        {
            target.Indent = source.Indent;
        }

        if (source.IndentUnit.HasValue)
        {
            target.IndentUnit = source.IndentUnit;
        }

        if (source.Alignment.HasValue)
        {
            target.Alignment = source.Alignment;
        }

        if (source.LayoutMode.HasValue)
        {
            target.LayoutMode = source.LayoutMode;
        }

        if (source.CellSpacing.HasValue)
        {
            target.CellSpacing = source.CellSpacing;
        }

        if (source.CellSpacingUnit.HasValue)
        {
            target.CellSpacingUnit = source.CellSpacingUnit;
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

        if (source.FloatingAnchor is not null)
        {
            target.FloatingAnchor = CloneFloatingAnchor(source.FloatingAnchor);
        }

        ApplyTableBorders(target.Borders, source.Borders);
    }

    private static FloatingAnchor CloneFloatingAnchor(FloatingAnchor source)
    {
        return new FloatingAnchor
        {
            HorizontalReference = source.HorizontalReference,
            VerticalReference = source.VerticalReference,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            WrapStyle = source.WrapStyle,
            WrapSide = source.WrapSide,
            WrapPolygon = CloneWrapPolygon(source.WrapPolygon),
            BehindText = source.BehindText,
            Distance = source.Distance,
            AnchorOffset = source.AnchorOffset
        };
    }

    private static FloatingWrapPolygon? CloneWrapPolygon(FloatingWrapPolygon? polygon)
    {
        if (polygon is null)
        {
            return null;
        }

        var points = polygon.Points.ToArray();
        return new FloatingWrapPolygon(points);
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
        bool separateBorders,
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

        ApplyTableBordersToCell(target.Borders, source.Borders, separateBorders, rowIndex, columnIndex, rowCount, columnCount);
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
        bool separateBorders,
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

        if (source.InsideHorizontal is not null)
        {
            if (separateBorders)
            {
                if (!isFirstRow)
                {
                    target.Top = source.InsideHorizontal.Clone();
                }

                if (!isLastRow)
                {
                    target.Bottom = source.InsideHorizontal.Clone();
                }
            }
            else if (!isLastRow)
            {
                target.Bottom = source.InsideHorizontal.Clone();
            }
        }

        if (source.InsideVertical is not null)
        {
            if (separateBorders)
            {
                if (!isFirstColumn)
                {
                    target.Left = source.InsideVertical.Clone();
                }

                if (!isLastColumn)
                {
                    target.Right = source.InsideVertical.Clone();
                }
            }
            else if (!isLastColumn)
            {
                target.Right = source.InsideVertical.Clone();
            }
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

    private static void ApplyTableRowPropertiesToCell(TableCellProperties target, TableRowProperties source)
    {
        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }
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

    private static LineLayout JustifyLineLayout(LineLayout layout, float targetWidth, ITextMeasurer measurer, float charGridSpacing)
    {
        return LineJustifier.Justify(layout, targetWidth, measurer, charGridSpacing);
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
    private readonly record struct DropCapInfo(
        int Length,
        int Lines,
        float Width,
        float Distance,
        DropCapKind Kind);
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
