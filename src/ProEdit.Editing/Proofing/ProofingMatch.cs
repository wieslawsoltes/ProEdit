using ProEdit.Documents;

namespace ProEdit.Editing;

public readonly record struct ProofingMatch(
    int StartOffset,
    int Length,
    ProofingIssueKind Kind,
    string Text,
    string? RuleId = null,
    string? Category = null,
    string? Message = null,
    IReadOnlyList<string>? Suggestions = null);
