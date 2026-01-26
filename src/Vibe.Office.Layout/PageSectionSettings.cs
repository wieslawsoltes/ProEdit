using Vibe.Office.Documents;
using Vibe.Office.Primitives;

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
    IReadOnlyList<float> ColumnGaps,
    DocGridSettings? DocGrid,
    DocColor? PageBackgroundColor,
    PageBorders? PageBorders,
    LineNumberingSettings? LineNumbering,
    PageNumberingSettings? PageNumbering,
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
            Array.Empty<float>(),
            null,
            null,
            null,
            null,
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
        var columnGaps = properties.ColumnGaps.Count > 0
            ? properties.ColumnGaps.ToArray()
            : ColumnGaps.ToArray();
        var columnCount = properties.ColumnCount
            ?? (properties.ColumnWidths.Count > 0
                ? properties.ColumnWidths.Count
                : properties.ColumnGaps.Count > 0
                    ? properties.ColumnGaps.Count + 1
                    : ColumnCount);
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
            columnGaps,
            properties.DocGrid?.Clone() ?? DocGrid,
            properties.PageBackgroundColor ?? PageBackgroundColor,
            properties.PageBorders?.Clone() ?? PageBorders?.Clone(),
            properties.LineNumbering?.Clone() ?? LineNumbering?.Clone(),
            properties.PageNumbering?.Clone() ?? PageNumbering?.Clone(),
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

        if (left < 0f)
        {
            left = 0f;
        }

        if (right < 0f)
        {
            right = 0f;
        }

        if (top < 0f)
        {
            top = 0f;
        }

        if (bottom < 0f)
        {
            bottom = 0f;
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
