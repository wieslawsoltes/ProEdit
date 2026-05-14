using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProEdit.Mcp;

/// <summary>
/// Well-known MCP protocol version constants used by the ProEdit host.
/// </summary>
public static class McpProtocolVersions
{
    /// <summary>
    /// The default MCP protocol version used by the built-in dispatcher.
    /// </summary>
    public const string Current = "2025-11-25";
}

/// <summary>
/// Describes the MCP server implementation.
/// </summary>
public sealed class McpImplementationInfo
{
    /// <summary>
    /// Gets or sets the stable implementation name.
    /// </summary>
    public string Name { get; set; } = "proedit";

    /// <summary>
    /// Gets or sets the implementation version.
    /// </summary>
    public string Version { get; set; } = "0.1.0";

    /// <summary>
    /// Gets or sets the optional human-readable title.
    /// </summary>
    public string? Title { get; set; }
}

/// <summary>
/// Configures the ProEdit MCP server surface.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Gets or sets the exposed protocol version.
    /// </summary>
    public string ProtocolVersion { get; set; } = McpProtocolVersions.Current;

    /// <summary>
    /// Gets or sets the server information advertised to clients.
    /// </summary>
    public McpImplementationInfo ServerInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets optional operator guidance shown to clients during initialization.
    /// </summary>
    public string? Instructions { get; set; }
}

/// <summary>
/// Captures request-scoped MCP metadata.
/// </summary>
public sealed class McpRequestContext
{
    /// <summary>
    /// Gets or sets the negotiated protocol version.
    /// </summary>
    public string ProtocolVersion { get; set; } = McpProtocolVersions.Current;

    /// <summary>
    /// Gets or sets the logical session identifier.
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets the request-scoped metadata bag.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Describes one MCP tool.
/// </summary>
public sealed class McpToolDefinition
{
    /// <summary>
    /// Gets or sets the stable tool name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional human-readable title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the tool description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON schema for the tool arguments.
    /// </summary>
    public JsonObject InputSchema { get; set; } = new();
}

/// <summary>
/// Describes one MCP resource.
/// </summary>
public sealed class McpResourceDefinition
{
    /// <summary>
    /// Gets or sets the stable resource URI.
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional human-readable title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the resource description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource media type.
    /// </summary>
    public string MimeType { get; set; } = "application/json";
}

/// <summary>
/// Base type for MCP tool result content.
/// </summary>
public abstract class McpContentItem
{
    /// <summary>
    /// Gets the MCP content type discriminator.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Plain-text MCP tool result content.
/// </summary>
public sealed class McpTextContentItem : McpContentItem
{
    /// <inheritdoc />
    public override string Type => "text";

    /// <summary>
    /// Gets or sets the text payload.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Embedded resource MCP tool result content.
/// </summary>
public sealed class McpEmbeddedResourceContentItem : McpContentItem
{
    /// <inheritdoc />
    public override string Type => "resource";

    /// <summary>
    /// Gets or sets the embedded resource payload.
    /// </summary>
    public McpResourceContents Resource { get; set; } = new McpTextResourceContents();
}

/// <summary>
/// Base type for MCP resource-read content.
/// </summary>
public abstract class McpResourceContents
{
    /// <summary>
    /// Gets or sets the resource URI.
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MimeType { get; set; } = "application/json";
}

/// <summary>
/// Text resource content.
/// </summary>
public sealed class McpTextResourceContents : McpResourceContents
{
    /// <summary>
    /// Gets or sets the text payload.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Binary resource content represented as base64.
/// </summary>
public sealed class McpBlobResourceContents : McpResourceContents
{
    /// <summary>
    /// Gets or sets the base64 payload.
    /// </summary>
    public string Blob { get; set; } = string.Empty;
}

/// <summary>
/// Represents one MCP tool invocation result.
/// </summary>
public sealed class McpToolCallResult
{
    /// <summary>
    /// Gets the content emitted by the tool.
    /// </summary>
    public List<McpContentItem> Content { get; } = new();

    /// <summary>
    /// Gets or sets optional structured JSON content.
    /// </summary>
    public JsonObject? StructuredContent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool call failed semantically.
    /// </summary>
    public bool IsError { get; set; }
}

/// <summary>
/// Represents one MCP resource-read result.
/// </summary>
public sealed class McpResourceReadResult
{
    /// <summary>
    /// Gets the returned resource contents.
    /// </summary>
    public List<McpResourceContents> Contents { get; } = new();
}

/// <summary>
/// Handles one MCP tool.
/// </summary>
public interface IMcpToolHandler
{
    /// <summary>
    /// Gets the advertised tool definition.
    /// </summary>
    McpToolDefinition Definition { get; }

    /// <summary>
    /// Invokes the tool with the supplied JSON arguments.
    /// </summary>
    /// <param name="arguments">The raw JSON argument object.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool invocation result.</returns>
    ValueTask<McpToolCallResult> InvokeAsync(
        JsonElement arguments,
        McpRequestContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides one or more MCP resources.
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>
    /// Lists the resources exposed by this provider.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The exposed resources.</returns>
    IReadOnlyList<McpResourceDefinition> ListResources(McpRequestContext context);

    /// <summary>
    /// Attempts to read one resource by URI.
    /// </summary>
    /// <param name="uri">The requested URI.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resource result, or <see langword="null" /> when the provider does not own the URI.</returns>
    ValueTask<McpResourceReadResult?> TryReadAsync(
        string uri,
        McpRequestContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Registers a pluggable feature into an MCP server builder.
/// </summary>
public interface IMcpFeature
{
    /// <summary>
    /// Registers the feature's tools and resources.
    /// </summary>
    /// <param name="builder">The target builder.</param>
    void Register(McpServerBuilder builder);
}
