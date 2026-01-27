using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed class DocumentLayout
{
    public LayoutSettings Settings { get; }
    public IReadOnlyList<ParagraphBlock> Paragraphs { get; }
    public IReadOnlyList<LayoutLine> Lines { get; }
    public IReadOnlyList<TableLayout> Tables { get; }
    public IReadOnlyList<PageLayout> Pages { get; }
    public IReadOnlyList<HeaderFooterLayout> HeaderFooters { get; }
    public IReadOnlyList<FootnoteLayout> Footnotes { get; }
    public IReadOnlyList<FloatingLayoutObject> FloatingObjects { get; }
    public IReadOnlyList<FloatingLayoutObject> ExtraFloatingObjects { get; }
    public IReadOnlyList<PageSectionSettings> PageSections { get; }
    public IReadOnlyDictionary<int, PageSectionSettings> SectionSettings { get; }
    public IReadOnlyList<BreakMarker> BreakMarkers { get; }
    public LineIndex LineIndex { get; }
    public IReadOnlyDictionary<int, LineRange> ParagraphLineRanges { get; }
    public IReadOnlyDictionary<int, int> ParagraphSectionIndices { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<CommentHighlightSpan>> CommentHighlightsByParagraph { get; }
    public IReadOnlyDictionary<int, float> ParagraphSpacingBefore { get; }
    public float LineHeight { get; }
    public float Ascent { get; }
    public float ContentHeight { get; }

    public DocumentLayout(
        LayoutSettings settings,
        IReadOnlyList<ParagraphBlock> paragraphs,
        IReadOnlyList<LayoutLine> lines,
        IReadOnlyList<TableLayout> tables,
        IReadOnlyList<PageLayout> pages,
        IReadOnlyList<HeaderFooterLayout> headerFooters,
        IReadOnlyList<FootnoteLayout> footnotes,
        IReadOnlyList<FloatingLayoutObject> floatingObjects,
        IReadOnlyList<FloatingLayoutObject> extraFloatingObjects,
        IReadOnlyList<PageSectionSettings> pageSections,
        IReadOnlyDictionary<int, PageSectionSettings> sectionSettings,
        IReadOnlyList<BreakMarker> breakMarkers,
        LineIndex lineIndex,
        IReadOnlyDictionary<int, LineRange> paragraphLineRanges,
        IReadOnlyDictionary<int, int> paragraphSectionIndices,
        IReadOnlyDictionary<int, IReadOnlyList<CommentHighlightSpan>> commentHighlightsByParagraph,
        IReadOnlyDictionary<int, float> paragraphSpacingBefore,
        float lineHeight,
        float ascent,
        float contentHeight)
    {
        Settings = settings;
        Paragraphs = paragraphs;
        Lines = lines;
        Tables = tables;
        Pages = pages;
        HeaderFooters = headerFooters;
        Footnotes = footnotes;
        FloatingObjects = floatingObjects;
        ExtraFloatingObjects = extraFloatingObjects;
        PageSections = pageSections;
        SectionSettings = sectionSettings;
        BreakMarkers = breakMarkers;
        LineIndex = lineIndex;
        ParagraphLineRanges = paragraphLineRanges;
        ParagraphSectionIndices = paragraphSectionIndices;
        CommentHighlightsByParagraph = commentHighlightsByParagraph;
        ParagraphSpacingBefore = paragraphSpacingBefore;
        LineHeight = lineHeight;
        Ascent = ascent;
        ContentHeight = contentHeight;
    }
}
