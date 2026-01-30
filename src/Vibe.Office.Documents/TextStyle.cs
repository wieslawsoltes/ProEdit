using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public enum DocFontWeight
{
    Normal = 400,
    Bold = 700
}

public enum DocFontStyle
{
    Normal,
    Italic
}

public sealed class TextStyle
{
    public string FontFamily { get; set; } = "Segoe UI";
    public string? FontFamilyAscii { get; set; }
    public string? FontFamilyHighAnsi { get; set; }
    public string? FontFamilyEastAsia { get; set; }
    public string? FontFamilyComplexScript { get; set; }
    public float FontSize { get; set; } = 14f;
    public float? FontSizeComplexScript { get; set; }
    public DocFontWeight FontWeight { get; set; } = DocFontWeight.Normal;
    public DocFontStyle FontStyle { get; set; } = DocFontStyle.Normal;
    public DocColor Color { get; set; } = DocColor.Black;
    public DocThemeColor? ThemeColor { get; set; }
    public byte? ThemeTint { get; set; }
    public byte? ThemeShade { get; set; }
    public DocVerticalPosition VerticalPosition { get; set; } = DocVerticalPosition.Normal;
    public float BaselineOffset { get; set; }
    public float LetterSpacing { get; set; }
    public float HorizontalScale { get; set; } = 1f;
    public float? Kerning { get; set; }
    public bool Caps { get; set; }
    public bool SmallCaps { get; set; }
    public bool Underline { get; set; }
    public DocUnderlineStyle UnderlineStyle { get; set; } = DocUnderlineStyle.None;
    public DocColor? UnderlineColor { get; set; }
    public DocThemeColor? UnderlineThemeColor { get; set; }
    public byte? UnderlineThemeTint { get; set; }
    public byte? UnderlineThemeShade { get; set; }
    public bool Strikethrough { get; set; }
    public DocColor? HighlightColor { get; set; }
    public bool Hidden { get; set; }
    public DocThemeFont? ThemeFontAscii { get; set; }
    public DocThemeFont? ThemeFontHighAnsi { get; set; }
    public DocThemeFont? ThemeFontEastAsia { get; set; }
    public DocThemeFont? ThemeFontComplexScript { get; set; }
    public string? Language { get; set; }
    public string? LanguageEastAsia { get; set; }
    public string? LanguageBidi { get; set; }
    public DocTextDirection? TextDirection { get; set; }
    public EastAsianLayoutProperties? EastAsianLayout { get; set; }
    public TextOpenTypeFeatures? OpenTypeFeatures { get; set; }
    public TextEffects? Effects { get; set; }

    public TextStyle Clone()
    {
        return new TextStyle
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
            TextDirection = TextDirection,
            EastAsianLayout = EastAsianLayout?.Clone(),
            OpenTypeFeatures = OpenTypeFeatures?.Clone(),
            Effects = Effects?.Clone()
        };
    }
}
