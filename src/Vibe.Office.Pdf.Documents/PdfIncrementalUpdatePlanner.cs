using Vibe.Office.Documents;
using Vibe.Office.Pdf;

namespace Vibe.Office.Pdf.Documents;

public static class PdfIncrementalUpdatePlanner
{
    public static PdfIncrementalUpdatePlan Build(Document document, PdfPreservedData preservedData)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preservedData);

        var plan = new PdfIncrementalUpdatePlan();
        var manifest = preservedData.Manifest;

        if (manifest.ObjectMap.Pages.Count == 0)
        {
            plan.Issues.Add("No PDF object map is available for incremental updates.");
        }
        else if (manifest.PageCount > 0 && manifest.ObjectMap.Pages.Count < manifest.PageCount)
        {
            plan.Issues.Add($"PDF object map covers {manifest.ObjectMap.Pages.Count} of {manifest.PageCount} pages.");
        }

        if (manifest.ImportMode != PdfImportMode.FixedLayout)
        {
            plan.Issues.Add("Incremental updates require fixed layout imports.");
        }

        var currentHash = PdfDocumentHash.Compute(document);
        plan.HasChanges = !string.Equals(currentHash, manifest.ContentHash, StringComparison.Ordinal);

        if (!plan.HasChanges)
        {
            plan.CanApply = true;
            return plan;
        }

        if (manifest.PageCount <= 0)
        {
            plan.Issues.Add("Missing page count for incremental overlay planning.");
            plan.CanApply = false;
            return plan;
        }

        for (var pageIndex = 0; pageIndex < manifest.PageCount; pageIndex++)
        {
            plan.Overlays.Add(new PdfIncrementalOverlay
            {
                PageIndex = pageIndex,
                Kind = PdfIncrementalOverlayKind.Image,
                Description = "Document changes overlay"
            });
        }

        plan.CanApply = plan.Issues.Count == 0;
        return plan;
    }
}
