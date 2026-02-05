using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Protocol;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class PresenceRegistryTests
{
    [Fact]
    public void PrunesExpiredPresence()
    {
        var registry = new CollabPresenceRegistry(TimeSpan.FromSeconds(1));
        var now = DateTimeOffset.UtcNow;
        var presence = new PresenceState(Guid.NewGuid(), "User", null, null, now, "#00FF00");

        registry.Update(presence, TimeSpan.FromMilliseconds(100), now);
        Assert.Single(registry.GetActive(now));

        var removed = registry.Prune(now.AddSeconds(2));
        Assert.Single(removed);
        Assert.Empty(registry.GetActive(now.AddSeconds(2)));
    }
}
