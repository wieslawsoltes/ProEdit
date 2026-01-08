namespace Vibe.Office.Documents;

public sealed class ImageInline : Inline
{
    public Guid Id { get; } = Guid.NewGuid();
    public byte[] Data { get; }
    public float Width { get; }
    public float Height { get; }
    public string ContentType { get; }
    public EmbeddedObjectInfo? EmbeddedObject { get; set; }
    public DiagramInfo? Diagram { get; set; }

    public ImageInline(byte[] data, float width, float height, string contentType)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Width = width;
        Height = height;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
    }
}
