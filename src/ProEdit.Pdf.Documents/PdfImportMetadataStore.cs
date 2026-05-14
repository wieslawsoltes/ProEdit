using System.Xml.Linq;
using ProEdit.Documents;
using ProEdit.Pdf;

namespace ProEdit.Pdf.Documents;

public sealed record PdfImportMetadata(
    PdfImportMode ImportMode,
    string? ParserProviderId,
    int PageCount);

public static class PdfImportMetadataStore
{
    private const string PartName = "proedit:pdf-import";
    private const string RootElement = "pdf-import";

    public static void Store(Document document, PdfImportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);

        var xml = new XDocument(
            new XElement(RootElement,
                new XAttribute("importMode", metadata.ImportMode.ToString()),
                new XAttribute("parser", metadata.ParserProviderId ?? string.Empty),
                new XAttribute("pageCount", metadata.PageCount)));

        document.CustomXmlParts[PartName] = xml;
    }

    public static bool TryRead(Document document, out PdfImportMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.CustomXmlParts.TryGetValue(PartName, out var xml) || xml.Root is null)
        {
            metadata = null;
            return false;
        }

        if (!Enum.TryParse(xml.Root.Attribute("importMode")?.Value, out PdfImportMode importMode))
        {
            importMode = PdfImportMode.Reflow;
        }

        var parserId = xml.Root.Attribute("parser")?.Value;
        var pageCountText = xml.Root.Attribute("pageCount")?.Value;
        var pageCount = int.TryParse(pageCountText, out var parsed) ? parsed : 0;

        metadata = new PdfImportMetadata(importMode, parserId, pageCount);
        return true;
    }
}
