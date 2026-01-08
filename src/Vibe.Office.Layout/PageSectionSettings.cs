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
    float Gutter,
    bool GutterAtTop,
    bool MirrorMargins,
    int ColumnCount,
    float ColumnGap,
    bool ColumnSeparator,
    bool ColumnEqualWidth,
    IReadOnlyList<float> ColumnWidths,
    DocGridSettings? DocGrid,
    int SectionIndex)
{
    public static PageSectionSettings FromSettings(
        LayoutSettings settings,
        SectionProperties? properties,
        int sectionIndex,
        bool mirrorMargins = false,
        bool gutterAtTop = false)
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
            settings.Gutter,
            gutterAtTop,
            mirrorMargins,
            1,
            settings.ColumnGap,
            false,
            true,
            Array.Empty<float>(),
            null,
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
        var columnSeparator = properties.ColumnSeparator ?? ColumnSeparator;
        var columnEqualWidth = properties.ColumnEqualWidth
            ?? (columnWidths.Length == 0 && columnCount > 1);
        var gutter = properties.Gutter ?? Gutter;

        return new PageSectionSettings(
            properties.PageWidth ?? PageWidth,
            properties.PageHeight ?? PageHeight,
            properties.MarginLeft ?? MarginLeft,
            properties.MarginTop ?? MarginTop,
            properties.MarginRight ?? MarginRight,
            properties.MarginBottom ?? MarginBottom,
            properties.HeaderOffset ?? HeaderOffset,
            properties.FooterOffset ?? FooterOffset,
            gutter,
            GutterAtTop,
            MirrorMargins,
            columnCount,
            columnGap,
            columnSeparator,
            columnEqualWidth,
            columnWidths,
            properties.DocGrid?.Clone() ?? DocGrid,
            SectionIndex);
    }

    public PageSectionSettings ResolveForPage(int pageIndex)
    {
        if (!MirrorMargins && !GutterAtTop && MathF.Abs(Gutter) < 0.01f)
        {
            return this;
        }

        var left = MarginLeft;
        var right = MarginRight;
        var top = MarginTop;
        var bottom = MarginBottom;
        var gutter = Gutter;
        var isEvenPage = (pageIndex + 1) % 2 == 0;

        if (MirrorMargins && isEvenPage)
        {
            (left, right) = (right, left);
        }

        if (MathF.Abs(gutter) > 0.01f)
        {
            if (GutterAtTop)
            {
                top += gutter;
            }
            else
            {
                if (MirrorMargins && isEvenPage)
                {
                    right += gutter;
                }
                else
                {
                    left += gutter;
                }
            }
        }

        if (left == MarginLeft
            && right == MarginRight
            && top == MarginTop
            && bottom == MarginBottom)
        {
            return this;
        }

        return this with
        {
            MarginLeft = left,
            MarginRight = right,
            MarginTop = top,
            MarginBottom = bottom
        };
    }
}
