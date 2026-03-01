namespace Vibe.Office.WinUICompat.Text;

public readonly record struct ProofingDiagnostic(
    ProofingIssueKind Kind,
    int ParagraphIndex,
    int StartOffset,
    int Length,
    string Message);
