using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record PageSectionSettings(
    float PageWidth,
    float PageHeight,
    float MarginLeft,
    float MarginTop,
    float MarginRight,
    float MarginBottom,
    float HeaderOffset,
    float FooterOffset,
    int ColumnCount,
    float ColumnGap,
    bool ColumnEqualWidth,
    IReadOnlyList<float> ColumnWidths,
    int SectionIndex)
{
    public static PageSectionSettings FromSettings(LayoutSettings settings, SectionProperties? properties, int sectionIndex)
    {
        var defaults = new PageSectionSettings(
            settings.PageWidth,
            settings.PageHeight,
            settings.MarginLeft,
            settings.MarginTop,
            settings.MarginRight,
            settings.MarginBottom,
            settings.HeaderOffset,
            settings.FooterOffset,
            1,
            settings.ColumnGap,
            true,
            Array.Empty<float>(),
            sectionIndex);

        return defaults.ApplyOverrides(properties);
    }

    public PageSectionSettings ApplyOverrides(SectionProperties? properties)
    {
        if (properties is null)
        {
            return this;
        }

        var columnWidths = properties.ColumnWidths.Count > 0
            ? properties.ColumnWidths.ToArray()
            : ColumnWidths.ToArray();
        var columnCount = properties.ColumnCount
            ?? (properties.ColumnWidths.Count > 0 ? properties.ColumnWidths.Count : ColumnCount);
        columnCount = Math.Max(1, columnCount);
        var columnGap = properties.ColumnGap ?? ColumnGap;
        var columnEqualWidth = properties.ColumnEqualWidth
            ?? (columnWidths.Length == 0 && columnCount > 1);

        return new PageSectionSettings(
            properties.PageWidth ?? PageWidth,
            properties.PageHeight ?? PageHeight,
            properties.MarginLeft ?? MarginLeft,
            properties.MarginTop ?? MarginTop,
            properties.MarginRight ?? MarginRight,
            properties.MarginBottom ?? MarginBottom,
            properties.HeaderOffset ?? HeaderOffset,
            properties.FooterOffset ?? FooterOffset,
            columnCount,
            columnGap,
            columnEqualWidth,
            columnWidths,
            SectionIndex);
    }
}
