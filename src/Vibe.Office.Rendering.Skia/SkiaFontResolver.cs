using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Rendering.Skia;

public interface ISkiaTypefaceResolver
{
    SKTypeface ResolveTypeface(TextStyle style);
}

public interface ISkiaTypefaceFallbackResolver : ISkiaTypefaceResolver
{
    SKTypeface? ResolveFallbackTypeface(TextStyle style, ReadOnlySpan<char> text);
}

public sealed class SkiaDocumentFontResolver : ISkiaTypefaceFallbackResolver, IDisposable
{
    private readonly DocumentFonts _fonts;
    private readonly Dictionary<FontKey, SKTypeface> _cache = new();
    private readonly Dictionary<EmbeddedFontData, SKTypeface> _embeddedCache = new();
    private readonly Dictionary<EmbeddedFontData, SKData> _embeddedData = new();
    private readonly Dictionary<FallbackKey, SKTypeface?> _fallbackCache = new();
    private bool _disposed;

    public SkiaDocumentFontResolver(DocumentFonts fonts)
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    }

    public SKTypeface ResolveTypeface(TextStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public SKTypeface? ResolveFallbackTypeface(TextStyle style, ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (text.IsEmpty)
        {
            return null;
        }

        if (!Utf16Decoder.TryDecodeFromUtf16(text, out var rune, out _))
        {
            rune = new System.Text.Rune(text[0]);
        }

        var language = ResolveLanguageHint(style, rune);
        var key = new FallbackKey(rune.Value, style.FontWeight, style.FontStyle, language);
        if (_fallbackCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var embedded = ResolveEmbeddedFallback(rune, style);
        if (embedded is not null)
        {
            _fallbackCache[key] = embedded;
            return embedded;
        }

        var weight = style.FontWeight == DocFontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.FontStyle == DocFontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var bcp47 = string.IsNullOrWhiteSpace(language) ? null : new[] { language };
        var family = string.IsNullOrWhiteSpace(style.FontFamily) ? null : style.FontFamily;

        var manager = SKFontManager.Default;
        var typeface = manager.MatchCharacter(family, weight, SKFontStyleWidth.Normal, slant, bcp47, rune.Value)
                       ?? manager.MatchCharacter(null, weight, SKFontStyleWidth.Normal, slant, bcp47, rune.Value);

        _fallbackCache[key] = typeface;
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
            var typeface = GetEmbeddedTypeface(embedded);
            if (typeface is not null)
            {
                return typeface;
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.AltName))
        {
            return ResolveSystemTypeface(style, definition.AltName);
        }

        return null;
    }

    private SKTypeface? ResolveEmbeddedFallback(System.Text.Rune rune, TextStyle style)
    {
        if (_fonts.FontTable.Count == 0)
        {
            return null;
        }

        Span<int> codepoints = stackalloc int[1];
        codepoints[0] = rune.Value;

        foreach (var definition in _fonts.FontTable.Values)
        {
            var embedded = definition.GetEmbeddedFont(style.FontWeight, style.FontStyle);
            if (embedded is null)
            {
                continue;
            }

            var typeface = GetEmbeddedTypeface(embedded);
            if (typeface is null)
            {
                continue;
            }

            if (typeface.ContainsGlyphs(codepoints))
            {
                return typeface;
            }
        }

        return null;
    }

    private SKTypeface? GetEmbeddedTypeface(EmbeddedFontData embedded)
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
        }

        return typeface;
    }

    private static string? ResolveLanguageHint(TextStyle style, System.Text.Rune rune)
    {
        var language = style.Language;
        var code = rune.Value;
        if (IsRtlCodepoint(code))
        {
            language = style.LanguageBidi ?? language;
        }
        else if (IsEastAsianCodepoint(code))
        {
            language = style.LanguageEastAsia ?? language;
        }

        return language;
    }

    private static bool IsRtlCodepoint(int code)
    {
        return (code >= 0x0590 && code <= 0x08FF)
               || (code >= 0xFB1D && code <= 0xFDFF)
               || (code >= 0xFE70 && code <= 0xFEFF);
    }

    private static bool IsEastAsianCodepoint(int code)
    {
        return (code >= 0x1100 && code <= 0x11FF)
               || (code >= 0x3040 && code <= 0x30FF)
               || (code >= 0x31F0 && code <= 0x31FF)
               || (code >= 0x3130 && code <= 0x318F)
               || (code >= 0x3300 && code <= 0x33FF)
               || (code >= 0x3400 && code <= 0x4DBF)
               || (code >= 0x4E00 && code <= 0x9FFF)
               || (code >= 0xA960 && code <= 0xA97F)
               || (code >= 0xAC00 && code <= 0xD7AF)
               || (code >= 0xD7B0 && code <= 0xD7FF)
               || (code >= 0xF900 && code <= 0xFAFF)
               || (code >= 0xFE30 && code <= 0xFE4F)
               || (code >= 0x20000 && code <= 0x2A6DF)
               || (code >= 0x2A700 && code <= 0x2B73F)
               || (code >= 0x2B740 && code <= 0x2B81F)
               || (code >= 0x2B820 && code <= 0x2CEAF)
               || (code >= 0x2CEB0 && code <= 0x2EBEF)
               || (code >= 0x2F800 && code <= 0x2FA1F);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var disposedTypefaces = new HashSet<SKTypeface>();
        DisposeTypefaces(_cache.Values, disposedTypefaces);
        DisposeTypefaces(_embeddedCache.Values, disposedTypefaces);
        DisposeTypefaces(_fallbackCache.Values, disposedTypefaces);

        foreach (var data in _embeddedData.Values)
        {
            data.Dispose();
        }

        _cache.Clear();
        _embeddedCache.Clear();
        _fallbackCache.Clear();
        _embeddedData.Clear();
    }

    private static void DisposeTypefaces(IEnumerable<SKTypeface?> typefaces, HashSet<SKTypeface> disposed)
    {
        foreach (var typeface in typefaces)
        {
            if (typeface is null || ReferenceEquals(typeface, SKTypeface.Default))
            {
                continue;
            }

            if (!disposed.Add(typeface))
            {
                continue;
            }

            typeface.Dispose();
        }
    }

    private readonly record struct FontKey(string Family, DocFontWeight Weight, DocFontStyle Style);

    private readonly record struct FallbackKey(int Codepoint, DocFontWeight Weight, DocFontStyle Style, string? Language);
}
