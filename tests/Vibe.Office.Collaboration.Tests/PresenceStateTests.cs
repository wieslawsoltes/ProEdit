using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Protocol;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class PresenceStateTests
{
    [Fact]
    public void TablePresenceRange_NormalizeOrdersIndices()
    {
        var tableId = Guid.NewGuid();
        var range = new TablePresenceRange(tableId, 3, 1, 4, 2).Normalize();

        Assert.Equal(tableId, range.TableId);
        Assert.Equal(1, range.RowStart);
        Assert.Equal(3, range.RowEnd);
        Assert.Equal(2, range.ColumnStart);
        Assert.Equal(4, range.ColumnEnd);
    }

    [Fact]
    public void PresenceState_HasSelectionTracksExtendedSelections()
    {
        var presence = new PresenceState(
            Guid.NewGuid(),
            "Remote",
            null,
            null,
            DateTimeOffset.UtcNow,
            "#00FF00",
            SelectionRanges: null,
            TableSelections: new[] { new TablePresenceRange(Guid.NewGuid(), 0, 0, 0, 0) },
            FloatingSelections: new[] { Guid.NewGuid() });

        Assert.True(presence.HasSelection);
    }

    [Fact]
    public void PresenceState_RoundTripsExtendedSelections()
    {
        var paragraphId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var selectionRanges = new[]
        {
            new AnchorRange(TextAnchor.Before(paragraphId, 0), TextAnchor.Before(paragraphId, 3)),
            new AnchorRange(TextAnchor.Before(paragraphId, 5), TextAnchor.Before(paragraphId, 7))
        };
        var tableSelections = new[]
        {
            new TablePresenceRange(tableId, 0, 1, 2, 3)
        };
        var floatingSelections = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var presence = new PresenceState(
            Guid.NewGuid(),
            "Remote",
            TextAnchor.Before(paragraphId, 1),
            new AnchorRange(TextAnchor.Before(paragraphId, 1), TextAnchor.Before(paragraphId, 2)),
            DateTimeOffset.UtcNow,
            "#112233",
            selectionRanges,
            tableSelections,
            floatingSelections);

        var payload = CollabProtocolJsonCodec.SerializePayload(new PresenceMessage(presence, TimeSpan.FromSeconds(5)));
        var result = CollabProtocolJsonCodec.DeserializePayload<PresenceMessage>(payload);

        Assert.Equal(presence.SelectionRanges?.Count, result.Presence.SelectionRanges?.Count);
        Assert.Equal(presence.TableSelections?.Count, result.Presence.TableSelections?.Count);
        Assert.Equal(presence.FloatingSelections?.Count, result.Presence.FloatingSelections?.Count);
        Assert.Equal(tableId, result.Presence.TableSelections![0].TableId);
    }
}
