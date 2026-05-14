using System.Text;
using ProEdit.Documents;
using ProEdit.Documents.Formats;

namespace ProEdit.FlowDocument.IO.Formats;

internal sealed class RtfDocumentFormat : IDocumentFormat
{
    private static readonly DocumentFormatProfile RtfProfile = new(
        "rtf",
        "Rich Text Format",
        DocumentFormatCapability.Paragraphs
        | DocumentFormatCapability.Lists
        | DocumentFormatCapability.Tables
        | DocumentFormatCapability.Images
        | DocumentFormatCapability.Links
        | DocumentFormatCapability.Emphasis
        | DocumentFormatCapability.Strong
        | DocumentFormatCapability.Strikethrough
        | DocumentFormatCapability.HardLineBreaks);

    public string FormatId => "rtf";

    public string DisplayName => "Rich Text Format";

    public IReadOnlyList<string> Extensions => [".rtf"];

    public DocumentFormatProfile Profile => RtfProfile;

    public bool CanLoad => true;

    public bool CanSave => true;

    public Document Load(Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var rtf = Encoding.Latin1.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (!DocumentRtfParser.TryParse(rtf, out var document))
        {
            throw new InvalidDataException("RTF parsing failed.");
        }

        return document;
    }

    public void Save(Document document, Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        var rtf = DocumentRtfSerializer.ToRtf(document);
        using var writer = new StreamWriter(stream, Encoding.ASCII, bufferSize: 4096, leaveOpen: true);
        writer.Write(rtf);
        writer.Flush();
    }
}
