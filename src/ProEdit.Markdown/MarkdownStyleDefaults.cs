namespace ProEdit.Markdown;

internal static class MarkdownStyleDefaults
{
    internal const float PointsToDipScale = 96f / 72f;
    internal const float NormalFontSizePoints = 12f;
    internal const float CodeFontSizePoints = 11f;
    internal const int LineSpacingTwips = 360;
    internal const float ParagraphSpacingAfterDips = 16f;
    internal const float HeadingSpacingBeforeDips = 24f;
    internal const float HeadingSpacingAfterDips = 16f;
    internal const float BlockQuoteIndentDips = 16f;
    internal const float BlockQuoteSpacingBeforeDips = 16f;
    internal const float BlockQuoteSpacingAfterDips = 16f;
    internal const float CodeBlockIndentDips = 16f;
    internal const float CodeBlockSpacingBeforeDips = 16f;
    internal const float CodeBlockSpacingAfterDips = 16f;
    internal const float TableCellPaddingVerticalDips = 6f;
    internal const float TableCellPaddingHorizontalDips = 13f;
    internal const string CodeFontFamily = "Consolas";

    internal static readonly float[] HeadingFontSizesPoints =
    {
        24f,
        18f,
        15f,
        12f,
        10.5f,
        10f
    };

    internal static float PointsToDips(float points) => points * PointsToDipScale;
}
