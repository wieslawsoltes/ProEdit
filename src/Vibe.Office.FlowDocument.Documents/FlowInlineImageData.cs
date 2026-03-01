namespace Vibe.Office.FlowDocument.Documents;

/// <summary>
/// Serializable payload used inside <see cref="Vibe.Office.FlowDocument.InlineUIContainer"/>
/// to preserve inline image content across FlowDocument conversions without UI platform services.
/// </summary>
public sealed class FlowInlineImageData
{
    public FlowInlineImageData(byte[] data, float width, float height, string contentType)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data.Length == 0 ? Array.Empty<byte>() : (byte[])data.Clone();
        Width = width;
        Height = height;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
    }

    public byte[] Data { get; }

    public float Width { get; }

    public float Height { get; }

    public string ContentType { get; }
}
