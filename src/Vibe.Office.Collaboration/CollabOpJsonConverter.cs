using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vibe.Office.Collaboration;

/// <summary>
/// JSON converter for collaboration operations.
/// </summary>
public sealed class CollabOpJsonConverter : JsonConverter<ICollabOp>
{
    public override ICollabOp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("kind", out var kindElement))
        {
            throw new JsonException("Missing op kind.");
        }

        var kind = Enum.Parse<CollabOpKind>(kindElement.GetString() ?? string.Empty, ignoreCase: true);
        return kind switch
        {
            CollabOpKind.InsertText => ReadInsertText(root),
            CollabOpKind.DeleteRange => ReadDeleteRange(root),
            CollabOpKind.SetParagraphProperties => ReadSetParagraphProperties(root),
            CollabOpKind.SetInlineProperties => ReadSetInlineProperties(root),
            CollabOpKind.InsertBlock => ReadInsertBlock(root),
            CollabOpKind.DeleteBlock => ReadDeleteBlock(root),
            CollabOpKind.ReplaceBlock => ReadReplaceBlock(root),
            CollabOpKind.ReplaceDocumentResources => ReadReplaceDocumentResources(root),
            _ => throw new JsonException($"Unsupported op kind: {kind}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ICollabOp value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind.ToString());

        switch (value)
        {
            case InsertTextOp insert:
                WriteAnchor(writer, "anchor", insert.Anchor);
                writer.WriteString("text", insert.Text);
                if (insert.AuthorId.HasValue)
                {
                    writer.WriteString("authorId", insert.AuthorId.Value);
                }
                break;
            case DeleteRangeOp delete:
                WriteAnchor(writer, "start", delete.Start);
                WriteAnchor(writer, "end", delete.End);
                break;
            case SetParagraphPropertiesOp setParagraph:
                writer.WriteString("paragraphNodeId", setParagraph.ParagraphNodeId);
                writer.WritePropertyName("properties");
                JsonSerializer.Serialize(writer, setParagraph.Properties, options);
                writer.WriteNumber("lamport", setParagraph.Lamport);
                break;
            case SetInlinePropertiesOp setInline:
                writer.WriteString("inlineNodeId", setInline.InlineNodeId);
                writer.WritePropertyName("properties");
                JsonSerializer.Serialize(writer, setInline.Properties, options);
                writer.WriteNumber("lamport", setInline.Lamport);
                break;
            case InsertBlockOp insertBlock:
                writer.WriteString("parentNodeId", insertBlock.ParentNodeId);
                writer.WriteString("position", insertBlock.Position.Value);
                writer.WriteString("blockType", insertBlock.BlockType);
                if (insertBlock.Payload is null)
                {
                    writer.WriteNull("payload");
                }
                else
                {
                    writer.WriteString("payload", Convert.ToBase64String(insertBlock.Payload));
                }
                break;
            case DeleteBlockOp deleteBlock:
                writer.WriteString("parentNodeId", deleteBlock.ParentNodeId);
                writer.WriteString("position", deleteBlock.Position.Value);
                writer.WriteString("blockNodeId", deleteBlock.BlockNodeId);
                break;
            case ReplaceBlockOp replaceBlock:
                writer.WriteString("blockNodeId", replaceBlock.BlockNodeId);
                writer.WriteString("payload", Convert.ToBase64String(replaceBlock.Payload));
                break;
            case ReplaceDocumentResourcesOp replaceResources:
                writer.WriteString("payload", Convert.ToBase64String(replaceResources.Payload));
                break;
        }

        writer.WriteEndObject();
    }

    private static InsertTextOp ReadInsertText(JsonElement element)
    {
        var anchor = ReadAnchor(element, "anchor");
        var text = element.GetProperty("text").GetString() ?? string.Empty;
        Guid? authorId = null;
        if (element.TryGetProperty("authorId", out var authorElement) && authorElement.ValueKind == JsonValueKind.String)
        {
            authorId = authorElement.GetGuid();
        }

        return new InsertTextOp(anchor, text, authorId);
    }

    private static DeleteRangeOp ReadDeleteRange(JsonElement element)
    {
        var start = ReadAnchor(element, "start");
        var end = ReadAnchor(element, "end");
        return new DeleteRangeOp(start, end);
    }

    private static SetParagraphPropertiesOp ReadSetParagraphProperties(JsonElement element)
    {
        var nodeId = element.GetProperty("paragraphNodeId").GetGuid();
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(element.GetProperty("properties"))
            ?? new Dictionary<string, string>();
        var lamport = element.GetProperty("lamport").GetInt64();
        return new SetParagraphPropertiesOp(nodeId, properties, lamport);
    }

    private static SetInlinePropertiesOp ReadSetInlineProperties(JsonElement element)
    {
        var nodeId = element.GetProperty("inlineNodeId").GetGuid();
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(element.GetProperty("properties"))
            ?? new Dictionary<string, string>();
        var lamport = element.GetProperty("lamport").GetInt64();
        return new SetInlinePropertiesOp(nodeId, properties, lamport);
    }

    private static InsertBlockOp ReadInsertBlock(JsonElement element)
    {
        var parentNodeId = element.GetProperty("parentNodeId").GetGuid();
        var position = new PositionToken(element.GetProperty("position").GetString() ?? string.Empty);
        var blockType = element.GetProperty("blockType").GetString() ?? string.Empty;
        byte[]? payload = null;
        if (element.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.String)
        {
            var payloadString = payloadElement.GetString();
            if (!string.IsNullOrWhiteSpace(payloadString))
            {
                payload = Convert.FromBase64String(payloadString);
            }
        }

        return new InsertBlockOp(parentNodeId, position, blockType, payload);
    }

    private static DeleteBlockOp ReadDeleteBlock(JsonElement element)
    {
        var parentNodeId = element.GetProperty("parentNodeId").GetGuid();
        var position = new PositionToken(element.GetProperty("position").GetString() ?? string.Empty);
        var blockNodeId = element.GetProperty("blockNodeId").GetGuid();
        return new DeleteBlockOp(parentNodeId, position, blockNodeId);
    }

    private static ReplaceBlockOp ReadReplaceBlock(JsonElement element)
    {
        var blockNodeId = element.GetProperty("blockNodeId").GetGuid();
        var payloadString = element.GetProperty("payload").GetString();
        if (string.IsNullOrWhiteSpace(payloadString))
        {
            throw new JsonException("ReplaceBlock payload is required.");
        }

        var payload = Convert.FromBase64String(payloadString);
        return new ReplaceBlockOp(blockNodeId, payload);
    }

    private static ReplaceDocumentResourcesOp ReadReplaceDocumentResources(JsonElement element)
    {
        var payloadString = element.GetProperty("payload").GetString();
        if (string.IsNullOrWhiteSpace(payloadString))
        {
            throw new JsonException("ReplaceDocumentResources payload is required.");
        }

        var payload = Convert.FromBase64String(payloadString);
        return new ReplaceDocumentResourcesOp(payload);
    }

    private static void WriteAnchor(Utf8JsonWriter writer, string name, TextAnchor anchor)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("nodeId", anchor.NodeId);
        writer.WriteNumber("offset", anchor.Offset);
        writer.WriteString("bias", anchor.Bias.ToString());
        writer.WriteEndObject();
    }

    private static TextAnchor ReadAnchor(JsonElement element, string name)
    {
        var anchorElement = element.GetProperty(name);
        var nodeId = anchorElement.GetProperty("nodeId").GetGuid();
        var offset = anchorElement.GetProperty("offset").GetInt32();
        var bias = Enum.Parse<AnchorBias>(anchorElement.GetProperty("bias").GetString() ?? "Before", true);
        return new TextAnchor(nodeId, offset, bias);
    }
}
