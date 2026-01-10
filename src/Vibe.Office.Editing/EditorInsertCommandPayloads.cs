using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public readonly record struct EditorTableInsertRequest(int Rows, int Columns);

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

public readonly record struct EditorChartInsertRequest(
    ChartType Type,
    string? Title = null,
    ChartBarDirection? BarDirection = null,
    ChartStacking? Stacking = null,
    int? SeriesCount = null,
    int? CategoryCount = null);
