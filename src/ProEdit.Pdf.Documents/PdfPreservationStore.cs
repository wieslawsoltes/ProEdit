using System.Text.Json;
using System.Xml.Linq;
using ProEdit.Documents;
using ProEdit.Pdf;

namespace ProEdit.Pdf.Documents;

public sealed record PdfPreservedData(
    byte[] Bytes,
    PdfPreservationManifest Manifest);

public static class PdfPreservationStore
{
    private const string PartName = "proedit:pdf-preserve";
    private const string RootElement = "pdf-preserve";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static void StoreOriginal(
        Document document,
        byte[] bytes,
        PdfPreservationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(manifest);

        var payload = Convert.ToBase64String(bytes);
        var manifestJson = JsonSerializer.Serialize(manifest, SerializerOptions);
        var xml = new XDocument(
            new XElement(RootElement,
                new XAttribute("version", manifest.Version),
                new XAttribute("importMode", manifest.ImportMode.ToString()),
                new XAttribute("preservationMode", manifest.PreservationMode.ToString()),
                new XAttribute("parser", manifest.ParserProviderId ?? string.Empty),
                new XAttribute("writer", manifest.WriterProviderId ?? string.Empty),
                new XElement("hash", manifest.ContentHash ?? string.Empty),
                new XElement("manifest", new XAttribute("format", "json"), manifestJson),
                new XElement("data", payload)));

        document.CustomXmlParts[PartName] = xml;
    }

    public static bool TryRead(Document document, out PdfPreservedData? data)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.CustomXmlParts.TryGetValue(PartName, out var xml) || xml.Root is null)
        {
            data = null;
            return false;
        }

        var dataElement = xml.Root.Element("data");
        if (dataElement is null || string.IsNullOrWhiteSpace(dataElement.Value))
        {
            data = null;
            return false;
        }

        var bytes = Convert.FromBase64String(dataElement.Value);
        var manifest = ReadManifest(xml.Root);
        data = new PdfPreservedData(bytes, manifest);
        return true;
    }

    private static PdfPreservationManifest ReadManifest(XElement root)
    {
        var manifestElement = root.Element("manifest");
        if (!string.IsNullOrWhiteSpace(manifestElement?.Value))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<PdfPreservationManifest>(manifestElement.Value, SerializerOptions);
                if (parsed is not null)
                {
                    if (string.IsNullOrWhiteSpace(parsed.ContentHash))
                    {
                        var fallbackHashElement = root.Element("hash");
                        parsed.ContentHash = string.IsNullOrWhiteSpace(fallbackHashElement?.Value) ? null : fallbackHashElement.Value;
                    }
                    return parsed;
                }
            }
            catch
            {
                // ignore and fall back to attributes
            }
        }

        var manifest = new PdfPreservationManifest();
        if (int.TryParse(root.Attribute("version")?.Value, out var version))
        {
            manifest.Version = version;
        }

        if (Enum.TryParse(root.Attribute("importMode")?.Value, out PdfImportMode importMode))
        {
            manifest.ImportMode = importMode;
        }

        if (Enum.TryParse(root.Attribute("preservationMode")?.Value, out PdfPreservationMode preservationMode))
        {
            manifest.PreservationMode = preservationMode;
        }

        manifest.ParserProviderId = root.Attribute("parser")?.Value;
        manifest.WriterProviderId = root.Attribute("writer")?.Value;

        var hashElement = root.Element("hash");
        manifest.ContentHash = string.IsNullOrWhiteSpace(hashElement?.Value) ? null : hashElement.Value;

        return manifest;
    }
}
