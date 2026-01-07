namespace Vibe.Office.Layout;

public enum BreakMarkerKind
{
    Page,
    Section
}

public sealed record BreakMarker(
    BreakMarkerKind Kind,
    int PageIndex,
    float X,
    float Width,
    float Y,
    string Label);
