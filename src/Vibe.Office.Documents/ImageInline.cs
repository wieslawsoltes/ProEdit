namespace Vibe.Office.Documents;

public sealed class ImageInline : Inline
{
    public Guid Id { get; } = Guid.NewGuid();
    public byte[] Data { get; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Rotation { get; set; }
    public string ContentType { get; }
    public EmbeddedObjectInfo? EmbeddedObject { get; set; }
    public DiagramInfo? Diagram { get; set; }
    public DrawingEffects? Effects { get; set; }
    public ImageCrop? Crop { get; set; }

    public ImageInline(byte[] data, float width, float height, string contentType)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Width = width;
        Height = height;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
    }
}
