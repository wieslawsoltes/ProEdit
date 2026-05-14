using ProEdit.Documents;

namespace ProEdit.Editing;

public readonly record struct EditorTableInsertRequest(int Rows, int Columns);

public readonly record struct EditorTableTemplateInsertRequest(
    int Rows,
    int Columns,
    string? StyleId = null,
    IReadOnlyList<string>? CellText = null);

public readonly record struct EditorImageInsertRequest(
    byte[] Data,
    float Width,
    float Height,
    string? ContentType);

public readonly record struct EditorHyperlinkInsertRequest(
    string? Uri,
    string? Anchor,
    string? DisplayText,
    string? Tooltip);

public readonly record struct EditorBookmarkInsertRequest(string Name);

public readonly record struct EditorPageNumberInsertRequest(bool InFooter, bool IncludeTotalPages);

public readonly record struct EditorHeaderFooterUpdateRequest(IReadOnlyList<Block> Blocks, Document? SourceDocument = null, HeaderFooter? Target = null);

public readonly record struct EditorChartInsertRequest(
    ChartType Type,
    string? Title = null,
    ChartBarDirection? BarDirection = null,
    ChartStacking? Stacking = null,
    int? SeriesCount = null,
    int? CategoryCount = null);

public readonly record struct EditorEmbeddedObjectInsertRequest(
    byte[] Data,
    string? ContentType,
    string? ProgId = null,
    string? TargetUri = null,
    bool IsLinked = false);

public readonly record struct EditorCrossReferenceInsertRequest(
    string? BookmarkName,
    bool IncludePageNumber = false,
    bool Hyperlink = true);
