using System.Security.Cryptography;

namespace ProEdit.Collaboration.Persistence;

public static class CollabChecksum
{
    public static byte[] Compute(ReadOnlySpan<byte> payload)
    {
        var hash = SHA256.HashData(payload);
        return new[] { hash[0], hash[1], hash[2], hash[3] };
    }
}
