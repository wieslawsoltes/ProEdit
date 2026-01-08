namespace Vibe.Office.Documents;

public sealed class EmbeddedObjectInfo
{
    public string? RelationshipId { get; set; }
    public string? ContentType { get; set; }
    public string? TargetUri { get; set; }
    public byte[]? Data { get; set; }
    public string? ProgId { get; set; }
    public string? ClassId { get; set; }
    public string? ObjectId { get; set; }
    public bool? IsLinked { get; set; }
    public string? UpdateMode { get; set; }
}
