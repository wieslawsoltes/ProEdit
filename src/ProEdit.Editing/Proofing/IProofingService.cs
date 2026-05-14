using ProEdit.Documents;

namespace ProEdit.Editing;

public interface IProofingService
{
    event EventHandler<ProofingUpdatedEventArgs>? Updated;

    IReadOnlyList<ProofingDiagnostic> GetParagraphDiagnostics(int paragraphIndex);
    bool TryGetDiagnosticAt(TextPosition position, out ProofingDiagnostic diagnostic);
    IReadOnlyList<string> GetSuggestions(ProofingDiagnostic diagnostic, int maxSuggestions = 5);
    int GetTotalDiagnostics();

    void AddToUserDictionary(string word, string? language = null);
    void IgnoreWord(string word, string? language = null);

    void RefreshAll();
    void RefreshParagraph(int paragraphIndex);
}
