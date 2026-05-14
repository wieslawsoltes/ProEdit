namespace ProEdit.Collaboration.Server;

public sealed class CollabWebSocketServerOptions
{
    public IReadOnlyList<string> Urls { get; init; } = new[] { "http://127.0.0.1:0" };

    public string WebSocketPath { get; init; } = "/collab";

    public int MaxMessageBytes { get; init; } = 4 * 1024 * 1024;

    public int MaxDecompressedBytes { get; init; } = 16 * 1024 * 1024;

    public TimeSpan PresenceThrottleInterval { get; init; } = TimeSpan.FromMilliseconds(80);

    public TimeSpan MinimumPresenceTimeToLive { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumPresenceTimeToLive { get; init; } = TimeSpan.FromSeconds(30);

    public ICollabServerAuthenticator? Authenticator { get; init; }
}
