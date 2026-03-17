using System.Text;
using System.Text.Json;

namespace Vibe.Office.Mcp;

/// <summary>
/// Stateful JSON-RPC dispatcher for the VibeOffice MCP server.
/// </summary>
public sealed class McpProtocolDispatcher
{
    private readonly McpRequestContext _context;
    private readonly McpServer _server;
    private bool _initializeCompleted;
    private bool _sessionReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolDispatcher" /> class.
    /// </summary>
    /// <param name="server">The MCP server.</param>
    /// <param name="sessionId">The optional stable session identifier.</param>
    public McpProtocolDispatcher(McpServer server, string? sessionId = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _context = new McpRequestContext
        {
            ProtocolVersion = server.Options.ProtocolVersion,
            SessionId = string.IsNullOrWhiteSpace(sessionId)
                ? Guid.NewGuid().ToString("N")
                : sessionId
        };
    }

    /// <summary>
    /// Handles one JSON-RPC request or notification.
    /// </summary>
    /// <param name="json">The incoming JSON payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The JSON-RPC response, or an empty string for notifications.</returns>
    public async ValueTask<string> HandleAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return WriteErrorResponse("null", -32600, "Invalid Request");
        }

        var idRaw = root.TryGetProperty("id", out var idElement)
            ? idElement.GetRawText()
            : null;

        if (!root.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32600, "Invalid Request");
        }

        var method = methodElement.GetString()!;
        var paramsElement = root.TryGetProperty("params", out var paramsValue)
            ? paramsValue
            : default;

        try
        {
            switch (method)
            {
                case "initialize":
                    return WriteResultResponse(idRaw, writer => WriteInitializeResult(writer, paramsElement));
                case "notifications/initialized":
                    if (_initializeCompleted)
                    {
                        _sessionReady = true;
                    }

                    return string.Empty;
                case "ping":
                    return WriteResultResponse(idRaw, static writer => writer.WriteStartObject(), static writer => writer.WriteEndObject());
                case "tools/list":
                    EnsureSessionReady();
                    return WriteResultResponse(idRaw, writer => WriteToolsResult(writer, _server.ListTools()));
                case "tools/call":
                    EnsureSessionReady();
                    var toolResult = await ExecuteToolAsync(paramsElement, cancellationToken);
                    return WriteResultResponse(idRaw, writer =>
                        WriteToolCallResult(writer, toolResult));
                case "resources/list":
                    EnsureSessionReady();
                    return WriteResultResponse(idRaw, writer => WriteResourcesResult(writer, _server.ListResources(_context)));
                case "resources/read":
                    EnsureSessionReady();
                    var resourceResult = await ReadResourceAsync(paramsElement, cancellationToken);
                    return WriteResultResponse(idRaw, writer =>
                        WriteResourceReadResult(writer, resourceResult));
                default:
                    return idRaw is null
                        ? string.Empty
                        : WriteErrorResponse(idRaw, -32601, $"Method '{method}' not found.");
            }
        }
        catch (McpProtocolException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, ex.Code, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32001, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32602, ex.Message);
        }
        catch (FormatException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32602, ex.Message);
        }
        catch (TimeZoneNotFoundException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32602, ex.Message);
        }
        catch (InvalidTimeZoneException ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32602, ex.Message);
        }
        catch (Exception ex)
        {
            return idRaw is null
                ? string.Empty
                : WriteErrorResponse(idRaw, -32000, ex.Message);
        }
    }

    private void EnsureSessionReady()
    {
        if (!_sessionReady)
        {
            throw new McpProtocolException(-32002, "MCP session is not initialized.");
        }
    }

    private void WriteInitializeResult(Utf8JsonWriter writer, JsonElement parameters)
    {
        if (_initializeCompleted)
        {
            throw new McpProtocolException(-32600, "MCP session is already initialized.");
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new McpProtocolException(-32602, "Initialize params must be an object.");
        }

        if (!parameters.TryGetProperty("protocolVersion", out var protocolVersionElement)
            || protocolVersionElement.ValueKind != JsonValueKind.String)
        {
            throw new McpProtocolException(-32602, "Initialize params require a string 'protocolVersion'.");
        }

        var requestedProtocolVersion = protocolVersionElement.GetString()!;
        _context.ProtocolVersion = NegotiateProtocolVersion(requestedProtocolVersion);
        _initializeCompleted = true;
        _sessionReady = false;

        writer.WriteStartObject();
        writer.WriteString("protocolVersion", _context.ProtocolVersion);
        writer.WritePropertyName("capabilities");
        writer.WriteStartObject();
        writer.WritePropertyName("tools");
        writer.WriteStartObject();
        writer.WriteBoolean("listChanged", false);
        writer.WriteEndObject();
        writer.WritePropertyName("resources");
        writer.WriteStartObject();
        writer.WriteBoolean("listChanged", false);
        writer.WriteBoolean("subscribe", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("serverInfo");
        writer.WriteStartObject();
        writer.WriteString("name", _server.Options.ServerInfo.Name);
        writer.WriteString("version", _server.Options.ServerInfo.Version);
        if (!string.IsNullOrWhiteSpace(_server.Options.ServerInfo.Title))
        {
            writer.WriteString("title", _server.Options.ServerInfo.Title);
        }

        writer.WriteEndObject();
        if (!string.IsNullOrWhiteSpace(_server.Options.Instructions))
        {
            writer.WriteString("instructions", _server.Options.Instructions);
        }

        writer.WriteEndObject();
    }

    private string NegotiateProtocolVersion(string requestedProtocolVersion)
    {
        if (string.Equals(requestedProtocolVersion, _server.Options.ProtocolVersion, StringComparison.Ordinal))
        {
            return requestedProtocolVersion;
        }

        return _server.Options.ProtocolVersion;
    }

    private async ValueTask<McpToolCallResult> ExecuteToolAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new McpProtocolException(-32602, "Tool call params must be an object.");
        }

        if (!parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            throw new McpProtocolException(-32602, "Tool call params require a string 'name'.");
        }

        var arguments = parameters.TryGetProperty("arguments", out var argumentsElement)
            ? argumentsElement
            : default;
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            using var emptyDocument = JsonDocument.Parse("{}");
            return await _server.CallToolAsync(nameElement.GetString()!, emptyDocument.RootElement, _context, cancellationToken);
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new McpProtocolException(-32602, "Tool call 'arguments' must be an object.");
        }

        return await _server.CallToolAsync(nameElement.GetString()!, arguments, _context, cancellationToken);
    }

    private async ValueTask<McpResourceReadResult> ReadResourceAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new McpProtocolException(-32602, "Resource read params must be an object.");
        }

        if (!parameters.TryGetProperty("uri", out var uriElement)
            || uriElement.ValueKind != JsonValueKind.String)
        {
            throw new McpProtocolException(-32602, "Resource read params require a string 'uri'.");
        }

        return await _server.ReadResourceAsync(uriElement.GetString()!, _context, cancellationToken);
    }

    private static void WriteToolsResult(
        Utf8JsonWriter writer,
        IReadOnlyList<McpToolDefinition> tools)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("tools");
        writer.WriteStartArray();
        for (var index = 0; index < tools.Count; index++)
        {
            var tool = tools[index];
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            if (!string.IsNullOrWhiteSpace(tool.Title))
            {
                writer.WriteString("title", tool.Title);
            }

            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("inputSchema");
            tool.InputSchema.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteResourcesResult(
        Utf8JsonWriter writer,
        IReadOnlyList<McpResourceDefinition> resources)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("resources");
        writer.WriteStartArray();
        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];
            writer.WriteStartObject();
            writer.WriteString("uri", resource.Uri);
            writer.WriteString("name", resource.Name);
            if (!string.IsNullOrWhiteSpace(resource.Title))
            {
                writer.WriteString("title", resource.Title);
            }

            writer.WriteString("description", resource.Description);
            writer.WriteString("mimeType", resource.MimeType);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteToolCallResult(
        Utf8JsonWriter writer,
        McpToolCallResult result)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isError", result.IsError);
        writer.WritePropertyName("content");
        writer.WriteStartArray();
        for (var index = 0; index < result.Content.Count; index++)
        {
            WriteContentItem(writer, result.Content[index]);
        }

        writer.WriteEndArray();
        if (result.StructuredContent is not null)
        {
            writer.WritePropertyName("structuredContent");
            result.StructuredContent.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteResourceReadResult(
        Utf8JsonWriter writer,
        McpResourceReadResult result)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("contents");
        writer.WriteStartArray();
        for (var index = 0; index < result.Contents.Count; index++)
        {
            WriteResourceContents(writer, result.Contents[index]);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteContentItem(Utf8JsonWriter writer, McpContentItem content)
    {
        writer.WriteStartObject();
        writer.WriteString("type", content.Type);
        switch (content)
        {
            case McpTextContentItem text:
                writer.WriteString("text", text.Text);
                break;
            case McpEmbeddedResourceContentItem resource:
                writer.WritePropertyName("resource");
                WriteResourceContents(writer, resource.Resource);
                break;
            default:
                throw new NotSupportedException($"Unsupported MCP content type '{content.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static void WriteResourceContents(
        Utf8JsonWriter writer,
        McpResourceContents contents)
    {
        writer.WriteStartObject();
        writer.WriteString("uri", contents.Uri);
        writer.WriteString("mimeType", contents.MimeType);
        switch (contents)
        {
            case McpTextResourceContents text:
                writer.WriteString("text", text.Text);
                break;
            case McpBlobResourceContents blob:
                writer.WriteString("blob", blob.Blob);
                break;
            default:
                throw new NotSupportedException($"Unsupported MCP resource content type '{contents.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static string WriteResultResponse(
        string? idRaw,
        Action<Utf8JsonWriter> writeResult)
    {
        return WriteResultResponse(idRaw, writeResult, null);
    }

    private static string WriteResultResponse(
        string? idRaw,
        Action<Utf8JsonWriter> writeStart,
        Action<Utf8JsonWriter>? writeEnd)
    {
        if (idRaw is null)
        {
            return string.Empty;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            writer.WriteRawValue(idRaw);
            writer.WritePropertyName("result");
            writeStart(writer);
            writeEnd?.Invoke(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteErrorResponse(string idRaw, int code, string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            writer.WriteRawValue(idRaw);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class McpProtocolException : Exception
    {
        public McpProtocolException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
