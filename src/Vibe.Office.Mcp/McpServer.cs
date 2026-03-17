using System.Text.Json;

namespace Vibe.Office.Mcp;

/// <summary>
/// Builder for a pluggable VibeOffice MCP server.
/// </summary>
public sealed class McpServerBuilder
{
    private readonly List<IMcpFeature> _features = new();
    private readonly List<IMcpResourceProvider> _resourceProviders = new();
    private readonly Dictionary<string, IMcpToolHandler> _toolHandlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerBuilder" /> class.
    /// </summary>
    /// <param name="options">The optional server options.</param>
    public McpServerBuilder(McpServerOptions? options = null)
    {
        Options = options ?? new McpServerOptions();
    }

    /// <summary>
    /// Gets the server options being built.
    /// </summary>
    public McpServerOptions Options { get; }

    /// <summary>
    /// Registers one tool handler.
    /// </summary>
    /// <param name="handler">The tool handler.</param>
    /// <returns>The builder.</returns>
    public McpServerBuilder RegisterTool(IMcpToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler.Definition.Name);
        if (_toolHandlers.ContainsKey(handler.Definition.Name))
        {
            throw new InvalidOperationException(
                $"MCP tool '{handler.Definition.Name}' is already registered.");
        }

        _toolHandlers[handler.Definition.Name] = handler;
        return this;
    }

    /// <summary>
    /// Registers one resource provider.
    /// </summary>
    /// <param name="provider">The resource provider.</param>
    /// <returns>The builder.</returns>
    public McpServerBuilder RegisterResourceProvider(IMcpResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _resourceProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// Applies one feature to the builder.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The builder.</returns>
    public McpServerBuilder UseFeature(IMcpFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        _features.Add(feature);
        feature.Register(this);
        return this;
    }

    /// <summary>
    /// Builds the immutable server instance.
    /// </summary>
    /// <returns>The server.</returns>
    public McpServer Build()
    {
        return new McpServer(
            Options,
            _toolHandlers.Values
                .OrderBy(static handler => handler.Definition.Name, StringComparer.Ordinal)
                .ToArray(),
            _resourceProviders.ToArray(),
            _features.ToArray());
    }
}

/// <summary>
/// Immutable in-process MCP server surface.
/// </summary>
public sealed class McpServer
{
    private readonly IMcpFeature[] _features;
    private readonly IMcpResourceProvider[] _resourceProviders;
    private readonly Dictionary<string, IMcpToolHandler> _toolHandlers;
    private readonly McpToolDefinition[] _tools;

    internal McpServer(
        McpServerOptions options,
        IReadOnlyList<IMcpToolHandler> toolHandlers,
        IReadOnlyList<IMcpResourceProvider> resourceProviders,
        IReadOnlyList<IMcpFeature> features)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(toolHandlers);
        ArgumentNullException.ThrowIfNull(resourceProviders);
        ArgumentNullException.ThrowIfNull(features);

        _toolHandlers = new Dictionary<string, IMcpToolHandler>(StringComparer.Ordinal);
        _tools = new McpToolDefinition[toolHandlers.Count];
        for (var index = 0; index < toolHandlers.Count; index++)
        {
            var handler = toolHandlers[index];
            _toolHandlers[handler.Definition.Name] = handler;
            _tools[index] = handler.Definition;
        }

        _resourceProviders = resourceProviders.ToArray();
        _features = features.ToArray();
    }

    /// <summary>
    /// Gets the configured server options.
    /// </summary>
    public McpServerOptions Options { get; }

    /// <summary>
    /// Gets the registered feature set.
    /// </summary>
    public IReadOnlyList<IMcpFeature> Features => _features;

    /// <summary>
    /// Lists all registered tools.
    /// </summary>
    /// <returns>The tool definitions.</returns>
    public IReadOnlyList<McpToolDefinition> ListTools()
    {
        return _tools;
    }

    /// <summary>
    /// Lists all resources exposed by the registered providers.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The resource definitions.</returns>
    public IReadOnlyList<McpResourceDefinition> ListResources(McpRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resources = new List<McpResourceDefinition>();
        for (var index = 0; index < _resourceProviders.Length; index++)
        {
            var providerResources = _resourceProviders[index].ListResources(context);
            for (var resourceIndex = 0; resourceIndex < providerResources.Count; resourceIndex++)
            {
                resources.Add(providerResources[resourceIndex]);
            }
        }

        resources.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Uri, right.Uri));
        return resources;
    }

    /// <summary>
    /// Invokes one tool by name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="arguments">The JSON argument object.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool result.</returns>
    public ValueTask<McpToolCallResult> CallToolAsync(
        string name,
        JsonElement arguments,
        McpRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        if (!_toolHandlers.TryGetValue(name, out var handler))
        {
            throw new KeyNotFoundException($"MCP tool '{name}' is not registered.");
        }

        return handler.InvokeAsync(arguments, context, cancellationToken);
    }

    /// <summary>
    /// Reads one resource by URI.
    /// </summary>
    /// <param name="uri">The resource URI.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resource payload.</returns>
    public async ValueTask<McpResourceReadResult> ReadResourceAsync(
        string uri,
        McpRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(context);

        for (var index = 0; index < _resourceProviders.Length; index++)
        {
            var result = await _resourceProviders[index].TryReadAsync(uri, context, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        throw new KeyNotFoundException($"MCP resource '{uri}' is not registered.");
    }
}
