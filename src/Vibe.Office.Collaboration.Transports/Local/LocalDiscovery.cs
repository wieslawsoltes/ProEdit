namespace Vibe.Office.Collaboration.Transports.Local;

public sealed record LocalCollabEndpoint(string Name, string Host, int Port);

public interface ILocalCollaborationDiscovery
{
    ValueTask<IReadOnlyList<LocalCollabEndpoint>> DiscoverAsync(CancellationToken cancellationToken = default);
}

public sealed class ManualLocalCollaborationDiscovery : ILocalCollaborationDiscovery
{
    private readonly IReadOnlyList<LocalCollabEndpoint> _endpoints;

    public ManualLocalCollaborationDiscovery(IEnumerable<LocalCollabEndpoint> endpoints)
    {
        _endpoints = endpoints?.ToArray() ?? throw new ArgumentNullException(nameof(endpoints));
    }

    public ValueTask<IReadOnlyList<LocalCollabEndpoint>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_endpoints);
    }
}
