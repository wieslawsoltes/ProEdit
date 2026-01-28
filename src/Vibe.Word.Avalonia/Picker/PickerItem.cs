namespace Vibe.Word.Avalonia;

public sealed record PickerItem(
    string Id,
    string Label,
    string? Description = null,
    string? IconKey = null,
    string? GeometryData = null);
