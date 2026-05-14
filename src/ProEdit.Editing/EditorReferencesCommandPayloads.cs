namespace ProEdit.Editing;

public readonly record struct EditorTocInsertRequest(
    int MaxLevel = 3,
    bool UseHyperlinks = true,
    bool ShowPageNumbers = true);

public readonly record struct EditorCaptionInsertRequest(
    string? Label,
    string? Title);
