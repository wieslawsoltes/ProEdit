using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

internal readonly struct TextStyleKey : IEquatable<TextStyleKey>
{
    private readonly string _fontFamily;
    private readonly float _fontSize;
    private readonly DocFontWeight _fontWeight;
    private readonly DocFontStyle _fontStyle;
    private readonly DocColor _color;
    private readonly bool _underline;
    private readonly bool _strikethrough;
    private readonly bool _hasHighlight;
    private readonly DocColor _highlight;
    private readonly string _language;

    public TextStyleKey(TextStyle style)
    {
        _fontFamily = style.FontFamily ?? string.Empty;
        _fontSize = style.FontSize;
        _fontWeight = style.FontWeight;
        _fontStyle = style.FontStyle;
        _color = style.Color;
        _underline = style.Underline;
        _strikethrough = style.Strikethrough;
        _hasHighlight = style.HighlightColor.HasValue;
        _highlight = style.HighlightColor ?? default;
        _language = style.Language ?? string.Empty;
    }

    public bool Equals(TextStyleKey other)
    {
        return _fontFamily == other._fontFamily
            && _fontSize.Equals(other._fontSize)
            && _fontWeight == other._fontWeight
            && _fontStyle == other._fontStyle
            && _color.Equals(other._color)
            && _underline == other._underline
            && _strikethrough == other._strikethrough
            && _hasHighlight == other._hasHighlight
            && (!_hasHighlight || _highlight.Equals(other._highlight))
            && _language == other._language;
    }

    public override bool Equals(object? obj) => obj is TextStyleKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(
            _fontFamily,
            _fontSize,
            (int)_fontWeight,
            (int)_fontStyle,
            _color,
            _underline,
            _strikethrough,
            _hasHighlight ? _highlight.GetHashCode() : 0);
        return HashCode.Combine(hash, _language);
    }
}
