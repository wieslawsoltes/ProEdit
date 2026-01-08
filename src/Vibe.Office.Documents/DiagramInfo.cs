namespace Vibe.Office.Documents;

public sealed class DiagramInfo
{
    public string? DataRelationshipId { get; set; }
    public string? LayoutRelationshipId { get; set; }
    public string? QuickStyleRelationshipId { get; set; }
    public string? ColorStyleRelationshipId { get; set; }
    public byte[]? DataPart { get; set; }
    public byte[]? LayoutPart { get; set; }
    public byte[]? QuickStylePart { get; set; }
    public byte[]? ColorStylePart { get; set; }

    public bool HasValues =>
        !string.IsNullOrWhiteSpace(DataRelationshipId)
        || !string.IsNullOrWhiteSpace(LayoutRelationshipId)
        || !string.IsNullOrWhiteSpace(QuickStyleRelationshipId)
        || !string.IsNullOrWhiteSpace(ColorStyleRelationshipId)
        || (DataPart?.Length ?? 0) > 0
        || (LayoutPart?.Length ?? 0) > 0
        || (QuickStylePart?.Length ?? 0) > 0
        || (ColorStylePart?.Length ?? 0) > 0;
}
