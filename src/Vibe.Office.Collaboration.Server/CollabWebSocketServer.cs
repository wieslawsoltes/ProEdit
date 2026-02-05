using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vibe.Office.Collaboration.Protocol;

namespace Vibe.Office.Collaboration.Server;

public sealed class CollabWebSocketServer : IAsyncDisposable
{
    private readonly CollabWebSocketServerOptions _options;
    private readonly CollabRelayHub _hub;
    private WebApplication? _app;
    private bool _started;

    public CollabWebSocketServer(CollabWebSocketServerOptions? options = null)
    {
        _options = options ?? new CollabWebSocketServerOptions();
        _hub = new CollabRelayHub(_options);
    }

    public Uri? WebSocketEndpoint { get; private set; }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
            serverOptions.Limits.MaxRequestBodySize = _options.MaxMessageBytes;
        });
        builder.WebHost.UseUrls(_options.Urls.ToArray());

        builder.Services.AddSingleton(_hub);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });

        app.Map(_options.WebSocketPath, async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            await _hub.HandleConnectionAsync(context, socket, context.RequestAborted);
        });

        await app.StartAsync(cancellationToken);
        _app = app;
        _started = true;
        WebSocketEndpoint = ResolveWebSocketEndpoint(app, _options.WebSocketPath);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            return;
        }

        await _hub.DisposeAsync();
        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
        _started = false;
    }

    private static Uri? ResolveWebSocketEndpoint(WebApplication app, string path)
    {
        var server = app.Services.GetService<IServer>();
        var feature = server?.Features.Get<IServerAddressesFeature>();
        var address = feature?.Addresses.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var builder = new UriBuilder(address)
        {
            Scheme = address.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = path
        };
        return builder.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
