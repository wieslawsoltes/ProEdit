using SkiaSharp;
using Vibe.Office.Documents;

namespace Vibe.Office.Rendering.Skia;

public interface ISkiaTypefaceResolver
{
    SKTypeface ResolveTypeface(TextStyle style);
}

public sealed class SkiaDocumentFontResolver : ISkiaTypefaceResolver
{
    private readonly DocumentFonts _fonts;
    private readonly Dictionary<FontKey, SKTypeface> _cache = new();
    private readonly Dictionary<EmbeddedFontData, SKTypeface> _embeddedCache = new();
    private readonly Dictionary<EmbeddedFontData, SKData> _embeddedData = new();

    public SkiaDocumentFontResolver(DocumentFonts fonts)
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    }

    public SKTypeface ResolveTypeface(TextStyle style)
    {
        var family = style.FontFamily ?? string.Empty;
        var key = new FontKey(family, style.FontWeight, style.FontStyle);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var typeface = ResolveEmbeddedTypeface(style, family);
        if (typeface is null)
        {
            typeface = ResolveSystemTypeface(style, family) ?? SKTypeface.Default;
        }

        _cache[key] = typeface;
        return typeface;
    }

    private SKTypeface? ResolveEmbeddedTypeface(TextStyle style, string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        if (!_fonts.FontTable.TryGetValue(family, out var definition))
        {
            return null;
        }

        var embedded = definition.GetEmbeddedFont(style.FontWeight, style.FontStyle);
        if (embedded is not null)
        {
            if (_embeddedCache.TryGetValue(embedded, out var cached))
            {
                return cached;
            }

            var data = SKData.CreateCopy(embedded.Data);
            _embeddedData[embedded] = data;
            var typeface = SKTypeface.FromData(data);
            if (typeface is not null)
            {
                _embeddedCache[embedded] = typeface;
                return typeface;
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.AltName))
        {
            return ResolveSystemTypeface(style, definition.AltName);
        }

        return null;
    }

    private SKTypeface? ResolveSystemTypeface(TextStyle style, string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var weight = style.FontWeight == DocFontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.FontStyle == DocFontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant);
    }

    private readonly record struct FontKey(string Family, DocFontWeight Weight, DocFontStyle Style);
}
