using System.Linq;
using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Pdf.Documents;

public sealed record PdfImportDiagnostics(IReadOnlyList<string> Issues);

public static class PdfImportDiagnosticsStore
{
    private const string PartName = "vibe:pdf-import-diagnostics";
    private const string RootElement = "pdf-import-diagnostics";

    public static void Store(Document document, PdfImportDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var root = new XElement(RootElement);
        foreach (var issue in diagnostics.Issues)
        {
            if (!string.IsNullOrWhiteSpace(issue))
            {
                root.Add(new XElement("issue", issue));
            }
        }

        document.CustomXmlParts[PartName] = new XDocument(root);
    }

    public static void Clear(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.CustomXmlParts.Remove(PartName);
    }

    public static bool TryRead(Document document, out PdfImportDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.CustomXmlParts.TryGetValue(PartName, out var xml) || xml.Root is null)
        {
            diagnostics = null;
            return false;
        }

        var issues = xml.Root.Elements("issue")
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        diagnostics = new PdfImportDiagnostics(issues);
        return true;
    }
}
