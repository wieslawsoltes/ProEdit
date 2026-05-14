using System.Text.Json;
using ProEdit.Documents;

namespace ProEdit.Macros;

public sealed class JsonMacroPayloadCodec : IMacroPayloadCodec
{
    private readonly Dictionary<string, Type> _idToType = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _typeToId = new();
    private readonly JsonSerializerOptions _options;

    public JsonMacroPayloadCodec()
    {
        _options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public void RegisterType<T>(string typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        var type = typeof(T);
        _idToType[typeId] = type;
        _typeToId[type] = typeId;
    }

    public bool TryEncode(object? payload, out MacroPayload? encoded)
    {
        if (payload is null)
        {
            encoded = null;
            return true;
        }

        var type = payload.GetType();
        var typeId = ResolveTypeId(type);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            encoded = null;
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, type, _options);
            encoded = new MacroPayload
            {
                TypeId = typeId,
                Json = json
            };
            return true;
        }
        catch (Exception)
        {
            encoded = null;
            return false;
        }
    }

    public bool TryDecode(MacroPayload? payload, out object? decoded)
    {
        if (payload is null)
        {
            decoded = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(payload.Json))
        {
            decoded = null;
            return true;
        }

        var type = ResolveType(payload.TypeId);
        if (type is null)
        {
            decoded = null;
            return false;
        }

        try
        {
            decoded = JsonSerializer.Deserialize(payload.Json, type, _options);
            return true;
        }
        catch (Exception)
        {
            decoded = null;
            return false;
        }
    }

    private string? ResolveTypeId(Type type)
    {
        if (_typeToId.TryGetValue(type, out var typeId))
        {
            return typeId;
        }

        return type.AssemblyQualifiedName ?? type.FullName;
    }

    private Type? ResolveType(string? typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return null;
        }

        if (_idToType.TryGetValue(typeId, out var type))
        {
            return type;
        }

        return Type.GetType(typeId, false);
    }
}
