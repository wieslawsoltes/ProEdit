namespace ProEdit.Collaboration.Persistence;

public sealed class CollabOpLogWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _sync = new();

    public CollabOpLogWriter(string path)
    {
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public void Append(CollabOpBatch batch)
    {
        AppendMany(new[] { batch });
    }

    public void AppendMany(IReadOnlyList<CollabOpBatch> batches)
    {
        if (batches is null || batches.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            using var writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
            foreach (var batch in batches)
            {
                var payload = CollabOpBatchJsonCodec.Serialize(batch);
                var checksum = CollabChecksum.Compute(payload);
                writer.Write(CollabPersistedFormat.OpLogMagic);
                writer.Write(CollabPersistedFormat.OpLogVersion);
                writer.Write(payload.Length);
                writer.Write(checksum);
                writer.Write(payload);
            }

            writer.Flush();
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}

public sealed class CollabOpLogReader : IDisposable
{
    private readonly FileStream _stream;

    public CollabOpLogReader(string path, long startPosition = 0)
    {
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
        if (startPosition > 0)
        {
            var clamped = startPosition >= _stream.Length ? _stream.Length : startPosition;
            _stream.Seek(clamped, SeekOrigin.Begin);
        }
    }

    public IEnumerable<CollabOpBatch> ReadAll()
    {
        using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
        while (true)
        {
            if (_stream.Position >= _stream.Length)
            {
                yield break;
            }

            if (_stream.Length - _stream.Position < sizeof(uint) + sizeof(int) + sizeof(int) + 4)
            {
                yield break;
            }

            uint magic;
            int version;
            int length;
            byte[] checksum;
            try
            {
                magic = reader.ReadUInt32();
                version = reader.ReadInt32();
                length = reader.ReadInt32();
                checksum = reader.ReadBytes(4);
            }
            catch (EndOfStreamException)
            {
                yield break;
            }

            if (magic != CollabPersistedFormat.OpLogMagic)
            {
                throw new InvalidDataException("Invalid op log magic.");
            }

            if (version != CollabPersistedFormat.OpLogVersion)
            {
                throw new InvalidDataException($"Unsupported op log version: {version}.");
            }

            if (length <= 0)
            {
                yield break;
            }

            if (_stream.Length - _stream.Position < length)
            {
                yield break;
            }

            var payload = reader.ReadBytes(length);
            if (payload.Length < length)
            {
                yield break;
            }

            var actualChecksum = CollabChecksum.Compute(payload);
            if (!checksum.SequenceEqual(actualChecksum))
            {
                throw new InvalidDataException("Op log checksum mismatch.");
            }

            yield return CollabOpBatchJsonCodec.Deserialize(payload);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
