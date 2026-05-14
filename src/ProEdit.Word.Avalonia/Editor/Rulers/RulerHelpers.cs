using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;

namespace ProEdit.Word.Avalonia;

internal static class RulerHelpers
{
    public const float DipPerInch = 96f;
    public const float DipPerCentimeter = DipPerInch / 2.54f;
    public static readonly bool UseMetricUnits = DocumentDefaults.UseMetricUnits;
    public static readonly float MinorTick = UseMetricUnits ? DipPerCentimeter / 10f : DipPerInch / 8f;
    public static readonly float MajorTick = UseMetricUnits ? DipPerCentimeter / 2f : DipPerInch / 2f;
    public static readonly float LabelTick = UseMetricUnits ? DipPerCentimeter : DipPerInch;
    public static readonly int MinorTicksPerMajor = UseMetricUnits ? 5 : 4;
    public static readonly int MinorTicksPerLabel = UseMetricUnits ? 10 : 8;
    public const float MinContentSize = 36f;
    public const float MinColumnWidth = 12f;

    public static bool TryGetCurrentPage(
        DocumentView view,
        out DocumentLayout layout,
        out PageLayout page,
        out PageSectionSettings pageSection,
        out PageSectionSettings baseSection,
        out int pageIndex,
        out int sectionIndex)
    {
        layout = view.Layout;
        page = default!;
        pageSection = default!;
        baseSection = default!;
        pageIndex = 0;
        sectionIndex = 0;

        if (layout.Pages.Count == 0)
        {
            return false;
        }

        pageIndex = ResolvePageIndex(layout, view.Caret);
        pageIndex = Math.Clamp(pageIndex, 0, layout.Pages.Count - 1);
        page = layout.Pages[pageIndex];
        if (layout.PageSections.Count > 0)
        {
            var safePageIndex = Math.Clamp(pageIndex, 0, layout.PageSections.Count - 1);
            pageSection = layout.PageSections[safePageIndex];
        }
        else
        {
            pageSection = PageSectionSettings.FromSettings(layout.Settings, null, 0);
        }

        sectionIndex = pageSection.SectionIndex;
        if (view.Document.ParagraphCount > 0)
        {
            var paragraphIndex = Math.Clamp(view.Caret.ParagraphIndex, 0, view.Document.ParagraphCount - 1);
            if (layout.ParagraphSectionIndices.TryGetValue(paragraphIndex, out var index))
            {
                sectionIndex = index;
            }
        }

        if (layout.SectionSettings.TryGetValue(sectionIndex, out var section))
        {
            baseSection = section;
        }
        else
        {
            baseSection = pageSection;
        }

        return true;
    }

    public static int ResolvePageIndex(DocumentLayout layout, TextPosition caret)
    {
        if (layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, caret, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 0 : pageIndex;
    }

    public static void ResolveMargins(
        PageSectionSettings section,
        int pageIndex,
        out float left,
        out float right,
        out float top,
        out float bottom)
    {
        left = section.MarginLeft;
        right = section.MarginRight;
        top = section.MarginTop;
        bottom = section.MarginBottom;

        var isEvenPage = (pageIndex + 1) % 2 == 0;
        if (section.MirrorMargins && isEvenPage)
        {
            (left, right) = (right, left);
        }

        if (MathF.Abs(section.Gutter) > 0.01f)
        {
            if (section.GutterAtTop)
            {
                top += section.Gutter;
            }
            else if (section.MirrorMargins && isEvenPage)
            {
                right += section.Gutter;
            }
            else
            {
                left += section.Gutter;
            }
        }
    }

    public static bool TryGetTableLayout(Document document, DocumentLayout layout, int paragraphIndex, out TableLayout tableLayout)
    {
        tableLayout = null!;
        if (document.ParagraphCount == 0)
        {
            return false;
        }

        var safeIndex = Math.Clamp(paragraphIndex, 0, document.ParagraphCount - 1);
        var location = document.GetParagraphLocation(safeIndex);
        if (!location.IsInTable || location.Table is null)
        {
            return false;
        }

        var tableIndex = 0;
        foreach (var block in document.Blocks)
        {
            if (block is not TableBlock table)
            {
                continue;
            }

            if (ReferenceEquals(table, location.Table))
            {
                if (tableIndex < layout.Tables.Count)
                {
                    tableLayout = layout.Tables[tableIndex];
                    return true;
                }

                return false;
            }

            tableIndex++;
        }

        return false;
    }

    public static bool ExecuteCommand(DocumentView view, string commandId, object? payload, bool recordHistory = true)
    {
        if (!view.TryGetService<IEditorCommandRouter>(out var router))
        {
            return false;
        }

        return router.ExecuteAsync(commandId, payload, recordHistory: recordHistory).GetAwaiter().GetResult();
    }
}
