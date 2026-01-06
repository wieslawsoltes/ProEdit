using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TextStyleProperties
{
    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }
    public DocFontWeight? FontWeight { get; set; }
    public DocFontStyle? FontStyle { get; set; }
    public DocColor? Color { get; set; }
    public DocVerticalPosition? VerticalPosition { get; set; }
    public bool? SmallCaps { get; set; }
    public bool? Underline { get; set; }
    public DocUnderlineStyle? UnderlineStyle { get; set; }
    public DocColor? UnderlineColor { get; set; }
    public bool? Strikethrough { get; set; }
    public DocColor? HighlightColor { get; set; }
    public DocThemeFont? ThemeFontAscii { get; set; }
    public DocThemeFont? ThemeFontHighAnsi { get; set; }
    public DocThemeFont? ThemeFontEastAsia { get; set; }
    public DocThemeFont? ThemeFontComplexScript { get; set; }

    public bool HasValues => !string.IsNullOrWhiteSpace(FontFamily)
                             || FontSize.HasValue
                             || FontWeight.HasValue
                             || FontStyle.HasValue
                             || Color.HasValue
                             || VerticalPosition.HasValue
                             || SmallCaps.HasValue
                             || Underline.HasValue
                             || UnderlineStyle.HasValue
                             || UnderlineColor.HasValue
                             || Strikethrough.HasValue
                             || HighlightColor.HasValue
                             || ThemeFontAscii.HasValue
                             || ThemeFontHighAnsi.HasValue
                             || ThemeFontEastAsia.HasValue
                             || ThemeFontComplexScript.HasValue;

    public TextStyleProperties Clone()
    {
        return new TextStyleProperties
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontStyle = FontStyle,
            Color = Color,
            VerticalPosition = VerticalPosition,
            SmallCaps = SmallCaps,
            Underline = Underline,
            UnderlineStyle = UnderlineStyle,
            UnderlineColor = UnderlineColor,
            Strikethrough = Strikethrough,
            HighlightColor = HighlightColor,
            ThemeFontAscii = ThemeFontAscii,
            ThemeFontHighAnsi = ThemeFontHighAnsi,
            ThemeFontEastAsia = ThemeFontEastAsia,
            ThemeFontComplexScript = ThemeFontComplexScript
        };
    }

    public bool IsEquivalentTo(TextStyleProperties? other)
    {
        if (other is null)
        {
            return !HasValues;
        }

        return string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
               && FontSize.Equals(other.FontSize)
               && FontWeight == other.FontWeight
               && FontStyle == other.FontStyle
               && Color.Equals(other.Color)
               && VerticalPosition == other.VerticalPosition
               && SmallCaps == other.SmallCaps
               && Underline == other.Underline
               && UnderlineStyle == other.UnderlineStyle
               && UnderlineColor.Equals(other.UnderlineColor)
               && Strikethrough == other.Strikethrough
               && HighlightColor.Equals(other.HighlightColor)
               && ThemeFontAscii == other.ThemeFontAscii
               && ThemeFontHighAnsi == other.ThemeFontHighAnsi
               && ThemeFontEastAsia == other.ThemeFontEastAsia
               && ThemeFontComplexScript == other.ThemeFontComplexScript;
    }

    public void ApplyTo(TextStyle style)
    {
        if (!string.IsNullOrWhiteSpace(FontFamily))
        {
            style.FontFamily = FontFamily;
        }

        if (FontSize.HasValue)
        {
            style.FontSize = FontSize.Value;
        }

        if (FontWeight.HasValue)
        {
            style.FontWeight = FontWeight.Value;
        }

        if (FontStyle.HasValue)
        {
            style.FontStyle = FontStyle.Value;
        }

        if (Color.HasValue)
        {
            style.Color = Color.Value;
        }

        if (VerticalPosition.HasValue)
        {
            style.VerticalPosition = VerticalPosition.Value;
        }

        if (SmallCaps.HasValue)
        {
            style.SmallCaps = SmallCaps.Value;
        }

        if (Underline.HasValue)
        {
            style.Underline = Underline.Value;
        }

        if (UnderlineStyle.HasValue)
        {
            style.UnderlineStyle = UnderlineStyle.Value;
            style.Underline = UnderlineStyle.Value != DocUnderlineStyle.None;
        }

        if (UnderlineColor.HasValue)
        {
            style.UnderlineColor = UnderlineColor;
        }

        if (Strikethrough.HasValue)
        {
            style.Strikethrough = Strikethrough.Value;
        }

        if (HighlightColor.HasValue)
        {
            style.HighlightColor = HighlightColor;
        }

        if (ThemeFontAscii.HasValue)
        {
            style.ThemeFontAscii = ThemeFontAscii;
        }

        if (ThemeFontHighAnsi.HasValue)
        {
            style.ThemeFontHighAnsi = ThemeFontHighAnsi;
        }

        if (ThemeFontEastAsia.HasValue)
        {
            style.ThemeFontEastAsia = ThemeFontEastAsia;
        }

        if (ThemeFontComplexScript.HasValue)
        {
            style.ThemeFontComplexScript = ThemeFontComplexScript;
        }
    }
}
