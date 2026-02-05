namespace Vibe.Office.Collaboration.Persistence;

public readonly record struct CollabSnapshotIndex(long Version, long OpLogLength, DateTimeOffset CreatedAtUtc);

public sealed class CollabSnapshotIndexSerializer
{
    public byte[] Serialize(CollabSnapshotIndex index)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(CollabPersistedFormat.SnapshotIndexMagic);
        writer.Write(CollabPersistedFormat.SnapshotIndexVersion);
        writer.Write(index.Version);
        writer.Write(index.OpLogLength);
        writer.Write(index.CreatedAtUtc.UtcTicks);

        writer.Flush();
        return stream.ToArray();
    }

    public CollabSnapshotIndex Deserialize(ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray());
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        if (magic != CollabPersistedFormat.SnapshotIndexMagic)
        {
            throw new InvalidDataException("Invalid snapshot index magic.");
        }

        var version = reader.ReadInt32();
        if (version <= 0 || version > CollabPersistedFormat.SnapshotIndexVersion)
        {
            throw new InvalidDataException($"Unsupported snapshot index version: {version}.");
        }

        var snapshotVersion = reader.ReadInt64();
        var opLogLength = reader.ReadInt64();
        var ticks = reader.ReadInt64();
        var created = new DateTimeOffset(ticks, TimeSpan.Zero);

        return new CollabSnapshotIndex(snapshotVersion, opLogLength, created);
    }
}
