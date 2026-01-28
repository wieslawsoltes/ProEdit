using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TextStyleProperties
{
    public string? FontFamily { get; set; }
    public string? FontFamilyAscii { get; set; }
    public string? FontFamilyHighAnsi { get; set; }
    public string? FontFamilyEastAsia { get; set; }
    public string? FontFamilyComplexScript { get; set; }
    public float? FontSize { get; set; }
    public float? FontSizeComplexScript { get; set; }
    public DocFontWeight? FontWeight { get; set; }
    public DocFontStyle? FontStyle { get; set; }
    public DocColor? Color { get; set; }
    public DocThemeColor? ThemeColor { get; set; }
    public byte? ThemeTint { get; set; }
    public byte? ThemeShade { get; set; }
    public DocVerticalPosition? VerticalPosition { get; set; }
    public float? BaselineOffset { get; set; }
    public float? LetterSpacing { get; set; }
    public float? HorizontalScale { get; set; }
    public float? Kerning { get; set; }
    public bool? Caps { get; set; }
    public bool? SmallCaps { get; set; }
    public bool? Underline { get; set; }
    public DocUnderlineStyle? UnderlineStyle { get; set; }
    public DocColor? UnderlineColor { get; set; }
    public DocThemeColor? UnderlineThemeColor { get; set; }
    public byte? UnderlineThemeTint { get; set; }
    public byte? UnderlineThemeShade { get; set; }
    public bool? Strikethrough { get; set; }
    public DocColor? HighlightColor { get; set; }
    public bool? Hidden { get; set; }
    public DocThemeFont? ThemeFontAscii { get; set; }
    public DocThemeFont? ThemeFontHighAnsi { get; set; }
    public DocThemeFont? ThemeFontEastAsia { get; set; }
    public DocThemeFont? ThemeFontComplexScript { get; set; }
    public string? Language { get; set; }
    public string? LanguageEastAsia { get; set; }
    public string? LanguageBidi { get; set; }
    public EastAsianLayoutProperties? EastAsianLayout { get; set; }
    public TextOpenTypeFeatures? OpenTypeFeatures { get; set; }
    public TextEffects? Effects { get; set; }

    public bool HasValues => !string.IsNullOrWhiteSpace(FontFamily)
                             || !string.IsNullOrWhiteSpace(FontFamilyAscii)
                             || !string.IsNullOrWhiteSpace(FontFamilyHighAnsi)
                             || !string.IsNullOrWhiteSpace(FontFamilyEastAsia)
                             || !string.IsNullOrWhiteSpace(FontFamilyComplexScript)
                             || FontSize.HasValue
                             || FontSizeComplexScript.HasValue
                             || FontWeight.HasValue
                             || FontStyle.HasValue
                             || Color.HasValue
                             || ThemeColor.HasValue
                             || ThemeTint.HasValue
                             || ThemeShade.HasValue
                             || VerticalPosition.HasValue
                             || BaselineOffset.HasValue
                             || LetterSpacing.HasValue
                             || HorizontalScale.HasValue
                             || Kerning.HasValue
                             || Caps.HasValue
                             || SmallCaps.HasValue
                             || Underline.HasValue
                             || UnderlineStyle.HasValue
                             || UnderlineColor.HasValue
                             || UnderlineThemeColor.HasValue
                             || UnderlineThemeTint.HasValue
                             || UnderlineThemeShade.HasValue
                             || Strikethrough.HasValue
                             || HighlightColor.HasValue
                             || Hidden.HasValue
                             || ThemeFontAscii.HasValue
                             || ThemeFontHighAnsi.HasValue
                             || ThemeFontEastAsia.HasValue
                             || ThemeFontComplexScript.HasValue
                             || !string.IsNullOrWhiteSpace(Language)
                             || !string.IsNullOrWhiteSpace(LanguageEastAsia)
                             || !string.IsNullOrWhiteSpace(LanguageBidi)
                             || (EastAsianLayout?.HasValues ?? false)
                             || (OpenTypeFeatures?.HasValues ?? false)
                             || (Effects?.HasValues ?? false);

    public TextStyleProperties Clone()
    {
        return new TextStyleProperties
        {
            FontFamily = FontFamily,
            FontFamilyAscii = FontFamilyAscii,
            FontFamilyHighAnsi = FontFamilyHighAnsi,
            FontFamilyEastAsia = FontFamilyEastAsia,
            FontFamilyComplexScript = FontFamilyComplexScript,
            FontSize = FontSize,
            FontSizeComplexScript = FontSizeComplexScript,
            FontWeight = FontWeight,
            FontStyle = FontStyle,
            Color = Color,
            ThemeColor = ThemeColor,
            ThemeTint = ThemeTint,
            ThemeShade = ThemeShade,
            VerticalPosition = VerticalPosition,
            BaselineOffset = BaselineOffset,
            LetterSpacing = LetterSpacing,
            HorizontalScale = HorizontalScale,
            Kerning = Kerning,
            Caps = Caps,
            SmallCaps = SmallCaps,
            Underline = Underline,
            UnderlineStyle = UnderlineStyle,
            UnderlineColor = UnderlineColor,
            UnderlineThemeColor = UnderlineThemeColor,
            UnderlineThemeTint = UnderlineThemeTint,
            UnderlineThemeShade = UnderlineThemeShade,
            Strikethrough = Strikethrough,
            HighlightColor = HighlightColor,
            Hidden = Hidden,
            ThemeFontAscii = ThemeFontAscii,
            ThemeFontHighAnsi = ThemeFontHighAnsi,
            ThemeFontEastAsia = ThemeFontEastAsia,
            ThemeFontComplexScript = ThemeFontComplexScript,
            Language = Language,
            LanguageEastAsia = LanguageEastAsia,
            LanguageBidi = LanguageBidi,
            EastAsianLayout = EastAsianLayout?.Clone(),
            OpenTypeFeatures = OpenTypeFeatures?.Clone(),
            Effects = Effects?.Clone()
        };
    }

    public bool IsEquivalentTo(TextStyleProperties? other)
    {
        if (other is null)
        {
            return !HasValues;
        }

        return string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
               && string.Equals(FontFamilyAscii, other.FontFamilyAscii, StringComparison.Ordinal)
               && string.Equals(FontFamilyHighAnsi, other.FontFamilyHighAnsi, StringComparison.Ordinal)
               && string.Equals(FontFamilyEastAsia, other.FontFamilyEastAsia, StringComparison.Ordinal)
               && string.Equals(FontFamilyComplexScript, other.FontFamilyComplexScript, StringComparison.Ordinal)
               && FontSize.Equals(other.FontSize)
               && FontSizeComplexScript.Equals(other.FontSizeComplexScript)
               && FontWeight == other.FontWeight
               && FontStyle == other.FontStyle
               && Color.Equals(other.Color)
               && ThemeColor == other.ThemeColor
               && ThemeTint == other.ThemeTint
               && ThemeShade == other.ThemeShade
               && VerticalPosition == other.VerticalPosition
               && BaselineOffset.Equals(other.BaselineOffset)
               && LetterSpacing.Equals(other.LetterSpacing)
               && HorizontalScale.Equals(other.HorizontalScale)
               && Kerning.Equals(other.Kerning)
               && Caps == other.Caps
               && SmallCaps == other.SmallCaps
               && Underline == other.Underline
               && UnderlineStyle == other.UnderlineStyle
               && UnderlineColor.Equals(other.UnderlineColor)
               && UnderlineThemeColor == other.UnderlineThemeColor
               && UnderlineThemeTint == other.UnderlineThemeTint
               && UnderlineThemeShade == other.UnderlineThemeShade
               && Strikethrough == other.Strikethrough
               && HighlightColor.Equals(other.HighlightColor)
               && Hidden == other.Hidden
               && ThemeFontAscii == other.ThemeFontAscii
               && ThemeFontHighAnsi == other.ThemeFontHighAnsi
               && ThemeFontEastAsia == other.ThemeFontEastAsia
               && ThemeFontComplexScript == other.ThemeFontComplexScript
               && string.Equals(Language, other.Language, StringComparison.Ordinal)
               && string.Equals(LanguageEastAsia, other.LanguageEastAsia, StringComparison.Ordinal)
               && string.Equals(LanguageBidi, other.LanguageBidi, StringComparison.Ordinal)
               && Equals(EastAsianLayout, other.EastAsianLayout)
               && Equals(OpenTypeFeatures, other.OpenTypeFeatures)
               && Equals(Effects, other.Effects);
    }

    public void ApplyTo(TextStyle style)
    {
        if (!string.IsNullOrWhiteSpace(FontFamily))
        {
            style.FontFamily = FontFamily;
        }

        if (!string.IsNullOrWhiteSpace(FontFamilyAscii))
        {
            style.FontFamilyAscii = FontFamilyAscii;
        }

        if (!string.IsNullOrWhiteSpace(FontFamilyHighAnsi))
        {
            style.FontFamilyHighAnsi = FontFamilyHighAnsi;
        }

        if (!string.IsNullOrWhiteSpace(FontFamilyEastAsia))
        {
            style.FontFamilyEastAsia = FontFamilyEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(FontFamilyComplexScript))
        {
            style.FontFamilyComplexScript = FontFamilyComplexScript;
        }

        if (FontSize.HasValue)
        {
            style.FontSize = FontSize.Value;
        }

        if (FontSizeComplexScript.HasValue)
        {
            style.FontSizeComplexScript = FontSizeComplexScript.Value;
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
            if (!ThemeColor.HasValue)
            {
                style.ThemeColor = null;
                style.ThemeTint = null;
                style.ThemeShade = null;
            }
        }

        if (ThemeColor.HasValue)
        {
            style.ThemeColor = ThemeColor;
            style.ThemeTint = ThemeTint;
            style.ThemeShade = ThemeShade;
        }

        if (VerticalPosition.HasValue)
        {
            style.VerticalPosition = VerticalPosition.Value;
        }

        if (BaselineOffset.HasValue)
        {
            style.BaselineOffset = BaselineOffset.Value;
        }

        if (LetterSpacing.HasValue)
        {
            style.LetterSpacing = LetterSpacing.Value;
        }

        if (HorizontalScale.HasValue && HorizontalScale.Value > 0f)
        {
            style.HorizontalScale = HorizontalScale.Value;
        }

        if (Kerning.HasValue)
        {
            style.Kerning = Kerning.Value;
        }

        if (Caps.HasValue)
        {
            style.Caps = Caps.Value;
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
            if (!UnderlineThemeColor.HasValue)
            {
                style.UnderlineThemeColor = null;
                style.UnderlineThemeTint = null;
                style.UnderlineThemeShade = null;
            }
        }

        if (UnderlineThemeColor.HasValue)
        {
            style.UnderlineThemeColor = UnderlineThemeColor;
            style.UnderlineThemeTint = UnderlineThemeTint;
            style.UnderlineThemeShade = UnderlineThemeShade;
        }

        if (Strikethrough.HasValue)
        {
            style.Strikethrough = Strikethrough.Value;
        }

        if (HighlightColor.HasValue)
        {
            style.HighlightColor = HighlightColor;
        }

        if (Hidden.HasValue)
        {
            style.Hidden = Hidden.Value;
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

        if (!string.IsNullOrWhiteSpace(Language))
        {
            style.Language = Language;
        }

        if (!string.IsNullOrWhiteSpace(LanguageEastAsia))
        {
            style.LanguageEastAsia = LanguageEastAsia;
        }

        if (!string.IsNullOrWhiteSpace(LanguageBidi))
        {
            style.LanguageBidi = LanguageBidi;
        }

        if (EastAsianLayout?.HasValues == true)
        {
            style.EastAsianLayout = EastAsianLayout.Clone();
        }

        if (OpenTypeFeatures?.HasValues == true)
        {
            style.OpenTypeFeatures ??= new TextOpenTypeFeatures();
            style.OpenTypeFeatures.ApplyOverrides(OpenTypeFeatures);
        }

        if (Effects?.HasValues == true)
        {
            style.Effects ??= new TextEffects();
            style.Effects.ApplyOverrides(Effects);
        }
    }
}
