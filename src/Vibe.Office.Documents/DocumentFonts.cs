namespace Vibe.Office.Documents;

public sealed class DocumentFonts
{
    public Dictionary<string, DocumentFontDefinition> FontTable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DocumentThemeFontMap Theme { get; } = new DocumentThemeFontMap();
}

public sealed class DocumentThemeFontMap
{
    private readonly Dictionary<DocThemeFont, string> _fonts = new();

    public bool HasValues => _fonts.Count > 0;

    public void Set(DocThemeFont font, string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        _fonts[font] = family;
    }

    public bool TryGet(DocThemeFont font, out string family)
    {
        return _fonts.TryGetValue(font, out family!);
    }

    public string? Get(DocThemeFont font)
    {
        return _fonts.TryGetValue(font, out var family) ? family : null;
    }
}

public sealed class DocumentFontDefinition
{
    public DocumentFontDefinition(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Font name is required.", nameof(name)) : name;
    }

    public string Name { get; }
    public string? AltName { get; set; }
    public string? Charset { get; set; }
    public string? Family { get; set; }
    public string? Pitch { get; set; }
    public string? Panose1 { get; set; }
    public EmbeddedFontData? Regular { get; set; }
    public EmbeddedFontData? Bold { get; set; }
    public EmbeddedFontData? Italic { get; set; }
    public EmbeddedFontData? BoldItalic { get; set; }

    public EmbeddedFontData? GetEmbeddedFont(DocFontWeight weight, DocFontStyle style)
    {
        var isBold = weight == DocFontWeight.Bold;
        var isItalic = style == DocFontStyle.Italic;
        if (isBold && isItalic && BoldItalic is not null)
        {
            return BoldItalic;
        }

        if (isBold && Bold is not null)
        {
            return Bold;
        }

        if (isItalic && Italic is not null)
        {
            return Italic;
        }

        return Regular ?? Bold ?? Italic ?? BoldItalic;
    }
}

public sealed class EmbeddedFontData
{
    public EmbeddedFontData(byte[] data, string? contentType, string? fontKey)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        ContentType = contentType;
        FontKey = fontKey;
    }

    public byte[] Data { get; }
    public string? ContentType { get; }
    public string? FontKey { get; }
}
