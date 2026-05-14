using ProEdit.Documents;

namespace ProEdit.Editing;

public readonly record struct ProofingSourceDiagnostic(
    int StartOffset,
    int Length,
    string Text,
    string Language,
    ProofingIssueKind Kind,
    string? RuleId = null,
    string? Category = null,
    string? Message = null,
    IReadOnlyList<string>? Suggestions = null);
