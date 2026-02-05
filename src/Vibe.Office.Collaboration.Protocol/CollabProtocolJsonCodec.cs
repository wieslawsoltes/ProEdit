using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Protocol;

/// <summary>
/// JSON codec for collaboration protocol envelopes.
/// </summary>
public static class CollabProtocolJsonCodec
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize<TPayload>(CollabEnvelope<TPayload> envelope)
    {
        return JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
    }

    public static CollabEnvelope<TPayload> Deserialize<TPayload>(ReadOnlySpan<byte> payload)
    {
        var envelope = JsonSerializer.Deserialize<CollabEnvelope<TPayload>>(payload, Options);
        if (envelope is null)
        {
            throw new InvalidDataException("Failed to deserialize collaboration envelope.");
        }

        return envelope;
    }

    public static CollabEnvelope<JsonElement> DeserializeEnvelope(ReadOnlySpan<byte> payload)
    {
        var envelope = JsonSerializer.Deserialize<CollabEnvelope<JsonElement>>(payload, Options);
        if (envelope is null)
        {
            throw new InvalidDataException("Failed to deserialize collaboration envelope.");
        }

        return envelope;
    }

    public static byte[] SerializePayload<TPayload>(TPayload payload)
    {
        return JsonSerializer.SerializeToUtf8Bytes(payload, Options);
    }

    public static TPayload DeserializePayload<TPayload>(ReadOnlySpan<byte> payload)
    {
        var result = JsonSerializer.Deserialize<TPayload>(payload, Options);
        if (result is null)
        {
            throw new InvalidDataException($"Failed to deserialize payload: {typeof(TPayload).Name}.");
        }

        return result;
    }

    public static TPayload DeserializePayload<TPayload>(JsonElement payload)
    {
        var result = payload.Deserialize<TPayload>(Options);
        if (result is null)
        {
            throw new InvalidDataException($"Failed to deserialize payload: {typeof(TPayload).Name}.");
        }

        return result;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new CollabOpJsonConverter());
        return options;
    }
}
