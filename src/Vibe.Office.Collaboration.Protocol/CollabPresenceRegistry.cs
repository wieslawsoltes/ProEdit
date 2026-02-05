using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Protocol;

/// <summary>
/// Tracks presence state with TTL-based expiry.
/// </summary>
public sealed class CollabPresenceRegistry
{
    private readonly Dictionary<Guid, PresenceEntry> _entries = new();
    private readonly object _sync = new();

    public CollabPresenceRegistry(TimeSpan? defaultTimeToLive = null)
    {
        DefaultTimeToLive = defaultTimeToLive ?? TimeSpan.FromSeconds(10);
    }

    public TimeSpan DefaultTimeToLive { get; }

    public void Update(PresenceState presence, TimeSpan? timeToLive = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        var nowValue = now ?? DateTimeOffset.UtcNow;
        var ttl = timeToLive ?? DefaultTimeToLive;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var updatedAt = presence.UpdatedAtUtc == default ? nowValue : presence.UpdatedAtUtc;
        var normalized = presence with { UpdatedAtUtc = updatedAt };
        var expiresAt = updatedAt + ttl;

        lock (_sync)
        {
            _entries[presence.UserId] = new PresenceEntry(normalized, expiresAt);
        }
    }

    public IReadOnlyList<PresenceState> GetActive(DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            PruneInternal(nowValue, out _);
            return _entries.Values.Select(entry => entry.Presence).ToList();
        }
    }

    public IReadOnlyList<Guid> Prune(DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            PruneInternal(nowValue, out var removed);
            return removed;
        }
    }

    private void PruneInternal(DateTimeOffset now, out List<Guid> removed)
    {
        removed = new List<Guid>();
        foreach (var (userId, entry) in _entries.ToList())
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _entries.Remove(userId);
                removed.Add(userId);
            }
        }
    }

    private sealed record PresenceEntry(PresenceState Presence, DateTimeOffset ExpiresAtUtc);
}
