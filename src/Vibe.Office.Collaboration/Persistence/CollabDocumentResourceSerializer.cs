using Vibe.Office.Documents;
using Vibe.Office.OpenXml;

namespace Vibe.Office.Collaboration.Persistence;

public sealed class CollabDocumentResourceSerializer
{
    private static readonly Guid PlaceholderNodeId = new("9e2c3d0b-5a73-4c85-bba3-985f5e64e3fd");

    public byte[] Serialize(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var exportDocument = DocumentClone.Clone(document);
        StripContent(exportDocument);
        CollabNodeIdMap.TryAttach(exportDocument);

        using var stream = new MemoryStream();
        var exporter = new DocxExporter();
        exporter.Save(exportDocument, stream);
        return stream.ToArray();
    }

    public Document Deserialize(ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray());
        var importer = new DocxImporter();
        var document = importer.Load(stream);

        if (CollabNodeIdMap.TryExtract(document, out var map) && map is not null)
        {
            CollabNodeIdMap.TryApply(document, map);
            CollabNodeIdMap.Remove(document);
        }

        return document;
    }

    private static void StripContent(Document document)
    {
        document.Blocks.Clear();
        var placeholder = new ParagraphBlock();
        placeholder.NodeId = PlaceholderNodeId;
        document.Blocks.Add(placeholder);

        ClearHeaderFooter(document.Header);
        ClearHeaderFooter(document.Footer);
        ClearHeaderFooter(document.FirstHeader);
        ClearHeaderFooter(document.FirstFooter);
        ClearHeaderFooter(document.EvenHeader);
        ClearHeaderFooter(document.EvenFooter);

        foreach (var section in document.Sections)
        {
            ClearHeaderFooter(section.Header);
            ClearHeaderFooter(section.Footer);
            ClearHeaderFooter(section.FirstHeader);
            ClearHeaderFooter(section.FirstFooter);
            ClearHeaderFooter(section.EvenHeader);
            ClearHeaderFooter(section.EvenFooter);
        }

        foreach (var footnote in document.Footnotes.Values)
        {
            footnote.Blocks.Clear();
        }

        foreach (var endnote in document.Endnotes.Values)
        {
            endnote.Blocks.Clear();
        }

        foreach (var comment in document.Comments.Values)
        {
            comment.Blocks.Clear();
        }
    }

    private static void ClearHeaderFooter(HeaderFooter headerFooter)
    {
        headerFooter.Blocks.Clear();
    }
}
