namespace ProEdit.Documents;

public sealed class AltChunkBlock : Block
{
    public string? RelationshipId { get; set; }
    public string? ContentType { get; set; }
    public string? TargetUri { get; set; }
    public byte[]? Data { get; set; }
    public string? Label { get; set; }
}
