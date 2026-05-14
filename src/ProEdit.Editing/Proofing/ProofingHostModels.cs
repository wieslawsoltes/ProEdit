namespace ProEdit.Editing;

public sealed record ProofingHostRequest(
    string Text,
    string Language,
    ProofingHostCheckOptions Options);

public sealed record ProofingHostCheckOptions(
    bool IncludeSpelling,
    bool IncludeGrammar,
    bool IncludeStyle,
    int? MaxSuggestions = null);

public sealed record ProofingHostResponse(
    IReadOnlyList<ProofingHostMatch> Matches,
    string? Source = null);

public sealed record ProofingHostMatch(
    int Offset,
    int Length,
    string? Message,
    string? RuleId,
    string? IssueType,
    string? Category,
    IReadOnlyList<string>? Replacements);
