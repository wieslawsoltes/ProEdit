using System.Globalization;

namespace Vibe.Office.Documents;

public static class DocumentDefaults
{
    private const float PointsToDipScale = 96f / 72f;
    private const float LetterWidthPoints = 612f;
    private const float LetterHeightPoints = 792f;
    private const float A4WidthPoints = 595f;
    private const float A4HeightPoints = 842f;
    private const float DefaultMarginPoints = 72f;
    private const float DefaultHeaderFooterPoints = 36f;
    private static readonly bool UseMetricUnitsValue = ResolveUseMetric();

    public static bool UseMetricUnits => UseMetricUnitsValue;

    public static PageSetupDefaults ResolvePageSetup()
    {
        var widthPoints = UseMetricUnitsValue ? A4WidthPoints : LetterWidthPoints;
        var heightPoints = UseMetricUnitsValue ? A4HeightPoints : LetterHeightPoints;
        var margin = DefaultMarginPoints * PointsToDipScale;
        var headerFooter = DefaultHeaderFooterPoints * PointsToDipScale;

        return new PageSetupDefaults(
            widthPoints * PointsToDipScale,
            heightPoints * PointsToDipScale,
            margin,
            margin,
            margin,
            margin,
            headerFooter,
            headerFooter);
    }

    public static void ApplyDefaultPageSetup(SectionProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var defaults = ResolvePageSetup();
        properties.PageWidth ??= defaults.PageWidth;
        properties.PageHeight ??= defaults.PageHeight;
        properties.MarginLeft ??= defaults.MarginLeft;
        properties.MarginRight ??= defaults.MarginRight;
        properties.MarginTop ??= defaults.MarginTop;
        properties.MarginBottom ??= defaults.MarginBottom;
        properties.HeaderOffset ??= defaults.HeaderOffset;
        properties.FooterOffset ??= defaults.FooterOffset;
    }

    private static bool ResolveUseMetric()
    {
        try
        {
            var cultureName = CultureInfo.CurrentCulture.Name;
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return false;
            }

            return new RegionInfo(cultureName).IsMetric;
        }
        catch
        {
            return false;
        }
    }
}

public readonly record struct PageSetupDefaults(
    float PageWidth,
    float PageHeight,
    float MarginLeft,
    float MarginRight,
    float MarginTop,
    float MarginBottom,
    float HeaderOffset,
    float FooterOffset);
