namespace Vibe.Office.Collaboration;

/// <summary>
/// Represents ephemeral presence information for a collaborator.
/// </summary>
public sealed record PresenceState(
    Guid UserId,
    string DisplayName,
    TextAnchor? Caret,
    AnchorRange? Selection,
    DateTimeOffset UpdatedAtUtc,
    string? Color = null)
{
    public bool HasSelection => Selection.HasValue && !Selection.Value.IsEmpty;
}
