using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Layout;

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
    private readonly int _textDirection;
    private readonly float _letterSpacing;
    private readonly float _horizontalScale;
    private readonly bool _hasLigatures;
    private readonly DocLigatureOptions _ligatures;
    private readonly bool _hasContextualAlternates;
    private readonly bool _contextualAlternates;
    private readonly bool _hasNumberForm;
    private readonly DocNumberForm _numberForm;
    private readonly bool _hasNumberSpacing;
    private readonly DocNumberSpacing _numberSpacing;
    private readonly bool _hasStylisticSets;
    private readonly uint _stylisticSets;

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
        _textDirection = style.TextDirection.HasValue ? (int)style.TextDirection.Value + 1 : 0;
        _letterSpacing = style.LetterSpacing;
        _horizontalScale = style.HorizontalScale;

        var features = style.OpenTypeFeatures;
        if (features is not null)
        {
            _hasLigatures = features.Ligatures.HasValue;
            _ligatures = features.Ligatures ?? DocLigatureOptions.None;
            _hasContextualAlternates = features.ContextualAlternates.HasValue;
            _contextualAlternates = features.ContextualAlternates ?? false;
            _hasNumberForm = features.NumberForm.HasValue;
            _numberForm = features.NumberForm ?? DocNumberForm.Default;
            _hasNumberSpacing = features.NumberSpacing.HasValue;
            _numberSpacing = features.NumberSpacing ?? DocNumberSpacing.Default;
            _hasStylisticSets = features.StylisticSets.HasValue;
            _stylisticSets = features.StylisticSets ?? 0u;
        }
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
            && _language == other._language
            && _textDirection == other._textDirection
            && _letterSpacing.Equals(other._letterSpacing)
            && _horizontalScale.Equals(other._horizontalScale)
            && _hasLigatures == other._hasLigatures
            && (!_hasLigatures || _ligatures == other._ligatures)
            && _hasContextualAlternates == other._hasContextualAlternates
            && (!_hasContextualAlternates || _contextualAlternates == other._contextualAlternates)
            && _hasNumberForm == other._hasNumberForm
            && (!_hasNumberForm || _numberForm == other._numberForm)
            && _hasNumberSpacing == other._hasNumberSpacing
            && (!_hasNumberSpacing || _numberSpacing == other._numberSpacing)
            && _hasStylisticSets == other._hasStylisticSets
            && (!_hasStylisticSets || _stylisticSets == other._stylisticSets);
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
        hash = HashCode.Combine(hash, _language, _textDirection, _letterSpacing, _horizontalScale);
        hash = HashCode.Combine(hash, _hasLigatures ? (int)_ligatures : 0, _hasContextualAlternates ? (_contextualAlternates ? 1 : 0) : 0);
        hash = HashCode.Combine(hash, _hasNumberForm ? (int)_numberForm : 0, _hasNumberSpacing ? (int)_numberSpacing : 0);
        return HashCode.Combine(hash, _hasStylisticSets ? (int)_stylisticSets : 0);
    }
}
