using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public readonly record struct ProofingDiagnostic(
    int ParagraphIndex,
    int StartOffset,
    int Length,
    string Text,
    string Language,
    ProofingIssueKind Kind,
    string? RuleId = null,
    string? Category = null,
    string? Message = null,
    IReadOnlyList<string>? Suggestions = null);
