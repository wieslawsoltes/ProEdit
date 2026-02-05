using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Vibe.Office.Collaboration.Protocol;

public enum CollabCompressionAlgorithm : byte
{
    None = 0,
    Brotli = 1
}

public readonly record struct CollabCompressionHeader(byte Version, CollabCompressionAlgorithm Algorithm, int UncompressedLength);

public static class CollabMessageCompression
{
    public const string BrotliCompressionId = "br";
    public const int DefaultThresholdBytes = 1024;
    public const int DefaultMaxDecompressedBytes = 16 * 1024 * 1024;

    private const uint Magic = 0x56434231; // "VCB1"
    private const byte HeaderVersion = 1;
    private const int HeaderSize = 10;
    private const int BrotliQuality = 4;
    private const int BrotliWindow = 22;

    public static CollabCompressionAlgorithm ParseAlgorithm(string? compression)
    {
        if (string.IsNullOrWhiteSpace(compression))
        {
            return CollabCompressionAlgorithm.None;
        }

        if (string.Equals(compression, BrotliCompressionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(compression, "brotli", StringComparison.OrdinalIgnoreCase))
        {
            return CollabCompressionAlgorithm.Brotli;
        }

        return CollabCompressionAlgorithm.None;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> payload, out CollabCompressionHeader header)
    {
        header = default;
        if (payload.Length < HeaderSize)
        {
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (magic != Magic)
        {
            return false;
        }

        var version = payload[4];
        var algorithm = (CollabCompressionAlgorithm)payload[5];
        var uncompressedLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(6, 4));

        header = new CollabCompressionHeader(version, algorithm, uncompressedLength);
        return true;
    }

    public static bool TryDecompress(
        ReadOnlySpan<byte> payload,
        out ReadOnlyMemory<byte> decompressed,
        out CollabCompressionHeader header)
    {
        return TryDecompress(payload, DefaultMaxDecompressedBytes, out decompressed, out header);
    }

    public static bool TryDecompress(
        ReadOnlySpan<byte> payload,
        int maxDecompressedBytes,
        out ReadOnlyMemory<byte> decompressed,
        out CollabCompressionHeader header)
    {
        decompressed = default;
        if (!TryReadHeader(payload, out header))
        {
            return false;
        }

        if (header.Version != HeaderVersion)
        {
            throw new InvalidDataException($"Unsupported compression header version {header.Version}.");
        }

        if (header.UncompressedLength <= 0 || header.UncompressedLength > maxDecompressedBytes)
        {
            throw new InvalidDataException($"Invalid decompressed payload size {header.UncompressedLength}.");
        }

        var compressed = payload.Slice(HeaderSize);
        var output = new byte[header.UncompressedLength];
        switch (header.Algorithm)
        {
            case CollabCompressionAlgorithm.Brotli:
            {
                if (!BrotliDecoder.TryDecompress(compressed, output, out var written) || written != output.Length)
                {
                    throw new InvalidDataException("Failed to decompress brotli payload.");
                }

                break;
            }
            default:
                throw new InvalidDataException($"Unsupported compression algorithm {header.Algorithm}.");
        }

        decompressed = output;
        return true;
    }

    public static ReadOnlyMemory<byte> MaybeCompress(
        ReadOnlyMemory<byte> payload,
        string? compression,
        int thresholdBytes = DefaultThresholdBytes)
    {
        var algorithm = ParseAlgorithm(compression);
        return MaybeCompress(payload, algorithm, thresholdBytes);
    }

    public static ReadOnlyMemory<byte> MaybeCompress(
        ReadOnlyMemory<byte> payload,
        CollabCompressionAlgorithm algorithm,
        int thresholdBytes = DefaultThresholdBytes)
    {
        if (algorithm == CollabCompressionAlgorithm.None)
        {
            return payload;
        }

        if (thresholdBytes < 0)
        {
            thresholdBytes = 0;
        }

        if (payload.Length < thresholdBytes)
        {
            return payload;
        }

        var compressed = CompressPayload(payload.Span, algorithm);
        if (compressed.Length == 0 || compressed.Length >= payload.Length)
        {
            return payload;
        }

        return compressed;
    }

    private static byte[] CompressPayload(ReadOnlySpan<byte> payload, CollabCompressionAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case CollabCompressionAlgorithm.Brotli:
                return CompressBrotli(payload);
            default:
                return Array.Empty<byte>();
        }
    }

    private static byte[] CompressBrotli(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var maxCompressedLength = BrotliEncoder.GetMaxCompressedLength(payload.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxCompressedLength);
        try
        {
            if (!BrotliEncoder.TryCompress(payload, rented, out var written, BrotliQuality, BrotliWindow))
            {
                return Array.Empty<byte>();
            }

            var result = new byte[HeaderSize + written];
            WriteHeader(result.AsSpan(0, HeaderSize), payload.Length);
            rented.AsSpan(0, written).CopyTo(result.AsSpan(HeaderSize));
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void WriteHeader(Span<byte> destination, int uncompressedLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[4] = HeaderVersion;
        destination[5] = (byte)CollabCompressionAlgorithm.Brotli;
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(6, 4), uncompressedLength);
    }
}
