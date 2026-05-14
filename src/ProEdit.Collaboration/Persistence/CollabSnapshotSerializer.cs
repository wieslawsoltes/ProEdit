using System.Text;
using ProEdit.Documents;
using ProEdit.OpenXml;

namespace ProEdit.Collaboration.Persistence;

public sealed class CollabSnapshotSerializer
{
    public byte[] Serialize(CollabSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(CollabPersistedFormat.SnapshotMagic);
        writer.Write(CollabPersistedFormat.SnapshotVersion);
        writer.Write(snapshot.Version);
        WriteDocumentPayload(writer, snapshot.Document);

        writer.Flush();
        return stream.ToArray();
    }

    public CollabSnapshot DeserializeSnapshot(ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray());
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        if (magic != CollabPersistedFormat.SnapshotMagic)
        {
            throw new InvalidDataException("Invalid snapshot magic.");
        }

        var version = reader.ReadInt32();
        if (version <= 0 || version > CollabPersistedFormat.SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported snapshot version: {version}.");
        }

        var snapshotVersion = reader.ReadInt64();
        var document = ReadDocumentPayload(reader, version);
        return new CollabSnapshot(Guid.NewGuid(), snapshotVersion, document, DateTimeOffset.UtcNow);
    }

    private static void WriteDocumentPayload(BinaryWriter writer, Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var exportDocument = DocumentClone.Clone(document);
        CollabNodeIdMap.TryAttach(exportDocument);

        using var docxStream = new MemoryStream();
        var exporter = new DocxExporter();
        exporter.Save(exportDocument, docxStream);

        var docxBytes = docxStream.ToArray();
        writer.Write(docxBytes.Length);
        writer.Write(docxBytes);
    }

    private static Document ReadDocumentPayload(BinaryReader reader, int version)
    {
        if (version == 1)
        {
            return ReadLegacyDocument(reader);
        }

        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException("Invalid snapshot payload length.");
        }

        var docxBytes = reader.ReadBytes(length);
        if (docxBytes.Length != length)
        {
            throw new EndOfStreamException("Unexpected end of snapshot payload.");
        }

        using var docxStream = new MemoryStream(docxBytes);
        var importer = new DocxImporter();
        var document = importer.Load(docxStream);

        if (CollabNodeIdMap.TryExtract(document, out var map) && map is not null)
        {
            CollabNodeIdMap.TryApply(document, map);
            CollabNodeIdMap.Remove(document);
        }

        return document;
    }

    private static Document ReadLegacyDocument(BinaryReader reader)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.TrackChangesEnabled = reader.ReadBoolean();

        var blockCount = reader.ReadInt32();
        for (var i = 0; i < blockCount; i++)
        {
            document.Blocks.Add(ReadBlock(reader));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }

    private static void WriteBlock(BinaryWriter writer, Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                writer.Write((byte)SnapshotBlockKind.Paragraph);
                WriteGuid(writer, paragraph.NodeId);
                writer.Write(paragraph.StyleId ?? string.Empty);
                writer.Write(paragraph.Text ?? string.Empty);
                writer.Write(paragraph.Inlines.Count);
                foreach (var inline in paragraph.Inlines)
                {
                    WriteInline(writer, inline);
                }
                break;
            case PageBreakBlock:
                writer.Write((byte)SnapshotBlockKind.PageBreak);
                WriteGuid(writer, block.NodeId);
                break;
            case ColumnBreakBlock:
                writer.Write((byte)SnapshotBlockKind.ColumnBreak);
                WriteGuid(writer, block.NodeId);
                break;
            default:
                throw new NotSupportedException($"Snapshot serializer does not support block type: {block.GetType().Name}");
        }
    }

    private static Block ReadBlock(BinaryReader reader)
    {
        var kind = (SnapshotBlockKind)reader.ReadByte();
        return kind switch
        {
            SnapshotBlockKind.Paragraph => ReadParagraph(reader),
            SnapshotBlockKind.PageBreak => ReadBreak(new PageBreakBlock(), reader),
            SnapshotBlockKind.ColumnBreak => ReadBreak(new ColumnBreakBlock(), reader),
            _ => throw new InvalidDataException($"Unsupported block kind: {kind}")
        };
    }

    private static Block ReadBreak(Block block, BinaryReader reader)
    {
        block.NodeId = ReadGuid(reader);
        return block;
    }

    private static ParagraphBlock ReadParagraph(BinaryReader reader)
    {
        var paragraph = new ParagraphBlock();
        paragraph.NodeId = ReadGuid(reader);
        var styleId = reader.ReadString();
        paragraph.StyleId = string.IsNullOrEmpty(styleId) ? null : styleId;

        paragraph.Text = reader.ReadString();
        var inlineCount = reader.ReadInt32();
        paragraph.Inlines.Clear();
        for (var i = 0; i < inlineCount; i++)
        {
            paragraph.Inlines.Add(ReadInline(reader));
        }
        if (paragraph.Inlines.Count > 0)
        {
            paragraph.Text = BuildParagraphText(paragraph);
        }
        return paragraph;
    }

    private static void WriteInline(BinaryWriter writer, Inline inline)
    {
        switch (inline)
        {
            case RunInline run:
                writer.Write((byte)SnapshotInlineKind.Run);
                WriteGuid(writer, run.NodeId);
                writer.Write(run.Text.GetText());
                break;
            default:
                throw new NotSupportedException($"Snapshot serializer does not support inline type: {inline.GetType().Name}");
        }
    }

    private static Inline ReadInline(BinaryReader reader)
    {
        var kind = (SnapshotInlineKind)reader.ReadByte();
        return kind switch
        {
            SnapshotInlineKind.Run => ReadRun(reader),
            _ => throw new InvalidDataException($"Unsupported inline kind: {kind}")
        };
    }

    private static RunInline ReadRun(BinaryReader reader)
    {
        var nodeId = ReadGuid(reader);
        var text = reader.ReadString();
        var run = new RunInline(text) { NodeId = nodeId };
        return run;
    }

    private static void WriteGuid(BinaryWriter writer, Guid value)
    {
        Span<byte> buffer = stackalloc byte[16];
        value.TryWriteBytes(buffer);
        writer.Write(buffer);
    }

    private static Guid ReadGuid(BinaryReader reader)
    {
        Span<byte> buffer = stackalloc byte[16];
        reader.Read(buffer);
        return new Guid(buffer);
    }

    private static string BuildParagraphText(ParagraphBlock paragraph)
    {
        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                builder.Append(run.Text.GetText());
            }
            else
            {
                builder.Append(DocumentConstants.ObjectReplacementChar);
            }
        }

        return builder.ToString();
    }

    private enum SnapshotBlockKind : byte
    {
        Paragraph = 1,
        PageBreak = 2,
        ColumnBreak = 3
    }

    private enum SnapshotInlineKind : byte
    {
        Run = 1
    }
}
