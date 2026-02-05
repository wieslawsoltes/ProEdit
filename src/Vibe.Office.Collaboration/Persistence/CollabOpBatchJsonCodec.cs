using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace Vibe.Office.Collaboration.Persistence;

public static class CollabOpBatchJsonCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new CollabOpJsonConverter() }
    };

    public static byte[] Serialize(CollabOpBatch batch)
    {
        return JsonSerializer.SerializeToUtf8Bytes(batch, Options);
    }

    public static CollabOpBatch Deserialize(ReadOnlySpan<byte> payload)
    {
        var batch = JsonSerializer.Deserialize<CollabOpBatch>(payload, Options);
        if (batch is null)
        {
            throw new InvalidDataException("Failed to deserialize op batch.");
        }

        return batch;
    }

}
