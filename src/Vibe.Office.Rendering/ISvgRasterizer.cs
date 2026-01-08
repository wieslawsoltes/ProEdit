namespace Vibe.Office.Rendering;

public interface ISvgRasterizer
{
    bool TryRasterize(ReadOnlySpan<byte> svgData, SvgRasterizationOptions options, out RasterImage raster);
}
