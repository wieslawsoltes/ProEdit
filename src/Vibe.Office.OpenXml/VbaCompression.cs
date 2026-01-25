namespace Vibe.Office.OpenXml;

internal static class VbaCompression
{
    private const int ChunkSize = 4096;

    public static byte[] Decompress(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (data[0] == 0x00)
        {
            return data.Length > 1 ? data.AsSpan(1).ToArray() : Array.Empty<byte>();
        }

        if (data[0] != 0x01)
        {
            return Array.Empty<byte>();
        }

        var output = new List<byte>();
        var offset = 1;

        while (offset + 2 <= data.Length)
        {
            var header = ReadUInt16(data, offset);
            offset += 2;

            var chunkSize = (header & 0x0FFF) + 3;
            var isCompressed = (header & 0x8000) != 0;

            if (offset + chunkSize > data.Length)
            {
                chunkSize = data.Length - offset;
            }

            if (!isCompressed)
            {
                if (chunkSize > 0)
                {
                    output.AddRange(data.AsSpan(offset, chunkSize).ToArray());
                }

                offset += chunkSize;
                continue;
            }

            var chunkEnd = offset + chunkSize;
            var chunkOutput = new List<byte>(ChunkSize);
            while (offset < chunkEnd && chunkOutput.Count < ChunkSize)
            {
                var flags = data[offset++];
                for (var bit = 0; bit < 8 && offset < chunkEnd && chunkOutput.Count < ChunkSize; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        chunkOutput.Add(data[offset++]);
                        continue;
                    }

                    if (offset + 1 >= chunkEnd)
                    {
                        offset = chunkEnd;
                        break;
                    }

                    var token = ReadUInt16(data, offset);
                    offset += 2;

                    var offsetBits = 4;
                    var windowSize = chunkOutput.Count;
                    while ((1 << offsetBits) < windowSize && offsetBits < 12)
                    {
                        offsetBits++;
                    }

                    var lengthBits = 16 - offsetBits;
                    var offsetMask = (1 << offsetBits) - 1;
                    var lengthMask = (1 << lengthBits) - 1;

                    var backOffset = (token & offsetMask) + 1;
                    var length = ((token >> offsetBits) & lengthMask) + 3;

                    for (var i = 0; i < length && chunkOutput.Count < ChunkSize; i++)
                    {
                        var sourceIndex = chunkOutput.Count - backOffset;
                        var value = sourceIndex >= 0 && sourceIndex < chunkOutput.Count
                            ? chunkOutput[sourceIndex]
                            : (byte)0;
                        chunkOutput.Add(value);
                    }
                }
            }

            output.AddRange(chunkOutput);
        }

        return output.ToArray();
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }
}
