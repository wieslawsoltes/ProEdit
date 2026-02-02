namespace Vibe.Office.Editing;

public interface IProofingToggleService
{
    bool IsEnabled { get; }
    bool IsSpellingEnabled { get; }
    bool IsGrammarEnabled { get; }
    bool IsStyleEnabled { get; }
    void SetEnabled(bool enabled);
    void SetSpellingEnabled(bool enabled);
    void SetGrammarEnabled(bool enabled);
    void SetStyleEnabled(bool enabled);
}
