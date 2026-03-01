using Vibe.Office.Documents;
using Vibe.Office.Documents.Formats;
using Vibe.Office.OpenXml;

namespace Vibe.Office.FlowDocument.IO.Formats;

internal sealed class DocxDocumentFormat : IDocumentFormat
{
    private static readonly DocumentFormatProfile DocxProfile = new(
        "docx",
        "Word Document",
        DocumentFormatCapability.Paragraphs
        | DocumentFormatCapability.Headings
        | DocumentFormatCapability.Lists
        | DocumentFormatCapability.Tables
        | DocumentFormatCapability.Images
        | DocumentFormatCapability.Links
        | DocumentFormatCapability.Emphasis
        | DocumentFormatCapability.Strong
        | DocumentFormatCapability.Strikethrough
        | DocumentFormatCapability.HardLineBreaks);

    public string FormatId => "docx";

    public string DisplayName => "Word Document";

    public IReadOnlyList<string> Extensions => [".docx"];

    public DocumentFormatProfile Profile => DocxProfile;

    public bool CanLoad => true;

    public bool CanSave => true;

    public Document Load(Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new DocxImporter().Load(stream);
    }

    public void Save(Document document, Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        new DocxExporter().Save(document, stream);
    }
}
