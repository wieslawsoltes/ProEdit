namespace ProEdit.Rendering;

public readonly record struct RasterImage(
    ReadOnlyMemory<byte> Data,
    string ContentType,
    int PixelWidth,
    int PixelHeight);
