namespace ProEdit.Collaboration;

/// <summary>
/// Represents ephemeral presence information for a collaborator.
/// </summary>
public sealed record PresenceState(
    Guid UserId,
    string DisplayName,
    TextAnchor? Caret,
    AnchorRange? Selection,
    DateTimeOffset UpdatedAtUtc,
    string? Color = null,
    IReadOnlyList<AnchorRange>? SelectionRanges = null,
    IReadOnlyList<TablePresenceRange>? TableSelections = null,
    IReadOnlyList<Guid>? FloatingSelections = null)
{
    public bool HasSelection =>
        (Selection.HasValue && !Selection.Value.IsEmpty)
        || (SelectionRanges?.Count > 0)
        || (TableSelections?.Count > 0)
        || (FloatingSelections?.Count > 0);
}
